using System;
using System.Linq;
using Game.Core.Ring;
using Game.Core.Ring.Match;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>Таблиця дій (gdd §2.3), зони, реакції (§2.5), захисні кидки (§2.6), перки (§6).</summary>
    public class MatchActionsTests
    {
        private static UnavailableReason Why(MatchEngine e, MatchActionId id) { return e.Describe(id).Reason; }

        // ---------- умови доступності ----------

        [Test]
        public void Strike_NeedsBothStanding_SameSide()
        {
            var e = MatchKit.Engine(true);
            Assert.IsTrue(e.Describe(MatchActionId.Strike).Available);
            e.State.Rival.Posture = Posture.Down; e.State.Rival.Damage = Math.Max(e.State.Rival.Damage, MatchRules.PinMinDamage);
            Assert.AreEqual(UnavailableReason.TargetNotStanding, Why(e, MatchActionId.Strike));
            e.State.Rival.Posture = Posture.Standing;
            e.State.Rival.Zone = Zone.Outside;
            Assert.AreEqual(UnavailableReason.NotSameZone, Why(e, MatchActionId.Strike));
            e.State.Player.Zone = Zone.Outside;
            Assert.IsTrue(e.Describe(MatchActionId.Strike).Available, "обидва поза рингом — удар можливий");
        }

        [Test]
        public void LyingActor_CanOnlyGetUpRecoverLeaveBreath()
        {
            var e = MatchKit.Engine(true);
            e.State.Player.Posture = Posture.Down; e.State.Player.Damage = Math.Max(e.State.Player.Damage, MatchRules.PinMinDamage);
            e.State.Player.Damage = 3;
            foreach (var o in e.GetAvailableActions())
            {
                bool allowed = o.Id == MatchActionId.GetUp || o.Id == MatchActionId.Recover || o.Id == MatchActionId.LeaveRing
                               || o.Id == MatchActionId.CatchBreath;
                Assert.AreEqual(allowed, o.Available, o.Id + " " + o.Reason);
            }
            Assert.AreEqual(UnavailableReason.ActorDown, Why(e, MatchActionId.Strike));
        }

        [Test]
        public void Pin_NeedsLyingTarget_InRing_NotInRopes()
        {
            var e = MatchKit.Engine(true);
            Assert.AreEqual(UnavailableReason.TargetNotDown, Why(e, MatchActionId.Pin), "на стоячому недоступне");
            e.State.Rival.Posture = Posture.Down; e.State.Rival.Damage = Math.Max(e.State.Rival.Damage, MatchRules.PinMinDamage);
            Assert.IsTrue(e.Describe(MatchActionId.Pin).Available);
            e.State.Rival.Zone = Zone.Ropes;
            Assert.AreEqual(UnavailableReason.InRopes, Why(e, MatchActionId.Pin));
            e.State.Rival.Zone = Zone.Outside;
            Assert.AreEqual(UnavailableReason.NotInRing, Why(e, MatchActionId.Pin));
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
            e.State.Rival.Posture = Posture.Down; e.State.Rival.Damage = Math.Max(e.State.Rival.Damage, MatchRules.PinMinDamage);
            Assert.IsTrue(e.Describe(MatchActionId.Submission).Available);
        }

        [Test]
        public void ThrowRopesCorner_NeedStandingTargetInRing()
        {
            var e = MatchKit.Engine(true);
            foreach (var id in new[] { MatchActionId.Throw, MatchActionId.IntoRopes, MatchActionId.IntoCorner })
                Assert.IsTrue(e.Describe(id).Available, id.ToString());
            e.State.Rival.Zone = Zone.Outside;
            foreach (var id in new[] { MatchActionId.Throw, MatchActionId.IntoRopes, MatchActionId.IntoCorner })
                Assert.AreEqual(UnavailableReason.NotInRing, Why(e, id));
            e.State.Rival.Zone = Zone.Center; e.State.Rival.Posture = Posture.Down; e.State.Rival.Damage = Math.Max(e.State.Rival.Damage, MatchRules.PinMinDamage);
            Assert.AreEqual(UnavailableReason.TargetNotStanding, Why(e, MatchActionId.Throw));
        }

        [Test]
        public void TopRope_OnlyFromCorner()
        {
            var e = MatchKit.Engine(true);
            Assert.AreEqual(UnavailableReason.NotInCorner, Why(e, MatchActionId.TopRope));
            e.State.Player.Zone = Zone.Corner;
            Assert.IsTrue(e.Describe(MatchActionId.TopRope).Available);
        }

        [Test]
        public void Finisher_NeedsMomentumHypeAndHurtTarget()
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
        }

        [Test]
        public void Taunt_NeedsLyingOrOutsideTarget()
        {
            var e = MatchKit.Engine(true);
            Assert.AreEqual(UnavailableReason.TargetNotDownOrOutside, Why(e, MatchActionId.Taunt));
            e.State.Rival.Posture = Posture.Down; e.State.Rival.Damage = Math.Max(e.State.Rival.Damage, MatchRules.PinMinDamage);
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
        public void GetUp_OnlyWhenLying_PartialCostsAnotherAp()
        {
            var e = MatchKit.Engine(true, new[] { 8 });
            Assert.AreEqual(UnavailableReason.NotLying, Why(e, MatchActionId.GetUp));
            e.State.Player.Posture = Posture.Down; e.State.Player.Damage = Math.Max(e.State.Player.Damage, MatchRules.PinMinDamage);
            MatchKit.Play(e, MatchActionId.GetUp);
            Assert.AreEqual(Posture.Standing, e.State.Player.Posture);
            Assert.AreEqual(1, e.Ap, "ціна 1 + 7–9 забирає ще 1");
        }

        [Test]
        public void LeaveAndReturn_ToggleOutside()
        {
            var e = MatchKit.Engine(true);
            Assert.AreEqual(UnavailableReason.NotOutside, Why(e, MatchActionId.ReturnToRing));
            MatchKit.Play(e, MatchActionId.LeaveRing);
            Assert.AreEqual(Zone.Outside, e.State.Player.Zone);
            Assert.AreEqual(UnavailableReason.AlreadyOutside, Why(e, MatchActionId.LeaveRing));
            MatchKit.Play(e, MatchActionId.ReturnToRing);
            Assert.AreEqual(Zone.Center, e.State.Player.Zone);
        }

        [Test]
        public void Recover_LowersDamageAndHype()
        {
            var e = MatchKit.Engine(true);
            Assert.AreEqual(UnavailableReason.NothingToRecover, Why(e, MatchActionId.Recover));
            e.State.Player.Damage = 4;
            MatchKit.Play(e, MatchActionId.Recover);
            Assert.AreEqual(3, e.State.Player.Damage);
            Assert.AreEqual(2, e.State.Hype);
        }

        // ---------- результати дій ----------

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
        }

        [Test]
        public void Throw_Partial_CostsSelfDamage_Miss_PutsAttackerDown()
        {
            var e = MatchKit.Engine(true, new[] { 8 });
            MatchKit.Play(e, MatchActionId.Throw);
            Assert.AreEqual(2, e.State.Rival.Damage);
            Assert.AreEqual(1, e.State.Player.Damage);
            var m = MatchKit.Engine(true, new[] { 3 });
            MatchKit.Play(m, MatchActionId.Throw);
            Assert.AreEqual(Posture.Down, m.State.Player.Posture, "він вивернувся: ти лежиш");
            Assert.AreEqual(MatchSide.Rival, m.CurrentActor);
        }

        [Test]
        public void TopRope_Tiers()
        {
            var e = MatchKit.Engine(true, new[] { 11 });
            e.State.Player.Zone = Zone.Corner;
            MatchKit.Play(e, MatchActionId.TopRope);
            Assert.AreEqual(4, e.State.Rival.Damage);
            Assert.AreEqual(Posture.Down, e.State.Rival.Posture);
            Assert.AreEqual(5, e.State.Hype);
            Assert.AreEqual(Zone.Center, e.State.Player.Zone);
            var m = MatchKit.Engine(true, new[] { 3 });
            m.State.Player.Zone = Zone.Corner;
            MatchKit.Play(m, MatchActionId.TopRope);
            Assert.AreEqual(2, m.State.Player.Damage);
            Assert.AreEqual(Posture.Down, m.State.Player.Posture);
        }

        [Test]
        public void IntoRopes_Strong_GivesNextHitBonus_ThatExpires()
        {
            var e = MatchKit.Engine(true, new[] { 11 });
            MatchKit.Play(e, MatchActionId.IntoRopes);
            Assert.AreEqual(Zone.Ropes, e.State.Rival.Zone);
            Assert.AreEqual(1, e.Describe(MatchActionId.Strike).Modifier);
            Assert.AreEqual(1, e.Describe(MatchActionId.Throw).Modifier);
            MatchKit.Play(e, MatchActionId.Strike);
            Assert.AreEqual(0, e.Describe(MatchActionId.Strike).Modifier, "бонус витрачено");
        }

        [Test]
        public void IntoRopes_Partial_NoBonus()
        {
            var e = MatchKit.Engine(true, new[] { 8 });
            MatchKit.Play(e, MatchActionId.IntoRopes);
            Assert.AreEqual(Zone.Ropes, e.State.Rival.Zone);
            Assert.AreEqual(0, e.Describe(MatchActionId.Strike).Modifier);
        }

        [Test]
        public void IntoCorner_TargetTakesExtraStrikeDamage_MissPutsSelfInCorner()
        {
            var e = MatchKit.Engine(true, new[] { 8 });
            MatchKit.Play(e, MatchActionId.IntoCorner);
            Assert.AreEqual(Zone.Corner, e.State.Rival.Zone);
            MatchKit.Play(e, MatchActionId.Strike);
            Assert.AreEqual(2, e.State.Rival.Damage, "удар 7–9 = 1, у куті +1");
            var m = MatchKit.Engine(true, new[] { 3 });
            MatchKit.Play(m, MatchActionId.IntoCorner);
            Assert.AreEqual(Zone.Corner, m.State.Player.Zone);
        }

        [Test]
        public void CornerAndRopes_PersistThroughOwnersNextTurn_ThenReset()
        {
            var e = MatchKit.Engine(true, new[] { 8 });
            MatchKit.Play(e, MatchActionId.IntoRopes);
            e.EndTurn();                       // хід суперника: канати ще діють
            Assert.AreEqual(Zone.Ropes, e.State.Rival.Zone);
            e.EndTurn();
            Assert.AreEqual(Zone.Center, e.State.Rival.Zone);
        }

        [Test]
        public void Taunt_Tiers()
        {
            var e = MatchKit.Engine(true, new[] { 11 });
            e.State.Rival.Posture = Posture.Down; e.State.Rival.Damage = Math.Max(e.State.Rival.Damage, MatchRules.PinMinDamage);
            MatchKit.Play(e, MatchActionId.Taunt);
            Assert.AreEqual(5, e.State.Hype);
            Assert.AreEqual(1, e.State.Player.Momentum);
            var m = MatchKit.Engine(true, new[] { 8 });
            m.State.Rival.Posture = Posture.Down; m.State.Rival.Damage = Math.Max(m.State.Rival.Damage, MatchRules.PinMinDamage);
            MatchKit.Play(m, MatchActionId.Taunt);
            Assert.AreEqual(4, m.State.Hype);
            Assert.AreEqual(Posture.Standing, m.State.Rival.Posture, "він встає");
        }

        [Test]
        public void RepeatingTheSameActionInATurn_CostsHype()
        {
            var e = MatchKit.Engine(true);
            MatchKit.Play(e, MatchActionId.Strike);
            Assert.AreEqual(3, e.State.Hype);
            MatchKit.Play(e, MatchActionId.Strike);
            Assert.AreEqual(2, e.State.Hype);
            e.EndTurn(); e.EndTurn();
            MatchKit.Play(e, MatchActionId.Strike);
            Assert.AreEqual(2, e.State.Hype, "новий хід — повтору немає");
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
        public void Dirty_Miss_WarnsAndCostsHype_EndsTurn()
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
        public void DistractedReferee_SeesNothing_UntilRoundEnds()
        {
            var e = MatchKit.Engine(true, new[] { 3 }, s => s.PlayerHasAlly = true);
            MatchKit.Play(e, MatchActionId.Ally);
            Assert.IsTrue(e.State.RefereeDistracted);
            MatchKit.Play(e, MatchActionId.Dirty);
            Assert.AreEqual(0, e.State.Player.Warnings);
            Assert.IsTrue(e.State.RefereeDistracted, "раунд ще не скінчився");
            e.EndTurn();
            Assert.IsFalse(e.State.RefereeDistracted);
        }

        // ---------- реакція на великий прийом ----------

        [Test]
        public void BigMoves_AskForReaction_OthersDoNot()
        {
            foreach (var id in new[] { MatchActionId.Throw })
            {
                var e = MatchKit.Engine(true);
                e.Perform(id);
                Assert.AreEqual(MatchPhase.AwaitReaction, e.Phase);
                Assert.AreEqual(MatchSide.Rival, e.PendingReaction.Defender);
                CollectionAssert.AreEqual(TwoD6.ChancesOf36(0), e.PendingReaction.ReverseChances);
            }
            var s = MatchKit.Engine(true);
            s.Perform(MatchActionId.Strike);
            Assert.AreEqual(MatchPhase.AwaitBoost, s.Phase);
            var t = MatchKit.Engine(true);
            t.State.Player.Zone = Zone.Corner;
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
            e.State.Rival.Posture = Posture.Down; e.State.Rival.Damage = Math.Max(e.State.Rival.Damage, MatchRules.PinMinDamage);
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
        public void Reverse_Strong_AttackerFallsAndTurnEnds()
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
        public void Reverse_Partial_MoveBrokenTurnEnds_AttackerStands()
        {
            var e = MatchKit.Engine(true, new[] { 8 });
            e.Perform(MatchActionId.Throw);
            e.React(ReactionChoice.Reverse);
            Assert.AreEqual(Posture.Standing, e.State.Player.Posture);
            Assert.AreEqual(MatchSide.Rival, e.CurrentActor);
            Assert.AreEqual(0, e.State.Rival.Damage);
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

        private static MatchEngine PinReady(System.Action<MatchSetup> tweak = null)
        {
            var e = MatchKit.Engine(true, null, tweak);
            e.State.Rival.Posture = Posture.Down; e.State.Rival.Damage = Math.Max(e.State.Rival.Damage, MatchRules.PinMinDamage);
            return e;
        }

        [Test]
        public void Kickout_Escalates_WithEveryKickoutDone()
        {
            var e = PinReady();
            Assert.AreEqual(MatchRules.DefenseBase, e.Describe(MatchActionId.Pin).Modifier);
            e.State.Rival.KickoutsDone = 1;
            Assert.AreEqual(MatchRules.DefenseBase-1, e.Describe(MatchActionId.Pin).Modifier);
            e.State.Rival.KickoutsDone = 3;
            Assert.AreEqual(MatchRules.DefenseBase-3, e.Describe(MatchActionId.Pin).Modifier);
        }

        [Test]
        public void Kickout_DamageThresholds()
        {
            var e = PinReady();
            e.State.Rival.Damage = 5; Assert.AreEqual(MatchRules.DefenseBase, e.Describe(MatchActionId.Pin).Modifier);
            e.State.Rival.Damage = 6; Assert.AreEqual(MatchRules.DefenseBase-1, e.Describe(MatchActionId.Pin).Modifier);
            e.State.Rival.Damage = 10; Assert.AreEqual(MatchRules.DefenseBase-2, e.Describe(MatchActionId.Pin).Modifier);
        }

        [Test]
        public void Perk_DamageThresholdShift_MovesBothThresholds()
        {
            var e = PinReady(s => s.RivalMods.DamageThresholdShift = 2);
            e.State.Rival.Damage = 6; Assert.AreEqual(MatchRules.DefenseBase, e.Describe(MatchActionId.Pin).Modifier);
            e.State.Rival.Damage = 8; Assert.AreEqual(MatchRules.DefenseBase-1, e.Describe(MatchActionId.Pin).Modifier);
            e.State.Rival.Damage = 12; Assert.AreEqual(MatchRules.DefenseBase-2, e.Describe(MatchActionId.Pin).Modifier);
            e.State.Rival.Damage = 10; Assert.AreEqual(MatchRules.DefenseBase-1, e.Describe(MatchActionId.Pin).Modifier);
        }

        [Test]
        public void Perk_KickOutBonus_AndStamina()
        {
            var e = PinReady(s => { s.RivalMods.KickOutBonus = 1; s.Rival.Set(RingAttr.Stamina, 2); });
            Assert.AreEqual(MatchRules.DefenseBase+3, e.Describe(MatchActionId.Pin).Modifier);
        }

        [Test]
        public void LateStage_FromRound12_PenalisesDefence()
        {
            var e = PinReady();
            e.State.Round = 11; Assert.AreEqual(MatchRules.DefenseBase, e.Describe(MatchActionId.Pin).Modifier);
            e.State.Round = 12; Assert.AreEqual(MatchRules.DefenseBase-1, e.Describe(MatchActionId.Pin).Modifier);
        }

        [Test]
        public void Perk_RiskyMoveBonus_TopRopeAndDesperate()
        {
            var e = MatchKit.Engine(true, null, s => { s.PlayerMods.RiskyMoveBonus = 1; s.Player.Set(RingAttr.Agility, 1); });
            e.State.Player.Zone = Zone.Corner;
            Assert.AreEqual(2, e.Describe(MatchActionId.TopRope).Modifier);
            Assert.AreEqual(0, e.Describe(MatchActionId.Strike).Modifier);
            Assert.AreEqual(1, e.Describe(MatchActionId.Desperate).Modifier);
        }

        [Test]
        public void Kickout_Tiers_AtOne_AtTwo()
        {
            // «раз»: 10+, «два»: 7–9 (ажіотаж +2, імпульс +1 тому, хто вирвався)
            var e = PinReady();
            e.Perform(MatchActionId.Pin);
            Assert.IsTrue(e.IsOver || e.Phase == MatchPhase.AwaitAction);
            var one = MatchKit.Engine(true, new[] { 11 });
            one.State.Rival.Posture = Posture.Down; one.State.Rival.Damage = Math.Max(one.State.Rival.Damage, MatchRules.PinMinDamage);
            one.Perform(MatchActionId.Pin);
            Assert.AreEqual(1, one.State.Rival.KickoutsDone);
            Assert.AreEqual(3, one.State.Hype);
            var two = MatchKit.Engine(true, new[] { 6 });
            two.State.Rival.Posture = Posture.Down; two.State.Rival.Damage = Math.Max(two.State.Rival.Damage, MatchRules.PinMinDamage);
            two.Perform(MatchActionId.Pin);
            Assert.AreEqual(5, two.State.Hype);
            Assert.AreEqual(1, two.State.Rival.Momentum);
            Assert.IsFalse(two.IsOver);
        }

        [Test]
        public void Finisher_SpendsMomentum_AutoPinsWithPenalty()
        {
            var e = MatchKit.Engine(true, new[] { 11, 12 });
            e.State.Player.Momentum = 5; e.State.Hype = 4; e.State.Rival.Damage = 6;
            e.Perform(MatchActionId.Finisher);
            Assert.AreEqual(0, e.State.Player.Momentum);
            e.React(ReactionChoice.Sell);
            e.Commit(0);
            var kick = e.State.Log.Last(x => x.Kind == MatchEventKind.Kickout);
            Assert.AreEqual(10, e.State.Rival.Damage);
            Assert.AreEqual(MatchRules.DefenseBase - 5, kick.Modifier, "-2 поріг на межі, -3 фінішер");
            Assert.AreEqual(Posture.Down, e.State.Rival.Posture);
        }

        [Test]
        public void Endure_ModifiersAndRopes()
        {
            // больовий 7–9 (штраф -1), «Терпіти» 8-1 = 7 → тримається; у канатах — розірвано
            var e = MatchKit.Engine(true, new[] { 8, 8 });
            e.State.Rival.Posture = Posture.Down; e.State.Rival.Damage = Math.Max(e.State.Rival.Damage, MatchRules.PinMinDamage);
            MatchKit.Play(e, MatchActionId.Submission);
            Assert.IsFalse(e.IsOver);
            var ev = e.State.Log.Last(x => x.Kind == MatchEventKind.Endure);
            Assert.AreEqual(MatchRules.DefenseBase - 1, ev.Modifier);
            Assert.AreEqual(RollTier.Partial, ev.Tier);
            Assert.AreEqual(Posture.Down, e.State.Rival.Posture);
        }
    }
}
