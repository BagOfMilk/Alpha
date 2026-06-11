using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>Прокачка: XP даёт очки скилов в пул, игрок тратит вручную (классов нет, респека нет; US-2.2/5.1).</summary>
    public class CompanionProgressionTests
    {
        private static Companion Make() => new Companion("c", new AttributeBlock(3, 3, 3, 3), 4);

        [Test]
        public void GainXp_LevelUp_GrantsSkillPointsToPool()
        {
            var cfg = new BalanceConfig { SkillPointsPerLevel = 3 };
            var c = Make();
            c.GainXp(ProgressionMath.XpToNext(1, cfg), cfg);
            Assert.AreEqual(2, c.Level);
            Assert.AreEqual(3, c.UnspentSkillPoints);
        }

        [Test]
        public void SpendSkillPoint_RaisesSkill_AndConsumesPool()
        {
            var cfg = new BalanceConfig { SkillPointsPerLevel = 2 };
            var c = Make();
            c.GainXp(ProgressionMath.XpToNext(1, cfg), cfg); // +2 очка
            Assert.IsTrue(c.SpendSkillPoint(SkillType.Ranged));
            Assert.AreEqual(1, c.GetSkill(SkillType.Ranged));
            Assert.AreEqual(1, c.UnspentSkillPoints);
        }

        [Test]
        public void SpendSkillPoint_WithoutPoints_Fails()
        {
            var c = Make();
            Assert.IsFalse(c.SpendSkillPoint(SkillType.Ranged));
            Assert.AreEqual(0, c.GetSkill(SkillType.Ranged));
        }

        [Test]
        public void Skills_NoRespec_GameplayOnlyRaises()
        {
            var c = Make();
            c.Skills.Set(SkillType.Melee, 3);
            c.Skills.Set(SkillType.Melee, 1); // Set допускает авторскую раздачу/сейв...
            Assert.AreEqual(1, c.GetSkill(SkillType.Melee));
            // ...но игровой путь — только Raise (повышение), понижения в геймплее нет.
            c.Skills.Raise(SkillType.Melee, 2);
            Assert.AreEqual(3, c.GetSkill(SkillType.Melee));
        }
    }
}
