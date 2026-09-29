using System.Linq;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Scenes;
using Game.Core.Session;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Поправка №15.2 (класи-архетипи, рішення власника 29.09.2026: «скіли
    /// будут по классам... Клас = архетип»). 10 скілів лишаються — клас лише
    /// задає стартові скіли/роль і «рідний набір» прийомів; доступ до
    /// прийомів за класом у бою НЕ реалізований (це трек бою, Поправка №14) —
    /// перевіряється тут тільки, що дані класу правильні і клас видно
    /// гравцю, не бойова поведінка.
    /// </summary>
    public class ClassArchetypeTests
    {
        // ================= 1. Клас кожного іменного персонажа =================

        [Test]
        public void NamedCast_HasExpectedClass()
        {
            Assert.AreEqual(CompanionClass.Brawler, OpeningCast.Maksym().Class, "Максим — Рубака");
            Assert.AreEqual(CompanionClass.Shooter, OpeningCast.Myroslava().Class, "Мирослава — Стрілець");
            Assert.AreEqual(CompanionClass.Healer, OpeningCast.Zakhar().Class, "Захар — Знахар (переконання)");
            Assert.AreEqual(CompanionClass.Shooter, OpeningCast.Keeper().Class, "Дід Овсій — Стрілець (Виживання)");
            Assert.AreEqual(CompanionClass.Healer, OpeningCast.Healer().Class, "Гафія — Знахар");
            Assert.AreEqual(CompanionClass.Crafter, OpeningCast.Goban().Class, "Гобан-Сайр — Майстер");
            Assert.AreEqual(CompanionClass.Crafter, OpeningCast.Sindbad().Class, "Синдбад — Майстер");
        }

        // ================= 2. Протагоніст за передісторією =================

        [TestCase("warrior", CompanionClass.Brawler)]
        [TestCase("trader", CompanionClass.Crafter)]
        [TestCase("healer", CompanionClass.Healer)]
        public void Protagonist_ClassFollowsBackground(string backgroundId, CompanionClass expected)
        {
            var s = NewSessionThroughCreation(backgroundId);
            var sheet = s.GetCharacterSheet(GameSession.ProtagonistId);
            Assert.IsNotNull(sheet);
            Assert.AreEqual(expected, sheet.Class);
        }

        [Test]
        public void Protagonist_SkipCreation_DefaultsToWarriorClass()
        {
            var s = new GameSession();
            s.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold });
            var sheet = s.GetCharacterSheet(GameSession.ProtagonistId);
            Assert.IsNotNull(sheet);
            Assert.AreEqual(CompanionClass.Brawler, sheet.Class,
                "SkipCreation лишає дефолт «warrior» (CLAUDE.md) — клас мусить бути той самий, що й warrior дає явно");
        }

        // ================= 3. Клас у CompanionSummary =================

        [Test]
        public void RosterView_ExposesClass_ForNamedCompanions()
        {
            var s = NewSessionThroughCreation("warrior");
            var roster = s.GetRosterView();
            Assert.IsNotNull(roster?.Companions);

            var maksym = roster.Companions.First(c => c.Id == "maksym");
            Assert.AreEqual(CompanionClass.Brawler, maksym.Class);

            var myroslava = roster.Companions.First(c => c.Id == "myroslava");
            Assert.AreEqual(CompanionClass.Shooter, myroslava.Class);

            var zakhar = roster.Companions.First(c => c.Id == "zakhar");
            Assert.AreEqual(CompanionClass.Healer, zakhar.Class);
        }

        // ================= 4. Охоронець: найвищий скіл — серед головних скілів класу =================

        [Test]
        public void NamedCast_TopSkill_IsAmongItsClassMainSkills()
        {
            var s = NewSessionThroughCreation("warrior");
            string[] ids = { "maksym", "myroslava", "zakhar", "keeper", "healer", "goban", "sindbad" };

            foreach (var id in ids)
            {
                var sheet = s.GetCharacterSheet(id);
                Assert.IsNotNull(sheet, id);

                int topScore = sheet.Skills.Where(l => l.SkillKey != "tactics").Max(l => l.Score);
                var topKeys = sheet.Skills.Where(l => l.SkillKey != "tactics" && l.Score == topScore)
                    .Select(l => l.SkillKey).ToList();

                var mainSkillKeys = CompanionClasses.MainSkills(sheet.Class)
                    .Select(sk => Skills.KeyId(sk)).ToList();

                Assert.IsTrue(topKeys.Any(k => mainSkillKeys.Contains(k)),
                    $"{id}: найвищий скіл ({string.Join(",", topKeys)}={topScore}) не серед головних скілів класу " +
                    $"{sheet.Class} ({string.Join(",", mainSkillKeys)})");
            }
        }

        // ================= 5. Дані архетипу — цілісність =================

        [Test]
        public void NativeAbilityIds_AllExistInAbilityCatalog()
        {
            var catalogIds = DefaultCombatContent.AbilityCatalog().Select(a => a.Id).ToList();
            foreach (var cls in CompanionClasses.All)
                foreach (var id in CompanionClasses.NativeAbilityIds(cls))
                    Assert.IsTrue(catalogIds.Contains(id), $"{cls}: '{id}' відсутній у DefaultCombatContent.AbilityCatalog()");
        }

        [Test]
        public void MainSkills_NeverIncludeTactics()
        {
            foreach (var cls in CompanionClasses.All)
                Assert.IsFalse(CompanionClasses.MainSkills(cls).Contains(SkillType.Tactics),
                    $"{cls}: Tactics спільний скіл, не належить жодному класу");
        }

        // ================= 6. Сейв: клас переживає відновлення у свіжу сесію =================

        [Test]
        public void ProtagonistClass_SurvivesSave_IntoAFreshSession()
        {
            // trader -> Crafter, свідомо не Brawler (дефолт CharacterCard.Class):
            // якщо RestoreState мовчки не пише поле, тест ловить це напевно,
            // а не випадковим збігом з дефолтом.
            var s = NewSessionThroughCreation("trader");
            Assert.AreEqual(CompanionClass.Crafter, s.GetCharacterSheet(GameSession.ProtagonistId).Class);

            string blob = s.SaveState(0);

            var fresh = new GameSession();
            fresh.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold });
            fresh.RestoreFromBlob(blob);

            var sheet = fresh.GetCharacterSheet(GameSession.ProtagonistId);
            Assert.IsNotNull(sheet);
            Assert.AreEqual(CompanionClass.Crafter, sheet.Class,
                "Клас протагоніста (RosterAdapter, дев'яте поле «ros=») мусить пережити SaveState/RestoreFromBlob у СВІЖУ сесію");
        }

        [Test]
        public void NamedCompanionClass_SurvivesSave_IntoAFreshSession()
        {
            // Іменний каст класу в зліпок не пише взагалі (фіксований архетип
            // з OpeningCast.*, як і Card/Skills НЕ-протагоніста) — після
            // RestoreFromBlob клас так само надійно приходить з контенту
            // заново на кожному Build(), як і решта картки.
            var s = NewSessionThroughCreation("warrior");
            string blob = s.SaveState(0);

            var fresh = new GameSession();
            fresh.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold });
            fresh.RestoreFromBlob(blob);

            Assert.AreEqual(CompanionClass.Healer, fresh.GetCharacterSheet("zakhar").Class);
            Assert.AreEqual(CompanionClass.Crafter, fresh.GetCharacterSheet("goban").Class);
        }

        // ================= допоміжне =================

        private static GameSession NewSessionThroughCreation(string backgroundId)
        {
            var s = new GameSession();
            s.NewGame(new NewGameOptions { SkipCreation = false, HitRule = HitRuleKind.Threshold });
            s.SetProtagonistBackground(backgroundId);
            s.ConfirmCreation();
            var step = s.AdvanceScene();
            while (!step.IsFinished)
                step = step.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene();
            Assert.AreEqual(SessionState.Morning, s.State);
            return s;
        }
    }
}
