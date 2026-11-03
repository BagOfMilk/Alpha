using System;
using System.Collections.Generic;
using Game.Core.Randomness;
using Game.Core.Ring;
using Game.Core.Ring.Match;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>Кубик за сценарієм: кожне значення — сума 2d6; після черги — 7 (7–9). Реалізація живе в тестах, не в ядрі.</summary>
    internal sealed class ScriptedDice : IDiceRoller
    {
        private readonly Queue<int> _dice = new Queue<int>();
        private int _tail;

        public ScriptedDice(params int[] totals)
        {
            foreach (var t in totals) { int a = t / 2; _dice.Enqueue(a); _dice.Enqueue(t - a); }
        }

        public double Roll01(string streamId)
        {
            int d = _dice.Count > 0 ? _dice.Dequeue() : (_tail++ % 2 == 0 ? 3 : 4);
            return (d - 0.5) / 6.0;
        }

        public string CaptureState() { return ""; }
        public void RestoreState(string blob) { }
    }

    internal static class MatchKit
    {
        public static MatchSetup Setup(Action<MatchSetup> tweak = null)
        {
            var s = new MatchSetup
            {
                Player = new WrestlerStats("p", "p"),
                Rival = new WrestlerStats("r", "r"),
                StartHype = 3,
                PlayerRole = RingPath.Face,
                RivalRole = RingPath.Heel,
                RoundLimit = 20
            };
            if (tweak != null) tweak(s);
            return s;
        }

        /// <summary>Обмін: перший ходить гравець (12 проти 2) або суперник; далі кидки за сценарієм.</summary>
        public static MatchEngine Engine(bool playerFirst, int[] rolls = null, Action<MatchSetup> tweak = null)
        {
            var all = new List<int>(playerFirst ? new[] { 12, 2 } : new[] { 2, 12 });
            if (rolls != null) all.AddRange(rolls);
            return new MatchEngine(Setup(tweak), new ScriptedDice(all.ToArray()));
        }

        /// <summary>Виконати дію до кінця: реакція — продати, докрутки немає.</summary>
        public static void Play(MatchEngine e, MatchActionId id, ReactionChoice react = ReactionChoice.Sell)
        {
            e.Perform(id);
            if (e.Phase == MatchPhase.AwaitReaction) e.React(react);
            if (e.Phase == MatchPhase.AwaitBoost) e.Commit(0);
        }

        public static ActionOption Opt(MatchEngine e, MatchActionId id) { return e.Describe(id); }
    }

    public class MatchTurnOrderTests
    {
        [Test]
        public void FirstMover_IsExchangeWinner()
        {
            Assert.AreEqual(MatchSide.Player, MatchKit.Engine(true).CurrentActor);
            Assert.AreEqual(MatchSide.Rival, MatchKit.Engine(false).CurrentActor);
        }

        [Test]
        public void Exchange_UsesTechnique_ThenMomentum_ThenPlayer()
        {
            var e1 = new MatchEngine(MatchKit.Setup(s => s.Player.Set(RingAttr.Technique, 1)), new ScriptedDice(7, 7));
            Assert.AreEqual(MatchSide.Player, e1.CurrentActor);
            var e2 = new MatchEngine(MatchKit.Setup(s => s.Rival.Set(RingAttr.Technique, 1)), new ScriptedDice(7, 7));
            Assert.AreEqual(MatchSide.Rival, e2.CurrentActor);
            var e3 = new MatchEngine(MatchKit.Setup(s => s.RivalStartMomentum = 2), new ScriptedDice(7, 7));
            Assert.AreEqual(MatchSide.Rival, e3.CurrentActor);
            var e4 = new MatchEngine(MatchKit.Setup(), new ScriptedDice(7, 7));
            Assert.AreEqual(MatchSide.Player, e4.CurrentActor);
        }

        [Test]
        public void Turns_StrictlyAlternate_RoundGrowsAfterPair()
        {
            var e = MatchKit.Engine(true);
            Assert.AreEqual(1, e.Round);
            var order = new List<MatchSide>();
            for (int i = 0; i < 6; i++) { order.Add(e.CurrentActor); e.EndTurn(); }
            CollectionAssert.AreEqual(new[]
            {
                MatchSide.Player, MatchSide.Rival, MatchSide.Player, MatchSide.Rival, MatchSide.Player, MatchSide.Rival
            }, order);
            Assert.AreEqual(4, e.Round);
        }

        [Test]
        public void RoundGrows_AlsoWhenRivalMovesFirst()
        {
            var e = MatchKit.Engine(false);
            e.EndTurn();
            Assert.AreEqual(MatchSide.Player, e.CurrentActor);
            Assert.AreEqual(1, e.Round);
            e.EndTurn();
            Assert.AreEqual(2, e.Round);
            Assert.AreEqual(MatchSide.Rival, e.CurrentActor);
        }

        [Test]
        public void Action_SpendsItsCost()
        {
            var e = MatchKit.Engine(true);
            Assert.AreEqual(3, e.Ap);
            MatchKit.Play(e, MatchActionId.Strike);
            Assert.AreEqual(2, e.Ap);
            MatchKit.Play(e, MatchActionId.Strike);
            Assert.AreEqual(1, e.Ap);
        }

        [Test]
        public void NotEnoughAp_ActionUnavailable_WithReason()
        {
            var e = MatchKit.Engine(true);
            MatchKit.Play(e, MatchActionId.Strike);
            MatchKit.Play(e, MatchActionId.Strike);
            Assert.AreEqual(1, e.Ap);
            var o = MatchKit.Opt(e, MatchActionId.Throw);
            Assert.IsFalse(o.Available);
            Assert.AreEqual(UnavailableReason.NotEnoughAp, o.Reason);
            Assert.Throws<InvalidOperationException>(() => e.Perform(MatchActionId.Throw));
        }

        [Test]
        public void SpendingAllAp_EndsTheTurn_WithoutCarry()
        {
            var e = MatchKit.Engine(true);
            MatchKit.Play(e, MatchActionId.Strike);
            MatchKit.Play(e, MatchActionId.Strike);
            MatchKit.Play(e, MatchActionId.Strike);
            Assert.AreEqual(MatchSide.Rival, e.CurrentActor);
            Assert.AreEqual(0, e.State.Player.CarryAp);
        }

        [Test]
        public void CatchBreath_CarriesAtMostOneAp()
        {
            var e = MatchKit.Engine(true);
            e.EndTurn();                 // 3 AP лишилось -> переноситься 1
            e.EndTurn();                 // суперник
            Assert.AreEqual(MatchSide.Player, e.CurrentActor);
            Assert.AreEqual(4, e.Ap);
            MatchKit.Play(e, MatchActionId.Strike);
            e.EndTurn(); e.EndTurn();
            Assert.AreEqual(4, e.Ap);
        }

        [Test]
        public void FailedRoll_EndsTheTurn_RestApBurns()
        {
            var e = MatchKit.Engine(true, new[] { 3 });
            MatchKit.Play(e, MatchActionId.Strike);
            Assert.AreEqual(MatchSide.Rival, e.CurrentActor);
            Assert.AreEqual(1, e.State.Player.Damage, "контрудар: собі +1");
            Assert.AreEqual(3, e.Ap);
            e.EndTurn();
            Assert.AreEqual(3, e.Ap, "згорілі AP не переносяться");
        }

        [Test]
        public void Stunned_HasOneApLessOnNextTurn_ThenRecovers()
        {
            var e = MatchKit.Engine(true);
            e.State.Rival.Posture = Posture.Stunned;
            e.EndTurn();
            Assert.AreEqual(2, e.Ap);
            e.EndTurn();
            Assert.AreEqual(Posture.Standing, e.State.Rival.Posture);
        }

        [Test]
        public void PendingRoll_ShowsDiceBeforeConsequences_AndBoostRaisesTier()
        {
            var e = MatchKit.Engine(true, new[] { 5 }, s => s.PlayerStartMomentum = 3);
            e.Perform(MatchActionId.Strike);
            Assert.AreEqual(MatchPhase.AwaitBoost, e.Phase);
            var pr = e.PendingRoll;
            Assert.AreEqual(RollTier.Miss, pr.Tier);
            Assert.AreEqual(2, pr.PointsToPartial);
            Assert.AreEqual(0, pr.PointsToStrong, "5 імпульсу немає");
            Assert.IsTrue(pr.CanBoost);
            Assert.AreEqual(0, e.State.Rival.Damage, "наслідків ще немає");
            Assert.Throws<ArgumentException>(() => e.Commit(1), "1 очко ступінь не піднімає");
            var ev = e.Commit(2);
            Assert.AreEqual(1, e.State.Rival.Damage);
            Assert.AreEqual(1, e.State.Player.Momentum);
            Assert.AreEqual(2, ev[0].BoostSpent);
            Assert.AreEqual(MatchSide.Player, e.CurrentActor, "після докрутки це вже не провал");
        }

        [Test]
        public void Calls_OutOfPhase_Throw()
        {
            var e = MatchKit.Engine(true);
            Assert.Throws<InvalidOperationException>(() => e.Commit(0));
            Assert.Throws<InvalidOperationException>(() => e.React(ReactionChoice.Sell));
            e.Perform(MatchActionId.Strike);
            Assert.Throws<InvalidOperationException>(() => e.EndTurn());
            Assert.Throws<InvalidOperationException>(() => e.Perform(MatchActionId.Strike));
        }

        [Test]
        public void Chances_AreShownBeforeTheRoll_WithAllModifiers()
        {
            var e = MatchKit.Engine(true, null, s => s.Player.Set(RingAttr.Strength, 2));
            var o = MatchKit.Opt(e, MatchActionId.Strike);
            Assert.IsTrue(o.HasRoll);
            Assert.AreEqual(2, o.Modifier);
            CollectionAssert.AreEqual(TwoD6.ChancesOf36(2), o.Chances);
            e.State.Player.Damage = 6;
            Assert.AreEqual(1, MatchKit.Opt(e, MatchActionId.Strike).Modifier, "виснажений -1");
            e.State.Player.Damage = 10;
            Assert.AreEqual(0, MatchKit.Opt(e, MatchActionId.Strike).Modifier, "на межі -2");
        }

        [Test]
        public void ActionList_CoversEveryActionOnce_WithCosts()
        {
            var e = MatchKit.Engine(true);
            var list = e.GetAvailableActions();
            Assert.AreEqual(17, list.Count);
            Assert.AreEqual(2, list[(int)MatchActionId.Throw].Cost);
            Assert.AreEqual(3, list[(int)MatchActionId.Finisher].Cost);
            Assert.IsTrue(list[(int)MatchActionId.CatchBreath].Available);
        }
    }
}
