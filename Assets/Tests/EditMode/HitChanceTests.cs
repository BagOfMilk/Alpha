using Game.Core.Balance;
using Game.Core.Combat;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>Надёжный % (US-3.3): состав формулы, клампы, граза-полоса, пол высокого шанса.</summary>
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
            Assert.AreEqual(Cfg.HitChanceMin, Chance(10, def: 50));
            Assert.AreEqual(Cfg.HitChanceMax, Chance(200));
        }

        [Test]
        public void Roll_HitWithinChance_GrazeInBand_MissBeyond()
        {
            Assert.AreEqual(HitOutcome.Hit, HitChanceCalculator.Roll(50, new ScriptedRng(50), Cfg));
            Assert.AreEqual(HitOutcome.Graze, HitChanceCalculator.Roll(50, new ScriptedRng(65), Cfg)); // 15 ≤ 15
            Assert.AreEqual(HitOutcome.Miss, HitChanceCalculator.Roll(50, new ScriptedRng(66), Cfg));  // 16 > 15
        }

        [Test]
        public void Roll_VeryHighChance_CannotFullyMiss()
        {
            // Сужаем гразу до 5%, чтобы изолировать правило пола: шанс ≥85 → худшее граза.
            var cfg = new BalanceConfig { GrazeThresholdPercent = 5 };
            Assert.AreEqual(HitOutcome.Graze, HitChanceCalculator.Roll(90, new ScriptedRng(100), cfg));
            // Контроль: при шансе ниже пола тот же ролл — промах.
            Assert.AreEqual(HitOutcome.Miss, HitChanceCalculator.Roll(80, new ScriptedRng(100), cfg));
        }
    }
}
