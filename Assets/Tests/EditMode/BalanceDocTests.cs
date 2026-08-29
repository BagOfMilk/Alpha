using Game.Core.Balance;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Сверяет числа, напечатанные в docs/BALANCE.md, с формулами кода.
    ///
    /// Зачем отдельный файл: остальные тесты прогрессии берут пороги вызовом самой же
    /// тестируемой функции, поэтому мимо них проходит любое расхождение с докой.
    /// Здесь значения прибиты литералами — если поменять XpBase или XpExponent,
    /// тест покраснеет и покажет, какие строки таблицы пересчитать.
    /// </summary>
    public class BalanceDocTests
    {
        private static BalanceConfig Defaults() => new BalanceConfig();

        // docs/BALANCE.md §1, колонка «XpToNext».
        [TestCase(1, 100)]
        [TestCase(2, 283)]
        [TestCase(3, 520)]
        [TestCase(4, 800)]
        [TestCase(5, 1118)]
        [TestCase(6, 1470)]
        [TestCase(7, 1852)]
        [TestCase(8, 2263)]
        [TestCase(9, 2700)]
        [TestCase(10, 3162)]
        public void XpToNext_MatchesBalanceDoc(int level, int expected)
        {
            Assert.AreEqual(expected, ProgressionMath.XpToNext(level, Defaults()));
        }

        // docs/BALANCE.md §1, колонка «суммарно до уровня».
        [TestCase(1, 0L)]
        [TestCase(2, 100L)]
        [TestCase(3, 383L)]
        [TestCase(4, 903L)]
        [TestCase(5, 1703L)]
        [TestCase(6, 2821L)]
        [TestCase(7, 4291L)]
        [TestCase(8, 6143L)]
        [TestCase(9, 8406L)]
        [TestCase(10, 11106L)]
        public void TotalXpForLevel_MatchesBalanceDoc(int level, long expected)
        {
            Assert.AreEqual(expected, ProgressionMath.TotalXpForLevel(level, Defaults()));
        }

        /// <summary>
        /// «Суммарно» обязано быть суммой предыдущих порогов. Ловит случай, когда
        /// одну колонку пересчитали, а вторую забыли — именно так таблица и разъехалась.
        /// </summary>
        [Test]
        public void TotalXpForLevel_IsSumOfThresholds()
        {
            var cfg = Defaults();
            long running = 0;
            for (int level = 1; level <= 10; level++)
            {
                Assert.AreEqual(running, ProgressionMath.TotalXpForLevel(level, cfg),
                    $"суммарный опыт до уровня {level}");
                running += ProgressionMath.XpToNext(level, cfg);
            }
        }

        /// <summary>
        /// docs/BALANCE.md §7 обещает, что ассет баланса переносит все числа конфига.
        /// Тесты не видят сборку Game.Gameplay, поэтому сверяем то, что доступно:
        /// у BalanceConfig не должно появиться поля без дефолта из доки.
        /// </summary>
        [Test]
        public void BalanceConfig_DefaultsMatchDoc()
        {
            var cfg = Defaults();
            Assert.AreEqual(100.0, cfg.XpBase, "XpBase");
            Assert.AreEqual(1.5, cfg.XpExponent, "XpExponent");
            Assert.AreEqual(20, cfg.MaxLevel, "MaxLevel");
            Assert.AreEqual(3, cfg.StatPointsPerLevel, "StatPointsPerLevel");
            Assert.AreEqual(20, cfg.RoleXpPerCycle, "RoleXpPerCycle");
            Assert.AreEqual(5, cfg.AptitudeMatchThreshold, "AptitudeMatchThreshold");
            Assert.AreEqual(1.5, cfg.WellSuitedXpMultiplier, "WellSuitedXpMultiplier");
            Assert.AreEqual(1.0, cfg.GlobalProductionMultiplier, "GlobalProductionMultiplier");
            Assert.AreEqual(0.5, cfg.InjuredProductionMultiplier, "InjuredProductionMultiplier");
            Assert.AreEqual(5.0, cfg.BaseHealingPerCycle, "BaseHealingPerCycle");
            Assert.AreEqual(1, cfg.FoodUpkeepPerCompanion, "FoodUpkeepPerCompanion");
        }
    }
}
