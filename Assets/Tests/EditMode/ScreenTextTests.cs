using System.Collections.Generic;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Characters.Creation;
using Game.Core.Checks;
using Game.Core.Session;
using Game.Core.Session.Views;
using Game.Gameplay.UI;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Пакет E1b: чисті screen-model хелпери (ScreenText.cs) — легальність
    /// кнопок і форматування тексту, поза UnityEngine, тестовані headless.
    /// </summary>
    public class ScreenTextTests
    {
        private static RosterView Roster(params CompanionSummary[] companions)
            => new RosterView { Companions = companions };

        private static CompanionSummary Companion(string id, CompanionStatus status, string name = null)
            => new CompanionSummary { Id = id, DisplayName = name ?? id, Status = status };

        // ---------------- легальність ----------------

        [Test]
        public void AssignCandidateLegality_Idle_IsOk()
        {
            var legality = ScreenText.AssignCandidateLegality(Companion("maksym", CompanionStatus.Idle));
            Assert.IsTrue(legality.Enabled);
            Assert.IsNull(legality.ReasonKey);
        }

        [Test]
        public void AssignCandidateLegality_Antagonist_IsBlocked()
        {
            var legality = ScreenText.AssignCandidateLegality(Companion("tuhar", CompanionStatus.Antagonist));
            Assert.IsFalse(legality.Enabled);
            Assert.AreEqual("ui.reason.antagonist", legality.ReasonKey);
        }

        [Test]
        public void AssignCandidateLegality_Dead_IsBlocked()
        {
            var legality = ScreenText.AssignCandidateLegality(Companion("x", CompanionStatus.Dead));
            Assert.IsFalse(legality.Enabled);
            Assert.AreEqual("ui.reason.dead", legality.ReasonKey);
        }

        [Test]
        public void AssignCandidateLegality_OnMission_IsBlocked()
        {
            var legality = ScreenText.AssignCandidateLegality(Companion("x", CompanionStatus.OnMission));
            Assert.IsFalse(legality.Enabled);
            Assert.AreEqual("ui.reason.on_mission", legality.ReasonKey);
        }

        [Test]
        public void AssignCandidateLegality_Null_IsUnknown()
        {
            var legality = ScreenText.AssignCandidateLegality(null);
            Assert.IsFalse(legality.Enabled);
            Assert.AreEqual("ui.reason.unknown_companion", legality.ReasonKey);
        }

        [Test]
        public void ExpeditionPartyLegality_Empty_IsBlocked()
        {
            var legality = ScreenText.ExpeditionPartyLegality(new List<string>(), Roster());
            Assert.IsFalse(legality.Enabled);
            Assert.AreEqual("ui.reason.empty_party", legality.ReasonKey);
        }

        [Test]
        public void ExpeditionPartyLegality_Duplicate_IsBlocked()
        {
            var roster = Roster(Companion("maksym", CompanionStatus.Idle));
            var legality = ScreenText.ExpeditionPartyLegality(new[] { "maksym", "maksym" }, roster);
            Assert.IsFalse(legality.Enabled);
            Assert.AreEqual("ui.reason.duplicate_companion", legality.ReasonKey);
        }

        [Test]
        public void ExpeditionPartyLegality_ContainsAntagonist_IsBlocked()
        {
            var roster = Roster(
                Companion("maksym", CompanionStatus.Idle),
                Companion("tuhar", CompanionStatus.Antagonist));
            var legality = ScreenText.ExpeditionPartyLegality(new[] { "maksym", "tuhar" }, roster);
            Assert.IsFalse(legality.Enabled);
            Assert.AreEqual("ui.reason.antagonist", legality.ReasonKey);
        }

        [Test]
        public void ExpeditionPartyLegality_AllIdle_IsOk()
        {
            var roster = Roster(Companion("maksym", CompanionStatus.Idle), Companion("myroslava", CompanionStatus.Idle));
            var legality = ScreenText.ExpeditionPartyLegality(new[] { "maksym", "myroslava" }, roster);
            Assert.IsTrue(legality.Enabled);
        }

        // ---------------- decision options ----------------

        [Test]
        public void DecisionOptionLine_WithCandidate_MentionsSkillThresholdAndBand()
        {
            var option = new DecisionOptionView
            {
                Path = IncidentPathView.Quiet,
                SkillKey = "Persuade",
                Threshold = 5,
                BestActorId = "maksym",
                HasCandidate = true,
                ExpectedBand = "Good"
            };

            string line = ScreenText.DecisionOptionLine(option, Gender.Male);

            StringAssert.Contains("5", line);
            StringAssert.Contains(ScreenText.SkillLabel("Persuade", Gender.Male), line);
        }

        [Test]
        public void DecisionOptionLine_NoCandidate_SaysSo()
        {
            var option = new DecisionOptionView
            {
                Path = IncidentPathView.Bloody,
                SkillKey = "Melee",
                Threshold = 6,
                BestActorId = null,
                HasCandidate = false,
                ExpectedBand = "Worst"
            };

            string line = ScreenText.DecisionOptionLine(option, Gender.Female);
            Assert.IsFalse(string.IsNullOrEmpty(line));
            Assert.IsFalse(line.Contains("["), "Рядок варіанту не повинен містити заглушку відсутнього ключа: " + line);
        }

        [Test]
        public void SkillLabel_KnownKey_ReturnsUkrainianWord()
        {
            Assert.AreEqual("Переконання", ScreenText.SkillLabel("Persuade", Gender.Male));
        }

        [Test]
        public void SkillLabel_UnknownKey_FallsBackToRaw()
        {
            Assert.AreEqual("Something", ScreenText.SkillLabel("Something", Gender.Male));
        }

        [Test]
        public void BandLabel_CoversAllFourBands()
        {
            Assert.AreEqual("Найкраща", ScreenText.BandLabel(OutcomeBand.Best, Gender.Male));
            Assert.AreEqual("Найгірша", ScreenText.BandLabel(OutcomeBand.Worst, Gender.Male));
        }

        // ---------------- roster labels ----------------

        [Test]
        public void CompanionStatusLabel_ResolvesGenderVariants()
        {
            Assert.AreEqual("Ранений", ScreenText.CompanionStatusLabel(CompanionStatus.Injured, Gender.Male));
            Assert.AreEqual("Ранена", ScreenText.CompanionStatusLabel(CompanionStatus.Injured, Gender.Female));
        }

        [Test]
        public void LoyaltyLabel_Null_ReturnsDash()
        {
            Assert.AreEqual("—", ScreenText.LoyaltyLabel(null, Gender.Male));
        }

        [Test]
        public void LoyaltyLabel_Devoted_ReturnsWord()
        {
            Assert.AreEqual("Віддана", ScreenText.LoyaltyLabel(LoyaltyBand.Devoted, Gender.Male));
        }

        [Test]
        public void ResolveCompanionName_KnownCast_UsesTable()
        {
            Assert.AreEqual("Максим Беркут", ScreenText.ResolveCompanionName("maksym", Gender.Male, null));
        }

        [Test]
        public void ResolveCompanionName_UnknownId_FallsBackToRoster()
        {
            var roster = Roster(Companion("companion_x", CompanionStatus.Idle, "Свій Напарник"));
            Assert.AreEqual("Свій Напарник", ScreenText.ResolveCompanionName("companion_x", Gender.Male, roster));
        }

        [Test]
        public void ResolveCompanionName_TotallyUnknown_FallsBackToId()
        {
            Assert.AreEqual("ghost", ScreenText.ResolveCompanionName("ghost", Gender.Male, null));
        }

        // ---------------- save slots ----------------

        [Test]
        public void SaveSlotLine_Empty_UsesEmptyTemplate()
        {
            string line = ScreenText.SaveSlotLine(1, false, null, 0, Gender.Male);
            StringAssert.Contains("1", line);
            StringAssert.Contains("порожньо", line);
        }

        [Test]
        public void SaveSlotLine_Occupied_MentionsDay()
        {
            string line = ScreenText.SaveSlotLine(0, true, "Хутір, Спокій", 4, Gender.Male);
            StringAssert.Contains("4", line);
            StringAssert.Contains("Хутір, Спокій", line);
        }

        // ---------------- результати команд ----------------

        [Test]
        public void AssignResultText_EveryValue_HasNonEmptyText()
        {
            foreach (AssignmentResult r in System.Enum.GetValues(typeof(AssignmentResult)))
                Assert.IsFalse(string.IsNullOrEmpty(ScreenText.AssignResultText(r, Gender.Male)), r.ToString());
        }

        [Test]
        public void BuildResultText_EveryValue_HasNonEmptyText()
        {
            foreach (BuildOrderResult r in System.Enum.GetValues(typeof(BuildOrderResult)))
                Assert.IsFalse(string.IsNullOrEmpty(ScreenText.BuildResultText(r, Gender.Male)), r.ToString());
        }

        [Test]
        public void CouncilResultText_EveryValue_HasNonEmptyText()
        {
            foreach (CouncilOrderResult r in System.Enum.GetValues(typeof(CouncilOrderResult)))
                Assert.IsFalse(string.IsNullOrEmpty(ScreenText.CouncilResultText(r, Gender.Male)), r.ToString());
        }

        [Test]
        public void DispatchResultText_EveryValue_HasNonEmptyText()
        {
            foreach (DispatchResult r in System.Enum.GetValues(typeof(DispatchResult)))
                Assert.IsFalse(string.IsNullOrEmpty(ScreenText.DispatchResultText(r, Gender.Male)), r.ToString());
        }

        // ---------------- event feed ----------------

        [Test]
        public void EventLine_AssignMade_MentionsCompanionAndPost()
        {
            var args = new Dictionary<string, string> { { "companionId", "maksym" }, { "slotId", "council_seat" } };
            var evt = new GameEvent("assign.made", 1, Game.Core.Loop.DayPhase.Day, args);

            string line = ScreenText.EventLine(evt, Gender.Male, null);

            StringAssert.Contains("Максим Беркут", line);
            StringAssert.Contains("Місце в раді", line);
        }

        // Знайдено власником у білді 24.09.2026: «Максим Беркут: нова глава —
        // ch1». Стрічка мусить показувати назву глави, а не службовий id.
        [Test]
        public void EventLine_ArcChapterOpened_ShowsChapterTitle_NotRawId()
        {
            var args = new Dictionary<string, string>
            {
                { "companionId", "maksym" }, { "arcId", "arc_maksym" },
                { "chapterId", "ch1" }, { "chapterTitleKey", "arc.maksym.ch1.title" }
            };
            foreach (var key in new[] { "arc.chapter_opened", "arc.chapter_begun", "arc.chapter_completed" })
            {
                string line = ScreenText.EventLine(new GameEvent(key, 2, Game.Core.Loop.DayPhase.Day, args), Gender.Male, null);

                StringAssert.Contains("Максим Беркут", line, key);
                StringAssert.Contains("Вірність понад образу", line, key);
                StringAssert.DoesNotContain("ch1", line, key);
            }
        }

        [Test]
        public void EventLine_UnknownKey_FallsBackReadably()
        {
            var args = new Dictionary<string, string> { { "foo", "bar" } };
            var evt = new GameEvent("totally.unknown.key", 1, Game.Core.Loop.DayPhase.Day, args);

            string line = ScreenText.EventLine(evt, Gender.Male, null);

            StringAssert.Contains("totally.unknown.key", line);
            StringAssert.Contains("foo=bar", line);
        }

        [Test]
        public void EventLine_DayAdvanced_MentionsDayNumber()
        {
            var args = new Dictionary<string, string> { { "day", "3" }, { "phase", "Day" } };
            var evt = new GameEvent("day.advanced", 3, Game.Core.Loop.DayPhase.Day, args);

            string line = ScreenText.EventLine(evt, Gender.Male, null);
            StringAssert.Contains("3", line);
        }

        [Test]
        public void EventLine_Null_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, ScreenText.EventLine(null, Gender.Male, null));
        }

        // Фікс-ревью (major): ExpeditionSite.DomainTag ("road"/"craft"/"trade")
        // раніше підставлявся у signal.domain сирим — EventLine мав перекладати
        // його так само, як item/building/site/faction/scar, а не пропускати.
        [Test]
        public void EventLine_SignalDomain_TranslatesDomainTag()
        {
            var args = new Dictionary<string, string> { { "domain", "road" } };
            var evt = new GameEvent("signal.domain", 1, Game.Core.Loop.DayPhase.Day, args);

            string line = ScreenText.EventLine(evt, Gender.Male, null);

            StringAssert.Contains("дорога", line);
            StringAssert.DoesNotContain("road", line);
        }

        // Фікс-ревью (major, раунд 2, знайдено QA): GameSession.LogEvent(
        // "char.seen", Args("char", actorId)) називає суб'єкта через сирий
        // аргумент "char", а не "companionId" — EventLine раніше читав лише
        // "companionId", тож {char} підставлявся порожнім рядком і стрічка
        // показувала " тут." замість "Тугар Вовк тут.".
        [Test]
        public void EventLine_CharSeen_ResolvesNameFromRawCharArg()
        {
            var args = new Dictionary<string, string> { { "char", "tuhar" } };
            var evt = new GameEvent("char.seen", 1, Game.Core.Loop.DayPhase.Day, args);

            string line = ScreenText.EventLine(evt, Gender.Male, null);

            StringAssert.Contains("Тугар Вовк", line);
            StringAssert.AreEqualIgnoringCase("Тугар Вовк тут.", line);
        }

        // Фікс-ревью (major, знайдено QA): "scene.choice.made" — єдина подія,
        // чиї sceneId/optionId ішли СИРИМИ в стрічку ("opening.neighbour:
        // вибір ухвалено — refuse (Базова)." замість перекладеного заголовка
        // й тексту варіанту), доки решта аргументів події вже перекладались
        // через ContentLabel. Три сценарії — три різні конвенції найменування
        // ключів у Core/Scenes/*.cs (див. коментар над
        // ScreenText.ResolveSceneLabel).
        [Test]
        public void EventLine_SceneChoiceMade_OpeningScene_TranslatesTitleAndOption()
        {
            // OpeningScenes: sceneId "opening.neighbour" — заголовок лишає
            // sceneId цілим ("scene.opening.neighbour.title"), варіант
            // відкидає перший сегмент ("scene.neighbour.option.refuse").
            var args = new Dictionary<string, string> { { "sceneId", "opening.neighbour" }, { "optionId", "refuse" }, { "band", "Base" } };
            var evt = new GameEvent("scene.choice.made", 1, Game.Core.Loop.DayPhase.Day, args);

            string line = ScreenText.EventLine(evt, Gender.Female, null);

            StringAssert.Contains("Сусід з претензією", line);
            StringAssert.DoesNotContain("opening.neighbour", line);
            StringAssert.DoesNotContain("refuse", line);
        }

        [Test]
        public void EventLine_SceneChoiceMade_ScenePrefixedId_TranslatesTitleAndOption()
        {
            // CompanionScenes: sceneId уже сам є префіксом ключа
            // ("scene.myroslava.confrontation" + ".title"/".option.persuade").
            var args = new Dictionary<string, string> { { "sceneId", "scene.myroslava.confrontation" }, { "optionId", "persuade" }, { "band", "Good" } };
            var evt = new GameEvent("scene.choice.made", 1, Game.Core.Loop.DayPhase.Day, args);

            string line = ScreenText.EventLine(evt, Gender.Female, null);

            StringAssert.Contains("Нічна розмова", line);
            StringAssert.Contains("Переконати", line);
            StringAssert.DoesNotContain("scene.myroslava.confrontation", line);
            StringAssert.DoesNotContain("persuade", line);
        }

        [Test]
        public void EventLine_SceneChoiceMade_ArcChapterId_TranslatesTitleAndOption_NotArcJournalTitle()
        {
            // CompanionScenes: sceneId "arc.myroslava.ch1" — перший сегмент
            // ("arc") заміняється на "scene." ("scene.myroslava.ch1.title"/
            // ".option.trust"). "arc.myroslava.ch1.title" ("Довіра, що
            // росте") — ІНШИЙ ключ (заголовок глави в журналі,
            // Core/Companions/DefaultArcs.cs) — сама сцена мусить показати
            // СВІЙ заголовок ("Донька боярина"), не сусідній.
            var args = new Dictionary<string, string> { { "sceneId", "arc.myroslava.ch1" }, { "optionId", "trust" }, { "band", "Base" } };
            var evt = new GameEvent("scene.choice.made", 1, Game.Core.Loop.DayPhase.Day, args);

            string line = ScreenText.EventLine(evt, Gender.Female, null);

            StringAssert.Contains("Донька боярина", line);
            StringAssert.DoesNotContain("Довіра, що росте", line);
            StringAssert.DoesNotContain("arc.myroslava.ch1", line);
            StringAssert.DoesNotContain(": trust", line);
        }

        // ---------------- ряба ростера (ціль 5 «Якість стрічки») ----------------

        [Test]
        public void EventLine_RosterRippledKinshipDeath_NamesBothSides()
        {
            var args = new Dictionary<string, string> { { "companionId", "maksym" }, { "triggerId", "myroslava" }, { "kinship", "Kinship" } };
            var evt = new GameEvent("roster.rippled.kinship.death", 1, Game.Core.Loop.DayPhase.Day, args);

            string line = ScreenText.EventLine(evt, Gender.Male, null);

            // maksym/myroslava — реальні id з char.<id> у таблиці (Максим
            // Беркут/Мирослава) — EventLine резолвить обидва в ІМЕНА, не в сирі id.
            StringAssert.Contains("Максим Беркут", line);
            StringAssert.Contains("Мирослава", line);
            StringAssert.DoesNotContain("{", line);
        }

        [Test]
        public void EventLine_RosterRippledFrictionBetrayal_DifferentTextThanKinshipDeath()
        {
            var argsA = new Dictionary<string, string> { { "companionId", "maksym" }, { "triggerId", "myroslava" } };
            var lineA = ScreenText.EventLine(new GameEvent("roster.rippled.kinship.death", 1, Game.Core.Loop.DayPhase.Day, argsA), Gender.Male, null);
            var lineB = ScreenText.EventLine(new GameEvent("roster.rippled.friction.betrayal", 1, Game.Core.Loop.DayPhase.Day, argsA), Gender.Male, null);

            Assert.AreNotEqual(lineA, lineB, "Різні типи ряби мають різний текст, а не один безликий рядок.");
        }

        // ---------------- згортання повторів (BuildFeedLines) ----------------

        [Test]
        public void BuildFeedLines_ConsecutiveIdenticalLines_CollapseWithCount()
        {
            var log = new List<GameEvent>
            {
                new GameEvent("night.calm", 1, Game.Core.Loop.DayPhase.Night),
                new GameEvent("night.calm", 2, Game.Core.Loop.DayPhase.Night),
                new GameEvent("night.calm", 3, Game.Core.Loop.DayPhase.Night),
            };

            var lines = ScreenText.BuildFeedLines(log, Gender.Male, null);

            Assert.AreEqual(1, lines.Count);
            Assert.AreEqual(3, lines[0].Count);
        }

        [Test]
        public void BuildFeedLines_DifferentLines_DoNotCollapse()
        {
            var log = new List<GameEvent>
            {
                new GameEvent("night.calm", 1, Game.Core.Loop.DayPhase.Night),
                new GameEvent("companion.died", 1, Game.Core.Loop.DayPhase.Night, new Dictionary<string, string> { { "companionId", "maksym" } }),
            };

            var lines = ScreenText.BuildFeedLines(log, Gender.Male, null);

            Assert.AreEqual(2, lines.Count);
            Assert.AreEqual(1, lines[0].Count);
            Assert.AreEqual(1, lines[1].Count);
        }

        [Test]
        public void BuildFeedLines_NonConsecutiveDuplicates_DoNotCollapse()
        {
            var log = new List<GameEvent>
            {
                new GameEvent("night.calm", 1, Game.Core.Loop.DayPhase.Night),
                new GameEvent("companion.died", 1, Game.Core.Loop.DayPhase.Night, new Dictionary<string, string> { { "companionId", "maksym" } }),
                new GameEvent("night.calm", 2, Game.Core.Loop.DayPhase.Night),
            };

            var lines = ScreenText.BuildFeedLines(log, Gender.Male, null);

            Assert.AreEqual(3, lines.Count, "Один і той самий рядок, розділений іншим — це не 'підряд', обидва мають лишитись окремо.");
        }

        [Test]
        public void BuildFeedLines_PreservesNewestFirstOrder()
        {
            var log = new List<GameEvent>
            {
                new GameEvent("companion.died", 1, Game.Core.Loop.DayPhase.Night, new Dictionary<string, string> { { "companionId", "maksym" } }),
                new GameEvent("companion.died", 2, Game.Core.Loop.DayPhase.Night, new Dictionary<string, string> { { "companionId", "myroslava" } }),
            };

            var lines = ScreenText.BuildFeedLines(log, Gender.Male, null);

            Assert.AreEqual(2, lines.Count);
            StringAssert.Contains("Мирослава", lines[0].Text); // найновіше — перше
            StringAssert.Contains("Максим Беркут", lines[1].Text);
        }

        /// <summary>
        /// Фікс-ревью (minor, знайдено QA): один і той самий triggerId/тип
        /// ряби, кілька РІЗНИХ реагуючих (companionId) підряд — раніше п'ять
        /// окремих рядків з різними іменами (×N бачить лише буквально
        /// однаковий текст, а тут ім'я щоразу інше). Тепер це ОДИН рядок,
        /// решта імен — у AlsoNames.
        /// </summary>
        [Test]
        public void BuildFeedLines_GroupReaction_SameTriggerDifferentSubjects_CollapseIntoAlsoNames()
        {
            var log = new List<GameEvent>
            {
                new GameEvent("roster.rippled.neutral.betrayal", 1, Game.Core.Loop.DayPhase.Day,
                    new Dictionary<string, string> { { "companionId", "maksym" }, { "triggerId", "myroslava" } }),
                new GameEvent("roster.rippled.neutral.betrayal", 1, Game.Core.Loop.DayPhase.Day,
                    new Dictionary<string, string> { { "companionId", "zakhar" }, { "triggerId", "myroslava" } }),
            };

            var lines = ScreenText.BuildFeedLines(log, Gender.Male, null);

            Assert.AreEqual(1, lines.Count, "Група реакцій на ту саму подію — один рядок, не два.");
            // Стрічка — найновіше перше (§BuildFeedLines_PreservesNewestFirstOrder):
            // "zakhar" стоїть ДРУГИМ у DayLog (хронологічно пізніше) — його рядок
            // лишається основним Text, "maksym" (раніше) іде в AlsoNames.
            StringAssert.Contains("Захар Беркут", lines[0].Text);
            Assert.IsNotNull(lines[0].AlsoNames);
            Assert.AreEqual(1, lines[0].AlsoNames.Count);
            StringAssert.Contains("Максим", lines[0].AlsoNames[0]);
        }

        [Test]
        public void BuildFeedLines_GroupReaction_DifferentTrigger_DoesNotCollapse()
        {
            var log = new List<GameEvent>
            {
                new GameEvent("roster.rippled.neutral.betrayal", 1, Game.Core.Loop.DayPhase.Day,
                    new Dictionary<string, string> { { "companionId", "maksym" }, { "triggerId", "myroslava" } }),
                new GameEvent("roster.rippled.neutral.death", 1, Game.Core.Loop.DayPhase.Day,
                    new Dictionary<string, string> { { "companionId", "zakhar" }, { "triggerId", "someoneelse" } }),
            };

            var lines = ScreenText.BuildFeedLines(log, Gender.Male, null);

            Assert.AreEqual(2, lines.Count, "Різний тригер/ключ — не одна й та сама хвиля ряби.");
        }
    }
}
