using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Core.Characters.Creation;
using Game.Core.Session;
using Game.Core.Session.Views;
using Game.Gameplay.Text;
using Game.Gameplay.UI;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Панелі «Люди», «Журнал», «Збереження» (docs/UX_DESIGN.md §5.11, §5.12,
    /// §5.14): картки замість таблиць, приховане — лише словом, дія — в одній
    /// картці, підтвердження — лише для незворотного.
    /// </summary>
    public class UxPanelBuildersTests
    {
        private static readonly Regex Latin = new Regex("[A-Za-z]");
        private static readonly Regex Digit = new Regex("[0-9]");

        private static GameSession NewSession()
        {
            var s = new GameSession();
            s.NewGame(new NewGameOptions { SkipCreation = true, HitRule = Game.Core.Combat.HitRuleKind.Threshold });
            return s;
        }

        private static IEnumerable<string> AllText(UxPanelModel p)
        {
            yield return p.Title;
            if (p.EmptyText != null) yield return p.EmptyText;
            foreach (var c in p.Cards)
            {
                yield return c.Title;
                if (c.Subtitle != null) yield return c.Subtitle;
                foreach (var ch in c.Chips) yield return ch.Text;
                foreach (var l in c.Lines) yield return l;
                foreach (var a in c.Actions) yield return a.Label;
            }
        }

        // ---------------- Люди (C) ----------------

        [Test]
        public void People_OneCardPerPerson_UkrainianOnly_LoyaltyAsWord()
        {
            var s = NewSession();
            var roster = s.GetRosterView();
            Assume.That(roster.Companions.Count, Is.GreaterThan(0), "після прологу хтось має бути поруч");

            var panel = UxPeoplePanel.Build(roster, Gender.Male);
            Assert.AreEqual(roster.Companions.Count, panel.Cards.Count);
            foreach (var text in AllText(panel))
                Assert.IsFalse(Latin.IsMatch(text ?? ""), "латиниця на панелі: " + text);

            // Лояльність — лише словом (UI-06): жодна фішка, крім рівня і шрамів, не має цифр.
            string level = UkrainianText.Format("ux.people.level", Gender.Male, "n", "");
            string scars = UkrainianText.Format("ux.people.scars", Gender.Male, "n", "");
            foreach (var chip in panel.Cards.SelectMany(c => c.Chips))
                if (!chip.Text.StartsWith(level) && !chip.Text.StartsWith(scars))
                    Assert.IsFalse(Digit.IsMatch(chip.Text), "число в чипі, що мав бути словом: " + chip.Text);
        }

        [Test]
        public void People_EmptyRoster_TeachesWhy()
        {
            var panel = UxPeoplePanel.Build(new RosterView { Companions = new List<CompanionSummary>() }, Gender.Female);
            Assert.IsEmpty(panel.Cards);
            Assert.IsNotEmpty(panel.EmptyText);
        }

        // ---------------- Журнал (J) ----------------

        [Test]
        public void Journal_Readiness_IsWordOnly_NoMilestoneCount()
        {
            // Мутація: додати рядок «віхи N з M» — тест падає (GDD US-11.4).
            var readiness = new ReadinessView { Band = "Wary", MilestonesReached = 3, MilestonesTotal = 5 };
            var panel = UxJournalPanel.Build(null, new FactionsView { Factions = new List<FactionSummary>() }, readiness, Gender.Male);
            string threat = UkrainianText.Get("ux.journal.section.threat", Gender.Male);
            foreach (var card in panel.Cards.Where(c => c.Section == threat))
                foreach (var text in new[] { card.Title }.Concat(card.Chips.Select(x => x.Text)).Concat(card.Lines))
                    Assert.IsFalse(Digit.IsMatch(text), "готовність числом: " + text);
        }

        [Test]
        public void Journal_AtMostFourSections_ExpeditionsLinkToThePlace_NoOwnActions()
        {
            var s = NewSession();
            var panel = UxJournalPanel.Build(s.GetQuestOffer(), s.GetFactionsView(), s.GetReadinessView(), Gender.Male);

            Assert.That(panel.Cards.Select(c => c.Section).Distinct().Count(), Is.LessThanOrEqualTo(4), "підвкладок не більше чотирьох (§3.3)");
            string trips = UkrainianText.Get("ux.journal.section.expeditions", Gender.Male);
            var trip = panel.Cards.Single(c => c.Section == trips);
            Assert.AreEqual(Game.Gameplay.Walk.VillagePlaces.StationPrefix + Game.Gameplay.Walk.BuildingCatalog.MusterStation, trip.LinkPlaceId,
                "збори — на Заставі (станція «Збори»), панель лише веде (UX-04); раніше посилання було на неіснуюче «post:scouting_post»");
            Assert.IsEmpty(panel.Cards.SelectMany(c => c.Actions), "журнал не дублює дій місць (UX-04, P-UX-01)");

            string world = UkrainianText.Get("ux.journal.section.world", Gender.Male);
            Assert.AreEqual(s.GetFactionsView().Factions.Count, panel.Cards.Count(c => c.Section == world));
            foreach (var text in AllText(panel))
                Assert.IsFalse(Latin.IsMatch(text ?? ""), "латиниця на панелі: " + text);
        }

        // ---------------- Збереження ----------------

        private static List<SaveSlotView> Slots() => new List<SaveSlotView>
        {
            new SaveSlotView { Slot = 0, Occupied = true, Headline = "Calm", Day = 3 },
            new SaveSlotView { Slot = 1, Occupied = false },
            new SaveSlotView { Slot = -1, Occupied = true, Headline = "Murmur", Day = 4 },
        };

        [Test]
        public void Save_OverwriteAsksConfirm_EmptySlotDoesNot_AutosaveNotWritable()
        {
            // Мутація: прибрати Confirm з перезапису — тест падає (UX-12).
            var panel = UxSavePanel.Build(Slots(), Gender.Male, i => UxOutcome.Success(), i => UxOutcome.Success(), true);
            var occupiedSave = panel.Cards[0].Actions.Single(a => a.Id == "save:0");
            var emptySave = panel.Cards[1].Actions.Single(a => a.Id == "save:1");
            Assert.IsNotNull(occupiedSave.Confirm, "перезапис зайнятого слота — лише з підтвердженням");
            Assert.IsNull(emptySave.Confirm, "порожній слот зберігається одразу — картка вже показала ціну");
            Assert.IsFalse(panel.Cards[2].Actions.Any(a => a.Id.StartsWith("save:")), "в автозбереження гравець не пише");
            Assert.IsNotNull(panel.Cards[2].Actions.Single(a => a.Id == "load:-1").Confirm, "завантаження поверх гри — з підтвердженням");
        }

        [Test]
        public void Save_OnlyMorningOrFreePlay_RefusedHumanly_CoreNotCalled()
        {
            int calls = 0;
            var panel = UxSavePanel.Build(Slots(), Gender.Male, i => { calls++; return UxOutcome.Success(); }, null, false);
            var save = panel.Cards[1].Actions.Single(a => a.Id == "save:1");

            var evening = UxCommandRunner.Invoke(save, SessionState.Evening, false);
            Assert.IsFalse(evening.Ok);
            StringAssert.Contains("вранці", evening.Refusal);
            Assert.AreEqual(0, calls, "увечері запис не кличеться");

            Assert.IsTrue(UxCommandRunner.Invoke(save, SessionState.Morning, false).Ok);
            Assert.AreEqual(1, calls);
            Assert.IsFalse(panel.Cards.SelectMany(c => c.Actions).Any(a => a.Id.StartsWith("load:")), "без дозволу — жодного «Завантажити»");
        }

        [Test]
        public void Save_SlotTitles_AreUkrainian()
        {
            var panel = UxSavePanel.Build(Slots(), Gender.Female, null, null, true);
            foreach (var text in AllText(panel))
                Assert.IsFalse(Latin.IsMatch(text ?? ""), "латиниця на панелі збереження: " + text);
        }
    }
}
