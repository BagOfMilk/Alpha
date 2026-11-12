using System;
using System.Collections.Generic;
using System.Linq;
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
                RoundLimit = MatchRules.ActionLimit
            };
            if (tweak != null) tweak(s);
            return s;
        }

        /// <summary>Обмін: першим діє гравець (12 проти 2) або суперник; далі кидки за сценарієм.</summary>
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

        /// <summary>
        /// Борця вже накривали й тримали в больовому: бонус «свіжого» (MatchRules.FirstPinBonus / FirstHoldBonus) не діє. Для тестів,
        /// що перевіряють інше; сам бонус — MatchActionsTests.FreshWrestler_FirstCoverAndFirstHold_GetBonus_OnlyOnce.
        /// </summary>
        public static void NotFresh(Fighter f) { f.PinsFaced = 1; f.HoldsFaced = 1; }

        public static ActionOption Find(IReadOnlyList<ActionOption> list, MatchActionId id)
        {
            foreach (var o in list) if (o.Id == id) return o;
            return null;
        }
    }

    /// <summary>
    /// Ланцюг ініціативи (рішення власника 08.11.2026, TRPG_DESIGN §4.5): Обмін на старті; 10+ — ініціатива лишається, 7–9 —
    /// лишається з ускладненням, 6 і менше — переходить; «Сили» — гальмо ланцюга.
    /// </summary>
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
        public void Exchange_IsLogged_WithBothTotals()
        {
            var e = new MatchEngine(MatchKit.Setup(s => s.Player.Set(RingAttr.Technique, 2)), new ScriptedDice(8, 9));
            var ex = e.State.Log[0];
            Assert.AreEqual(MatchEventKind.Exchange, ex.Kind);
            Assert.AreEqual(MatchSide.Player, ex.Actor, "8+2 проти 9");
            Assert.AreEqual(10, ex.Total);
            Assert.AreEqual(9, ex.OtherTotal);
            Assert.AreEqual(0, e.Chain);
        }

        [Test]
        public void Strong_KeepsInitiative_ChainGrows()
        {
            var e = MatchKit.Engine(true, new[] { 10, 11 });
            MatchKit.Play(e, MatchActionId.Strike);
            Assert.AreEqual(MatchSide.Player, e.CurrentActor);
            Assert.AreEqual(1, e.Chain);
            MatchKit.Play(e, MatchActionId.Strike);
            Assert.AreEqual(MatchSide.Player, e.CurrentActor, "10+ — ініціатива лишається");
            Assert.AreEqual(2, e.Chain);
            Assert.AreEqual(3, e.Round, "кожна дія — +1");
            Assert.AreEqual(0, e.State.InitiativeChanges);
            Assert.AreEqual(2, e.State.Log[e.State.Log.Count - 1].ChainIndex);
        }

        [Test]
        public void Partial_KeepsInitiative_WithComplication()
        {
            var e = MatchKit.Engine(true, new[] { 8 });
            MatchKit.Play(e, MatchActionId.Strike);
            Assert.AreEqual(MatchSide.Player, e.CurrentActor, "7–9 — ініціатива лишається");
            Assert.AreEqual(1, e.State.Rival.Damage);
            Assert.AreEqual(MatchRules.StartStamina - 1 - 1, e.State.Player.Stamina, "ціна удару 1 і ускладнення «−1 сила»");
        }

        [Test]
        public void Miss_PassesInitiative_WithTurnEndEvent()
        {
            var e = MatchKit.Engine(true, new[] { 10, 3 });
            MatchKit.Play(e, MatchActionId.Strike);
            MatchKit.Play(e, MatchActionId.Strike);
            Assert.AreEqual(MatchSide.Rival, e.CurrentActor, "6 і менше — ініціатива переходить");
            Assert.AreEqual(0, e.Chain);
            Assert.AreEqual(1, e.State.InitiativeChanges);
            Assert.AreEqual(1, e.State.Player.Damage, "контрудар: собі +1");
            var te = e.State.Log.Last(ev => ev.Kind == MatchEventKind.TurnEnd);
            Assert.AreEqual(MatchSide.Player, te.Actor, "TurnEnd — того, хто втратив ініціативу");
            Assert.IsNull(te.MoveKey);
        }

        [Test]
        public void CatchBreath_GivesStamina_AndPassesInitiative()
        {
            var e = MatchKit.Engine(true);
            e.State.Player.Stamina = 4;
            e.EndTurn();
            Assert.AreEqual(4 + MatchRules.CatchBreathStamina, e.State.Player.Stamina);
            Assert.AreEqual(MatchSide.Rival, e.CurrentActor);
            e.State.Rival.Stamina = 9;
            e.EndTurn();
            Assert.AreEqual(MatchRules.MaxStamina, e.State.Rival.Stamina, "не вище за максимум");
        }

        [Test]
        public void EveryMove_Costs_DefenderRegainsOne()
        {
            var e = MatchKit.Engine(true, new[] { 10, 10 });
            e.State.Rival.Stamina = 5;
            MatchKit.Play(e, MatchActionId.Strike);
            Assert.AreEqual(MatchRules.StartStamina - MatchRules.Cost(MatchActionId.Strike), e.State.Player.Stamina);
            Assert.AreEqual(6, e.State.Rival.Stamina, "захисник +1 після прийому");
            MatchKit.Play(e, MatchActionId.Throw);
            Assert.AreEqual(MatchRules.StartStamina - 1 - 2, e.State.Player.Stamina, "кидок коштує 2");
            Assert.AreEqual(7, e.State.Rival.Stamina);
        }

        [Test]
        public void Winded_MinusOne_RiskyAndFinisherNeedFullCost()
        {
            var e = MatchKit.Engine(true);
            e.State.Player.Stamina = 0;
            var o = MatchKit.Opt(e, MatchActionId.Strike);
            Assert.IsTrue(o.Available, "удар можна й на нулі");
            Assert.IsTrue(o.Winded);
            Assert.AreEqual(-MatchRules.WindedPenalty, o.Modifier);
            e.State.Player.Stamina = MatchRules.Cost(MatchActionId.TopRope) - 1;
            o = MatchKit.Opt(e, MatchActionId.TopRope);
            Assert.IsFalse(o.Available);
            Assert.AreEqual(UnavailableReason.NotEnoughStamina, o.Reason);
            e.State.Player.Stamina = MatchRules.Cost(MatchActionId.TopRope);
            Assert.IsTrue(MatchKit.Opt(e, MatchActionId.TopRope).Available, "кута для трюку більше не треба");
            Assert.IsFalse(MatchKit.Opt(e, MatchActionId.Strike).Winded);
        }

        [Test]
        public void Stunned_RecoversWhenInitiativeArrives()
        {
            var e = MatchKit.Engine(true);
            e.State.Rival.Posture = Posture.Stunned;
            var evs = e.EndTurn();
            Assert.AreEqual(MatchSide.Rival, e.CurrentActor);
            Assert.AreEqual(Posture.Standing, e.State.Rival.Posture);
            Assert.IsTrue(evs.Any(ev => ev.Effects.Any(f => f.Kind == MatchEffectKind.Stand && f.Who == MatchSide.Rival)));
        }

        [Test]
        public void RoundCountsEveryAction_InitiativeAlternatesOnCatchBreath()
        {
            var e = MatchKit.Engine(true);
            Assert.AreEqual(1, e.Round);
            var order = new List<MatchSide>();
            for (int i = 0; i < 6; i++) { order.Add(e.CurrentActor); e.EndTurn(); }
            CollectionAssert.AreEqual(new[]
            {
                MatchSide.Player, MatchSide.Rival, MatchSide.Player, MatchSide.Rival, MatchSide.Player, MatchSide.Rival
            }, order);
            Assert.AreEqual(7, e.Round);
            Assert.AreEqual(6, e.State.InitiativeChanges);
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
        public void ChancesShown_AreTheChancesRolled()
        {
            // показаний модифікатор — той, з яким кидаємо (як у XCOM: що бачиш, те й кидаєш)
            var e = MatchKit.Engine(true, new[] { 6 }, s => s.Player.Set(RingAttr.Strength, 1));
            e.State.Player.Stamina = 0;
            int shown = MatchKit.Opt(e, MatchActionId.Strike).Modifier;
            e.Perform(MatchActionId.Strike);
            Assert.AreEqual(shown, e.PendingRoll.Modifier);
            Assert.AreEqual(0, shown, "сила +1, без «Сил» −1");
        }

        [Test]
        public void ActionList_CoversEveryActionOnce_WithCosts()
        {
            var e = MatchKit.Engine(true);
            var list = e.GetAvailableActions();
            // усі дії рушія, крім «Променя надії» (той пропонується окремо)
            Assert.AreEqual(Enum.GetValues(typeof(MatchActionId)).Length - 1, list.Count);
            Assert.AreEqual(list.Count, list.Select(o => o.Id).Distinct().Count());
            Assert.AreEqual(1, MatchKit.Find(list, MatchActionId.Strike).Cost);
            Assert.AreEqual(2, MatchKit.Find(list, MatchActionId.Throw).Cost);
            Assert.AreEqual(3, MatchKit.Find(list, MatchActionId.TopRope).Cost);
            Assert.AreEqual(4, MatchKit.Find(list, MatchActionId.Finisher).Cost);
            Assert.AreEqual(1, MatchKit.Find(list, MatchActionId.LeaveRing).Cost);
            Assert.AreEqual(0, MatchKit.Find(list, MatchActionId.ReturnToRing).Cost);
            Assert.AreEqual(2, MatchKit.Find(list, MatchActionId.Weapon).Cost);
            Assert.IsTrue(MatchKit.Find(list, MatchActionId.CatchBreath).Available);
            Assert.IsNull(MatchKit.Find(list, MatchActionId.HopeSpot), "промінь надії пропонується окремо");
        }
    }
}
