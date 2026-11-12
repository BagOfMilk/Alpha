using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core.Randomness;
using Game.Core.Ring;
using Game.Core.Ring.Match;
using Game.Gameplay.Combat;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>Лічильник викликів кубика: прев'ю і вибір ШІ не мають його торкатись.</summary>
    internal sealed class CountingDice : IDiceRoller
    {
        private readonly IDiceRoller _inner;
        public int Calls;
        public CountingDice(IDiceRoller inner) { _inner = inner; }
        public double Roll01(string streamId) { Calls++; return _inner.Roll01(streamId); }
        public string CaptureState() { return _inner.CaptureState(); }
        public void RestoreState(string blob) { _inner.RestoreState(blob); }
    }

    /// <summary>Прев'ю дії для будь-якої сторони (MatchEngine.Preview): «що бачиш, те й кидаєш», чистота, хід суперника як свій.</summary>
    public class MatchPreviewTests
    {
        private static readonly MatchActionId[] All = (MatchActionId[])Enum.GetValues(typeof(MatchActionId));

        private static MatchSetup SeededSetup(int seed)
        {
            Func<int, int> attr = k => (int)(((uint)(seed * 2654435761u + (uint)k * 40503u) >> 7) % 5) - 1;
            return MatchKit.Setup(x =>
            {
                foreach (RingAttr a in Enum.GetValues(typeof(RingAttr)))
                {
                    x.Player.Set(a, attr((int)a));
                    x.Rival.Set(a, attr(10 + (int)a));
                }
                x.Player.FinisherAttr = (RingAttr)(seed % 5);
                x.Rival.FinisherAttr = (RingAttr)((seed / 5) % 5);
                x.PlayerRole = (RingPath)(seed % 3);
                x.PlayerHasAlly = seed % 4 == 0;
                x.PlayerStartMomentum = seed % 3;
                x.StartHype = 2 + seed % 4;
                x.HopeSpotEnabled = seed % 3 == 0;
                if (seed % 5 == 0) x.Stipulations |= MatchStipulations.NoDq;
            });
        }

        private static string Snap(MatchEngine e)
        {
            var s = e.State;
            return string.Join("|", e.Phase, s.Round, s.Chain, s.CurrentActor, s.Hype, s.Outside, s.OutsideCount,
                s.Player.Stamina, s.Rival.Stamina, s.Player.Damage, s.Rival.Damage, s.Player.Momentum, s.Rival.Momentum,
                s.Player.Posture, s.Rival.Posture, s.Player.ReversalTokens, s.Rival.ReversalTokens, s.Log.Count);
        }

        private static void AssertSame(ActionOption o, ActionPreview p, string ctx)
        {
            Assert.AreEqual(o.Id, p.Id, ctx); Assert.AreEqual(o.Cost, p.Cost, ctx); Assert.AreEqual(o.IsBig, p.IsBig, ctx);
            Assert.AreEqual(o.HasAttr, p.HasAttr, ctx); if (o.HasAttr) Assert.AreEqual(o.Attr, p.Attr, ctx);
            Assert.AreEqual(o.Available, p.Available, ctx); Assert.AreEqual(o.Reason, p.Reason, ctx);
            Assert.AreEqual(o.HasRoll, p.HasRoll, ctx); Assert.AreEqual(o.RollIsOpponents, p.RollIsOpponents, ctx);
            Assert.AreEqual(o.Modifier, p.Modifier, ctx); Assert.AreEqual(o.Winded, p.Winded, ctx);
            CollectionAssert.AreEqual(o.Chances, p.Chances36, ctx);
        }

        [Test]
        public void Chances36_SumTo36_ForEveryAction_BothSides()
        {
            for (int seed = 1; seed <= 30; seed++)
            {
                var e = new MatchEngine(SeededSetup(seed), new SeededDiceRoller((ulong)seed));
                foreach (var side in new[] { MatchSide.Player, MatchSide.Rival })
                    foreach (var id in All)
                    {
                        var p = e.Preview(side, id);
                        if (!p.HasRoll) { Assert.IsNull(p.Chances36, "без кидка шансів нема"); continue; }
                        Assert.AreEqual(3, p.Chances36.Length);
                        Assert.AreEqual(36, p.Chances36.Sum(), "seed " + seed + " " + side + " " + id);
                    }
            }
        }

        [Test]
        public void Preview_OfCurrentActor_EqualsDescribe_ThroughWholeMatches()
        {
            for (int seed = 1; seed <= 40; seed++)
            {
                var e = new MatchEngine(SeededSetup(seed), new SeededDiceRoller((ulong)seed));
                int steps = 0;
                while (!e.IsOver && steps++ < 400)
                {
                    if (e.Phase == MatchPhase.AwaitAction)
                        foreach (var id in All)
                        {
                            AssertSame(e.Describe(id), e.Preview(e.CurrentActor, id), "seed " + seed + " крок " + steps + " " + id);
                            AssertSame(e.Describe(id, 2), e.Preview(e.CurrentActor, id, 2), "extra seed " + seed + " " + id);
                        }
                    MatchAi.Step(e);
                }
            }
        }

        [Test]
        public void PreviewAll_FollowsActionOrder_AndAvailabilityMatchesGetAvailableActions()
        {
            var e = new MatchEngine(SeededSetup(3), new SeededDiceRoller(3));
            var list = e.GetAvailableActions();
            var all = e.PreviewAll(e.CurrentActor);
            Assert.GreaterOrEqual(all.Count, list.Count);
            for (int i = 0; i < list.Count; i++) AssertSame(list[i], all[i], "i=" + i);
            for (int i = list.Count; i < all.Count; i++) Assert.AreEqual(MatchActionId.HopeSpot, all[i].Id);
        }

        [Test]
        public void ModifierOfPreview_IsTheModifierOfTheEvent_AfterPerform()
        {
            // звичайна дія
            var e = MatchKit.Engine(true, new[] { 8 });
            var p = e.Preview(MatchSide.Player, MatchActionId.Strike);
            Assert.IsTrue(p.Available);
            MatchKit.Play(e, MatchActionId.Strike);
            var ev = e.State.Log.First(x => x.Kind == MatchEventKind.Action && x.Action == MatchActionId.Strike);
            Assert.AreEqual(p.Modifier, ev.Modifier);

            // утримання — кидок суперника «Вирватися» (подія Kickout)
            var e2 = MatchKit.Engine(true, new[] { 10, 3, 9 });
            e2.State.Rival.Posture = Posture.Down; e2.State.Rival.Damage = 8;
            var pp = e2.Preview(MatchSide.Player, MatchActionId.Pin);
            Assert.IsTrue(pp.Available, pp.Reason.ToString());
            Assert.IsTrue(pp.RollIsOpponents);
            e2.Perform(MatchActionId.Pin);
            var k = e2.State.Log.Last(x => x.Kind == MatchEventKind.Kickout);
            Assert.AreEqual(pp.Modifier, k.Modifier);
        }

        [Test]
        public void Preview_OfWaitingSide_UsesThatSidesStats_AndIsNotAvailable()
        {
            var e = new MatchEngine(MatchKit.Setup(s =>
            {
                s.Player.Set(RingAttr.Strength, 3); s.Rival.Set(RingAttr.Strength, -1);
                s.Player.Set(RingAttr.Technique, 3);
            }), new ScriptedDice(12, 2));
            Assert.AreEqual(MatchSide.Player, e.CurrentActor);
            var mine = e.Preview(MatchSide.Player, MatchActionId.Strike);
            var his = e.Preview(MatchSide.Rival, MatchActionId.Strike);
            Assert.AreEqual(3, mine.Modifier);
            Assert.AreEqual(-1, his.Modifier);
            Assert.IsTrue(mine.Available);
            Assert.IsFalse(his.Available);
            Assert.AreEqual(UnavailableReason.WrongPhase, his.Reason);
            Assert.AreEqual(36, his.Chances36.Sum());

            // коли ініціатива переходить, Describe нової сторони == попереднє Preview
            var before = e.Preview(MatchSide.Rival, MatchActionId.Strike);
            e.EndTurn();
            Assert.AreEqual(MatchSide.Rival, e.CurrentActor);
            var after = e.Describe(MatchActionId.Strike);
            Assert.AreEqual(before.Modifier, after.Modifier);
            CollectionAssert.AreEqual(before.Chances36, after.Chances);
            Assert.IsTrue(after.Available);
        }

        [Test]
        public void Winded_AtZeroStamina_MinusOne_StaminaAfterZero()
        {
            var e = MatchKit.Engine(true);
            var fresh = e.Preview(MatchSide.Player, MatchActionId.Strike);
            e.State.Player.Stamina = 0;
            var p = e.Preview(MatchSide.Player, MatchActionId.Strike);
            Assert.IsTrue(p.Winded);
            Assert.IsTrue(p.Available, "удар на нулі можна");
            Assert.AreEqual(fresh.Modifier - MatchRules.WindedPenalty, p.Modifier);
            Assert.AreEqual(0, p.StaminaAfter);
            Assert.AreEqual(0, p.StaminaBefore);
        }

        [Test]
        public void NeedsFullStamina_MakesActionUnavailable()
        {
            var e = MatchKit.Engine(true);
            e.State.Player.Stamina = MatchRules.Cost(MatchActionId.TopRope) - 1;
            var p = e.Preview(MatchSide.Player, MatchActionId.TopRope);
            Assert.IsFalse(p.Available);
            Assert.AreEqual(UnavailableReason.NotEnoughStamina, p.Reason);
            Assert.AreEqual(MatchRules.Cost(MatchActionId.TopRope), p.Cost);
        }

        [Test]
        public void CatchBreath_StaminaAfter_IsCappedAtMax()
        {
            var e = MatchKit.Engine(true);
            e.State.Player.Stamina = 4;
            Assert.AreEqual(7, e.Preview(MatchSide.Player, MatchActionId.CatchBreath).StaminaAfter);
            e.State.Player.Stamina = MatchRules.MaxStamina - 1;
            Assert.AreEqual(MatchRules.MaxStamina, e.Preview(MatchSide.Player, MatchActionId.CatchBreath).StaminaAfter);
            var c = e.Preview(MatchSide.Player, MatchActionId.CatchBreath);
            Assert.IsFalse(c.HasRoll);
            Assert.IsTrue(c.Available);
        }

        [Test]
        public void HopeSpot_MissDoesNotPassInitiative_AndIsNotWinded()
        {
            var e = MatchKit.Engine(true, null, s => s.HopeSpotEnabled = true);
            Assert.IsFalse(e.Preview(MatchSide.Player, MatchActionId.HopeSpot).MissPassesInitiative);
            Assert.IsTrue(e.Preview(MatchSide.Player, MatchActionId.Strike).MissPassesInitiative);
            e.State.Player.Stamina = 0;
            Assert.IsFalse(e.Preview(MatchSide.Player, MatchActionId.HopeSpot).Winded);
        }

        [Test]
        public void Preview_DoesNotMutateState_AndDoesNotTouchTheDice()
        {
            for (int seed = 1; seed <= 30; seed++)
            {
                var dice = new CountingDice(new SeededDiceRoller((ulong)seed));
                var e = new MatchEngine(SeededSetup(seed), dice);
                int steps = 0;
                while (!e.IsOver && steps++ < 400)
                {
                    int calls = dice.Calls; string snap = Snap(e);
                    foreach (var side in new[] { MatchSide.Player, MatchSide.Rival })
                    {
                        e.PreviewAll(side);
                        foreach (var id in All) e.Preview(side, id, 1);
                    }
                    Assert.AreEqual(snap, Snap(e), "seed " + seed + " крок " + steps);
                    Assert.AreEqual(calls, dice.Calls, "прев'ю торкнулось кубика, seed " + seed);
                    MatchAi.Step(e);
                }
            }
        }

        [Test]
        public void AiChoice_IsPure_AndStepPerformsExactlyTheChosenAction()
        {
            int checkedActions = 0;
            for (int seed = 1; seed <= 250; seed++)
            {
                var dice = new CountingDice(new SeededDiceRoller((ulong)seed));
                var e = new MatchEngine(SeededSetup(seed), dice);
                int steps = 0;
                while (!e.IsOver && steps++ < 400)
                {
                    if (e.Phase == MatchPhase.AwaitAction)
                    {
                        string snap = Snap(e); int calls = dice.Calls; int logBefore = e.State.Log.Count;
                        var chosen = MatchAi.ChooseAction(e);
                        Assert.AreEqual(snap, Snap(e), "ChooseAction мутує стан, seed " + seed);
                        Assert.AreEqual(calls, dice.Calls, "ChooseAction торкнувся кубика, seed " + seed);
                        Assert.AreEqual(chosen, MatchAi.ChooseAction(e), "ChooseAction недетермінований");
                        var actor = e.CurrentActor;
                        MatchAi.Step(e);
                        // великі прийоми спершу чекають реакції захисника, решта — одразу пишуть подію Action
                        var first = e.State.Log.Skip(logBefore).FirstOrDefault(x => x.Kind == MatchEventKind.Action);
                        if (first != null)
                        {
                            Assert.AreEqual(chosen, first.Action, "seed " + seed + " крок " + steps);
                            Assert.AreEqual(actor, first.Actor);
                        }
                        else
                        {
                            if (e.Phase == MatchPhase.AwaitReaction)
                            {
                                Assert.AreEqual(chosen, e.PendingReaction.Action);
                                Assert.AreEqual(actor, e.PendingReaction.Attacker);
                            }
                            else
                            {
                                Assert.AreEqual(MatchPhase.AwaitBoost, e.Phase, "seed " + seed + " " + chosen);
                                Assert.AreEqual(chosen, e.PendingRoll.Action);
                                Assert.AreEqual(actor, e.PendingRoll.Actor);
                            }
                        }
                        checkedActions++;
                    }
                    else
                    {
                        string snap = Snap(e);
                        if (e.Phase == MatchPhase.AwaitReaction) MatchAi.ChooseReaction(e);
                        else if (e.Phase == MatchPhase.AwaitBoost) MatchAi.ChooseBoost(e);
                        Assert.AreEqual(snap, Snap(e), "вибір реакції/докрутки мутує стан, seed " + seed);
                        MatchAi.Step(e);
                    }
                }
            }
            Assert.Greater(checkedActions, 1000);
        }

        [Test]
        public void AiFinisher_IsPerformedViaSetupFinisherOf()
        {
            bool seen = false;
            for (int seed = 1; seed <= 250 && !seen; seed++)
            {
                var setup = SeededSetup(seed);
                setup.PlayerFinisher = new MoveHint { Key = "p_fin" };
                setup.RivalFinisher = new MoveHint { Key = "r_fin" };
                var e = new MatchEngine(setup, new SeededDiceRoller((ulong)seed));
                int steps = 0;
                while (!e.IsOver && steps++ < 400)
                {
                    if (e.Phase == MatchPhase.AwaitAction && MatchAi.ChooseAction(e) == MatchActionId.Finisher)
                    {
                        var actor = e.CurrentActor; int n = e.State.Log.Count;
                        MatchAi.Step(e);
                        // великий прийом: захисник реагує, а без жетонів реверсу кидок одразу — AwaitBoost (P2-1: матчі довші, жетони встигають вичерпатись)
                        Assert.IsTrue(e.Phase == MatchPhase.AwaitReaction || e.Phase == MatchPhase.AwaitBoost, "фінішер — великий прийом");
                        if (e.Phase == MatchPhase.AwaitReaction) e.React(ReactionChoice.Sell);
                        if (e.Phase == MatchPhase.AwaitBoost) e.Commit(0);
                        if (e.Phase == MatchPhase.AwaitBoost) e.Commit(0);
                        var ev = e.State.Log.Skip(n).First(x => x.Kind == MatchEventKind.Action);
                        Assert.AreEqual(MatchActionId.Finisher, ev.Action);
                        Assert.AreEqual(setup.FinisherOf(actor).Key, ev.MoveKey);
                        seen = true; break;
                    }
                    MatchAi.Step(e);
                }
            }
            Assert.IsTrue(seen, "ШІ жодного разу не обрав фінішер на 250 сідах");
        }

        /// <summary>
        /// «Накрити знову — після іншого прийому» стосується лише того, хто щойно накривав і далі ходить. Сторона без ініціативи
        /// не бачить у прев'ю JustPinned (рев'ю D 11.11.2026: без цієї умови тести не червоніли).
        /// </summary>
        [Test]
        public void JustPinned_OnlyForTheSideThatPinned_NotInTheOtherSidesPreview()
        {
            var e = MatchKit.Engine(false, new[] { 4, 7, 7, 4 }, s => s.PlayerHasAlly = true);
            e.State.Player.Posture = Posture.Down; e.State.Player.Damage = Math.Max(e.State.Player.Damage, MatchRules.PinMinDamage); MatchKit.NotFresh(e.State.Player);
            e.Perform(MatchActionId.Pin);
            Assert.IsFalse(e.IsOver, "напарник розірвав утримання");
            Assert.AreEqual(MatchSide.Rival, e.CurrentActor, "накривав суперник — він і ходить");
            Assert.AreEqual(UnavailableReason.JustPinned, e.Preview(MatchSide.Rival, MatchActionId.Pin).Reason);

            // у гравця все для утримання є, крім ходу: причина — не його черга, а не JustPinned
            e.State.Player.Posture = Posture.Standing;
            e.State.Rival.Posture = Posture.Down; e.State.Rival.Damage = Math.Max(e.State.Rival.Damage, MatchRules.PinMinDamage);
            var other = e.Preview(MatchSide.Player, MatchActionId.Pin);
            Assert.AreNotEqual(UnavailableReason.JustPinned, other.Reason);
            Assert.AreEqual(UnavailableReason.WrongPhase, other.Reason);
        }
    }
}
