using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Health;
using Game.Core.Stats;
using Game.Core.Traits;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>Единый пайплайн модификаторов (US-18.2): без двойного счёта.</summary>
    public class ModifierAggregatorTests
    {
        [Test]
        public void FlatAndPercent_Combine()
        {
            var mods = new List<StatModifier>
            {
                new StatModifier(DerivedStat.MaxHp, 4, ModMode.Flat),
                new StatModifier(DerivedStat.MaxHp, 50, ModMode.PercentAdd) // +50%
            };
            // (10 + 4) * 1.5 = 21
            Assert.AreEqual(21, ModifierAggregator.Resolve(DerivedStat.MaxHp, 10, mods));
        }

        [Test]
        public void ModifierForOtherStat_IsIgnored()
        {
            var mods = new List<StatModifier> { new StatModifier(DerivedStat.Defense, 100, ModMode.Flat) };
            Assert.AreEqual(10, ModifierAggregator.Resolve(DerivedStat.MaxHp, 10, mods));
        }

        [Test]
        public void Companion_AggregatesTraitAndScar_EachOnce()
        {
            var cfg = new BalanceConfig();
            var c = new Companion("t", new AttributeBlock(3, 3, 3, 3), 4); // база HP = 4 + 3*2 = 10
            c.Traits.TryAdd(new Trait("v", "V").WithCombat(DerivedStat.MaxHp, 2));
            c.Scars.Add(new Scar("s", "S").WithCombat(DerivedStat.MaxHp, 1));
            Assert.AreEqual(13, c.GetDerived(DerivedStat.MaxHp, cfg)); // 10 + 2 + 1, не 10 + 6
        }

        [Test]
        public void NeverGoesNegative()
        {
            var mods = new List<StatModifier> { new StatModifier(DerivedStat.Accuracy, -999, ModMode.Flat) };
            Assert.AreEqual(0, ModifierAggregator.Resolve(DerivedStat.Accuracy, 50, mods));
        }
    }
}
