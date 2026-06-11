using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Stats;
using Game.Core.Traits;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>Производные из атрибутов + прохождение модификаторов через агрегатор.</summary>
    public class DerivedStatTests
    {
        private static Companion Make(int str, int agi, int wits, int will)
            => new Companion("t", new AttributeBlock(str, agi, wits, will), 4);

        [Test]
        public void Derived_BasicMath_NoModifiers()
        {
            var cfg = new BalanceConfig();
            var d = Make(8, 5, 4, 6).EffectiveDerived(cfg);

            Assert.AreEqual(20, d[DerivedStat.MaxHp]);        // 4 + 8*2
            Assert.AreEqual(9,  d[DerivedStat.ActionPoints]); // round(8 + 5*0.2)
            Assert.AreEqual(60, d[DerivedStat.Accuracy]);     // 50 + 5*2
            Assert.AreEqual(5,  d[DerivedStat.Defense]);      // 0 + 5*1
            Assert.AreEqual(9,  d[DerivedStat.Initiative]);   // 5*1 + 4*1
            Assert.AreEqual(12, d[DerivedStat.Carry]);        // 4 + 8*1
            Assert.AreEqual(10, d[DerivedStat.CritChance]);   // 5 + 5*1
            Assert.AreEqual(6,  d[DerivedStat.Resolve]);      // 6*1
        }

        [Test]
        public void Hp_StaysInSmallNumberBand()
        {
            var cfg = new BalanceConfig();
            Assert.AreEqual(6,  Make(1, 0, 0, 0).EffectiveDerived(cfg)[DerivedStat.MaxHp]);  // 4 + 1*2
            Assert.AreEqual(20, Make(8, 0, 0, 0).EffectiveDerived(cfg)[DerivedStat.MaxHp]);  // 4 + 8*2
        }

        [Test]
        public void Ap_StaysInEightToTenBand()
        {
            var cfg = new BalanceConfig();
            Assert.AreEqual(8,  Make(0, 0, 0, 0).GetDerived(DerivedStat.ActionPoints, cfg));
            Assert.AreEqual(10, Make(0, 10, 0, 0).GetDerived(DerivedStat.ActionPoints, cfg));
        }

        [Test]
        public void TraitCombatModifier_FlowsThroughAggregator()
        {
            var cfg = new BalanceConfig();
            var c = Make(3, 6, 4, 3);                 // база Accuracy = 50 + 6*2 = 62
            c.Traits.TryAdd(new Trait("t", "T").WithCombat(DerivedStat.Accuracy, 5));
            Assert.AreEqual(67, c.GetDerived(DerivedStat.Accuracy, cfg));
        }
    }
}
