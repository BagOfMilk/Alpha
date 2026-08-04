using System.Collections.Generic;
using Game.Core;
using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Прокачка: XP даёт очки скилов в пул, игрок тратит вручную (классов нет,
    /// респека нет; US-2.2/5.1) + пререквизиты перков и планировщик билда (US-2.3).
    /// </summary>
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
            Assert.IsTrue(c.SpendSkillPoint(SkillType.Ranged, cfg));
            Assert.AreEqual(1, c.GetSkill(SkillType.Ranged));
            Assert.AreEqual(1, c.UnspentSkillPoints);
        }

        [Test]
        public void SpendSkillPoint_WithoutPoints_Fails()
        {
            var c = Make();
            Assert.IsFalse(c.SpendSkillPoint(SkillType.Ranged, new BalanceConfig()));
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

        // ---- Пререквизиты перков (US-2.2: «порог скила + пререквизиты») ----
        [Test]
        public void Perk_Prerequisite_FixpointAndOrphan()
        {
            var a = new PerkDefinition("a", "A", SkillType.Ranged, 2).With(DerivedStat.Accuracy, 5);
            var b = new PerkDefinition("b", "B", SkillType.Ranged, 2).Requires("a").With(DerivedStat.CritChance, 5);
            var orphan = new PerkDefinition("orphan", "X", SkillType.Ranged, 2).Requires("missing");

            var c = Make();
            c.Skills.Set(SkillType.Ranged, 2);
            c.RefreshPerks(new List<PerkDefinition> { orphan, b, a }); // b раньше a — порядок не важен

            Assert.IsTrue(c.HasPerk("a"));
            Assert.IsTrue(c.HasPerk("b"), "пререквизит-цепочка разрешается до фикс-пойнта");
            Assert.IsFalse(c.HasPerk("orphan"), "без открытого пререквизита перк заперт");
        }

        [Test]
        public void Perk_CheckModifiers_FlowIntoChecks()
        {
            var c = Make();
            c.Skills.Set(SkillType.Medicine, 2);
            c.RefreshPerks(DefaultContent.PerkCatalog()); // «Сортировка раненых»: +1 к Медицине
            Assert.AreEqual(1, c.CheckModifierFor(SkillType.Medicine),
                "утилита/соц-перки дают пассив к проверкам (US-3.11)");
        }

        // ---- Планировщик билда (US-2.3): превью БЕЗ мутации ----
        [Test]
        public void BuildPlanner_Preview_ShowsPerksAndDeltas_WithoutMutation()
        {
            var cfg = new BalanceConfig { SkillPointsPerLevel = 1 };
            var c = Make();
            c.Skills.Set(SkillType.Melee, 1);
            c.RefreshPerks(DefaultContent.PerkCatalog());
            c.GainXp(ProgressionMath.XpToNext(1, cfg), cfg); // 1 нераспределённое очко

            var preview = BuildPlanner.PreviewSkillPoint(c, SkillType.Melee, cfg, DefaultContent.PerkCatalog());

            Assert.IsTrue(preview.CanSpend);
            Assert.AreEqual(2, preview.NewLevel);
            Assert.IsTrue(preview.Irreversible, "явное предупреждение: респека нет");
            Assert.IsTrue(preview.PerksUnlocked.Exists(p => p.Id == "thick_hide"), "покажет открывающийся перк");
            Assert.AreEqual(2, preview.DerivedDeltas[DerivedStat.MaxHp], "и дельту статов от него");

            // Превью ничего не мутирует:
            Assert.AreEqual(1, c.GetSkill(SkillType.Melee));
            Assert.IsFalse(c.HasPerk("thick_hide"));
            Assert.AreEqual(1, c.UnspentSkillPoints);
        }

        [Test]
        public void BuildPlanner_CheckDelta_CountsPointAndPerkBonus()
        {
            var cfg = new BalanceConfig();
            var c = Make();
            c.Skills.Set(SkillType.Medicine, 1);
            var preview = BuildPlanner.PreviewSkillPoint(c, SkillType.Medicine, cfg, DefaultContent.PerkCatalog());
            Assert.AreEqual(2, preview.CheckValueDelta, "+1 само очко и +1 от «Сортировки раненых»");
        }

        [Test]
        public void BuildPlanner_DoesNotAttribute_AlreadyEarnedPerks()
        {
            // Перки достижимы ТЕКУЩИМИ уровнями, но RefreshPerks не звали (stale).
            var cfg = new BalanceConfig();
            var c = Make();
            c.Skills.Set(SkillType.Tactics, 2);  // light_step уже заработан
            c.Skills.Set(SkillType.Medicine, 3); // triage уже заработан

            var survival = BuildPlanner.PreviewSkillPoint(c, SkillType.Survival, cfg, DefaultContent.PerkCatalog());
            Assert.IsFalse(survival.PerksUnlocked.Exists(p => p.Id == "light_step"),
                "чужой перк не приписывается очку в несвязанный скил");

            var medicine = BuildPlanner.PreviewSkillPoint(c, SkillType.Medicine, cfg, DefaultContent.PerkCatalog());
            Assert.IsFalse(medicine.PerksUnlocked.Exists(p => p.Id == "triage"),
                "уже достигнутый порог — не заслуга нового очка");
            Assert.AreEqual(1, medicine.CheckValueDelta, "дельта проверки честная: только само очко");
        }

        [Test]
        public void SpendSkillPoint_WithCatalog_RefreshesPerksImmediately()
        {
            var cfg = new BalanceConfig { SkillPointsPerLevel = 1 };
            var c = Make();
            c.Skills.Set(SkillType.Melee, 1);
            c.GainXp(ProgressionMath.XpToNext(1, cfg), cfg);

            Assert.IsTrue(c.SpendSkillPoint(SkillType.Melee, cfg, DefaultContent.PerkCatalog()));
            Assert.IsTrue(c.HasPerk("thick_hide"), "production-путь траты очка сразу открывает перк");
        }

        // ---- Потолок скила за кампанию (итерация 18) ----
        [Test]
        public void SpendSkillPoint_AtSkillMax_Fails_AndKeepsPoint()
        {
            var cfg = new BalanceConfig();
            var c = Make();
            c.Skills.Set(SkillType.Ranged, cfg.SkillMax);
            c.GainXp(5000, cfg);
            int pool = c.UnspentSkillPoints;

            Assert.IsFalse(c.SpendSkillPoint(SkillType.Ranged, cfg, DefaultContent.PerkCatalog()),
                "выше потолка кампании скил не растёт (иначе точность вылезает за кламп)");
            Assert.AreEqual(pool, c.UnspentSkillPoints, "очко не сгорело");
            Assert.AreEqual(cfg.SkillMax, c.GetSkill(SkillType.Ranged));

            var preview = BuildPlanner.PreviewSkillPoint(c, SkillType.Ranged, cfg, DefaultContent.PerkCatalog());
            Assert.IsTrue(preview.AtCap, "превью и трата — один источник истины");
            Assert.IsFalse(preview.CanSpend);
        }

        [Test]
        public void SkillMax_IsNotBelowCreationCap()
        {
            var cfg = new BalanceConfig();
            Assert.GreaterOrEqual(cfg.SkillMax, cfg.CreationSkillMax,
                "иначе создание персонажа сможет превысить потолок кампании");
        }

        // ---- Кривая XP против реальных источников кампании (итерация 18) ----
        [Test]
        public void XpCurve_CampaignBudget_ReachesMidLevels()
        {
            var cfg = new BalanceConfig(); // дефолтная кривая — та, по которой играют
            var c = Make();

            // Эталонный прогон док (BALANCE.md §1): 10 победных вылазок (80 каждому
            // участнику) + весь авторский пул квестов (~400) = 1200 XP.
            c.GainXp(10 * cfg.XpPerExpeditionVictory + 400, cfg);

            // Границы двусторонние: односторонний ассерт не заметил бы ни отката
            // дефолтов, ни случайного ускорения кривой — и док разошёлся бы с кодом.
            Assert.AreEqual(5, c.Level, "эталонный прогон = 5-й уровень (док BALANCE.md §1)");
            Assert.AreEqual(4 * cfg.SkillPointsPerLevel, c.UnspentSkillPoints,
                "12 очков — этого хватает на пороги перков/приёмов 4–5");
        }

        [Test]
        public void SkillGrowth_UnlocksTopTierAbility()
        {
            var cfg = new BalanceConfig();
            var c = new Companion("m", new AttributeBlock(3, 5, 3, 3), 4) { DisplayName = "Стрелок" };
            c.Skills.Set(SkillType.Ranged, 4); // потолок создания
            c.GainXp(5000, cfg);               // кампания прокачала

            var before = Game.Core.Combat.CombatUnit.FromCompanion(
                c, DefaultContent.Rifle(), cfg, DefaultContent.AbilityCatalog());
            Assert.IsFalse(HasAbility(before, "mark_target"), "на 4 верхушка ветки закрыта");

            Assert.IsTrue(c.SpendSkillPoint(SkillType.Ranged, cfg, DefaultContent.PerkCatalog()));
            var after = Game.Core.Combat.CombatUnit.FromCompanion(
                c, DefaultContent.Rifle(), cfg, DefaultContent.AbilityCatalog());
            Assert.IsTrue(HasAbility(after, "mark_target"),
                "трата очка открывает приём порога 5 — прокачка меняет бой");
        }

        [Test]
        public void Technician_HasHackDrone_OutOfTheBox()
        {
            var cfg = new BalanceConfig();
            var tech = DefaultContent.Technician().CreateInstance("technician", cfg);
            var unit = Game.Core.Combat.CombatUnit.FromCompanion(
                tech, DefaultContent.Pistol(), cfg, DefaultContent.AbilityCatalog());

            Assert.IsTrue(HasAbility(unit, "hack_drone"),
                "штатный техник обязан уметь взлом робота без узкого билда (US-3.11)");
        }

        private static bool HasAbility(Game.Core.Combat.CombatUnit unit, string abilityId)
        {
            foreach (var a in unit.Abilities)
                if (a.Id == abilityId) return true;
            return false;
        }
    }
}
