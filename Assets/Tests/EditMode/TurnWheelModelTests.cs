using System.Collections.Generic;
using System.Linq;
using Game.Core.Combat;
using Game.Core.Session;
using Game.Core.Session.Views;
using Game.Gameplay.UI;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Колесо черги ходів (Поправка №14.5; власник, 29.09.2026: «не треба,
    /// краще показати по черзі хто ходить, колесо чи щось таке»). Модель
    /// без рушія: що саме стоїть на колесі — твердження тестів, а не
    /// враження від знімка.
    /// </summary>
    public class TurnWheelModelTests
    {
        private static BattleUnitView U(string id, string side = "Player", bool downed = false,
            bool out_ = false, params string[] statuses)
            => new BattleUnitView { Id = id, Side = side, IsDowned = downed, IsOutOfBattle = out_, Statuses = statuses };

        private static BattleView View(int round, string current, IList<string> order, params BattleUnitView[] units)
            => new BattleView { Round = round, CurrentUnitId = current, InitiativeOrder = order.ToList(), Units = units };

        private static List<string> Ids(TurnWheelModel m) => m.Slots.Select(s => s.UnitId).ToList();

        [Test]
        public void Wheel_StartsWithCurrent_ThenRestOfRound_ThenNextRoundFromTheTop()
        {
            var view = View(2, "c", new[] { "a", "b", "c", "d" },
                U("a"), U("b", "Enemy"), U("c"), U("d", "Enemy"));

            var wheel = TurnWheelModel.Build(view);

            CollectionAssert.AreEqual(new[] { "c", "d", "a", "b", "c", "d" }, Ids(wheel));
            Assert.IsTrue(wheel.Slots[0].IsCurrent);
            Assert.IsFalse(wheel.Slots.Skip(1).Any(s => s.IsCurrent), "поточний — лише один, перший");
            Assert.AreEqual(2, wheel.NextRoundStartsAt, "межа раунду — перед «a», першим у черзі");
            Assert.AreEqual(new[] { 2, 2, 3, 3, 3, 3 }, wheel.Slots.Select(s => s.Round).ToArray());
            Assert.AreEqual(2, wheel.CurrentRound);
        }

        [Test]
        public void Wheel_HidesThoseOutOfBattle_ButKeepsTheDowned()
        {
            var view = View(1, "a", new[] { "a", "dead", "b" },
                U("a"), U("dead", "Enemy", out_: true), U("b", "Player", downed: true));

            var wheel = TurnWheelModel.Build(view);

            CollectionAssert.DoesNotContain(Ids(wheel), "dead", "загиблий чи винесений з бою на колесі не стоїть");
            var downed = wheel.Slots.First(s => s.UnitId == "b");
            Assert.IsTrue(downed.IsDowned, "упалий лишається — перекресленим");
        }

        [Test]
        public void Stunned_SkipsOnlyItsNearestTurn()
        {
            var view = View(1, "a", new[] { "a", "s", "b" },
                U("a"), U("s", "Enemy", false, false, "Stunned"), U("b"));

            var wheel = TurnWheelModel.Build(view);

            var stunned = wheel.Slots.Where(s => s.UnitId == "s").ToList();
            Assert.AreEqual(2, stunned.Count);
            Assert.IsTrue(stunned[0].SkipsTurn, "найближчий хід оглушеного — «пропускає»");
            Assert.IsFalse(stunned[1].SkipsTurn, "оглушення знімає рівно один хід (CombatState.BeginTurn)");
        }

        [Test]
        public void CurrentUnit_IsNeverMarkedAsSkipping()
        {
            // Хід уже почався: стан знято на початку ходу, пропуск видно по нулю ОД.
            var view = View(1, "s", new[] { "s", "a" }, U("s", "Player", false, false, "Stunned"), U("a", "Enemy"));

            var wheel = TurnWheelModel.Build(view);

            Assert.IsFalse(wheel.Slots[0].SkipsTurn);
        }

        [Test]
        public void Wheel_IsCappedAtEightSlots_SoItStaysReadable()
        {
            var ids = Enumerable.Range(0, 9).Select(i => "u" + i).ToArray();
            var view = View(1, "u0", ids, ids.Select(id => U(id)).ToArray());

            var wheel = TurnWheelModel.Build(view);

            Assert.AreEqual(TurnWheelModel.DefaultMaxSlots, wheel.Slots.Count);
            Assert.AreEqual(-1, wheel.NextRoundStartsAt, "8 з 9 — межа наступного раунду не влазить");
        }

        [Test]
        public void SlotAngles_CurrentOnTop_ThenClockwise()
        {
            Assert.AreEqual(-90f, TurnWheelModel.SlotAngleDegrees(0, 6), 0.001f);
            Assert.AreEqual(-30f, TurnWheelModel.SlotAngleDegrees(1, 6), 0.001f, "наступний — праворуч-униз від верху");
        }

        [Test]
        public void RealTrainingBattle_WheelStartsAtTheUnitWhoseTurnItIs()
        {
            var s = new GameSession();
            s.NewTrainingBattle(new TrainingBattleOptions { HitRule = HitRuleKind.Threshold });
            s.CombatEndTurn();

            var view = s.GetBattleView();
            var wheel = TurnWheelModel.Build(view);

            Assert.IsNotEmpty(wheel.Slots);
            Assert.AreEqual(view.CurrentUnitId, wheel.Slots[0].UnitId);
            Assert.IsTrue(wheel.Slots[0].IsCurrent);
            foreach (var slot in wheel.Slots)
                Assert.IsTrue(view.Units.Any(u => u.Id == slot.UnitId && !u.IsOutOfBattle));
        }
    }
}
