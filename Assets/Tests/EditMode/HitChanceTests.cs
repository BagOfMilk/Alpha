using System.Linq;
using Game.Core.Balance;
using Game.Core.Combat;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Надійне число (склад формули, клампи) — спільне для обох правил
    /// влучання (R1). Перенесено з архівної бойової лінії, адаптовано на
    /// BalanceConfig.Combat (R14: числа бою — своя секція).
    /// </summary>
    public class HitChanceTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();

        private static int Chance(int acc, bool suppressed = false, int def = 0,
                                  CoverType cover = CoverType.None, bool ignoreCover = false,
                                  int distance = 3, int optimal = 6, BalanceConfig cfg = null)
            => HitChanceCalculator.Compute(acc, suppressed, def, cover, ignoreCover, distance, optimal, cfg ?? Cfg);

        [Test]
        public void Compute_BaseMinusDefense()
            => Assert.AreEqual(65, Chance(70, def: 5));

        [Test]
        public void Compute_CoverPenalties_HalfAndFull()
        {
            Assert.AreEqual(50, Chance(70, cover: CoverType.Half)); // −20
            Assert.AreEqual(30, Chance(70, cover: CoverType.Full)); // −40
        }

        [Test]
        public void Compute_MeleeIgnoresCover()
            => Assert.AreEqual(70, Chance(70, cover: CoverType.Full, ignoreCover: true, distance: 1, optimal: 1));

        [Test]
        public void Compute_DistanceBeyondOptimal_Penalized()
            => Assert.AreEqual(55, Chance(70, distance: 9, optimal: 6)); // 3 тайла × −5

        [Test]
        public void Compute_Suppression_LowersAccuracy()
            => Assert.AreEqual(55, Chance(70, suppressed: true)); // −15

        [Test]
        public void Compute_ClampsToBounds()
        {
            Assert.AreEqual(Cfg.Combat.HitChanceMin, Chance(10, def: 50));
            Assert.AreEqual(Cfg.Combat.HitChanceMax, Chance(200));
        }

        // ---- PercentRule: Miss/Graze/Hit/Crit через ScriptedDiceRoller ----

        private static CombatUnit Attacker(int critChance = 0) => new CombatUnit("atk", Side.Player,
            new UnitProfile { DisplayName = "A", MaxHp = 10, MaxAp = 8, Accuracy = 70, CritChance = critChance }, null);

        private static CombatUnit Defender() => new CombatUnit("tgt", Side.Enemy,
            new UnitProfile { DisplayName = "T", MaxHp = 10, MaxAp = 8, Accuracy = 0 }, null);

        [Test]
        public void PercentRule_HitWithinChance_GrazeInBand_MissBeyond()
        {
            var rule = new PercentRule(Cfg);
            var a = Attacker();
            var t = Defender();

            Assert.AreEqual(AttackOutcome.Hit, rule.Resolve(a, t, 50, new ScriptedDiceRoller(0.49, 0.99)));
            Assert.AreEqual(AttackOutcome.Graze, rule.Resolve(a, t, 50, new ScriptedDiceRoller(0.64))); // 14 ≤ 15
            Assert.AreEqual(AttackOutcome.Miss, rule.Resolve(a, t, 50, new ScriptedDiceRoller(0.66)));  // 16 > 15
        }

        [Test]
        public void PercentRule_VeryHighChance_CannotFullyMiss()
        {
            var cfg = new BalanceConfig();
            cfg.Combat.GrazeThresholdPercent = 5;
            var rule = new PercentRule(cfg);
            var a = Attacker();
            var t = Defender();

            // Шанс ≥85 → найгірше можливе — Graze, навіть на поганому роллі.
            Assert.AreEqual(AttackOutcome.Graze, rule.Resolve(a, t, 90, new ScriptedDiceRoller(0.999)));
            // Контроль: при шансі нижче підлоги той самий ролл — промах.
            Assert.AreEqual(AttackOutcome.Miss, rule.Resolve(a, t, 80, new ScriptedDiceRoller(0.999)));
        }

        [Test]
        public void PercentRule_Hit_RollsSecondTimeForCrit()
        {
            var rule = new PercentRule(Cfg);
            var a = Attacker(critChance: 50);
            var t = Defender();

            // Перший ролл 0.1 < 50% → Hit-гілка; другий ролл 0.4 < 50% крита → Crit.
            Assert.AreEqual(AttackOutcome.Crit, rule.Resolve(a, t, 50, new ScriptedDiceRoller(0.1, 0.4)));
            // Другий ролл 0.9 ≥ 50% крита → звичайний Hit, без третього кидка.
            Assert.AreEqual(AttackOutcome.Hit, rule.Resolve(a, t, 50, new ScriptedDiceRoller(0.1, 0.9)));
        }

        // ---- ThresholdRule: повністю детерміновано, roller не чіпає ----

        [Test]
        public void ThresholdRule_CarryPerFighter_NoRollerCalls()
        {
            // Без кубика (29.09.2026, «Усі відсотки мають працювати»): лічильник бійця
            // стартує з 50, удар додає шанс, на 100 — влучання; від 85 — крит.
            var cfg = new BalanceConfig();
            var rule = new ThresholdRule(cfg);
            var roller = new ScriptedDiceRoller(); // порожня черга — перевіримо, що її не торкнуться
            CombatUnit Fresh() => new CombatUnit("u", Side.Player, new UnitProfile { MaxHp = 1, MaxAp = 1 }, null);

            Assert.AreEqual(AttackOutcome.Miss, rule.Resolve(Fresh(), null, 49, roller), "50+49 < 100");
            Assert.AreEqual(AttackOutcome.Hit, rule.Resolve(Fresh(), null, 50, roller), "перший удар від 50 % влучає");
            Assert.AreEqual(AttackOutcome.Crit, rule.Resolve(Fresh(), null, 85, roller));

            var archer = Fresh();
            var thirty = Enumerable.Range(0, 10).Select(_ => rule.Resolve(archer, null, 30, roller)).ToList();
            Assert.AreEqual(3, thirty.Count(o => o != AttackOutcome.Miss), "30 % — рівно 3 влучання з 10");
            CollectionAssert.DoesNotContain(thirty, AttackOutcome.Graze);

            Assert.AreEqual(0, roller.Streams.Count, "ThresholdRule не зобов'язаний кликати IDiceRoller жодного разу (R1)");
        }

        [Test]
        public void ThresholdRule_AcceptsNullRoller()
        {
            var rule = new ThresholdRule(Cfg);
            Assert.DoesNotThrow(() => rule.Resolve(null, null, 90, null));
        }
    }
}
