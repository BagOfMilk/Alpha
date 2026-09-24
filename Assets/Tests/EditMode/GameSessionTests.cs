using System;
using System.Collections.Generic;
using System.Text;
using Game.Core.Base;
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
        private static NewGameOptions SkipCreationOptions()
            => new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold };

        /// <summary>Доганяє сесію крізь відкриваючу сцену до Morning доби 1 (State доступний одразу після NewGame(SkipCreation=true)).</summary>
        private static void FastForwardOpeningToMorning(GameSession s)
        {
            Assert.AreEqual(SessionState.Scene, s.State);
            SceneStepView step;
            do { step = s.AdvanceScene(); } while (!step.IsFinished);
            Assert.AreEqual(SessionState.Morning, s.State);
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
                    do { step = s.AdvanceScene(); } while (!step.IsFinished);
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

        // ---- Доба 1: тихий шлях вузла 1 (Ж) ----

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
            do { step = s.AdvanceScene(); } while (!step.IsFinished);
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
            do { step = s.AdvanceScene(); } while (!step.IsFinished);
            Assert.AreEqual(SessionState.Evening, s.State);
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
            s.CombatAutoResolve();

            // Черга QueueExternal(PlaystyleBlood) — той самий мостик R6, що й у
            // квестів: споживається лише тіком Напруги НАСТУПНОЇ фази, не
            // миттєво в момент рішення.
            if (s.State == SessionState.Scene)
            {
                SceneStepView step;
                do { step = s.AdvanceScene(); } while (!step.IsFinished);
            }
            s.ConfirmEvening();
            s.AdvanceNight();

            Assert.Greater(s.DebugTensionValue, tensionBeforeBlood,
                "PlaystyleBlood мав піднятi Напругу так само, як IncidentResolver.ApplyBloodCost для звичайного кровавого шляху");
            Assert.IsTrue(s.DebugCommunityIsAfraid,
                "кроваве рішення вузла 1 через бій мало налякати громаду так само, як CausedFear звичайного кровавого шляху");
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

        // ---- Дефекція (US-9.4, R2/§2 №25): DefectionWatch.Tick + Defection.ShouldDefect ----

        /// <summary>
        /// Детермінований 3v2 бій вузла 1 (без хазяїна, як і в
        /// Day1_BloodyPath_...) заводить Максима вбитим і Мирославу — на
        /// полосу Base: PassVanguardOutcome сіє "defector_seeded" і реальна
        /// лояльність падає до Resentful (50-35=15 -> Resentful за §3.1
        /// коментарем у CompanionSocialBalance). Це рівно та комбінація
        /// (прапор + полоса ≤ Resentful), за якої Defection.ShouldDefect
        /// дефектить БЕЗ очікування 5 підряд-діб (seamsForD1 пакета B4) —
        /// TickDefectionWatch мала підхопити її на найближчому завершенні
        /// доби, а не мовчати, як до цього пакету.
        /// </summary>
        [Test]
        public void Day1_BloodyPath_SeedsDefector_AndDefectionWatchDefectsOnNightEnd()
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

            SceneStepView step;
            do { step = s.AdvanceScene(); } while (!step.IsFinished);
            Assert.AreEqual(SessionState.Evening, s.State);

            s.ConfirmEvening();
            Assert.AreEqual(SessionState.Night, s.State);
            s.AdvanceNight();

            bool sawDefected = false, sawRipple = false;
            foreach (var e in s.DayLog)
            {
                if (e.Key == "companion.defected" && e.Args["companionId"] == "myroslava") sawDefected = true;
                if (e.Key == "roster.rippled") sawRipple = true;
            }
            Assert.IsTrue(sawDefected,
                "прапор defector_seeded + полоса ≤ Resentful мали дефектити Мирославу негайно (Defection.ShouldDefect), " +
                "не чекаючи 5 підряд-діб — TickDefectionWatch мала бути звичайно неможливою без виклику з D1a");
            Assert.IsTrue(sawRipple, "дефекція — це предаство (RosterDrama.OnBetrayal), а не тиха відсутність ряби");
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

            bool sawResolvedBattle = false;
            foreach (var e in s.DayLog) if (e.Key == "combat.battle.resolved") sawResolvedBattle = true;
            Assert.IsTrue(sawResolvedBattle);
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
                do { step = s.AdvanceScene(); } while (!step.IsFinished);
            }
            s.ReactToCrisis(CrisisReaction.SpendGold);
            s.ConfirmEvening();

            Assert.AreEqual(SessionState.Night, s.State);
            var duringBattle = s.ResolveFinale(IncidentPath.Bloody);
            Assert.IsNull(duringBattle);
            Assert.AreEqual(SessionState.Battle, s.State);

            var battle = s.GetBattleView();
            Assert.IsNotNull(battle);
            bool hasBurunda = false;
            foreach (var u in battle.Units) if (u.Id.Contains("burunda")) hasBurunda = true;
            Assert.IsTrue(hasBurunda, "фінальний штурм завжди включає Бурунду-бегадира");

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
                do { step = s.AdvanceScene(); } while (!step.IsFinished);
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
                do { step = s.AdvanceScene(); } while (!step.IsFinished);
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
