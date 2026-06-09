using Game.Core.Characters;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public class GrowthProfileTests
    {
        [Test]
        public void AllocatePoints_DistributesExactTotal()
        {
            var g = new GrowthProfile();
            g.SetWeight(StatType.Aim, 3);
            g.SetWeight(StatType.Health, 2);
            g.SetWeight(StatType.Will, 1);

            var block = g.AllocatePoints(12);
            int sum = block.Get(StatType.Aim) + block.Get(StatType.Health) + block.Get(StatType.Will);
            Assert.AreEqual(12, sum, "Сумма выданных очков должна точно равняться запрошенной");
        }

        [Test]
        public void AllocatePoints_RespectsWeightOrder()
        {
            var g = new GrowthProfile();
            g.SetWeight(StatType.Aim, 3);
            g.SetWeight(StatType.Health, 2);
            g.SetWeight(StatType.Will, 1);

            var block = g.AllocatePoints(60);
            Assert.Greater(block.Get(StatType.Aim), block.Get(StatType.Health));
            Assert.Greater(block.Get(StatType.Health), block.Get(StatType.Will));
        }

        [Test]
        public void AllocatePoints_NoWeights_ReturnsEmpty()
        {
            var g = new GrowthProfile();
            var block = g.AllocatePoints(10);
            Assert.AreEqual(0, block.Values.Count);
        }
    }
}
