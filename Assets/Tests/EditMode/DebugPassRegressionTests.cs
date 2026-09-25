using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Characters.Creation;
using Game.Core.Combat;
using Game.Core.Session;
using Game.Core.Session.Bots;
using Game.Core.Session.Views;
using Game.Gameplay.Text;
using Game.Gameplay.UI;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Дебаг тестової збірки 25.09.2026 («Давай дебаг всего»): хиби, які
    /// знайшли аудити людських шляхів, сейвів і наслідків вибору. Кожен тест
    /// падав до виправлення.
    /// </summary>
    public class DebugPassRegressionTests
    {
        private const string PassIncident = "pass_vanguard";
        private const string PassSource = "opening.pass";
        private const int NeighbourRefuse = 0;
        private const int NeighbourBargain = 1;

        private static NewGameOptions Quick(bool ironman = false) =>
            new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold, Ironman = ironman };

        /// <summary>Сцена відкриття до кінця; на першому виборі — <paramref name="firstChoice"/>, на решті — 0.</summary>
        private static void PlayOpening(GameSession s, int firstChoice = 0)
        {
            SceneStepView step = s.AdvanceScene();
            bool chosen = false;
            while (!step.IsFinished)
            {
                if (step.IsChoice)
                {
                    step = s.ChooseSceneOption(chosen ? 0 : firstChoice);
                    chosen = true;
                }
                else step = s.AdvanceScene();
            }
            Assert.AreEqual(SessionState.Morning, s.State);
        }

        /// <summary>Нова гра торговцем (Торгівля 6 — торг із Тугаром проходить) і сцена відкриття до ранку доби 1.</summary>
        private static void NewTraderGame(GameSession s, int neighbourOption)
        {
            s.NewGame(new NewGameOptions { SkipCreation = false, HitRule = HitRuleKind.Threshold });
            s.SetProtagonistName("Торговець");
            s.SetProtagonistBackground("trader");
            s.ConfirmCreation();
            PlayOpening(s, neighbourOption);
        }

        private static int PassThreshold(GameSession s) => s.DebugQuietThreshold(PassIncident, PassSource);

        private static GameSession Continue(string blob)
        {
            var restored = new GameSession();
            restored.PreloadSlot(1, blob);
            Assert.IsTrue(restored.ContinueGame(1), "зліпок мав завантажитись");
            return restored;
        }

        // ============ торг із Тугаром: числовий наслідок вибору ============

        [Test]
        public void Bargain_LowersQuietThreshold_OfNode1()
        {
            var refused = new GameSession();
            NewTraderGame(refused, NeighbourRefuse);
            var bargained = new GameSession();
            NewTraderGame(bargained, NeighbourBargain);

            Assert.Less(PassThreshold(bargained), PassThreshold(refused),
                "торг із Тугаром (Поправка №7.8) мав полегшити тихий шлях вузла 1");
        }

        /// <summary>
        /// Прапор «бонус уже застосовано» не скидався в NewGame: друга партія
        /// в тому самому процесі торгувалась даремно — прапор сцени стояв, а
        /// поріг лишався як без торгу.
        /// </summary>
        [Test]
        public void Bargain_WorksInSecondGame_OfTheSameSession()
        {
            var baseline = new GameSession();
            NewTraderGame(baseline, NeighbourBargain);

            var s = new GameSession();
            NewTraderGame(s, NeighbourBargain);
            NewTraderGame(s, NeighbourBargain);
            Assert.AreEqual(PassThreshold(baseline), PassThreshold(s),
                "у другій грі того самого процесу торг мав діяти так само, як у першій");
        }

        /// <summary>Поріг інциденту не входить у зліпок; прапор — входить, але бонус торгу після завантаження не застосовувався знову.</summary>
        [Test]
        public void Bargain_SurvivesSaveAndContinue()
        {
            var s = new GameSession();
            NewTraderGame(s, NeighbourBargain);
            int expected = PassThreshold(s);

            var restored = Continue(s.SaveState(1));
            Assert.AreEqual(expected, PassThreshold(restored),
                "після «Продовжити» торг із Тугаром мав лишитись у порозі вузла 1");
        }

        // ============ сейв: ironman, фаза доби, шрами ============

        [Test]
        public void Ironman_SurvivesSaveAndContinue()
        {
            var s = new GameSession();
            s.NewGame(Quick(ironman: true));
            PlayOpening(s);
            Assert.IsTrue(s.DebugIronman);

            var restored = Continue(s.SaveState(1));
            Assert.IsTrue(restored.DebugIronman,
                "ironman мовчки вимикався після «Продовжити» — протагоніст знову ставав безсмертним");
        }

        [Test]
        public void PhaseAndScars_SurviveSaveIntoFreshSession()
        {
            // DelveGreedy за 10 діб уже має шрами в загоні (аудит сейвів).
            var s = new GameSession();
            s.NewGame(Quick());
            BotRunner.Drive(s, new DelveGreedyPolicy(), 10);
            Assert.IsTrue(s.State == SessionState.Morning || s.State == SessionState.FreePlay, "зберігати можна лише вранці");

            var before = s.GetRosterView().Companions.ToDictionary(c => c.Id, c => c.ScarCount);
            Assert.IsTrue(before.Values.Any(n => n > 0), "за 10 діб DelveGreedy хтось мав отримати шрам — сценарій зламано");
            var phase = s.CurrentView.Phase;

            var restored = Continue(s.SaveState(1));
            var after = restored.GetRosterView().Companions.ToDictionary(c => c.Id, c => c.ScarCount);
            CollectionAssert.AreEquivalent(before, after, "шрами — вічний трек (US-2.5): після «Продовжити» вони зникали");
            Assert.AreEqual(phase, restored.CurrentView.Phase, "фаза доби губилась у зліпку");
        }

        // ============ місто: відкриті пости, готовність переселенців ============

        [Test]
        public void CityView_ListsOnlyOpenPosts()
        {
            var s = new GameSession();
            s.NewGame(Quick());
            PlayOpening(s);

            var open = s.GetCityView().OpenPosts;
            Assert.IsNotNull(open);
            Assert.IsFalse(open.Contains("workshop_bench"), "майстерня ще не збудована — її пост закритий");

            var free = s.GetRosterView().Companions.First(c => c.Id != GameSession.ProtagonistId &&
                                                               string.IsNullOrEmpty(c.AssignedSlotId) &&
                                                               c.Status == CompanionStatus.Idle);
            Assert.AreEqual(AssignmentResult.SlotLocked, s.Assign(free.Id, "workshop_bench"),
                "на закритий пост не призначають — і екран тепер знає це заздалегідь");
        }

        // ============ автосейв доходить до оболонки ============

        [Test]
        public void MorningAutosave_IsExposed_ForTheShellToWriteToDisk()
        {
            var s = new GameSession();
            s.NewGame(Quick());
            PlayOpening(s);
            int before = s.AutosaveVersion;

            BotRunner.Drive(s, new StewardPolicy(), 2);
            Assert.Greater(s.AutosaveVersion, before, "ранковий автосейв мав з'явитись");
            Assert.IsFalse(string.IsNullOrEmpty(s.AutosaveBlob));

            var restored = Continue(s.AutosaveBlob);
            Assert.AreEqual(s.CurrentView.Day, restored.CurrentView.Day, "автосейв — повноцінний зліпок, з нього продовжують");
        }

        // ============ екрани: відмова видна, а не мовчить ============

        [Test]
        public void FailureTexts_AreNullOnSuccess_AndWordsOnFailure()
        {
            var g = Gender.Male;
            Assert.IsNull(ScreenText.CouncilFailure(CouncilOrderResult.Applied, g));
            Assert.IsNull(ScreenText.CouncilFailure(CouncilOrderResult.Queued, g));
            Assert.IsNull(ScreenText.AssignFailure(AssignmentResult.Success, g));
            Assert.IsNull(ScreenText.BuildFailure(BuildOrderResult.Started, g));

            foreach (var line in new[]
            {
                ScreenText.CouncilFailure(CouncilOrderResult.OnCooldown, g),
                ScreenText.CouncilFailure(CouncilOrderResult.NotEnoughFood, g),
                ScreenText.AssignFailure(AssignmentResult.SlotLocked, g),
                ScreenText.BuildFailure(BuildOrderResult.NotEnoughGold, g),
            })
            {
                Assert.IsFalse(string.IsNullOrEmpty(line));
                StringAssert.DoesNotContain("[", line, "відмова без тексту в таблиці");
            }
        }

        [Test]
        public void LockedPost_NamesTheBuildingThatOpensIt()
        {
            string reason = ScreenText.PostLockedReason("workshop_bench", Gender.Male);
            StringAssert.Contains(UkrainianText.Get("building.workshop", Gender.Male), reason);
            StringAssert.DoesNotContain("{", reason);
        }

        [Test]
        public void SaveSlotLine_NamesTheMood_NotTheRawBand()
        {
            string line = ScreenText.SaveSlotLine(1, true, "Calm", 3, Gender.Male);
            StringAssert.DoesNotContain("Calm", line, "заголовок слота йшов на титул сирим англійським enum");
            StringAssert.Contains(UkrainianText.Get("ui.mood.calm", Gender.Male), line);

            Assert.AreEqual(UkrainianText.Get("ui.save.to.auto", Gender.Male), ScreenText.SavedToLabel(-1, Gender.Male),
                "«Збережено: слот -1.» замість «автозбереження»");
        }

        // ============ мова: русизми не доходять до гравця ============

        /// <summary>
        /// Уся таблиця тексту гри — без російських літер і без русизмів, на
        /// які вже скаржився власник («Накал», «кровавий», «полоса», «стройка»…).
        /// </summary>
        [Test]
        public void TextTable_HasNoRussianLettersOrKnownRussisms()
        {
            var russianLetters = new Regex("[ыэъёЫЭЪЁ]");
            var russisms = new Regex("(?<![а-яіїєґА-ЯІЇЄҐ'])(полос|накал|излом|брожен|ропот|кровав|стройк|слепок|протагонист)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            var problems = new List<string>();
            foreach (var key in UkrainianText.AllKeys)
            {
                string text = UkrainianText.Get(key, Gender.Male);
                if (russianLetters.IsMatch(text) || russisms.IsMatch(text))
                    problems.Add(key + ": " + text);
            }
            CollectionAssert.IsEmpty(problems, "російське в тексті гри:\n" + string.Join("\n", problems));
        }
    }
}
