using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core.Balance;
using Game.Core.Characters.Creation;
using Game.Core.Expeditions;
using Game.Core.Loop;
using Game.Core.Scenes;
using Game.Core.Session;
using Game.Core.Session.Bots;
using Game.Core.Session.Views;
using Game.Core.World;
using Game.Gameplay.Text;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Замір темпу Напруги в тестовій збірці (Поправка №7, рішення власника
    /// 24.09.2026: «За 5 днів пожежа, потім інші механіки. Так для тесту
    /// ввімкни що за 15 днів перша смуга за 20 друга і за 25 криза?»).
    ///
    /// Так само, як <see cref="CampaignPacingTests"/> — це тести ТЕМПА, не
    /// арифметики: замір "коли гравець уперше побачить зміну смуги/кризу", а
    /// не перевірка конкретної формули. Боти зі своїм "норовом":
    /// еталон — ситий домосід (<see cref="HomebodyPolicy"/>: мирно, без
    /// вилазок, без Облави/Храму/Укріплень); "дефузер" — той самий домосід із
    /// рутиною ради; голодний — <see cref="PacifistPolicy"/> з вилазками;
    /// "шкідник" (кроваво, порожні пости, без патруля, без Облави й Храму).
    /// Детермінізм (інваріант 1) означає, що те саме дерево рішень завжди
    /// дає той самий прогін.
    /// </summary>
    public class TestBuildTensionPaceTests
    {
        private const int Days = 30;

        private static (List<GameEvent> log, GameSession session) Run(IBotPolicy policy, bool suppressCouncilRoutine, int days = Days)
        {
            var options = new NewGameOptions();
            var session = new GameSession(options.Roller);
            session.NewGame(options);
            var log = new List<GameEvent>();
            BotRunner.Drive(session, policy, days, fullLog: log, suppressCouncilRoutine: suppressCouncilRoutine);
            return (log, session);
        }

        /// <summary>
        /// Поправка №12.10 (пул прибульців): "безрукий" еталон темпу мусить
        /// іти ДЕФОЛТНИМ шляхом створення — передісторія-дефолт
        /// (<c>GameSession._pendingBackgroundId</c> == "warrior", той самий,
        /// що й у BotRunner.Drive, який ніколи сам не кличе
        /// SetProtagonistBackground) і ПЕРШИЙ варіант відповіді Тугарові
        /// ("refuse", перший рядок сцени). HomebodyPolicy/PacifistPolicy сама
        /// відповідає Тугарові через <see cref="BotSupport.ChooseScenePersuasive"/>
        /// (шукає перевірку Переконання — "ask_myroslava"), що для ЦІЄЇ
        /// конкретної сцени НЕ є дефолтом гравця, який просто тисне «далі»/
        /// перший варіант. Ця обгортка лишає HomebodyPolicy незмінним для
        /// всіх інших користувачів (Defuser/Hunger/Neglect/SaveLoad тощо) і
        /// підмінює РІВНО один вибір — <see cref="OpeningScenes.TugarOfferChoiceId"/>.
        /// </summary>
        private sealed class DefaultArrivalHomebodyPolicy : IBotPolicy
        {
            private static readonly string[] TugarChoiceOrder = { "refuse", "bargain", "ask_myroslava" };

            private readonly HomebodyPolicy _inner = new HomebodyPolicy();
            private readonly string _tugarChoiceId;

            public DefaultArrivalHomebodyPolicy(string tugarChoiceId = "refuse")
            {
                _tugarChoiceId = tugarChoiceId;
            }

            public string Name => "DefaultArrivalHomebody";

            public IncidentPath ChooseIncidentPath(PendingOfferView offer) => _inner.ChooseIncidentPath(offer);

            public int ChooseQuestOption(QuestOfferView offer) => _inner.ChooseQuestOption(offer);

            public int ChooseSceneOption(SceneStepView step)
            {
                if (step != null && step.ChoiceId == OpeningScenes.TugarOfferChoiceId)
                {
                    int idx = Array.IndexOf(TugarChoiceOrder, _tugarChoiceId);
                    return idx >= 0 ? idx : 0;
                }
                return _inner.ChooseSceneOption(step);
            }

            public bool ChoosePatrol(SessionView view) => _inner.ChoosePatrol(view);

            public IReadOnlyDictionary<string, string> ChooseAssignments(RosterView roster, CityView city)
                => _inner.ChooseAssignments(roster, city);

            public ExpeditionChoice? ChooseExpedition(SessionView view) => _inner.ChooseExpedition(view);

            public CombatAction ChooseCombatAction(BattleView battle) => _inner.ChooseCombatAction(battle);

            public bool ChooseAutoResolve(BattleView battle) => _inner.ChooseAutoResolve(battle);

            public bool ChoosePushDeeper(DungeonView view) => _inner.ChoosePushDeeper(view);
        }

        /// <summary>
        /// Той самий "безрукий" еталон, але явно задає передісторію ПЕРЕД
        /// Creation (BotRunner.Drive сам ніколи не кличе
        /// SetProtagonistBackground, лишає дефолт "warrior") — потрібно для
        /// 9-комбінаційного охоронця <see cref="Pace_IsRobustToArrivalsPoolComposition"/>.
        /// </summary>
        private static (List<GameEvent> log, GameSession session) RunWithArrivals(
            string backgroundId, string tugarChoiceId, bool suppressCouncilRoutine = true, int days = Days)
        {
            var options = new NewGameOptions();
            var session = new GameSession(options.Roller);
            session.NewGame(options);
            session.SetProtagonistBackground(backgroundId);
            session.SetProtagonistName(BotRunner.DefaultProtagonistName);
            session.ConfirmCreation();

            var policy = new DefaultArrivalHomebodyPolicy(tugarChoiceId);
            var log = new List<GameEvent>();
            BotRunner.Drive(session, policy, days, fullLog: log, suppressCouncilRoutine: suppressCouncilRoutine);
            return (log, session);
        }

        private static int? FirstDay(List<GameEvent> log, string key)
        {
            foreach (var e in log)
                if (e.Key == key) return e.Day;
            return null;
        }

        private static int? FirstDayCrisisResolved(List<GameEvent> log)
        {
            foreach (var e in log)
                if (e.Key == "decision.resolved" && e.Args.TryGetValue("incidentId", out var id) && id == "crisis_riot")
                    return e.Day;
            return null;
        }

        private static int? FirstDayForewarn(List<GameEvent> log, int level, string subject)
        {
            foreach (var e in log)
                if (e.Key == "forewarn.level" + level && e.Args.TryGetValue("subject", out var s) && s == subject)
                    return e.Day;
            return null;
        }

        // ================= (a) "Безрукий еталон" =================

        [Test]
        public void Reference_HitsMurmurFermentHeat_InTargetWindows()
        {
            // Поправка №12.10 (пул прибульців, 29.09.2026): "безрукий" еталон —
            // ДЕФОЛТНИЙ шлях створення (передісторія "warrior" — Backgrounds.
            // All()[0], перший варіант відповіді Тугарові — "refuse"), а не
            // те, що вважає "мирним" HomebodyPolicy/PacifistPolicy сама (вона
            // відповідає Тугарові Persuade-варіантом "ask_myroslava" — не
            // дефолт гравця, який просто тисне перший рядок). Дефолтний шлях
            // приводить Гобана-Сайра (передісторія) і Діда Овсія (Тугар,
            // "refuse" → KeeperId) — той самий гурт, що й до пулу прибульців,
            // тож вікна темпу лишаються старими (Поправка №7.9).
            var (log, _) = RunWithArrivals("warrior", "refuse");

            int? murmur = FirstDay(log, "tension.band.Murmur");
            int? ferment = FirstDay(log, "tension.band.Ferment");
            int? heat = FirstDay(log, "tension.band.Heat");

            Assert.NotNull(murmur, "еталон повинен побачити Ропіт за 30 діб");
            Assert.That(murmur.Value, Is.InRange(14, 16), "Ропіт цілиться в добу 15 (±1, Поправка №7.9)");

            Assert.NotNull(ferment, "еталон повинен побачити Брожіння за 30 діб");
            Assert.That(ferment.Value, Is.InRange(19, 22), "Брожіння цілиться в добу 20 (±1-2, Поправка №7.9)");

            Assert.NotNull(heat, "еталон повинен побачити Накал за 30 діб");
            Assert.That(heat.Value, Is.InRange(21, 24), "Накал цілиться в добу 22-23 (Поправка №7.9)");
        }

        [Test]
        public void Reference_CrisisRiotOffered_AfterFullForewarnLadder_NearDay25()
        {
            // Той самий дефолтний шлях створення, що й вище.
            var (log, _) = RunWithArrivals("warrior", "refuse");

            int? l1 = FirstDayForewarn(log, 1, "crisis");
            int? l2 = FirstDayForewarn(log, 2, "crisis");
            int? l3 = FirstDayForewarn(log, 3, "crisis");
            int? riot = FirstDayCrisisResolved(log);

            Assert.NotNull(l1, "перший предвестник кризи має прозвучати");
            Assert.NotNull(l2, "другий предвестник кризи має прозвучати");
            Assert.NotNull(l3, "третій предвестник кризи має прозвучати");
            Assert.NotNull(riot, "великий кризис має вдарити за 30 діб для еталонного прогону");

            // Інваріант 4 / лестниця передвісників: жодна ступінь не пропущена,
            // і криза не має права вдарити, доки третя не прозвучала.
            Assert.LessOrEqual(l1.Value, l2.Value);
            Assert.LessOrEqual(l2.Value, l3.Value);
            Assert.LessOrEqual(l3.Value, riot.Value, "криза не може вдарити німо — третя ступінь має прозвучати ДО неї");

            Assert.That(riot.Value, Is.InRange(24, 26), "бунт цілиться в добу 25 (±1, Поправка №7.9)");
        }

        /// <summary>
        /// Поправка №12.10: темп стійкий до складу гурту прибульців — для
        /// всіх 9 комбінацій (передісторія × відповідь Тугарові) "безрукий"
        /// ситий еталон бачить Ропіт і бунт у ширшому вікні, ніж один
        /// дефолтний шлях (сильніші/слабші фахівці відсувають/наближають
        /// неспокій — задум, не баг). Числа виміряні прогоном — див. звіт
        /// сесії, що додала цей тест.
        /// </summary>
        [TestCase("warrior", "refuse")]
        [TestCase("warrior", "bargain")]
        [TestCase("warrior", "ask_myroslava")]
        [TestCase("trader", "refuse")]
        [TestCase("trader", "bargain")]
        [TestCase("trader", "ask_myroslava")]
        [TestCase("healer", "refuse")]
        [TestCase("healer", "bargain")]
        [TestCase("healer", "ask_myroslava")]
        public void Pace_IsRobustToArrivalsPoolComposition(string backgroundId, string tugarChoiceId)
        {
            var (log, _) = RunWithArrivals(backgroundId, tugarChoiceId);

            int? murmur = FirstDay(log, "tension.band.Murmur");
            int? riot = FirstDayCrisisResolved(log);

            Assert.NotNull(murmur, backgroundId + "/" + tugarChoiceId + ": еталон повинен побачити Ропіт за 30 діб");
            Assert.That(murmur.Value, Is.InRange(12, 18),
                backgroundId + "/" + tugarChoiceId + ": Ропіт вийшов за вікно [12,18] — добу " + murmur.Value);

            Assert.NotNull(riot, backgroundId + "/" + tugarChoiceId + ": еталон повинен дійти до бунту за 30 діб");
            Assert.That(riot.Value, Is.InRange(22, 28),
                backgroundId + "/" + tugarChoiceId + ": бунт вийшов за вікно [22,28] — добу " + riot.Value);
        }

        /// <summary>
        /// Integrate-фаза (24.09.2026): SETTLEMENT_LAYER §5.1 правило 4 —
        /// щабель 2 передвісника мусить назвати домен, щабель 3 — той самий
        /// домен (близькість). Знайдено разом зі стисненим темпом: раніше
        /// природна криза не доживала до власної драбини, і безликість
        /// "forewarn.level2/3" ("щось готують. скоро.") лишалась непоміченою
        /// для БУДЬ-ЯКОГО джерела. Ключ DayLog лишається спільним
        /// ("forewarn.levelN") — домен несе окремий аргумент "domain"
        /// (GameSession.TranslateReport/DomainTagFrom), який ScreenText.
        /// EventLine підставляє через {domain} (перевірено нижче).
        /// </summary>
        [Test]
        public void Reference_ForewarnLevels2And3_CarryCrisisDomainTag_AndTextNamesIt()
        {
            var (log, _) = Run(new HomebodyPolicy(), suppressCouncilRoutine: true);

            var l2 = log.FirstOrDefault(e => e.Key == "forewarn.level2" &&
                e.Args.TryGetValue("subject", out var s) && s == "crisis");
            var l3 = log.FirstOrDefault(e => e.Key == "forewarn.level3" &&
                e.Args.TryGetValue("subject", out var s2) && s2 == "crisis");

            Assert.NotNull(l2, "другий предвестник кризи має прозвучати за 30 діб еталонного прогону");
            Assert.NotNull(l3, "третій предвестник кризи має прозвучати за 30 діб еталонного прогону");

            Assert.IsTrue(l2.Args.TryGetValue("domain", out var d2) && d2 == "площадь",
                "щабель 2 мусить нести домен кризи (правило 4)");
            Assert.IsTrue(l3.Args.TryGetValue("domain", out var d3) && d3 == "площадь",
                "щабель 3 мусить нести той самий домен");

            // Домен насправді читається українською в готовому тексті, не
            // сирим тегом ("площадь" — внутрішній DomainTag, гравець його
            // ніколи не бачить напряму) — та сама підстановка "domain.<tag>",
            // що вже показує "domain.road"/"domain.craft".
            string translatedDomain = UkrainianText.Get("domain." + d2, Gender.Male);
            string renderedL2 = UkrainianText.Format("forewarn.level2", Gender.Male, "domain", translatedDomain);
            string renderedL3 = UkrainianText.Format("forewarn.level3", Gender.Male, "domain", translatedDomain);
            StringAssert.Contains("площ", renderedL2, "щабель 2 мусить назвати площу словами, не тегом");
            StringAssert.Contains("площ", renderedL3, "щабель 3 мусить назвати площу словами, не тегом");
            StringAssert.DoesNotContain("{domain}", renderedL2);
            StringAssert.DoesNotContain("{domain}", renderedL3);
            StringAssert.DoesNotContain("площадь", renderedL2, "гравцеві — лише українською, не сирим тегом");
        }

        /// <summary>
        /// Integrate-фаза: журнал механік бачить і зсув смуги, і природний
        /// бунт на площі окремими записами, які не плутаються зі скриптованою
        /// пожежею доби 5 (<c>MechanicJournalDef("crisis", ...)</c>).
        /// </summary>
        [Test]
        public void Reference_JournalSeesTensionBandChange_AndGreatCrisis_Separately()
        {
            var (_, session) = Run(new HomebodyPolicy(), suppressCouncilRoutine: true);
            var journal = session.GetMechanicsJournal();

            var bandEntry = journal.FirstOrDefault(e => e.Id == "tension_band_change");
            var crisisEntry = journal.FirstOrDefault(e => e.Id == "great_crisis");
            var scriptedFireEntry = journal.FirstOrDefault(e => e.Id == "crisis");

            Assert.NotNull(bandEntry, "запис tension_band_change має існувати в реєстрі журналу");
            Assert.NotNull(crisisEntry, "запис great_crisis має існувати в реєстрі журналу");
            Assert.NotNull(scriptedFireEntry, "старий запис про скриптовану пожежу доби 5 не мав зникнути");

            Assert.IsTrue(bandEntry.Seen, "еталонний прогін 30 діб мусить побачити зсув смуги Напруги");
            Assert.IsTrue(crisisEntry.Seen, "еталонний прогін 30 діб мусить побачити розв'язку великого бунту");

            // Два записи журналу — окремі тексти, і жоден не переплутаний.
            Assert.AreNotEqual(UkrainianText.Get(crisisEntry.TitleKey, Gender.Male),
                UkrainianText.Get(scriptedFireEntry.TitleKey, Gender.Male),
                "великий бунт і скриптована пожежа доби 5 не мають ділити один заголовок");
        }

        /// <summary>
        /// Пункт журналу «Ніч: патруль чи сон» чекав події "night.forewarn",
        /// якої ядро не пише, і не позначався ніколи. Тепер — з першої ночі на
        /// варті; той, хто завжди спить, його не бачить.
        /// </summary>
        [Test]
        public void Journal_NightPatrol_SeenOnlyAfterAPatrolledNight()
        {
            var (_, patrol) = Run(new PatrolAlwaysPolicy(), suppressCouncilRoutine: true, days: 3);
            Assert.IsTrue(patrol.GetMechanicsJournal().First(e => e.Id == "night_patrol").Seen,
                "хто вартував уночі — побачив механіку патруля");

            var (_, sleeper) = Run(new NeverPatrol(), suppressCouncilRoutine: true, days: 3);
            Assert.IsFalse(sleeper.GetMechanicsJournal().First(e => e.Id == "night_patrol").Seen,
                "хто щоночі спав — патруля ще не пробував");
        }

        private sealed class NeverPatrol : IBotPolicy
        {
            private readonly PacifistPolicy _p = new PacifistPolicy();
            public string Name => "NeverPatrol";
            public Game.Core.Loop.IncidentPath ChooseIncidentPath(Game.Core.Session.Views.PendingOfferView o) => _p.ChooseIncidentPath(o);
            public int ChooseQuestOption(Game.Core.Session.Views.QuestOfferView o) => _p.ChooseQuestOption(o);
            public int ChooseSceneOption(Game.Core.Session.Views.SceneStepView s) => _p.ChooseSceneOption(s);
            public bool ChoosePatrol(Game.Core.Session.Views.SessionView v) => false;
            public IReadOnlyDictionary<string, string> ChooseAssignments(Game.Core.Session.Views.RosterView r, Game.Core.Session.Views.CityView c) => _p.ChooseAssignments(r, c);
            public ExpeditionChoice? ChooseExpedition(Game.Core.Session.Views.SessionView v) => null;
            public CombatAction ChooseCombatAction(Game.Core.Session.Views.BattleView b) => _p.ChooseCombatAction(b);
            public bool ChooseAutoResolve(Game.Core.Session.Views.BattleView b) => _p.ChooseAutoResolve(b);
            public bool ChoosePushDeeper(Game.Core.Session.Views.DungeonView d) => _p.ChoosePushDeeper(d);
        }

        /// <summary>
        /// Дві лінії квестів (Гафія і Максим) пропонуються щовечора; раніше
        /// одна пам'ять «останнього записаного» ключа давала «Нова пропозиція:
        /// Максим / Гафія» у стрічці щодоби (довгий автопрогін 25.09.2026).
        /// Той самий етап того самого квесту — рівно один рядок за партію.
        /// </summary>
        [Test]
        public void QuestOffered_LoggedOncePerQuestStage_EvenWithTwoParallelLines()
        {
            var (log, _) = Run(new HomebodyPolicy(), suppressCouncilRoutine: true, days: 20);

            var offered = log.Where(e => e.Key == "quest.offered")
                .Select(e => e.Args["questId"] + "#" + e.Args["stage"]).ToList();
            Assert.GreaterOrEqual(offered.Distinct().Count(), 2, "у прогоні мали бути пропозиції обох ліній");
            var repeated = offered.GroupBy(k => k).Where(gr => gr.Count() > 1).Select(gr => gr.Key + "×" + gr.Count()).ToList();
            CollectionAssert.IsEmpty(repeated, "той самий етап квесту не мусить знову з'являтися як «Нова пропозиція»");
        }

        // ================= (b) "Дефузер" =================

        [Test]
        public void Defuser_RiotIsLaterThanReference_OrNeverWithin30Days()
        {
            var (referenceLog, _) = Run(new HomebodyPolicy(), suppressCouncilRoutine: true);
            // Той самий ситий домосід, але з рутиною ради (Облава на кожній
            // готовності, Храм/Укріплення в черзі стройки) — різниця лише в
            // тому, чи гравець гасить Напругу.
            var (defuserLog, _) = Run(new HomebodyPolicy(), suppressCouncilRoutine: false);

            int? referenceRiot = FirstDayCrisisResolved(referenceLog);
            int? defuserRiot = FirstDayCrisisResolved(defuserLog);

            Assert.NotNull(referenceRiot, "еталон — контрольна точка порівняння, має вдарити");

            // Облава (-80 Напруги) і згодом Храм/Укріплення (дренаж) — той самий
            // рутинний бот, що й AllMechanicsCoverageTests/tools/Alpha.Sim.
            // Або кризи взагалі немає за 30 діб, або вона прийшла ПІЗНІШЕ еталону.
            if (defuserRiot.HasValue)
                Assert.Greater(defuserRiot.Value, referenceRiot.Value,
                    "дефузер (Облава + Храм/Укріплення) має відкласти бунт пізніше еталону");
        }

        /// <summary>
        /// Голод прискорює, а не задає темп: громада з порожніми фермами
        /// доходить до бунту РАНІШЕ ситого еталона. До 25.09.2026 темп тримався
        /// саме на голоді, і ситий тестер кризи не бачив зовсім.
        ///
        /// Поправка №12.7: раніше «голодним» був Pacifist — бот ставив
        /// Максима на ферми (склад/лазарет/рада вже були зайняті), а вилазки
        /// забирали його з поста. Зі стартом без будівель вільний Дід Овсій
        /// стає на ферми і нікуди не ходить — Pacifist ситий. Голод тепер
        /// ізольовано прямо: той самий домосід, тільки ферми порожні
        /// (<see cref="EmptyFarmsHomebody"/>) — різниця з еталоном рівно одна.
        /// </summary>
        [Test]
        public void Hunger_BringsRiotSooner_ThanFedReference()
        {
            var (referenceLog, _) = Run(new HomebodyPolicy(), suppressCouncilRoutine: true);
            var (hungryLog, _) = Run(new EmptyFarmsHomebody(), suppressCouncilRoutine: true);

            Assert.IsNull(FirstDay(referenceLog, "production.food_shortage"), "еталон має бути ситим — інакше він міряє голод, а не темп");
            Assert.NotNull(FirstDay(hungryLog, "production.food_shortage"), "громада з порожніми фермами мала голодувати");

            int? referenceRiot = FirstDayCrisisResolved(referenceLog);
            int? hungryRiot = FirstDayCrisisResolved(hungryLog);
            Assert.NotNull(referenceRiot);
            Assert.NotNull(hungryRiot, "голодна громада мусить дійти до бунту за 30 діб");
            Assert.Less(hungryRiot.Value, referenceRiot.Value, "голод мусить наближати бунт");
        }

        // ================= (c) "Шкідник" =================

        [Test]
        public void Neglect_RiotIsClearlyEarlierThanReference()
        {
            var (referenceLog, _) = Run(new HomebodyPolicy(), suppressCouncilRoutine: true);
            var (neglectLog, _) = Run(new NeglectPolicy(), suppressCouncilRoutine: true);

            int? referenceRiot = FirstDayCrisisResolved(referenceLog);
            int? neglectRiot = FirstDayCrisisResolved(neglectLog);

            Assert.NotNull(referenceRiot);
            Assert.NotNull(neglectRiot, "шкідник (кроваво, порожні пости, без патруля) має добігти до бунту за 30 діб");
            Assert.Less(neglectRiot.Value, referenceRiot.Value,
                "шкідник (кроваво, порожні пости, без патруля, без Облави/Храму) мусить дійти до бунту раніше еталону");
        }

        // ================= передвісник не бреше =================

        /// <summary>
        /// SETTLEMENT_LAYER §5.1, правило 4: третя ступінь «площі» обіцяє бунт —
        /// і він мусить прийти. Знайдено 25.09.2026: із кампанійним відкатом
        /// кризи (30 діб) заряд порогу 9 відновлювався за дві фази, драбина
        /// звучала знову наутро після бунту, а бунту за нею не було — у
        /// шкідника друга драбина (доби 24-26) висіла в повітрі. Кожна почута
        /// третя ступінь, після якої прогін триває ще щонайменше
        /// <see cref="PromiseWindowDays"/> діб, мусить закінчитись бунтом у
        /// цьому вікні.
        /// </summary>
        private const int PromiseWindowDays = 4;

        [TestCase("reference")]
        [TestCase("neglect")]
        public void CrisisLadder_Level3_IsAlwaysFollowedByARiot(string who)
        {
            var (log, _) = who == "neglect"
                ? Run(new NeglectPolicy(), suppressCouncilRoutine: true)
                : Run(new HomebodyPolicy(), suppressCouncilRoutine: true);

            var riots = new List<int>();
            foreach (var e in log)
                if (e.Key == "decision.resolved" && e.Args.TryGetValue("incidentId", out var id) && id == "crisis_riot")
                    riots.Add(e.Day);

            int checkedLadders = 0;
            foreach (var e in log)
            {
                if (e.Key != "forewarn.level3" || !e.Args.TryGetValue("subject", out var s) || s != "crisis") continue;
                if (e.Day + PromiseWindowDays > Days) continue;
                checkedLadders++;
                int day = e.Day;
                Assert.IsTrue(riots.Exists(r => r > day && r <= day + PromiseWindowDays),
                    who + ": третя ступінь «площі» на добу " + day + " не завершилась бунтом до доби " +
                    (day + PromiseWindowDays) + " — передвісник збрехав (бунти: " + string.Join(",", riots) + ")");
            }
            Assert.Greater(checkedLadders, 0, who + ": за 30 діб мала прозвучати хоч одна повна драбина «площі»");
        }

        // ================= бите/лог кризи =================

        [Test]
        public void Riot_AppliesBiteAndLogsCompanionDeath_DuringFreePlay()
        {
            // Регресія, знайдена саме цим заміром (24.09.2026): природна криза
            // раніше НІКОЛИ не спрацьовувала за жодного прогону (Напруга не
            // доходила до Накалу за тестований діапазон днів), тож побічний
            // ефект "GameSession.ResolveIncident ховає щойно розв'язаний
            // інцидент від TranslateReport (MarkIncidentsAlreadyTranslated), а
            // разом з ним — виклик HandleCompanionDeath для
            // CrisisBite.KillCompanion" лишався непоміченим.
            var (log, session) = Run(new NeglectPolicy(), suppressCouncilRoutine: true);

            int? riotDay = FirstDayCrisisResolved(log);
            Assert.NotNull(riotDay, "шкідник має дійти до бунту за 30 діб");
            Assert.Greater(riotDay.Value, 5, "бунт має статись у вільній грі — ПІСЛЯ форсованого фіналу доби 5");

            bool diedLogged = false;
            foreach (var e in log)
                if (e.Key == "companion.died" && e.Day == riotDay.Value) diedLogged = true;

            Assert.IsTrue(diedLogged,
                "CrisisBite.KillCompanion зобов'язаний лишити \"companion.died\" у DayLog того самого дня, що й бунт");
            Assert.AreEqual(SessionState.FreePlay, session.State, "прогін на 30 діб має завершитись у вільній грі");
        }

        // ================= флаг вимкнено: кампанія не бачить різниці =================

        [Test]
        public void Off_CampaignProfileUnchanged()
        {
            var campaignDefaultTension = new TensionBalance();
            var campaignDefaultPulse = new PulseBalance();

            var cfg = new BalanceConfig();
            var world = FirstHourWorld.Build(1, requirePlayerDecision: false, balance: cfg, testBuildTensionPace: false);

            Assert.AreEqual(campaignDefaultTension.BandThresholds, cfg.Tension.BandThresholds);
            Assert.AreEqual(campaignDefaultTension.TierTickPerDay, cfg.Tension.TierTickPerDay);
            Assert.AreEqual(campaignDefaultTension.TempleDrainPerDay, cfg.Tension.TempleDrainPerDay);
            Assert.AreEqual(campaignDefaultTension.FortificationDrainPerDay, cfg.Tension.FortificationDrainPerDay);
            Assert.AreEqual(campaignDefaultTension.HungerDeltaPerDay, cfg.Tension.HungerDeltaPerDay);
            Assert.AreEqual(campaignDefaultTension.RaidDelta, cfg.Tension.RaidDelta);

            Assert.AreEqual(campaignDefaultPulse.CrisisGraceDays, cfg.Pulse.CrisisGraceDays);
            Assert.AreEqual(campaignDefaultPulse.Forewarn1At, cfg.Pulse.Forewarn1At);

            // Джерело кризи лишається кампанійним (Threshold=120) — не
            // стисненим тестовим (internal PressureTrack, IVT).
            Assert.AreEqual(120, world.Processor.Pulse.Tracks["crisis"].Threshold);
        }

        [Test]
        public void On_IsTheDefaultForNewGameOptions()
        {
            // Усі три шляхи в тестову збірку (Unity/Alpha.Play/боти) йдуть крізь
            // GameSession.NewGame без явного значення цього поля — дефолт
            // зобов'язаний лишатись true, інакше тестова збірка мовчки втратить
            // стиснутий темп (той самий прийом, що й TestBuildOneDayConstruction).
            Assert.IsTrue(new NewGameOptions().TestBuildTensionPace);
        }

        // ================= збереження/завантаження =================

        [Test]
        public void SaveLoad_MidFreePlay_PreservesTrajectory()
        {
            // Порівнюємо з ТИМ САМИМ розбиттям прогону без збереження: другий
            // Drive заново збирає ще не очищений DayLog межової доби, тож
            // розбитий прогін відрізняється від суцільного на ці рядки сам по
            // собі (перевірено 25.09.2026) — різниця має бути лише від
            // SaveState/LoadState, а не від розбиття.
            var baseline = TrajectorySnapshot(new StewardPolicy(), splitAtDay: 18, saveLoad: false);
            var withSaveLoad = TrajectorySnapshot(new StewardPolicy(), splitAtDay: 18, saveLoad: true);

            Assert.IsTrue(baseline.Exists(s => s.StartsWith("2") && s.Contains("tension.band.")),
                "після точки збереження (доба 18) прогін мусить мати зсув смуги — інакше тест нічого не доводить");
            Assert.AreEqual(baseline, withSaveLoad,
                "той самий детермінований прогін до і після SaveState/LoadState посеред вільної гри мусить дати ту саму траєкторію смуг");
        }

        /// <summary>
        /// Знайдено 25.09.2026: прапорець темпу не жив у сейві — «Продовжити»
        /// будувало світ із типовими опціями, і партія з кампанійним темпом
        /// (перемикач на титулі) мовчки продовжувалась у тестовому.
        /// </summary>
        [Test]
        public void ContinueGame_KeepsCampaignPace_ChosenOnTitle()
        {
            var options = new NewGameOptions { TestBuildTensionPace = false };
            var s = new GameSession(options.Roller);
            s.NewGame(options);
            BotRunner.Drive(s, new HomebodyPolicy(), 7, suppressCouncilRoutine: true);
            string blob = s.SaveState(0);

            var resumed = new GameSession();
            resumed.PreloadSlot(0, blob);
            Assert.IsTrue(resumed.ContinueGame(0));

            Assert.IsFalse(resumed.DebugTensionPace, "продовжена партія мусить лишитись у кампанійному темпі");
            Assert.AreEqual(120, resumed.DebugCrisisThreshold, "накопичувач кризи — кампанійний, не тестовий");
            Assert.AreEqual(s.DebugTensionValue, resumed.DebugTensionValue);
        }

        /// <summary>
        /// Слот з іншим темпом, завантажений у вже запущену партію: світ
        /// перебудовується під темп слота, і далі партія йде так само, як
        /// та, з якої слот знято.
        /// </summary>
        [Test]
        public void LoadingSlotWithOtherPace_RebuildsWorld_AndContinuesIdentically()
        {
            var testOptions = new NewGameOptions();
            var source = new GameSession(testOptions.Roller);
            source.NewGame(testOptions);
            BotRunner.Drive(source, new HomebodyPolicy(), 12, suppressCouncilRoutine: true);
            string blob = source.SaveState(0);

            var campaignOptions = new NewGameOptions { TestBuildTensionPace = false };
            var target = new GameSession(campaignOptions.Roller);
            target.NewGame(campaignOptions);
            BotRunner.Drive(target, new HomebodyPolicy(), 7, suppressCouncilRoutine: true);
            target.SaveState(2);
            target.RestoreFromBlob(blob);

            Assert.IsTrue(target.DebugTensionPace);
            Assert.AreEqual(TestBuildTensionPace.CrisisThreshold, target.DebugCrisisThreshold);
            Assert.AreEqual(source.DebugTensionValue, target.DebugTensionValue);

            // Після правки відновлення у свіжу сесію (зайнятість постів, голод,
            // завершені глави арок — FreshSessionRestoreTests) перебудований
            // світ мусить іти рівно тією траєкторією, що й джерело слота.
            BotRunner.Drive(source, new HomebodyPolicy(), 14, suppressCouncilRoutine: true);
            BotRunner.Drive(target, new HomebodyPolicy(), 14, suppressCouncilRoutine: true);
            Assert.AreEqual(source.DebugTensionValue, target.DebugTensionValue,
                "після перебудови світ мусить іти тією самою траєкторією, що й джерело слота");
            Assert.IsTrue(target.LoadState(2), "слоти партії переживають перебудову світу");
        }

        private static List<string> TrajectorySnapshot(IBotPolicy policy, int splitAtDay, bool saveLoad)
        {
            var options = new NewGameOptions();
            var session = new GameSession(options.Roller);
            session.NewGame(options);

            var trajectory = new List<string>();

            {
                var log1 = new List<GameEvent>();
                BotRunner.Drive(session, policy, splitAtDay, fullLog: log1, suppressCouncilRoutine: false);
                CollectBandAndForewarn(log1, trajectory);

                if (saveLoad)
                {
                    session.SaveState(0);
                    session.LoadState(0);
                }

                var log2 = new List<GameEvent>();
                BotRunner.Drive(session, policy, Days - splitAtDay, fullLog: log2, suppressCouncilRoutine: false);
                CollectBandAndForewarn(log2, trajectory);
            }

            return trajectory;
        }

        private static void CollectBandAndForewarn(List<GameEvent> log, List<string> outTrajectory)
        {
            foreach (var e in log)
                if (e.Key.StartsWith("tension.band.") || e.Key.StartsWith("forewarn.level"))
                    outTrajectory.Add(e.Day + ":" + e.Key);
        }
            /// <summary>Домосід, який не ставить нікого на ферми, — щоб голод був ЄДИНОЮ відмінністю від еталона.</summary>
        private sealed class EmptyFarmsHomebody : IBotPolicy
        {
            private readonly HomebodyPolicy _inner = new HomebodyPolicy();
            public string Name => "EmptyFarmsHomebody";
            public Game.Core.Loop.IncidentPath ChooseIncidentPath(Game.Core.Session.Views.PendingOfferView offer) => _inner.ChooseIncidentPath(offer);
            public int ChooseQuestOption(Game.Core.Session.Views.QuestOfferView offer) => _inner.ChooseQuestOption(offer);
            public int ChooseSceneOption(Game.Core.Session.Views.SceneStepView step) => _inner.ChooseSceneOption(step);
            public bool ChoosePatrol(Game.Core.Session.Views.SessionView view) => _inner.ChoosePatrol(view);
            public IReadOnlyDictionary<string, string> ChooseAssignments(Game.Core.Session.Views.RosterView roster, Game.Core.Session.Views.CityView city)
            {
                var plan = _inner.ChooseAssignments(roster, city);
                var result = new Dictionary<string, string>();
                if (plan != null)
                    foreach (var kv in plan)
                        if (kv.Value != "settlement_farms") result[kv.Key] = kv.Value;
                return result;
            }
            public ExpeditionChoice? ChooseExpedition(Game.Core.Session.Views.SessionView view) => _inner.ChooseExpedition(view);
            public CombatAction ChooseCombatAction(Game.Core.Session.Views.BattleView battle) => _inner.ChooseCombatAction(battle);
            public bool ChooseAutoResolve(Game.Core.Session.Views.BattleView battle) => _inner.ChooseAutoResolve(battle);
            public bool ChoosePushDeeper(Game.Core.Session.Views.DungeonView view) => _inner.ChoosePushDeeper(view);
        }
    }
}
