using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game.Core.Ring;
using Game.Core.Ring.Match;
using Game.Gameplay.Combat;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Таблиця дій (TRPG_DESIGN §4.6), ускладнення 7–9, реакції (§4.7), захисні кидки, перки, постановка (§4.4, §4.12) і QTE як видовище.
    /// З 08.11.2026 зон рингу немає: усе в центрі, прийом із канатів чи кута — лише постановка для показу.
    /// </summary>
    public class MatchActionsTests
    {
        private static UnavailableReason Why(MatchEngine e, MatchActionId id) { return e.Describe(id).Reason; }

        private static void Lay(Fighter f)
        {
            f.Posture = Posture.Down; f.Damage = Math.Max(f.Damage, MatchRules.PinMinDamage);
        }

        // ---------- умови доступності ----------

        [Test]
        public void Strike_NeedsUprightTarget()
        {
            var e = MatchKit.Engine(true);
            Assert.IsTrue(e.Describe(MatchActionId.Strike).Available);
            Lay(e.State.Rival);
            Assert.AreEqual(UnavailableReason.TargetNotStanding, Why(e, MatchActionId.Strike));
            e.State.Rival.Posture = Posture.Stunned;
            Assert.IsTrue(e.Describe(MatchActionId.Strike).Available, "приголомшеного бити можна");
        }

        [Test]
        public void LyingActor_CanOnlyGetUpRecoverBreath()
        {
            var e = MatchKit.Engine(true);
            Lay(e.State.Player);
            e.State.Player.Damage = 3;
            foreach (var o in e.GetAvailableActions())
            {
                bool allowed = o.Id == MatchActionId.GetUp || o.Id == MatchActionId.Recover || o.Id == MatchActionId.CatchBreath;
                Assert.AreEqual(allowed, o.Available, o.Id + " " + o.Reason);
            }
            Assert.AreEqual(UnavailableReason.ActorDown, Why(e, MatchActionId.Strike));
        }

        [Test]
        public void Pin_NeedsLyingTarget()
        {
            var e = MatchKit.Engine(true);
            Assert.AreEqual(UnavailableReason.TargetNotDown, Why(e, MatchActionId.Pin), "на стоячому недоступне");
            Lay(e.State.Rival);
            Assert.IsTrue(e.Describe(MatchActionId.Pin).Available);
        }

        [Test]
        public void Pin_AfterKickoutAtTwo_NeedsAnotherMoveFirst()
        {
            // «два»: ініціатива лишається, але накрити знову можна лише після іншого прийому (TRPG_DESIGN §4.6)
            var e = MatchKit.Engine(true, new[] { 6 });
            Lay(e.State.Rival);
            e.Perform(MatchActionId.Pin);
            Assert.AreEqual(MatchSide.Player, e.CurrentActor);
            Assert.AreEqual(UnavailableReason.JustPinned, Why(e, MatchActionId.Pin));
            MatchKit.Play(e, MatchActionId.Submission);
            Assert.IsFalse(e.IsOver);
            Assert.AreEqual(MatchSide.Player, e.CurrentActor, "больовий 7–9, «Терпіти» 7–9 — тримається");
            Assert.IsTrue(e.Describe(MatchActionId.Pin).Available, "після іншого прийому — знову можна");
        }

        [Test]
        public void PinAndSubmission_NeedAccumulatedDamage_TargetNotHurtBelowThreshold()
        {
            var e = MatchKit.Engine(true);
            e.State.Rival.Posture = Posture.Down;
            e.State.Rival.Damage = MatchRules.PinMinDamage - 1;
            Assert.AreEqual(4, MatchRules.PinMinDamage);
            Assert.AreEqual(UnavailableReason.TargetNotHurt, Why(e, MatchActionId.Pin));
            Assert.AreEqual(UnavailableReason.TargetNotHurt, Why(e, MatchActionId.Submission));
            e.State.Rival.Damage = MatchRules.PinMinDamage;
            Assert.IsTrue(e.Describe(MatchActionId.Pin).Available);
            Assert.IsTrue(e.Describe(MatchActionId.Submission).Available);
        }

        [Test]
        public void Submission_NeedsLyingTarget()
        {
            var e = MatchKit.Engine(true);
            Assert.AreEqual(UnavailableReason.TargetNotDown, Why(e, MatchActionId.Submission));
            Lay(e.State.Rival);
            Assert.IsTrue(e.Describe(MatchActionId.Submission).Available);
        }

        [Test]
        public void Throw_NeedsStandingTarget()
        {
            var e = MatchKit.Engine(true);
            Assert.IsTrue(e.Describe(MatchActionId.Throw).Available);
            Lay(e.State.Rival);
            Assert.AreEqual(UnavailableReason.TargetNotStanding, Why(e, MatchActionId.Throw));
        }

        [Test]
        public void TopRope_NoCornerNeeded_ButFullStamina()
        {
            var e = MatchKit.Engine(true);
            Assert.IsTrue(e.Describe(MatchActionId.TopRope).Available, "кута для ризикованого трюку більше не треба");
            Assert.AreEqual(RingAttr.Agility, e.Describe(MatchActionId.TopRope).Attr);
            e.State.Player.Stamina = MatchRules.Cost(MatchActionId.TopRope) - 1;
            Assert.AreEqual(UnavailableReason.NotEnoughStamina, Why(e, MatchActionId.TopRope));
        }

        [Test]
        public void Finisher_NeedsMomentumHypeHurtTargetAndStamina()
        {
            var e = MatchKit.Engine(true);
            Assert.AreEqual(UnavailableReason.NeedImpulse, Why(e, MatchActionId.Finisher));
            e.State.Player.Momentum = 5;
            e.State.Hype = 3;
            Assert.AreEqual(UnavailableReason.NeedHype, Why(e, MatchActionId.Finisher));
            e.State.Hype = 4;
            Assert.AreEqual(UnavailableReason.TargetNotHurt, Why(e, MatchActionId.Finisher));
            e.State.Rival.Damage = 6;
            Assert.IsTrue(e.Describe(MatchActionId.Finisher).Available);
            e.State.Player.Stamina = MatchRules.Cost(MatchActionId.Finisher) - 1;
            Assert.AreEqual(UnavailableReason.NotEnoughStamina, Why(e, MatchActionId.Finisher));
        }

        [Test]
        public void Taunt_NeedsLyingOrStunnedTarget()
        {
            var e = MatchKit.Engine(true);
            Assert.AreEqual(UnavailableReason.TargetNotDownOrStunned, Why(e, MatchActionId.Taunt));
            Lay(e.State.Rival);
            Assert.IsTrue(e.Describe(MatchActionId.Taunt).Available);
            e.State.Rival.Posture = Posture.Stunned;
            Assert.IsTrue(e.Describe(MatchActionId.Taunt).Available);
        }

        [Test]
        public void Desperate_LowMomentum_HurtWhole_OncePerMatch()
        {
            var e = MatchKit.Engine(true, new[] { 11 });
            Assert.AreEqual(UnavailableReason.NotExhausted, Why(e, MatchActionId.Desperate), "імпульс 0 — лишається ушкодження");
            e.State.Player.Damage = 6;
            Assert.IsTrue(e.Describe(MatchActionId.Desperate).Available);
            e.State.Player.Momentum = 3;
            Assert.AreEqual(UnavailableReason.ImpulseTooHigh, Why(e, MatchActionId.Desperate));
            e.State.Player.Momentum = 0;
            MatchKit.Play(e, MatchActionId.Desperate);
            Assert.AreEqual(3, e.State.Player.Momentum, "10+: імпульс +3");
            Assert.AreEqual(3, e.State.Rival.Damage);
            Assert.AreEqual(Posture.Down, e.State.Rival.Posture);
            e.State.Player.Momentum = 0;
            Assert.AreEqual(UnavailableReason.AlreadyUsed, Why(e, MatchActionId.Desperate));
        }

        [Test]
        public void GetUp_Strong_KeepsInitiative_Partial_StandsButPasses_Miss_StaysDown()
        {
            var e = MatchKit.Engine(true, new[] { 11 });
            Assert.AreEqual(UnavailableReason.NotLying, Why(e, MatchActionId.GetUp));
            Lay(e.State.Player);
            MatchKit.Play(e, MatchActionId.GetUp);
            Assert.AreEqual(Posture.Standing, e.State.Player.Posture);
            Assert.AreEqual(MatchSide.Player, e.CurrentActor, "10+: встав, ініціатива твоя");

            var p = MatchKit.Engine(true, new[] { 8 });
            Lay(p.State.Player);
            MatchKit.Play(p, MatchActionId.GetUp);
            Assert.AreEqual(Posture.Standing, p.State.Player.Posture);
            Assert.AreEqual(MatchSide.Rival, p.CurrentActor, "7–9: встав, ініціатива в нього");

            var m = MatchKit.Engine(true, new[] { 4 });
            Lay(m.State.Player);
            MatchKit.Play(m, MatchActionId.GetUp);
            Assert.AreEqual(Posture.Down, m.State.Player.Posture);
            Assert.AreEqual(MatchSide.Rival, m.CurrentActor);
        }

        [Test]
        public void Recover_LowersDamageAndHype_PassesInitiative()
        {
            var e = MatchKit.Engine(true);
            Assert.AreEqual(UnavailableReason.NothingToRecover, Why(e, MatchActionId.Recover));
            e.State.Player.Damage = 4;
            MatchKit.Play(e, MatchActionId.Recover);
            Assert.AreEqual(3, e.State.Player.Damage);
            Assert.AreEqual(2, e.State.Hype);
            Assert.AreEqual(MatchSide.Rival, e.CurrentActor);
        }

        // ---------- результати дій і ускладнення 7–9 ----------

        [Test]
        public void Strike_Tiers()
        {
            var e = MatchKit.Engine(true, new[] { 11, 8, 4 });
            MatchKit.Play(e, MatchActionId.Strike);
            Assert.AreEqual(2, e.State.Rival.Damage);
            Assert.AreEqual(4, e.State.Hype, "10+: ажіотаж +1");
            Assert.AreEqual(1, e.State.Player.Momentum, "чистий успіх: імпульс +1");
            MatchKit.Play(e, MatchActionId.Strike);
            Assert.AreEqual(3, e.State.Rival.Damage);
            MatchKit.Play(e, MatchActionId.Strike);
            Assert.AreEqual(1, e.State.Player.Damage, "провал: контрудар");
            Assert.AreEqual(MatchSide.Rival, e.CurrentActor);
        }

        [Test]
        public void Strike_Partial_OnEmptyStamina_DoesNotGoBelowZero()
        {
            var e = MatchKit.Engine(true, new[] { 9 });
            e.State.Player.Stamina = 0;
            MatchKit.Play(e, MatchActionId.Strike);
            Assert.AreEqual(0, e.State.Player.Stamina);
            Assert.AreEqual(MatchSide.Player, e.CurrentActor);
        }

        [Test]
        public void Strike_StrongOnExhaustedTarget_Stuns()
        {
            var e = MatchKit.Engine(true, new[] { 11 });
            e.State.Rival.Damage = 5;
            MatchKit.Play(e, MatchActionId.Strike);
            Assert.AreEqual(Posture.Stunned, e.State.Rival.Posture);
        }

        [Test]
        public void Throw_Strong_PutsTargetDown()
        {
            var e = MatchKit.Engine(true, new[] { 11 });
            MatchKit.Play(e, MatchActionId.Throw);
            Assert.AreEqual(3, e.State.Rival.Damage);
            Assert.AreEqual(Posture.Down, e.State.Rival.Posture);
            Assert.AreEqual(4, e.State.Hype);
            Assert.AreEqual(1, e.State.Rival.Momentum, "суперник продав прийом");
            Assert.AreEqual(1, e.State.Player.Momentum);
            Assert.AreEqual(MatchSide.Player, e.CurrentActor);
        }

        [Test]
        public void Throw_Partial_CostsSelfDamage_Miss_PutsAttackerDown()
        {
            var e = MatchKit.Engine(true, new[] { 8 });
            MatchKit.Play(e, MatchActionId.Throw);
            Assert.AreEqual(2, e.State.Rival.Damage);
            Assert.AreEqual(1, e.State.Player.Damage, "ускладнення: влучив, але ушкодив себе");
            Assert.AreEqual(MatchSide.Player, e.CurrentActor);
            var m = MatchKit.Engine(true, new[] { 3 });
            MatchKit.Play(m, MatchActionId.Throw);
            Assert.AreEqual(Posture.Down, m.State.Player.Posture, "він вивернувся: ти лежиш");
            Assert.AreEqual(MatchSide.Rival, m.CurrentActor);
        }

        [Test]
        public void TopRope_Tiers()
        {
            var e = MatchKit.Engine(true, new[] { 11 });
            MatchKit.Play(e, MatchActionId.TopRope);
            Assert.AreEqual(4, e.State.Rival.Damage);
            Assert.AreEqual(Posture.Down, e.State.Rival.Posture);
            Assert.AreEqual(5, e.State.Hype);
            var p = MatchKit.Engine(true, new[] { 8 });
            MatchKit.Play(p, MatchActionId.TopRope);
            Assert.AreEqual(2, p.State.Rival.Damage);
            Assert.AreEqual(1, p.State.Player.Damage, "ускладнення: приземлився невдало");
            var m = MatchKit.Engine(true, new[] { 3 });
            MatchKit.Play(m, MatchActionId.TopRope);
            Assert.AreEqual(2, m.State.Player.Damage);
            Assert.AreEqual(Posture.Down, m.State.Player.Posture);
            Assert.AreEqual(MatchSide.Rival, m.CurrentActor);
        }

        [Test]
        public void Desperate_Partial_CostsSelfDamage()
        {
            var e = MatchKit.Engine(true, new[] { 8 });
            e.State.Player.Damage = 6;
            MatchKit.Play(e, MatchActionId.Desperate);
            Assert.AreEqual(2, e.State.Rival.Damage);
            Assert.AreEqual(7, e.State.Player.Damage);
        }

        [Test]
        public void Taunt_Tiers()
        {
            var e = MatchKit.Engine(true, new[] { 11 });
            Lay(e.State.Rival);
            MatchKit.Play(e, MatchActionId.Taunt);
            Assert.AreEqual(5, e.State.Hype);
            Assert.AreEqual(1, e.State.Player.Momentum);
            var m = MatchKit.Engine(true, new[] { 8 });
            Lay(m.State.Rival);
            MatchKit.Play(m, MatchActionId.Taunt);
            Assert.AreEqual(4, m.State.Hype);
            Assert.AreEqual(Posture.Standing, m.State.Rival.Posture, "ускладнення: він встає");
        }

        [Test]
        public void RepeatingTheSameMoveInAChain_CostsHype()
        {
            var e = MatchKit.Engine(true);
            MatchKit.Play(e, MatchActionId.Strike);
            Assert.AreEqual(3, e.State.Hype);
            MatchKit.Play(e, MatchActionId.Strike);
            Assert.AreEqual(2, e.State.Hype);
            e.EndTurn(); e.EndTurn();
            MatchKit.Play(e, MatchActionId.Strike);
            Assert.AreEqual(2, e.State.Hype, "новий ланцюг — повтору немає");
        }

        [Test]
        public void SameAction_DifferentMoves_IsNotARepeat()
        {
            var e = MatchKit.Engine(true);
            e.Perform(MatchActionId.Strike, new MoveHint { Key = "forearm" }); e.Commit(0);
            e.Perform(MatchActionId.Strike, new MoveHint { Key = "low_kick" }); e.Commit(0);
            Assert.AreEqual(3, e.State.Hype);
        }

        // ---------- постановка (усе в центрі) ----------

        [Test]
        public void Staging_TopRopeFromCorner_OthersCenter_HintOverrides()
        {
            var e = MatchKit.Engine(true, new[] { 11, 11, 11 });
            MatchKit.Play(e, MatchActionId.Strike);
            var a = e.State.Log.Last(x => x.Kind == MatchEventKind.Action);
            Assert.AreEqual(MoveStaging.Center, a.Staging);
            Assert.IsNull(a.MoveKey);
            e.Perform(MatchActionId.Strike, new MoveHint { Key = "whip", Staging = MoveStaging.Ropes }); e.Commit(0);
            a = e.State.Log.Last(x => x.Kind == MatchEventKind.Action);
            Assert.AreEqual(MoveStaging.Ropes, a.Staging);
            Assert.AreEqual("whip", a.MoveKey);
            Assert.AreEqual(2, a.ChainIndex);
            e.Perform(MatchActionId.TopRope); e.React(ReactionChoice.Sell); e.Commit(0);
            var sell = e.State.Log.Last(x => x.Kind == MatchEventKind.Sell);
            a = e.State.Log.Last(x => x.Kind == MatchEventKind.Action);
            Assert.AreEqual(MoveStaging.Corner, a.Staging, "ризикований трюк без підказки — склейкою з кута");
            Assert.AreEqual(MoveStaging.Corner, sell.Staging, "продаж — та сама постановка");
        }

        // ---------- брудний і ролі ----------

        [Test]
        public void Dirty_Partial_WarnsAttacker_FaceLosesHype()
        {
            var e = MatchKit.Engine(true, new[] { 8 });
            MatchKit.Play(e, MatchActionId.Dirty);
            Assert.AreEqual(2, e.State.Rival.Damage);
            Assert.AreEqual(1, e.State.Player.Warnings);
            Assert.AreEqual(1, e.State.Hype, "герой: -2");
            Assert.AreEqual(1, e.State.PlayerDirtyMoves);
        }

        [Test]
        public void Dirty_Strong_NoWarning_HeelGainsHype()
        {
            var e = MatchKit.Engine(true, new[] { 11 }, s => s.PlayerRole = RingPath.Heel);
            MatchKit.Play(e, MatchActionId.Dirty);
            Assert.AreEqual(0, e.State.Player.Warnings);
            Assert.AreEqual(4, e.State.Hype, "лиходій: +1 без попередження");
            Assert.AreEqual(1, e.State.Player.Momentum);
        }

        [Test]
        public void Dirty_Partial_HeelWarned_NoHypeBonus()
        {
            var e = MatchKit.Engine(true, new[] { 8 }, s => s.PlayerRole = RingPath.Heel);
            MatchKit.Play(e, MatchActionId.Dirty);
            Assert.AreEqual(1, e.State.Player.Warnings);
            Assert.AreEqual(3, e.State.Hype);
        }

        [Test]
        public void Dirty_Miss_WarnsAndCostsHype_PassesInitiative()
        {
            var e = MatchKit.Engine(true, new[] { 3 }, s => s.PlayerRole = RingPath.Rebel);
            MatchKit.Play(e, MatchActionId.Dirty);
            Assert.AreEqual(1, e.State.Player.Warnings);
            Assert.AreEqual(2, e.State.Hype);
            Assert.AreEqual(MatchSide.Rival, e.CurrentActor);
        }

        [Test]
        public void Perk_DirtyNoWarningOnPartial()
        {
            var e = MatchKit.Engine(true, new[] { 8 }, s => s.PlayerMods.DirtyNoWarningOnPartial = true);
            MatchKit.Play(e, MatchActionId.Dirty);
            Assert.AreEqual(0, e.State.Player.Warnings);
        }

        [Test]
        public void DistractedReferee_SeesNothing_UntilInitiativePasses()
        {
            var e = MatchKit.Engine(true, new[] { 8 }, s => s.PlayerHasAlly = true);
            MatchKit.Play(e, MatchActionId.Ally);
            Assert.IsTrue(e.State.RefereeDistracted);
            MatchKit.Play(e, MatchActionId.Dirty);
            Assert.AreEqual(0, e.State.Player.Warnings);
            Assert.IsTrue(e.State.RefereeDistracted, "ініціатива ще в тебе");
            e.EndTurn();
            Assert.IsFalse(e.State.RefereeDistracted);
        }

        // ---------- реакція на великий прийом ----------

        [Test]
        public void BigMoves_AskForReaction_OthersDoNot()
        {
            var e = MatchKit.Engine(true);
            e.Perform(MatchActionId.Throw);
            Assert.AreEqual(MatchPhase.AwaitReaction, e.Phase);
            Assert.AreEqual(MatchSide.Rival, e.PendingReaction.Defender);
            CollectionAssert.AreEqual(TwoD6.ChancesOf36(0), e.PendingReaction.ReverseChances);
            var s = MatchKit.Engine(true);
            s.Perform(MatchActionId.Strike);
            Assert.AreEqual(MatchPhase.AwaitBoost, s.Phase);
            var t = MatchKit.Engine(true);
            t.Perform(MatchActionId.TopRope);
            Assert.AreEqual(MatchPhase.AwaitReaction, t.Phase);
            var f = MatchKit.Engine(true);
            f.State.Player.Momentum = 5; f.State.Hype = 4; f.State.Rival.Damage = 6;
            f.Perform(MatchActionId.Finisher);
            Assert.AreEqual(MatchPhase.AwaitReaction, f.Phase);
            Assert.AreEqual(-3, f.PendingReaction.ReverseModifier, "проти фінішера -2 і -1 за ушкодження 6");
        }

        [Test]
        public void NoReversal_AgainstPin()
        {
            var e = MatchKit.Engine(true, new[] { 12 });
            Lay(e.State.Rival);
            e.Perform(MatchActionId.Pin);
            Assert.IsNull(e.PendingReaction);
        }

        [Test]
        public void Sell_GivesMomentum_HeroGivesHype_CountsForResult()
        {
            var e = MatchKit.Engine(false, new[] { 8 });
            e.State.Player.Momentum = 0;
            e.Perform(MatchActionId.Throw);
            e.React(ReactionChoice.Sell);
            Assert.AreEqual(1, e.State.Player.Momentum);
            Assert.AreEqual(4, e.State.Hype, "герой продав: ажіотаж +1");
            Assert.AreEqual(1, e.State.PlayerSells);
        }

        [Test]
        public void Reverse_Strong_AttackerFalls_InitiativeToDefender()
        {
            var e = MatchKit.Engine(true, new[] { 11 });
            e.Perform(MatchActionId.Throw);
            e.React(ReactionChoice.Reverse);
            Assert.AreEqual(Posture.Down, e.State.Player.Posture);
            Assert.AreEqual(MatchSide.Rival, e.CurrentActor);
            Assert.AreEqual(1, e.State.Rival.ReversalTokens);
            Assert.AreEqual(0, e.State.Rival.Damage);
        }

        [Test]
        public void Reverse_Partial_MoveBroken_NewExchange()
        {
            // 7–9: прийом зірвано, ніхто не має переваги — знову Обмін (§4.7)
            var e = MatchKit.Engine(true, new[] { 8, 2, 12 });
            e.Perform(MatchActionId.Throw);
            var evs = e.React(ReactionChoice.Reverse);
            Assert.IsTrue(evs.Any(x => x.Kind == MatchEventKind.Exchange));
            Assert.AreEqual(Posture.Standing, e.State.Player.Posture);
            Assert.AreEqual(MatchSide.Rival, e.CurrentActor, "Обмін 2:12 — ініціатива суперника");
            Assert.AreEqual(0, e.State.Rival.Damage);

            var k = MatchKit.Engine(true, new[] { 8, 12, 2 });
            k.Perform(MatchActionId.Throw);
            k.React(ReactionChoice.Reverse);
            Assert.AreEqual(MatchSide.Player, k.CurrentActor, "Обмін 12:2 — ініціатива лишилась");
            Assert.AreEqual(0, k.Chain, "ланцюг почався наново");
        }

        [Test]
        public void Reverse_Miss_MoveLandsWithExtraDamage()
        {
            var e = MatchKit.Engine(true, new[] { 3, 11 });
            e.Perform(MatchActionId.Throw);
            e.React(ReactionChoice.Reverse);
            Assert.AreEqual(MatchPhase.AwaitBoost, e.Phase);
            e.Commit(0);
            Assert.AreEqual(4, e.State.Rival.Damage, "3 + 1");
            Assert.AreEqual(1, e.State.Rival.ReversalTokens);
        }

        [Test]
        public void NoTokens_OrStunned_AutoSells()
        {
            var e = MatchKit.Engine(true);
            e.State.Rival.ReversalTokens = 0;
            e.Perform(MatchActionId.Throw);
            Assert.AreEqual(MatchPhase.AwaitBoost, e.Phase);
            Assert.IsTrue(e.State.Log.Any(x => x.Kind == MatchEventKind.Sell));
            var s = MatchKit.Engine(true);
            s.State.Rival.Posture = Posture.Stunned;
            s.Perform(MatchActionId.Throw);
            Assert.AreEqual(MatchPhase.AwaitBoost, s.Phase, "приголомшений не може реверсувати");
        }

        [Test]
        public void Perk_ExtraReversalTokens()
        {
            var e = MatchKit.Engine(true, null, s => s.RivalMods.ExtraReversalTokens = 1);
            Assert.AreEqual(3, e.State.Rival.ReversalTokens);
            Assert.AreEqual(2, e.State.Player.ReversalTokens);
        }

        // ---------- захисні кидки ----------

        private static MatchEngine PinReady(System.Action<MatchSetup> tweak = null, int[] rolls = null)
        {
            var e = MatchKit.Engine(true, rolls, tweak);
            Lay(e.State.Rival);
            return e;
        }

        [Test]
        public void Kickout_Escalates_WithEveryKickoutDone()
        {
            var e = PinReady();
            Assert.AreEqual(MatchRules.DefenseBase, e.Describe(MatchActionId.Pin).Modifier);
            e.State.Rival.KickoutsDone = 1;
            Assert.AreEqual(MatchRules.DefenseBase - 1, e.Describe(MatchActionId.Pin).Modifier);
            e.State.Rival.KickoutsDone = 3;
            Assert.AreEqual(MatchRules.DefenseBase - 3, e.Describe(MatchActionId.Pin).Modifier);
        }

        [Test]
        public void Kickout_DamageThresholds()
        {
            var e = PinReady();
            e.State.Rival.Damage = 5; Assert.AreEqual(MatchRules.DefenseBase, e.Describe(MatchActionId.Pin).Modifier);
            e.State.Rival.Damage = 6; Assert.AreEqual(MatchRules.DefenseBase - 1, e.Describe(MatchActionId.Pin).Modifier);
            e.State.Rival.Damage = 10; Assert.AreEqual(MatchRules.DefenseBase - 2, e.Describe(MatchActionId.Pin).Modifier);
        }

        [Test]
        public void Perk_DamageThresholdShift_MovesBothThresholds()
        {
            var e = PinReady(s => s.RivalMods.DamageThresholdShift = 2);
            e.State.Rival.Damage = 6; Assert.AreEqual(MatchRules.DefenseBase, e.Describe(MatchActionId.Pin).Modifier);
            e.State.Rival.Damage = 8; Assert.AreEqual(MatchRules.DefenseBase - 1, e.Describe(MatchActionId.Pin).Modifier);
            e.State.Rival.Damage = 12; Assert.AreEqual(MatchRules.DefenseBase - 2, e.Describe(MatchActionId.Pin).Modifier);
            e.State.Rival.Damage = 10; Assert.AreEqual(MatchRules.DefenseBase - 1, e.Describe(MatchActionId.Pin).Modifier);
        }

        [Test]
        public void Perk_KickOutBonus_AndStamina()
        {
            var e = PinReady(s => { s.RivalMods.KickOutBonus = 1; s.Rival.Set(RingAttr.Stamina, 2); });
            Assert.AreEqual(MatchRules.DefenseBase + 3, e.Describe(MatchActionId.Pin).Modifier);
        }

        [Test]
        public void LateStage_PenalisesDefence()
        {
            var e = PinReady();
            e.State.Round = MatchRules.LateStageRound - 1; Assert.AreEqual(MatchRules.DefenseBase, e.Describe(MatchActionId.Pin).Modifier);
            e.State.Round = MatchRules.LateStageRound; Assert.AreEqual(MatchRules.DefenseBase - 1, e.Describe(MatchActionId.Pin).Modifier);
        }

        [Test]
        public void Perk_RiskyMoveBonus_TopRopeAndDesperate()
        {
            var e = MatchKit.Engine(true, null, s => { s.PlayerMods.RiskyMoveBonus = 1; s.Player.Set(RingAttr.Agility, 1); });
            Assert.AreEqual(2, e.Describe(MatchActionId.TopRope).Modifier);
            Assert.AreEqual(0, e.Describe(MatchActionId.Strike).Modifier);
            Assert.AreEqual(1, e.Describe(MatchActionId.Desperate).Modifier);
        }

        [Test]
        public void Kickout_AtOne_InitiativeToDefender_AtTwo_StaysWithAttacker()
        {
            // «раз»: 10+ — ініціатива захиснику; «два»: 7–9 — ажіотаж +2, імпульс +1 тому, хто вирвався, ініціатива лишається
            var one = PinReady(null, new[] { 11 });
            one.Perform(MatchActionId.Pin);
            Assert.AreEqual(1, one.State.Rival.KickoutsDone);
            Assert.AreEqual(3, one.State.Hype);
            Assert.AreEqual(MatchSide.Rival, one.CurrentActor);
            var two = PinReady(null, new[] { 6 });
            two.Perform(MatchActionId.Pin);
            Assert.AreEqual(5, two.State.Hype);
            Assert.AreEqual(1, two.State.Rival.Momentum);
            Assert.IsFalse(two.IsOver);
            Assert.AreEqual(MatchSide.Player, two.CurrentActor);
        }

        [Test]
        public void Finisher_SpendsMomentumAndStamina_AutoPinsWithPenalty()
        {
            var e = MatchKit.Engine(true, new[] { 11, 12 });
            e.State.Player.Momentum = MatchRules.FinisherMomentum; e.State.Hype = 4; e.State.Rival.Damage = 6;
            e.Perform(MatchActionId.Finisher);
            Assert.AreEqual(0, e.State.Player.Momentum);
            Assert.AreEqual(MatchRules.StartStamina - MatchRules.Cost(MatchActionId.Finisher), e.State.Player.Stamina);
            e.React(ReactionChoice.Sell);
            e.Commit(0);
            var kick = e.State.Log.Last(x => x.Kind == MatchEventKind.Kickout);
            Assert.AreEqual(10, e.State.Rival.Damage);
            Assert.AreEqual(MatchRules.DefenseBase - 5, kick.Modifier, "-2 поріг на межі, -3 фінішер");
            Assert.AreEqual(Posture.Down, e.State.Rival.Posture);
            Assert.AreEqual(UnavailableReason.JustPinned, Why(e, MatchActionId.Pin), "фінішер закінчується утриманням");
        }

        [Test]
        public void Endure_Partial_Holds()
        {
            // больовий 7–9 (штраф -1), «Терпіти» 8-1 = 7 → тримається, ініціатива в нападника
            var e = MatchKit.Engine(true, new[] { 8, 8 });
            Lay(e.State.Rival);
            MatchKit.Play(e, MatchActionId.Submission);
            Assert.IsFalse(e.IsOver);
            var ev = e.State.Log.Last(x => x.Kind == MatchEventKind.Endure);
            Assert.AreEqual(MatchRules.DefenseBase - 1, ev.Modifier);
            Assert.AreEqual(RollTier.Partial, ev.Tier);
            Assert.AreEqual(Posture.Down, e.State.Rival.Posture);
            Assert.AreEqual(MatchSide.Player, e.CurrentActor);
        }

        [Test]
        public void Endure_Strong_BreaksFree_InitiativeToDefender()
        {
            // больовий 10+ (штраф -2), «Терпіти» 12-2 = 10 → вирвався
            var e = MatchKit.Engine(true, new[] { 11, 12 });
            Lay(e.State.Rival);
            MatchKit.Play(e, MatchActionId.Submission);
            Assert.IsFalse(e.IsOver);
            Assert.AreEqual(RollTier.Strong, e.State.Log.Last(x => x.Kind == MatchEventKind.Endure).Tier);
            Assert.AreEqual(MatchSide.Rival, e.CurrentActor);
        }

        // ---------- QTE — лише видовище ----------

        [Test]
        public void Flourish_PlusOneHype_OncePerEvent_OnlyPlayersLandedMoves()
        {
            var e = MatchKit.Engine(true, new[] { 8, 3 });
            MatchKit.Play(e, MatchActionId.Strike);
            var hit = e.State.Log.Last(x => x.Kind == MatchEventKind.Action);
            int hype = e.State.Hype;
            var fl = e.RegisterFlourish(hit.Seq);
            Assert.AreEqual(1, fl.Count);
            Assert.AreEqual(MatchEventKind.Flourish, fl[0].Kind);
            Assert.AreEqual(hype + 1, e.State.Hype);
            Assert.AreEqual(0, e.RegisterFlourish(hit.Seq).Count, "раз на подію");
            Assert.AreEqual(hype + 1, e.State.Hype);
            MatchKit.Play(e, MatchActionId.Strike);
            var miss = e.State.Log.Last(x => x.Kind == MatchEventKind.Action);
            Assert.AreEqual(RollTier.Miss, miss.Tier);
            Assert.AreEqual(0, e.RegisterFlourish(miss.Seq).Count, "на провалі QTE немає");
            Assert.AreEqual(0, e.RegisterFlourish(0).Count, "Обмін — не прийом");
            Assert.AreEqual(0, e.RegisterFlourish(-1).Count);
            Assert.AreEqual(0, e.RegisterFlourish(9999).Count);
        }

        [Test]
        public void Flourish_IgnoresRivalMoves()
        {
            var e = MatchKit.Engine(false, new[] { 11 });
            MatchKit.Play(e, MatchActionId.Strike);
            var hit = e.State.Log.Last(x => x.Kind == MatchEventKind.Action);
            Assert.AreEqual(MatchSide.Rival, hit.Actor);
            Assert.AreEqual(0, e.RegisterFlourish(hit.Seq).Count);
        }

        /// <summary>
        /// Інваріант QTE (рішення власника 08.11.2026: «Лише видовище»): той самий кубик з QTE і без нього дає ті самі кидки, ступені,
        /// ушкодження й ініціативу; QTE змінює лише ажіотаж (+1 раз на подію). Політика дій не дивиться на ажіотаж — інакше розбіжність
        /// була б від вибору, а не від QTE.
        /// </summary>
        [Test]
        public void Flourish_NeverChangesDiceTiersDamageOrInitiative()
        {
            int flourishes = 0;
            for (int seed = 1; seed <= 200; seed++)
            {
                var a = new MatchEngine(MatchKit.Setup(), new SeededDiceRoller((ulong)seed));
                var b = new MatchEngine(MatchKit.Setup(), new SeededDiceRoller((ulong)seed));
                for (int step = 0; step < 400 && !a.IsOver; step++)
                {
                    int before = b.State.Log.Count;
                    Drive(a); Drive(b);
                    for (int i = before; i < b.State.Log.Count; i++)
                    {
                        var ev = b.State.Log[i];
                        if (ev.Kind == MatchEventKind.Action && ev.HasRoll && ev.Tier != RollTier.Miss && ev.Actor == MatchSide.Player)
                        {
                            if (b.RegisterFlourish(ev.Seq).Count > 0) flourishes++;
                            break;
                        }
                    }
                }
                Assert.AreEqual(a.IsOver, b.IsOver, "seed " + seed);
                Assert.AreEqual(Print(a), Print(b), "seed " + seed);
                Assert.GreaterOrEqual(b.State.Hype, a.State.Hype, "seed " + seed);
                if (a.IsOver) Assert.AreEqual(a.Result.Winner, b.Result.Winner, "seed " + seed);
            }
            Assert.Greater(flourishes, 100);
        }

        /// <summary>Одна дія за простою політикою (не дивиться на ажіотаж): встати, утримати, кинути, вдарити, перевести подих.</summary>
        private static void Drive(MatchEngine e)
        {
            if (e.IsOver) return;
            if (e.Phase == MatchPhase.AwaitReaction) { e.React(ReactionChoice.Sell); return; }
            if (e.Phase == MatchPhase.AwaitBoost) { e.Commit(0); return; }
            foreach (var id in new[] { MatchActionId.GetUp, MatchActionId.Pin, MatchActionId.Throw, MatchActionId.Strike })
                if (e.Describe(id).Available) { e.Perform(id); return; }
            e.EndTurn();
        }

        /// <summary>Журнал без QTE і без ажіотажу: що впало на кубиках і що сталося з тілами.</summary>
        private static string Print(MatchEngine e)
        {
            var sb = new StringBuilder();
            foreach (var ev in e.State.Log)
            {
                if (ev.Kind == MatchEventKind.Flourish) continue;
                sb.Append(ev.Kind).Append(':').Append(ev.Actor).Append(':').Append(ev.Action).Append(':').Append(ev.Roll).Append(':')
                  .Append(ev.Total).Append(':').Append(ev.Tier);
                foreach (var f in ev.Effects) if (f.Kind != MatchEffectKind.Hype) sb.Append('[').Append(f.Kind).Append(f.Who).Append(f.Amount).Append(']');
                sb.Append(';');
            }
            sb.Append(e.State.Player.Damage).Append('/').Append(e.State.Rival.Damage).Append('/').Append(e.CurrentActor);
            return sb.ToString();
        }
    }
}
