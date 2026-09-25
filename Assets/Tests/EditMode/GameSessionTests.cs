using System;
using System.Collections.Generic;
using System.Text;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Dungeons;
using Game.Core.Items;
using Game.Core.Loop;
using Game.Core.Session;
using Game.Core.Session.Views;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// GameSession — фасад над злитим трунком (Core/Session, пакет D1,
    /// docs/TEST_BUILD.md §4.1–4.9, §4.14–4.15). Приймання пакета (§5, рядок D1):
    /// (1) NewGame→AdvanceDay(×2)→SaveState→RestoreState→AdvanceDay дає ідентичний
    ///     DayReportView; (2) усі команди §4.1 реалізовані і покриті хоч одним
    ///     тестом; (3) RequestBattle/OnBattleResolved коректно повертає
    ///     _resume.ReturnState для кожного SuspendReason;
    ///     (4) GameSession_Views_NeverExposeRawHiddenNumbers (ArchitectureGuardTests) зелений.
    /// </summary>
    public class GameSessionTests
    {
        private static NewGameOptions SkipCreationOptions(bool testBuildOneDayConstruction = true)
            => new NewGameOptions
                { SkipCreation = true, HitRule = HitRuleKind.Threshold, TestBuildOneDayConstruction = testBuildOneDayConstruction };

        /// <summary>Доганяє сесію крізь відкриваючу сцену до Morning доби 1 (State доступний одразу після NewGame(SkipCreation=true)).</summary>
        private static void FastForwardOpeningToMorning(GameSession s)
        {
            Assert.AreEqual(SessionState.Scene, s.State);
            RunSceneToFinish(s);
            Assert.AreEqual(SessionState.Morning, s.State);
        }

        /// <summary>
        /// Доганяє поточну сцену до кінця (Поправка №7.8): на кожному
        /// Choice-кроці бере варіант 0 — цей файл перевіряє РЕШТУ конвеєра
        /// (розв'язку вузла, збереження, конвеєр дня), а не саму розмову;
        /// зміст вибору сцен покритий окремими тестами (SceneChoiceTests/
        /// BetrayalConfrontationTests).
        /// </summary>
        private static SceneStepView RunSceneToFinish(GameSession s)
        {
            SceneStepView step = s.AdvanceScene();
            while (!step.IsFinished)
                step = step.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene();
            return step;
        }

        /// <summary>
        /// Один повний сценарний цикл доба N (тихий шлях на кожному рішенні,
        /// доки день не впаде в Evening/Summary). <c>collectInto</c> — не
        /// обов'язковий акумулятор: <see cref="GameSession.DayLog"/>
        /// очищується на початку КОЖНОЇ фази (§4.3), тож події денної фази
        /// (напр. "expedition.returned") зникають з DayLog ще до кінця цього
        /// методу (після AdvanceNight) — хто хоче побачити їх усі одразу,
        /// передає список, і хелпер копіює в нього DayLog після кожного кроку.
        /// </summary>
        private static DayReportView PlayFullDayQuiet(GameSession s, List<GameEvent> collectInto = null)
        {
            s.ConfirmMorning();
            var report = s.AdvanceDay();
            Collect(s, collectInto);
            while (report != null && report.AwaitsDecision)
            {
                report = s.ResolveIncident(IncidentPath.Quiet);
                Collect(s, collectInto);
            }

            if (report != null && report.Pending == null)
            {
                // Розв'язка вузла 1 (доба 1) відкриває сцену — доганяємо її, як і
                // відкриваючу, перш ніж підтверджувати вечір.
                if (s.State == SessionState.Scene)
                {
                    SceneStepView step;
                    step = RunSceneToFinish(s);
                }
            }

            if (s.State == SessionState.Evening) s.ConfirmEvening();
            if (s.State == SessionState.Night)
            {
                var night = s.AdvanceNight();
                Collect(s, collectInto);
                while (night != null && night.AwaitsDecision)
                {
                    night = s.ResolveIncident(IncidentPath.Quiet);
                    Collect(s, collectInto);
                }
            }
            return report;
        }

        private static void Collect(GameSession s, List<GameEvent> into)
        {
            if (into == null) return;
            into.AddRange(s.DayLog);
        }

        private static bool SawEvent(List<GameEvent> log, string key)
        {
            foreach (var e in log) if (e.Key == key) return true;
            return false;
        }

        /// <summary>
        /// Фікс-ревью раунд 2 (QA, майже-блокер балансу бою — CombatBalance.
        /// AccuracyPerWeaponSkill 2->8): вузол 1 (2 горд-розвідники) раніше був
        /// назавжди невигравним (Accuracy 17 навіть у Максима, скила Мілі 6, —
        /// нижче порога Graze), тож CombatAutoResolve() на ньому детерміновано
        /// давав полосу Worst (Defeat, склад повністю виведений) — саме на це
        /// спирались тести дефекції Мирослави нижче (порогу Resentful, -35,
        /// потрібна саме Worst — не просто Base). Тепер той самий бій
        /// виграється (полоса Good/Best), тож тести, яким явно потрібен
        /// ПОГАНИЙ вихід бою (а не перевірка балансу), змушують його навмисно:
        /// команда лише завершує ходи (жодної атаки), а ворог б'є на повну
        /// (CombatAiStepOneAction — той самий ІІ, що й CombatAutoResolve), доки
        /// весь склад не вибуде (Defeat) або не спрацює запобіжник guard.
        /// Це РОБАСТНІШЕ за стару залежність від конкретних чисел балансу: бій
        /// справді програний (команда навмисно не захищається), а не випадково
        /// зламаний.
        /// </summary>
        private static void LoseBattleOnPurpose(GameSession s)
        {
            int guard = 0;
            while (s.State == SessionState.Battle && guard++ < 2000)
            {
                var view = s.GetBattleView();
                string currentSide = null;
                if (view?.Units != null)
                    foreach (var u in view.Units)
                        if (u.Id == view.CurrentUnitId) { currentSide = u.Side; break; }

                if (currentSide == "Player") s.CombatEndTurn();
                else s.CombatAiStepOneAction();
            }
            Assert.AreEqual(SessionState.Scene, s.State, "бій мав завершитись Defeat (склад вибув) до ліміту guard");
        }

        // ---- Title / створення / відкриття ----

        [Test]
        public void NewGame_SkipCreation_ReachesMorningOfDay1ThroughOpeningScene()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            Assert.AreEqual(0, s.CurrentView.Day, "до першого AdvanceDay лічильник ще 0");
        }

        [Test]
        public void ProtagonistCreation_ConfirmCreation_AppliesBackgroundAndOpensScene()
        {
            var s = new GameSession();
            s.NewGame(new NewGameOptions { SkipCreation = false });
            Assert.AreEqual(SessionState.Creation, s.State);

            s.SetProtagonistName("Богдан");
            s.SetProtagonistBackground("healer");
            s.SetProtagonistGender(Game.Core.Characters.Creation.Gender.Male);
            var view = s.GetProtagonistCreationView();
            Assert.AreEqual("healer", view.BackgroundId);

            s.ConfirmCreation();
            Assert.AreEqual(SessionState.Scene, s.State);

            var roster = s.GetRosterView();
            Game.Core.Session.Views.CompanionSummary protagonist = null;
            foreach (var c in roster.Companions) if (c.Id == GameSession.ProtagonistId) protagonist = c;
            Assert.IsNotNull(protagonist);
            Assert.AreEqual("Богдан", protagonist.DisplayName);
        }

        [Test]
        public void NewTrainingBattle_FromTitle_SuspendsAndReturnsToTitle_OnAutoResolve()
        {
            var s = new GameSession();
            Assert.AreEqual(SessionState.Title, s.State);

            s.NewTrainingBattle(new TrainingBattleOptions { HitRule = HitRuleKind.Threshold });
            Assert.AreEqual(SessionState.Battle, s.State);
            Assert.IsNotNull(s.GetBattleView());

            s.CombatAutoResolve();
            Assert.AreEqual(SessionState.Title, s.State, "TrainingSkirmish повинен повернути ReturnState=Title");
            Assert.IsNull(s.GetBattleView());
        }

        // ==== Полірування (ціль 1 «Картка персонажа»): GetCharacterSheet ====

        [Test]
        public void GetCharacterSheet_UnknownCompanion_ReturnsNull()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            Assert.IsNull(s.GetCharacterSheet("no_such_companion"));
        }

        [Test]
        public void GetCharacterSheet_Maksym_HasFourAttributesTenSkillsAndStartingTraits()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            var sheet = s.GetCharacterSheet("maksym");
            Assert.IsNotNull(sheet);
            Assert.AreEqual("maksym", sheet.CompanionId);
            Assert.AreEqual(4, sheet.Attributes.Count, "чотири атрибути (Сила/Спритність/Кмітливість/Воля)");
            Assert.AreEqual(10, sheet.Skills.Count, "десять скілів");
            Assert.AreEqual(1, sheet.Level);
            Assert.AreEqual(CompanionStatus.Idle, sheet.Status, "Максим у полі — не на посту (§3.0)");
            Assert.IsNotNull(sheet.Loyalty, "Максим — напарник, Loyalty не null");

            // Стартові трейти (DefaultTraits, FirstHourWorld.BuildRoster): steadfast+hot_blooded.
            var traitIds = new List<string>();
            foreach (var t in sheet.Traits) traitIds.Add(t.TraitId);
            CollectionAssert.Contains(traitIds, "steadfast");
            CollectionAssert.Contains(traitIds, "hot_blooded");

            Assert.IsNotNull(sheet.Combat);
            Assert.Greater(sheet.Combat.HpMax, 0);
            Assert.Greater(sheet.Combat.ApMax, 0);

            Assert.IsNull(sheet.Equipment.WeaponId, "нічого не надіто на старті");
        }

        [Test]
        public void GetCharacterSheet_Protagonist_HasNoStartingTraits()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            var sheet = s.GetCharacterSheet(GameSession.ProtagonistId);
            Assert.IsNotNull(sheet);
            Assert.AreEqual(0, sheet.Traits.Count, "протагоніст — кастомна збірка (R12), стартових трейтів архетипу немає");
        }

        [Test]
        public void GetCharacterSheet_Equip_ReflectsInEquipmentSlots()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            var stash = s.GetStash();
            Assert.IsNotNull(stash);
            ItemInstance weapon = null;
            foreach (var it in stash) if (it.Slot == EquipSlot.Weapon) { weapon = it; break; }

            if (weapon == null)
                Assert.Ignore("У стартовому сташі немає предмета в слот Weapon — нема що екіпірувати цим тестом.");

            Assert.IsTrue(s.Equip("maksym", weapon.InstanceId, EquipSlot.Weapon));
            var sheet = s.GetCharacterSheet("maksym");
            Assert.AreEqual(weapon.Definition.Id, sheet.Equipment.WeaponId);
        }

        [Test]
        public void GetCharacterSheet_AvailablePerks_MarksSkillTooLow_ForZeroSkillCompanion()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            // Дід Овсій (keeper): Trade 7 -> master_trader (гейт Trade>=6) мав
            // бути Available; Medicine 0 -> field_medic (гейт Medicine>=6) мав
            // бути SkillTooLow. Обидва — з DefaultPerks (полірування, ціль 1).
            var sheet = s.GetCharacterSheet("keeper");
            Assert.IsNotNull(sheet);

            Game.Core.Session.Views.PerkPreviewLineView trader = null, medic = null;
            foreach (var p in sheet.AvailablePerks)
            {
                if (p.PerkId == "master_trader") trader = p;
                if (p.PerkId == "field_medic") medic = p;
            }

            Assert.IsNotNull(trader);
            Assert.IsTrue(trader.Available, "Trade 7 >= гейт 6 у master_trader");
            Assert.IsNull(trader.ReasonKey);

            Assert.IsNotNull(medic);
            Assert.IsFalse(medic.Available);
            Assert.AreEqual("ui.reason.perk.skill_too_low", medic.ReasonKey);
        }

        // ---- Доба 1: вибір репліки у сцені відкриття (Поправка №7.8, механіка «dialogue_choice») ----

        /// <summary>Доганяє сцену відкриття до Choice-кроку (не резолвлячи його) — так само, як OfferMyroslavaEveningScene() у тестах вище.</summary>
        private static SceneStepView AdvanceOpeningToChoice(GameSession s)
        {
            Assert.AreEqual(SessionState.Scene, s.State);
            SceneStepView step = s.AdvanceScene();
            while (!step.IsChoice && !step.IsFinished)
                step = s.AdvanceScene();
            Assert.IsTrue(step.IsChoice, "сцена відкриття мала зупинитись на виборі «відмовити/виторгувати час/спитати Мирославу»");
            return step;
        }

        private static int OptionIndexByTextKey(SceneStepView step, string textKey)
        {
            for (int i = 0; i < step.Options.Count; i++)
                if (step.Options[i].TextKey == textKey) return i;
            return -1;
        }

        private static Game.Core.Session.Views.MechanicJournalEntryView FindJournalEntry(
            System.Collections.Generic.IReadOnlyList<Game.Core.Session.Views.MechanicJournalEntryView> journal, string id)
        {
            foreach (var e in journal) if (e.Id == id) return e;
            return null;
        }

        /// <summary>
        /// GameSession.GetMechanicsJournal() (Поправка №7.8, п. 4): реєстр
        /// рахує "seen" по кумулятивних ключах подій усієї сесії
        /// (`_seenEventKeys`), а не поточної фази/дня — тож усе унсін на
        /// свіжій грі, і рівно одна опція вибору репліки вже досить, щоб
        /// "dialogue_choice" стало Seen=true (решта нових Id тут ще ні —
        /// покрито окремо в тестах арки/конфронтації нижче, де журнал
        /// перевіряється в кінці того самого сценарію).
        /// </summary>
        [Test]
        public void GetMechanicsJournal_DialogueChoice_BecomesSeenAfterFirstSceneChoice()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());

            var before = s.GetMechanicsJournal();
            Assert.IsNotNull(FindJournalEntry(before, "dialogue_choice"), "реєстр мав містити нову механіку «dialogue_choice»");
            Assert.IsNotNull(FindJournalEntry(before, "arc_chapter"));
            Assert.IsNotNull(FindJournalEntry(before, "betrayal_confrontation"));
            Assert.IsFalse(FindJournalEntry(before, "dialogue_choice").Seen, "на свіжій грі жодна механіка ще не бачена");
            Assert.IsFalse(FindJournalEntry(before, "arc_chapter").Seen);
            Assert.IsFalse(FindJournalEntry(before, "betrayal_confrontation").Seen);

            var step = AdvanceOpeningToChoice(s);
            s.ChooseSceneOption(OptionIndexByTextKey(step, "scene.neighbour.option.refuse"));

            var after = s.GetMechanicsJournal();
            Assert.IsTrue(FindJournalEntry(after, "dialogue_choice").Seen, "scene.choice.made мав позначити dialogue_choice побаченим");
            Assert.IsFalse(FindJournalEntry(after, "arc_chapter").Seen, "інші дві нові механіки ще НЕ трапились у цьому сценарії");
            Assert.IsFalse(FindJournalEntry(after, "betrayal_confrontation").Seen);
        }

        [Test]
        public void OpeningChoice_Refuse_LogsChoiceMade_WithBaseBand_AndConvergesToElderRefuses()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            var step = AdvanceOpeningToChoice(s);

            int idx = OptionIndexByTextKey(step, "scene.neighbour.option.refuse");
            Assert.GreaterOrEqual(idx, 0, "варіант «відмовити» мав бути серед опцій");
            Assert.IsTrue(step.Options[idx].HasCandidate, "просту опцію без перевірки завжди можна обрати");

            s.ChooseSceneOption(idx);

            bool sawChoiceMade = false;
            foreach (var e in s.DayLog)
                if (e.Key == "scene.choice.made" && e.Args["sceneId"] == "opening.neighbour" &&
                    e.Args["optionId"] == "refuse" && e.Args["band"] == "Base")
                    sawChoiceMade = true;
            Assert.IsTrue(sawChoiceMade, "§2: подія scene.choice.made на кожен вибір репліки — «відмовити» без перевірки завжди Base");

            // Без перевірки шлях сходиться на «elder_refuses» — та сама
            // репліка, що й у старій (до Поправки №7.8) версії сцени.
            bool sawElderRefuses = false;
            SceneStepView tail = s.AdvanceScene();
            while (!tail.IsFinished)
            {
                if (tail.LineKey == "scene.neighbour.elder_refuses") sawElderRefuses = true;
                tail = s.AdvanceScene();
            }
            Assert.IsTrue(sawElderRefuses);
            Assert.AreEqual("to.node1.pass", tail.TransitionKey);
        }

        [Test]
        public void OpeningChoice_Bargain_LogsChoiceMade_WithCheckDeterminedBand()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            var step = AdvanceOpeningToChoice(s);

            int idx = OptionIndexByTextKey(step, "scene.neighbour.option.bargain");
            Assert.GreaterOrEqual(idx, 0, "варіант «виторгувати час» (Торгівля) мав бути серед опцій");
            Assert.AreEqual(Game.Core.Checks.SkillKeys.Trade.Id, step.Options[idx].SkillKey);
            string expectedBand = step.Options[idx].ExpectedBand;

            s.ChooseSceneOption(idx);

            bool sawChoiceMade = false;
            foreach (var e in s.DayLog)
                if (e.Key == "scene.choice.made" && e.Args["sceneId"] == "opening.neighbour" &&
                    e.Args["optionId"] == "bargain" && e.Args["band"] == expectedBand)
                    sawChoiceMade = true;
            Assert.IsTrue(sawChoiceMade,
                "band у події мав збігтися з ExpectedBand прев'ю (той самий детермінований CheckResolver, інваріант 8)");
        }

        [Test]
        public void OpeningChoice_AskMyroslava_BranchesToRevealLine_InsteadOfElderRefuses()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            var step = AdvanceOpeningToChoice(s);

            int idx = OptionIndexByTextKey(step, "scene.neighbour.option.ask_myroslava");
            Assert.GreaterOrEqual(idx, 0, "варіант «спитати Мирославу» (Переконання) мав бути серед опцій");
            Assert.AreEqual(Game.Core.Checks.SkillKeys.Persuade.Id, step.Options[idx].SkillKey);

            s.ChooseSceneOption(idx);

            bool sawChoiceMade = false, sawReveal = false, sawElderRefuses = false;
            foreach (var e in s.DayLog)
                if (e.Key == "scene.choice.made" && e.Args["optionId"] == "ask_myroslava") sawChoiceMade = true;

            SceneStepView tail = s.AdvanceScene();
            while (!tail.IsFinished)
            {
                if (tail.LineKey == "scene.neighbour.myroslava_reveals") sawReveal = true;
                if (tail.LineKey == "scene.neighbour.elder_refuses") sawElderRefuses = true;
                tail = s.AdvanceScene();
            }

            Assert.IsTrue(sawChoiceMade);
            Assert.IsTrue(sawReveal, "гілка «спитати Мирославу» веде на окрему репліку-розкриття, а не на «elder_refuses»");
            Assert.IsFalse(sawElderRefuses, "ця гілка НЕ проходить через «elder_refuses» — інша репліка Захара тут не звучить");
            Assert.AreEqual("to.node1.pass", tail.TransitionKey, "всі три гілки сходяться в тому самому вузлі 1");
        }

        // ---- Доба 1: тихий шлях вузла 1 (Ж) ----

        // ---- Полірування (ціль 6 «Рішення»): вузол 1 кроваво каже "тактичний бій: N" ----

        [Test]
        public void Day1_Decision_BloodyOption_CarriesTacticalBattleEnemyCount()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            s.ConfirmMorning();
            s.AdvanceDay();
            var offer = s.GetPendingOffer();
            Assert.IsNotNull(offer);
            Assert.AreEqual("incident.pass_vanguard", offer.TopicId);

            Game.Core.Session.Views.DecisionOptionView quiet = null, bloody = null;
            foreach (var opt in offer.Options)
            {
                if (opt.Path == Game.Core.Session.Views.IncidentPathView.Quiet) quiet = opt;
                if (opt.Path == Game.Core.Session.Views.IncidentPathView.Bloody) bloody = opt;
            }

            Assert.IsNotNull(bloody, "вузол 1 завжди має кровавий варіант");
            Assert.AreEqual(2, bloody.TacticalBattleEnemyCount, "owner: 'тактичний бій: N ворогів' — Node1BloodyEnemyIds несе двох");

            Assert.IsNotNull(quiet, "вузол 1 завжди має тихий варіант");
            Assert.AreEqual(0, quiet.TacticalBattleEnemyCount, "тихий шлях вузла 1 — перевірка, не бій");
        }

        [Test]
        public void Day1_QuietPath_ResolvesPassVanguard_AndAppliesOutcome()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            s.ConfirmMorning();
            var report = s.AdvanceDay();
            Assert.IsTrue(report.AwaitsDecision);
            Assert.AreEqual("incident.pass_vanguard", report.Pending.TopicId);
            Assert.AreEqual(SessionState.Decision, s.State);

            var afterDecision = s.ResolveIncident(IncidentPath.Quiet);
            Assert.IsNotNull(afterDecision);
            // Тихий шлях веде в сцену розв'язки вузла 1, а не прямо у Вечір.
            Assert.AreEqual(SessionState.Scene, s.State);

            SceneStepView step;
            step = RunSceneToFinish(s);
            Assert.AreEqual(SessionState.Evening, s.State);

            bool sawResolved = false;
            foreach (var e in s.DayLog)
                if (e.Key == "decision.resolved") sawResolved = true;
            Assert.IsTrue(sawResolved, "decision.resolved має піти в DayLog (§4.3)");
        }

        /// <summary>
        /// Аудит П9: підсумок циклу (<see cref="Game.Core.Base.ProductionStep"/>.LastReport)
        /// нікуди не йшов — до цього пакету ApplyCycleReport не викликався
        /// взагалі, і жоден production.* не потрапляв у стрічку.
        /// </summary>
        [Test]
        public void AdvanceDay_EmitsProductionEvents_FromCycleReport()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            s.ConfirmMorning();
            s.AdvanceDay();

            bool sawResource = false;
            foreach (var e in s.DayLog)
                if (e.Key == "production.resource") sawResource = true;
            Assert.IsTrue(sawResource, "П9: CycleReport.Produced мав дійти до стрічки events production.*");
        }

        // ---- Доба 1: кровавий шлях вузла 1 → справжній бій (SuspendReason.PassVanguardBloody) ----

        [Test]
        public void Day1_BloodyPath_SuspendsToBattle_AndOnAutoResolve_ReturnsThroughDecisionToScene()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            s.ConfirmMorning();
            var report = s.AdvanceDay();
            Assert.IsTrue(report.AwaitsDecision);

            var duringBattle = s.ResolveIncident(IncidentPath.Bloody);
            Assert.IsNull(duringBattle, "поки триває бій, готового DayReportView ще немає");
            Assert.AreEqual(SessionState.Battle, s.State);

            var battleView = s.GetBattleView();
            Assert.IsNotNull(battleView);
            Assert.AreEqual(3, CountSide(battleView, "Player"));
            Assert.AreEqual(2, CountSide(battleView, "Enemy"));

            s.CombatAutoResolve();

            // OnBattleResolved спочатку встановлює State=_resume.ReturnState (Decision
            // для PassVanguardBloody), а вже потім, застосувавши наслідки бою й
            // розблокувавши денний конвеєр (DayProcessor.ResolvePendingWithBand),
            // веде сесію далі в сцену розв'язки вузла 1 — той самий кінцевий стан,
            // що й у тихого шляху.
            Assert.AreEqual(SessionState.Scene, s.State);
            Assert.IsNull(s.GetBattleView(), "бій має бути прибраний після резолву");
            Assert.IsNotNull(s.LastDayReport, "день мав завершитись battle-driven резолвом рішення");

            SceneStepView step;
            step = RunSceneToFinish(s);
            Assert.AreEqual(SessionState.Evening, s.State);
        }

        /// <summary>
        /// Фікс-ревью D1b (блокер): CombatAutoResolve() на СПРАВЖНЬОМУ бою
        /// (кровавий вузол 1, не тренувальна пісочниця) мав жодного разу не
        /// логувати ні "combat.attack.*", ні "combat.overwatch.triggered" — ІІ
        /// грав обидві сторони 3v2 (з реальними жертвами), а стрічка подій несла
        /// лише сукупний "combat.autoresolved"/"combat.battle.resolved". Тепер
        /// LogNewAttacks(before) кличеться і тут — так само, як в одиночних
        /// командах — тож бій, повністю розв'язаний ІІ, лишає той самий слід
        /// у DayLog, що й покроково дограний.
        /// </summary>
        [Test]
        public void CombatAutoResolve_OnRealBattle_LogsCombatAttackEvents_InDayLog()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            s.ConfirmMorning();
            var report = s.AdvanceDay();
            Assert.IsTrue(report.AwaitsDecision);

            var duringBattle = s.ResolveIncident(IncidentPath.Bloody);
            Assert.IsNull(duringBattle);
            Assert.AreEqual(SessionState.Battle, s.State);

            s.CombatAutoResolve();

            bool sawAttackEvent = false;
            foreach (var e in s.DayLog)
                if (e.Key == "combat.attack.hit" || e.Key == "combat.attack.miss" ||
                    e.Key == "combat.attack.graze" || e.Key == "combat.attack.crit")
                    sawAttackEvent = true;
            Assert.IsTrue(sawAttackEvent,
                "CombatAutoResolve на реальному 3v2 бою мав залишити хоч один combat.attack.* у DayLog (§2 рядок 30)");
        }

        private static int CountSide(BattleView view, string side)
        {
            int n = 0;
            foreach (var u in view.Units) if (u.Side == side) n++;
            return n;
        }

        /// <summary>
        /// D1b (seamsForD1 §5 B7/IncidentResolver.ApplyBloodCost): бій замінює
        /// саму перевірку вузла 1, але не звільняє кровавий шлях від двох цін,
        /// що платить БУДЬ-ЯКИЙ інший інцидент з HasBloodyPath незалежно від
        /// того, ЯК саме розв'язано вибір (перевіркою чи боєм) — драйвер
        /// Напруги PlaystyleBlood (закритий перелік, інваріант 5) і пам'ять
        /// страху громади (CausedFear). Обидва — приховані числа (R17), тож
        /// перевіряються лише через IVT-гачок (<see cref="GameSession.DebugTensionValue"/>/
        /// <see cref="GameSession.DebugCommunityIsAfraid"/>), не через жоден View.
        ///
        /// Фікс-ревью D1b: раніше PlaystyleBlood лягав через QueueExternal
        /// (мостик R6), який TensionTickStep дренує лише на ПЕРШОМУ тіку
        /// НАСТУПНОЇ фази — це давало ціні крові запізнення на цілу фазу
        /// проти Напруги полоси виходу (яка лягає синхронно тим самим
        /// викликом). Тепер обидва застосовуються атомарно.
        ///
        /// Фікс-ревью раунд 2 (QA): порівняння тепер бере знімок ОДРАЗУ після
        /// ResolveIncident(Bloody) — ДО CombatAutoResolve, а не після. Причина:
        /// САМ бій (OnBattleResolved -> ResolvePendingWithBand) окремо рухає
        /// Напругу драйвером incident.TensionByBand[полоса] — і для Good/Best
        /// (чистої перемоги, тепер звичного виходу цього бою після фіксу
        /// CombatBalance.AccuracyPerWeaponSkill) ця дельта ВІД'ЄМНА (чиста
        /// перемога заслужено заспокоює, не тривожить) і може повністю
        /// перекрити синхронний ріст від самого PlaystyleBlood. Це НЕ спростовує
        /// PlaystyleBlood — той факт перевіряється тут ДО того, як бій встигає
        /// додати свою окрему (і легітимно різну за знаком) дельту.
        /// </summary>
        [Test]
        public void Day1_BloodyPath_AppliesPlaystyleBloodTension_AndCausedFear_LikeAnyBloodyIncident()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            s.ConfirmMorning();
            var report = s.AdvanceDay();
            Assert.IsTrue(report.AwaitsDecision);

            int tensionBeforeBlood = s.DebugTensionValue;
            Assert.IsFalse(s.DebugCommunityIsAfraid, "страх не мав з'явитися ДО кровавого рішення");

            var duringBattle = s.ResolveIncident(IncidentPath.Bloody);
            Assert.IsNull(duringBattle);

            // Синхронно, у ТОМУ САМОМУ виклику ResolveIncident(Bloody) — ще до
            // того, як бій сам щось порахує (§ коментар класу вище).
            Assert.Greater(s.DebugTensionValue, tensionBeforeBlood,
                "PlaystyleBlood мав піднятi Напругу СИНХРОННО, так само, як IncidentResolver.ApplyBloodCost для звичайного кровавого шляху");
            Assert.IsTrue(s.DebugCommunityIsAfraid,
                "кроваве рішення вузла 1 через бій мало налякати громаду так само, як CausedFear звичайного кровавого шляху");

            s.CombatAutoResolve();
            Assert.AreEqual(SessionState.Scene, s.State);

            SceneStepView step;
            step = RunSceneToFinish(s);
            s.ConfirmEvening();
            s.AdvanceNight();
        }

        /// <summary>
        /// D1b (§2 рядок 30): кожна власна атака команди (Attack/UseAbility з
        /// WeaponAttack-ефектом) мусить лишити слід у стрічці подій одним із
        /// "combat.attack.hit/miss/graze/crit" — окремо від сирого
        /// <see cref="BattleView.Log"/> (рядки для гравця, не доказ для тесту).
        /// Тренувальний бій (ThresholdRule, детермінований): ініціатива
        /// trainee_1(6,seq0) → trainee_2(6,seq1) → training_scout_1(6,seq2) →
        /// training_scout_2(5,seq3) (TurnSystem: спад інціативи, тай-брейк —
        /// порядок додавання). trainee_1 (спис, мілі) не дістає ворога без
        /// руху — пропускаємо; trainee_2 (лук) атакує без обмеження дальності
        /// (Attack() перевіряє лише пряму видимість для дальньої зброї).
        /// </summary>
        [Test]
        public void CombatAttack_LogsCombatAttackOutcomeEvent_InDayLog()
        {
            var s = new GameSession();
            s.NewTrainingBattle(new TrainingBattleOptions { HitRule = HitRuleKind.Threshold });
            Assert.AreEqual(SessionState.Battle, s.State);

            s.CombatEndTurn(); // trainee_1 пропускає хід (мілі, дистанція завелика без руху)

            var result = s.CombatAttack("training_scout_1");
            Assert.AreEqual(CombatActionResult.Success, result, "trainee_2 (лук) мав влучити пряму видимість без руху");

            bool sawAttackEvent = false;
            foreach (var e in s.DayLog)
                if (e.Key == "combat.attack.hit" || e.Key == "combat.attack.miss" ||
                    e.Key == "combat.attack.graze" || e.Key == "combat.attack.crit")
                    sawAttackEvent = true;
            Assert.IsTrue(sawAttackEvent, "CombatAttack мав залогувати combat.attack.* у DayLog (§2 рядок 30)");
        }

        /// <summary>
        /// D1b (§2 рядок 30): реакція дозору — ОКРЕМИЙ ключ
        /// "combat.overwatch.triggered", не той самий "combat.attack.*", що й
        /// власна атака команди — незалежно від того, влучив дозор чи ні
        /// (§ геометрія — та сама, що в CombatOverwatchTests: конус 90°,
        /// тангенс півширини 1/1, дальня зброя — без обмеження на дистанцію,
        /// лише пряма видимість).
        /// </summary>
        [Test]
        public void CombatMove_TriggersOverwatchReaction_LogsCombatOverwatchTriggeredEvent()
        {
            var s = new GameSession();
            s.NewTrainingBattle(new TrainingBattleOptions { HitRule = HitRuleKind.Threshold });
            Assert.AreEqual(SessionState.Battle, s.State);

            s.CombatEndTurn(); // trainee_1 пропускає хід

            // trainee_2 (лук, (1,3)) бере сектор під прицел уздовж свого ряду —
            // Overwatch() сам резервує AP і завершує хід.
            var overwatchResult = s.CombatEnterOverwatch(new GridPos(6, 3));
            Assert.AreEqual(CombatActionResult.Success, overwatchResult);

            // training_scout_1 (6,1): один крок на (5,1) — усередині конуса й
            // прямої видимості дозору trainee_2. Тест керує юнітом напряму
            // (як і будь-яка тактична команда GameSession — керування стороною
            // вирішує викликач, не сам фасад).
            var moveResult = s.CombatMove(new GridPos(5, 1));
            Assert.AreEqual(CombatActionResult.Success, moveResult);

            bool sawOverwatchTriggered = false;
            foreach (var e in s.DayLog)
                if (e.Key == "combat.overwatch.triggered") sawOverwatchTriggered = true;
            Assert.IsTrue(sawOverwatchTriggered, "рух training_scout_1 у сектор trainee_2 мав спричинити реакцію дозору (§2 рядок 30)");
        }

        // ---- Полірування (ціль 3 «Бойові декорації»): вороги — не один стовпець ----

        /// <summary>
        /// Owner feedback: "Enemy deployments must be sensible formations
        /// with cover (not a single column)". Вузол 1 кроваво — 2 вороги
        /// (Node1BloodyEnemyIds) — мали стояти на РІЗНИХ X (зигзаг), і хоч
        /// один нести укриття на своєму тайлі (BattleGridView.TileCover
        /// читає той самий тайл, що юніт займає).
        /// </summary>
        [Test]
        public void Day1_BloodyBattle_EnemyFormation_IsNotASingleColumn_AndCarriesCover()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            s.ConfirmMorning();
            s.AdvanceDay();
            s.ResolveIncident(IncidentPath.Bloody);
            Assert.AreEqual(SessionState.Battle, s.State);

            var battle = s.GetBattleView();
            Assert.IsNotNull(battle);

            var enemyXs = new System.Collections.Generic.HashSet<int>();
            bool anyEnemyCovered = false;
            foreach (var u in battle.Units)
            {
                if (u.Side != "Enemy") continue;
                enemyXs.Add(u.Pos.X);
                int idx = u.Pos.X + u.Pos.Y * battle.Grid.Width;
                if (idx >= 0 && idx < battle.Grid.TileCover.Count && battle.Grid.TileCover[idx] != "None")
                    anyEnemyCovered = true;
            }

            Assert.Greater(enemyXs.Count, 1, "два вороги на РІЗНИХ X — не один стовпець");
            Assert.IsTrue(anyEnemyCovered, "хоч один ворог мав стояти на тайлі з укриттям (не лише декоративне укриття посеред мапи)");
        }

        // ---- Дефекція (US-9.4, R2/§2 №25): DefectionWatch.Tick + Defection.ShouldDefect ----

        private static Game.Core.Session.Views.CompanionSummary FindCompanion(RosterView roster, string id)
        {
            if (roster?.Companions == null) return null;
            foreach (var c in roster.Companions) if (c.Id == id) return c;
            return null;
        }

        /// <summary>
        /// Детермінований 3v2 бій вузла 1 (без хазяїна) заводить Максима
        /// вбитим і Мирославу — на полосу Base: <c>PassVanguardOutcome</c>
        /// сіє "defector_seeded" і реальна лояльність падає до Resentful
        /// (50-35=15 -> Resentful за §3.1 коментарем у
        /// CompanionSocialBalance). До Поправки №7.8 ця сама комбінація
        /// (прапор + полоса ≤ Resentful) дефектила Мирославу МОВЧКИ, на
        /// найближчому завершенні доби. Тепер для неї є сценарна нічна
        /// розмова-конфронтація (доба 3) — <c>GameSession.TickDefectionWatch</c>
        /// чекає на її розв'язку (<c>CompanionScenes.MyroslavaConfrontationResolvedFlag</c>),
        /// тож миттєвої мовчазної дефекції на добу 1 більше нема: гравець
        /// зобов'язаний побачити зраду, що насуває, а не прочитати про неї
        /// постфактум у стрічці подій.
        ///
        /// Полоса лояльності Мирослави природно спливає з Resentful до Wary
        /// вже на добу 2 (пасивний бонус «Morale» від council_seat,
        /// <c>LoyaltyRules.OnMorale</c>) — тому «насувана зрада» тут
        /// перевіряється лише прапором <c>Defection.DefectorSeededFlag</c>, а
        /// не повторним читанням полоси (GameSession.OfferMyroslavaEveningScene).
        /// </summary>
        [Test]
        public void Day1_BloodyPath_SeedsDefector_ButDefectionWaitsForNightThreeConfrontation()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            s.ConfirmMorning();
            var report = s.AdvanceDay();
            Assert.IsTrue(report.AwaitsDecision);

            var duringBattle = s.ResolveIncident(IncidentPath.Bloody);
            Assert.IsNull(duringBattle);
            Assert.AreEqual(SessionState.Battle, s.State);

            // Цьому тесту потрібна саме полоса Worst (-35 лояльності, поріг
            // Resentful нижче) — навмисно програний бій (LoseBattleOnPurpose),
            // а не CombatAutoResolve() (з полагодженим балансом дає Good/Best).
            LoseBattleOnPurpose(s);
            Assert.AreEqual(SessionState.Scene, s.State);

            bool sawLeft = false, sawResentful = false;
            foreach (var e in s.DayLog)
            {
                if (e.Key == "companion.left_settlement" && e.Args["companionId"] == "myroslava") sawLeft = true;
                if (e.Key == "loyalty.band_changed" && e.Args["companionId"] == "myroslava" && e.Args["band"] == "Resentful")
                    sawResentful = true;
            }
            Assert.IsTrue(sawLeft, "детермінований бій мав дати полосу Base/Worst (Мирослава йде) — тест писано під конкретний вихід");
            Assert.IsTrue(sawResentful, "лояльність Мирослави мала впасти рівно до Resentful (§3.1, -35 -> 15)");

            RunSceneToFinish(s);
            Assert.AreEqual(SessionState.Evening, s.State);

            s.ConfirmEvening();
            Assert.AreEqual(SessionState.Night, s.State);
            s.AdvanceNight();

            bool sawDefectedDay1 = false;
            foreach (var e in s.DayLog)
                if (e.Key == "companion.defected" && e.Args["companionId"] == "myroslava") sawDefectedDay1 = true;
            Assert.IsFalse(sawDefectedDay1,
                "Поправка №7.8: прапор defector_seeded + Resentful більше НЕ дефектять Мирославу мовчки на " +
                "найближчій ночі — вона чекає нічної розмови доби 3 (GameSession.OfferMyroslavaEveningScene)");

            var stillPresent = FindCompanion(s.GetRosterView(), "myroslava");
            Assert.IsNotNull(stillPresent);
            Assert.AreNotEqual(Game.Core.Characters.CompanionStatus.Antagonist, stillPresent.Status,
                "вона лишається в поселенні, поки конфронтація доби 3 не розв'язана");

            // Доба 2, тихо — нічого не мало б розв'язати конфронтацію завчасно
            // (полоса лояльності тим часом спливає з Resentful до Wary —
            // навмисно: див. коментар над тестом).
            s.ConfirmMorning();
            var d2report = s.AdvanceDay();
            while (d2report != null && d2report.AwaitsDecision)
                d2report = s.ResolveIncident(IncidentPath.Quiet);
            if (s.State == SessionState.Scene) RunSceneToFinish(s);
            s.ConfirmEvening();
            var d2night = s.AdvanceNight();
            while (d2night != null && d2night.AwaitsDecision)
                d2night = s.ResolveIncident(IncidentPath.Quiet);
            Assert.AreEqual(2, s.CurrentView.Day);

            // Доба 3, вечір: конфронтація сама себе пропонує лише тут
            // (GameSession.OfferMyroslavaEveningScene — той самий контракт,
            // що BotRunner.MaybeOfferScriptedScene використовує для ботів).
            s.ConfirmMorning();
            var day3Report = s.AdvanceDay();
            while (day3Report != null && day3Report.AwaitsDecision)
                day3Report = s.ResolveIncident(IncidentPath.Quiet);
            if (s.State == SessionState.Scene) RunSceneToFinish(s);
            Assert.AreEqual(SessionState.Evening, s.State);
            Assert.AreEqual(3, s.CurrentView.Day);

            var confrontation = s.OfferMyroslavaEveningScene();
            Assert.IsNotNull(confrontation, "зрада насуває (defector_seeded і досі не розв'язано) — доба 3 мала відкрити саме конфронтацію, не тиху перевірку");

            bool sawConfrontationBegun = false;
            foreach (var e in s.DayLog)
                if (e.Key == "scene.betrayal_confrontation.begun") sawConfrontationBegun = true;
            Assert.IsTrue(sawConfrontationBegun, "§2 нова механіка betrayal_confrontation: подія на відкриття сцени");
            Assert.IsTrue(FindJournalEntry(s.GetMechanicsJournal(), "betrayal_confrontation").Seen,
                "GetMechanicsJournal() мав позначити betrayal_confrontation побаченим");

            // OfferMyroslavaEveningScene() повертає лише ПЕРШИЙ кадр сцени
            // (Shot миттю за BeginScene, той самий контракт, що й
            // BotRunner.DriveSceneStep коментує: "перший кадр, який одразу
            // повертають OfferMyroslavaEveningScene/OfferZakharCouncilScene");
            // до вибору доганяємо так само, як RunSceneToFinish/FastForwardScene.
            while (confrontation != null && !confrontation.IsChoice && !confrontation.IsFinished)
                confrontation = s.AdvanceScene();

            Assert.IsTrue(confrontation.IsChoice, "конфронтація одразу зупиняється на виборі (переконати/звинуватити/відпустити)");
            int accuseIndex = -1;
            for (int i = 0; i < confrontation.Options.Count; i++)
                if (confrontation.Options[i].SkillKey == Game.Core.Checks.SkillKeys.Intimidate.Id) accuseIndex = i;
            Assert.GreaterOrEqual(accuseIndex, 0, "варіант «звинуватити» (Залякування) мав бути серед опцій конфронтації");

            s.ChooseSceneOption(accuseIndex);

            bool sawDefectedAfterConfrontation = false, sawRipple = false;
            foreach (var e in s.DayLog)
            {
                if (e.Key == "companion.defected" && e.Args["companionId"] == "myroslava") sawDefectedAfterConfrontation = true;
                // Полірування ціль 5 (trunk, уже злите до tb/choices): ключ
                // ряби несе тип зв'язку й причину суфіксом ("roster.rippled."
                // + bond + "." + betrayal/death, GameSession.LogRipple) —
                // перевіряємо префіксом, як і AllMechanicsCoverageTests.cs
                // рядок 595 та GameSessionTests.cs рядок 994 нижче.
                if (e.Key.StartsWith("roster.rippled", StringComparison.Ordinal)) sawRipple = true;
            }
            Assert.IsTrue(sawDefectedAfterConfrontation,
                "«звинуватити» на нічній розмові виконує дефекцію негайно (GameSession.ApplyBetrayalConfrontationSideEffectsIfNeeded)");
            Assert.IsTrue(sawRipple, "дефекція — це предаство (RosterDrama.OnBetrayal), а не тиха відсутність ряби");

            var afterConfrontation = FindCompanion(s.GetRosterView(), "myroslava");
            Assert.IsNotNull(afterConfrontation);
            Assert.AreEqual(Game.Core.Characters.CompanionStatus.Antagonist, afterConfrontation.Status,
                "після звинувачення вона переходить у статус Antagonist — так само, як старий мовчазний шлях");
        }

        /// <summary>
        /// Той самий сетап, що й <see cref="Day1_BloodyPath_SeedsDefector_ButDefectionWaitsForNightThreeConfrontation"/>
        /// (бій доби 1 сіє defector_seeded, доба 2 тихо, доба 3 увечері
        /// відкриває конфронтацію) — винесено окремо, бо на неї спираються ще
        /// два тести гілок «переконати»/«відпустити» нижче. Повертає кадр
        /// сцени вже НА виборі (переконати/звинуватити/відпустити).
        /// </summary>
        private static SceneStepView SeedMyroslavaDefectionAndReachConfrontationChoice(GameSession s)
        {
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            s.ConfirmMorning();
            var report = s.AdvanceDay();
            Assert.IsTrue(report.AwaitsDecision);
            Assert.IsNull(s.ResolveIncident(IncidentPath.Bloody));
            Assert.AreEqual(SessionState.Battle, s.State);
            // Base/Worst потрібна навмисно — див. LoseBattleOnPurpose.
            LoseBattleOnPurpose(s);
            Assert.AreEqual(SessionState.Scene, s.State);
            RunSceneToFinish(s);
            s.ConfirmEvening();
            s.AdvanceNight();

            // Доба 2, тихо.
            s.ConfirmMorning();
            var d2report = s.AdvanceDay();
            while (d2report != null && d2report.AwaitsDecision)
                d2report = s.ResolveIncident(IncidentPath.Quiet);
            if (s.State == SessionState.Scene) RunSceneToFinish(s);
            s.ConfirmEvening();
            var d2night = s.AdvanceNight();
            while (d2night != null && d2night.AwaitsDecision)
                d2night = s.ResolveIncident(IncidentPath.Quiet);

            // Доба 3, увечері — конфронтація.
            s.ConfirmMorning();
            var day3Report = s.AdvanceDay();
            while (day3Report != null && day3Report.AwaitsDecision)
                day3Report = s.ResolveIncident(IncidentPath.Quiet);
            if (s.State == SessionState.Scene) RunSceneToFinish(s);
            Assert.AreEqual(SessionState.Evening, s.State);
            Assert.AreEqual(3, s.CurrentView.Day);

            var confrontation = s.OfferMyroslavaEveningScene();
            Assert.IsNotNull(confrontation, "зрада насуває (defector_seeded і досі не розв'язано) — доба 3 мала відкрити саме конфронтацію");

            while (confrontation != null && !confrontation.IsChoice && !confrontation.IsFinished)
                confrontation = s.AdvanceScene();
            Assert.IsTrue(confrontation.IsChoice, "конфронтація одразу зупиняється на виборі (переконати/звинуватити/відпустити)");
            return confrontation;
        }

        /// <summary>
        /// Гілка «переконати» (Persuade): успіх (Good/Best) відновлює довіру і
        /// лишає Мирославу в поселенні, невдача (Worst/Base) — той самий
        /// прапор невдачі, що й будь-яка інша гілка, і вона так само йде в
        /// антагоністи. Викликач бере <c>ExpectedBand</c> прямо з прев'ю
        /// опції (той самий детермінований <c>CheckResolver</c>, що резолвить
        /// вибір), тому тест не залежить від конкретних чисел білда.
        /// </summary>
        [Test]
        public void MyroslavaConfrontation_Persuade_StaysOnSuccess_DefectsOnFailure()
        {
            var s = new GameSession();
            var confrontation = SeedMyroslavaDefectionAndReachConfrontationChoice(s);

            int persuadeIndex = -1;
            for (int i = 0; i < confrontation.Options.Count; i++)
                if (confrontation.Options[i].SkillKey == Game.Core.Checks.SkillKeys.Persuade.Id) persuadeIndex = i;
            Assert.GreaterOrEqual(persuadeIndex, 0, "варіант «переконати» (Переконання) мав бути серед опцій конфронтації");

            string expectedBand = confrontation.Options[persuadeIndex].ExpectedBand;
            bool expectSuccess = expectedBand == "Good" || expectedBand == "Best";

            s.ChooseSceneOption(persuadeIndex);

            bool sawDefected = false;
            foreach (var e in s.DayLog)
                if (e.Key == "companion.defected" && e.Args["companionId"] == "myroslava") sawDefected = true;

            var after = FindCompanion(s.GetRosterView(), "myroslava");
            Assert.IsNotNull(after);

            if (expectSuccess)
            {
                Assert.IsFalse(sawDefected, "успішне «переконати» (полоса " + expectedBand + ") мало відновити довіру, не дефектити");
                Assert.AreNotEqual(Game.Core.Characters.CompanionStatus.Antagonist, after.Status,
                    "вона лишається в поселенні після успішного переконання");
            }
            else
            {
                Assert.IsTrue(sawDefected, "невдале «переконати» (полоса " + expectedBand + ") дефектить так само, як звинувачення/відпускання");
                Assert.AreEqual(Game.Core.Characters.CompanionStatus.Antagonist, after.Status);
            }
        }

        /// <summary>«Відпустити» (без перевірки): завжди зрада, але м'якшим прапором — <c>MyroslavaConfrontedReleaseFlag</c>, а не «звинуватити»/провокацію.</summary>
        [Test]
        public void MyroslavaConfrontation_Release_AlwaysDefectsSoftened()
        {
            var s = new GameSession();
            var confrontation = SeedMyroslavaDefectionAndReachConfrontationChoice(s);

            int releaseIndex = -1;
            for (int i = 0; i < confrontation.Options.Count; i++)
                if (confrontation.Options[i].SkillKey == null || confrontation.Options[i].SkillKey.Length == 0)
                {
                    // "відпустити" — єдиний варіант без перевірки в цій сцені.
                    releaseIndex = i;
                }
            Assert.GreaterOrEqual(releaseIndex, 0, "варіант «відпустити» (без перевірки) мав бути серед опцій конфронтації");

            s.ChooseSceneOption(releaseIndex);

            bool sawDefected = false, sawRipple = false;
            foreach (var e in s.DayLog)
            {
                if (e.Key == "companion.defected" && e.Args["companionId"] == "myroslava") sawDefected = true;
                // Фікс-ревью (полірування, ціль 5 «Якість стрічки»): "roster.rippled"
                // більше не єдиний ключ — GameSession.LogRipple обирає один із
                // шести (тип зв'язку × загибель/зрада), тож перевіряємо префікс.
                if (e.Key.StartsWith("roster.rippled")) sawRipple = true;
            }
            Assert.IsTrue(sawDefected, "«відпустити» — теж зрада, лише м'якша, за GameSession.ApplyBetrayalConfrontationSideEffectsIfNeeded");
            Assert.IsTrue(sawRipple);

            var after = FindCompanion(s.GetRosterView(), "myroslava");
            Assert.IsNotNull(after);
            Assert.AreEqual(Game.Core.Characters.CompanionStatus.Antagonist, after.Status);
        }

        /// <summary>
        /// Адверсаріал-огляд Поправки №7.8: "prevent"-гілка конфронтації —
        /// коли зрада НЕ насуває (жоден кровавий вузол 1 не сіяв
        /// defector_seeded), OfferMyroslavaEveningScene() на добу 3 мала
        /// відкрити запасну тиху сцену (MyroslavaTrustCheckup), а не
        /// конфронтацію — до цього тесту тільки структурна валідність цієї
        /// сцени (SceneTests) була перевірена, сам факт, що ГРА обирає саме
        /// її гілку runtime-логіки, ще ні.
        /// </summary>
        [Test]
        public void OfferMyroslavaEveningScene_WithoutSeededDefection_OpensCheckupNotConfrontation()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s); // "відмовити" (варіант 0) — жодного зерна зради

            PlayFullDayQuiet(s); // доба 1, тихо
            PlayFullDayQuiet(s); // доба 2, тихо

            s.ConfirmMorning();
            var day3Report = s.AdvanceDay();
            while (day3Report != null && day3Report.AwaitsDecision)
                day3Report = s.ResolveIncident(IncidentPath.Quiet);
            if (s.State == SessionState.Scene) RunSceneToFinish(s);
            Assert.AreEqual(SessionState.Evening, s.State);
            Assert.AreEqual(3, s.CurrentView.Day);

            var offered = s.OfferMyroslavaEveningScene();
            Assert.IsNotNull(offered, "доба 3 мала запропонувати якусь сцену — зрада не насуває, тож це має бути checkup");

            bool sawCheckupBegun = false, sawConfrontationBegun = false;
            foreach (var e in s.DayLog)
            {
                if (e.Key == "scene.trust_checkup.begun") sawCheckupBegun = true;
                if (e.Key == "scene.betrayal_confrontation.begun") sawConfrontationBegun = true;
            }
            Assert.IsTrue(sawCheckupBegun,
                "без defector_seeded доба 3 мала відкрити тиху перевірку стосунків, не нічну розмову-конфронтацію");
            Assert.IsFalse(sawConfrontationBegun);

            var myroslava = FindCompanion(s.GetRosterView(), "myroslava");
            Assert.IsNotNull(myroslava);
            Assert.AreNotEqual(Game.Core.Characters.CompanionStatus.Antagonist, myroslava.Status,
                "тиха гілка того самого вузла не зраджує нікого");
        }

        // ---- Особиста арка (Поправка №7.8, п. 3 «ARCS PLAYABLE»): Begin → зміст → CompleteChapter → arc.chapter_completed ----

        /// <summary>
        /// Арка Максима гл.1 «Не за кров» — квестова (CompanionArcContent.IsQuestChapter),
        /// без гейту лояльності (Максим стартує на Steady, §6.1 №27). Проходить
        /// шлях «громадський суд» (Persuade) — м'якший, ЛОГІЧНО детермінований
        /// тим самим CheckResolver, що й будь-яка інша перевірка; результат
        /// (успіх/невдача) не важливий для самого факту завершення глави —
        /// термінал квесту завершує главу арки ЗАВЖДИ (успіх чи ні).
        /// </summary>
        [Test]
        public void Maksym_ArcChapter1_QuestPlayedToTerminal_CompletesChapterAndLogsEvent()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);
            PlayFullDayQuiet(s); // доба 1 повна — TickCompanionArcs() відкриває главу без гейту

            Assert.IsTrue(s.IsArcChapterAvailable("maksym"), "Максим стартує на Steady — перша глава без гейту (§6.1 №27)");
            Assert.IsTrue(s.IsArcChapterQuestContent("maksym"), "глава 1 Максима — квестова («Не за кров»), не сценова");
            Assert.AreEqual(SessionState.Morning, s.State, "BeginArcChapterQuest кличеться просто в Morning, без ConfirmMorning");

            var offer = s.BeginArcChapterQuest("maksym");
            Assert.IsNotNull(offer);

            bool sawBegun = false;
            foreach (var e in s.DayLog)
                if (e.Key == "arc.chapter_begun" && e.Args["companionId"] == "maksym" &&
                    e.Args["arcId"] == "arc_maksym" && e.Args["chapterId"] == "ch1" &&
                    e.Args["chapterTitleKey"] == "arc.maksym.ch1.title")
                    sawBegun = true;
            Assert.IsTrue(sawBegun);

            // "path"-вибір: 1 = "justice" (громадський суд, next=2 -> justice_check).
            s.ResolveQuestChoice(1);

            // ResolveQuestChoice ЗАВЖДИ скидає _currentQuestOffer (навіть не
            // на терміналі) — новий крок треба знову запросити тим самим
            // OfferQuestStage, яким і водій ботів (BotRunner.MaybeAdvanceMaksymArcQuest)
            // просуває цю саму главу по одній стадії за раз.
            s.OfferQuestStage(Game.Core.Quests.DefaultQuests.MaksymCh1Id);
            // Check-стадія: індекс ігнорується (QuestRun.ResolveCheck), результат
            // веде прямо на терміналну Outcome-стадію "justice_done" (Terminal=true).
            s.ResolveQuestChoice(0);

            bool sawCompleted = false;
            foreach (var e in s.DayLog)
                if (e.Key == "arc.chapter_completed" && e.Args["companionId"] == "maksym" &&
                    e.Args["arcId"] == "arc_maksym" && e.Args["chapterId"] == "ch1" &&
                    e.Args["chapterTitleKey"] == "arc.maksym.ch1.title")
                    sawCompleted = true;
            Assert.IsTrue(sawCompleted,
                "термінал квестової глави (успіх чи невдача) мав завершити главу арки тим самим шляхом, що й сценова (CompleteArcChapterFor)");
            Assert.IsTrue(FindJournalEntry(s.GetMechanicsJournal(), "arc_chapter").Seen,
                "GetMechanicsJournal() мав позначити arc_chapter побаченим");
        }

        /// <summary>
        /// Фікс-ревью (адверсаріал-огляд Поправки №7.8, save/load): квестова
        /// глава арки (Максим ч.1) реєструє своє QuestDefinition в пулі
        /// _quests ЛІНИВО, лише всередині BeginArcChapterQuest — а лінк
        /// questId→companionId (_activeArcChapterQuestCompanion) живе тільки
        /// в пам'яті цього інстансу. Жодне з двох НЕ потрапляло в сейв: до
        /// цього фіксу Save (Morning, дозволено §R13) посередині квесту й
        /// Load у СВІЖОМУ інстансі (як після перезапуску застосунку — те, що
        /// й перевіряє цей тест через PreloadSlot+ContinueGame, а не
        /// LoadState того самого об'єкта) тихо губили прогін квесту цілком
        /// (QuestLog.RestoreState відкидає запис, чийого визначення нема в
        /// пулі — ";quests=" іде в блобі ПЕРЕД ";arc=") — глава арки лишалась
        /// InProgress НАЗАВЖДИ, а термінал квеста ніколи не діставав шансу
        /// завершити її. Фікс — GameSession.ReattachInProgressArcChapterQuests,
        /// що читає "arc=" ДО основного проходу ApplySave й реєструє
        /// визначення заздалегідь.
        /// </summary>
        [Test]
        public void SaveLoad_MidMaksymArcQuest_AcrossInstance_RestoresQuestAndCompletesChapter()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);
            PlayFullDayQuiet(s);

            var offer = s.BeginArcChapterQuest("maksym");
            Assert.IsNotNull(offer);
            s.ResolveQuestChoice(1); // "path" -> justice_check

            // Save mid-quest, in Morning (allowed, §R13).
            Assert.AreEqual(SessionState.Morning, s.State);
            string blob = s.SaveState(0);

            // Свіжий інстанс — так, як після перезапуску застосунку (той самий
            // контракт, що інші SaveLoad_NewInstance-тести цього файлу).
            var s2 = new GameSession();
            s2.PreloadSlot(0, blob);
            bool ok = s2.ContinueGame(0);
            Assert.IsTrue(ok, "ContinueGame мав завантажити щойно підкладений слот");

            var stageOffer = s2.OfferQuestStage(Game.Core.Quests.DefaultQuests.MaksymCh1Id);
            Assert.IsNotNull(stageOffer, "квест «Не за кров» мав відновитись на стадії justice_check після Load у новому інстансі");
            s2.ResolveQuestChoice(0);

            bool sawCompleted = false;
            foreach (var e in s2.DayLog)
                if (e.Key == "arc.chapter_completed" && e.Args["companionId"] == "maksym") sawCompleted = true;
            Assert.IsTrue(sawCompleted,
                "лінк questId→companionId мав відновитись разом з квестом — термінал завершив главу арки й після Save/Load");
        }

        // ---- Морнінг-команди: Assign/Order*/Preview/Depart/Quest/Build/Equip/Craft ----

        [Test]
        public void Assign_And_Unassign_UpdateSlotsAndLog()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            var result = s.Assign("maksym", "scouting_post");
            Assert.AreEqual(AssignmentResult.Success, result);
            s.Unassign("scouting_post");

            bool sawMade = false, sawCleared = false;
            foreach (var e in s.DayLog)
            {
                if (e.Key == "assign.made") sawMade = true;
                if (e.Key == "assign.cleared") sawCleared = true;
            }
            Assert.IsTrue(sawMade);
            Assert.IsTrue(sawCleared);
        }

        [Test]
        public void OrderBuilding_And_CouncilOrders_ReachCityWorks()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            // Council Hall вже в StartingSet (§3.0), тож OrderDiplomacy не
            // впирається у NoCouncilHall — далі це вже питання гаманця
            // (плейсхолдер-старт 40 золота, Дипломатія коштує 25 — легальний
            // NotEnoughGold теж підтверджує, що команда дійшла до CityWorks).
            var diplomacy = s.OrderDiplomacy(Game.Core.Factions.DefaultFactions.Community);
            Assert.AreNotEqual(CouncilOrderResult.NoCouncilHall, diplomacy,
                "Зал совета вже стоїть у StartingSet — команда не повинна впиратись у його відсутність");
            Assert.AreEqual(CouncilOrderResult.Applied, diplomacy, "40 стартового золота вистачає на 25 Дипломатії");

            // Стройку перевіряємо окремою, ще не витраченою частиною гаманця
            // (Дипломатія + Майстерня разом перевищили б стартовий плейсхолдер-
            // капітал — не про це цей тест).
            var s2 = new GameSession();
            s2.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s2);

            var buildResult = s2.OrderBuilding(Game.Core.Base.DefaultBuildings.Workshop);
            Assert.AreEqual(BuildOrderResult.Started, buildResult);

            var city = s2.GetCityView();
            bool building = false;
            foreach (var b in city.InProgress) if (b.Id == Game.Core.Base.DefaultBuildings.Workshop) building = true;
            Assert.IsTrue(building);
        }

        /// <summary>
        /// Фікс-ревью (блокер, знайдено тур-автоплеєм §"тактичні бої"):
        /// PreviewExpedition(Delve) кличе BuildDungeonRoomView(rooms[0]) ДО
        /// DepartExpedition, коли _dungeon ще null — QuietBestActorId
        /// (ціль 6) раніше безумовно читав _dungeon.PartyIds і падав
        /// NullReferenceException на КОЖЕН прев'ю вилазки-данжу (тур
        /// впав з кодом виходу 2 на MaybeDepartDelve).
        /// </summary>
        [Test]
        public void PreviewExpedition_Delve_BeforeDeparture_DoesNotThrow_AndStillNamesQuietCandidate()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            ExpeditionPreviewView preview = null;
            Assert.DoesNotThrow(() =>
                preview = s.PreviewExpedition(DefaultDungeon.AbandonedCamp, Game.Core.Expeditions.ExpeditionApproach.Delve,
                    new[] { "protagonist", "maksym", "myroslava" }));

            Assert.IsNotNull(preview);
            Assert.IsTrue(preview.IsDelve);
            Assert.IsNotNull(preview.FirstRoom);
            Assert.IsTrue(preview.FirstRoom.QuietHasCandidate, "кандидат мав рахуватись із МАЙБУТНЬОЇ партії (companionIds), не з ще неіснуючого _dungeon.PartyIds");
            Assert.AreEqual("maksym", preview.FirstRoom.QuietBestActorId);
        }

        [Test]
        public void PreviewExpedition_And_DepartExpedition_Quiet_TicksHomeAndBanksLoot()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            var preview = s.PreviewExpedition("outskirts", Game.Core.Expeditions.ExpeditionApproach.Quiet,
                new[] { "maksym", "myroslava" });
            Assert.AreEqual("outskirts", preview.SiteId);
            Assert.IsFalse(preview.IsDelve);

            var dispatch = s.DepartExpedition("outskirts", Game.Core.Expeditions.ExpeditionApproach.Quiet,
                new[] { "maksym", "myroslava" }, preview.Days);
            Assert.AreEqual(Game.Core.Base.DispatchResult.Success, dispatch);

            // "Ближні розвалини" (outskirts) — 4 доби тихим шляхом: доганяємо цикли,
            // доки відряд не повернеться (expedition.returned у стрічці подій).
            var log = new List<GameEvent>();
            for (int i = 0; i < preview.Days + 1 && !SawEvent(log, "expedition.returned"); i++)
                PlayFullDayQuiet(s, log);
            Assert.IsTrue(SawEvent(log, "expedition.returned"), "відряд мав повернутись протягом заявлених діб");
        }

        /// <summary>
        /// D1b: те саме, що Quiet вище, але Forceful-підхід (§4.11 R15 — той
        /// самий єдиний вхід DepartExpedition, лише інший ForcefulSkill/дні).
        /// "Ближні розвалини" мають Threshold=3 (DefaultSites.Outskirts); Максим
        /// (Melee 6) веде відряд — детермінований запас над порогом (без
        /// жодного кубика, R1), тож preview.ExpectedBand не може бути Worst, а
        /// ExpectedMaterials/ExpectedGold — додатні (BaseMaterials=2/BaseGold=8,
        /// bandMult>0 для будь-якої не-Worst полоси). Порівнюємо не сирі суми
        /// гаманця (їх забруднює звичайне виробництво циклу за ті самі доби), а
        /// сам факт полоси повернення — той самий доказ, що прев'ю обіцяло.
        /// </summary>
        [Test]
        public void PreviewExpedition_And_DepartExpedition_Forceful_ReturnsNonWorstBand_WithPositiveExpectedMaterials()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            var preview = s.PreviewExpedition("outskirts", Game.Core.Expeditions.ExpeditionApproach.Forceful,
                new[] { "maksym", "myroslava" });
            Assert.AreEqual("outskirts", preview.SiteId);
            Assert.IsFalse(preview.IsDelve);
            Assert.AreNotEqual("Worst", preview.ExpectedBand, "Максим (Melee 6) мав з запасом здолати Threshold=3 outskirts силою");
            Assert.Greater(preview.ExpectedMaterials, 0, "детермінований прев'ю (R1) мав пообіцяти матеріали за не-Worst полосою");
            Assert.Greater(preview.ExpectedGold, 0);

            var dispatch = s.DepartExpedition("outskirts", Game.Core.Expeditions.ExpeditionApproach.Forceful,
                new[] { "maksym", "myroslava" }, preview.Days);
            Assert.AreEqual(Game.Core.Base.DispatchResult.Success, dispatch);
            Assert.AreEqual(SessionState.Morning, s.State, "силова (не-Delve) вилазка не рухає стан з Morning");

            var log = new List<GameEvent>();
            for (int i = 0; i < preview.Days + 1 && !SawEvent(log, "expedition.returned"); i++)
                PlayFullDayQuiet(s, log);

            string returnedBand = null;
            foreach (var e in log)
                if (e.Key == "expedition.returned") returnedBand = e.Args["band"];
            Assert.IsNotNull(returnedBand, "силовий відряд мав повернутись протягом заявлених діб");
            Assert.AreNotEqual("Worst", returnedBand, "фактична полоса повернення мала збігтися з детермінованим прев'ю — матеріали справді прийшли, не 0");
        }

        [Test]
        public void OfferQuestStage_And_ResolveQuestChoice_Hafiya_Accept()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            var offer = s.OfferQuestStage(Game.Core.Quests.DefaultQuests.HafiyaId);
            Assert.IsNotNull(offer);
            Assert.AreEqual(Game.Core.Quests.DefaultQuests.HafiyaId, offer.QuestId);
            Assert.AreEqual(2, offer.Options.Count);

            s.ResolveQuestChoice(0); // accept

            bool sawResolved = false;
            foreach (var e in s.DayLog) if (e.Key == "quest.choice.resolved") sawResolved = true;
            Assert.IsTrue(sawResolved);
        }

        [Test]
        public void BuildPlan_Preview_And_Commit_SpendsBankedPoints()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            // Протагоніст банкує очки (R11) лише через XP — надаємо їх напряму
            // тим самим шляхом, яким це робить квест/бій (GrantXp, internal-приватний
            // у GameSession) не доступний тесту, тож імітуємо джерело точок так,
            // як це реально трапляється у грі: через квестову нагороду XP.
            s.OfferQuestStage(Game.Core.Quests.DefaultQuests.HafiyaId);
            s.ResolveQuestChoice(0); // приймає пропозицію -> етап "grass"
            s.OfferQuestStage(Game.Core.Quests.DefaultQuests.HafiyaId);
            s.ResolveQuestChoice(0); // резолв перевірки -> термінал з WithXp(10) чи 30

            var plan = new Game.Core.Characters.Build.BuildPlan();
            var preview = s.PreviewBuildPlan(GameSession.ProtagonistId, plan);
            Assert.AreEqual(Game.Core.Characters.Build.BuildPlanStatus.Ok, preview.Status);
        }

        [Test]
        public void Equip_Unequip_Craft_RoundTripOnDroppedItem()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            var dispatch = s.DepartExpedition("outskirts", Game.Core.Expeditions.ExpeditionApproach.Forceful,
                new[] { "maksym" }, 2);
            Assert.AreEqual(Game.Core.Base.DispatchResult.Success, dispatch);

            var log = new List<GameEvent>();
            for (int i = 0; i < 4 && !SawEvent(log, "expedition.returned"); i++)
                PlayFullDayQuiet(s, log);
            Assert.IsTrue(SawEvent(log, "expedition.returned"));

            var stash = s.GetStash();
            Assert.Greater(stash.Count, 0, "силовий підхід на Базовій+ полосі мав скинути хоч один предмет у сташ");

            var item = stash[0];
            bool equipped = s.Equip("maksym", item.InstanceId, item.Slot);
            Assert.IsTrue(equipped);

            bool unequipped = s.Unequip("maksym", item.Slot);
            Assert.IsTrue(unequipped);
            Assert.AreEqual(1, s.GetStash().Count);
        }

        // ---- Данж (Delve): кровавий шлях бойової кімнати → бій (SuspendReason.DungeonCombatRoom) ----

        [Test]
        public void Dungeon_Delve_CombatRoom_Bloody_SuspendsToBattle_ThenBackToDungeon()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            var dispatch = s.DepartExpedition(DefaultDungeon.AbandonedCamp, Game.Core.Expeditions.ExpeditionApproach.Delve,
                new[] { "protagonist", "maksym", "myroslava" }, 2);
            Assert.AreEqual(Game.Core.Base.DispatchResult.Success, dispatch);
            Assert.AreEqual(SessionState.Dungeon, s.State);

            var duringBattle = s.ResolveDungeonRoom(IncidentPath.Bloody);
            Assert.IsNull(duringBattle);
            Assert.AreEqual(SessionState.Battle, s.State);

            s.CombatAutoResolve();

            // OnBattleResolved повертає State=_resume.ReturnState=Dungeon для
            // DungeonCombatRoom безумовно (акцептанс D1), а вже далі FinishDungeonCombat
            // або лишає сесію в Dungeon (кімната пройдена), або, якщо цей конкретний
            // детермінований 3v2 пішов не на користь відряду (Worst-полоса бою),
            // веде до Morning через dungeon.wiped — обидва наслідки коректні,
          // третього не існує (RequireBattle/OnBattleResolved не могли лишити
            // сесію в Battle чи в іншому стані).
            bool wiped = false;
            foreach (var e in s.DayLog) if (e.Key == "dungeon.wiped") wiped = true;
            Assert.AreEqual(wiped ? SessionState.Morning : SessionState.Dungeon, s.State);

            // Фікс-ревью D1b (мажор): "combat.autoresolved"/"combat.battle.resolved"
            // тепер обираються за тим, ЯК саме завершився бій (CombatAutoResolve
            // vs покрокові команди), а не за SuspendReason — цей бій справжній
            // (DungeonCombatRoom), але довершений автобоєм, тож несе саме
            // "combat.autoresolved".
            bool sawAutoResolved = false, sawManuallyResolved = false;
            foreach (var e in s.DayLog)
            {
                if (e.Key == "combat.autoresolved") sawAutoResolved = true;
                if (e.Key == "combat.battle.resolved") sawManuallyResolved = true;
            }
            Assert.IsTrue(sawAutoResolved, "CombatAutoResolve мав залогувати combat.autoresolved незалежно від SuspendReason");
            Assert.IsFalse(sawManuallyResolved, "combat.battle.resolved — лише для бою, дограного покроковими командами");
        }

        // ---- Полірування (ціль 6 «Рішення»): картка кімнати данжу ----

        [Test]
        public void GetDungeonView_CombatRoom_ExposesPartyEnemyCountAndQuietCandidate()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            var dispatch = s.DepartExpedition(DefaultDungeon.AbandonedCamp, Game.Core.Expeditions.ExpeditionApproach.Delve,
                new[] { "protagonist", "maksym", "myroslava" }, 2);
            Assert.AreEqual(Game.Core.Base.DispatchResult.Success, dispatch);

            var view = s.GetDungeonView();
            Assert.IsNotNull(view);
            CollectionAssert.AreEquivalent(new[] { "protagonist", "maksym", "myroslava" }, view.PartyIds,
                "owner: 'dungeon room card shows the party'");

            var room = view.CurrentRoom;
            Assert.IsNotNull(room);
            Assert.AreEqual(2, room.EnemyCount, "room1 (scouts_left_behind) несе двох horde_skirmisher — owner: 'тактичний бій: N ворогів'");
            Assert.IsTrue(room.HasQuietBypass);
            Assert.IsTrue(room.QuietHasCandidate, "owner: 'the quiet candidate'");
            Assert.AreEqual("maksym", room.QuietBestActorId, "Максим — Survival 5, найкращий у party на перший QuietCheck (Survival≥5)");
        }

        [Test]
        public void Dungeon_PushDeeper_ResolveEvent_Extract_BanksLootToBaseState()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            s.DepartExpedition(DefaultDungeon.AbandonedCamp, Game.Core.Expeditions.ExpeditionApproach.Delve,
                new[] { "protagonist", "maksym", "myroslava" }, 2);

            // Кімната 1 (бій) — тихий обхід: Survival/Persuade ≥5, партія сильна.
            var afterRoom1 = s.ResolveDungeonRoom(IncidentPath.Quiet);
            Assert.IsNotNull(afterRoom1, "тихий обхід кімнати 1 не повинен вести в бій за таким сильним відрядом");

            var afterPush2 = s.PushDeeper(); // кімната 2: схованка (гарантований лут)
            Assert.AreEqual("hidden_cache", afterPush2.CurrentRoom.Id);

            var afterCache = s.ResolveDungeonRoom(IncidentPath.Quiet); // Cache не має шляху — розв'язується як є
            Assert.IsNotNull(afterCache);

            var afterPush3 = s.PushDeeper(); // кімната 3: подія-вибір
            Assert.AreEqual("hidden_ashes", afterPush3.CurrentRoom.Id);

            s.ResolveDungeonEvent(1); // "обережно": менше здобичі, без Threat

            int goldBefore = s.GetEconomyView().Gold;
            s.ExtractDungeon();
            Assert.AreEqual(SessionState.Morning, s.State);
            Assert.GreaterOrEqual(s.GetEconomyView().Gold, goldBefore, "Extract має забанкувати незабанковане в BaseState.Resources");
        }

        /// <summary>
        /// D1b (seamsForD1 B3, «Ріг вивідника» item.scout_horn, ефект
        /// «forewarn_boost», documented deterministic equivalent через
        /// новий адитивний шов WorldPulse.BoostCharge): здобуття рогу в
        /// кімнаті 2 «Схованка» одразу і детерміновано підіймає заповнення
        /// єдиного Announces-накопичувача кампанії (Тугар, §3.3) — приховане
        /// число (R17), перевіряється лише IVT-гачком.
        /// </summary>
        [Test]
        public void Dungeon_HiddenCache_GrantsScoutHorn_BoostsTuharPulseFill()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            double fillBefore = s.DebugTuharPulseFill;

            s.DepartExpedition(DefaultDungeon.AbandonedCamp, Game.Core.Expeditions.ExpeditionApproach.Delve,
                new[] { "protagonist", "maksym", "myroslava" }, 2);
            s.ResolveDungeonRoom(IncidentPath.Quiet); // кімната 1, тихий обхід
            s.PushDeeper(); // кімната 2: схованка з "Ріг вивідника"
            s.ResolveDungeonRoom(IncidentPath.Quiet); // грант предмета всередині ApplyDungeonResolution

            bool sawBoostEvent = false;
            foreach (var e in s.DayLog) if (e.Key == "item.scout_horn.forewarn_boosted") sawBoostEvent = true;
            Assert.IsTrue(sawBoostEvent, "здобуття рогу мало залогувати item.scout_horn.forewarn_boosted");

            Assert.Greater(s.DebugTuharPulseFill, fillBefore,
                "Ріг вивідника мав детерміновано підняти заповнення накопичувача Тугара (наступні попередження — раніше/легше)");
        }

        /// <summary>
        /// Фікс-ревью D1b (WorldPulse.BoostCharge): одноразовий стрибок
        /// боста не сміє сам дістати чи перескочити Threshold. У Тугара
        /// (Kind=InternalThreat) PressureTrack.IsReady() вимагає лише
        /// Charge&gt;=Threshold — без прив'язки до почутих попереджень, а
        /// зареєстрованого інциденту з SourceId=="tuhar" ще нема (в перші
        /// 5 діб у нього тільки драбина передвісників, справжня розв'язка —
        /// в сценарному Фіналі), тож WorldPulse.Fire() тут мовчки скидає
        /// Charge/DeliveredLevel без жодної події в лозі. Якщо гравець
        /// знаходить ріг ПІЗНІШЕ (не на добу 1, а після кількох діб
        /// природного накопичення — саме так за сценарієм §3.4 і буває,
        /// ріг лежить у кімнаті 2 доби 4), одноразовий +30 заряду міг ЗРАЗУ,
        /// в тому самому виклику BoostCharge, перескочити Threshold — це
        /// найгостріший і повністю усувний випадок дефекту, і саме його
        /// закриває клямп у WorldPulse.BoostCharge.
        ///
        /// ЗАКРИТО 24.09.2026 (було відкритим питанням цього тесту): звичне
        /// накопичення Тугара (тіка КОЖНУ фазу — §1 рядок 8) саме по собі
        /// дістає Threshold=60 на добі 3 і без рогу, а інциденту Тугара нема —
        /// WorldPulse мовчки скидав заряд. Тепер Advance розряджає лише
        /// джерело, для якого є інцидент (IncidentStep.HasIncidentFor); сторож
        /// на кілька діб уперед —
        /// <see cref="TuharPulse_QuietFirstHour_NeverRegressesWithoutALoggedIncident"/>.
        /// </summary>
        [Test]
        public void ScoutHornBoost_WhenChargeAlreadyNearThreshold_ClampsInsteadOfOvershooting()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            // Дві тихі доби природного накопичення Тугара (~0.8 заповнення на
            // кінець доби 2 — day2 end=48/60 у сценарії §3.2-3.3), АНАЛОГІЧНО
            // до того, як за сценарієм §3.4 ріг насправді лежить не на добу 1,
            // а на добу 4 — Тугар на той момент уже НЕ порожній.
            PlayFullDayQuiet(s);
            PlayFullDayQuiet(s);
            double fillBeforeHorn = s.DebugTuharPulseFill;
            Assert.Less(fillBeforeHorn, 1.0, "передумова: до рогу накопичувач ще не мав вистрелити сам");

            s.DepartExpedition(DefaultDungeon.AbandonedCamp, Game.Core.Expeditions.ExpeditionApproach.Delve,
                new[] { "protagonist", "maksym", "myroslava" }, 2);
            s.ResolveDungeonRoom(IncidentPath.Quiet);
            s.PushDeeper();
            s.ResolveDungeonRoom(IncidentPath.Quiet); // грант рогу — одноразовий +30 заряду

            double fillAfterHorn = s.DebugTuharPulseFill;
            Assert.Greater(fillAfterHorn, fillBeforeHorn, "ріг усе одно мав підняти заповнення");
            Assert.Less(fillAfterHorn, 1.0,
                "одноразовий буст НЕ сміє сам дістати чи перескочити Threshold (інакше миттєвий мовчазний Fire без інциденту " +
                "«з'їдає» саме ту вигоду, яку ріг обіцяє)");
        }

        /// <summary>
        /// Накопичувач Тугара не розряджається мовчки (закриває ВІДКРИТЕ
        /// ПИТАННЯ тесту вище). Тугар тіка кожну фазу (§1 рядок 8) і сам
        /// дістає Threshold=60 на денну фазу доби 3, а інциденту з
        /// SourceId=="tuhar" у першій годині немає — його розв'язка це
        /// сценарний Фінал. Раніше WorldPulse.Fire() тут-таки скидав заряд у
        /// нуль, IncidentStep не знаходив інциденту і мовчки йшов далі.
        ///
        /// Правило, яке тримає тест: заповнення і почута ступінь Тугара між
        /// фазами НЕ падають, якщо в лозі цієї фази немає decision.resolved
        /// інциденту Тугара. Пул таких інцидентів збирається тут з того ж
        /// контенту, що й FirstHourWorld, — тест не зламається, коли Тугар
        /// дістане власний інцидент. І драбина 1→2→3 доходить до гравця ДО
        /// Фіналу, кожна ступінь — окремою подією forewarn.level{n}.
        /// </summary>
        [Test]
        public void TuharPulse_QuietFirstHour_NeverRegressesWithoutALoggedIncident()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            var tuharIncidentIds = new HashSet<string>();
            foreach (var d in Game.Core.World.DefaultIncidents.All())
                if (d.SourceId == Game.Core.World.OpeningContent.TuharSourceId) tuharIncidentIds.Add(d.Id);
            foreach (var d in Game.Core.World.OpeningContent.All())
                if (d.SourceId == Game.Core.World.OpeningContent.TuharSourceId) tuharIncidentIds.Add(d.Id);

            double lastFill = s.DebugTuharPulseFill;
            int lastLevel = s.DebugTuharDeliveredLevel;
            var heardLevels = new List<int>();

            // Лог фази читається ПІСЛЯ всіх рішень фази: DayLog очищують лише
            // AdvanceDay/AdvanceNight, тож на цей момент він повний.
            void CheckPhase(string phase)
            {
                bool dischargedByIncident = false;
                foreach (var e in s.DayLog)
                {
                    if (e.Key == "decision.resolved" && e.Args.TryGetValue("incidentId", out var id)
                        && tuharIncidentIds.Contains(id))
                        dischargedByIncident = true;

                    if (e.Key.StartsWith("forewarn.level", StringComparison.Ordinal)
                        && e.Args.TryGetValue("subject", out var subject)
                        && subject == Game.Core.World.OpeningContent.TuharSourceId)
                        heardLevels.Add(int.Parse(e.Key.Substring("forewarn.level".Length)));
                }

                double fill = s.DebugTuharPulseFill;
                int level = s.DebugTuharDeliveredLevel;
                if (!dischargedByIncident)
                {
                    Assert.GreaterOrEqual(fill, lastFill,
                        $"{phase}: заповнення Тугара впало {lastFill:0.00} → {fill:0.00} без інциденту Тугара в лозі — мовчазний розряд");
                    Assert.GreaterOrEqual(level, lastLevel,
                        $"{phase}: почута ступінь Тугара впала {lastLevel} → {level} без інциденту Тугара в лозі");
                }
                lastFill = fill;
                lastLevel = level;
            }

            for (int day = 1; day <= 5; day++)
            {
                s.ConfirmMorning();
                var report = s.AdvanceDay();
                while (report != null && report.AwaitsDecision)
                    report = s.ResolveIncident(IncidentPath.Quiet);
                if (s.State == SessionState.Scene)
                {
                    SceneStepView step;
                    do { step = s.AdvanceScene(); } while (!step.IsFinished);
                }
                CheckPhase($"доба {day}, день");

                // Ніч доби 5 — Фінал, його розв'язує ResolveFinale, а не
                // конвеєр: вікно першої години закінчується перед ним.
                if (day == 5) break;

                if (s.State == SessionState.Evening) s.ConfirmEvening();
                Assert.AreEqual(SessionState.Night, s.State, $"доба {day}: тихий шлях мав дійти до ночі");
                var night = s.AdvanceNight();
                while (night != null && night.AwaitsDecision)
                    night = s.ResolveIncident(IncidentPath.Quiet);
                CheckPhase($"доба {day}, ніч");
            }

            Assert.AreEqual(3, s.DebugTuharDeliveredLevel,
                "до Фіналу гравець мав почути всю драбину Тугара — інакше Фінал приходить без попередження");
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, heardLevels,
                "драбина Тугара звучить рівно раз на ступінь, по порядку, і не починається знову");
        }

        // ---- Фінал доби 5: кровавий шлях → справжній бій (SuspendReason.FinaleAssault) ----

        [Test]
        public void Finale_Bloody_OnDay5Night_SuspendsToBattle_AndResolves()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            for (int day = 1; day <= 4; day++) PlayFullDayQuiet(s);

            // Доба 5: ранок повернення/попередження кризи вже відбулось усередині
            // AdvanceDay (§3.5); доганяємо до ночі, реагуємо на кризу, тоді фінал.
            s.ConfirmMorning();
            var dayReport = s.AdvanceDay();
            while (dayReport != null && dayReport.AwaitsDecision)
                dayReport = s.ResolveIncident(IncidentPath.Quiet);
            if (s.State == SessionState.Scene)
            {
                SceneStepView step;
                step = RunSceneToFinish(s);
            }
            s.ReactToCrisis(CrisisReaction.SpendGold);
            s.ConfirmEvening();

            Assert.AreEqual(SessionState.Night, s.State);

            // Полірування (ціль 6 «Рішення», owner: "тактичний бій: N ворогів"):
            // прев'ю ДО кліку має збігтись із реальним боєм — чиста функція,
            // виклик двічі поспіль дає те саме число.
            int previewedCount = s.GetFinaleEnemyCount();
            Assert.Greater(previewedCount, 0, "фінальний штурм завжди має бодай Бурунду");
            Assert.AreEqual(previewedCount, s.GetFinaleEnemyCount(), "GetFinaleEnemyCount — чисте читання, без побічних ефектів");

            var duringBattle = s.ResolveFinale(IncidentPath.Bloody);
            Assert.IsNull(duringBattle);
            Assert.AreEqual(SessionState.Battle, s.State);

            var battle = s.GetBattleView();
            Assert.IsNotNull(battle);
            bool hasBurunda = false;
            int actualEnemyCount = 0;
            foreach (var u in battle.Units)
            {
                if (u.Id.Contains("burunda")) hasBurunda = true;
                if (u.Side == "Enemy") actualEnemyCount++;
            }
            Assert.IsTrue(hasBurunda, "фінальний штурм завжди включає Бурунду-бегадира");
            Assert.AreEqual(previewedCount, actualEnemyCount, "прев'ю мало назвати ТУ САМУ кількість, що реально вийшла на поле");

            s.CombatAutoResolve();
            // OnBattleResolved повертає State=_resume.ReturnState=Night для
            // FinaleAssault, а CompleteFinale лишає сесію в Night — далі AdvanceNight
            // сама доводить добу до Summary.
            Assert.AreEqual(SessionState.Night, s.State);

            var night = s.AdvanceNight();
            Assert.AreEqual(SessionState.Summary, s.State);

            var summary = s.GetSummaryView();
            Assert.IsNotNull(summary.FinaleOutcomeKey);

            s.AcknowledgeSummary();
            Assert.AreEqual(SessionState.FreePlay, s.State);
            Assert.IsTrue(s.CurrentView.IsFreePlay);
        }

        /// <summary>
        /// D1b: тихий шлях фіналу (§4.14 B6/openIssue — гірша з двох перевірок
        /// Finale.BuildDam/BuildDamTactics, рішення інтегратора зафіксоване в
        /// <see cref="GameSession.ResolveFinale"/>) не підвішує в Battle взагалі
        /// — на відміну від кровавого, DayReportView готовий одразу.
        /// </summary>
        [Test]
        public void Finale_Quiet_OnDay5Night_ResolvesViaChecks_NoBattle_AndReachesSummary()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            for (int day = 1; day <= 4; day++) PlayFullDayQuiet(s);

            s.ConfirmMorning();
            var dayReport = s.AdvanceDay();
            while (dayReport != null && dayReport.AwaitsDecision)
                dayReport = s.ResolveIncident(IncidentPath.Quiet);
            if (s.State == SessionState.Scene)
            {
                SceneStepView step;
                step = RunSceneToFinish(s);
            }
            s.ReactToCrisis(CrisisReaction.SpendGold);
            s.ConfirmEvening();
            Assert.AreEqual(SessionState.Night, s.State);

            var resolved = s.ResolveFinale(IncidentPath.Quiet);
            Assert.IsNotNull(resolved, "тихий шлях фіналу — перевірки, не бій: DayReportView готовий без підвісу в Battle");
            Assert.AreEqual(SessionState.Night, s.State, "CompleteFinale лишає сесію в Night — далі AdvanceNight сама доводить добу до Summary");

            bool sawFinaleResolved = false;
            foreach (var e in s.DayLog)
                if (e.Key == "finale.resolved" && e.Args["path"] == "Quiet") sawFinaleResolved = true;
            Assert.IsTrue(sawFinaleResolved, "finale.resolved(path=Quiet) мав піти у стрічку подій");

            s.AdvanceNight();
            Assert.AreEqual(SessionState.Summary, s.State);

            var summary = s.GetSummaryView();
            Assert.IsNotNull(summary.FinaleOutcomeKey, "тихий шлях фіналу теж має власну ціну — жодна полоса не «чиста» перемога (§3.5)");

            s.AcknowledgeSummary();
            Assert.AreEqual(SessionState.FreePlay, s.State);
        }

        // ---- Фікс-ревью (роль FIXER, пакет D1a): регрес-тести на кожну знахідку ----

        /// <summary>
        /// Блокер-фікс: раніше AdvanceNight() доби 5 доводив ніч до Summary
        /// незалежно від того, чи розв'язано ResolveFinale — фінал (R8, "РЕАЛЬНИЙ,
        /// не прев'ю") можна було мовчки пропустити (ConfirmEvening→AdvanceNight
        /// напряму), і SummaryView.FinaleOutcomeKey лишався null.
        /// </summary>
        [Test]
        public void AdvanceNight_OnDay5_WithoutResolveFinale_Throws()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            for (int day = 1; day <= 4; day++) PlayFullDayQuiet(s);

            s.ConfirmMorning();
            var dayReport = s.AdvanceDay();
            while (dayReport != null && dayReport.AwaitsDecision)
                dayReport = s.ResolveIncident(IncidentPath.Quiet);
            if (s.State == SessionState.Scene)
            {
                SceneStepView step;
                step = RunSceneToFinish(s);
            }
            s.ReactToCrisis(CrisisReaction.SpendGold);
            s.ConfirmEvening();
            Assert.AreEqual(SessionState.Night, s.State);

            Assert.Throws<InvalidOperationException>(() => s.AdvanceNight(),
                "доба 5: AdvanceNight() не повинен мовчки провести ніч повз нерозв'язаний фінал");

            // Право шлях лишається доступним: ResolveFinale все ще можна
            // викликати після відмови, і AdvanceNight() після нього вже не падає.
            s.ResolveFinale(IncidentPath.Quiet);
            Assert.DoesNotThrow(() => s.AdvanceNight());
            Assert.AreEqual(SessionState.Summary, s.State);
        }

        /// <summary>
        /// Блокер-фікс: §4.1 документує OfferQuestStage/ResolveQuestChoice як
        /// "Morning/Evening" (R6 — поза конвеєром дня), а сценарій доби 2 (§3.2)
        /// додатково кличе їх УНОЧІ ("Ніч | Квест Гафії, етап 1"). Блок
        /// Morning-гвардів (2f860c6) помилково звузив обидві команди до
        /// суцільного RequireState(Morning) — задокументований сценарій падав
        /// би з InvalidOperationException.
        /// </summary>
        [Test]
        public void OfferQuestStage_And_ResolveQuestChoice_FromNight_Succeeds()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            PlayFullDayQuiet(s); // доба 1 -> Morning доби 2

            s.ConfirmMorning();
            var report = s.AdvanceDay(); // доба 2: інцидент spoiled_stores
            while (report != null && report.AwaitsDecision)
                report = s.ResolveIncident(IncidentPath.Quiet);
            if (s.State == SessionState.Evening) s.ConfirmEvening();
            Assert.AreEqual(SessionState.Night, s.State, "доба 2 має дійти до Ночі без сценарних зупинок");

            var offer = s.OfferQuestStage(Game.Core.Quests.DefaultQuests.HafiyaId);
            Assert.IsNotNull(offer, "OfferQuestStage мав спрацювати вночі (§3.2 сценарій доби 2)");

            var afterChoice = s.ResolveQuestChoice(0);
            Assert.IsNotNull(afterChoice);
            bool sawResolved = false;
            foreach (var e in s.DayLog) if (e.Key == "quest.choice.resolved") sawResolved = true;
            Assert.IsTrue(sawResolved);
        }

        /// <summary>
        /// Майор-фікс: TranslateReport логував "day.advanced" на кожен свій
        /// виклик, а ResolveIncident/CompletePassVanguard кличуть його ЗНОВУ в
        /// тій самій фазі (лише довирішуючи вже відкрите рішення, без нового
        /// DayProcessor.Advance()) — DayLog фіксував по 2 "day.advanced" на
        /// фазу з хоч одним рішенням.
        /// </summary>
        [Test]
        public void ResolveIncident_DoesNotDuplicate_DayAdvancedEvent()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            s.ConfirmMorning();
            var report = s.AdvanceDay();
            Assert.IsTrue(report.AwaitsDecision);

            s.ResolveIncident(IncidentPath.Quiet);

            int count = 0;
            foreach (var e in s.DayLog) if (e.Key == "day.advanced") count++;
            Assert.AreEqual(1, count,
                "AdvanceDay рухає DayProcessor рівно раз за фазу — 'day.advanced' не повинен дублюватись " +
                "довирішенням інциденту в тій самій фазі (DayLog — єдине джерело правди, §4.3)");
        }

        /// <summary>
        /// Майор-фікс: конструкторський канал кубика мав пріоритет над
        /// NewGameOptions.Roller лише формально — guard кидав виняток, щойно
        /// конструкторське поле було null, НАВІТЬ якщо o.Roller передано, а
        /// сам клас документує NewGameOptions.Roller як рівноцінний канал
        /// (клас. коментар GameSession). _roller був readonly — навіть минувши
        /// guard, o.Roller ніде реально не читався (RequestBattle/ComposeSave/
        /// ApplySave/NewTrainingBattle бачили лише конструкторське поле).
        /// </summary>
        [Test]
        public void NewGame_PercentRule_HonorsRollerFromNewGameOptions_WhenConstructorRollerIsNull()
        {
            var roller = new ScriptedDiceRoller(0.01, 0.99, 0.01, 0.99, 0.01, 0.99, 0.01, 0.99, 0.01, 0.99);
            var s = new GameSession(); // без кубика в конструкторі

            Assert.DoesNotThrow(() => s.NewGame(new NewGameOptions
            {
                SkipCreation = true,
                HitRule = HitRuleKind.Percent,
                Roller = roller
            }), "NewGameOptions.Roller — задокументований рівноцінний канал інжекції кубика");

            FastForwardOpeningToMorning(s);

            // Не лише проходить guard — справді використовується: якби _roller
            // лишився null (не промотувався за межі NewGame()), тренувальний бій
            // Percent-правилом впав би на ArgumentNullException при першій атаці.
            s.NewTrainingBattle(new TrainingBattleOptions { HitRule = HitRuleKind.Percent });
            Assert.AreEqual(SessionState.Battle, s.State);
            Assert.DoesNotThrow(() => s.CombatAutoResolve());
            Assert.IsNull(s.GetBattleView(), "бій автопройдено — кубик з опцій реально відпрацював");
        }

        /// <summary>
        /// Майор-фікс (§2 №27, seamsForD1 пакета B4): особисті арки напарників
        /// існували в Core/Companions, але Refresh()/подія "arc.chapter_opened"
        /// не звалися нізвідки з GameSession. Максим стартує на полосі Steady
        /// (CompanionSocialBalance: "старт Максима 60 -> Steady") — перша глава
        /// його арки (без RequiresFlag, поріг Steady) мала стати доступною вже
        /// в кінці доби 1, щойно щоденний тик арок нарешті з'явився.
        /// </summary>
        [Test]
        public void TickCompanionArcs_OpensMaksymChapter1_OnFirstDayEnd_SinceHeStartsAtSteady()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            var log = new List<GameEvent>();
            PlayFullDayQuiet(s, log);

            bool sawArcOpened = false;
            foreach (var e in log)
                if (e.Key == "arc.chapter_opened" && e.Args["companionId"] == "maksym") sawArcOpened = true;

            Assert.IsTrue(sawArcOpened,
                "Максим стартує на Steady — перша глава його арки мала відкритись у кінці доби 1 (§2 №27)");
        }

        // ---- R13: побайтова безперервність збереження/завантаження ----

        [Test]
        public void SaveState_Then_RestoreFromBlob_InNewInstance_ProducesIdenticalNextDayReport()
        {
            var baseline = new GameSession();
            baseline.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(baseline);
            PlayFullDayQuiet(baseline); // доба 1 повністю -> Morning доби 2

            Assert.AreEqual(SessionState.Morning, baseline.State);
            string blob = baseline.SaveState(0);
            Assert.IsFalse(string.IsNullOrEmpty(blob));

            // Продовжуємо той самий (безперервний) прогін — контрольний DayReportView доби 2.
            baseline.ConfirmMorning();
            var continuousReport = baseline.AdvanceDay();

            // Новий екземпляр: свіжий світ, той самий сід/правило, а потім —
            // відновлення СЛІПКОМ (а не внутрішнім слотом іншого об'єкта).
            var reloaded = new GameSession();
            reloaded.NewGame(SkipCreationOptions());
            reloaded.RestoreFromBlob(blob);
            Assert.AreEqual(SessionState.Morning, reloaded.State);

            reloaded.ConfirmMorning();
            var reloadedReport = reloaded.AdvanceDay();

            Assert.AreEqual(Summarize(continuousReport), Summarize(reloadedReport),
                "AdvanceDay після SaveState→RestoreFromBlob мав дати структурно ідентичний DayReportView");
        }

        /// <summary>
        /// D1b (§4.8 R13, той самий акцептанс, що B7 просив для ExpeditionParty:
        /// "SaveState посередине вилазки→RestoreState→повернення дає той самий
        /// результат"): звичайна (не-Delve) вилазка НЕ рухає стан з Morning
        /// (§4.11), тож SaveState посередині неї — легальний виклик у Morning,
        /// а не окремий гейт. Партія «в полі» (party.IsAway, ExpeditionResult
        /// ще НЕ заморожений) мусить дожити збереження/завантаження в новому
        /// екземплярі й повернутись з тим самим гаманцем, що безперервний прогін.
        /// </summary>
        [Test]
        public void SaveState_WhilePartyIsAwayOnExpedition_RestoresInNewInstance_AndReturnsIdentically()
        {
            var baseline = new GameSession();
            baseline.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(baseline);
            PlayFullDayQuiet(baseline); // доба 1 -> Morning доби 2 (пости стабілізувались після вузла 1)

            Assert.AreEqual(SessionState.Morning, baseline.State);

            // Forceful (2 доби, не 4 як Quiet) — відряд повертається до доби 5
            // (форсована криза/фінал), а PlayFullDayQuiet нижче не знає про
            // ResolveFinale (тест на «SaveState посередині вилазки», не на
            // фінал — той окремо покритий Finale_Quiet/Finale_Bloody вище).
            var preview = baseline.PreviewExpedition("outskirts", Game.Core.Expeditions.ExpeditionApproach.Forceful,
                new[] { "maksym", "myroslava" });
            var dispatch = baseline.DepartExpedition("outskirts", Game.Core.Expeditions.ExpeditionApproach.Forceful,
                new[] { "maksym", "myroslava" }, preview.Days);
            Assert.AreEqual(Game.Core.Base.DispatchResult.Success, dispatch);
            Assert.AreEqual(SessionState.Morning, baseline.State, "звичайна (не-Delve) вилазка не рухає стан з Morning");

            string blob = baseline.SaveState(0);
            Assert.IsFalse(string.IsNullOrEmpty(blob));

            var baselineLog = new List<GameEvent>();
            for (int i = 0; i < preview.Days + 1 && !SawEvent(baselineLog, "expedition.returned"); i++)
                PlayFullDayQuiet(baseline, baselineLog);
            Assert.IsTrue(SawEvent(baselineLog, "expedition.returned"));
            var baselineEconomy = baseline.GetEconomyView();

            var reloaded = new GameSession();
            reloaded.NewGame(SkipCreationOptions());
            reloaded.RestoreFromBlob(blob);
            Assert.AreEqual(SessionState.Morning, reloaded.State, "відновлення посередині вилазки має лишити сесію в Morning, як і до збереження");

            var reloadedLog = new List<GameEvent>();
            for (int i = 0; i < preview.Days + 1 && !SawEvent(reloadedLog, "expedition.returned"); i++)
                PlayFullDayQuiet(reloaded, reloadedLog);
            Assert.IsTrue(SawEvent(reloadedLog, "expedition.returned"),
                "відряд мав повернутись так само і після Save/Load посередині вилазки");

            var reloadedEconomy = reloaded.GetEconomyView();
            Assert.AreEqual(baselineEconomy.Gold, reloadedEconomy.Gold, "гаманець після Save/Load-посередині-вилазки мав дійти до того самого числа, що безперервний прогін");
            Assert.AreEqual(baselineEconomy.Materials, reloadedEconomy.Materials);
            Assert.AreEqual(baselineEconomy.Food, reloadedEconomy.Food);
        }

        // ---- Фікс-ревью (наступний прохід D1): команди §4.1 без жодного тесту ----

        /// <summary>
        /// Блокер-фікс: <c>ContinueGame(slot)</c> раніше НІКОЛИ не міг успішно
        /// завантажити жоден слот — <c>NewGame()</c> усередині безумовно чистить
        /// <c>_slots</c> ДО того, як <c>LoadState(slot)</c> встигав його прочитати
        /// (той самий словник, спорожнений щойно викликаним <c>NewGame</c>), і при
        /// цьому невдалий виклик однаково встигав збудувати нову гру й піти зі
        /// стану <c>Title</c> перш ніж повернути <c>false</c>. Реальний потік
        /// (Alpha.Play/Unity SaveLoadScreen читає файл слота з диска і кладе
        /// рядок у сесію через новий <c>PreloadSlot</c>, аналогічно тому, як
        /// <see cref="GameSession.RestoreFromBlob"/> уже робить для наскрізного
        /// «зберегти → перезапустити застосунок → відновити») — перевіряємо тут.
        /// </summary>
        [Test]
        public void ContinueGame_FromTitle_WithPreloadedSlot_RestoresSavedDay()
        {
            var source = new GameSession();
            source.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(source);
            PlayFullDayQuiet(source); // доба 1 -> Morning доби 2
            Assert.AreEqual(SessionState.Morning, source.State);
            int savedDay = source.CurrentView.Day;
            string blob = source.SaveState(0);

            // Свіжий інстанс — так, як після перезапуску застосунку: Title,
            // порожні слоти, жодного NewGame ще не було.
            var s = new GameSession();
            Assert.AreEqual(SessionState.Title, s.State);
            s.PreloadSlot(0, blob);

            bool ok = s.ContinueGame(0);
            Assert.IsTrue(ok, "ContinueGame мав завантажити щойно підкладений слот");
            Assert.AreEqual(SessionState.Morning, s.State);
            Assert.AreEqual(savedDay, s.CurrentView.Day, "день після ContinueGame мав збігтися зі збереженим");
        }

        [Test]
        public void ContinueGame_FromTitle_EmptySlot_ReturnsFalse_AndDoesNotMoveOffTitle()
        {
            var s = new GameSession();
            Assert.AreEqual(SessionState.Title, s.State);

            bool ok = s.ContinueGame(2);

            Assert.IsFalse(ok, "порожній слот не можна продовжити");
            Assert.AreEqual(SessionState.Title, s.State,
                "невдалий ContinueGame не повинен лишати сесію на півдорозі в новозбудованому світі — це досі Title");
        }

        /// <summary>
        /// Фаза F (UI-tour autoplay, докладено §"CORE GAPS" TEST_BUILD.md):
        /// ComposeSave/ApplySave раніше НІКОЛИ не переносили
        /// _pendingName/_pendingGender/_pendingBackgroundId — ContinueGame()
        /// кличе NewGame(SkipCreation:true), яка навмисно НЕ скидає ці поля
        /// (лишає дефолти щойно сконструйованого інстансу), тож після Load
        /// GetProtagonistCreationView() і сам об'єкт протагоніста в ростері
        /// мовчки відкочувались до Gender.Male/"warrior"/дефолтного імені.
        /// </summary>
        [Test]
        public void ContinueGame_PreservesProtagonistGenderNameAndBackground()
        {
            var source = new GameSession();
            source.NewGame(new NewGameOptions { SkipCreation = false, HitRule = HitRuleKind.Threshold });
            Assert.AreEqual(SessionState.Creation, source.State);

            source.SetProtagonistName("Оксана");
            source.SetProtagonistGender(Game.Core.Characters.Creation.Gender.Female);
            source.SetProtagonistBackground("healer");
            source.ConfirmCreation();
            Assert.AreEqual(SessionState.Scene, source.State);

            SceneStepView step;
            step = RunSceneToFinish(source);
            Assert.AreEqual(SessionState.Morning, source.State);

            string blob = source.SaveState(0);

            var s = new GameSession();
            Assert.AreEqual(SessionState.Title, s.State);
            s.PreloadSlot(0, blob);
            bool ok = s.ContinueGame(0);
            Assert.IsTrue(ok, "ContinueGame мав завантажити щойно підкладений слот");

            var view = s.GetProtagonistCreationView();
            Assert.AreEqual(Game.Core.Characters.Creation.Gender.Female, view.Gender, "рід протагоніста мав пережити Save/Load");
            Assert.AreEqual("healer", view.BackgroundId, "передісторія мала пережити Save/Load");

            var roster = s.GetRosterView();
            Game.Core.Session.Views.CompanionSummary protagonist = null;
            foreach (var c in roster.Companions) if (c.Id == GameSession.ProtagonistId) protagonist = c;
            Assert.IsNotNull(protagonist);
            Assert.AreEqual("Оксана", protagonist.DisplayName, "ім'я протагоніста мало пережити Save/Load");
        }

        [Test]
        public void CommitBuildPlan_WithoutConfirmation_ReturnsNotConfirmed_AndLogsNothing()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            var plan = new Game.Core.Characters.Build.BuildPlan();
            var status = s.CommitBuildPlan(GameSession.ProtagonistId, plan, confirmedIrreversible: false);

            Assert.AreEqual(Game.Core.Characters.Build.BuildPlanStatus.NotConfirmed, status);
            Assert.IsFalse(SawEvent(new List<GameEvent>(s.DayLog), "progression.build_committed"));
        }

        [Test]
        public void CommitBuildPlan_Confirmed_AppliesPlan_AndLogsProgressionCommitted()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            var plan = new Game.Core.Characters.Build.BuildPlan();
            var status = s.CommitBuildPlan(GameSession.ProtagonistId, plan, confirmedIrreversible: true);

            Assert.AreEqual(Game.Core.Characters.Build.BuildPlanStatus.Ok, status);
            bool sawCommitted = false;
            foreach (var e in s.DayLog) if (e.Key == "progression.build_committed") sawCommitted = true;
            Assert.IsTrue(sawCommitted, "CommitBuildPlan мав залогувати progression.build_committed (§2 рядок 30)");
        }

        /// <summary>
        /// Детермінований (R1, без кубика) розклад: Forceful+"maksym" самотою
        /// на outskirts (Threshold=3, ForcefulSkill=Melee, Максим Melee=6, без
        /// трейтів/атрибутного бонуса на Neutral-підході) дає запас (margin) 3 —
        /// рівно <c>CheckBalance.GoodMargin</c>, отже полоса Good: <c>DropTable</c>
        /// (без <c>AddNamed</c>) віддає безіменний <c>HuntersBow</c> рідкості
        /// <c>Rarity.Rare</c> — ніколи не Epic/іменний, тож <c>CraftUpgrade</c>
        /// не може впертися в AlreadyMaxRarity/NamedNotUpgradable і чесно
        /// перевіряє саме шов "чи відкрита Майстерня" (workshopOpen з CityWorks).
        ///
        /// <c>TestBuildOneDayConstruction = false</c> — навмисно: цей тест
        /// перевіряє БАЛАНС КАМПАНІЇ (проєктні п'ять стадій, Майстерня
        /// Days=4), а не тестову збірку (Поправка №7.7). Тестову збірку (один
        /// день, дефолт) перевіряє
        /// <see cref="CraftUpgrade_TestBuild_WorkshopOpensNextMorning_CraftReachableByDay5"/>.
        /// </summary>
        [Test]
        public void CraftUpgrade_WorkshopClosed_ThenReachesCraftSystem_OnceWorkshopBuilt()
        {
            var s = new GameSession();
            s.NewGame(new NewGameOptions
                { SkipCreation = true, HitRule = HitRuleKind.Threshold, TestBuildOneDayConstruction = false });
            FastForwardOpeningToMorning(s);

            var buildResult = s.OrderBuilding(Game.Core.Base.DefaultBuildings.Workshop);
            Assert.AreEqual(BuildOrderResult.Started, buildResult);

            var dispatch = s.DepartExpedition("outskirts", Game.Core.Expeditions.ExpeditionApproach.Forceful,
                new[] { "maksym" }, 2);
            Assert.AreEqual(Game.Core.Base.DispatchResult.Success, dispatch);

            Game.Core.Items.ItemInstance item = null;
            bool workshopBuilt = false;
            var log = new List<GameEvent>();
            for (int i = 0; i < 6 && !workshopBuilt; i++)
            {
                PlayFullDayQuiet(s, log);

                if (item == null && s.GetStash().Count > 0) item = s.GetStash()[0];

                foreach (var b in s.GetCityView().Built)
                    if (b.Id == Game.Core.Base.DefaultBuildings.Workshop) workshopBuilt = true;

                // Лут уже прийшов, а Майстерня (Days=4) ще будується — CraftUpgrade
                // мав впертись САМЕ в WorkshopClosed.
                if (item != null && !workshopBuilt)
                    Assert.AreEqual(Game.Core.Items.CraftResult.WorkshopClosed, s.CraftUpgrade(item.InstanceId));
            }

            Assert.IsNotNull(item, "силовий відряд на outskirts мав скинути хоч один предмет у сташ");
            Assert.IsFalse(item.Definition.IsNamed, "передумова детермінізму: DropTable outskirts не містить іменних предметів");
            Assert.Less((int)item.Rarity, (int)Game.Core.Items.Rarity.Epic,
                "передумова детермінізму: Good-полоса (margin=3) дає Rare, не Epic");
            Assert.IsTrue(workshopBuilt, "майстерня (Days=4) мала добудуватись за відведені доби циклу");

            var economy = s.GetEconomyView();
            var expected = economy.Gold >= 5 && economy.Materials >= 3
                ? Game.Core.Items.CraftResult.Success
                : Game.Core.Items.CraftResult.CannotAfford;
            Assert.AreEqual(expected, s.CraftUpgrade(item.InstanceId),
                "після побудови Майстерні CraftUpgrade мав дійти до CraftSystem і розв'язатись лише афордом");
        }

        /// <summary>
        /// Поправка №7.7 (рішення власника 24.09.2026): у тестовій збірці
        /// (SkipCreationOptions лишає TestBuildOneDayConstruction=true — це
        /// дефолт NewGameOptions, тим самим шляхом ідуть Unity нова гра,
        /// Alpha.Play і боти) Майстерня, заказана уранці доби 1, відкриває
        /// пост уже до ранку доби 2 — замість Days=4. Той самий детермінований
        /// розклад відрядження (margin 3, Good, безіменний Rare), що і в
        /// <see cref="CraftUpgrade_WorkshopClosed_ThenReachesCraftSystem_OnceWorkshopBuilt"/>,
        /// повертає предмет на добу 3 — і за 4 доби, задовго до форсованого
        /// фіналу доби 5 (§3.5), CraftUpgrade дістає до CraftSystem. Раніше
        /// (Days=4, а Майстерня остання в BuildPriority бота) цей ланцюг не
        /// встигав дійти до гравця в межах короткого тестового прогону —
        /// корінь скарги "майстерня добудовується надто пізно, щоб боти
        /// встигли щось зробити".
        ///
        /// Фікс-ревью (major): перевірка «Майстерня готова» тут навмисно
        /// відокремлена від однієї добою вперед — раніше вона стояла ПІСЛЯ
        /// чотирьох <c>PlayFullDayQuiet</c> і випадково збігалась із старим
        /// <c>BuildingDefinition.Days</c> Майстерні (4), тож тест лишався б
        /// зеленим, навіть якби протяжка прапорця
        /// <c>NewGameOptions.TestBuildOneDayConstruction</c> крізь
        /// <c>GameSession.NewGame</c>/<c>FirstHourWorld.Build</c> зламалась і
        /// стройка мовчки повернулась до проєктних строків. Тепер «готово за
        /// одну добу» перевіряється ОКРЕМО й одразу після першої доби — саме
        /// на цьому кроці тест провалиться, якщо протяжка регресує.
        /// </summary>
        [Test]
        public void CraftUpgrade_TestBuild_WorkshopOpensNextMorning_CraftReachableByDay5()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            var buildResult = s.OrderBuilding(Game.Core.Base.DefaultBuildings.Workshop);
            Assert.AreEqual(BuildOrderResult.Started, buildResult);

            var dispatch = s.DepartExpedition("outskirts", Game.Core.Expeditions.ExpeditionApproach.Forceful,
                new[] { "maksym" }, 2);
            Assert.AreEqual(Game.Core.Base.DispatchResult.Success, dispatch);

            var log = new List<GameEvent>();
            PlayFullDayQuiet(s, log); // рівно ОДНА доба — не «стільки ж, скільки був старий Days=4»

            bool workshopBuilt = false;
            foreach (var b in s.GetCityView().Built)
                if (b.Id == Game.Core.Base.DefaultBuildings.Workshop) workshopBuilt = true;
            Assert.IsTrue(workshopBuilt,
                "Поправка №7.7: Майстерня, заказана уранці доби 1, мала добудуватись за одну добу");

            for (int i = 0; i < 3; i++) PlayFullDayQuiet(s, log); // доби 2..4 — до фіналу доби 5 не дійшли

            var stash = s.GetStash();
            Assert.IsTrue(stash.Count > 0, "силовий відряд на outskirts мав повернутись із предметом до доби 4");
            var item = stash[0];

            var economy = s.GetEconomyView();
            var expected = economy.Gold >= 5 && economy.Materials >= 3
                ? Game.Core.Items.CraftResult.Success
                : Game.Core.Items.CraftResult.CannotAfford;
            Assert.AreEqual(expected, s.CraftUpgrade(item.InstanceId),
                "§6.1 №22 / Поправка №7.7: CraftUpgrade мав дійти до CraftSystem задовго до фіналу доби 5");
        }

        /// <summary>
        /// Фікс-ревью (major, verifier): попередні тести на Поправку №7.7
        /// або дзвонили просто в конструктор <c>CityWorks</c>
        /// (<c>CityWorksTests</c> — не проходять крізь
        /// <c>GameSession</c>/<c>FirstHourWorld</c> взагалі), або збігом
        /// днів маскували поламану протяжку (див. коментар вище). Цей тест
        /// — мінімальна пара «позитив/негатив» САМЕ на протяжку прапорця
        /// крізь реальний шлях виклику (<c>GameSession.NewGame</c> →
        /// <c>FirstHourWorld.Build</c> → конструктор <c>CityWorks</c>):
        /// дефолтні <c>NewGameOptions</c> (тестова збірка,
        /// <c>TestBuildOneDayConstruction=true</c>), замовлення Майстерні,
        /// РІВНО одна доба — і здание вже готове. Верифікатор довів, що це
        /// ловить регресію: відкат саме інтеграційної точки (видалений
        /// аргумент <c>testBuildOneDayConstruction</c> у виклику
        /// <c>FirstHourWorld.Build</c> з <c>GameSession.NewGame</c>) лишав
        /// увесь <c>tools/run-tests.sh</c> зеленим, бо жоден тест того часу
        /// не перевіряв «готово рівно за добу» ізольовано від часу
        /// повернення відрядження.
        /// </summary>
        [Test]
        public void NewGame_TestBuildDefault_WorkshopOrderedDay1_BuiltAfterExactlyOneDay()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions()); // TestBuildOneDayConstruction=true (дефолт NewGameOptions)
            FastForwardOpeningToMorning(s);

            Assert.AreEqual(BuildOrderResult.Started, s.OrderBuilding(Game.Core.Base.DefaultBuildings.Workshop));

            PlayFullDayQuiet(s); // рівно одна доба

            bool workshopBuilt = false;
            foreach (var b in s.GetCityView().Built)
                if (b.Id == Game.Core.Base.DefaultBuildings.Workshop) workshopBuilt = true;
            Assert.IsTrue(workshopBuilt,
                "Поправка №7.7: протяжка GameSession.NewGame→FirstHourWorld.Build→CityWorks мала дати " +
                "тестовій збірці одноденну стройку — Майстерня мала бути готова рівно за одну добу");
        }

        /// <summary>
        /// Пара-негатив до <see cref="NewGame_TestBuildDefault_WorkshopOrderedDay1_BuiltAfterExactlyOneDay"/>:
        /// та сама протяжка, але з <c>TestBuildOneDayConstruction=false</c>
        /// (кампанія) — за одну добу Майстерня (Days=4) свідомо ще НЕ готова.
        /// Без цієї пари позитивний тест міг би ловити не протяжку прапорця,
        /// а якусь іншу випадкову зміну строку стройки в даних.
        /// </summary>
        [Test]
        public void NewGame_CampaignMode_WorkshopOrderedDay1_NotBuiltAfterOneDay()
        {
            var s = new GameSession();
            s.NewGame(new NewGameOptions
                { SkipCreation = true, HitRule = HitRuleKind.Threshold, TestBuildOneDayConstruction = false });
            FastForwardOpeningToMorning(s);

            Assert.AreEqual(BuildOrderResult.Started, s.OrderBuilding(Game.Core.Base.DefaultBuildings.Workshop));

            PlayFullDayQuiet(s); // рівно одна доба

            bool workshopBuilt = false;
            foreach (var b in s.GetCityView().Built)
                if (b.Id == Game.Core.Base.DefaultBuildings.Workshop) workshopBuilt = true;
            Assert.IsFalse(workshopBuilt,
                "Кампанія (Поправка №6.1, Days=4): Майстерня не мала добудуватись за одну добу");
        }

        [Test]
        public void LoadState_UnoccupiedSlot_ReturnsFalse()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            bool ok = s.LoadState(1);
            Assert.IsFalse(ok, "слот 1 нічого не зберігав — LoadState має чесно повернути false, а не кинути виняток");
        }

        [Test]
        public void LoadState_OccupiedSlot_RestoresSameDay()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);
            PlayFullDayQuiet(s);
            int savedDay = s.CurrentView.Day;
            s.SaveState(1);

            PlayFullDayQuiet(s); // рухаємо стан далі, щоб LoadState справді щось відновлював

            bool ok = s.LoadState(1);
            Assert.IsTrue(ok);
            Assert.AreEqual(savedDay, s.CurrentView.Day);
            Assert.AreEqual(SessionState.Morning, s.State);
        }

        [Test]
        public void SetPatrol_InEvening_SetsIsPatrolling_VisibleOnSessionView()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            // Той самий шлях до Evening, що й перша половина PlayFullDayQuiet
            // (доба 1 завжди зупиняється на Decision вузла 1, §3.1) — лише БЕЗ
            // завершального ConfirmEvening, інакше нема на чому перевірити
            // SetPatrol (команда саме стану Evening, до AdvanceNight).
            s.ConfirmMorning();
            var report = s.AdvanceDay();
            while (report != null && report.AwaitsDecision)
                report = s.ResolveIncident(IncidentPath.Quiet);

            if (s.State == SessionState.Scene)
            {
                SceneStepView step;
                step = RunSceneToFinish(s);
            }
            Assert.AreEqual(SessionState.Evening, s.State);

            Assert.IsFalse(s.CurrentView.IsPatrolling, "передумова: патруль вимкнено, доки гравець не попросив");
            s.SetPatrol(true);
            Assert.IsTrue(s.CurrentView.IsPatrolling, "SetPatrol мав одразу відбитись у processor.IsPatrolling/SessionView");
        }

        [Test]
        public void AbandonDungeon_BeforeAnyRoomResolved_ReturnsToMorning_AndLogsDepart()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);

            var dispatch = s.DepartExpedition(DefaultDungeon.AbandonedCamp, Game.Core.Expeditions.ExpeditionApproach.Delve,
                new[] { "protagonist", "maksym", "myroslava" }, 2);
            Assert.AreEqual(Game.Core.Base.DispatchResult.Success, dispatch);
            Assert.AreEqual(SessionState.Dungeon, s.State);

            var view = s.AbandonDungeon();

            Assert.IsNull(view, "AbandonDungeon завершує підвішений Dungeon-стан, як і ResolveDungeonRoom/ExtractDungeon — DungeonView більше нема що показувати");
            Assert.AreEqual(SessionState.Morning, s.State);
            bool sawDepart = false;
            foreach (var e in s.DayLog) if (e.Key == "dungeon.depart") sawDepart = true;
            Assert.IsTrue(sawDepart, "AbandonDungeon мав залогувати dungeon.depart (§4.1)");
        }

        [Test]
        public void CombatUseAbility_UnknownAbilityId_ReturnsInvalidAction_WithoutThrowing()
        {
            var s = new GameSession();
            s.NewTrainingBattle(new TrainingBattleOptions { HitRule = HitRuleKind.Threshold });
            Assert.AreEqual(SessionState.Battle, s.State);

            var result = s.CombatUseAbility("no_such_ability_id");

            Assert.AreEqual(CombatActionResult.InvalidAction, result,
                "невідомий abilityId детерміновано не знаходиться в CombatUnit.FindAbility — обгортка має повернути InvalidAction, не кинути");
        }

        private static string Summarize(DayReportView v)
        {
            if (v == null) return "<null>";
            var sb = new StringBuilder();
            sb.Append("day=").Append(v.Day).Append(";phase=").Append(v.Phase);
            sb.Append(";awaits=").Append(v.AwaitsDecision);
            if (v.Pending != null)
            {
                sb.Append(";pending.kind=").Append(v.Pending.Kind).Append(";pending.topic=").Append(v.Pending.TopicId);
                sb.Append(";pending.options=").Append(v.Pending.Options.Count);
            }
            if (v.Incidents != null)
            {
                sb.Append(";incidents=").Append(v.Incidents.Count);
                foreach (var o in v.Incidents) sb.Append('|').Append(o.IncidentId).Append(':').Append(o.Band);
            }
            if (v.Signals != null && v.Signals.Requests != null)
            {
                sb.Append(";signals=").Append(v.Signals.Requests.Count);
                foreach (var r in v.Signals.Requests) sb.Append('|').Append(r.TopicId);
            }
            return sb.ToString();
        }
    }
}
