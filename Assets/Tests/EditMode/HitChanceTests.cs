using Game.Core.Balance;
using Game.Core.Combat;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Надёжное число (состав формулы, клампы) — общее для обоих правил
    /// попадания (R1). Перенесено из архивной боевой линии, адаптировано на
    /// BalanceConfig.Combat (R14: числа боя — своя секция).
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

            // Шанс ≥85 → худшее возможное — Graze, даже на плохом ролле.
            Assert.AreEqual(AttackOutcome.Graze, rule.Resolve(a, t, 90, new ScriptedDiceRoller(0.999)));
            // Контроль: при шансе ниже пола тот же ролл — промах.
            Assert.AreEqual(AttackOutcome.Miss, rule.Resolve(a, t, 80, new ScriptedDiceRoller(0.999)));
        }

        [Test]
        public void PercentRule_Hit_RollsSecondTimeForCrit()
        {
            var rule = new PercentRule(Cfg);
            var a = Attacker(critChance: 50);
            var t = Defender();

            // Первый ролл 0.1 < 50% → Hit-ветка; второй ролл 0.4 < 50% крита → Crit.
            Assert.AreEqual(AttackOutcome.Crit, rule.Resolve(a, t, 50, new ScriptedDiceRoller(0.1, 0.4)));
            // Второй ролл 0.9 ≥ 50% крита → обычный Hit, без третьего броска.
            Assert.AreEqual(AttackOutcome.Hit, rule.Resolve(a, t, 50, new ScriptedDiceRoller(0.1, 0.9)));
        }

        // ---- ThresholdRule: полностью детерминировано, roller не трогает ----

        [Test]
        public void ThresholdRule_BandsByMarginFromBaseline_NoRollerCalls()
        {
            var cfg = new BalanceConfig(); // Baseline=50, GrazeBand=15, CritBand=35
            var rule = new ThresholdRule(cfg);
            var roller = new ScriptedDiceRoller(); // пустая очередь — если тронут, вернёт 0.5, но мы проверим, что не тронут

            Assert.AreEqual(AttackOutcome.Miss, rule.Resolve(null, null, 49, roller));
            Assert.AreEqual(AttackOutcome.Graze, rule.Resolve(null, null, 50, roller));
            Assert.AreEqual(AttackOutcome.Graze, rule.Resolve(null, null, 64, roller));
            Assert.AreEqual(AttackOutcome.Hit, rule.Resolve(null, null, 65, roller));
            Assert.AreEqual(AttackOutcome.Hit, rule.Resolve(null, null, 84, roller));
            Assert.AreEqual(AttackOutcome.Crit, rule.Resolve(null, null, 85, roller));

            Assert.AreEqual(0, roller.Streams.Count, "ThresholdRule не обязан звать IDiceRoller ни разу (R1)");
        }

        [Test]
        public void ThresholdRule_AcceptsNullRoller()
        {
            var rule = new ThresholdRule(Cfg);
            Assert.DoesNotThrow(() => rule.Resolve(null, null, 90, null));
        }
    }
}
