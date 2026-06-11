using Game.Core.Balance;
using Game.Core.Combat;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Очередь ходов (US-3.2) и жизненный цикл состояний (US-3.7): DoT в начале
    /// хода носителя, длительность тикает в конце его хода, Воля сокращает при
    /// наложении (мин 1).
    /// </summary>
    public class TurnAndStatusTests
    {
        private static CombatUnit U(string id, Side side, int init, int hp = 10, int resolve = 0)
        {
            var p = new UnitProfile
            {
                DisplayName = id, MaxHp = hp, MaxAp = 8, Accuracy = 70,
                Initiative = init, Resolve = resolve
            };
            return new CombatUnit(id, side, p, null);
        }

        [Test]
        public void TurnOrder_DescendingInitiative_StableTies()
        {
            var a = U("a", Side.Player, 8);
            var b = U("b", Side.Enemy, 5);
            var c = U("c", Side.Player, 5); // та же инициатива, добавлен после b
            var d = U("d", Side.Enemy, 3);
            var turns = new TurnSystem(new[] { a, b, c, d });

            CollectionAssert.AreEqual(new[] { a, b, c, d }, (System.Collections.ICollection)turns.Order);
        }

        [Test]
        public void Round_IncrementsOnWrap()
        {
            var turns = new TurnSystem(new[] { U("a", Side.Player, 5), U("b", Side.Enemy, 3) });
            Assert.AreEqual(1, turns.Round);
            turns.Advance();
            Assert.AreEqual(1, turns.Round);
            turns.Advance(); // оборот очереди
            Assert.AreEqual(2, turns.Round);
        }

        private static (CombatState cs, CombatUnit actor, CombatUnit other) TwoPlusDummy()
        {
            var map = new GridMap(12, 1);
            var cs = new CombatState(map, new BalanceConfig(), new ScriptedRng());
            var actor = U("actor", Side.Player, 10);
            var other = U("other", Side.Player, 5);
            var dummy = U("dummy", Side.Enemy, 0, hp: 50); // бой «идёт», тестам не мешает
            cs.AddUnit(actor, new GridPos(0, 0));
            cs.AddUnit(other, new GridPos(2, 0));
            cs.AddUnit(dummy, new GridPos(11, 0));
            cs.Begin(); // ход actor
            return (cs, actor, other);
        }

        [Test]
        public void Suppression_LastsThroughOwnTurn_ExpiresAfterIt()
        {
            var (cs, _, other) = TwoPlusDummy();
            cs.ApplyStatus(other, StatusType.Suppressed);
            Assert.IsTrue(other.HasStatus(StatusType.Suppressed));

            cs.EndTurn(); // ход переходит к other
            Assert.AreSame(other, cs.Current);
            Assert.IsTrue(other.HasStatus(StatusType.Suppressed), "действует во время собственного хода");
            Assert.AreEqual(2, cs.MoveCostPerTile(other)); // ceil(1 × 1.5)

            cs.EndTurn(); // конец хода other — состояние спадает
            Assert.IsFalse(other.HasStatus(StatusType.Suppressed));
            Assert.AreEqual(1, cs.MoveCostPerTile(other));
        }

        [Test]
        public void Bleeding_TicksAtStartOfOwnTurn_DurationAtEnd()
        {
            var (cs, _, other) = TwoPlusDummy();
            cs.ApplyStatus(other, StatusType.Bleeding); // длительность 3, DoT 2 (True)
            Assert.AreEqual(3, other.GetStatus(StatusType.Bleeding).RemainingTurns);

            cs.EndTurn(); // начало хода other → тик DoT
            Assert.AreEqual(8, other.Hp);
            Assert.AreEqual(3, other.GetStatus(StatusType.Bleeding).RemainingTurns, "длительность тикает в конце хода");

            cs.EndTurn(); // конец хода other
            Assert.AreEqual(2, other.GetStatus(StatusType.Bleeding).RemainingTurns);
        }

        [Test]
        public void Resolve_ShortensDuration_MinimumOne()
        {
            var map = new GridMap(5, 1);
            var cs = new CombatState(map, new BalanceConfig(), new ScriptedRng());
            var tough = U("tough", Side.Player, 10, resolve: 9); // 9 / 3 = −3 хода
            var dummy = U("dummy", Side.Enemy, 0);
            cs.AddUnit(tough, new GridPos(0, 0));
            cs.AddUnit(dummy, new GridPos(4, 0));
            cs.Begin();

            cs.ApplyStatus(tough, StatusType.Bleeding); // база 3 − 3 → мин 1
            Assert.AreEqual(1, tough.GetStatus(StatusType.Bleeding).RemainingTurns);
        }

        [Test]
        public void Reapply_RefreshesDuration_NoStacking()
        {
            var (cs, _, other) = TwoPlusDummy();
            cs.ApplyStatus(other, StatusType.Bleeding);
            cs.EndTurn(); // тик DoT (−2 HP)
            cs.EndTurn(); // длительность → 2

            cs.ApplyStatus(other, StatusType.Bleeding); // освежение до 3, второй экземпляр не вешается
            Assert.AreEqual(3, other.GetStatus(StatusType.Bleeding).RemainingTurns);
            int count = 0;
            foreach (var s in other.Statuses) if (s.Type == StatusType.Bleeding) count++;
            Assert.AreEqual(1, count);
        }
    }
}
