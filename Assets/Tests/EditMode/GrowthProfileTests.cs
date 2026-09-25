using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public class GrowthProfileTests
    {
        private static SkillGrowthProfile Profile()
        {
            var g = new SkillGrowthProfile();
            g.SetWeight(SkillType.Ranged, 3);
            g.SetWeight(SkillType.Melee, 2);
            g.SetWeight(SkillType.Tactics, 1);
            return g;
        }

        [Test]
        public void AllocatePoints_DistributesExactTotal()
        {
            var gained = Profile().AllocatePoints(12);
            Assert.AreEqual(12, gained.TotalPoints, "Сумма выданных очков должна точно равняться запрошенной");
        }

        [Test]
        public void AllocatePoints_RespectsWeightOrder()
        {
            var gained = Profile().AllocatePoints(60);
            Assert.Greater(gained[SkillType.Ranged], gained[SkillType.Melee]);
            Assert.Greater(gained[SkillType.Melee], gained[SkillType.Tactics]);
        }

        [Test]
        public void AllocatePoints_NoWeights_ReturnsEmpty()
        {
            var gained = new SkillGrowthProfile().AllocatePoints(10);
            Assert.AreEqual(0, gained.TotalPoints);
        }

        /// <summary>
        /// Рівень роздає очки СКІЛІВ і лише їх: атрибути за US-2.1 піднімає
        /// один аугмент. Якщо це правило колись потече, ламатися почне
        /// тут, а не в балансі пізньої гри.
        /// </summary>
        [Test]
        public void LevelUp_GrowsSkills_ButNeverAttributes()
        {
            var cfg = new BalanceConfig();
            var arch = new CompanionArchetype("sold", "Боец");
            arch.SetAttribute(AttributeType.Strength, 6);
            arch.SetSkill(SkillType.Ranged, 2);
            arch.SetGrowth(SkillType.Ranged, 1);

            var comp = arch.CreateInstance("sold_1", cfg);
            comp.GainXp(10000, cfg);

            Assert.Greater(comp.Level, 1, "опыта хватило на уровни");
            Assert.Greater(comp.Skill(SkillType.Ranged), 2, "скил вырос");
            Assert.AreEqual(6, comp.Attribute(AttributeType.Strength), "атрибут не трогали");
        }

        /// <summary>Стеля шкали тримає: надлишок згорає, а не тече за межу.</summary>
        [Test]
        public void LevelUp_StopsAtSkillCeiling()
        {
            var cfg = new BalanceConfig();
            var arch = new CompanionArchetype("sold", "Боец");
            arch.SetSkill(SkillType.Ranged, 9);
            arch.SetGrowth(SkillType.Ranged, 1);

            var comp = arch.CreateInstance("sold_1", cfg);
            comp.GainXp(1000000, cfg);

            Assert.AreEqual(cfg.MaxSkillLevel, comp.Skill(SkillType.Ranged));
        }
    }
}
