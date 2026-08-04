using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>Перки (US-3.10): авторазблокировка по порогам скилов, модификаторы без двойного счёта.</summary>
    public class PerkTests
    {
        private static Companion Make(int ranged = 0)
        {
            var c = new Companion("c", new AttributeBlock(3, 3, 3, 3), 4);
            if (ranged > 0) c.Skills.Set(SkillType.Ranged, ranged);
            return c;
        }

        private static List<PerkDefinition> Catalog() => new List<PerkDefinition>
        {
            new PerkDefinition("p_acc", "Твёрдая рука", SkillType.Ranged, 2).With(DerivedStat.Accuracy, 5)
        };

        [Test]
        public void RefreshPerks_UnlocksByThreshold()
        {
            var low = Make(ranged: 1);
            low.RefreshPerks(Catalog());
            Assert.AreEqual(0, low.Perks.Count);

            var high = Make(ranged: 2);
            high.RefreshPerks(Catalog());
            Assert.AreEqual(1, high.Perks.Count);
        }

        [Test]
        public void PerkModifiers_FlowIntoDerivedStats()
        {
            var cfg = new BalanceConfig();
            var c = Make(ranged: 2); // база Accuracy = 50 + 3*2 = 56
            Assert.AreEqual(56, c.GetDerived(DerivedStat.Accuracy, cfg));

            c.RefreshPerks(Catalog());
            Assert.AreEqual(61, c.GetDerived(DerivedStat.Accuracy, cfg)); // +5 от перка
        }

        [Test]
        public void RefreshPerks_Idempotent_NoDoubleCount()
        {
            var cfg = new BalanceConfig();
            var c = Make(ranged: 2);
            c.RefreshPerks(Catalog());
            c.RefreshPerks(Catalog()); // повторный пересчёт не задваивает
            Assert.AreEqual(1, c.Perks.Count);
            Assert.AreEqual(61, c.GetDerived(DerivedStat.Accuracy, cfg));
        }

        [Test]
        public void SkillGrowth_UnlocksPerkAfterRefresh()
        {
            var cfg = new BalanceConfig { SkillPointsPerLevel = 1 };
            var c = Make(ranged: 1);
            c.RefreshPerks(Catalog());
            Assert.AreEqual(0, c.Perks.Count);

            c.GainXp(Game.Core.Balance.ProgressionMath.XpToNext(1, cfg), cfg); // +1 очко
            Assert.IsTrue(c.SpendSkillPoint(SkillType.Ranged, cfg));            // Ranged 1→2
            c.RefreshPerks(Catalog());
            Assert.AreEqual(1, c.Perks.Count);
        }
    }
}
