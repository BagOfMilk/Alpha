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
    }
}
