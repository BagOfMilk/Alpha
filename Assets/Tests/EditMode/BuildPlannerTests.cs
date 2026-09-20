using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Characters.Build;
using Game.Core.Characters.Perks;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Планировщик билда (US-2.3). Проверяется главное свойство: до
    /// подтверждения не меняется ничего, а к подтверждению игрок приходит,
    /// увидев эффект, пороги и то, что откроется.
    /// </summary>
    public class BuildPlannerTests
    {
        private static BalanceConfig Cfg() => new BalanceConfig();

        private static Companion Hero(BalanceConfig cfg, int ranged = 2)
        {
            var arch = new CompanionArchetype("hero", "Протагонист");
            arch.SetAttribute(AttributeType.Strength, 5);
            arch.SetAttribute(AttributeType.Agility, 5);
            arch.SetAttribute(AttributeType.Wits, 4);
            arch.SetAttribute(AttributeType.Will, 4);
            arch.SetSkill(SkillType.Ranged, ranged);
            return arch.CreateInstance("hero_1", cfg);
        }

        private static PerkDefinition Steady()
            => new PerkDefinition("steady", "Твёрдая рука", SkillType.Ranged, 4)
                .WithModifier(StatKey.Accuracy, 5);

        private static PerkDefinition Deadeye()
            => new PerkDefinition("deadeye", "Меткий глаз", SkillType.Ranged, 4)
                .Requiring("steady").WithModifier(StatKey.CritChance, 5);

        [Test]
        public void Preview_ChangesNothing()
        {
            var cfg = Cfg();
            var hero = Hero(cfg);
            var plan = new BuildPlan().Invest(SkillType.Ranged, 2).TakePerk(Steady());

            BuildPlanner.Preview(hero, plan, 3, new List<PerkDefinition> { Steady() }, cfg);

            Assert.AreEqual(2, hero.Skill(SkillType.Ranged), "скил не тронут до подтверждения");
            Assert.AreEqual(0, hero.Perks.Count, "перк не взят до подтверждения");
        }

        [Test]
        public void Preview_ShowsSkillAndStatEffect()
        {
            var cfg = Cfg();
            var hero = Hero(cfg);
            var plan = new BuildPlan().Invest(SkillType.Ranged, 2).TakePerk(Steady());

            var p = BuildPlanner.Preview(hero, plan, 2, null, cfg);

            Assert.AreEqual(BuildPlanStatus.Ok, p.Status);
            Assert.AreEqual(1, p.Skills.Count);
            Assert.AreEqual(2, p.Skills[0].From);
            Assert.AreEqual(4, p.Skills[0].To);

            var accuracy = p.Stats.Find(s => s.Key == StatKey.Accuracy);
            Assert.AreEqual(5, accuracy.To - accuracy.From, 1e-9, "перк поднимает точность на 5");
        }

        /// <summary>
        /// Перк за порогом, до которого план как раз дотягивает. Без учёта
        /// плановых очков планировщик показал бы ложный отказ — и игрок решил
        /// бы, что вкладывать бессмысленно.
        /// </summary>
        [Test]
        public void PerkBehindThreshold_BecomesAvailableWithinTheSamePlan()
        {
            var cfg = Cfg();
            var hero = Hero(cfg, ranged: 3);
            var plan = new BuildPlan().Invest(SkillType.Ranged, 1).TakePerk(Steady());

            var p = BuildPlanner.Preview(hero, plan, 1, null, cfg);

            Assert.AreEqual(PerkAvailability.Available, p.Perks[0].Verdict);
            Assert.AreEqual(BuildPlanStatus.Ok, p.Status);
        }

        /// <summary>Перки одного плана могут быть пререквизитами друг друга.</summary>
        [Test]
        public void PerkChain_InsideOnePlan_IsAllowed()
        {
            var cfg = Cfg();
            var hero = Hero(cfg, ranged: 4);
            var plan = new BuildPlan().TakePerk(Steady()).TakePerk(Deadeye());

            var p = BuildPlanner.Preview(hero, plan, 0, null, cfg);

            Assert.AreEqual(PerkAvailability.Available, p.Perks[0].Verdict);
            Assert.AreEqual(PerkAvailability.Available, p.Perks[1].Verdict, "пререквизит взят этим же планом");
            Assert.AreEqual(BuildPlanStatus.Ok, p.Status);
        }

        /// <summary>«Что откроется» — это то, чего игрок не планировал.</summary>
        [Test]
        public void Unlocks_ListWhatTheInvestmentOpens()
        {
            var cfg = Cfg();
            var hero = Hero(cfg, ranged: 2);
            var plan = new BuildPlan().Invest(SkillType.Ranged, 2);

            var p = BuildPlanner.Preview(hero, plan, 2, new List<PerkDefinition> { Steady(), Deadeye() }, cfg);

            Assert.AreEqual(1, p.Unlocks.Count, "Твёрдая рука открывается, Меткий глаз — нет: нужен пререквизит");
            Assert.AreEqual("steady", p.Unlocks[0].Id);
        }

        [Test]
        public void NotEnoughPoints_IsRefused()
        {
            var cfg = Cfg();
            var plan = new BuildPlan().Invest(SkillType.Ranged, 5);
            var p = BuildPlanner.Preview(Hero(cfg), plan, 2, null, cfg);

            Assert.AreEqual(BuildPlanStatus.NotEnoughPoints, p.Status);
            Assert.IsFalse(p.CanCommit);
        }

        /// <summary>Потолок шкалы показывается, а не зажимается молча.</summary>
        [Test]
        public void AboveCeiling_IsShown_NotSilentlyClamped()
        {
            var cfg = Cfg();
            var hero = Hero(cfg, ranged: 9);
            var plan = new BuildPlan().Invest(SkillType.Ranged, 3);

            var p = BuildPlanner.Preview(hero, plan, 3, null, cfg);

            Assert.AreEqual(BuildPlanStatus.AboveSkillCeiling, p.Status);
            Assert.AreEqual(12, p.Skills[0].To, "показано настоящее намерение, а не обрезанное");
        }

        [Test]
        public void Commit_WithoutConfirmation_ChangesNothing()
        {
            var cfg = Cfg();
            var hero = Hero(cfg);
            var plan = new BuildPlan().Invest(SkillType.Ranged, 2);

            var status = BuildPlanner.Commit(hero, plan, 2, null, cfg, confirmedIrreversible: false);

            Assert.AreEqual(BuildPlanStatus.NotConfirmed, status);
            Assert.AreEqual(2, hero.Skill(SkillType.Ranged));
        }

        [Test]
        public void Commit_Confirmed_AppliesSkillsAndPerks()
        {
            var cfg = Cfg();
            var hero = Hero(cfg);
            var plan = new BuildPlan().Invest(SkillType.Ranged, 2).TakePerk(Steady());

            var status = BuildPlanner.Commit(hero, plan, 2, null, cfg, confirmedIrreversible: true);

            Assert.AreEqual(BuildPlanStatus.Ok, status);
            Assert.AreEqual(4, hero.Skill(SkillType.Ranged));
            Assert.IsTrue(hero.Perks.Has("steady"));
            Assert.IsTrue(plan.IsEmpty, "подтверждённый план израсходован");
        }

        /// <summary>Предупреждение о необратимости существует и непустое.</summary>
        [Test]
        public void IrreversibleWarning_IsNotEmpty()
        {
            StringAssert.Contains("необратим", BuildPreview.IrreversibleWarning);
        }
    }
}
