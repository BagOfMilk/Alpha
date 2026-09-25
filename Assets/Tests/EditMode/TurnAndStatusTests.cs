using Game.Core.Balance;
using Game.Core.Combat;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Черга ходів і життєвий цикл станів: DoT на початку ходу носія,
    /// тривалість тікає в кінці його ходу, Resolve (похідна
    /// StatusDurationReduction) скорочує при накладенні (мін 1). Перенесено з
    /// архівної бойової лінії; не прив'язано до конкретного правила влучання —
    /// використовується ThresholdRule як найпростіший конструктор.
    /// </summary>
    public class TurnAndStatusTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();

        private static CombatUnit U(string id, Side side, int init, int hp = 10, int resolve = 0)
        {
            var p = new UnitProfile
            {
                DisplayName = id, MaxHp = hp, MaxAp = 8, Accuracy = 70,
                Initiative = init, Resolve = resolve, MoveApPerTile = 1
            };
            return new CombatUnit(id, side, p, null);
        }

        [Test]
        public void TurnOrder_DescendingInitiative_StableTies()
        {
            var a = U("a", Side.Player, 8);
            var b = U("b", Side.Enemy, 5);
            var c = U("c", Side.Player, 5); // та сама ініціатива, доданий після b
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
            turns.Advance(); // оберт черги
            Assert.AreEqual(2, turns.Round);
        }

        private static CombatState NewCombat(GridMap map) => new CombatState(map, Cfg, new ThresholdRule(Cfg), null);

        private static (CombatState cs, CombatUnit actor, CombatUnit other) TwoPlusDummy()
        {
            var map = new GridMap(12, 1);
            var cs = NewCombat(map);
            var actor = U("actor", Side.Player, 10);
            var other = U("other", Side.Player, 5);
            var dummy = U("dummy", Side.Enemy, 0, hp: 50); // бій «триває», тестам не заважає
            cs.AddUnit(actor, new GridPos(0, 0));
            cs.AddUnit(other, new GridPos(2, 0));
            cs.AddUnit(dummy, new GridPos(11, 0));
            cs.Begin(); // хід actor
            return (cs, actor, other);
        }

        [Test]
        public void Suppression_LastsThroughOwnTurn_ExpiresAfterIt()
        {
            var (cs, _, other) = TwoPlusDummy();
            cs.ApplyStatus(other, StatusType.Suppressed);
            Assert.IsTrue(other.HasStatus(StatusType.Suppressed));

            cs.EndTurn(); // хід переходить до other
            Assert.AreSame(other, cs.Current);
            Assert.IsTrue(other.HasStatus(StatusType.Suppressed), "действует во время собственного хода");
            Assert.AreEqual(2, cs.MoveCostPerTile(other)); // ceil(1 × 1.5)

            cs.EndTurn(); // кінець ходу other — стан спадає
            Assert.IsFalse(other.HasStatus(StatusType.Suppressed));
            Assert.AreEqual(1, cs.MoveCostPerTile(other));
        }

        [Test]
        public void Bleeding_TicksAtStartOfOwnTurn_DurationAtEnd()
        {
            var (cs, _, other) = TwoPlusDummy();
            cs.ApplyStatus(other, StatusType.Bleeding); // тривалість 3, DoT 2 (True)
            Assert.AreEqual(3, other.GetStatus(StatusType.Bleeding).RemainingTurns);

            cs.EndTurn(); // початок ходу other → тік DoT
            Assert.AreEqual(8, other.Hp);
            Assert.AreEqual(3, other.GetStatus(StatusType.Bleeding).RemainingTurns, "длительность тикает в конце хода");

            cs.EndTurn(); // кінець ходу other
            Assert.AreEqual(2, other.GetStatus(StatusType.Bleeding).RemainingTurns);
        }

        [Test]
        public void Resolve_ShortensDuration_MinimumOne()
        {
            var map = new GridMap(5, 1);
            var cs = NewCombat(map);
            var tough = U("tough", Side.Player, 10, resolve: 9); // 9 / 3 = −3 ходи (ResolvePerStatusTurnReduction=3)
            var dummy = U("dummy", Side.Enemy, 0);
            cs.AddUnit(tough, new GridPos(0, 0));
            cs.AddUnit(dummy, new GridPos(4, 0));
            cs.Begin();

            cs.ApplyStatus(tough, StatusType.Bleeding); // база 3 − 3 → мін 1
            Assert.AreEqual(1, tough.GetStatus(StatusType.Bleeding).RemainingTurns);
        }

        [Test]
        public void Reapply_RefreshesDuration_NoStacking()
        {
            var (cs, _, other) = TwoPlusDummy();
            cs.ApplyStatus(other, StatusType.Bleeding);
            cs.EndTurn(); // тік DoT (−2 HP)
            cs.EndTurn(); // тривалість → 2

            cs.ApplyStatus(other, StatusType.Bleeding); // освіження до 3, другий екземпляр не вішається
            Assert.AreEqual(3, other.GetStatus(StatusType.Bleeding).RemainingTurns);
            int count = 0;
            foreach (var s in other.Statuses) if (s.Type == StatusType.Bleeding) count++;
            Assert.AreEqual(1, count);
        }

        [Test]
        public void Stunned_LosesTurnAp_StatusConsumed()
        {
            var (cs, _, other) = TwoPlusDummy();
            cs.ApplyStatus(other, StatusType.Stunned);
            Assert.IsTrue(other.HasStatus(StatusType.Stunned));

            cs.EndTurn(); // початок ходу other: Оглушення з'їдає AP
            Assert.AreSame(other, cs.Current);
            Assert.AreEqual(0, other.Ap, "оглушённый пропускает ход");
            Assert.IsFalse(other.HasStatus(StatusType.Stunned), "статус расходуется");

            cs.EndTurn(); // dummy
            cs.EndTurn(); // actor
            cs.EndTurn(); // знову other
            Assert.AreSame(other, cs.Current);
            Assert.AreEqual(8, other.Ap, "следующий ход — полные AP");
        }

        [Test]
        public void Burning_TicksFireDot_WithVulnerabilityMultiplier()
        {
            var map = new GridMap(5, 1);
            var cs = NewCombat(map);
            var torch = U("torch", Side.Player, 10);
            var mutant = U("mutant", Side.Enemy, 5, hp: 20);
            mutant.Profile.Resists = new ResistProfile().With(DamageType.Fire, 1.5); // вразливий до вогню
            cs.AddUnit(torch, new GridPos(0, 0));
            cs.AddUnit(mutant, new GridPos(4, 0));
            cs.Begin();

            cs.ApplyStatus(mutant, StatusType.Burning); // Підпал: DoT вогнем
            cs.EndTurn(); // початок ходу мутанта: тік 2 × 1.5 = 3
            Assert.AreEqual(17, mutant.Hp, "Поджог тикает типизированным огнём с множителем уязвимости");
        }
    }
}
