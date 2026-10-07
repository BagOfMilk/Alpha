using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Characters.Build;
using Game.Core.Characters.Creation;
using Game.Core.Characters.Perks;
using Game.Core.Characters.Progression;
using Game.Core.Checks;
using Game.Core.Combat;
using Game.Core.Companions;
using Game.Core.Dungeons;
using Game.Core.Economy;
using Game.Core.Expeditions;
using Game.Core.Factions;
using Game.Core.Items;
using Game.Core.Loop;
using Game.Core.Pressure;
using Game.Core.Quests;
using Game.Core.Randomness;
using Game.Core.Scenes;
using Game.Core.Session.Views;
using Game.Core.Prisoners;
using Game.Core.Stats;
using Game.Core.Story;
using Game.Core.World;
using BaseState = Game.Core.Base.BaseState;
using CityWorksType = Game.Core.Base.CityWorks;
using RosterAdapter = Game.Core.Base.RosterAdapter;
using DispatchResult = Game.Core.Base.DispatchResult;
using ExpeditionRunner = Game.Core.Base.ExpeditionRunner;
using AssignmentResult = Game.Core.Base.AssignmentResult;
using BuildOrderResult = Game.Core.Base.BuildOrderResult;
using CouncilOrderResult = Game.Core.Base.CouncilOrderResult;
using DefaultBuildingsType = Game.Core.Base.DefaultBuildings;
using SettlementCycleType = Game.Core.Base.SettlementCycle;
using CycleReport = Game.Core.Base.CycleReport;

namespace Game.Core.Session
{
    /// <summary>
    /// Фасад над злитим трунком (§4.1–4.9, §4.14–4.15 TEST_BUILD.md, пакет D1).
    ///
    /// Єдина точка входу для Unity/консолі/тестів: усередині тримає світ
    /// (<see cref="FirstHourWorld"/>) і веде його крізь стани (§4.1). Жодна
    /// зовнішня збірка не бачить прихованих чисел — лише View-DTO
    /// (Core/Session/Views/*) і <see cref="GameEvent"/> (§4.3).
    ///
    /// Кубик (R1): GameSession НІКОЛИ не створює <see cref="IDiceRoller"/> сам —
    /// єдина сидована реалізація (<c>Game.Gameplay.Combat.SeededDiceRoller</c>)
    /// живе поза Game.Core (Core не має на неї посилання взагалі, інакше
    /// напрямок залежності Gameplay→Core розвернувся б). Той, хто збирає гру,
    /// будує кубик один раз і передає його сюди через
    /// <see cref="NewGameOptions.Roller"/>; сідування на конкретний
    /// <see cref="NewGameOptions.Seed"/> йде через контракт самого інтерфейсу
    /// (<c>RestoreState</c>), без жодного знання про конкретний тип —
    /// <c>ArchitectureGuardTests.Core_NoTypeImplementsIDiceRoller</c> лишається
    /// зеленим.
    /// </summary>
    public sealed class GameSession
    {
        public const string ProtagonistId = FirstHourWorld.ProtagonistId;

        /// <summary>
        /// Полірування (ціль 6 «Рішення»): ворог вузла 1 (криваво) — одна
        /// назва в ОБОХ місцях, що його читають (BuildBattleSetup виклику
        /// нижче й DecisionOptionView.TacticalBattleEnemyCount у
        /// BuildPendingOfferView), замість двох незалежних літералів "2",
        /// які могли б розійтись при правці контенту.
        /// </summary>
        private static readonly string[] Node1BloodyEnemyIds = { "horde_scout", "horde_scout" };

        public SessionState State { get; private set; } = SessionState.Title;

        // ---- світ (заповнюється NewGame/ContinueGame) ----
        private FirstHourWorld _world;
        private BalanceConfig _cfg = new BalanceConfig();
        private BaseState _state;
        private CityWorksType _works;
        private DayProcessor _processor;
        private SettlementCycleType _cycle;
        private Roster _worldRoster;
        private ExpeditionParty _party;
        private SiteLedger _sites;
        private StoryFlags _flags;
        private QuestLog _quests;
        private FactionRegistry _factions;
        private SpendablePoints _points;
        private ReadinessTrack _readiness;
        private Inventory _inventory;
        private RosterAdapter _rosterAdapter;
        private IRosterView _rosterView;
        private IRepeatTracker _repeats;
        private ForcedCrisisSource _crisis;

        /// <summary>
        /// Годинник дефекції (B4, шов seamsForD1): рахує підряд-дні на дні
        /// лояльності на кожного напарника. Tick() і перевірка ShouldDefect
        /// звуться рівно раз на календарну добу — <see cref="TickDefectionWatch"/>.
        /// </summary>
        private DefectionWatch _defectionWatch;

        // ---- Здача і полон (Поправка №14.2) ----
        private PrisonerLedger _prisoners = new PrisonerLedger();
        // ---- Поразка → полон (Поправка №14.7): наші люди в чужих руках ----
        private CaptivityLedger _captives = new CaptivityLedger();
        /// <summary>Загін тримача, на який зараз іде рейд (null — рейду немає).</summary>
        private string _raidGroupId;
        // ---- Досьє ворога (Поправка №14.6): що громада знає про кожен тип ворога ----
        private EnemyDossierBook _dossier = new EnemyDossierBook();
        /// <summary>«Відкуп»: скільки разів ватага цього типу вже відмовилась — ціна повтору зростає.</summary>
        private readonly Dictionary<string, int> _bribeRefusals = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<SurrenderedEnemy> _pendingSurrenders = new List<SurrenderedEnemy>();
        /// <summary>Переманені полонені: щоб відтворити їх у ростері при завантаженні (ростер відновлює лише наявних).</summary>
        private readonly List<string> _recruits = new List<string>();

        /// <summary>
        /// Major-фікс ревью (§2 №27, seamsForD1 пакета B4): особисті арки
        /// напарників (<see cref="CompanionArc"/>/<see cref="CompanionArcRun"/>)
        /// існували з пакета B4, але жодного разу не інстанціювались і не
        /// тікались з GameSession — Refresh() не звав ніхто, подія
        /// "arc.chapter_opened" не могла піти в DayLog. Тут лише гейтинг
        /// (лояльність/прапор) тікається щоденно (<see cref="TickCompanionArcs"/>) —
        /// реальний ЗМІСТ глави (Begin/CompleteChapter через квест з
        /// ArcChapter.QuestId) лишається відкритим гачком для пакета змісту
        /// (та сама межа декаплінгу, яку документує сам CompanionArc: "зміст
        /// глави грає викликач, через майбутній QuestRun, B6/D1").
        /// </summary>
        private List<CompanionArcRun> _arcRuns;

        /// <summary>Прапори гейтингу арок (ArcChapter.RequiresFlag/SetsFlag) — окремі від StoryFlags: без Begin/CompleteChapter (гачок вище) їх ще нікому виставляти.</summary>
        private readonly HashSet<string> _arcFlags = new HashSet<string>();

        // ---- Поправка №7.8: глави арок ПРОГРАЮТЬСЯ (Begin → сцена/квест → CompleteChapter) ----

        /// <summary>
        /// Companion.Id, чия сцена зараз триває, коли ця сцена — зміст глави
        /// арки (BeginArcChapterScene). На фініші сцени (BuildSceneStepView)
        /// глава завершується (CompleteArcChapterFor) — так само, як
        /// "to.node1.pass" ставить прапор на фініші, лише для арки.
        /// </summary>
        private string _activeArcCompanionId;

        /// <summary>QuestId → companionId для квестових глав арки (BeginArcChapterQuest): термінал цього квесту в ResolveQuestChoice завершує главу.</summary>
        private readonly Dictionary<string, string> _activeArcChapterQuestCompanion = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Поточний id сцени, що триває (для теми повторів вибору й події scene.choice.made) — null, якщо сцена не активна.</summary>
        private string _currentSceneId;

        // ---- кубик/сід/бій ----
        // НЕ readonly (фікс-ревью): NewGameOptions.Roller — задокументований
        // класовим коментарем і самим полем NewGameOptions як рівноцінний
        // конструктору канал інжекції кубика (той, хто збирає гру, може
        // лишити конструктор GameSession(roller: null) і передати кубик через
        // NewGame(o)/ContinueGame(slot, roller) натомість) — NewGame()
        // промотує o.Roller сюди рівно раз за прогін, якщо конструкторський
        // канал порожній; без цього поле NewGameOptions.Roller було мертвим
        // (RequestBattle/ComposeSave/ApplySave/NewTrainingBattle читали лише
        // конструкторське значення).
        private IDiceRoller _roller;
        private ulong _seed = 1;
        private HitRuleKind _hitRule = HitRuleKind.Threshold;

        /// <summary>
        /// Правило попадання поточної партії (§7.6, ще ВІДКРИТО власником) —
        /// публічний гачок для UI-точок входу, які мають повторити те саме
        /// правило, з яким іде кампанія (напр. тренувальний бій усередині
        /// гри, HubScreen.DrawReadiness): titульний екран задає його один раз
        /// у NewGame, і жодна команда всередині партії його не міняє.
        /// </summary>
        public HitRuleKind HitRule => _hitRule;

        /// <summary>
        /// Змінити правило влучання посеред партії (меню паузи; власник, 29.09.2026: «в
        /// настройках його можна змінить»). Діє з наступного бою — бій, що йде, лишається
        /// на своєму правилі. Правило з кубиком потребує впровадженого IDiceRoller (R1).
        /// </summary>
        public void SetHitRule(HitRuleKind rule)
        {
            if (rule == _hitRule) return;
            if (rule == HitRuleKind.Percent)
            {
                if (_roller == null)
                    throw new InvalidOperationException("Правило з кубиком потребує IDiceRoller, впровадженого в GameSession (R1).");
                _roller.RestoreState(_seed.ToString(CultureInfo.InvariantCulture));
            }
            _hitRule = rule;
        }

        /// <summary>
        /// Темп Напруги, з яким побудовано світ цієї партії
        /// (<see cref="NewGameOptions.TestBuildTensionPace"/>). Живе в сейві:
        /// інакше «Продовжити» будувало світ із типовими опціями, і партія,
        /// почата з кампанійним темпом (перемикач на титулі), мовчки
        /// продовжувалась у тестовому. Знайдено 25.09.2026.
        /// </summary>
        private bool _tensionPace = true;
        private bool _ironman;

        private CombatState _battle;
        private SuspendToken _resume;

        /// <summary>
        /// Фікс-ревью D1b (мажор): чи саме ЦЕЙ бій довела до кінця команда
        /// <see cref="CombatAutoResolve"/> (а не покрокові команди гравця). Раніше
        /// подія "combat.autoresolved" вибиралась за SuspendReason.TrainingSkirmish
        /// — тобто за тим, ЩО за бій (тренувальний), а не ЯК саме його завершили,
        /// тож для будь-якого справжнього кампанійного бою (кривавий вузол 1,
        /// бойові кімнати данжу, фінальний штурм), розв'язаного автобоєм — а це
        /// панівний шлях "one-game" проходження — ключ "combat.autoresolved" був
        /// НЕДОСЯЖНИЙ, а покроково дограний тренувальний бій хибно ніс саме цей
        /// ключ. Прапор виставляється в CombatAutoResolve БЕЗПОСЕРЕДНЬО перед
        /// AfterCombatAction() і читається (та скидається) один раз в
        /// OnBattleResolved — саме там, де CombatState.Outcome вже != Ongoing.
        /// </summary>
        private bool _battleAutoResolvedThisCall;

        // ---- данж ----
        private DungeonRun _dungeon;

        // ---- сцена ----
        private ScenePlayback _scenePlayback;
        private SessionState _sceneReturn;

        // ---- створення протагоніста (R12) ----
        private string _pendingName;
        private Gender _pendingGender = Gender.Male;
        // Поправка №19.3: зовнішність героя (лише вигляд, без чисел балансу); null — образ за
        // замовчуванням для обраної статі (AppearanceCatalog.DefaultProtagonist).
        private Appearance _pendingAppearance;
        private string _pendingBackgroundId = "warrior";
        private Gender _protagonistGender = Gender.Male;

        // ---- Поправка №15.1: пізніше приєднання, шлях «Таверна» ----
        /// <summary>Хто заплановано наступним, кого приведе Таверна — id, або null, поки нікого не заплановано.</summary>
        private string _pendingTavernSpecialistId;
        /// <summary>Доба, на яку заплановано прихід <see cref="_pendingTavernSpecialistId"/>.</summary>
        private int _pendingTavernDueDay;

        // ---- рішення/квест/фінал/підсумок ----
        private PendingDecision _currentPending;
        private QuestOfferView _currentQuestOffer;
        // Фікс-ревью (major, знайдено тур-автоплеєм): OfferQuestStage — по суті
        // запит стану ("що зараз пропонується"), але логував "quest.offered" на
        // КОЖЕН виклик. І HubScreen, і NightScreen малюють свою пропозицію
        // щокадру й самі кешують результат ПРО СЕБЕ (щоб не топити стрічку
        // подій дублями всередині одного екрана) — але два різні екрани того
        // самого дня (ранковий хаб і вечірній/нічний) кожен тримає ВЛАСНИЙ кеш,
        // тож обидва однаково викликають цей метод і разом дають два підряд
        // однакові рядки "Нова пропозиція: ..." за одну добу — Поправка №3.7/
        // §2 рядок 11 такого не дозволяє. Один вибір і той самий етап того
        // самого квесту логується рівно раз — ключ скидається, щойно етап
        // справді змінюється (ResolveQuestChoice/NewGame/ApplySave).
        //
        // Множина, а не «останній ключ»: ліній квестів дві (Гафія і Максим), і
        // вечірня панель пропонує обидві щовечора — з одним «останнім ключем»
        // вони перебивали одна одну, і «Нова пропозиція: Максим / Гафія»
        // писалась у стрічку щодоби (довгий автопрогін 25.09.2026).
        private readonly HashSet<string> _loggedQuestOfferKeys = new HashSet<string>(StringComparer.Ordinal);
        private DayReportView _lastDayReport;
        private DayPhase _lastPhase = DayPhase.Day;
        private bool _summaryAcknowledged;
        private bool _freePlay;

        /// <summary>
        /// Хоч одну ніч партії гравець провів на варті — пункт журналу
        /// «Ніч: патруль чи сон». Раніше пункт чекав події "night.forewarn",
        /// якої ядро не пише ніде, і не позначався ніколи (знайдено довгим
        /// автопрогоном 25.09.2026: 32 доби, половина ночей на варті, «ще ні»).
        /// </summary>
        private bool _patrolledANight;
        private bool _finaleResolved;
        private string _finaleOutcomeKey;

        // ---- сейв-слоти (Morning лише, R13) ----
        private readonly Dictionary<int, string> _slots = new Dictionary<int, string>();

        // ---- стрічка подій (§4.3) ----
        private readonly List<GameEvent> _dayLog = new List<GameEvent>();
        public IReadOnlyList<GameEvent> DayLog => _dayLog;

        /// <summary>
        /// Лічильник очищень <see cref="_dayLog"/> (росте на кожен <see cref="ClearDayLog"/>
        /// і на скидання в NewGame). Призначення — дати <c>BotRunner.CollectDelta</c>
        /// (і будь-якому іншому інкрементальному читачу DayLog) НАДІЙНУ ознаку
        /// «це вже нова фаза, курсор читання треба скинути в нуль», замість
        /// висновку з розміру логу (`log.Count &lt; cursor`), який мовчки не
        /// спрацьовує, коли нова фаза встигла дати записів БІЛЬШЕ, ніж курсор
        /// мав на кінець попередньої, — перші записи нової фази тоді тихо
        /// губляться для читача (не для самої гри: LogEvent/DayLog їх бачить,
        /// тільки бот-водій їх не забирає). Саме так один реальний прогін
        /// Steward зловив "forewarn.level2" (Тугар, доба 6/День), що є в
        /// DayLog, але не доходить до bot-логу.
        /// </summary>
        private int _dayLogVersion;
        internal int DayLogVersion => _dayLogVersion;

        /// <summary>
        /// Журнал механік (Поправка №7.8): КУМУЛЯТИВНИЙ (на відміну від
        /// <see cref="_dayLog"/>, який чистить кожна фаза) набір ключів усіх
        /// подій, що коли-небудь пішли в DayLog за цей прогін —
        /// <see cref="GetMechanicsJournal"/> рахує "seen" саме по ньому.
        /// </summary>
        private readonly HashSet<string> _seenEventKeys = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Скільки записів <c>report.Incidents</c> уже перекладено у DayLog цієї
        /// фази (<see cref="TranslateReport"/>). DayProcessor.BuildReport
        /// повертає ПОВНИЙ накопичений список інцидентів фази щоразу (аудит
        /// П10: 2+ рішення в одній фазі), тож без цього лічильника кожен
        /// наступний виклик TranslateReport у тій самій фазі перекладав би вже
        /// перекладені інциденти заново — дублікати "decision.resolved" у
        /// DayLog (§4.3: єдине джерело правди для рахунку наслідків).
        /// </summary>
        private int _translatedIncidentCount;

        /// <summary>Ідемпотентний бонус Гафії (D1, seamsForD1): застосовується рівно раз за прогін.</summary>
        private bool _hafiyaGrassBonusApplied;

        /// <summary>
        /// GameSession отримує кубик ЛИШЕ звідси (конструктор) або через
        /// <see cref="NewGameOptions.Roller"/> — ніколи не будує його сам.
        /// Той самий екземпляр можна передати в кілька сесій підряд (Title →
        /// NewGame → ContinueGame): справжнє сідування відбувається окремо,
        /// через <see cref="IDiceRoller.RestoreState"/>.
        /// </summary>
        public GameSession(IDiceRoller roller = null)
        {
            _roller = roller;
        }

        // =====================================================================
        // Title
        // =====================================================================

        public void NewGame(NewGameOptions o)
        {
            o = o ?? new NewGameOptions();

            // Промоція кубика з NewGameOptions.Roller (фікс-ревью, див. коментар
            // поля _roller): конструкторський канал має пріоритет, якщо задані
            // обидва; якщо GameSession зібрано з roller:null, а кубик передали
            // сюди (напр. ContinueGame(slot, roller), де HitRule на момент
            // виклику ще Threshold-заглушка до LoadState) — саме тут єдине
            // місце, де він стає "тим самим" кубиком для решти сесії.
            _roller = _roller ?? o.Roller;

            _cfg = new BalanceConfig();
            _world = FirstHourWorld.Build(tier: 1, requirePlayerDecision: true, balance: _cfg,
                testBuildOneDayConstruction: o.TestBuildOneDayConstruction,
                testBuildTensionPace: o.TestBuildTensionPace);
            _state = _world.BaseState;
            _state.ProtagonistId = ProtagonistId;
            _works = _world.CityWorks;
            _processor = _world.Processor;
            _cycle = _world.Cycle;
            _worldRoster = _world.Roster;
            _party = _world.Party;
            _sites = _world.Sites;
            _flags = _world.Flags;
            _quests = _world.Quests;
            _factions = _world.Factions;
            _points = _world.Points;
            _readiness = _world.Readiness;
            _inventory = _world.Inventory;
            _rosterAdapter = _processor.Casualties as RosterAdapter;
            _rosterView = _processor.Roster;
            _repeats = _processor.Repeats;
            _crisis = new ForcedCrisisSource(5, 1);
            _defectionWatch = new DefectionWatch();
            _prisoners = new PrisonerLedger();
            _captives = new CaptivityLedger();
            _raidGroupId = null;
            _dossier = new EnemyDossierBook();
            _bribeRefusals.Clear();
            _pendingSurrenders.Clear();
            _recruits.Clear();

            _arcFlags.Clear();
            _arcRuns = new List<CompanionArcRun>();
            foreach (var arc in DefaultArcs.All())
                _arcRuns.Add(new CompanionArcRun(arc, _arcFlags));

            _activeArcCompanionId = null;
            _activeArcChapterQuestCompanion.Clear();
            _currentSceneId = null;
            _seenEventKeys.Clear();
            _patrolledANight = false;

            _slots.Clear();
            _dayLog.Clear();
            _dayLogVersion++;
            _currentPending = null;
            _currentQuestOffer = null;
            _loggedQuestOfferKeys.Clear();
            _lastDayReport = null;
            _dungeon = null;
            _battle = null;
            _resume = null;
            _battleAutoResolvedThisCall = false;
            _summaryAcknowledged = false;
            _freePlay = false;
            _finaleResolved = false;
            _finaleOutcomeKey = null;
            _translatedIncidentCount = 0;
            _hafiyaGrassBonusApplied = false;
            _bargainedTimeBonusApplied = false;
            _myroslavaHintBonusApplied = false;

            _hitRule = o.HitRule;
            _tensionPace = o.TestBuildTensionPace;
            _seed = o.Seed;
            _ironman = o.Ironman;

            if (_hitRule == HitRuleKind.Percent)
            {
                if (_roller == null)
                    throw new InvalidOperationException(
                        "PercentRule потребує IDiceRoller, injected ззовні (Game.Gameplay.Combat.SeededDiceRoller) " +
                        "у конструктор GameSession або NewGameOptions.Roller — Core сам кубик не створює (R1).");
                _roller.RestoreState(_seed.ToString(CultureInfo.InvariantCulture));
            }

            // Поправка №12.10: _pendingBackgroundId тепер керує ще й тим,
            // хто прибився до гурту (ArrivalsPool читає його з BeginOpeningScene
            // нижче) — скидаємо його тут БЕЗУМОВНО, а не лише в гілці
            // Creation. Інакше повторний NewGame(SkipCreation:true) на тому
            // самому екземплярі GameSession (боти/тести/журнальний тур)
            // успадковував би передісторію МИНУЛОЇ гри цього ж інстансу.
            _pendingName = null;
            _pendingGender = Gender.Male;
            _pendingAppearance = null;
            _pendingBackgroundId = Backgrounds.All()[0].Id;
            _pendingTavernSpecialistId = null;
            _pendingTavernDueDay = 0;

            if (o.SkipCreation)
            {
                BeginOpeningScene();
            }
            else
            {
                State = SessionState.Creation;
            }
        }

        /// <summary>
        /// Занести в пам'ять сесії готовий зліпок диска (той самий рядок,
        /// що повернув <see cref="SaveState"/> у МИНУЛОМУ запуску застосунку)
        /// під номером слота — ДО виклику <see cref="ContinueGame"/>. Core сам
        /// файли не читає (той самий принцип, що й у <see cref="RestoreFromBlob"/>):
        /// фактичне читання файлу слота — робота викликача (Alpha.Play/Unity
        /// SaveLoadScreen), <c>_slots</c> лишається пам'яттю самого інстансу.
        /// Легально лише з Title — той самий стан, з якого йде подальший
        /// <see cref="ContinueGame"/>.
        /// </summary>
        public void PreloadSlot(int slot, string blob)
        {
            RequireState(SessionState.Title);
            if (!string.IsNullOrEmpty(blob)) _slots[slot] = blob;
        }

        /// <summary>
        /// Продовжити збережену гру. Сигнатура додає необов'язковий
        /// <paramref name="roller"/> понад §4.1 (<c>bool ContinueGame(int slot)</c>)
        /// свідомо: інакше сесія Percent-бою не мала б звідки взяти кубик до
        /// того, як прочитає збережений <c>hitRule</c> зі слота (той самий
        /// R1-конфлікт, що й у <see cref="NewGameOptions.Roller"/>).
        ///
        /// Фікс-ревью (реальний блокер): раніше тут спершу викликався
        /// <see cref="NewGame"/>, який БЕЗУМОВНО чистить <c>_slots</c>
        /// (свіжий світ — порожні слоти), а вже ПОТІМ <see cref="LoadState"/>
        /// читав той самий, щойно спорожнений словник — команда НІКОЛИ не
        /// могла успішно завантажити жоден слот, і при цьому невдалий виклик
        /// однаково встигав збудувати нову гру і піти зі стану <c>Title</c>
        /// (SkipCreation=true → <c>State=Scene</c>) ще до повернення <c>false</c>,
        /// тож "не вдалось продовжити" мовчки лишало сесію в невідомому світі
        /// замість чесного <c>Title</c>. Тепер зліпок читається ДО <see cref="NewGame"/>
        /// (і без нього рано виходимо, не чіпаючи стан), а після — повертається
        /// назад у словник для <see cref="LoadState"/>.
        /// </summary>
        public bool ContinueGame(int slot, IDiceRoller roller = null)
        {
            RequireState(SessionState.Title);

            string blob;
            if (!_slots.TryGetValue(slot, out blob) || string.IsNullOrEmpty(blob)) return false;

            // Темп Напруги визначає, як побудовано світ (пороги смуг, тик,
            // накопичувач кризи), тож його треба знати ДО NewGame, а не лише
            // відновити полем, як hitRule. Старий сейв без поля — тестовий темп.
            NewGame(new NewGameOptions
            {
                SkipCreation = true, HitRule = HitRuleKind.Threshold, Roller = roller,
                TestBuildTensionPace = PeekTensionPace(blob) ?? true
            });
            _slots[slot] = blob;
            return LoadState(slot);
        }

        public void NewTrainingBattle(TrainingBattleOptions o)
        {
            o = o ?? new TrainingBattleOptions();
            var fromState = State;

            if (o.HitRule == HitRuleKind.Percent && _roller != null)
                _roller.RestoreState(_seed.ToString(CultureInfo.InvariantCulture));

            _battle = DefaultCombatContent.Training(_cfg, o.HitRule, o.HitRule == HitRuleKind.Percent ? _roller : null);
            _resume = new SuspendToken(SuspendReason.TrainingSkirmish, fromState);
            State = SessionState.Battle;
            LogEvent("combat.training.started");
            OnBattleResolved();
        }

        // =====================================================================
        // Creation (R12)
        // =====================================================================

        public void SetProtagonistName(string name)
        {
            RequireState(SessionState.Creation);
            _pendingName = name;
        }

        public void SetProtagonistGender(Gender gender)
        {
            RequireState(SessionState.Creation);
            _pendingGender = gender;
            // інша стать — інший набір (зачіски, крій): образ скидається на дефолтний цієї статі
            if (_pendingAppearance != null && _pendingAppearance.Gender != gender) _pendingAppearance = null;
        }

        /// <summary>
        /// Зовнішність героя на екрані створення (Поправка №19.3). Приймається лише образ із відомих
        /// частин набору (<see cref="KitParts"/>) і кольорів #RRGGBB; стать образу стає статтю героя.
        /// Повертає false і нічого не змінює, якщо образ некоректний.
        /// </summary>
        public bool SetProtagonistAppearance(Appearance appearance)
        {
            RequireState(SessionState.Creation);
            if (!IsValidAppearance(appearance)) return false;
            _pendingAppearance = appearance.Clone();
            _pendingGender = appearance.Gender;
            return true;
        }

        internal static bool IsValidAppearance(Appearance a)
        {
            if (a == null || !KitParts.IsKnownCulture(a.Culture) || !Appearance.IsColor(a.HairColor)) return false;
            if (a.Hair.Length > 0 && !KitParts.IsBuilt(a.Hair)) return false;
            if (a.FacialHair.Length > 0 && (a.Gender == Gender.Female || !KitParts.IsBuilt(a.FacialHair))) return false;
            if (a.SignatureWeapon.Length > 0 && !KitParts.IsBuilt(a.SignatureWeapon)) return false;
            foreach (var p in a.Outfit)
                if (!KitParts.IsBuilt(p.Part) || !Appearance.IsColor(p.Color)) return false;
            foreach (var acc in a.Accents)
                if (!KitParts.IsKnown(acc)) return false;
            return true;
        }

        /// <summary>
        /// Зовнішність будь-кого з загону (Поправка №19.1): герой — обране гравцем; іменні — образ із
        /// першоджерела (<see cref="AppearanceCatalog.Named"/>); решта — стабільний образ від id.
        /// Повертає копію: зміна результату не змінює гри. Надіте спорядження гра накладає зверху.
        /// </summary>
        public Appearance GetAppearance(string companionId)
        {
            if (string.Equals(companionId, ProtagonistId, StringComparison.Ordinal))
                return (_pendingAppearance ?? AppearanceCatalog.DefaultProtagonist(_pendingGender)).Clone();
            var named = AppearanceCatalog.Named(companionId);
            if (named != null) return named;
            var c = _worldRoster?.Get(companionId);
            string cardId = c?.Card?.Id;
            if (cardId != null && (named = AppearanceCatalog.Named(cardId)) != null) return named;
            return AppearanceCatalog.ForUnnamed(companionId, StableGender(companionId));
        }

        private static Gender StableGender(string id)
        {
            uint h = 2166136261u;
            foreach (char ch in id ?? "") { h ^= ch; h *= 16777619u; }
            return (h & 1u) == 0u ? Gender.Male : Gender.Female;
        }

        public void SetProtagonistBackground(string presetId)
        {
            RequireState(SessionState.Creation);
            _pendingBackgroundId = presetId;
        }

        public SessionState ConfirmCreation()
        {
            RequireState(SessionState.Creation);
            var preset = Backgrounds.ById(_pendingBackgroundId) ?? Backgrounds.All()[0];
            var protagonist = _worldRoster.Get(ProtagonistId);
            ProtagonistCreation.Apply(protagonist, preset, _pendingName);
            _protagonistGender = _pendingGender;

            LogEvent("creation.confirmed", Args("backgroundId", preset.Id));
            BeginOpeningScene();
            return State;
        }

        public ProtagonistCreationView GetProtagonistCreationView()
        {
            var ids = new List<string>();
            foreach (var b in Backgrounds.All()) ids.Add(b.Id);
            return new ProtagonistCreationView
            {
                Name = _pendingName,
                Gender = _pendingGender,
                BackgroundId = _pendingBackgroundId,
                AvailableBackgrounds = ids,
                Appearance = (_pendingAppearance ?? AppearanceCatalog.DefaultProtagonist(_pendingGender)).Clone()
            };
        }

        // =====================================================================
        // Scene (§3.0/§3.1: відкриття та розв'язка вузла 1)
        // =====================================================================

        private void BeginOpeningScene()
        {
            // Поправка №12.10: передісторія вже відома на цей момент
            // (ConfirmCreation застосовує preset і кличе BeginOpeningScene
            // ПІСЛЯ; SkipCreation лишає дефолт "warrior") — сцена рахує три
            // гілки вибору першої будівлі наперед, по одній на кожну
            // відповідь Тугарові (OpeningScenes.NeighbourWithADemand).
            BeginScene(OpeningScenes.NeighbourWithADemand(_pendingBackgroundId), SessionState.Morning);
        }

        private void BeginScene(Scene scene, SessionState afterState)
        {
            _scenePlayback = new ScenePlayback(scene);
            _sceneReturn = afterState;
            _currentSceneId = scene?.Id;
            State = SessionState.Scene;
            _lastFramedActorId = null;
            _lastFramedSecondActorId = null;
        }

        public SceneStepView AdvanceScene()
        {
            RequireState(SessionState.Scene);
            if (_scenePlayback == null) throw new InvalidOperationException("Немає активної сцени.");

            _scenePlayback.Next();
            return BuildSceneStepView();
        }

        /// <summary>
        /// Розв'язує поточний вибір сцени (Поправка №7.8): визначає
        /// виконавця (протагоніст, якщо варіант не називає присутнього
        /// напарника), за наявності — резолвить перевірку ІСНУЮЧИМ
        /// <see cref="CheckResolver"/> (одна й та сама лестниця, що й в
        /// інцидентах/квестах), застосовує наслідок ЄДИНИМ застосувачем
        /// (<see cref="ApplyConsequence"/>) і веде сцену далі
        /// (<see cref="ScenePlayback.Choose"/>) — на метку, переходом, або
        /// просто лінійно.
        /// </summary>
        public SceneStepView ChooseSceneOption(int optionIndex)
        {
            RequireState(SessionState.Scene);
            if (_scenePlayback == null || !_scenePlayback.IsAwaitingChoice)
                throw new InvalidOperationException("Сцена не стоїть на виборі.");

            var options = _scenePlayback.PendingOptions;
            if (options == null || optionIndex < 0 || optionIndex >= options.Count)
                throw new ArgumentOutOfRangeException(nameof(optionIndex));

            var option = options[optionIndex];
            string choiceId = _scenePlayback.ChoiceId;

            OutcomeBand band = OutcomeBand.Base;
            QuestConsequence consequence = option.Consequence ?? QuestConsequence.Empty();

            if (option.HasCheck)
            {
                string performerId = ResolveScenePerformerId(option.PerformerCompanionId);
                var actor = BuildSingleActor(performerId);
                string topic = (_currentSceneId ?? "scene") + "." + (choiceId ?? "choice") + "." + (option.Id ?? optionIndex.ToString(CultureInfo.InvariantCulture));
                var request = new CheckRequest(option.CheckSkill, option.Threshold, option.Approach, topic);
                var outcome = CheckResolver.Resolve(request, new SingleActorRosterView(actor), _repeats, _processor.CurrentDay, _cfg);
                band = outcome.Band;
                int bandIndex = (int)band;
                consequence = option.ConsequenceByBand != null && option.ConsequenceByBand.Length > bandIndex
                    ? (option.ConsequenceByBand[bandIndex] ?? QuestConsequence.Empty())
                    : QuestConsequence.Empty();
            }

            ApplyConsequence(consequence, "scene:" + (_currentSceneId ?? "scene"));
            LogEvent("scene.choice.made", Args("sceneId", _currentSceneId ?? string.Empty,
                "optionId", option.Id ?? optionIndex.ToString(CultureInfo.InvariantCulture), "band", band.ToString()));

            if (string.Equals(choiceId, OpeningScenes.TugarOfferChoiceId, StringComparison.Ordinal))
                ApplyArrivalsPool(option.Id);

            ApplyBetrayalConfrontationSideEffectsIfNeeded();
            ApplyZakharCouncilSideEffectsIfNeeded();

            _scenePlayback.Choose(optionIndex);
            return BuildSceneStepView();
        }

        /// <summary>Спільний хвіст AdvanceScene/ChooseSceneOption: показ кадру, слід антагоніста, фініш сцени (+ завершення глави арки, якщо ця сцена — її зміст).</summary>
        private SceneStepView BuildSceneStepView()
        {
            var frame = _scenePlayback.Current;
            var view = new SceneStepView
            {
                ActorId = frame.ActorId,
                SecondActorId = frame.SecondActorId,
                SpeakerId = frame.SpeakerId,
                LineKey = frame.LineKey,
                EffectKey = frame.EffectKey,
                IsFinished = _scenePlayback.IsFinished,
                TransitionKey = _scenePlayback.TransitionKey
            };

            LogNamedAntagonistSeen(frame.ActorId, isSecondActorSlot: false);
            LogNamedAntagonistSeen(frame.SecondActorId, isSecondActorSlot: true);

            if (_scenePlayback.IsAwaitingChoice)
            {
                view.IsChoice = true;
                view.ChoiceId = _scenePlayback.ChoiceId;
                view.Options = BuildSceneChoiceOptions(_scenePlayback.PendingOptions);
                return view;
            }

            if (_scenePlayback.IsFinished)
            {
                State = _sceneReturn;
                _scenePlayback = null;
                LogEvent("scene.finished", Args("transition", view.TransitionKey));
                _currentSceneId = null;

                if (_activeArcCompanionId != null)
                {
                    string companionId = _activeArcCompanionId;
                    _activeArcCompanionId = null;
                    CompleteArcChapterFor(companionId);
                }
            }
            return view;
        }

        /// <summary>Прев'ю варіантів вибору (§4.2 DecisionOptionView-подібно): показує скіл/поріг/полосу-прев'ю заздалегідь (інваріант 8), жодного прихованого числа (R17).</summary>
        private List<DecisionOptionView> BuildSceneChoiceOptions(IReadOnlyList<SceneChoiceOption> options)
        {
            var result = new List<DecisionOptionView>();
            if (options == null) return result;

            foreach (var opt in options)
            {
                if (!opt.HasCheck)
                {
                    result.Add(new DecisionOptionView { TextKey = opt.TextKey, HasCandidate = true });
                    continue;
                }

                string performerId = ResolveScenePerformerId(opt.PerformerCompanionId);
                var actor = BuildSingleActor(performerId);
                string topic = (_currentSceneId ?? "scene") + ".preview." + (opt.Id ?? opt.TextKey ?? "opt");
                var request = new CheckRequest(opt.CheckSkill, opt.Threshold, opt.Approach, topic);
                var preview = CheckResolver.Preview(request, new SingleActorRosterView(actor), _repeats, _processor.CurrentDay, _cfg);

                result.Add(new DecisionOptionView
                {
                    TextKey = opt.TextKey,
                    SkillKey = opt.CheckSkill.Id,
                    Threshold = opt.Threshold,
                    Form = opt.Approach.ToString(),
                    BestActorId = preview.BestActorId,
                    HasCandidate = preview.HasCandidate,
                    ExpectedBand = preview.ExpectedBand.ToString()
                });
            }
            return result;
        }

        /// <summary>Виконавець варіанту: названий напарник, якщо він присутній у поселенні — інакше протагоніст (спека виконавця «за замовчуванням»).</summary>
        private string ResolveScenePerformerId(string namedCompanionId)
        {
            if (string.IsNullOrEmpty(namedCompanionId)) return ProtagonistId;
            var c = _worldRoster?.Get(namedCompanionId);
            if (c == null) return ProtagonistId;
            var adapter = new Base.CompanionActorAdapter(c, false, _cfg);
            return adapter.IsPresentInSettlement ? namedCompanionId : ProtagonistId;
        }

        private ISettlementActor BuildSingleActor(string companionId)
        {
            var c = _worldRoster.Get(companionId);
            bool isProtagonist = string.Equals(companionId, ProtagonistId, StringComparison.Ordinal);
            return new Base.CompanionActorAdapter(c, isProtagonist, _cfg);
        }

        /// <summary>
        /// Ростер із рівно одним актором (Поправка №7.8): сценовий вибір
        /// резолвиться конкретним виконавцем ("протагоніст, якщо варіант не
        /// називає присутнього напарника"), а не "найкращим серед
        /// присутніх", як звичайний <see cref="CheckResolver"/> робить для
        /// інцидентів/квестів без прив'язки до конкретної людини. Обгортка
        /// дозволяє скористатись ТИМ САМИМ резолвером без нового API.
        /// </summary>
        private sealed class SingleActorRosterView : IRosterView
        {
            private readonly ISettlementActor _actor;
            private readonly List<ISettlementActor> _list;

            public SingleActorRosterView(ISettlementActor actor)
            {
                _actor = actor;
                _list = new List<ISettlementActor> { actor };
            }

            public IReadOnlyList<ISettlementActor> PresentActors => _list;
            public ISettlementActor Protagonist => _actor != null && _actor.IsProtagonist ? _actor : null;
        }

        /// <summary>
        /// §6.1 рядок 39 (докладено Фазою F): коли кадр сцени/сигналу показує
        /// іменного антагоніста, пишемо char.seen у DayLog — окремий
        /// нарративний слід появи персонажа, який AllMechanicsCoverageTests
        /// звіряє по всіх бот-прогонах. Список навмисно короткий: сьогодні
        /// єдиний антагоніст із реальним кадром у сцені — Тугар Вовк
        /// (R7/Поправка №5.10); командир орди (`horde_commander`) лишається
        /// карткою-заглушкою без першоджерела (§5.7) і кадру в жодній сцені
        /// цієї збірки, тож у список поки не входить.
        /// </summary>
        private static readonly HashSet<string> NamedAntagonistIds = new HashSet<string> { "tuhar" };

        /// <summary>
        /// <see cref="SceneFrame.ActorId"/>/<see cref="SceneFrame.SecondActorId"/>
        /// ТРИМАЮТЬСЯ між кроками (ScenePlayback.Next(): лише Shot-крок їх
        /// міняє, Line/Beat/Effect лишають як є) — тож "хто в кадрі" не
        /// змінюється, поки камера не переріже на інший план. char.seen мав
        /// би відзначати САМУ появу (новий план), а не кожен наступний
        /// AdvanceScene()-крок, поки той самий план тримається (інакше одна
        /// поява давала б 2 записи — Shot і репліка під тим самим планом).
        /// </summary>
        private string _lastFramedActorId;
        private string _lastFramedSecondActorId;

        private void LogNamedAntagonistSeen(string actorId, bool isSecondActorSlot)
        {
            if (string.IsNullOrEmpty(actorId))
            {
                if (isSecondActorSlot) _lastFramedSecondActorId = actorId; else _lastFramedActorId = actorId;
                return;
            }

            string previous = isSecondActorSlot ? _lastFramedSecondActorId : _lastFramedActorId;
            if (isSecondActorSlot) _lastFramedSecondActorId = actorId; else _lastFramedActorId = actorId;

            if (actorId == previous) return; // той самий план — уже зараховано
            if (!NamedAntagonistIds.Contains(actorId)) return;
            LogEvent("char.seen", Args("char", actorId));
        }

        // =====================================================================
        // Morning
        // =====================================================================

        public AssignmentResult Assign(string companionId, string slotId)
        {
            RequireMorningOrFreePlay();
            var result = _state.TryAssign(companionId, slotId);
            if (result == AssignmentResult.Success)
                LogEvent("assign.made", Args("companionId", companionId, "slotId", slotId));
            return result;
        }

        public void Unassign(string slotId)
        {
            RequireMorningOrFreePlay();
            _state.Unassign(slotId);
            LogEvent("assign.cleared", Args("slotId", slotId));
        }

        public BuildOrderResult OrderBuilding(string id)
        {
            RequireMorningOrFreePlay();
            var r = _works.Order(id, _state, _processor.CurrentDay, _cfg);
            if (r == BuildOrderResult.Started) LogEvent("city.building.ordered", Args("buildingId", id));
            return r;
        }

        public CouncilOrderResult OrderRaid()
        {
            RequireMorningOrFreePlay();
            var beforeTuhar = _factions.BandOf(DefaultFactions.TuharBoyars);
            var beforeCommunity = _factions.BandOf(DefaultFactions.Community);
            var r = _works.OrderRaid(_state, _processor.CurrentDay, _cfg, _factions);
            if (r == CouncilOrderResult.Queued) LogEvent("council.raid.ordered");
            LogFactionBandChange(DefaultFactions.TuharBoyars, beforeTuhar);
            LogFactionBandChange(DefaultFactions.Community, beforeCommunity);
            return r;
        }

        public CouncilOrderResult OrderSettlers()
        {
            RequireMorningOrFreePlay();
            var r = _works.OrderSettlers(_state, _processor.CurrentDay, _cfg);
            if (r == CouncilOrderResult.Queued) LogEvent("council.settlers.ordered");
            return r;
        }

        public CouncilOrderResult OrderDecree(string favoredFactionId, string costFactionId = null)
        {
            RequireMorningOrFreePlay();
            var beforeFavored = _factions.BandOf(favoredFactionId);
            var beforeCost = _factions.BandOf(costFactionId);
            var r = _works.OrderDecree(_state, _processor, _factions, favoredFactionId, costFactionId, _processor.CurrentDay, _cfg);
            if (r == CouncilOrderResult.Applied)
                LogEvent("council.decree", Args("favored", favoredFactionId, "cost", costFactionId ?? string.Empty));
            LogFactionBandChange(favoredFactionId, beforeFavored);
            LogFactionBandChange(costFactionId, beforeCost);
            return r;
        }

        public CouncilOrderResult OrderDiplomacy(string factionId)
        {
            RequireMorningOrFreePlay();
            var before = _factions.BandOf(factionId);
            var r = _works.OrderDiplomacy(_state, _factions, factionId, _processor.CurrentDay, _cfg);
            if (r == CouncilOrderResult.Applied) LogEvent("council.diplomacy", Args("factionId", factionId));
            LogFactionBandChange(factionId, before);
            return r;
        }

        /// <summary>
        /// Інваріант 4 для ради: раніше зміна щабля довіри логувалась лише з
        /// боку наслідків квестів/данжу (<see cref="ApplyFactionDelta"/>) —
        /// дії ради (Указ/Дипломатія/Облава) міняли ставлення НАПРЯМУ через
        /// CityWorks, і зміна полоси йшла німо. Той самий приём — звірка
        /// до/після — але тут виклик не сам рухає ставлення (це робить
        /// CityWorks усередині), тому знімок беремо ЗОВНІ, до виклику.
        /// </summary>
        private void LogFactionBandChange(string factionId, FactionStandingBand before)
        {
            if (string.IsNullOrEmpty(factionId)) return;
            var after = _factions.BandOf(factionId);
            if (after != before)
                LogEvent("faction.standing_changed", Args("factionId", factionId, "band", after.ToString()));
        }

        public CouncilOrderResult OrderInvestment(string buildingId)
        {
            RequireMorningOrFreePlay();
            var r = _works.OrderInvestment(_state, buildingId, _processor.CurrentDay, _cfg);
            if (r == CouncilOrderResult.Queued) LogEvent("council.invest", Args("buildingId", buildingId ?? string.Empty));
            return r;
        }

        public CouncilOrderResult OrderPrepareThreat()
        {
            RequireMorningOrFreePlay();
            var r = _works.OrderPrepareThreat(_state, _processor.CurrentDay, _cfg);
            if (r == CouncilOrderResult.Applied) LogEvent("council.prepare_threat");
            return r;
        }

        public CouncilOrderResult OrderOutfitExpedition(string siteId)
        {
            RequireMorningOrFreePlay();
            var r = _works.OrderOutfitExpedition(_state, siteId, _processor.CurrentDay, _cfg);
            if (r == CouncilOrderResult.Applied) LogEvent("council.outfit_expedition", Args("siteId", siteId));
            return r;
        }

        public ExpeditionPreviewView PreviewExpedition(string siteId, ExpeditionApproach approach, IReadOnlyList<string> companionIds)
        {
            RequireMorningOrFreePlay();
            if (approach == ExpeditionApproach.Delve)
            {
                var rooms = DefaultDungeon.Rooms(siteId);
                // Прев'ю (owner: "the quiet candidate"): _dungeon ще НЕ
                // існує до реального DepartExpedition — companionIds
                // параметра (та сама майбутня партія) і є "партія" на цей момент.
                var firstRoom = rooms != null && rooms.Count > 0 ? BuildDungeonRoomView(rooms[0], companionIds) : null;
                return new ExpeditionPreviewView
                {
                    SiteId = siteId, Approach = approach, IsDelve = true, FirstRoom = firstRoom, Days = 2,
                    WaitingSpecialistId = WaitingSpecialistAt(siteId)
                };
            }

            var site = FindSite(siteId);
            if (site == null) return new ExpeditionPreviewView { SiteId = siteId, Approach = approach };

            var actors = ResolveActors(companionIds);
            var preview = ExpeditionResolver.Preview(site, approach, actors, _sites, _cfg);
            return new ExpeditionPreviewView
            {
                SiteId = siteId,
                Approach = approach,
                Threshold = preview.Threshold,
                PartyValue = preview.PartyValue,
                Days = preview.Days,
                ExpectedBand = preview.Band.ToString(),
                ExpectedBuildComponent = preview.BuildComponent,
                ExpectedCraftComponent = preview.CraftComponent,
                ExpectedGold = preview.Gold,
                ExpectedWounded = preview.ExpectedWounded,
                IsDelve = false,
                WaitingSpecialistId = WaitingSpecialistAt(siteId)
            };
        }

        /// <summary>
        /// Поправка №15.1: відсутній фахівець, прив'язаний до
        /// <paramref name="siteId"/> (<see cref="ArrivalsPool.ExpeditionSiteOf"/>),
        /// якщо він ще НЕ прибув — для «тут бачили: {ім'я}» у прев'ю
        /// вилазки. Null, коли на точці ніхто не прив'язаний, або фахівець
        /// уже в гурті.
        /// </summary>
        private string WaitingSpecialistAt(string siteId)
        {
            if (string.IsNullOrEmpty(siteId) || _worldRoster == null) return null;
            foreach (var id in ArrivalsPool.AllSpecialistIds)
            {
                if (!string.Equals(ArrivalsPool.ExpeditionSiteOf(id), siteId, StringComparison.Ordinal)) continue;
                var c = _worldRoster.Get(id);
                return c != null && c.Status == CompanionStatus.NotArrived ? id : null;
            }
            return null;
        }

        public DispatchResult DepartExpedition(string siteId, ExpeditionApproach approach, IReadOnlyList<string> companionIds, int days)
        {
            RequireMorningOrFreePlay();
            var site = approach == ExpeditionApproach.Delve ? new ExpeditionSite(siteId, siteId) : FindSite(siteId);
            if (site == null) return DispatchResult.NoSuchSite;

            var result = ExpeditionRunner.Depart(_state, _party, site, approach, companionIds, days, _sites, _cfg);
            if (result != DispatchResult.Success) return result;

            // B5: разовий бонус наступній вилазці (OrderOutfitExpedition) — сид
            // застосовується тут же, поштучним доданням до вже замороженого
            // результату (ExpeditionResult.Gold — публічне поле), не чіпаючи
            // файлів B7 (ExpeditionRunner/ExpeditionResolver — виключно їхні).
            //
            // Блокер-фікс ревью: PeekExpeditionOutfitBuff() НЕ знімає бонус —
            // знімаємо (TakeExpeditionOutfitBuff) лише коли siteId справді
            // збігається з тим, на який його замовили. Раніше бонус забирався
            // безумовно на першому ж відправленні (навіть на ІНШУ точку) і
            // губився назавжди, ніколи не діставшись тієї, на яку був
            // замовлений (CityWorks.OrderOutfitExpedition документує це саме
            // так: «одноразовий бонус наступній вилазці НА ТОЧКУ siteId».
            var buff = _works.PeekExpeditionOutfitBuff();
            if (buff != null && string.Equals(buff.SiteId, siteId, StringComparison.Ordinal) && _party.PendingResult != null)
            {
                _works.TakeExpeditionOutfitBuff();
                _party.PendingResult.Gold += buff.BonusValue;
            }

            LogEvent("expedition.departed", Args("siteId", siteId, "approach", approach.ToString()));
            // П11 (аудит розривів): вилазка лунає своїм доменом на виході.
            LogEvent("signal.domain", Args("domain", site.DomainTag ?? "road", "phase", "depart"));

            if (approach == ExpeditionApproach.Delve)
            {
                _dungeon = DefaultDungeon.Start(siteId, companionIds, _cfg);
                State = SessionState.Dungeon;
                LogEvent("dungeon.push", Args("room", _dungeon?.CurrentRoom?.Id, "depth", _dungeon?.Depth.ToString()));
            }

            return DispatchResult.Success;
        }

        public QuestOfferView OfferQuestStage(string questId)
        {
            RequireAnyState(SessionState.Morning, SessionState.Evening, SessionState.Night);

            // Поправка №12.10: квест Гафії — про Гафію; якщо вона не
            // прибила до гурту цього прогону, пропозиція не з'являється
            // взагалі (не просто мовчить — ЕКРАН і не викликав би
            // ResolveQuestChoice, бо офера немає).
            if (string.Equals(questId, DefaultQuests.HafiyaId, StringComparison.Ordinal) &&
                !IsSpecialistArrived(ArrivalsPool.HealerId))
            {
                _currentQuestOffer = null;
                return null;
            }

            var run = _quests.Get(questId) ?? _quests.Start(questId);
            // Завершений квест більше не пропонується: раніше його підсумковий
            // етап лишався «пропозицією» з кнопкою «Підтвердити», і кожне
            // натискання знову «ухвалювало рішення» (власник, 25.09.2026).
            // Вказівник на пропозицію теж скидаємо: екрани перезапитують квест
            // прямо перед ResolveQuestChoice, і без скидання «Підтвердити»
            // завершеного квесту розв'язало б ЧУЖУ пропозицію, що лишилась від
            // попереднього запиту.
            if (run == null || !run.IsActive || run.Current == null)
            {
                _currentQuestOffer = null;
                return null;
            }

            var stage = run.Current;
            var offer = new QuestOfferView
            {
                Kind = "Quest", TopicId = run.Def.Id, QuestId = questId, Stage = run.CurrentIndex,
                StageTextKey = stage.TextKey
            };
            if (stage.Kind == QuestStageKind.Check)
            {
                offer.CheckSkillKey = stage.CheckSkill.Id;
                offer.CheckThreshold = stage.Threshold;
            }

            if (stage.Kind == QuestStageKind.Choice)
            {
                var options = new List<DecisionOptionView>();
                for (int i = 0; i < stage.Options.Count; i++)
                {
                    var opt = stage.Options[i];
                    options.Add(new DecisionOptionView { TextKey = opt.TextKey, HasCandidate = opt.IsAvailable(_flags) });
                }
                offer.Options = options;
            }
            else
            {
                offer.Options = new List<DecisionOptionView>();
            }

            _currentQuestOffer = offer;

            string offerKey = questId + "#" + run.CurrentIndex.ToString(CultureInfo.InvariantCulture);
            if (_loggedQuestOfferKeys.Add(offerKey))
            {
                LogEvent("quest.offered", Args("questId", questId, "stage", run.CurrentIndex.ToString(CultureInfo.InvariantCulture)));
            }
            return offer;
        }

        public DayReportView ResolveQuestChoice(int optionIndex)
        {
            RequireAnyState(SessionState.Morning, SessionState.Evening, SessionState.Night);
            if (_currentQuestOffer == null) throw new InvalidOperationException("Немає активної пропозиції квесту.");
            var run = _quests.Get(_currentQuestOffer.QuestId);
            if (run == null) throw new InvalidOperationException("Квест не знайдено.");
            if (!run.IsActive)
            {
                _currentQuestOffer = null;
                throw new InvalidOperationException("Квест уже завершено.");
            }

            QuestStepReport step = run.Current.Kind == QuestStageKind.Choice
                ? run.Choose(optionIndex, _flags)
                : run.ResolveCheck(_rosterView, _repeats, _processor.CurrentDay, _cfg);

            // Недоступний варіант чи чужий тип етапу: квест не рушив — нічого не
            // застосовуємо й не пишемо в стрічку.
            if (!step.Accepted)
            {
                _currentQuestOffer = null;
                return _lastDayReport;
            }

            ApplyConsequence(step.Consequence);

            // R8 (seamsForD1): завершення квесту — віха Готовності, що трапляється
            // ПОЗА конвеєром дня (ReadinessTickStep бачить лише будівлю/указ/страх),
            // тож зараховує її сюди безпосередньо D1, як і задокументовано.
            if (step.Terminal && step.Succeeded)
                _readiness.Add(_cfg.Readiness.QuestDoneAmount);

            LogEvent("quest.choice.resolved",
                Args("questId", run.Def.Id, "stage", step.StageId, "band", step.Band.ToString()));

            if (string.Equals(run.Def.Id, DefaultQuests.HafiyaId, StringComparison.Ordinal) &&
                string.Equals(step.StageId, "grass", StringComparison.Ordinal))
            {
                LogEvent(step.Band >= OutcomeBand.Good ? DefaultQuests.Stage2FoundKey : DefaultQuests.Stage2MissingKey);
            }

            // Підсумок квесту — один раз у стрічку: після завершення квест
            // більше не показується пропозицією, і текст підсумкового етапу
            // інакше гравець не побачив би взагалі.
            if (step.Terminal && run.Current != null && !string.IsNullOrEmpty(run.Current.TextKey))
                LogEvent(run.Current.TextKey);

            // Поправка №7.8: коли цей квест — зміст квестової глави арки
            // (BeginArcChapterQuest зареєстрував companionId у мапі нижче),
            // термінал квесту (успіх ЧИ невдача — глава ПРОГРАНА, а не лише
            // виграна) завершує главу арки тим самим шляхом, що й сценова
            // глава на фініші сцени (CompleteArcChapterFor).
            if (step.Terminal)
            {
                string arcCompanionId;
                if (_activeArcChapterQuestCompanion.TryGetValue(run.Def.Id, out arcCompanionId))
                {
                    _activeArcChapterQuestCompanion.Remove(run.Def.Id);
                    CompleteArcChapterFor(arcCompanionId);
                }
            }

            _currentQuestOffer = null;
            return _lastDayReport;
        }

        public BuildPreview PreviewBuildPlan(string companionId, BuildPlan plan)
        {
            RequireMorningOrFreePlay();
            var c = _worldRoster.Get(companionId);
            return BuildPlanner.Preview(c, plan, _points.Get(companionId), null, _cfg);
        }

        public BuildPlanStatus CommitBuildPlan(string companionId, BuildPlan plan, bool confirmedIrreversible)
        {
            RequireMorningOrFreePlay();
            var c = _worldRoster.Get(companionId);
            int cost = plan?.PointCost ?? 0;
            var status = BuildPlanner.Commit(c, plan, _points.Get(companionId), null, _cfg, confirmedIrreversible);
            if (status == BuildPlanStatus.Ok)
            {
                _points.Spend(companionId, cost);
                LogEvent("progression.build_committed", Args("companionId", companionId, "pointCost", cost.ToString(CultureInfo.InvariantCulture)));
            }
            return status;
        }

        public bool Equip(string companionId, string itemInstanceId, EquipSlot slot)
        {
            RequireMorningOrFreePlay();
            var c = _worldRoster.Get(companionId);
            if (c == null) return false;
            var item = _inventory.FindAnywhere(_worldRoster.All, itemInstanceId);
            if (item == null || item.Slot != slot) return false;

            // Предмет міг бути надітий на когось іншого — знімаємо з нього (перенадіти).
            if (!_inventory.Remove(item))
                foreach (var other in _worldRoster.All)
                    if (other.Equipment.Find(item.InstanceId) == item) { other.Equipment.Unequip(item.Slot); break; }
            // Поправка №19.2: дворучна зброя знімає щит, щит — дворучну зброю; усе витіснене — у сташ.
            foreach (var displaced in c.Equipment.EquipDisplacing(item))
                _inventory.Add(displaced);

            LogEvent("equip.changed", Args("companionId", companionId, "itemId", item.Definition.Id, "slot", slot.ToString()));
            return true;
        }

        /// <summary>
        /// Кузня Збройні (Поправка №19.2): кує базовий (Common) предмет з <see cref="DefaultItems.ForgeCatalog"/>
        /// у сташ за золото й сировину (<see cref="Balance.ItemBalance.ForgeCost"/>, ПЛЕЙСХОЛДЕР). Лише вранці
        /// чи у вільній грі й лише з відкритою Збройнею. Детерміновано: та сама команда — той самий предмет.
        /// </summary>
        public ForgeResult ForgeItem(string itemDefinitionId)
        {
            RequireMorningOrFreePlay();
            ItemDefinition def = null;
            foreach (var d in DefaultItems.ForgeCatalog())
                if (string.Equals(d.Id, itemDefinitionId, StringComparison.Ordinal)) { def = d; break; }
            if (def == null) return ForgeResult.UnknownItem;
            if (!_works.Has(DefaultBuildingsType.Armory)) return ForgeResult.ArmoryClosed;
            int gold, craft;
            _cfg.Items.ForgeCost(def.Slot, out gold, out craft);
            var ledger = _state.Resources;
            if (!ledger.CanAfford(ResourceType.Gold, gold) || !ledger.CanAfford(ResourceType.CraftComponent, craft))
                return ForgeResult.CannotAfford;
            ledger.TrySpend(ResourceType.Gold, gold);
            ledger.TrySpend(ResourceType.CraftComponent, craft);
            var item = new ItemInstance(def, Rarity.Common);
            _inventory.Add(item);
            LogEvent("forge.made", Args("itemId", def.Id, "instanceId", item.InstanceId,
                "gold", gold.ToString(CultureInfo.InvariantCulture), "craft", craft.ToString(CultureInfo.InvariantCulture)));
            return ForgeResult.Success;
        }

        /// <summary>Каталог кузні для екрана Збройні: що можна викувати і за скільки.</summary>
        public IReadOnlyList<ForgeOfferView> GetForgeOffers()
        {
            var list = new List<ForgeOfferView>();
            bool open = _works.Has(DefaultBuildingsType.Armory);
            foreach (var d in DefaultItems.ForgeCatalog())
            {
                int gold, craft;
                _cfg.Items.ForgeCost(d.Slot, out gold, out craft);
                list.Add(new ForgeOfferView
                {
                    ItemId = d.Id, Slot = d.Slot, VisualKey = d.VisualKey, TwoHanded = d.TwoHanded,
                    GoldCost = gold, CraftCost = craft, ArmoryOpen = open,
                    Affordable = open && _state.Resources.CanAfford(ResourceType.Gold, gold)
                                      && _state.Resources.CanAfford(ResourceType.CraftComponent, craft)
                });
            }
            return list;
        }

        public bool Unequip(string companionId, EquipSlot slot)
        {
            RequireMorningOrFreePlay();
            var c = _worldRoster.Get(companionId);
            if (c == null) return false;
            var item = c.Equipment.Unequip(slot);
            if (item == null) return false;

            _inventory.Add(item);
            LogEvent("equip.changed", Args("companionId", companionId, "itemId", item.Definition.Id, "slot", slot.ToString()));
            return true;
        }

        public CraftResult CraftUpgrade(string itemInstanceId)
        {
            RequireMorningOrFreePlay();
            var item = _inventory.FindAnywhere(_worldRoster.All, itemInstanceId);
            bool workshopOpen = _works.Has(DefaultBuildingsType.Workshop);
            var result = CraftSystem.TryUpgrade(item, _state.Resources, workshopOpen, _cfg.Items);
            if (result == CraftResult.Success)
                LogEvent("craft.upgraded", Args("itemId", item.Definition.Id));
            return result;
        }

        public string SaveState(int slot)
        {
            // Morning і FreePlay — той самий хаб (ConfirmMorning уже трактує їх
            // однаково); після фіналу доби 5 гра ЗАВЖДИ у FreePlay, тож заборона
            // збереження лише в Morning робила б ручний сейв неможливим до кінця
            // прогону (R13: 3 слоти + автосейв щоранку — обидва мають працювати
            // й у FreePlay).
            if (State != SessionState.Morning && State != SessionState.FreePlay)
                throw new InvalidOperationException("Збереження лише в Morning/FreePlay (R13).");
            string blob = ComposeSave();
            _slots[slot] = blob;
            LogEvent("game.saved", Args("slot", slot.ToString(CultureInfo.InvariantCulture)));
            return blob;
        }

        public bool LoadState(int slot)
        {
            string blob;
            if (!_slots.TryGetValue(slot, out blob) || string.IsNullOrEmpty(blob)) return false;
            ApplySave(blob);
            LogEvent("game.loaded", Args("slot", slot.ToString(CultureInfo.InvariantCulture)));
            return true;
        }

        /// <summary>
        /// Відновлення з готового зліпка (той самий рядок, що повертає
        /// <see cref="SaveState"/>), а не з внутрішнього слота цього екземпляра.
        /// Потрібне для акцептансу D1 "SaveState → LoadState У НОВОМУ
        /// екземплярі GameSession": слоти (<see cref="_slots"/>) — пам'ять
        /// одного інстансу, а фактичний файл на диску — робота викликача
        /// (Alpha.Play/Unity SaveLoadScreen), який і передає рядок сюди.
        /// </summary>
        public void RestoreFromBlob(string blob)
        {
            if (string.IsNullOrEmpty(blob)) throw new ArgumentException("Порожній зліпок збереження", nameof(blob));
            ApplySave(blob);
            LogEvent("game.loaded", Args("slot", "external"));
        }

        private static List<EquipSlotView> BuildSlotViews(Equipment eq)
        {
            var list = new List<EquipSlotView>();
            var weapon = eq.Get(EquipSlot.Weapon);
            bool twoHanded = weapon != null && weapon.Definition.TwoHanded;
            foreach (EquipSlot slot in Enum.GetValues(typeof(EquipSlot)))
            {
                var item = eq.Get(slot);
                list.Add(new EquipSlotView
                {
                    Slot = slot, ItemId = item?.Definition.Id, InstanceId = item?.InstanceId,
                    VisualKey = item?.Definition.VisualKey,
                    BlockedByTwoHanded = slot == EquipSlot.Offhand && twoHanded
                });
            }
            return list;
        }

        /// <summary>Лише для тестів: Common-екземпляр предмета в сташ (обхід лута й кузні).</summary>
        internal ItemInstance DebugGrantItem(string definitionId)
        {
            foreach (var d in DefaultItems.AllDefinitions())
                if (d.Id == definitionId)
                {
                    var item = d.IsNamed ? ItemInstance.NamedFrom(d) : new ItemInstance(d, Rarity.Common);
                    _inventory.Add(item);
                    return item;
                }
            return null;
        }

        /// <summary>Лише для тестів: будівля готова, ресурси в гаманець.</summary>
        internal void DebugMarkBuilt(string buildingId) => _works.DebugMarkBuilt(buildingId);
        internal void DebugAddResource(ResourceType resource, int amount) => _state.Resources.Add(resource, amount);

        /// <summary>Лише для тестів: виставити сюжетний прапор (і одразу застосувати числові читачі, що чекають на нього).</summary>
        internal void DebugSetFlag(string flag)
        {
            _flags.Set(flag);
            ApplyHafiyaGrassBonusToSickChildIfNeeded();
            ApplyBargainedTimeBonusIfNeeded();
            ApplyMyroslavaHintBonusIfNeeded();
        }

        /// <summary>Лише для тестів: чи виставлено сюжетний прапор.</summary>
        internal bool DebugHasFlag(string flag) => _flags.Get(flag);

        /// <summary>Лише для тестів: чи завершено арку напарника (<see cref="IsArcCompleted"/>).</summary>
        internal bool DebugIsArcCompleted(string companionId) => IsArcCompleted(companionId);

        /// <summary>Лише для тестів: примусово завершити арку напарника — гейти лояльності обходяться, стан той самий, що після обох глав.</summary>
        internal void DebugCompleteArc(string companionId)
        {
            var run = FindArcRun(companionId);
            if (run == null) return;
            int guard = 0;
            while (!run.IsFinished && guard++ < 10) run.CompleteChapter();
        }

        private string CaptureGear()
        {
            var parts = new List<string>();
            foreach (var c in _worldRoster.All)
            {
                string items = c.Equipment.CaptureState();
                if (items.Length > 0) parts.Add(c.Id + ">" + items);
            }
            return string.Join("|", parts.ToArray());
        }

        /// <summary>Старий сейв без "gear=" — надітого немає (як і було в тих збірках).</summary>
        private void RestoreGear(string blob)
        {
            if (string.IsNullOrEmpty(blob)) return;
            foreach (var entry in blob.Split('|'))
            {
                int gt = entry.IndexOf('>');
                if (gt <= 0) continue;
                var c = _worldRoster.Get(entry.Substring(0, gt));
                if (c != null) c.Equipment.RestoreState(entry.Substring(gt + 1));
            }
        }

        /// <summary>Вирізає з заголовка поле з довжина-префіксом ("key=N^значення"); null — поля немає.</summary>
        private static string CutLengthPrefixed(ref string headPart, string key)
        {
            int idx = headPart.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return null;
            int afterKey = idx + key.Length;
            int caret = headPart.IndexOf('^', afterKey);
            int len = ParseInt(headPart.Substring(afterKey, caret - afterKey));
            string value = headPart.Substring(caret + 1, len);
            headPart = headPart.Substring(0, idx) + headPart.Substring(caret + 1 + len);
            return value;
        }

        /// <summary>Сташ поселення для UI/тестів (§4.1 Equip/CraftUpgrade адресують предмети звідси за InstanceId).</summary>
        public IReadOnlyList<ItemInstance> GetStash() => _inventory.Items;

        /// <summary>
        /// Тестовий гачок IVT (<c>AssemblyInfo.cs</c>: <c>Game.Tests.EditMode</c>
        /// бачить <c>internal</c>-члени <c>Game.Core.*</c> — той самий підхід,
        /// що вже застосований до <c>Companion.Loyalty</c>): перевірити, що
        /// ціна кривавого шляху вузла 1 (PlaystyleBlood/CausedFear, D1b) реально
        /// дійшла до прихованих шкал, БЕЗ появи жодного числа в публічному View
        /// (R17) — жоден офіційний контракт §4.2 цього не показує навмисно.
        /// </summary>
        internal int DebugTensionValue => _processor?.Tension?.Value ?? 0;

        /// <summary>Гачок налагодження (той самий прийом, що <see cref="DebugTensionValue"/>): розклад Таверни, Поправка №15.1.</summary>
        internal (string SpecialistId, int DueDay) DebugTavernSchedule => (_pendingTavernSpecialistId, _pendingTavernDueDay);

        /// <summary>Поріг тихого шляху інциденту в поточному світі — щоб тест бачив, чи дійшов числовий наслідок вибору (торг, трава Гафії) до самого порогу.</summary>
        internal int DebugQuietThreshold(string incidentId, string sourceId)
        {
            if (_processor?.Incidents == null) return -1;
            foreach (var def in _processor.Incidents.All)
                if (def.Id == incidentId && def.SourceId == sourceId) return def.QuietPathThreshold;
            return -1;
        }

        /// <summary>IVT-гачок: з яким темпом Напруги побудовано світ (прапорець і фактичний поріг накопичувача кризи).</summary>
        internal bool DebugTensionPace => _tensionPace;
        internal bool DebugIronman => _ironman;

        internal int DebugCrisisThreshold =>
            _processor?.Pulse != null && _processor.Pulse.Tracks.TryGetValue("crisis", out var track) ? track.Threshold : 0;
        internal bool DebugCommunityIsAfraid => _processor != null && _processor.Fear != null && _processor.Fear.IsAfraid(_processor.CurrentDay);

        /// <summary>Той самий гачок для scout_horn "forewarn_boost" (D1b) — заповнення накопичувача Тугара (0..1+), приховане від View.</summary>
        internal double DebugTuharPulseFill
        {
            get
            {
                Game.Core.World.PressureTrack track;
                if (_processor?.Pulse != null && _processor.Pulse.Tracks.TryGetValue(OpeningContent.TuharSourceId, out track))
                    return track.Fill;
                return 0.0;
            }
        }

        /// <summary>Ступінь передвісника Тугара, яку гравець УЖЕ почув (0..3) — той самий гачок, приховане від View.</summary>
        internal int DebugTuharDeliveredLevel => _processor?.Pulse?.DeliveredLevelOf(OpeningContent.TuharSourceId) ?? 0;

        public SessionState ConfirmMorning()
        {
            if (State != SessionState.Morning && State != SessionState.FreePlay)
                throw new InvalidOperationException("ConfirmMorning лише з Morning/FreePlay.");
            State = SessionState.Day;
            return State;
        }

        // =====================================================================
        // Day / Decision
        // =====================================================================

        public DayReportView AdvanceDay()
        {
            RequireState(SessionState.Day);
            ClearDayLog();
            _lastPhase = DayPhase.Day;

            // Нерозв'язані після бою — відпущені (Поправка №14.2; панель результату попереджає).
            ReleasePendingSurrenders();

            TickExpeditionReturnIfAny();

            // Час рухає ЛИШЕ SettlementCycle (CLAUDE.md §«Час іде тільки
            // через SettlementCycle») — саме він переносить прапор голоду
            // (BaseState.WasHungryLastCycle → DayProcessor.IsHungry) перед
            // кроком конвеєра. Прямий викл _processor.Advance() лишав голод
            // непідключеним: HungerStep читав би завжди застаріле значення.
            var report = _cycle.AdvanceDay(DayPhase.Day);
            LogEvent("day.advanced", Args("day", report.Day.ToString(CultureInfo.InvariantCulture), "phase", report.Phase.ToString()));
            TranslateReport(report);
            ApplyCycleReport(_world.Cycle.Production.LastReport);

            // Поправка №15.1: два з трьох шляхів пізнього приєднання —
            // переселенці ради й Таверна — прив'язані до КАЛЕНДАРНОЇ доби, не
            // до події вилазки, тож звіряються тут же, одразу після того, як
            // SettlementCycle.AdvanceDay довів добу до кінця денного
            // конвеєра (CityWorksStep уже відпрацював — і TakeSettlers,
            // і AdvanceConstruction Таверни, якщо сьогодні їхній день).
            ProcessSettlersArrivalIfAny();
            ProcessTavernArrivalSchedule();
            _lastDayReport = BuildDayReportView(report);
            SettleAfterDayReport(report);
            TickPrisoners(report.Day);
            TickCaptives(report.Day);

            if (_processor.CurrentDay == 5 && _crisis.Phase == CrisisPhase.Idle)
            {
                _crisis.Warn(_processor.CurrentDay);
                LogEvent("crisis.test.warn");
                _crisis.OpenReactionWindow();
                LogEvent("crisis.test.window");
            }

            return _lastDayReport;
        }

        public DayReportView ResolveIncident(IncidentPath path)
        {
            RequireState(SessionState.Decision);
            if (_currentPending == null) throw new InvalidOperationException("Немає рішення, що чекає.");
            string incidentId = _currentPending.IncidentId;

            if (string.Equals(incidentId, "pass_vanguard", StringComparison.Ordinal) && path == IncidentPath.Bloody)
            {
                // Р5/D1b (seamsForD1): бій замінює саму ПЕРЕВІРКУ вузла 1
                // (IncidentResolver.Resolve тут не викликається взагалі — його
                // замінює справжній тактичний бій), але дві ціни кривавого
                // шляху, що не залежать від того, ЯК саме розв'язано кривавий
                // вибір (перевіркою чи боєм), лишаються тими самими, що й для
                // будь-якого іншого інциденту з HasBloodyPath (IncidentResolver.
                // ApplyBloodCost): драйвер PlaystyleBlood закритого переліку
                // (інваріант 5) і пам'ять страху громади (CausedFear — криваве
                // рішення само лякає, незалежно від виходу бою). Рана виконавцю
                // тут НЕ дублюється: справжні втрати вже рахує ApplyBattleCasualties
                // після резолву бою (RosterAdapter.Wound/Kill), а не абстрактний
                // "казуальний" удар check.ActorId, якого при бою просто немає.
                //
                // Фікс-ревью D1b: раніше тут стояв _processor.QueueExternal(...),
                // а це — мостик R6, який за контрактом TensionTickStep дренує
                // заявку лише на ПЕРШОМУ тіку НАСТУПНОЇ фази, тоді як
                // IncidentResolver.ApplyBloodCost для будь-якого іншого
                // кривавого інциденту застосовує PlaystyleBlood СИНХРОННО, в
                // тому самому виклику, що й Напругу полоси виходу. Викликаємо
                // TensionState.Apply напряму (internal, той самий Game.Core,
                // що й IncidentResolver) — так ціна крові лягає атомарно з
                // рештою наслідків цього ж вузла, а не фазою пізніше.
                _processor.Tension.Apply(TensionDriver.PlaystyleBlood, _cfg.Tension.BloodDeltaPerNode,
                    "blood:" + incidentId);
                _processor.Fear?.Remember(_processor.CurrentDay, _cfg.Checks);

                var setup = BuildBattleSetup(new[] { ProtagonistId, "maksym", "myroslava" },
                    Node1BloodyEnemyIds, 8, 8);
                RequestBattle(setup, SuspendReason.PassVanguardBloody, SessionState.Decision);
                return null;
            }

            var report = _processor.ResolvePending(path);

            if (string.Equals(incidentId, "pass_vanguard", StringComparison.Ordinal))
            {
                var band = FindBand(report, incidentId);
                CompletePassVanguard(report, band, wasBloody: false);
                return _lastDayReport;
            }

            if (path == IncidentPath.Bloody)
                LogLoyaltyChanges(LoyaltyRules.OnBloodyChoice(_worldRoster, _cfg));

            LogEvent("decision.resolved", Args("path", path.ToString(), "band", FindBand(report, incidentId).ToString(), "incidentId", incidentId));

            // Замір темпу Напруги (Поправка №7, 24.09.2026): справжня природна
            // криза (crisis_riot) розв'язується ЛИШЕ цим шляхом — рядок вище
            // логує "decision.resolved" сам (з "path", якого TranslateReport не
            // знає), тож MarkIncidentsAlreadyTranslated нижче ховає щойно
            // розв'язаний інцидент від циклу TranslateReport. Це навмисно рятує
            // від дубля ЛОГУ — але той самий цикл TranslateReport є ЄДИНИМ
            // місцем, що кличе HandleCompanionDeath для CrisisBite.KillCompanion
            // (companion.died/roster.rippled). Заховавши інцидент, ми ховали і
            // ПОБІЧНИЙ ЕФЕКТ, не лише лог: жертва кризи гинула у RosterAdapter
            // (Kill вже відпрацював усередині IncidentResolver), а гір ніколи не
            // повертався і "companion.died" ніколи не логувався — знайдено
            // саме через те, що природна криза раніше НІКОЛИ не спрацьовувала
            // за жодного прогону (замір 24.09.2026: 25 діб — Напруга ~95/1000).
            // Повторюємо тут той самий виклик, що робить TranslateReport.
            if (report?.Incidents != null && report.Incidents.Count > 0)
            {
                var justResolved = report.Incidents[report.Incidents.Count - 1];
                if (justResolved.Bite == CrisisBite.KillCompanion && !string.IsNullOrEmpty(justResolved.AffectedActorId))
                    HandleCompanionDeath(justResolved.AffectedActorId);
            }

            // Щойно розв'язаний інцидент уже залогований рядком вище (з "path",
            // якого TranslateReport не знає) — позначаємо його перекладеним,
            // інакше цикл TranslateReport нижче залогує "decision.resolved" для
            // нього вдруге (аудит дубля DayLog).
            MarkIncidentsAlreadyTranslated(report);
            TranslateReport(report);
            _lastDayReport = BuildDayReportView(report);
            SettleAfterDayReport(report);
            return _lastDayReport;
        }

        // =====================================================================
        // Evening / Night
        // =====================================================================

        public SessionState ConfirmEvening()
        {
            RequireState(SessionState.Evening);
            State = SessionState.Night;
            return State;
        }

        public void SetPatrol(bool patrol)
        {
            // Задокументовано в пакеті (§4.1 "Evening -> Night: SetPatrol,
            // AdvanceNight") як команда вечора — саме тут гравець вирішує
            // нічну варту, ДО того, як AdvanceNight (Night-лише) прочитає
            // IsPatrolling.
            RequireState(SessionState.Evening);
            _processor.IsPatrolling = patrol;
        }

        public DayReportView ReactToCrisis(CrisisReaction action)
        {
            // _crisis з'являється лише в NewGame — команда до нього (як і решта
            // команд файлу) повинна впасти чистим InvalidOperationException, а
            // не NullReferenceException. Стан НЕ обмежуємо однією фазою: вікно
            // реакції відкривається під час AdvanceDay (§3.5) і лишається
            // відкритим крізь Evening аж до Bite() у AdvanceNight — легальний
            // виклик з обох станів (ReactToCrisis_Bloody тест кличе його ще в
            // Evening, до ConfirmEvening).
            if (_crisis == null)
                throw new InvalidOperationException("Немає активної сесії (NewGame не викликано).");
            if (_crisis.Phase != CrisisPhase.WindowOpen)
                throw new InvalidOperationException("Вікно реакції на кризу закрите.");

            switch (action)
            {
                case CrisisReaction.SpendGold:
                    // B3 (M1.11): пом'якшення КУПУЄТЬСЯ — без золота (ціна в BalanceConfig,
                    // ПЛЕЙСХОЛДЕР) воно не спрацьовує; раніше результат TrySpend ігнорувався.
                    if (!_state.Resources.TrySpend(ResourceType.Gold, _cfg.CrisisMitigationGold))
                    {
                        LogEvent("crisis.test.no_gold");
                        return _lastDayReport;
                    }
                    break;
                case CrisisReaction.SendDefender:
                    // Символічна дія: відрядити людину з поста на ніч. Числового
                    // ефекту, відмінного від SpendGold, у тестовій збірці не
                    // заведено — обидва шляхи однаково пом'якшують укус нижче.
                    break;
            }

            if (action != CrisisReaction.Ignore)
            {
                _crisis.Mitigate();
                LogEvent("crisis.test.mitigated");
            }

            return _lastDayReport;
        }

        /// <summary>
        /// Полірування (ціль 6 «Рішення», owner: "тактичний бій: N ворогів"):
        /// прев'ю кількості ворогів кривавого шляху фіналу ДО кліку — той самий
        /// план (<see cref="BuildFinalePlan"/>), що й реальний бій; викликати
        /// двічі безпечно (жодної мутації стану).
        /// </summary>
        public int GetFinaleEnemyCount() => BuildFinalePlan().EnemyDefinitionIds.Count;

        /// <summary>
        /// План кривавого штурму — ЄДИНЕ джерело для прев'ю
        /// (<see cref="GetFinaleEnemyCount"/>, <see cref="GetFinaleView"/>) і для
        /// реального бою (<see cref="ResolveFinale(IncidentPath, IReadOnlyList{string})"/>).
        /// Раніше прев'ю рахувало спрощений план — без стану зради, «відпустити» й
        /// ради Захара, — і число ворогів могло не збігтися з боєм (B5). Чисте читання.
        /// </summary>
        private AssaultPlan BuildFinalePlan()
        {
            // Поправка №7.8: реальний стан зради — статус Antagonist
            // (конфронтація/TickDefectionWatch уже виконали
            // Defection.Defect) АБО, якщо конфронтація ще не встигла
            // (напр. гравець прискорив фінал вільною грою до доби 3), той
            // самий сюжетний прапор, що й раніше — але НЕ якщо
            // конфронтація вже розв'язалась «довірою» (тоді прапор лишився
            // висіти з вузла 1, а зради не сталося).
            bool myroslavaConfirmedAntagonist = _worldRoster?.Get("myroslava")?.Status == CompanionStatus.Antagonist;
            // M1.2: довіра — або виграна нічна розмова, або завершена арка Мирослави (вона вже не зрадить).
            bool myroslavaTrusted = _flags.Get(CompanionScenes.MyroslavaConfrontedTrustFlag) || IsArcCompleted("myroslava");
            bool myroslavaDefected = myroslavaConfirmedAntagonist ||
                (_flags.Get(PassVanguardOutcome.DefectorSeededFlag) && !myroslavaTrusted);

            var plan = Finale.BuildAssault(_readiness.Band, myroslavaDefected ? "myroslava" : null, _cfg.Readiness);

            // Поправка №7.8: «відпустити» на нічній розмові (замість
            // звинувачення) пом'якшує кривавий фінал — на одного
            // рядового ворога менше, той самий прийом, що м'якший фінал
            // взагалі не буває "чистим" (§7.15), лише тут менша ціна за
            // менш жорстоке рішення гравця, а не за полосу Готовності.
            if (_flags.Get(CompanionScenes.MyroslavaConfrontedReleaseFlag) &&
                plan.EnemyDefinitionIds.Count > 1)
                plan.EnemyDefinitionIds.RemoveAt(plan.EnemyDefinitionIds.Count - 2); // не боса (він останній)

            // B2 (M1.4): рада Захара «тримати перевал» (доба 5, увечері) лишає прапор саме
            // для цього читача — ще один рядовий ворог менше. Прапор ставився, але його
            // не читав ніхто, тож вибір у раді не мав наслідку.
            if (_flags.Get(CompanionScenes.ZakharPreparedAssaultFlag) &&
                plan.EnemyDefinitionIds.Count > 1)
                plan.EnemyDefinitionIds.RemoveAt(plan.EnemyDefinitionIds.Count - 2); // не боса

            return plan;
        }

        /// <summary>
        /// Чому цього напарника не можна обрати в загін фіналу (Поправка №17.2:
        /// «Гравець обирає отряд, але не може обрати тех хто на ролі назначений в
        /// місті»). Порядок причин — від найвагомішої для гравця: пост у місті
        /// називаємо раніше за поранення.
        /// </summary>
        private static FinaleBlock FinaleBlockOf(Companion c)
        {
            if (c.IsCaptive) return FinaleBlock.Captive;
            if (c.Status == CompanionStatus.OnMission) return FinaleBlock.Away;
            if (c.IsAssigned) return FinaleBlock.OnPost;
            if (c.IsInjured) return FinaleBlock.Injured;
            return FinaleBlock.None;
        }

        /// <summary>
        /// Склад кривавого фіналу: протагоніст іде завжди, решту гравець обирає з
        /// кандидатів (<see cref="FinaleView.Candidates"/>) до <see cref="BalanceConfig.FinalePartyMax"/>.
        /// Чисте читання.
        /// </summary>
        public FinaleView GetFinaleView()
        {
            var candidates = new List<FinaleCandidateView>();
            if (_worldRoster != null)
            {
                foreach (var c in _worldRoster.All)
                {
                    if (string.Equals(c.Id, ProtagonistId, StringComparison.Ordinal)) continue;
                    // Мертвих, не прибулих і тих, що вже на боці ворога, гравець не бачить зовсім.
                    if (c.IsDead || c.Status == CompanionStatus.Antagonist || c.Status == CompanionStatus.NotArrived) continue;
                    var block = FinaleBlockOf(c);
                    candidates.Add(new FinaleCandidateView
                    {
                        CompanionId = c.Id,
                        Selectable = block == FinaleBlock.None,
                        Block = block,
                        PostSlotId = block == FinaleBlock.OnPost ? c.AssignedSlotId : null
                    });
                }
            }
            return new FinaleView
            {
                ProtagonistId = ProtagonistId,
                Candidates = candidates,
                PartyMax = _cfg.FinalePartyMax,
                EnemyCount = BuildFinalePlan().EnemyDefinitionIds.Count
            };
        }

        /// <summary>
        /// Фінал зі складом «за замовчуванням» (боти, тести): протагоніст і всі, кого
        /// можна обрати, до ліміту. Людина через екран викликає
        /// <see cref="ResolveFinale(IncidentPath, IReadOnlyList{string})"/> зі своїм складом.
        /// </summary>
        public DayReportView ResolveFinale(IncidentPath path) => ResolveFinale(path, null);

        /// <summary>
        /// Розв'язати фінал доби 5. <paramref name="allyIds"/> — напарники, яких гравець
        /// обрав у загін кривавого шляху (протагоніст іде завжди й тут не вказується);
        /// <c>null</c> — склад за замовчуванням (усі, кого можна обрати, у порядку ростера
        /// до ліміту). Порожній список — протагоніст іде сам. Обрати того, хто на посту,
        /// пораненого, у вилазці чи в полоні, не можна. Тихий шлях складу не потребує:
        /// перевірка йде за найкращими присутніми.
        /// </summary>
        public DayReportView ResolveFinale(IncidentPath path, IReadOnlyList<string> allyIds)
        {
            RequireState(SessionState.Night);
            if (_processor.CurrentDay != 5)
                throw new InvalidOperationException("Фінал лише на добу 5, вночі.");
            if (_finaleResolved)
                throw new InvalidOperationException("Фінал уже розв'язано.");

            if (path == IncidentPath.Bloody)
            {
                // Склад перевіряємо ДО будь-якої мутації стану.
                var view = GetFinaleView();
                var allies = new List<string>();
                if (allyIds == null)
                {
                    foreach (var candidate in view.Candidates)
                        if (candidate.Selectable && allies.Count < view.PartyMax - 1) allies.Add(candidate.CompanionId);
                }
                else
                {
                    if (allyIds.Count > view.PartyMax - 1)
                        throw new InvalidOperationException(
                            "Склад фіналу завеликий: разом із протагоністом не більше " + view.PartyMax + ".");
                    foreach (var id in allyIds)
                    {
                        FinaleCandidateView found = null;
                        foreach (var candidate in view.Candidates)
                            if (string.Equals(candidate.CompanionId, id, StringComparison.Ordinal)) { found = candidate; break; }
                        if (found == null)
                            throw new InvalidOperationException("Склад фіналу: «" + id + "» не може йти (невідомий або протагоніст).");
                        if (!found.Selectable)
                            throw new InvalidOperationException("Склад фіналу: «" + id + "» не може йти (" + found.Block + ").");
                        if (allies.Contains(id))
                            throw new InvalidOperationException("Склад фіналу: «" + id + "» вказано двічі.");
                        allies.Add(id);
                    }
                }

                var plan = BuildFinalePlan();
                var partyIds = new List<string> { ProtagonistId };
                partyIds.AddRange(allies);

                var setup = BuildBattleSetup(partyIds, plan.EnemyDefinitionIds, 10, 10,
                    plan.DefectorCompanionId);
                RequestBattle(setup, SuspendReason.FinaleAssault, SessionState.Night);
                return null;
            }

            var dam = Finale.BuildDam(_readiness.Band, _cfg.Readiness);
            var tactics = Finale.BuildDamTactics(_readiness.Band, _cfg.Readiness);
            var outDam = CheckResolver.Resolve(dam, _rosterView, _repeats, _processor.CurrentDay, _cfg);
            var outTactics = CheckResolver.Resolve(tactics, _rosterView, _repeats, _processor.CurrentDay, _cfg);

            // Комбінація двох перевірок тихого шляху фіналу — рішення інтегратора
            // (В6 лишив це відкритим питанням, §9): гірша з двох ("найслабша
            // ланка"), а не середнє чи "обидві мають пройти" — узгоджено з тим,
            // що фінал не має чистої перемоги за жодною полосою (§7.15).
            var band = outDam.Band < outTactics.Band ? outDam.Band : outTactics.Band;
            CompleteFinale(band, wasBloody: false);
            return _lastDayReport;
        }

        public DayReportView AdvanceNight()
        {
            RequireState(SessionState.Night);

            // Блокер-фікс ревью (R8/§7.15): фінал доби 5 — РЕАЛЬНИЙ, а не
            // прев'ю, і не може бути мовчки пропущений. AdvanceNight() рухає
            // конвеєр ночі до кінця доби (→ Summary на добу 5) НЕЗАЛЕЖНО від
            // того, чи розв'язано ResolveFinale — жодного власного гейту тут
            // не було. Гейтимо саме тут (єдина точка, звідки ніч доби 5 може
            // "проскочити" у Summary без фіналу): ResolveFinale має піти
            // ПЕРШИМ, інакше — явний виняток замість тихого порожнього
            // SummaryView.FinaleOutcomeKey.
            if (_processor.CurrentDay == 5 && !_finaleResolved)
                throw new InvalidOperationException(
                    "Доба 5, ніч: спершу ResolveFinale(path) — фінал не можна пропустити " +
                    "мовчки (R8, §7.15 «жодна полоса не чиста перемога»).");

            ClearDayLog();
            _lastPhase = DayPhase.Night;
            if (_processor.IsPatrolling) _patrolledANight = true;

            // Той самий SettlementCycle, що й у AdvanceDay (див. коментар там):
            // повторний SyncHunger перед ніччю нешкідливий — HungerStep сам
            // ігнорує ніч (ctx.IsNight), а WasHungryLastCycle між фазами
            // однієї доби не змінюється (AdvanceCycle іде лише вдень).
            var report = _cycle.AdvanceDay(DayPhase.Night);
            LogEvent("day.advanced", Args("day", report.Day.ToString(CultureInfo.InvariantCulture), "phase", report.Phase.ToString()));
            TranslateReport(report);

            if (_crisis.Phase == CrisisPhase.WindowOpen)
            {
                _crisis.Bite();
                if (_crisis.WasMitigated)
                {
                    LogEvent("crisis.test.mitigated");
                }
                else
                {
                    LogEvent("crisis.test.unmitigated");
                    ApplyCrisisBite();
                }
            }

            _lastDayReport = BuildDayReportView(report);
            SettleAfterDayReport(report);
            return _lastDayReport;
        }

        private void ApplyCrisisBite()
        {
            var candidates = _rosterAdapter?.KillableActorIds;
            if (candidates != null && candidates.Count > 0)
                LogScarIfGranted(candidates[0], _rosterAdapter.WoundReporting(candidates[0], 20.0, WoundTier.Light));
        }

        /// <summary>Major-фікс ревью (§2 №23): "scar.granted" — з усіх трьох реальних точок ранення в GameSession.</summary>
        private void LogScarIfGranted(string companionId, Game.Core.Characters.Scars.ScarDefinition granted)
        {
            if (granted == null) return;
            LogEvent("scar.granted", Args("companionId", companionId, "scarId", granted.Id));
        }

        // =====================================================================
        // Dungeon
        // =====================================================================

        /// <summary>
        /// Пакет D2 (шов для бот-прогону): стан поточного данжу без виклику
        /// команди, що його змінює. PushDeeper/ResolveDungeonRoom/ResolveDungeonEvent
        /// повертають DungeonView лише як РЕЗУЛЬТАТ дії, а `DepartExpedition`
        /// (Delve) — ні (повертає DispatchResult, штовхає в кімнату 1 мовчки) —
        /// без цього гетера бот, щойно увійшовши в підвішений стан Dungeon, не
        /// має звідки дізнатись, яка кімната перед ним і яка вона за типом
        /// (Combat/Treasure/Event), щоб узагалі вибрати команду. Null поза
        /// Dungeon — як і решта GetXView() гетерів файлу.
        /// </summary>
        public DungeonView GetDungeonView() => BuildDungeonView();

        public DungeonView PushDeeper()
        {
            RequireState(SessionState.Dungeon);
            var room = _dungeon.Push();
            LogEvent("dungeon.push", Args("room", room?.Id, "depth", _dungeon.Depth.ToString(CultureInfo.InvariantCulture)));
            return BuildDungeonView();
        }

        public DungeonView ResolveDungeonRoom(IncidentPath path)
        {
            RequireState(SessionState.Dungeon);

            // DungeonRun.ResolveRoom сам вирішує, чи веде цей шлях у бій
            // (res.NeedsBattle: завжди для Bloody на Combat-кімнаті, і для
            // Quiet, якщо BestQuietBand впала на Worst) — саме тому виклик
            // один для обох шляхів: побудова RoomResolution і переведення
            // AwaitingBattle у true (яке потім читає ReportCombat) відбувається
          // ЛИШЕ всередині цього виклику, окремої гілки для Bloody нема.
            var room = _dungeon.CurrentRoom;
            var party = ResolveActors(_dungeon.PartyIds);
            var res = _dungeon.ResolveRoom(path, party);
            ApplyDungeonResolution(res);

            if (res.NeedsBattle)
            {
                // Старт бою — від того, як загін дійшов до бою (Поправка №14.1).
                var opening = ToBattleOpening(_dungeon.PendingBattle?.Start ?? DungeonBattleStart.FirstStrike);
                var setup = BuildRoomBattleSetup(room, _dungeon.PartyIds, opening);
                RequestBattle(setup, SuspendReason.DungeonCombatRoom, SessionState.Dungeon);
                return null;
            }

            return BuildDungeonView();
        }

        public DungeonView ResolveDungeonEvent(int optionIndex)
        {
            RequireState(SessionState.Dungeon);
            var res = _dungeon.ResolveEvent(optionIndex);
            ApplyDungeonResolution(res);
            return BuildDungeonView();
        }

        public DungeonView ExtractDungeon()
        {
            RequireState(SessionState.Dungeon);
            var rep = _dungeon.Extract(_state);
            if (rep.ThreatBandChanged) LogEvent("dungeon.threat_band_changed", Args("band", _dungeon.ThreatBand.ToString()));
            LogEvent("dungeon.extract", Args("build", rep.BuildComponent.ToString(CultureInfo.InvariantCulture),
                "craft", rep.CraftComponent.ToString(CultureInfo.InvariantCulture),
                "gold", rep.Gold.ToString(CultureInfo.InvariantCulture)));

            // Поправка №15.1: вилазка "відбулась" — загін дійсно повернувся
            // з цієї точки, до того, як _dungeon обнулиться нижче.
            TryBringSpecialistFromExpedition(_dungeon.SiteId);

            ExpeditionResult discarded;
            _party.Return(_state, out discarded);
            _dungeon = null;
            State = SessionState.Morning;
            return null;
        }

        public DungeonView AbandonDungeon()
        {
            RequireState(SessionState.Dungeon);
            var rep = _dungeon.Abandon();
            if (rep.ThreatBandChanged) LogEvent("dungeon.threat_band_changed", Args("band", _dungeon.ThreatBand.ToString()));
            LogEvent("dungeon.depart", Args("depth", rep.DepthReached.ToString(CultureInfo.InvariantCulture)));

            // Поправка №15.1: покинутий данж — загін теж повернувся ЗВІДТИ,
            // вилазка відбулась (не лише успішна екстракція).
            TryBringSpecialistFromExpedition(_dungeon.SiteId);

            ExpeditionResult discarded;
            _party.Return(_state, out discarded);
            _dungeon = null;
            State = SessionState.Morning;
            return null;
        }

        private void ApplyDungeonResolution(RoomResolution res)
        {
            if (res.ThreatBandChanged) LogEvent("dungeon.threat_band_changed", Args("band", _dungeon.ThreatBand.ToString()));
            if (res.Bypassed) LogEvent("dungeon.room.bypassed", Args("room", res.RoomId));
            if (res.Wiped) LogEvent("dungeon.wiped", Args("room", res.RoomId));
            if (res.GrantedItemIds != null)
                foreach (var id in res.GrantedItemIds)
                {
                    GrantNamedItemById(id);
                    LogEvent("loot.dropped", Args("itemId", id, "named", "1"));
                }

            var consequence = res.Consequence;
            if (consequence == null) return;

            if (consequence.CausedFear) _processor.Fear?.Remember(_processor.CurrentDay, _cfg.Checks);
            foreach (var kv in consequence.FactionDeltas) ApplyFactionDelta(kv.Key, kv.Value);
            foreach (var flag in consequence.FlagsToSet) _flags.Set(flag);
        }

        /// <summary>
        /// Фіх-ревью (Фаза F, знайдено тур-автоплеєм): <c>DungeonRun.CurrentRoom</c>
        /// (визначення) — це просто <c>_rooms[_roomIndex]</c>, тож лишається
        /// НЕ-null і ПІСЛЯ того, як кімнату вже розв'язано (<c>CurrentCleared
        /// == true</c>) — індекс рухає лише явний <c>Push()</c>. Але
        /// <see cref="DungeonScreen"/> (і будь-який інший читач View-шару)
        /// вирішує "малювати кімнату чи Push/Extract/Abandon" рівно за
        /// <c>view.CurrentRoom != null</c> (§DungeonScreen.cs, той самий
        /// прийом, що вже й <c>AutoplayGameDriver</c>) — без цієї умови гравець
        /// (чи бот), що затримався на вкладці після розв'язку кімнати, бачив
        /// би ті самі кнопки "тихо/криваво" ЗНОВУ й отримував
        /// InvalidOperationException "кімната вже пройдена" на кожен клік.
        /// </summary>
        private DungeonView BuildDungeonView()
        {
            if (_dungeon == null) return null;
            return new DungeonView
            {
                Depth = _dungeon.Depth,
                ThreatBand = _dungeon.ThreatBand.ToString(),
                RoomsCleared = _dungeon.RoomsCleared,
                UnbankedGold = _dungeon.UnbankedGold,
                UnbankedBuildComponent = _dungeon.UnbankedBuildComponent,
                UnbankedCraftComponent = _dungeon.UnbankedCraftComponent,
                CurrentRoom = _dungeon.CurrentCleared ? null : BuildDungeonRoomView(_dungeon.CurrentRoom),
                Outcome = _dungeon.Outcome.ToString(),
                AwaitingBattle = _dungeon.AwaitingBattle,
                PartyIds = _dungeon.PartyIds
            };
        }

        /// <summary>
        /// <paramref name="partyIdsOverride"/> — фіксує ПАРТІЮ ДЛЯ КАНДИДАТА
        /// тихого обходу: null (дефолт) означає "жива партія поточного
        /// прогону данжу" (<c>_dungeon.PartyIds</c>); PreviewExpedition
        /// передає МАЙБУТНЮ партію (companionIds параметра), бо викликає цей
        /// метод ДО DepartExpedition, коли <c>_dungeon</c> ще null —
        /// фікс-ревью (блокер, знайдено тур-автоплеєм): без override тут
        /// падав NullReferenceException на КОЖЕН прев'ю вилазки-данжу.
        /// </summary>
        private DungeonRoomView BuildDungeonRoomView(DungeonRoomDefinition room, IReadOnlyList<string> partyIdsOverride = null)
        {
            if (room == null) return null;

            var view = new DungeonRoomView
            {
                Id = room.Id,
                DisplayName = room.DisplayNameKey,
                Type = room.Kind == DungeonRoomKind.Cache ? "Treasure" : room.Kind.ToString(),
                // Ціль 6 «Рішення»: "тактичний бій: N ворогів" у самому тексті
                // варіанту криваво, не лише поріг тихого обходу поруч.
                EnemyCount = room.EnemyIds?.Count ?? 0
            };

            if (room.Kind == DungeonRoomKind.Combat && room.QuietChecks.Count > 0)
            {
                var req = room.QuietChecks[0];
                view.HasQuietBypass = true;
                view.QuietSkillKey = req.Skill.Id;
                view.QuietThreshold = _dungeon != null ? _dungeon.EffectiveQuietThreshold(req) : req.Threshold;

                // Ціль 6 «Рішення» (owner: "the quiet candidate"): найкращий
                // член ПАРТІЇ (не всього ростеру — інші лишились вдома) для
                // цього скіла/підходу, та сама формула (ISettlementActor.
                // GetCheckValue), що резолвить сам обхід.
                var partyIds = partyIdsOverride ?? _dungeon?.PartyIds;
                var party = ResolveActors(partyIds);
                string bestId = null;
                int bestValue = int.MinValue;
                foreach (var actor in party)
                {
                    int v = actor.GetCheckValue(req.Skill, req.Approach);
                    if (v > bestValue) { bestValue = v; bestId = actor.Id; }
                }
                view.QuietBestActorId = bestId;
                view.QuietHasCandidate = bestId != null;
            }

            // Прогноз старту бою (Поправка №14.1) — лише для поточної кімнати живого
            // прогону: той самий розрахунок, що застосує DungeonRun.ResolveRoom.
            if (room.Kind == DungeonRoomKind.Combat && _dungeon != null && partyIdsOverride == null
                && ReferenceEquals(room, _dungeon.CurrentRoom))
            {
                view.BloodyOpening = ToBattleOpening(_dungeon.PreviewBloodyStart(ResolveActors(_dungeon.PartyIds))).ToString();
                if (room.QuietChecks.Count > 0)
                    view.QuietFailOpening = ToBattleOpening(_dungeon.PreviewQuietFailStart()).ToString();
                // Розмова перед боєм (docs/ABILITIES.md §4.6): пороги й ціна — до кліку.
                view.Parley = new List<ParleyView> { ParleyViewOf("peace"), ParleyViewOf("surrender"), ParleyViewOf("bribe") };
            }

            if (room.Kind == DungeonRoomKind.Event)
            {
                var keys = new List<string>();
                foreach (var opt in room.EventOptions) keys.Add(opt.LabelKey);
                view.EventOptionKeys = keys;
            }

            return view;
        }

        // =====================================================================
        // Battle
        // =====================================================================

        public CombatActionResult CombatMove(GridPos dest)
        {
            RequireBattle();
            int before = _battle.Attacks.Count;
            var r = _battle.Move(dest);
            LogNewAttacks(before);
            AfterCombatAction();
            return r;
        }

        public CombatActionResult CombatAttack(string targetId, bool useStrike = false)
        {
            RequireBattle();
            int before = _battle.Attacks.Count;
            var r = _battle.Attack(targetId, useStrike);
            LogNewAttacks(before);
            AfterCombatAction();
            return r;
        }

        public CombatActionResult CombatUseAbility(string abilityId, string targetUnitId = null, GridPos? targetTile = null)
        {
            RequireBattle();
            int before = _battle.Attacks.Count;
            var r = _battle.UseAbility(abilityId, targetUnitId, targetTile);
            LogNewAttacks(before);
            AfterCombatAction();
            return r;
        }

        public CombatActionResult CombatEnterOverwatch(GridPos aim) { RequireBattle(); var r = _battle.Overwatch(aim); AfterCombatAction(); return r; }
        public CombatActionResult CombatEndTurn() { RequireBattle(); var r = _battle.EndTurn(); AfterCombatAction(); return r; }

        /// <summary>
        /// D1b (§2 рядок 30): перекладає нові записи <see cref="CombatState.Attacks"/>
        /// (з'явилися за виклик команди Battle вище цього рядка, включно з
        /// <see cref="CombatAutoResolve"/>) у стрічку подій — єдине джерело
        /// доказу бою поза <see cref="BattleView.Log"/> (журнал бою для
        /// гравця, не для тесту покриття). Кожен запис класифікується за
        /// <see cref="AttackRecord.IsReaction"/> — прапором, який ставить сам
        /// <c>CombatState</c> у точці народження запису (ReactToMovement),
        /// а не позиційним порівнянням AttackerId із тим, хто мав хід на
        /// момент виклику команди.
        ///
        /// Фікс-ревью D1b (блокер): стара евристика ("AttackerId != actingUnitId
        /// → дозор") працювала лише для одиночних команд гравця (один
        /// "actingUnitId" на виклик) і мовчки ламалась на CombatAutoResolve,
        /// де CombatAi веде ОБИДВІ сторони через багато юнітів за один виклик —
        /// єдиного "хто зараз ходить ззовні" просто нема. IsReaction — реальний
        /// сигнал з Game.Core.Combat, тому той самий метод коректно працює і
        /// для одиночної команди, і для цілого автобою.
        /// </summary>
        private void LogNewAttacks(int before)
        {
            var attacks = _battle?.Attacks;
            if (attacks == null) return;

            for (int i = before; i < attacks.Count; i++)
            {
                var rec = attacks[i];
                var args = Args("attackerId", rec.AttackerId, "targetId", rec.TargetId,
                    "chance", rec.Chance.ToString(CultureInfo.InvariantCulture),
                    "damage", rec.Damage.ToString(CultureInfo.InvariantCulture));

                if (rec.IsReaction)
                {
                    LogEvent("combat.overwatch.triggered", args);
                    continue;
                }

                switch (rec.Outcome)
                {
                    case AttackOutcome.Miss: LogEvent("combat.attack.miss", args); break;
                    case AttackOutcome.Graze: LogEvent("combat.attack.graze", args); break;
                    case AttackOutcome.Hit: LogEvent("combat.attack.hit", args); break;
                    case AttackOutcome.Crit: LogEvent("combat.attack.crit", args); break;
                }
            }
        }

        /// <summary>
        /// Фікс-ревью D1b (блокер): раніше кликав лише <see cref="CombatAi.AutoResolve"/>
        /// і одразу <see cref="AfterCombatAction"/> — жоден AttackRecord, зіграний
        /// ІІ за ОБИДВІ сторони на шляху до результату, не діставався DayLog, хоча
        /// саме автобій (не покрокова команда) — панівний спосіб розв'язки бою в
        /// "one-game" проходженні (кривавий вузол 1, бойові кімнати данжу,
        /// фінальний штурм). Тепер знімок Attacks.Count і LogNewAttacks працюють
        /// так само, як і в одиночних командах вище — просто на весь бій одразу.
        /// Прапор <see cref="_battleAutoResolvedThisCall"/> дає OnBattleResolved
        /// знати, що САМЕ ЦЕЙ виклик довів бій до кінця (для вибору combat.
        /// autoresolved / combat.battle.resolved — фикс-ревью, мажор).
        /// </summary>
        public void CombatAutoResolve()
        {
            RequireBattle();
            int before = _battle.Attacks.Count;
            CombatAi.AutoResolve(_battle);
            LogNewAttacks(before);
            _battleAutoResolvedThisCall = true;
            AfterCombatAction();
        }

        /// <summary>
        /// Дебаг §6.1 №32 (24.09.2026): ОДНА дія того самого "розумного" ІІ
        /// (<see cref="CombatAi.TryAct"/>), що веде АвтоБій — цільовий
        /// скоринг, зближення клінч-ролей способністю (Ривок), лікування,
        /// статус-здібності, — а не наївне "йди до найближчого і бий" у
        /// <c>BotRunner.ExecuteCombatAction</c>, написане лише для того, щоб
        /// водій ботів МІГ вести бій покроково для тестів на UI/View. Коли
        /// <c>TryAct</c> каже "нічого більше" (false) — завершує хід сама,
        /// точнісінько як внутрішній цикл <see cref="CombatAi.TakeTurn"/>.
        /// Side-агностичний, як і решта Combat*-команд.
        ///
        /// ОДНА дія, не весь хід (<see cref="CombatAi.TakeTurn"/> цілком) —
        /// юніт із запасом AP на 2+ атаки за хід (саме випадок Бурунди, 10 AP /
        /// 4 за удар) інакше відпрацював би весь хід за один виклик, і
        /// зовнішній спостерігач побачив би BattleView лише ПІСЛЯ обох ударів,
        /// коли ціль уже могла загинути від другого — а короткий статус
        /// (наложений першим ударом, ціль ще жива) залишився б непоміченим.
        /// Саме ця різниця й ламала №32: наївний водій програвав фінальний
        /// штурм за 9 атак, перш ніж Бурунда встигав дійти до контакту.
        /// </summary>
        public void CombatAiStepOneAction()
        {
            RequireBattle();
            int before = _battle.Attacks.Count;
            var unit = _battle.Current;
            bool didSomething = unit != null && unit.IsActive && _battle.Outcome == CombatOutcome.Ongoing
                && CombatAi.TryAct(_battle, unit);
            if (!didSomething) _battle.EndTurn();
            LogNewAttacks(before);
            AfterCombatAction();
        }

        // ===================== Бій v2: прев'ю до кліку (docs/COMBAT_V2.md §7.1) =====================
        // Сигнатури заморожені контрактом; реалізацію пише частина «ядро».

        /// <summary>
        /// Прев'ю атаки зброєю (abilityId == null) або озброєною здібністю по
        /// цілі — БЕЗ мутацій: Result дзеркалить ту саму валідацію, що
        /// CombatState.Attack/UseAbility зроблять по факту (ОД, дальність,
        /// лінія видимості, відкат, валідність цілі, стан бою), а
        /// Chance/Terms/DamageExpected рахуються ТИМ САМИМ
        /// HitChanceCalculator.Decompose і DamageResolver, якими котиться
        /// справжній ролл — показане гравцю число фізично не може розійтись
        /// із фактом (§7.1 COMBAT_V2.md).
        /// </summary>
        public AttackPreviewView PreviewAttack(string attackerId, string targetId, string abilityId = null)
        {
            if (_battle == null) return null;
            var unit = _battle.GetUnit(attackerId);
            var target = _battle.GetUnit(targetId);

            bool unitReady = unit != null && _battle.Current == unit && unit.IsActive
                             && _battle.Outcome == CombatOutcome.Ongoing;
            if (!unitReady)
                return new AttackPreviewView
                {
                    AttackerId = attackerId, TargetId = targetId, AbilityId = abilityId,
                    Result = CombatActionResult.InvalidAction.ToString()
                };

            var view = abilityId == null
                ? PreviewWeaponAttack(unit, target)
                : PreviewAbilityAttack(unit, target, abilityId);
            view.AttackerId = attackerId;
            view.TargetId = targetId;
            view.AbilityId = abilityId;
            return view;
        }

        private AttackPreviewView PreviewWeaponAttack(CombatUnit unit, CombatUnit target)
        {
            var w = unit.Weapon;
            if (w == null) return new AttackPreviewView { Result = CombatActionResult.InvalidAction.ToString() };

            if (target == null || target.Side == unit.Side || target.LifeState != UnitLifeState.Active)
                return new AttackPreviewView
                {
                    Result = CombatActionResult.InvalidTarget.ToString(), ApCost = w.ApCost, HasAttackRoll = true
                };

            int distance = GridPos.Chebyshev(unit.Pos, target.Pos);
            string result;
            if (unit.Ap < w.ApCost) result = CombatActionResult.NotEnoughAp.ToString();
            else if (w.IsMelee && distance > w.OptimalRange) result = CombatActionResult.OutOfRange.ToString();
            else if (!w.IsMelee && !LineOfSight.HasLine(_battle.Map, unit.Pos, target.Pos)) result = CombatActionResult.NoLineOfSight.ToString();
            else result = CombatActionResult.Success.ToString();

            return BuildAttackPreview(unit, target, w, accuracyBonus: 0, distance: distance, range: w.OptimalRange,
                result: result, apCost: w.ApCost);
        }

        /// <summary>
        /// Здібності, яким потрібна КЛІТИНКА: ціль-тайл (пастка) або перестановка
        /// юніта на тайл («Наказ пересунутися» — союзник І клітинка).
        /// </summary>
        private static bool AbilityNeedsTile(AbilityDefinition a)
        {
            if (a.Targeting == AbilityTarget.Tile) return true;
            foreach (var e in a.Effects)
                if (e != null && e.Kind == AbilityEffectKind.RepositionTarget) return true;
            return false;
        }

        private AttackPreviewView PreviewAbilityAttack(CombatUnit unit, CombatUnit target, string abilityId)
        {
            var ability = unit.FindAbility(abilityId);
            if (ability == null) return new AttackPreviewView { Result = CombatActionResult.InvalidAction.ToString() };

            // PreviewAttack не бере targetTile — здібності, що цілять у тайл
            // (пастка тощо), цим методом не прев'юються (лише ціль-юніт).
            if (ability.Targeting == AbilityTarget.Tile)
                return new AttackPreviewView { Result = CombatActionResult.InvalidAction.ToString(), ApCost = ability.ApCost };

            CombatUnit resolvedTarget;
            switch (ability.Targeting)
            {
                case AbilityTarget.Self:
                    resolvedTarget = unit;
                    break;
                case AbilityTarget.Ally:
                    if (target == null || target == unit || target.Side != unit.Side || !target.IsActive)
                        return new AttackPreviewView { Result = CombatActionResult.InvalidTarget.ToString(), ApCost = ability.ApCost };
                    resolvedTarget = target;
                    break;
                case AbilityTarget.AllyOrSelf:
                    resolvedTarget = target ?? unit;
                    if (resolvedTarget.Side != unit.Side || !resolvedTarget.IsActive)
                        return new AttackPreviewView { Result = CombatActionResult.InvalidTarget.ToString(), ApCost = ability.ApCost };
                    break;
                case AbilityTarget.Enemy:
                    if (target == null || target.Side == unit.Side || !target.IsActive)
                        return new AttackPreviewView { Result = CombatActionResult.InvalidTarget.ToString(), ApCost = ability.ApCost };
                    resolvedTarget = target;
                    break;
                default:
                    return new AttackPreviewView { Result = CombatActionResult.InvalidAction.ToString() };
            }

            bool selfTarget = resolvedTarget == unit;
            int distance = selfTarget ? 0 : GridPos.Chebyshev(unit.Pos, resolvedTarget.Pos);
            bool hasLos = selfTarget || LineOfSight.HasLine(_battle.Map, unit.Pos, resolvedTarget.Pos);

            // Перша партія (docs/ABILITIES.md): та сама перевірка, що в CombatState.UseAbility.
            var check = _battle.DescribeCheck(unit, resolvedTarget, ability);

            string result;
            if (unit.CooldownRemaining(ability.Id) > 0) result = CombatActionResult.OnCooldown.ToString();
            else if (unit.Ap < ability.ApCost) result = CombatActionResult.NotEnoughAp.ToString();
            else if (check != null && check.BlockKey != null) result = CombatActionResult.InvalidTarget.ToString();
            else if (!selfTarget && distance > ability.Range) result = CombatActionResult.OutOfRange.ToString();
            else if (!selfTarget && ability.RequiresLineOfSight && !hasLos) result = CombatActionResult.NoLineOfSight.ToString();
            else if (HasEffect(ability, AbilityEffectKind.LungeToTarget) && !_battle.HasLungeLanding(unit, resolvedTarget))
                // Та сама перевірка, що в CombatState.UseAbility: ворог оточений —
                // приземлитись нікуди (рев'ю Бою v2: прев'ю казало «можна», факт — ні).
                result = CombatActionResult.NotReachable.ToString();
            else result = CombatActionResult.Success.ToString();

            bool hasAttackRoll = !selfTarget && unit.Weapon != null && ability.WeaponAttackCount() > 0;
            var preview = hasAttackRoll
                ? BuildAttackPreview(unit, resolvedTarget, unit.Weapon, ability.PreviewAccuracyBonus(),
                    distance, ability.Range, result, apCost: ability.ApCost, shots: ability.WeaponAttackCount())
                : new AttackPreviewView
                {
                    Result = result, ApCost = ability.ApCost, Distance = distance, Range = ability.Range,
                    HasLineOfSight = hasLos, HasAttackRoll = false
                };
            if (check != null)
            {
                preview.CheckKind = check.Kind.ToString();
                preview.CheckSkill = check.SkillKey;
                preview.CheckValue = check.Value;
                preview.CheckThreshold = check.Threshold;
                preview.CheckImmune = check.Immune;
                preview.CheckPasses = check.Passes;
                preview.CheckBlockKey = check.BlockKey;
            }
            return preview;
        }

        private static bool HasEffect(AbilityDefinition a, AbilityEffectKind kind)
        {
            foreach (var e in a.Effects)
                if (e != null && e.Kind == kind) return true;
            return false;
        }

        /// <summary>Спільний хвіст прев'ю: розклад шансу + прев'ю урону — той самий HitChanceCalculator/DamageResolver, яким котиться факт.</summary>
        private AttackPreviewView BuildAttackPreview(CombatUnit unit, CombatUnit target, WeaponDefinition w,
            int accuracyBonus, int distance, int range, string result, int apCost, int shots = 1)
        {
            bool ignoreCover = w.IsMelee;
            var cover = ignoreCover ? CoverType.None : _battle.Map.CoverAgainst(target.Pos, unit.Pos);
            bool hasLos = w.IsMelee || LineOfSight.HasLine(_battle.Map, unit.Pos, target.Pos);

            var terms = HitChanceCalculator.Decompose(unit.Profile.Accuracy, unit.HasStatus(StatusType.Suppressed),
                target.Profile.Defense, cover, ignoreCover, distance, w.OptimalRange, _battle.Balance,
                target.HasStatus(StatusType.Marked), target.HasStatus(StatusType.KnockedDown), accuracyBonus,
                target.HasStatus(StatusType.Enraged));

            int chance = 0;
            var termViews = new List<ChanceTermView>(terms.Count);
            foreach (var t in terms)
            {
                chance += t.ChanceDelta;
                termViews.Add(new ChanceTermView { Key = t.Key, ChanceDelta = t.ChanceDelta });
            }

            var dmg = DamageResolver.PreviewRange(unit, target, w);
            int expected = DamageResolver.ExpectedHitDamage(unit, target, w);
            // Досьє (№14.6): опори й броню невивченого ворога ще не знаємо — число з «?».
            bool damageUncertain = IsDossierSubject(target) && DossierOf(target) != DossierLevel.Studied;

            return new AttackPreviewView
            {
                Result = result,
                HasAttackRoll = true,
                Chance = chance,
                IsPercent = _battle.IsHitRulePercent,
                Terms = termViews,
                Cover = cover.ToString(),
                CoverIgnored = ignoreCover,
                IsFlanked = !ignoreCover && cover == CoverType.None && HasAnyCover(target.Pos),
                DamageMin = dmg.Min,
                DamageMax = dmg.Max,
                DamageCrit = dmg.Crit,
                IsDamageDeterministic = !_battle.IsHitRulePercent,
                DamageExpected = expected,
                DamageUncertain = damageUncertain,
                // Правило без кубика: результат саме цих ударів відомий наперед (інваріант 8).
                PredictedShots = _battle.IsHitRulePercent ? 0 : shots,
                PredictedHits = _battle.IsHitRulePercent ? 0 : Math.Max(0, _battle.PredictHits(unit, chance, shots)),
                ApCost = apCost,
                Distance = distance,
                Range = range,
                HasLineOfSight = hasLos
            };
        }

        /// <summary>
        /// Прев'ю руху поточного юніта до тайла, БЕЗ мутацій: Result/Tiles/ApCost
        /// дзеркалять те, що реально зробить CombatMove (той самий
        /// Pathfinder.Path і той самий словник CombatState.ReachableFor), плюс
        /// тайли шляху, накриті чужим дозором (CombatState.OverwatchCovers) —
        /// «під ворожим дозором!» видно ДО кліку, не після.
        /// </summary>
        public MovePathView PreviewMovePath(GridPos dest)
        {
            if (_battle == null) return null;
            var unit = _battle.Current;
            if (unit == null || !unit.IsActive || _battle.Outcome != CombatOutcome.Ongoing)
                return new MovePathView { Result = CombatActionResult.InvalidAction.ToString() };

            var reachable = _battle.ReachableFor(unit);
            if (!reachable.TryGetValue(dest, out int cost))
                return new MovePathView { Result = CombatActionResult.NotReachable.ToString() };

            var path = Pathfinder.Path(_battle.Map, unit.Pos, dest);
            var tiles = new List<GridPosView>(path.Count);
            foreach (var p in path) tiles.Add(new GridPosView(p.X, p.Y));

            var threat = new List<GridPosView>();
            foreach (var step in path)
            {
                bool covered = false;
                foreach (var watcher in _battle.Units)
                {
                    if (watcher.Side == unit.Side || !watcher.IsOverwatching) continue;
                    if (_battle.OverwatchCovers(watcher, step)) { covered = true; break; }
                }
                if (covered) threat.Add(new GridPosView(step.X, step.Y));
            }

            return new MovePathView
            {
                Result = CombatActionResult.Success.ToString(),
                Tiles = tiles,
                ApCost = cost,
                OverwatchThreatTiles = threat
            };
        }

        /// <summary>
        /// Тайли, які накрив би дозор поточного юніта з прицілом у
        /// <paramref name="aim"/> — БЕЗ фактичного входу в дозор (ніяких
        /// мутацій, AP не чіпається). Та сама геометрія й лінія видимості, що
        /// в ядрі (<see cref="CombatState.PreviewOverwatchCone"/>).
        /// </summary>
        public IReadOnlyList<GridPosView> PreviewOverwatchCone(GridPos aim)
        {
            var result = new List<GridPosView>();
            if (_battle?.Current == null) return result;
            foreach (var t in _battle.PreviewOverwatchCone(_battle.Current, aim))
                result.Add(new GridPosView(t.X, t.Y));
            return result;
        }

        /// <summary>Стабілізувати зваленого союзника поруч (фасад <c>CombatState.Stabilize</c>) — той самий патерн логування/AfterCombatAction, що й решта команд Battle.</summary>
        public CombatActionResult CombatStabilize(string targetId)
        {
            RequireBattle();
            var r = _battle.Stabilize(targetId);
            AfterCombatAction();
            return r;
        }

        public int PreviewHitChance(string attackerId, string targetId)
        {
            if (_battle == null) return 0;
            var a = _battle.GetUnit(attackerId);
            var t = _battle.GetUnit(targetId);
            if (a == null || t == null) return 0;
            return _battle.HitChancePreview(a, t);
        }

        /// <summary>Показаний гравцю діапазон урону поточної зброї атакуючого по цілі — той самий принцип, що PreviewHitChance вище (з нулів, якщо бою нема/юніт не знайдено/зброї нема).</summary>
        public void PreviewDamage(string attackerId, string targetId, out int min, out int max, out int crit)
        {
            min = max = crit = 0;
            if (_battle == null) return;
            var a = _battle.GetUnit(attackerId);
            var t = _battle.GetUnit(targetId);
            if (a == null || t == null) return;
            var info = _battle.DamagePreview(a, t);
            min = info.Min;
            max = info.Max;
            crit = info.Crit;
        }

        public BattleView GetBattleView()
        {
            if (_battle == null) return null;

            // Ordinal (§7.1): 0 для унікального DisplayNameKey, інакше 1..n за
            // порядком появи в _battle.Units — два порахувати заздалегідь
            // (перший прохід — скільки юнітів на кожне ім'я), а не помічати
            // "перший з двох" post-factum, бо порядок Units фіксований при
            // збірці бою (AddUnit) і не змінюється.
            var nameCounts = new Dictionary<string, int>();
            foreach (var u in _battle.Units)
            {
                nameCounts.TryGetValue(u.Profile.DisplayName, out var n);
                nameCounts[u.Profile.DisplayName] = n + 1;
            }
            var nameSeen = new Dictionary<string, int>();

            var units = new List<BattleUnitView>();
            foreach (var u in _battle.Units)
            {
                bool fromDefector = u.Side == Side.Enemy && !string.IsNullOrEmpty(u.SourceCompanionId);
                var abilities = new List<BattleAbilityView>();
                foreach (var a in u.Abilities)
                    abilities.Add(new BattleAbilityView
                    {
                        Id = a.Id, ApCost = a.ApCost, CooldownRemaining = u.CooldownRemaining(a.Id),
                        Range = a.Range, Targeting = a.Targeting.ToString(), NeedsTargetTile = AbilityNeedsTile(a)
                    });

                int ordinal = 0;
                if (nameCounts[u.Profile.DisplayName] > 1)
                {
                    nameSeen.TryGetValue(u.Profile.DisplayName, out var seen);
                    seen++;
                    nameSeen[u.Profile.DisplayName] = seen;
                    ordinal = seen;
                }

                var w = u.Weapon;
                bool hasOverwatchAim = u.IsOverwatching && u.Overwatch != null;

                units.Add(new BattleUnitView
                {
                    Id = u.Id,
                    DisplayNameKey = u.Profile.DisplayName,
                    Pos = new GridPosView(u.Pos.X, u.Pos.Y),
                    Side = fromDefector ? "FromDefector" : u.Side.ToString(),
                    Hp = u.Hp,
                    HpMax = u.Profile.MaxHp,
                    Ap = u.Ap,
                    ApMax = u.Profile.MaxAp,
                    ApReserved = u.IsOverwatching && w != null ? w.ApCost : 0,
                    IsOverwatching = u.IsOverwatching,
                    Statuses = MapStatuses(u),
                    IsDowned = u.LifeState == UnitLifeState.Downed,
                    HitChancePreview = 0,
                    // Досьє (№14.6): прийоми ворога відкриває розвідка чи бій.
                    Abilities = IsDossierSubject(u) && DossierOf(u) != DossierLevel.Studied ? new List<BattleAbilityView>() : abilities,
                    WeaponId = w?.Id,
                    Ordinal = ordinal,
                    StatusDetails = MapStatusDetails(u),
                    HasOverwatchAim = hasOverwatchAim,
                    OverwatchAim = hasOverwatchAim ? new GridPosView(u.Overwatch.Aim.X, u.Overwatch.Aim.Y) : default,
                    AttackApCost = w?.ApCost ?? 0,
                    // WeaponDefinition несе ЄДИНЕ поняття дальності (OptimalRange) —
                    // Range/OptimalRange тут рівні (див. коментар полів у BattleView.cs).
                    WeaponRange = w?.OptimalRange ?? 0,
                    WeaponOptimalRange = w?.OptimalRange ?? 0,
                    WeaponIsMelee = w != null && w.IsMelee,
                    DownWindowRemaining = u.LifeState == UnitLifeState.Downed ? u.DownWindowRemaining : 0,
                    IsAiControlled = u.Side != Side.Player,
                    IsOutOfBattle = u.LifeState == UnitLifeState.Dead || u.LifeState == UnitLifeState.Stabilized
                                    || u.LifeState == UnitLifeState.Surrendered || u.LifeState == UnitLifeState.Fled,
                    IsFled = u.LifeState == UnitLifeState.Fled,
                    Rank = u.Side == Side.Enemy && string.IsNullOrEmpty(u.SourceCompanionId) ? u.Profile.Rank.ToString() : null,
                    // Досьє (№14.6): поки ворог не вивчений, умова здачі невідома — поле порожнє, HUD пише «?».
                    CanSurrender = u.Profile.CanSurrender && DossierOf(u) == DossierLevel.Studied,
                    SurrenderAtHpPercent = DossierOf(u) == DossierLevel.Studied ? _battle.SurrenderThresholdPercent(u) : 0,
                    Dossier = IsDossierSubject(u) ? DossierOf(u).ToString() : null,
                    BondUnitIds = _battle.BondedWith(u.Id),
                    Role = IsDossierSubject(u) ? u.Profile.Role.ToString() : null,
                    ResistNotes = IsDossierSubject(u) && DossierOf(u) == DossierLevel.Studied ? ResistNotes(u) : null,
                    IsSurrendered = u.LifeState == UnitLifeState.Surrendered
                });
            }

            var reachable = new List<GridPosView>();
            var reachableCosts = new List<int>();
            if (_battle.Current != null && _battle.Current.IsActive)
                foreach (var kv in _battle.ReachableFor(_battle.Current))
                {
                    reachable.Add(new GridPosView(kv.Key.X, kv.Key.Y));
                    reachableCosts.Add(kv.Value);
                }

            // Пастки гравця — видно на арені; ворожі — ні (поки не спрацюють).
            var traps = new List<BattleTrapView>();
            foreach (var trap in _battle.Traps)
                if (trap.OwnerSide == Side.Player)
                    traps.Add(new BattleTrapView
                    {
                        Pos = new GridPosView(trap.Pos.X, trap.Pos.Y),
                        AbilityId = trap.AbilityId,
                        TrapDamage = trap.Damage,
                        StatusOnTrigger = trap.StatusOnTrigger == StatusType.None ? null : trap.StatusOnTrigger.ToString()
                    });

            var initiative = new List<string>();
            if (_battle.TurnOrder != null)
                foreach (var u in _battle.TurnOrder) initiative.Add(u.Id);

            var cover = new List<string>();
            var coverSides = new List<string>();
            var walkable = new List<bool>();
            for (int y = 0; y < _battle.Map.Height; y++)
                for (int x = 0; x < _battle.Map.Width; x++)
                {
                    var pos = new GridPos(x, y);
                    var best = CoverType.None;
                    foreach (Direction dir in Enum.GetValues(typeof(Direction)))
                    {
                        var c = _battle.Map.GetCover(pos, dir);
                        if (c > best) best = c;
                    }
                    cover.Add(best.ToString());
                    coverSides.Add(_battle.Map.GetCover(pos, Direction.North) + "|" + _battle.Map.GetCover(pos, Direction.East) + "|"
                        + _battle.Map.GetCover(pos, Direction.South) + "|" + _battle.Map.GetCover(pos, Direction.West));
                    walkable.Add(_battle.Map.IsWalkable(pos));
                }

            // Поле бою (Поправка №14.4): об'єкти, вогонь — видно гравцю так само, як ШІ.
            var objects = new List<BattleObjectView>();
            foreach (var o in _battle.Objects)
                objects.Add(new BattleObjectView
                {
                    Pos = new GridPosView(o.Pos.X, o.Pos.Y),
                    Kind = o.Kind.ToString(),
                    IsTargetable = o.IsTargetable,
                    EffectRadius = o.Kind == MapObjectKind.PowderKeg ? _battle.Balance.Combat.ExplosionRadius
                        : o.Kind == MapObjectKind.Haystack ? _battle.Balance.Combat.FireZoneRadius : 0,
                    EffectDamage = o.Kind == MapObjectKind.PowderKeg ? _battle.Balance.Combat.ExplosionDamage : 0,
                    EffectRounds = o.Kind == MapObjectKind.Haystack ? _battle.Balance.Combat.FireZoneRounds : 0
                });
            var fires = new List<BattleFireView>();
            foreach (var f in _battle.Fires)
                fires.Add(new BattleFireView { Center = new GridPosView(f.Center.X, f.Center.Y), Radius = f.Radius, RoundsLeft = f.RoundsLeft });

            bool isAiTurn = _battle.Current != null && _battle.Current.IsActive
                            && _battle.Outcome == CombatOutcome.Ongoing && _battle.Current.Side != Side.Player;

            return new BattleView
            {
                Round = _battle.Round,
                Outcome = _battle.Outcome.ToString(),
                Grid = new BattleGridView { Width = _battle.Map.Width, Height = _battle.Map.Height, TileCover = cover, TileWalkable = walkable, TileCoverSides = coverSides },
                Objects = objects,
                Fires = fires,
                ReinforcementRound = _battle.NextReinforcementRound,
                ReinforcementCount = _battle.NextReinforcementCount,
                Units = units,
                ReachableTiles = reachable,
                Traps = traps,
                ReachableTileCosts = reachableCosts,
                IsAiTurn = isAiTurn,
                CurrentUnitId = _battle.Current != null && _battle.Current.IsActive ? _battle.Current.Id : null,
                InitiativeOrder = initiative,
                NextRoundOrder = UnitIds(_battle.NextRoundTurnOrder),
                Opening = _battle.Opening.ToString(),
                DossierScoutSurvival = _cfg.Combat.DossierScoutSurvival,
                DossierScoutWits = _cfg.Combat.DossierScoutWits,
                RetreatConsequenceKey = RetreatConsequenceKey(_resume?.Reason),
                Log = MapBattleLog(_battle.Journal),
                IsHitRulePercent = _battle.IsHitRulePercent
            };
        }

        // =====================================================================
        // Здача і полон (Поправка №14.2)
        // =====================================================================

        /// <summary>Вороги, що здалися в останніх боях і чекають рішення.</summary>
        public IReadOnlyList<SurrenderView> GetPendingSurrenders()
        {
            var list = new List<SurrenderView>();
            foreach (var e in _pendingSurrenders)
                list.Add(new SurrenderView
                {
                    UnitId = e.UnitId,
                    EnemyDefinitionId = e.EnemyDefinitionId,
                    DisplayNameKey = e.DisplayName,
                    Rank = e.Rank.ToString(),
                    CanRecruitLater = !e.NeverRecruitable
                });
            return list;
        }

        /// <summary>
        /// Доля того, хто здався (Поправка №14.2): відпустити (повернеться — №5.3,
        /// MECH-12), взяти в полон (громада годує, віче вирішує) чи добити
        /// (кров: драйвер PlaystyleBlood, страх громади, реакція напарників за
        /// цінностями). false — такого немає серед тих, хто чекає рішення.
        /// </summary>
        public bool DecideSurrender(string unitId, SurrenderFate fate)
        {
            SurrenderedEnemy e = null;
            foreach (var s in _pendingSurrenders)
                if (s.UnitId == unitId) { e = s; break; }
            if (e == null) return false;
            _pendingSurrenders.Remove(e);

            switch (fate)
            {
                case SurrenderFate.Release:
                    LogEvent("enemy.released", Args("enemyId", e.EnemyDefinitionId));
                    break;
                case SurrenderFate.Capture:
                {
                    var p = _prisoners.Take(e.EnemyDefinitionId, e.DisplayName, (int)e.Rank, e.NeverRecruitable, _processor.CurrentDay);
                    LogEvent("enemy.captured", Args("enemyId", e.EnemyDefinitionId, "prisonerId", p.Id));
                    break;
                }
                case SurrenderFate.Execute:
                    // Кров — тим самим закритим драйвером, що й кривавий шлях (інваріант 5), через
                    // чергу доби: між фазами прямий виклик драйвера робив зміну полоси німою.
                    _processor.QueueExternal(TensionDriver.PlaystyleBlood, _cfg.Tension.BloodDeltaPerNode);
                    _processor.Fear?.Remember(_processor.CurrentDay, _cfg.Checks);
                    ReactToExecution();
                    LogEvent("enemy.executed", Args("enemyId", e.EnemyDefinitionId));
                    break;
            }
            return true;
        }

        /// <summary>Напарники реагують на страту полоненого за своїми цінностями: милосердні — гірше, жорсткі — краще.</summary>
        private void ReactToExecution()
        {
            if (_worldRoster == null) return;
            foreach (var c in _worldRoster.All)
            {
                if (c.IsDead || c.Status == CompanionStatus.Antagonist) continue;
                var values = c.Traits.Values;
                bool mercy = false, ruthless = false;
                for (int i = 0; i < values.Count; i++)
                {
                    if (values[i] == DefaultValues.Mercy) mercy = true;
                    if (values[i] == DefaultValues.Ruthless) ruthless = true;
                }
                if (mercy) LogLoyaltyChange(ApplyLoyaltyDelta(c.Id, -ExecutionLoyaltyCost, "execution"));
                else if (ruthless) LogLoyaltyChange(ApplyLoyaltyDelta(c.Id, ExecutionLoyaltyGain, "execution"));
            }
        }

        /// <summary>Скільки лояльності втрачає милосердний напарник за страту і отримує жорсткий — ПЛЕЙСХОЛДЕРИ.</summary>
        private const int ExecutionLoyaltyCost = 4;
        private const int ExecutionLoyaltyGain = 2;

        private void ReleasePendingSurrenders()
        {
            foreach (var e in _pendingSurrenders)
                LogEvent("enemy.released", Args("enemyId", e.EnemyDefinitionId));
            _pendingSurrenders.Clear();
        }

        /// <summary>Полонені громади — полоси, ціна викупу, чи можна переманити просто зараз.</summary>
        public IReadOnlyList<PrisonerView> GetPrisonersView()
        {
            bool guarded = _works != null && _works.Has(DefaultBuildingsType.Watch);
            var list = new List<PrisonerView>();
            foreach (var p in _prisoners.All)
                list.Add(new PrisonerView
                {
                    Id = p.Id,
                    EnemyDefinitionId = p.EnemyDefinitionId,
                    DisplayNameKey = p.DisplayName,
                    Rank = ((EnemyRank)p.Rank).ToString(),
                    Disposition = p.DispositionBand.ToString(),
                    Restlessness = p.RestlessnessBand.ToString(),
                    CanRecruitNow = !p.NeverRecruitable && p.DispositionBand == PrisonerDisposition.Ready,
                    NeverRecruitable = p.NeverRecruitable,
                    RansomGold = _prisoners.RansomFor(p),
                    Guarded = guarded,
                    DayTaken = p.DayTaken
                });
            return list;
        }

        /// <summary>Віче: відпустити полоненого (повернеться — №5.3).</summary>
        public bool ReleasePrisoner(string prisonerId)
        {
            var p = _prisoners.Get(prisonerId);
            if (p == null) return false;
            _prisoners.Remove(prisonerId);
            LogEvent("prisoner.freed", Args("prisonerId", p.Id, "enemyId", p.EnemyDefinitionId));
            return true;
        }

        /// <summary>Віче: обміняти на викуп — золото в казну, полонений іде.</summary>
        public bool RansomPrisoner(string prisonerId)
        {
            var p = _prisoners.Get(prisonerId);
            if (p == null) return false;
            int gold = _prisoners.RansomFor(p);
            _prisoners.Remove(prisonerId);
            _state.Resources.Add(ResourceType.Gold, gold);
            LogEvent("prisoner.ransomed", Args("prisonerId", p.Id, "enemyId", p.EnemyDefinitionId, "gold", gold.ToString(CultureInfo.InvariantCulture)));
            return true;
        }

        /// <summary>
        /// Віче: переманити (власник: «полон добре бо можна собі потім його переманити»).
        /// Лише з полоси «готовий» і не для персонажів із російських першоджерел (№12.9).
        /// Полонений стає напарником у ростері.
        /// </summary>
        public bool RecruitPrisoner(string prisonerId)
        {
            var p = _prisoners.Get(prisonerId);
            if (p == null || p.NeverRecruitable || p.DispositionBand != PrisonerDisposition.Ready) return false;
            var companion = BuildRecruit(p.EnemyDefinitionId, p.DisplayName, _recruits.Count);
            if (companion == null) return false;
            _prisoners.Remove(prisonerId);
            _worldRoster.Add(companion);
            _recruits.Add(p.EnemyDefinitionId + "," + System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(p.DisplayName ?? "")).Replace('=', '~'));
            LogEvent("prisoner.recruited", Args("prisonerId", p.Id, "companionId", companion.Id, "enemyId", p.EnemyDefinitionId));
            return true;
        }

        /// <summary>
        /// Напарник із переманеного полоненого: атрибути й навички — від його ролі в
        /// бою (грубо, ПЛЕЙСХОЛДЕР до авторських карток US-3.14). Картка — «Першоджерело»
        /// оригінальне: полонений, переманений на віче.
        /// </summary>
        private Companion BuildRecruit(string enemyDefinitionId, string displayName, int seq)
        {
            var def = ResolveEnemyById(enemyDefinitionId);
            string id = "recruit_" + (enemyDefinitionId ?? "x").Replace("enemy.", "") + "_" + seq.ToString(CultureInfo.InvariantCulture);
            var arch = new CompanionArchetype(id, displayName);
            var role = def != null ? def.Role : EnemyRole.Skirmisher;
            if (role == EnemyRole.Skirmisher)
                arch.SetAttribute(AttributeType.Agility, 5).SetAttribute(AttributeType.Wits, 4)
                    .SetAttribute(AttributeType.Strength, 3).SetAttribute(AttributeType.Will, 3)
                    .SetSkill(SkillType.Ranged, 4).SetSkill(SkillType.Survival, 3);
            else
                arch.SetAttribute(AttributeType.Strength, 5).SetAttribute(AttributeType.Agility, 4)
                    .SetAttribute(AttributeType.Wits, 3).SetAttribute(AttributeType.Will, 4)
                    .SetSkill(SkillType.Melee, 4).SetSkill(SkillType.Intimidate, 2);
            var companion = arch.CreateInstance(id, _cfg);
            companion.Card = new CharacterCard(id, displayName, SourceTier.Original, "полонений, переманений на віче (Поправка №14.2)");
            return companion;
        }

        // =====================================================================
        // Розмова перед боєм (docs/ABILITIES.md §4.6, друга партія; власник: «ок»,
        // 29.09.2026). «Слово миру» (Переконання), «Скласти зброю!» (Залякування),
        // «Відкуп» (Торгівля) — одна спроба на бойову кімнату, до першого пострілу.
        // Діють лише на тих, хто за рангом №14.2 може здатися; решта — «Імунітет».
        // Поріг — сира навичка найкращого в загоні проти Волі ватажка (як дії C5).
        // Страх громади піднімає пороги Переконання й Торгівлі (FearState);
        // Залякування страхом не дешевшає — так у FearState, інакше кривавий шлях
        // окупав би сам себе (Поправка №1).
        // =====================================================================

        /// <summary>Що дасть одна форма розмови: поріг, хто відгукнеться, хто лишиться, скільки золота.</summary>
        internal sealed class ParleyPlan
        {
            public string Form, SkillKey, BlockKey;
            public int Value, Threshold, GoldCost;
            public bool Passes;
            public readonly List<EnemyDefinition> Leaving = new List<EnemyDefinition>();
            public readonly List<string> Remaining = new List<string>();
            public readonly List<string> AllEnemies = new List<string>();
        }

        private ParleyPlan BuildParley(string form)
        {
            var room = _dungeon?.CurrentRoom;
            if (room == null || room.Kind != DungeonRoomKind.Combat || _dungeon.CurrentCleared || _dungeon.AwaitingBattle)
                return new ParleyPlan { Form = form, BlockKey = "no_room" };

            Func<SkillType, int> best = skill =>
            {
                int v = 0;
                foreach (var id in _dungeon.PartyIds)
                {
                    var comp = _worldRoster?.Get(id);
                    if (comp != null && !comp.IsDead) v = Math.Max(v, comp.Skill(skill));
                }
                return v;
            };
            int fear = _processor.Fear?.PenaltyOn(_processor.CurrentDay, _cfg.Checks) ?? 0;
            return PlanParley(form, room.EnemyIds, _dungeon.PartyIds?.Count ?? 0, best, fear,
                _state.Resources.Get(ResourceType.Gold), _bribeRefusals, _cfg.Combat);
        }

        /// <summary>
        /// Чиста арифметика форми розмови (детерміновано, інваріант 8): ватажок — найстарший
        /// за рангом; відгукуються ті, хто може здатися (для відкупу — ще й жадібні);
        /// поріг — Воля ватажка + надбавка (відкуп — жадібність + надбавка); страх громади
        /// дорожчить Переконання й Торгівлю; ультиматум дешевшає, якщо загін чисельніший.
        /// </summary>
        internal static ParleyPlan PlanParley(string form, IReadOnlyList<string> enemyIds, int partySize,
            Func<SkillType, int> bestSkill, int fearPenalty, int gold, IReadOnlyDictionary<string, int> bribeRefusals,
            CombatBalance c)
        {
            var plan = new ParleyPlan { Form = form };
            EnemyDefinition leader = null;
            int known = 0;
            foreach (var id in enemyIds)
            {
                plan.AllEnemies.Add(id);
                var def = ResolveEnemyById(id);
                if (def == null) continue;
                known++;
                if (leader == null || def.Rank > leader.Rank) leader = def;
            }
            int resolve = leader != null ? Math.Max(0, leader.Resolve) : 0;

            SkillType skill;
            switch (form)
            {
                case "peace":
                    skill = SkillType.Persuade;
                    plan.Threshold = resolve + c.PeaceOverResolve + fearPenalty;
                    break;
                case "surrender":
                    skill = SkillType.Intimidate;
                    plan.Threshold = resolve + c.UltimatumOverResolve - (partySize > known ? 1 : 0);
                    break;
                case "bribe":
                    skill = SkillType.Trade;
                    break;
                default:
                    plan.BlockKey = "no_room";
                    return plan;
            }
            plan.SkillKey = skill.ToString().ToLowerInvariant();

            int greed = 0;
            foreach (var id in enemyIds)
            {
                var def = ResolveEnemyById(id);
                bool yields = def != null && def.CanSurrender && def.Rank != EnemyRank.Boss;
                if (form == "bribe") yields = yields && def.Greed > 0;
                if (!yields) { plan.Remaining.Add(id); continue; }

                plan.Leaving.Add(def);
                if (form != "bribe") continue;
                greed = Math.Max(greed, def.Greed);
                int rank = Math.Min((int)def.Rank, c.BribeGoldPerRank.Length - 1);
                int refused = bribeRefusals != null && bribeRefusals.TryGetValue(def.Id, out int r) ? r : 0;
                plan.GoldCost += c.BribeGoldPerRank[rank] * (100 + refused * c.BribeRefusalMarkupPercent) / 100;
            }
            if (form == "bribe") plan.Threshold = greed + c.BribeOverGreed + fearPenalty;

            plan.Value = bestSkill != null ? bestSkill(skill) : 0;
            plan.Passes = plan.Value >= plan.Threshold;

            if (plan.Leaving.Count == 0) plan.BlockKey = form == "bribe" ? "not_for_sale" : "immune";
            else if (form == "bribe" && gold < plan.GoldCost) plan.BlockKey = "poor";
            return plan;
        }

        private ParleyView ParleyViewOf(string form)
        {
            var p = BuildParley(form);
            return new ParleyView
            {
                Form = form, SkillKey = p.SkillKey, ParleyValue = p.Value, ParleyThreshold = p.Threshold,
                Passes = p.Passes, BlockKey = p.BlockKey, GoldCost = p.GoldCost,
                LeavingCount = p.Leaving.Count, RemainingCount = p.Remaining.Count
            };
        }

        /// <summary>
        /// Розмова перед боєм у поточній бойовій кімнаті: "peace" | "surrender" | "bribe".
        /// Вдалось — ті, хто може здатися, відходять / здаються в полон / беруть гроші; з
        /// рештою — бій (або кімната пройдена без луту, якщо нікого не лишилось).
        /// Не вдалось — бій з усіма: без засідки; після відкинутого ультиматуму ворог
        /// у раунді 1 влучніший; після відмови від відкупу ціна повтору зростає.
        /// Повертає вид данжу; null — почався бій.
        /// </summary>
        public DungeonView ResolveDungeonParley(string form)
        {
            RequireState(SessionState.Dungeon);
            var plan = BuildParley(form);
            if (plan.BlockKey != null) return BuildDungeonView();

            var room = _dungeon.CurrentRoom;
            LogEvent("dungeon.parley." + form + (plan.Passes ? ".success" : ".fail"),
                Args("threshold", plan.Threshold.ToString(CultureInfo.InvariantCulture), "value", plan.Value.ToString(CultureInfo.InvariantCulture)));

            List<string> fighters = plan.AllEnemies;
            var start = DungeonBattleStart.Encounter;
            if (plan.Passes)
            {
                fighters = plan.Remaining;
                if (form == "surrender")
                    foreach (var def in plan.Leaving)
                    {
                        _prisoners.Take(def.Id, def.DisplayName, (int)def.Rank, def.NeverRecruitable, _processor.CurrentDay);
                        LogEvent("enemy.captured", Args("enemyId", def.Id));
                    }
                else if (form == "bribe")
                    _state.Resources.TrySpend(ResourceType.Gold, plan.GoldCost);
            }
            else if (form == "surrender") start = DungeonBattleStart.Provoked;
            else if (form == "bribe")
                foreach (var def in plan.Leaving)
                {
                    _bribeRefusals.TryGetValue(def.Id, out int refused);
                    _bribeRefusals[def.Id] = refused + 1;
                }

            var res = _dungeon.ResolveParley(fighters.Count == 0, start);
            ApplyDungeonResolution(res);
            if (!res.NeedsBattle) return BuildDungeonView();

            var setup = BuildRoomBattleSetup(room, _dungeon.PartyIds, ToBattleOpening(start), fighters);
            RequestBattle(setup, SuspendReason.DungeonCombatRoom, SessionState.Dungeon);
            return null;
        }

        private string CaptureBribeRefusals()
        {
            var keys = new List<string>(_bribeRefusals.Keys);
            keys.Sort(StringComparer.Ordinal);
            var parts = new List<string>();
            foreach (var k in keys) parts.Add(k + ":" + _bribeRefusals[k].ToString(CultureInfo.InvariantCulture));
            return string.Join(",", parts);
        }

        private void RestoreBribeRefusals(string blob)
        {
            _bribeRefusals.Clear();
            if (string.IsNullOrEmpty(blob)) return;
            foreach (var entry in blob.Split(','))
            {
                int colon = entry.LastIndexOf(':');
                if (colon > 0 && int.TryParse(entry.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                    _bribeRefusals[entry.Substring(0, colon)] = n;
            }
        }

        // =====================================================================
        // Досьє ворога (Поправка №14.6; власник: «го»): контакт відкриває роль і
        // здоров'я; розвідка перед боєм (Виживання чи Кмітливість) або сам бій —
        // решту. Тренувальний бій — пісочниця: там усе видно й нічого не пишеться.
        // =====================================================================

        /// <summary>
        /// Зв'язки в бою (Поправка №14.8; власник — «Лишити, низький пріоритет»): кожна пара
        /// бійців загону з наявним зв'язком Kinship (<see cref="RosterBonds"/>) стає парою
        /// побратимів — поруч раз за раунд прикривають одне одного.
        /// </summary>
        private void RegisterBonds()
        {
            if (_worldRoster == null) return;
            var bonds = new RosterBonds(null);
            var squad = new List<CombatUnit>();
            foreach (var u in _battle.Units)
                if (u.Side == Side.Player && !string.IsNullOrEmpty(u.SourceCompanionId)) squad.Add(u);
            for (int i = 0; i < squad.Count; i++)
                for (int j = i + 1; j < squad.Count; j++)
                {
                    var a = _worldRoster.Get(squad[i].SourceCompanionId);
                    var b = _worldRoster.Get(squad[j].SourceCompanionId);
                    if (a != null && b != null && bonds.Between(a, b) == BondType.Kinship)
                        _battle.AddBond(squad[i].Id, squad[j].Id);
                }
        }

        /// <summary>Ворог зі звичайного визначення (не перебіжчик — того знаємо як свого).</summary>
        private static bool IsDossierSubject(CombatUnit u) =>
            u != null && u.Side == Side.Enemy && !string.IsNullOrEmpty(u.EnemyDefinitionId) && string.IsNullOrEmpty(u.SourceCompanionId);

        private DossierLevel DossierOf(CombatUnit u)
        {
            if (!IsDossierSubject(u)) return DossierLevel.Studied;
            if (_resume?.Reason == SuspendReason.TrainingSkirmish) return DossierLevel.Studied;
            return _dossier.LevelOf(u.EnemyDefinitionId);
        }

        /// <summary>Старт бою: кожного ворога побачили (контакт); розвідник у загоні — вивчив одразу.</summary>
        private void MeetEnemies()
        {
            foreach (var u in _battle.Units)
                if (IsDossierSubject(u)) _dossier.Raise(u.EnemyDefinitionId, DossierLevel.Contact);
            if (PartyScouts()) StudyEnemies(_battle, "scout");
        }

        private bool PartyScouts()
        {
            foreach (var u in _battle.Units)
            {
                if (u.Side != Side.Player || string.IsNullOrEmpty(u.SourceCompanionId)) continue;
                var c = _worldRoster?.Get(u.SourceCompanionId);
                if (c == null) continue;
                if (c.Skill(SkillType.Survival) >= _cfg.Combat.DossierScoutSurvival) return true;
                if (c.Attribute(AttributeType.Wits) >= _cfg.Combat.DossierScoutWits) return true;
            }
            return false;
        }

        private void StudyEnemies(CombatState battle, string how)
        {
            if (battle == null) return;
            foreach (var u in battle.Units)
                if (IsDossierSubject(u) && _dossier.Raise(u.EnemyDefinitionId, DossierLevel.Studied))
                    LogEvent("dossier.studied", Args("enemyId", u.EnemyDefinitionId, "how", how));
        }

        /// <summary>Опори вивченого ворога: "Fire:weak", "Ballistic:strong" — лише ті, що відрізняються від звичайного.</summary>
        private static List<string> ResistNotes(CombatUnit u)
        {
            var notes = new List<string>();
            var resists = u.Profile.Resists;
            if (resists == null) return notes;
            foreach (DamageType type in Enum.GetValues(typeof(DamageType)))
            {
                if (type == DamageType.True) continue;
                double m = resists.Multiplier(type);
                if (m > 1.0) notes.Add(type + ":weak");
                else if (m < 1.0) notes.Add(type + ":strong");
            }
            return notes;
        }

        // =====================================================================
        // Поразка → полон (Поправка №14.7; власник, 29.09.2026: «Але десь половина
        // має втекти»; протагоніст у полон — «ні не може»).
        // =====================================================================

        /// <summary>Хто втік і хто в полоні після бою + у кого — порахувати ДО обнулення бою.</summary>
        private sealed class CaptivityOutcome
        {
            public EscapeSplit Split;
            public HashSet<string> Standing;
            public string CaptorEnemyId;
            public int CaptorRank;
            public List<string> EnemyGroup;
            public string GroupId;
        }

        private CaptivityOutcome ComputeCaptivity(BattleResult result, SuspendReason reason)
        {
            if (_battle == null) return null;
            if (reason != SuspendReason.DungeonCombatRoom && reason != SuspendReason.CaptiveRaid) return null;
            bool retreat = result.Outcome == BattleOutcome.Retreat;
            if (result.Outcome != BattleOutcome.Defeat && !retreat) return null;

            var living = new List<string>();
            var standing = new HashSet<string>(StringComparer.Ordinal);
            foreach (var u in _battle.Units)
            {
                if (u.Side != Side.Player || string.IsNullOrEmpty(u.SourceCompanionId)) continue;
                if (u.LifeState == UnitLifeState.Dead) continue;
                living.Add(u.SourceCompanionId);
                if (u.LifeState == UnitLifeState.Active) standing.Add(u.SourceCompanionId);
            }

            var outcome = new CaptivityOutcome
            {
                Split = CaptivityRules.Split(living, standing, result.FallOrderCompanionIds, ProtagonistId, retreat),
                Standing = standing
            };

            // Рейд, що не вдався: нові бранці йдуть до того самого загону.
            var raided = reason == SuspendReason.CaptiveRaid && _raidGroupId != null ? _captives.Group(_raidGroupId) : null;
            if (raided != null && raided.Count > 0)
            {
                outcome.CaptorEnemyId = raided[0].CaptorEnemyId;
                outcome.CaptorRank = raided[0].CaptorRank;
                outcome.EnemyGroup = new List<string>(raided[0].EnemyGroup);
                outcome.GroupId = _raidGroupId;
                return outcome;
            }

            // Тримач — найстарший за рангом ворог, що лишився на полі; загін — ті, хто стоїть.
            var alive = new List<CombatUnit>();
            var all = new List<CombatUnit>();
            foreach (var u in _battle.Units)
            {
                if (u.Side != Side.Enemy || string.IsNullOrEmpty(u.EnemyDefinitionId) || !string.IsNullOrEmpty(u.SourceCompanionId)) continue;
                all.Add(u);
                if (u.LifeState != UnitLifeState.Dead && u.LifeState != UnitLifeState.Surrendered && u.LifeState != UnitLifeState.Fled)
                    alive.Add(u);
            }
            var group = alive.Count > 0 ? alive : all;
            CombatUnit captor = null;
            foreach (var u in group)
                if (captor == null || u.Profile.Rank > captor.Profile.Rank) captor = u;

            outcome.CaptorEnemyId = captor?.EnemyDefinitionId;
            outcome.CaptorRank = captor != null ? (int)captor.Profile.Rank : 0;
            outcome.EnemyGroup = new List<string>();
            foreach (var u in group) outcome.EnemyGroup.Add(u.EnemyDefinitionId);
            return outcome;
        }

        private void ApplyCaptivity(CaptivityOutcome outcome)
        {
            if (outcome?.Split == null) return;

            foreach (var id in outcome.Split.Escaped)
                if (!outcome.Standing.Contains(id))
                    LogEvent("companion.escaped", Args("companionId", id)); // упав, але його винесли / виповз

            if (outcome.Split.Captured.Count == 0) return;
            string groupId = outcome.GroupId ?? _captives.NewGroupId(_processor.CurrentDay);
            foreach (var id in outcome.Split.Captured)
                TakeCaptive(id, outcome.CaptorEnemyId, outcome.CaptorRank, groupId, outcome.EnemyGroup);
        }

        /// <summary>
        /// Єдина точка взяття нашої людини в полон: зняти з поста, статус «у полоні»,
        /// запис у реєстр, подія в стрічку. Протагоніста не бере ніколи («ні не може»).
        /// </summary>
        internal bool TakeCaptive(string companionId, string captorEnemyId, int captorRank, string groupId,
                                  IReadOnlyList<string> enemyGroup)
        {
            if (string.Equals(companionId, ProtagonistId, StringComparison.Ordinal)) return false;
            var c = _worldRoster?.Get(companionId);
            if (c == null || c.IsDead || c.Status == CompanionStatus.Antagonist || c.IsCaptive) return false;
            if (c.IsAssigned) _state.Unassign(c.AssignedSlotId);
            c.Status = CompanionStatus.Captive;
            _captives.Take(companionId, captorEnemyId, captorRank, groupId ?? _captives.NewGroupId(_processor.CurrentDay),
                enemyGroup, _processor.CurrentDay);
            LogEvent("companion.captured", Args("companionId", companionId, "enemyId", captorEnemyId ?? ""));
            return true;
        }

        /// <summary>
        /// Доба полону: годинник іде, лояльність тане (що довше — то швидше), зміна
        /// полоси — у стрічку (інваріант 4). Хто вже зрадив чи загинув — зі списку геть.
        /// </summary>
        private void TickCaptives(int day)
        {
            foreach (var c in new List<Captive>(_captives.All))
            {
                var comp = _worldRoster?.Get(c.CompanionId);
                if (comp == null || !comp.IsCaptive) _captives.Remove(c.CompanionId);
            }
            if (_captives.All.Count == 0) return;

            foreach (var ev in _captives.Tick(day))
                LogEvent("captivity.band." + _captives.BandOf(ev.Captive).ToString().ToLowerInvariant(),
                    Args("companionId", ev.Captive.CompanionId, "enemyId", ev.Captive.CaptorEnemyId ?? ""));

            foreach (var c in _captives.All)
                LogLoyaltyChange(ApplyLoyaltyDelta(c.CompanionId, _captives.LoyaltyDeltaFor(c), "captivity"));
        }

        /// <summary>Найкраща навичка серед присутніх у громаді (не в полі, не в полоні, не проти нас).</summary>
        private int BestPresentSkill(SkillType skill)
        {
            int best = 0;
            if (_worldRoster == null) return best;
            foreach (var c in _worldRoster.All)
            {
                if (c.IsDead || c.Status == CompanionStatus.Antagonist || c.Status == CompanionStatus.OnMission || c.IsCaptive) continue;
                best = Math.Max(best, c.Skill(skill));
            }
            return best;
        }

        /// <summary>
        /// Хто може піти в рейд: присутні й здорові (ті самі умови, що для вилазки).
        /// Склад обирає гравець — як і загін вилазки (власник: «ну перед вилазкою,
        /// це ж вже є»; «ЗВІСНО ГРАВЦЕМ»), до <see cref="BalanceConfig.ExpeditionPartyMax"/>.
        /// </summary>
        private List<string> RaidCandidateIds()
        {
            var ids = new List<string>();
            if (_worldRoster == null) return ids;
            foreach (var c in _worldRoster.All)
                if (!c.IsInjured && IsCompanionBattleReady(c.Id)) ids.Add(c.Id);
            return ids;
        }

        /// <summary>Наші в полоні — у кого, полоса годинника, ціна викупу, поріг перемовин, склад рейду.</summary>
        public IReadOnlyList<CaptiveView> GetCaptivesView()
        {
            var list = new List<CaptiveView>();
            if (_captives.All.Count == 0) return list;
            int gold = _state.Resources.Get(ResourceType.Gold);
            int trade = BestPresentSkill(SkillType.Trade);
            int persuade = BestPresentSkill(SkillType.Persuade);
            bool hub = State == SessionState.Morning || State == SessionState.FreePlay;
            var candidates = RaidCandidateIds();
            foreach (var c in _captives.All)
            {
                var def = ResolveEnemyById(c.CaptorEnemyId);
                int ransom = _captives.RansomFor(c, trade);
                int threshold = _captives.TalkThreshold(c);
                list.Add(new CaptiveView
                {
                    CompanionId = c.CompanionId,
                    CaptorEnemyId = c.CaptorEnemyId,
                    CaptorNameKey = def != null ? def.DisplayName : c.CaptorEnemyId,
                    CaptorRank = ((EnemyRank)c.CaptorRank).ToString(),
                    Band = _captives.BandOf(c).ToString(),
                    DayTaken = c.DayTaken,
                    RansomGold = ransom,
                    CanAffordRansom = hub && gold >= ransom,
                    BestPersuade = persuade,
                    TalkThreshold = threshold,
                    CanTalk = hub && persuade >= threshold,
                    RaidCandidateIds = candidates,
                    RaidPartyMax = _cfg.ExpeditionPartyMax,
                    RaidEnemyIds = new List<string>(c.EnemyGroup),
                    CanRaid = hub && candidates.Count > 0 && c.EnemyGroup.Count > 0
                });
            }
            return list;
        }

        /// <summary>Тихий порятунок №1: викуп золотом (Торгівля знижує ціну; ціна видна до кліку).</summary>
        public bool RansomCaptive(string companionId)
        {
            RequireMorningOrFreePlay();
            var c = _captives.Get(companionId);
            if (c == null) return false;
            int cost = _captives.RansomFor(c, BestPresentSkill(SkillType.Trade));
            if (!_state.Resources.TrySpend(ResourceType.Gold, cost)) return false;
            FreeCaptive(c, "ransom");
            return true;
        }

        /// <summary>Тихий порятунок №2: перемовини — Переконання ≥ порога за рангом тримача (інваріант 8).</summary>
        public bool NegotiateCaptive(string companionId)
        {
            RequireMorningOrFreePlay();
            var c = _captives.Get(companionId);
            if (c == null || BestPresentSkill(SkillType.Persuade) < _captives.TalkThreshold(c)) return false;
            FreeCaptive(c, "talk");
            return true;
        }

        /// <summary>
        /// Кривавий порятунок: рейд на загін тримача — тактичний бій, загін ходить першим
        /// (свідомо обраний кривавий шлях, №14.1). Перемога визволяє всіх бранців загону;
        /// поразка чи відступ — той самий механізм полону для загону рейду.
        /// </summary>
        public bool RaidCaptors(string companionId, IReadOnlyList<string> partyIds)
        {
            RequireMorningOrFreePlay();
            var c = _captives.Get(companionId);
            if (c == null || c.EnemyGroup.Count == 0 || partyIds == null) return false;
            var candidates = RaidCandidateIds();
            var party = new List<string>();
            foreach (var id in partyIds)
                if (candidates.Contains(id) && !party.Contains(id)) party.Add(id);
            if (party.Count == 0 || party.Count != partyIds.Count || party.Count > _cfg.ExpeditionPartyMax) return false;

            _raidGroupId = c.GroupId;
            _processor.QueueExternal(TensionDriver.PlaystyleBlood, _cfg.Tension.BloodDeltaPerNode);
            LogEvent("captivity.raid.started", Args("companionId", c.CompanionId, "enemyId", c.CaptorEnemyId ?? ""));
            var setup = BuildBattleSetup(party, c.EnemyGroup, 8, 8, opening: BattleOpening.FirstStrike);
            RequestBattle(setup, SuspendReason.CaptiveRaid, State);
            return true;
        }

        private void FinishCaptiveRaid(BattleResult result)
        {
            string groupId = _raidGroupId;
            if (result.Outcome == BattleOutcome.Victory && groupId != null)
                foreach (var c in _captives.Group(groupId)) FreeCaptive(c, "raid");
            else
                LogEvent("captivity.raid.failed", Args("outcome", result.Outcome.ToString()));
            // Поразка: ComputeCaptivity уже взяв групу; ApplyCaptivity додасть нових бранців до неї.
        }

        private void FreeCaptive(Captive c, string way)
        {
            _captives.Remove(c.CompanionId);
            var comp = _worldRoster?.Get(c.CompanionId);
            if (comp != null && comp.IsCaptive)
                comp.Status = comp.InjuryPoints > 0 ? CompanionStatus.Injured : CompanionStatus.Idle;
            LogEvent("companion.rescued." + way, Args("companionId", c.CompanionId, "enemyId", c.CaptorEnemyId ?? ""));
            LogLoyaltyChange(ApplyLoyaltyDelta(c.CompanionId, _captives.Balance.RescueLoyaltyBonus, "rescued"));
        }

        /// <summary>
        /// Доба полону: їжа (по одній на полоненого), вмовляння найкращим Переконанням
        /// серед присутніх, варта Сторожі. Зміни полос і втечі — у стрічку (інваріант 4).
        /// </summary>
        private void TickPrisoners(int day)
        {
            if (_prisoners.All.Count == 0) return;
            int need = _prisoners.All.Count;
            bool fed = _state.Resources.Get(ResourceType.Food) >= need;
            if (fed) _state.Resources.TrySpend(ResourceType.Food, need);

            int bestPersuade = BestPresentSkill(SkillType.Persuade);

            bool guarded = _works != null && _works.Has(DefaultBuildingsType.Watch);
            foreach (var ev in _prisoners.Tick(new PrisonerDayInputs(day, bestPersuade, guarded, fed)))
            {
                var p = ev.Prisoner;
                switch (ev.Kind)
                {
                    case "disposition":
                        LogEvent("prisoner.disposition." + p.DispositionBand.ToString().ToLowerInvariant(), Args("prisonerId", p.Id));
                        break;
                    case "restless":
                        LogEvent("prisoner.restless." + p.RestlessnessBand.ToString().ToLowerInvariant(), Args("prisonerId", p.Id));
                        break;
                    case "escaped":
                        LogEvent("prisoner.escaped", Args("prisonerId", p.Id, "enemyId", p.EnemyDefinitionId));
                        break;
                    case "hungry":
                        LogEvent("prisoner.hungry", Args("prisonerId", p.Id));
                        break;
                }
            }
        }

        /// <summary>Чи є в тайла хоч з одного боку укриття — для позначки «фланг» (№14.4).</summary>
        private bool HasAnyCover(GridPos pos)
        {
            foreach (Direction dir in Enum.GetValues(typeof(Direction)))
                if (_battle.Map.GetCover(pos, dir) != CoverType.None) return true;
            return false;
        }

        /// <summary>
        /// Вдарити поточним бійцем по об'єкту поля (Поправка №14.4): бочка вибухне,
        /// сіно займеться. Ціна — ОД зброї, влучання гарантоване; видно в HUD до кліку.
        /// </summary>
        public CombatActionResult CombatAttackObject(GridPos pos)
        {
            RequireBattle();
            var r = _battle.AttackObject(pos);
            AfterCombatAction();
            return r;
        }

        private static List<string> UnitIds(IReadOnlyList<CombatUnit> units)
        {
            var ids = new List<string>();
            if (units != null)
                foreach (var u in units) ids.Add(u.Id);
            return ids;
        }

        /// <summary>Що буде, якщо відступити (Поправка №14.7) — ключ тексту для підтвердження, за тим, хто просив бій.</summary>
        private static string RetreatConsequenceKey(SuspendReason? reason)
        {
            switch (reason)
            {
                case SuspendReason.DungeonCombatRoom: return "ui.battle.retreat.consequence.dungeon";
                case SuspendReason.TrainingSkirmish: return "ui.battle.retreat.consequence.training";
                case SuspendReason.CaptiveRaid: return "ui.battle.retreat.consequence.raid";
                default: return "ui.battle.retreat.consequence.lost";
            }
        }

        /// <summary>
        /// Відступ з бою (Поправка №14.7; ROADMAP B13 — раніше <c>CombatState.Retreat</c>
        /// ніхто не кликав, і з бою не було виходу, статут ANTI-10). Лише у свій
        /// хід. Наслідок — за тим, хто просив бій (<see cref="OnBattleResolved"/>):
        /// данж — вихід з данжу без незабанкованого; вузол і фінал — поле за
        /// ворогом (полоса Worst); тренування — без наслідків. Упалі лишаються на
        /// полі й отримують рани тим самим шляхом, що після будь-якого бою;
        /// полон упалих — крок C6.
        /// </summary>
        public CombatActionResult CombatRetreat()
        {
            RequireBattle();
            var current = _battle.Current;
            if (current == null || current.Side != Side.Player || !current.IsActive)
                return CombatActionResult.InvalidAction;
            var r = _battle.Retreat();
            AfterCombatAction();
            return r;
        }

        /// <summary>
        /// Журнал бою — з <see cref="CombatState.Journal"/> (ключі й аргументи),
        /// а не з внутрішнього трейсу <c>CombatState.Log</c>: той — російський
        /// діагностичний текст, його не можна показувати гравцеві (і тепер він
        /// <c>internal</c>). Аргументи Core не змінює після запису — віддаємо
        /// той самий словник без копії.
        /// </summary>
        private static List<BattleLogLineView> MapBattleLog(IReadOnlyList<CombatLogEntry> journal)
        {
            var lines = new List<BattleLogLineView>(journal.Count);
            foreach (var e in journal)
                lines.Add(new BattleLogLineView { Round = e.Round, Key = e.Key, Args = e.Args });
            return lines;
        }

        private static List<string> MapStatuses(CombatUnit u)
        {
            var list = new List<string>();
            foreach (var s in u.Statuses) list.Add(s.Type.ToString());
            return list;
        }

        /// <summary>Той самий набір, що <see cref="MapStatuses"/>, але з тривалістю і DoT (§7.1 COMBAT_V2.md, аудит-ядро #5).</summary>
        private static List<BattleStatusView> MapStatusDetails(CombatUnit u)
        {
            var list = new List<BattleStatusView>(u.Statuses.Count);
            foreach (var s in u.Statuses)
                list.Add(new BattleStatusView { Type = s.Type.ToString(), RemainingTurns = s.RemainingTurns, DotDamagePerTurn = s.DotDamagePerTurn });
            return list;
        }

        private void RequireBattle()
        {
            if (State != SessionState.Battle || _battle == null)
                throw new InvalidOperationException("Немає активного бою.");
        }

        private void AfterCombatAction() => OnBattleResolved();

        private void RequestBattle(BattleSetup setup, SuspendReason reason, SessionState returnState)
        {
            if (setup.HitRule == HitRuleKind.Percent && _roller == null)
                throw new InvalidOperationException("PercentRule потребує IDiceRoller, injected ззовні в конструктор GameSession.");

            _resume = new SuspendToken(reason, returnState);
            var roller = setup.HitRule == HitRuleKind.Percent ? _roller : null;
            _battle = CombatBattleBuilder.Build(setup, _cfg, ResolvePlayerUnit, ResolveEnemyById, roller, DefaultCombatContent.AbilityCatalog());
            State = SessionState.Battle;
            LogEvent("combat.battle.started", Args("reason", reason.ToString()));
            if (reason != SuspendReason.TrainingSkirmish)
            {
                MeetEnemies();
                RegisterBonds();
            }
            OnBattleResolved();
        }

        /// <summary>
        /// Викликається після кожної команди Battle (§4.1: "OnBattleResolved()
        /// ... викликається після кожної команди Battle, коли CombatState.Outcome
        /// != Ongoing"). Повертає <c>State = _resume.ReturnState</c> безумовно
        /// для ВСІХ причин підвісу (акцептанс D1), а вже ПОТІМ, за причиною,
        /// застосовує системні наслідки (Напруга/Лояльність/дроп визначає той,
        /// хто просив бій — не сам CombatState, R8/§4.1) — це може посунути
        /// стан ДАЛІ (наприклад, у Scene для розв'язки вузла 1), що є нормальним
        /// продовженням конвеєра, а не порушенням повернення до ReturnState.
        /// </summary>
        /// <summary>
        /// Вид бою в мить розв'язки (останній рядок журналу — удар/смерть, що
        /// вирішили бій). <see cref="GetBattleView"/> після розв'язки повертає null,
        /// тож презентер бере цей знімок, щоб показати фінальну дію до панелі
        /// результату. null — ще жодного бою не розв'язано.
        /// </summary>
        public BattleView LastResolvedBattleView { get; private set; }

        private void OnBattleResolved()
        {
            if (_battle == null || _resume == null || _battle.Outcome == CombatOutcome.Ongoing) return;

            var result = BattleResult.From(_battle);
            var band = MapBattleBand(result);
            var reason = _resume.Reason;
            var returnState = _resume.ReturnState;

            // Відступ (Поправка №14.7): поле лишається за ворогом. Раніше MapBattleBand
            // давав відступу полосу Base — «важка перемога»; поки відступ ніхто не
            // кликав, діри не було видно, а з кнопкою відступ на першому ході
            // коштував би дешевше за бій (у данжі — «кімнату пройдено» з лутом).
            bool retreated = result.Outcome == BattleOutcome.Retreat;
            if (retreated) band = OutcomeBand.Worst;

            if (reason != SuspendReason.TrainingSkirmish)
                ApplyBattleCasualties(result);

            // Поразка → полон (Поправка №14.7): хто втікає, хто лишається в чужих руках.
            // Лише малі бої — кімнати данжу і рейд; вузол 1 і фінал — великі сценарні
            // битви з власними драбинами наслідків (фінал №14.7 не змінює).
            var captivity = ComputeCaptivity(result, reason);

            // Досьє (Поправка №14.6): хто з ворогом бився — той його знає.
            if (reason != SuspendReason.TrainingSkirmish) StudyEnemies(_battle, "battle");

            // Здача (Поправка №14.2): долю тих, хто здався, гравець вирішує на панелі
            // результату; тренування — пісочниця, полонених не дає.
            if (reason != SuspendReason.TrainingSkirmish && result.SurrenderedEnemies != null)
                foreach (var e in result.SurrenderedEnemies)
                {
                    if (e.Spared)
                    {
                        // «Милосердя на полі»: пощаджений — одразу полонений, долю вирішує віче.
                        _prisoners.Take(e.EnemyDefinitionId, e.DisplayName, (int)e.Rank, e.NeverRecruitable, _processor.CurrentDay);
                        LogEvent("enemy.spared", Args("enemyId", e.EnemyDefinitionId, "rank", e.Rank.ToString()));
                        continue;
                    }
                    _pendingSurrenders.Add(e);
                    LogEvent("enemy.surrendered", Args("enemyId", e.EnemyDefinitionId, "rank", e.Rank.ToString()));
                }

            bool autoResolved = _battleAutoResolvedThisCall;
            _battleAutoResolvedThisCall = false;
            LogEvent(autoResolved ? "combat.autoresolved" : "combat.battle.resolved",
                Args("outcome", result.Outcome.ToString(), "rounds", result.Rounds.ToString(CultureInfo.InvariantCulture), "reason", reason.ToString()));

            // Останній вид бою — ДО обнулення: презентер дограє такт фінального
            // удару (рев'ю Бою v2: раніше GetBattleView() тут уже повертав null,
            // і удар, що вирішив бій, не показувався зовсім).
            LastResolvedBattleView = GetBattleView();

            _battle = null;
            _resume = null;
            State = returnState;

            switch (reason)
            {
                case SuspendReason.PassVanguardBloody:
                {
                    var report = _processor.ResolvePendingWithBand(band);
                    CompletePassVanguard(report, band, wasBloody: true);
                    break;
                }
                case SuspendReason.DungeonCombatRoom:
                    if (retreated) RetreatFromDungeonBattle();
                    else FinishDungeonCombat(band, result);
                    break;
                case SuspendReason.FinaleAssault:
                    CompleteFinale(band, wasBloody: true);
                    break;
                case SuspendReason.TrainingSkirmish:
                    break; // пісочниця: State вже Title/ReturnState, кампанію не чіпаємо
                case SuspendReason.CaptiveRaid:
                    FinishCaptiveRaid(result);
                    break;
            }

            // Після розв'язки за причиною: вайп чи відступ уже повернули загін додому,
            // і бранці йдуть у полон з громади, а не посеред данжу.
            ApplyCaptivity(captivity);
            if (reason == SuspendReason.CaptiveRaid) _raidGroupId = null;
        }

        /// <summary>
        /// Відступ із бою бойової кімнати (Поправка №14.7): загін іде з данжу —
        /// не вайп, а обережний вихід (<see cref="DungeonRun.RetreatFromBattle"/>):
        /// незабанковане пропадає, рани вже прийшли з бою.
        /// </summary>
        private void RetreatFromDungeonBattle()
        {
            var rep = _dungeon.RetreatFromBattle();
            if (rep.ThreatBandChanged) LogEvent("dungeon.threat_band_changed", Args("band", _dungeon.ThreatBand.ToString()));
            LogEvent("dungeon.retreat", Args("depth", rep.DepthReached.ToString(CultureInfo.InvariantCulture)));

            ExpeditionResult discarded;
            _party.Return(_state, out discarded);
            _dungeon = null;
            State = SessionState.Morning;
        }

        private void FinishDungeonCombat(OutcomeBand band, BattleResult result)
        {
            var casualtyIds = new List<string>();
            if (result.Casualties != null)
                foreach (var c in result.Casualties) casualtyIds.Add(c.CompanionId);

            var res = _dungeon.ReportCombat(band, casualtyIds);
            ApplyDungeonResolution(res);

            if (res.Wiped)
            {
                LogEvent("dungeon.wiped", Args("room", res.RoomId));
                // Поправка №15.1: розгром теж повертає загін звідти, звідки
                // виходив — вилазка відбулась, навіть провалена.
                TryBringSpecialistFromExpedition(_dungeon.SiteId);
                ExpeditionResult discarded;
                _party.Return(_state, out discarded);
                _dungeon = null;
                State = SessionState.Morning;
                return;
            }

            State = SessionState.Dungeon;
        }

        private void CompletePassVanguard(DayReport report, OutcomeBand band, bool wasBloody)
        {
            PassVanguardOutcome.Apply(_state, band, wasBloody, _flags);
            var loyaltyChange = ApplyLoyaltyDelta("myroslava", PassVanguardOutcome.MyroslavaLoyaltyDelta(band), "pass_vanguard");
            LogLoyaltyChange(loyaltyChange);
            if (band == OutcomeBand.Base || band == OutcomeBand.Worst)
                LogEvent("companion.left_settlement", Args("companionId", "myroslava"));

            LogEvent("decision.resolved", Args("path", wasBloody ? "Bloody" : "Quiet", "band", band.ToString(), "incidentId", "pass_vanguard"));

            // Той самий фікс дубля, що в ResolveIncident: pass_vanguard уже
            // залогований рядком вище.
            MarkIncidentsAlreadyTranslated(report);
            TranslateReport(report);
            _lastDayReport = BuildDayReportView(report);

            if (report.AwaitsDecision)
            {
                _currentPending = report.Pending;
                State = SessionState.Decision;
            }
            else
            {
                // Фікс-ревью (major, раунд 2): справжній тактичний бій міг уже
                // вбити Максима (ApplyBattleCasualties, раніше в цьому ж
                // FinishBattle) — сценарний текст полос good/worst не повинен
                // стверджувати "поранений" про того, хто щойно "загинув" у
                // стрічці подій вище.
                bool maksymDead = _state.Roster.Get(PassVanguardOutcome.MaksymId)?.IsDead == true;
                BeginScene(OpeningScenes.PassResolution(PassVanguardOutcome.ResolveKey(band, wasBloody, maksymDead)), SessionState.Evening);
            }
        }

        private void CompleteFinale(OutcomeBand band, bool wasBloody)
        {
            _finaleResolved = true;
            var outcome = Finale.Resolve(band);
            ApplyFinaleCost(outcome.Cost);
            _finaleOutcomeKey = outcome.Key;

            LogEvent("finale.resolved", Args("path", wasBloody ? "Bloody" : "Quiet", "band", band.ToString(),
                "key", outcome.Key, "cost", outcome.Cost.ToString()));

            State = SessionState.Night;
        }

        private void ApplyFinaleCost(FinaleCostKind cost)
        {
            // ПЛЕЙСХОЛДЕР (як і решта чисел зрізу, §9): FinaleCostKind не мав
            // жодного механічного наслідку в пакеті B6 (openIssue "жоден
            // ресурс/ростер не визначено для жодного виду ціни") — тут
            // фіксується перше пряме рішення: Compromise коштує невеликого
            // золота (політичний компроміс), Hostage — рани найближчому
            // соратнику (когось лишають заручником/платить тілом).
            if (cost == FinaleCostKind.Compromise)
            {
                _state.Resources.TrySpend(ResourceType.Gold, 10);
            }
            else
            {
                var victim = _worldRoster.Get("maksym");
                if (victim != null && !victim.IsDead)
                    LogScarIfGranted("maksym", _rosterAdapter?.WoundReporting("maksym", 40.0, WoundTier.Serious));
            }
        }

        // =====================================================================
        // Summary / FreePlay
        // =====================================================================

        public SessionState AcknowledgeSummary()
        {
            RequireState(SessionState.Summary);
            _summaryAcknowledged = true;
            _freePlay = true;
            State = SessionState.FreePlay;
            AutoSave();
            return State;
        }

        public SummaryView GetSummaryView()
        {
            return new SummaryView
            {
                FinalRoster = GetRosterView().Companions,
                BuiltBuildings = new List<string>(_works.Built),
                Wallet = GetEconomyView(),
                Factions = GetFactionsView().Factions,
                FinaleOutcomeKey = _finaleOutcomeKey,
                Echoes = StoryEchoes.Collect(_flags, IsArcCompleted)
            };
        }

        // =====================================================================
        // Поправка №7.8, п.4 DELIVER: журнал механік для тестера — по одному
        // запису на кожен рядок §2 TEST_BUILD.md плюс нові механіки цього
        // пакета (вибір у сцені, глава арки, нічна розмова-конфронтація,
        // стройка за одну добу). Дані, а не дашборд (R17): лише bool Seen +
        // два текстові ключі, жодного схованого числа. "Seen" рахується з
        // КУМУЛЯТИВНИХ ключів подій (<see cref="_seenEventKeys"/>) — той
        // самий принцип, що вже тримає інваріант 3 (Game.Gameplay бачить
        // лише ключі/bool, ніколи сирі числа Напруги).
        //
        // Рядки без власної події ("похідне" в §2 — presence/empty_post/
        // signals_no_repeat) прив'язані до найближчого спостережуваного
        // ключа, який неминуче йде разом із механікою (задокументовано біля
        // кожного запису) — журнал каже "ця механіка спрацювала хоч раз",
        // не "усі її розгалуження побачені".
        // =====================================================================

        private sealed class MechanicJournalDef
        {
            public readonly string Id;
            public readonly string[] ExactKeys;
            public readonly string[] KeyPrefixes;
            public readonly Func<GameSession, bool> ExtraSeen;

            public MechanicJournalDef(string id, string[] exactKeys = null, string[] keyPrefixes = null,
                Func<GameSession, bool> extraSeen = null)
            {
                Id = id;
                ExactKeys = exactKeys ?? Array.Empty<string>();
                KeyPrefixes = keyPrefixes ?? Array.Empty<string>();
                ExtraSeen = extraSeen;
            }
        }

        private static readonly MechanicJournalDef[] MechanicsJournalRegistry =
        {
            new MechanicJournalDef("day_cycle", exactKeys: new[] { "day.advanced" }),
            new MechanicJournalDef("assignment", exactKeys: new[] { "assign.made" }),
            // Присутність — похідне (§2 №3, немає власної події): та сама
            // фільтрація кандидатів спрацьовує на кожному decision.resolved.
            new MechanicJournalDef("presence", exactKeys: new[] { "decision.resolved" }),
            new MechanicJournalDef("decision_point", exactKeys: new[] { "decision.resolved" }),
            new MechanicJournalDef("outcome_bands",
                exactKeys: new[] { "decision.resolved", "finale.resolved", "quest.choice.resolved" }),
            // Порожній пост = Найгірша (§2 №6) — той самий ключ, що decision_point:
            // args["band"]/["noCandidate"] різнять їх, а ключ у _seenEventKeys — ні.
            new MechanicJournalDef("empty_post", exactKeys: new[] { "decision.resolved" }),
            new MechanicJournalDef("night_patrol", extraSeen: s => s._patrolledANight),
            new MechanicJournalDef("forewarn_ladder", keyPrefixes: new[] { "forewarn.level" }),
            new MechanicJournalDef("crisis", exactKeys: new[]
                { "crisis.test.warn", "crisis.test.window", "crisis.test.mitigated", "crisis.test.unmitigated" }),
            new MechanicJournalDef("post_reports", keyPrefixes: new[] { "post." }),
            // Сигнали без повторів (§2 №11) — похідне, той самий щабель передвісника.
            new MechanicJournalDef("signals_no_repeat", keyPrefixes: new[] { "forewarn.level" }),
            // Темп тестової збірки (Поправка №7): зсув смуги Напруги і
            // великий природний бунт на площі — окремі записи журналу, щоб
            // тестер бачив обидва, не плутаючи їх зі скриптованою пожежею
            // доби 5 ("crisis" вище).
            new MechanicJournalDef("tension_band_change", keyPrefixes: new[] { "tension.band." }),
            new MechanicJournalDef("great_crisis", keyPrefixes: new[] { "incident.crisis_riot." }),
            new MechanicJournalDef("band_change_signal", exactKeys: new[] { "loyalty.band_changed", "faction.standing_changed" }),
            new MechanicJournalDef("production", exactKeys: new[]
                { "production.resource", "production.leveled_up", "production.food_shortage", "production.recovered" }),
            new MechanicJournalDef("building", keyPrefixes: new[] { "city.built." },
                exactKeys: new[] { "city.building.ordered", "council.raid.ordered", "council.settlers.ordered" }),
            new MechanicJournalDef("council_actions", keyPrefixes: new[]
                { "council.decree", "council.diplomacy", "council.invest", "council.prepare_threat", "council.outfit_expedition" }),
            new MechanicJournalDef("population_tier", keyPrefixes: new[] { "city.tier." }),
            new MechanicJournalDef("expedition", exactKeys: new[] { "expedition.departed" }),
            new MechanicJournalDef("dungeon_delve", exactKeys: new[] { "dungeon.push", "dungeon.extract", "dungeon.wiped", "dungeon.room.bypassed" }),
            new MechanicJournalDef("loot", exactKeys: new[] { "loot.dropped" }),
            new MechanicJournalDef("equip", exactKeys: new[] { "equip.changed" }),
            new MechanicJournalDef("craft", exactKeys: new[] { "craft.upgraded" }),
            new MechanicJournalDef("scars", exactKeys: new[] { "scar.granted" }),
            new MechanicJournalDef("loyalty", exactKeys: new[] { "loyalty.band_changed" }),
            // Фікс-ревью (журнал механік тестера, MechanicsJournalCompletionTests):
            // цей запис був НЕДОСЯЖНИЙ фізично — LogRipple (нижче) ніколи не
            // логує голий ключ "roster.rippled", лише суфіксовані варіанти
            // "roster.rippled.<тип зв'язку>.<загибель/зрада>" (полірування,
            // ціль 5 «Якість стрічки», задокументовано коментарем над самим
            // LogRipple), тож жоден Death/Defection за жоден прогін не міг
            // позначити цей запис побаченим. AllMechanicsCoverageTests.Row25
            // і GameSessionTests уже перевіряють подію префіксом
            // (<c>StartsWith("roster.rippled")</c>) — журнал реєстру мав
            // робити те саме.
            new MechanicJournalDef("roster_drama", keyPrefixes: new[] { "roster.rippled" }),
            new MechanicJournalDef("defection", exactKeys: new[] { "companion.defected" }),
            new MechanicJournalDef("companion_arc", exactKeys: new[] { "arc.chapter_opened" }),
            new MechanicJournalDef("quests", exactKeys: new[] { "quest.choice.resolved" }),
            new MechanicJournalDef("factions", exactKeys: new[] { "faction.standing_changed" }),
            new MechanicJournalDef("readiness_finale", exactKeys: new[] { "finale.resolved" }),
            new MechanicJournalDef("tactical_combat", exactKeys: new[]
                { "combat.attack.hit", "combat.attack.miss", "combat.attack.crit", "combat.attack.graze", "combat.overwatch.triggered" }),
            new MechanicJournalDef("auto_resolve", exactKeys: new[] { "combat.autoresolved" }),
            new MechanicJournalDef("training_battle", exactKeys: new[] { "combat.training.started" }),
            new MechanicJournalDef("creation", exactKeys: new[] { "creation.confirmed" }),
            new MechanicJournalDef("progression", exactKeys: new[] { "progression.level_up" }),
            new MechanicJournalDef("portrait_scenes", exactKeys: new[] { "scene.finished" }),
            new MechanicJournalDef("save_load", exactKeys: new[] { "game.saved", "game.loaded" }),
            // summary/free_play — стан сесії, не подія (AcknowledgeSummary/
            // AdvanceDay нічого не пишуть у DayLog про це), тож журнал читає
            // прапорці напряму, а не шукає ключ.
            new MechanicJournalDef("summary", extraSeen: s => s._summaryAcknowledged),
            new MechanicJournalDef("free_play", extraSeen: s => s._freePlay),

            // ---- нові механіки Поправки №7.8 ----
            new MechanicJournalDef("dialogue_choice", exactKeys: new[] { "scene.choice.made" }),
            new MechanicJournalDef("arc_chapter", exactKeys: new[] { "arc.chapter_completed" }),
            new MechanicJournalDef("betrayal_confrontation", exactKeys: new[] { "scene.betrayal_confrontation.begun" }),
            // Стройка за одну добу (Поправка №7.7) — той самий сигнал заверш-
            // еної будови, що й "building" вище; окремого ключа "за 1 добу"
            // немає (тривалість — внутрішня деталь CityWorks, не подія).
            new MechanicJournalDef("building_one_day", keyPrefixes: new[] { "city.built." }),
        };

        public IReadOnlyList<MechanicJournalEntryView> GetMechanicsJournal()
        {
            var result = new List<MechanicJournalEntryView>(MechanicsJournalRegistry.Length);
            foreach (var def in MechanicsJournalRegistry)
            {
                bool seen = def.ExtraSeen != null && def.ExtraSeen(this);

                if (!seen)
                    foreach (var key in def.ExactKeys)
                        if (_seenEventKeys.Contains(key)) { seen = true; break; }

                if (!seen && def.KeyPrefixes.Length > 0)
                    foreach (var seenKey in _seenEventKeys)
                    {
                        bool matched = false;
                        foreach (var prefix in def.KeyPrefixes)
                            if (seenKey.StartsWith(prefix, StringComparison.Ordinal)) { matched = true; break; }
                        if (matched) { seen = true; break; }
                    }

                result.Add(new MechanicJournalEntryView
                {
                    Id = def.Id,
                    TitleKey = "journal." + def.Id + ".title",
                    HintKey = "journal." + def.Id + ".hint",
                    Seen = seen
                });
            }
            return result;
        }

        // =====================================================================
        // Будь-де: зведений публічний стан + допоміжні View
        // =====================================================================

        public SessionView CurrentView
        {
            get
            {
                return new SessionView
                {
                    State = State,
                    Day = _processor?.CurrentDay ?? 0,
                    Phase = _lastPhase,
                    Tier = _processor?.Tier ?? 0,
                    CrowdBand = CrowdBandName(_processor?.Population?.CrowdBand ?? 0),
                    TensionBand = (_processor?.Tension.Band ?? TensionBand.Calm).ToString(),
                    DaysInBand = _processor?.Tension.DaysInCurrentBand ?? 0,
                    IsFreePlay = _freePlay,
                    IsPatrolling = _processor?.IsPatrolling ?? false
                };
            }
        }

        public EconomyView GetEconomyView()
        {
            return new EconomyView
            {
                Gold = _state.Resources.Get(ResourceType.Gold),
                BuildComponent = _state.Resources.Get(ResourceType.BuildComponent),
                CraftComponent = _state.Resources.Get(ResourceType.CraftComponent),
                Food = _state.Resources.Get(ResourceType.Food)
            };
        }

        public CityView GetCityView()
        {
            // Тренувальний бій з титулу йде без партії (світу немає): вид — порожнє
            // місто, а не NullReferenceException. Сцену села оболонка оновлює після
            // КОЖНОЇ команди, тож виняток тут ламав кнопку «Тренувальний бій»
            // (знайдено турами Бою v2, 25.09.2026).
            if (_works == null || _processor == null)
                return new CityView { Built = new List<BuildingView>(), InProgress = new List<BuildingView>(), OpenPosts = new List<string>() };

            // Порядок каталогу, а не HashSet: інакше після завантаження той самий
            // набір будівель ішов у вкладку в іншому порядку (аудит сейвів 25.09.2026).
            var built = new List<BuildingView>();
            foreach (var def in DefaultBuildingsType.All())
                if (_works.Has(def.Id)) built.Add(new BuildingView { Id = def.Id, StageOf = 5 });

            var inProgress = new List<BuildingView>();
            foreach (var def in DefaultBuildingsType.All())
                if (_works.IsBuilding(def.Id)) inProgress.Add(new BuildingView { Id = def.Id, StageOf = _works.StageOf(def.Id) });

            return new CityView
            {
                Built = built,
                InProgress = inProgress,
                RaidReady = _works.RaidReady(_processor.CurrentDay, _cfg),
                SettlersReady = _works.SettlersReady(_processor.CurrentDay, _cfg),
                TestBuildOneDayConstruction = _works.OneDayConstruction,
                OpenPosts = OpenPostIds()
            };
        }

        public RosterView GetRosterView()
        {
            var list = new List<CompanionSummary>();
            // Без партії (тренувальний бій з титулу) — порожній ростер, не виняток.
            if (_worldRoster == null) return new RosterView { Companions = list };
            foreach (var c in _worldRoster.All)
            {
                // Поправка №12.10: хто не прибився до гурту — ніде не
                // з'являється, ростер (вкладка «Люди») не виняток.
                if (c.Status == CompanionStatus.NotArrived) continue;

                var equipped = new List<string>();
                foreach (EquipSlot slot in Enum.GetValues(typeof(EquipSlot)))
                {
                    var item = c.Equipment.Get(slot);
                    if (item != null) equipped.Add(item.Definition.Id);
                }

                list.Add(new CompanionSummary
                {
                    Id = c.Id,
                    DisplayName = c.DisplayName,
                    Status = c.Status,
                    AssignedSlotId = c.AssignedSlotId,
                    Level = c.Level,
                    Class = c.Card?.Class ?? CompanionClass.Brawler,
                    Loyalty = c.Card != null && c.Card.CanBeCompanion ? (LoyaltyBand?)c.LoyaltyBand : null,
                    Equipped = equipped,
                    ScarCount = c.Scars.Count
                });
            }
            return new RosterView { Companions = list };
        }

        /// <summary>
        /// Повна картка персонажа (полірування, ціль 1 «Картка персонажа»):
        /// 4 атрибути, 10 скілів, активні трейти, шрами, доступні перки,
        /// рівень/xp, стан, лояльність-БАНД, спорядження, ключові похідні
        /// бойові стати — усе одним StatSnapshot (<see cref="Companion.Resolve"/>),
        /// тим самим агрегатором, що й бій, щоб картка й арена не розходились
        /// у числах. Null, якщо такого companionId немає в ростері.
        /// </summary>
        public Views.CharacterSheetView GetCharacterSheet(string companionId)
        {
            var c = _worldRoster?.Get(companionId);
            if (c == null) return null;

            var snapshot = c.Resolve(_cfg);

            var attributes = new List<Views.AttributeLineView>();
            foreach (var a in Stats.Attributes.All)
                attributes.Add(new Views.AttributeLineView { AttributeKey = a.ToString().ToLowerInvariant(), Score = snapshot.Attribute(a) });

            var skills = new List<Views.SkillLineView>();
            foreach (var s in Stats.Skills.All)
                skills.Add(new Views.SkillLineView { SkillKey = Stats.Skills.KeyId(s), Score = snapshot.Skill(s) });

            var traits = new List<Views.TraitLineView>();
            foreach (var t in c.Traits.Active)
                traits.Add(new Views.TraitLineView { TraitId = t.Id, Polarity = t.Polarity.ToString() });

            var scarIds = new List<string>();
            foreach (var sc in c.Scars.Scars) scarIds.Add(sc.Id);

            var unlockedPerkIds = new List<string>();
            foreach (var p in c.Perks.Taken) unlockedPerkIds.Add(p.Id);

            var availablePerks = new List<Views.PerkPreviewLineView>();
            foreach (var p in Characters.Perks.DefaultPerks.All())
            {
                if (c.Perks.Has(p.Id)) continue;
                var verdict = c.Perks.Evaluate(p, c.Skills);
                availablePerks.Add(new Views.PerkPreviewLineView
                {
                    PerkId = p.Id,
                    Available = verdict == Characters.Perks.PerkAvailability.Available,
                    ReasonKey = PerkReasonKey(verdict)
                });
            }

            return new Views.CharacterSheetView
            {
                CompanionId = c.Id,
                Level = c.Level,
                Xp = c.Xp,
                XpToNextLevel = Balance.ProgressionMath.XpToNext(c.Level, _cfg),
                Status = c.Status,
                Class = c.Card?.Class ?? CompanionClass.Brawler,
                Loyalty = c.Card != null && c.Card.CanBeCompanion ? (LoyaltyBand?)c.LoyaltyBand : null,
                Attributes = attributes,
                Skills = skills,
                Traits = traits,
                ScarIds = scarIds,
                UnlockedPerkIds = unlockedPerkIds,
                AvailablePerks = availablePerks,
                Combat = new Views.CombatStatsView
                {
                    HpMax = snapshot.GetInt(Stats.StatKeys.Of(Stats.DerivedStat.MaxHp)),
                    ApMax = snapshot.GetInt(Stats.StatKeys.Of(Stats.DerivedStat.MaxAp)),
                    Initiative = snapshot.GetInt(Stats.StatKeys.Of(Stats.DerivedStat.Initiative)),
                    Accuracy = snapshot.GetInt(Stats.StatKeys.Of(Stats.DerivedStat.Accuracy)),
                    Defense = snapshot.GetInt(Stats.StatKeys.Of(Stats.DerivedStat.Defense)),
                    Armor = snapshot.GetInt(Stats.StatKeys.Of(Stats.DerivedStat.Armor)),
                    CritChance = (int)System.Math.Round(snapshot.Get(Stats.StatKeys.Of(Stats.DerivedStat.CritChance)) * 100.0)
                },
                Equipment = new Views.EquipmentSheetView
                {
                    WeaponId = c.Equipment.Get(EquipSlot.Weapon)?.Definition.Id,
                    ArmorId = c.Equipment.Get(EquipSlot.Armor)?.Definition.Id,
                    AccessoryId = c.Equipment.Get(EquipSlot.Accessory)?.Definition.Id,
                    HeadId = c.Equipment.Get(EquipSlot.Head)?.Definition.Id,
                    HandsId = c.Equipment.Get(EquipSlot.Hands)?.Definition.Id,
                    LegsId = c.Equipment.Get(EquipSlot.Legs)?.Definition.Id,
                    FeetId = c.Equipment.Get(EquipSlot.Feet)?.Definition.Id,
                    OffhandId = c.Equipment.Get(EquipSlot.Offhand)?.Definition.Id,
                    Slots = BuildSlotViews(c.Equipment)
                }
            };
        }

        private static string PerkReasonKey(Characters.Perks.PerkAvailability verdict)
        {
            switch (verdict)
            {
                case Characters.Perks.PerkAvailability.SkillTooLow: return "ui.reason.perk.skill_too_low";
                case Characters.Perks.PerkAvailability.MissingPrerequisite: return "ui.reason.perk.missing_prerequisite";
                case Characters.Perks.PerkAvailability.AlreadyTaken: return "ui.reason.perk.already_taken";
                case Characters.Perks.PerkAvailability.Invalid: return "ui.reason.perk.invalid";
                default: return null;
            }
        }

        public SignalsFeed GetSignalsFeed()
        {
            var lines = new List<SignalLineView>();
            if (_lastDayReport?.Signals?.Requests != null)
                foreach (var req in _lastDayReport.Signals.Requests)
                    lines.Add(new SignalLineView { Channel = req.Channel.ToString(), TopicId = req.TopicId, Tags = req.Tags });
            return new SignalsFeed { Lines = lines };
        }

        public PendingOfferView GetPendingOffer() => BuildPendingOfferView(_currentPending);
        public QuestOfferView GetQuestOffer() => _currentQuestOffer;
        public DayReportView LastDayReport => _lastDayReport;

        public FactionsView GetFactionsView()
        {
            var names = new Dictionary<string, string>();
            foreach (var f in DefaultFactions.All()) names[f.Id] = f.DisplayName;

            var list = new List<FactionSummary>();
            foreach (var id in _factions.FactionIds)
            {
                var standing = _factions.Get(id);
                string name;
                names.TryGetValue(id, out name);
                list.Add(new FactionSummary { Id = id, DisplayName = name ?? id, Band = standing.Band.ToString() });
            }
            return new FactionsView { Factions = list };
        }

        public ReadinessView GetReadinessView()
        {
            // MilestonesReached — реальний лічильник (ReadinessTrack.Add
            // інкрементує його щоразу, коли віха таки застосована). MilestonesTotal
            // ПЛЕЙСХОЛДЕР (§9, як і решта чисел зрізу): дизайн не фіксує "загальну"
            // кількість віх як ціль — тут це кількість РІЗНОВИДІВ віхи, визначених
            // у ReadinessBalance (вилазка/квест/стройка/страх/указ ради), а не
            // прогрес-бар до конкретного числа.
            return new ReadinessView
            {
                Band = _readiness.Band.ToString(),
                MilestonesReached = _readiness.MilestonesReached,
                MilestonesTotal = 5
            };
        }

        // =====================================================================
        // Внутрішнє
        // =====================================================================

        private void RequireState(SessionState expected)
        {
            if (State != expected)
                throw new InvalidOperationException("Команда недоступна у стані " + State + " (потрібен " + expected + ").");
        }

        /// <summary>
        /// Пакет D2 (§3.6 "Вільна гра": "той самий цикл Morning→Night"; ConfirmMorning
        /// вже трактує Morning/FreePlay як один хаб — див. коментар над ним):
        /// усі команди ранку (Assign/Order*/PreviewExpedition/DepartExpedition/
        /// PreviewBuildPlan/CommitBuildPlan/Equip/Unequip/CraftUpgrade) досі були
        /// прибиті ЛИШЕ до Morning — після AcknowledgeSummary State назавжди стає
        /// FreePlay (SettleAfterDayReport: "State = _freePlay ? FreePlay : Morning"),
        /// тож ЖОДНА команда ранку не могла спрацювати вже починаючи з доби 6.
        /// Бот-прогін (D2, AllMechanicsCoverageTests §42 "Вільна гра") це й виявив
        /// напряму: без цієї правки данж/квест/фракції/стройка не мають чим
        /// спрацювати у вікні днів 6–15 — не слабшаємо тест, а замикаємо шов.
        /// </summary>
        private void RequireMorningOrFreePlay()
        {
            RequireAnyState(SessionState.Morning, SessionState.FreePlay);
        }

        /// <summary>
        /// Блокер-фікс ревью: §4.1 сам документує OfferQuestStage/ResolveQuestChoice
        /// як "Morning/Evening" (R6 — квестовий рушій ПОЗА конвеєром дня), а
        /// сценарій доби 2 (§3.2) додатково кличе їх уночі ("Ніч | Квест Гафії,
        /// етап 1"). Попередній блок Morning-гвардів (2f860c6) помилково
        /// причепив сюди суцільний RequireState(Morning) разом з рештою
        /// ранкових команд — цей метод звужує список легальних станів саме до
        /// задокументованих трьох, а не до одного.
        /// </summary>
        private void RequireAnyState(params SessionState[] allowed)
        {
            for (int i = 0; i < allowed.Length; i++)
                if (State == allowed[i]) return;
            throw new InvalidOperationException("Команда недоступна у стані " + State + " (потрібен один з: " +
                string.Join(", ", Array.ConvertAll(allowed, s => s.ToString())) + ").");
        }

        private void ClearDayLog()
        {
            _dayLog.Clear();
            _dayLogVersion++;
            _translatedIncidentCount = 0;
        }

        private void LogEvent(string key, IReadOnlyDictionary<string, string> args = null)
        {
            if (string.IsNullOrEmpty(key)) return;
            _dayLog.Add(new GameEvent(key, _processor?.CurrentDay ?? 0, _lastPhase, args));
            _seenEventKeys.Add(key);
        }

        private static IReadOnlyDictionary<string, string> Args(params string[] kv)
        {
            var dict = new Dictionary<string, string>();
            for (int i = 0; i + 1 < kv.Length; i += 2)
                if (kv[i] != null) dict[kv[i]] = kv[i + 1] ?? string.Empty;
            return dict;
        }

        /// <summary>
        /// Позначає всі інциденти поточного <paramref name="report"/> (звідси і
        /// раніше в цій фазі) уже перекладеними — викликач щойно залогував
        /// останній з них явно (з даними, яких TranslateReport не має, напр.
        /// "path"), і цикл у TranslateReport нижче не повинен зробити це вдруге.
        /// </summary>
        private void MarkIncidentsAlreadyTranslated(DayReport report)
        {
            if (report?.Incidents != null && report.Incidents.Count > _translatedIncidentCount)
                _translatedIncidentCount = report.Incidents.Count;
        }

        /// <summary>
        /// Майор-фікс ревью: "day.advanced" раніше логувався тут, а TranslateReport
        /// кличеться не лише з AdvanceDay/AdvanceNight (де DayProcessor.Advance()
        /// СПРАВДІ рухає CurrentDay/фазу рівно раз), а й повторно з ResolveIncident/
        /// CompletePassVanguard у тій самій фазі (лише довирішують уже відкрите
        /// рішення, без нового Advance()) — той самий клас дубля, що вже було
        /// пофіксено для "decision.resolved" (_translatedIncidentCount). Тепер
        /// "day.advanced" логують САМІ виклики AdvanceDay()/AdvanceNight(), а
        /// TranslateReport відповідає лише за Incidents/Signals.
        /// </summary>
        private void TranslateReport(DayReport report)
        {
            if (report == null) return;

            if (report.Incidents != null)
            {
                // DayProcessor.BuildReport завжди повертає ПОВНИЙ накопичений
                // report.Incidents цієї фази (не лише щойно розв'язаний) — тож
                // перекладаємо лише хвіст, якого ще не бачили (_translatedIncidentCount),
                // інакше повторний виклик у фазі з 2+ рішеннями (аудит П10)
                // дублював би "decision.resolved" для вже залогованих інцидентів.
                for (int i = _translatedIncidentCount; i < report.Incidents.Count; i++)
                {
                    var outcome = report.Incidents[i];
                    LogEvent("decision.resolved", Args("incidentId", outcome.IncidentId, "band", outcome.Band.ToString(),
                        "noCandidate", outcome.WasUnmanned ? "1" : "0"));

                    // Кризис із природного інциденту (CrisisBite.KillCompanion,
                    // DefaultIncidents) вбиває через IncidentResolver/ICasualtySink
                    // ДО того, як цей report дійшов сюди (RosterAdapter.Kill уже
                    // відпрацював) — тож тут лише довершуємо той самий шов смерті,
                    // що й бойові втрати нижче (ApplyBattleCasualties): повернути
                    // гір і сповістити. Без цього гір загиблого від кризи зникав би
                    // назавжди, а "companion.died"/"roster.rippled" не пішли б
                    // жодного разу для цього шляху смерті (seamsForD1 B3/B4: "whoever
                    // wires death must call RecoverGearFrom").
                    if (outcome.Bite == CrisisBite.KillCompanion && !string.IsNullOrEmpty(outcome.AffectedActorId))
                        HandleCompanionDeath(outcome.AffectedActorId);
                }
                _translatedIncidentCount = report.Incidents.Count;
            }

            if (report.Signals != null && report.Signals.Requests != null)
                foreach (var req in report.Signals.Requests)
                {
                    string topic = AdjustPassVanguardTopicIfMaksymDead(req.TopicId);
                    LogEvent(topic,
                        Args("channel", req.Channel.ToString(), "urgency", req.Urgency.ToString(),
                        "subject", req.SubjectId, "delta", req.IsDelta ? "1" : "0",
                        "domain", DomainTagFrom(req.Tags)));
                }
        }

        /// <summary>
        /// SETTLEMENT_LAYER §5.1 правило 4: щабель 2 передвісника зобов'язаний
        /// назвати домен, щабель 3 — близькість. <see cref="Signals.SignalComposer"/>
        /// вже кладе домен у теги кандидата ("domain:" + f.DomainTag) — але
        /// <see cref="TranslateReport"/> раніше цей тег ігнорував, і текст
        /// "forewarn.level2"/"forewarn.level3" лишався безликим для БУДЬ-ЯКОГО
        /// джерела (знайдено 24.09.2026 разом зі стисненим темпом Поправки №7:
        /// щойно природна криза вперше запрацювала, стало видно, що й її
        /// передвісники безликі). Тут тег дістається й кладеться окремим
        /// аргументом "domain" — <c>ScreenText.EventLine</c> уже вміє
        /// підставляти {domain} через <c>ContentLabel("domain", ...)</c> (та
        /// сама підстановка, що вже використовує "domain.road"/"domain.craft").
        /// Ключ Key лишається незмінним ("forewarn.levelN") — жодного
        /// топік-перемикання, тому існуючі фільтри за буквальним ключем
        /// (<c>TestBuildTensionPaceTests.FirstDayForewarn</c>, реєстр журналу
        /// механік) не ламаються.
        /// </summary>
        private static string DomainTagFrom(string[] tags)
        {
            if (tags == null) return null;
            const string prefix = "domain:";
            for (int i = 0; i < tags.Length; i++)
                if (tags[i] != null && tags[i].StartsWith(prefix, StringComparison.Ordinal))
                    return tags[i].Substring(prefix.Length);
            return null;
        }

        /// <summary>
        /// Фікс-ревью (major, раунд 2, знайдено QA): SignalComposer (Core,
        /// чистий C#) генерує ключ стрічки подій для вузла 1 механічно —
        /// <c>inc.TopicId + "." + inc.Band</c> → "incident.pass_vanguard.Good"/
        /// "...Worst" — той самий 4-полосний абстрактний розв'язок, що й
        /// <see cref="PassVanguardOutcome.ResolveKey"/> (яка вже враховує
        /// maksymDead для сценарного "«…»"), але ЦЕЙ ключ живе окремо в
        /// сигнальному шарі й друкується прямо у стрічку подій (DayLog) —
        /// саме його показав QA-скріншот. Core не знає про "справжня смерть у
        /// тактичному бою" (Gameplay-концепт), тож підміна — тут, на межі,
        /// де GameSession уже має і ready-to-log топік, і справжній стан
        /// ростера. Чіпає ЛИШЕ ці два топіки — усі інші сигнали проходять без
        /// змін.
        /// </summary>
        private string AdjustPassVanguardTopicIfMaksymDead(string topicId)
        {
            if (string.IsNullOrEmpty(topicId)) return topicId;
            bool good = string.Equals(topicId, "incident.pass_vanguard.Good", StringComparison.Ordinal);
            bool worst = string.Equals(topicId, "incident.pass_vanguard.Worst", StringComparison.Ordinal);
            if (!good && !worst) return topicId;

            bool maksymDead = _state?.Roster.Get(PassVanguardOutcome.MaksymId)?.IsDead == true;
            return maksymDead ? topicId + "_dead" : topicId;
        }

        private void SettleAfterDayReport(DayReport report)
        {
            if (report == null) return;

            if (report.AwaitsDecision)
            {
                _currentPending = report.Pending;
                State = SessionState.Decision;
                return;
            }

            _currentPending = null;

            if (report.Phase == DayPhase.Day)
            {
                State = SessionState.Evening;
                return;
            }

            // Доба справді завершена рівно тут: ніч дороблена (Phase != Day
            // вище вже відсіяв денний перехід у Evening), а AwaitsDecision
            // false — жодного нічного рішення не лишилось нерозв'язаним.
            // Рівно раз на календарну добу, як і задокументовано в
            // DefectionWatch.Tick.
            TickDefectionWatch();
            TickCompanionArcs();

            if (!_freePlay && _processor.CurrentDay >= 5 && !_summaryAcknowledged)
            {
                State = SessionState.Summary;
                return;
            }

            State = _freePlay ? SessionState.FreePlay : SessionState.Morning;
            // Автосейв щоранку (R13) — і в FreePlay теж: раніше гейт `!_freePlay`
            // вимикав автосейв назавжди одразу після фіналу доби 5, хоча
            // SaveState тепер (той самий фікс) дозволяє FreePlay так само, як
            // Morning.
            AutoSave();
        }

        /// <summary>
        /// US-9.4/R2 (§2 №25): раз на добу рахує підряд-дні на дні лояльності
        /// і дефектить, хто набрав поріг (<see cref="Defection.ShouldDefect"/>)
        /// — або кого вже посіяно сюжетним прапором «defector_seeded» (вузол 1,
        /// <see cref="PassVanguardOutcome"/>) при полосі ≤ Resentful. Раніше
        /// цей крок не звав ніхто (seamsForD1 пакета B4) — DefectionWatch.Tick
        /// не викликався взагалі, тож дефекція не траплялась ніколи, хай яка
        /// низька лояльність.
        /// </summary>
        private void TickDefectionWatch()
        {
            if (_defectionWatch == null || _worldRoster == null) return;
            _defectionWatch.Tick(_worldRoster);

            // Копія: Defect() знімає з посади і міняє Status під час проходу,
            // тож ітерація по живому Roster.All у момент запису була б
            // небезпечною.
            var candidates = new List<Companion>(_worldRoster.All);
            bool seeded = _flags.Get(Defection.DefectorSeededFlag);
            foreach (var c in candidates)
            {
                if (string.Equals(c.Id, ProtagonistId, StringComparison.Ordinal)) continue;

                // Поправка №7.8: коли зерно зради посіяно (defector_seeded) для
                // напарника, у якого є СЦЕНАРНА конфронтація («Нічна розмова»,
                // OfferMyroslavaEveningScene), автоматична дефекція чекає на
                // її розв'язку — інакше сцена ніколи не встигла б статися
                // (ShouldDefect повертає true одразу за прапором, задовго до
                // доби 3). Щойно конфронтація розв'язана (байдуже, якою
                // гілкою) — прапор знято, і звичний шлях знову діє.
                if (seeded && string.Equals(c.Id, "myroslava", StringComparison.Ordinal) &&
                    !_flags.Get(CompanionScenes.MyroslavaConfrontationResolvedFlag))
                    continue;

                // M1.2: хто завершив особисту арку, свій конфлікт розв'язав — не зраджує (стан арки, не прапор).
                if (IsArcCompleted(c.Id)) continue;

                int days = _defectionWatch.DaysAtOrBelowResentful(c.Id);
                if (!Defection.ShouldDefect(c, isProtagonist: false, days, seeded, _cfg)) continue;

                Defection.Defect(c, _state);
                LogEvent("companion.defected", Args("companionId", c.Id));
                var ripple = new RosterDrama(new RosterBonds(null), _cfg).OnBetrayal(_worldRoster, c.Id);
                LogRipple(ripple);
            }
        }

        /// <summary>
        /// Major-фікс ревью (§2 №27): щоденний перерахунок гейту особистих арок
        /// напарників — CompanionArcRun.Refresh() сигналізує ПОВЕРНЕННЯМ true,
        /// що глава щойно стала доступною вперше (не через поллінг State), і
        /// саме тут ця точка сигналу нарешті має слухача.
        /// </summary>
        private void TickCompanionArcs()
        {
            if (_arcRuns == null || _worldRoster == null) return;
            foreach (var run in _arcRuns)
            {
                var companion = _worldRoster.Get(run.Arc.CompanionId);
                if (companion == null) continue;
                if (run.Refresh(companion))
                    LogEvent("arc.chapter_opened", Args("companionId", run.Arc.CompanionId, "arcId", run.Arc.Id,
                        "chapterId", run.CurrentChapter?.Id ?? string.Empty,
                        "chapterTitleKey", run.CurrentChapter?.TitleKey ?? string.Empty));
            }
        }

        // =====================================================================
        // Поправка №7.8: глави арок ПРОГРАЮТЬСЯ (Begin → сцена/квест →
        // CompleteChapter), а не лише сигналізують "arc.chapter_opened".
        // =====================================================================

        /// <summary>
        /// Чи завершив напарник особисту арку (обидві глави пройдено). M1.2: читач замість прапорів
        /// <c>arc_*_done</c> — стан самого проходження (<see cref="ArcState.Completed"/>), що й так живе в зліпку.
        /// Тримає захист від зради (<see cref="TickDefectionWatch"/>, <see cref="BuildFinalePlan"/>) і рядки підсумку.
        /// </summary>
        private bool IsArcCompleted(string companionId) => FindArcRun(companionId)?.State == ArcState.Completed;

        private CompanionArcRun FindArcRun(string companionId)
        {
            if (_arcRuns == null || string.IsNullOrEmpty(companionId)) return null;
            foreach (var run in _arcRuns)
                if (string.Equals(run.Arc.CompanionId, companionId, StringComparison.Ordinal)) return run;
            return null;
        }

        /// <summary>Чи стала глава арки цього напарника доступною (гейт лояльності/прогресу пройдено, Begin ще не викликаний). Дані — bool, не View, тож R17 не застосовний.</summary>
        public bool IsArcChapterAvailable(string companionId) => FindArcRun(companionId)?.State == ArcState.Available;

        /// <summary>Чи зміст доступної глави — сцена (BeginArcChapterScene). false, якщо глава квестова (BeginArcChapterQuest) або недоступна.</summary>
        public bool IsArcChapterSceneContent(string companionId)
        {
            var run = FindArcRun(companionId);
            var chapter = run?.CurrentChapter;
            return chapter != null && CompanionArcContent.SceneFor(companionId, chapter.Id) != null;
        }

        /// <summary>Чи зміст доступної глави — квест (BeginArcChapterQuest).</summary>
        public bool IsArcChapterQuestContent(string companionId)
        {
            var run = FindArcRun(companionId);
            var chapter = run?.CurrentChapter;
            return chapter != null && CompanionArcContent.IsQuestChapter(companionId, chapter.Id);
        }

        /// <summary>
        /// Починає доступну главу арки, зміст якої — сцена: Begin() гейту,
        /// потім звичайна BeginScene/AdvanceScene. На фініші сцени
        /// (BuildSceneStepView) глава сама завершується — це і є "проиграна",
        /// а не лише "відкрита" (Поправка №7.8, п. 3 «ARCS PLAYABLE»).
        /// </summary>
        public SceneStepView BeginArcChapterScene(string companionId)
        {
            RequireAnyState(SessionState.Morning, SessionState.Evening, SessionState.Night, SessionState.FreePlay);
            var run = FindArcRun(companionId);
            if (run == null) throw new InvalidOperationException("У напарника «" + companionId + "» немає арки.");

            var companion = _worldRoster.Get(companionId);
            if (!run.Begin(companion)) throw new InvalidOperationException("Глава арки недоступна.");

            var chapter = run.CurrentChapter;
            var scene = chapter != null ? CompanionArcContent.SceneFor(companionId, chapter.Id) : null;
            if (scene == null) throw new InvalidOperationException("Ця глава — квестова (BeginArcChapterQuest), не сценова.");

            LogEvent("arc.chapter_begun", Args("companionId", companionId, "arcId", run.Arc.Id, "chapterId", chapter.Id,
                "chapterTitleKey", chapter.TitleKey ?? string.Empty));

            SessionState returnState = State;
            _activeArcCompanionId = companionId;
            BeginScene(scene, returnState);
            return AdvanceScene();
        }

        /// <summary>
        /// Починає доступну квестову главу арки (наразі лише Максим, гл. 1
        /// «Не за кров»): Begin() гейту, реєструє власне визначення квесту в
        /// пулі (воно НЕ входить у стартовий <c>DefaultQuests.All</c> — саме
        /// тому загальний <c>OfferQuestStage</c> не міг би запустити його,
        /// обійшовши гейт арки), пропонує перший етап. Термінал квесту
        /// (ResolveQuestChoice) завершує главу — успіх ЧИ невдача, глава
        /// ПРОГРАНА, а не лише виграна.
        /// </summary>
        public QuestOfferView BeginArcChapterQuest(string companionId)
        {
            RequireAnyState(SessionState.Morning, SessionState.Evening, SessionState.Night, SessionState.FreePlay);
            var run = FindArcRun(companionId);
            if (run == null) throw new InvalidOperationException("У напарника «" + companionId + "» немає арки.");

            var companion = _worldRoster.Get(companionId);
            if (!run.Begin(companion)) throw new InvalidOperationException("Глава арки недоступна.");

            var chapter = run.CurrentChapter;
            if (chapter == null || !CompanionArcContent.IsQuestChapter(companionId, chapter.Id))
                throw new InvalidOperationException("Ця глава — сценова (BeginArcChapterScene), не квестова.");

            if (_quests.DefinitionOf(chapter.QuestId) == null)
                _quests.RegisterPool(new[] { DefaultQuests.MaksymCh1(_cfg) });

            LogEvent("arc.chapter_begun", Args("companionId", companionId, "arcId", run.Arc.Id, "chapterId", chapter.Id,
                "chapterTitleKey", chapter.TitleKey ?? string.Empty));
            _activeArcChapterQuestCompanion[chapter.QuestId] = companionId;
            return OfferQuestStage(chapter.QuestId);
        }

        private void CompleteArcChapterFor(string companionId)
        {
            var run = FindArcRun(companionId);
            if (run == null || run.IsFinished) return;
            string arcId = run.Arc.Id;
            string chapterId = run.CurrentChapter != null ? run.CurrentChapter.Id : null;
            // Ключ назви глави їде в подію поруч із id: стрічка показує назву,
            // а не службовий "ch1" (id глави унікальний лише всередині арки).
            string chapterTitleKey = run.CurrentChapter != null ? run.CurrentChapter.TitleKey : null;
            run.CompleteChapter();
            LogEvent("arc.chapter_completed", Args("companionId", companionId, "arcId", arcId, "chapterId", chapterId ?? string.Empty,
                "chapterTitleKey", chapterTitleKey ?? string.Empty));
        }

        /// <summary>
        /// Вузол доби 3 (Поправка №7.8, п. 2c): якщо зерно зради Мирослави
        /// посіяно (<c>Defection.DefectorSeededFlag</c>, вузол 1) — «Нічна
        /// розмова», інакше — звичайна перевірка стосунків («якщо нікого
        /// зрада не насуває — сцена Мирослави стає довірчою»). Одноразово;
        /// null, якщо не доба 3 або вже розв'язано.
        ///
        /// Прапор посіяно — єдина умова, полоса лояльності НЕ перевіряється
        /// повторно: той самий контракт, що й <see cref="TickDefectionWatch"/>,
        /// де seeded-Мирослава пропускає звичайний поріг ШОДНЯ, поки
        /// конфронтація не розв'язана. Інакше пасивний бонус «Morale» від
        /// council_seat (<see cref="LoyaltyRules.OnMorale"/>) встигає підняти
        /// полосу з Resentful до Wary вже на добу 2 — і насувана зрада тихо
        /// розчинилась би, так і не показавши гравцю «Нічну розмову».
        /// </summary>
        public SceneStepView OfferMyroslavaEveningScene()
        {
            RequireAnyState(SessionState.Morning, SessionState.Evening, SessionState.Night, SessionState.FreePlay);
            if (_processor.CurrentDay != 3) return null;
            if (_flags.Get(CompanionScenes.MyroslavaConfrontationResolvedFlag)) return null;

            var myroslava = _worldRoster?.Get("myroslava");
            if (myroslava == null) return null;

            SessionState returnState = State;
            _flags.Set(CompanionScenes.MyroslavaConfrontationResolvedFlag);

            bool imminent = _flags.Get(Defection.DefectorSeededFlag)
                && !myroslava.IsDead && myroslava.Status != CompanionStatus.Antagonist;

            // M1.2: вибір у главі 1 арки (доба 2) зсуває пороги нічної розмови (CompanionScenes.ConfrontationThresholds).
            var scene = imminent
                ? CompanionScenes.MyroslavaConfrontation(
                    trustedInCh1: _flags.Get(CompanionScenes.MyroslavaTrustedFlag),
                    watchedInCh1: _flags.Get(CompanionScenes.MyroslavaWatchedFlag),
                    sentAwayInCh1: _flags.Get(CompanionScenes.MyroslavaSentAwayFlag))
                : CompanionScenes.MyroslavaTrustCheckup();
            LogEvent(imminent ? "scene.betrayal_confrontation.begun" : "scene.trust_checkup.begun",
                Args("companionId", "myroslava"));

            BeginScene(scene, returnState);
            return AdvanceScene();
        }

        /// <summary>Рада Захара перед фіналом (доба 5, увечері) — готує тихий/кривавий шлях. Одноразово; null, якщо не доба 5 або вже розв'язано.</summary>
        public SceneStepView OfferZakharCouncilScene()
        {
            RequireAnyState(SessionState.Morning, SessionState.Evening, SessionState.Night, SessionState.FreePlay);
            if (_processor.CurrentDay != 5) return null;
            if (_flags.Get(CompanionScenes.ZakharCouncilDoneFlag)) return null;

            SessionState returnState = State;
            _flags.Set(CompanionScenes.ZakharCouncilDoneFlag);
            LogEvent("scene.zakhar_council.begun");
            BeginScene(CompanionScenes.ZakharCouncil(), returnState);
            return AdvanceScene();
        }

        /// <summary>
        /// Аудит П9/G25: підсумок циклу (<see cref="ProductionStep.LastReport"/>)
        /// нікуди не йшов — ні у стрічку подій (П9: "жодних production.*"),
        /// ні в лояльність (G25: пасивний бонус "Morale" з council_seat
        /// накопичувався в CycleReport.PassiveBonuses і губився).
        /// <see cref="LoyaltyRules.OnMorale"/> — готовий, але не викликаний
        /// метод з B4, чекав саме цього виклику (seamsForD1).
        /// </summary>
        private void ApplyCycleReport(CycleReport report)
        {
            if (report == null) return;

            foreach (var kv in report.Produced)
                LogEvent("production.resource", Args("resource", kv.Key.ToString(),
                    "amount", kv.Value.ToString(CultureInfo.InvariantCulture)));

            foreach (var id in report.LeveledUp)
                LogEvent("production.leveled_up", Args("companionId", id));

            foreach (var id in report.Recovered)
                LogEvent("production.recovered", Args("companionId", id));

            if (report.FoodShortage)
                LogEvent("production.food_shortage");

            // R11 (seamsForD1 B7): постова XP протагоніста вже НЕ витрачена
            // автоматично (BaseState.AdvanceCycle тепер зве GainXpNoAutoSpend
            // для нього) — тут банкуємо очки тим самим шляхом, що й бойова/
            // квестова/інцидентна XP (GrantXp), інакше рівень піднявся б, а
            // очок для BuildPlanner так і не з'явилось.
            if (report.ProtagonistLevelsGained > 0)
            {
                _points.Grant(ProtagonistId, report.ProtagonistLevelsGained * _cfg.SkillPointsPerLevel);
                var protagonist = _worldRoster?.Get(ProtagonistId);
                if (protagonist != null)
                    LogEvent("progression.level_up", Args("companionId", ProtagonistId, "level", protagonist.Level.ToString(CultureInfo.InvariantCulture)));
            }

            LogLoyaltyChanges(LoyaltyRules.OnMorale(report, _worldRoster, _cfg));
        }

        private void AutoSave()
        {
            try
            {
                _slots[-1] = ComposeSave();
                AutosaveVersion++;
            }
            catch { /* автосейв best-effort — провал не повинен рвати денний конвеєр */ }
        }

        /// <summary>
        /// Росте з кожним ранковим автосейвом. Ядро файлів не пише (див.
        /// <see cref="PreloadSlot"/>), тому оболонка звіряє це число між кадрами
        /// й записує <see cref="AutosaveBlob"/> на диск, щойно воно змінилось.
        /// Без цього автосейв жив лише в пам'яті процесу, і «Продовжити» після
        /// перезапуску гри його не бачило (дебаг 25.09.2026).
        /// </summary>
        public int AutosaveVersion { get; private set; }

        /// <summary>Останній ранковий автосейв цієї сесії або null, якщо його ще не було.</summary>
        public string AutosaveBlob => _slots.TryGetValue(-1, out var blob) ? blob : null;

        /// <summary>Пости, які вже відкриті (будівля, що їх відкриває, стоїть) — тільки на них можна призначити людину.</summary>
        private List<string> OpenPostIds()
        {
            var open = new List<string>();
            foreach (var slot in _state.Slots)
                if (slot.Unlocked) open.Add(slot.Id);
            return open;
        }

        private void TickExpeditionReturnIfAny()
        {
            if (!_party.IsAway) return;
            bool arrived = _party.TickDay();
            if (!arrived) return;

            ExpeditionResult result;
            var returned = _party.Return(_state, out result);
            ExpeditionRunner.Complete(_state, result, _works);
            // Поправка №12.5 (MECH-03, сигнал ресурсу): гравець бачить, ЩО
            // принесла саме ця точка — інакше різниця між руїнами (будівельний)
            // і майстернею (крафтовий) лишалась би невидимою.
            LogEvent("expedition.returned", Args("siteId", result?.SiteId, "band", result?.Band.ToString(),
                "gold", (result?.Gold ?? 0).ToString(CultureInfo.InvariantCulture),
                "build", (result?.BuildComponent ?? 0).ToString(CultureInfo.InvariantCulture),
                "craft", (result?.CraftComponent ?? 0).ToString(CultureInfo.InvariantCulture)));

            // Поправка №15.1: тихий/силовий резолв — теж "вилазка відбулась".
            TryBringSpecialistFromExpedition(result?.SiteId);

            // R8 (seamsForD1): вилазка — віха Готовності, що трапляється ПОЗА
            // конвеєром дня (ReadinessTickStep її не бачить), тож зараховує
            // безпосередньо D1 — лише за Хорошою/Найкращою полосою виходу
            // (ReadinessBalance.ExpeditionSuccessAmount, за коментарем поля).
            if (result != null && result.Band >= OutcomeBand.Good)
                _readiness.Add(_cfg.Readiness.ExpeditionSuccessAmount);

            // §2 рядок 19 ("Лут ... за полосою"): звичайна вилазка (не данж)
            // теж крапає гір, детерміновано за полосою виходу — той самий
            // авторський пул, що і скрізь (R1, без жодного кубика).
            if (result != null && result.Band > OutcomeBand.Worst)
            {
                var drop = DefaultItems.DropTable().Roll(result.Band);
                if (drop != null)
                {
                    _inventory.Add(drop);
                    LogEvent("loot.dropped", Args("itemId", drop.Definition.Id, "named", drop.Definition.IsNamed ? "1" : "0"));
                }
            }
        }

        private static OutcomeBand FindBand(DayReport report, string incidentId)
        {
            if (report?.Incidents != null)
                foreach (var outcome in report.Incidents)
                    if (string.Equals(outcome.IncidentId, incidentId, StringComparison.Ordinal))
                        return outcome.Band;
            return OutcomeBand.Worst;
        }

        private DayReportView BuildDayReportView(DayReport report)
        {
            if (report == null) return null;
            return new DayReportView
            {
                Day = report.Day,
                Phase = report.Phase,
                Signals = report.Signals,
                Incidents = report.Incidents,
                Pending = BuildPendingOfferView(report.Pending)
            };
        }

        private PendingOfferView BuildPendingOfferView(PendingDecision pd)
        {
            if (pd == null) return null;

            // pd.TopicId несе префікс "incident." (для ключів тексту), а
            // ResolveIncident розпізнає вузол 1 за pd.IncidentId (сирий,
            // без префіксу) — та сама пара полів, порівнюємо тим самим.
            bool isNode1 = string.Equals(pd.IncidentId, "pass_vanguard", StringComparison.Ordinal);
            var options = new List<DecisionOptionView>();
            foreach (var opt in pd.Options)
            {
                options.Add(new DecisionOptionView
                {
                    Path = (IncidentPathView)(int)opt.Path,
                    SkillKey = opt.Skill.Id,
                    Threshold = opt.Threshold,
                    Form = opt.Form.ToString(),
                    BestActorId = opt.BestActorId,
                    HasCandidate = opt.HasCandidate,
                    ExpectedBand = opt.ExpectedBand.ToString(),
                    // Ціль 6 «Рішення»: вузол 1 криваво — ЗАВЖДИ тактичний бій
                    // (Node1BloodyEnemyIds, не перевірка), а не поріг навички —
                    // гравець має побачити це в самому тексті варіанту.
                    TacticalBattleEnemyCount = (isNode1 && opt.Path == IncidentPath.Bloody) ? Node1BloodyEnemyIds.Length : 0
                });
            }

            return new PendingOfferView
            {
                Kind = pd.IsCrisis ? "Crisis" : "Incident",
                TopicId = pd.TopicId,
                IsCrisis = pd.IsCrisis,
                Options = options
            };
        }

        private static string CrowdBandName(int band)
        {
            switch (band)
            {
                case 0: return "Hamlet";
                case 1: return "Village";
                case 2: return "Settlement";
                case 3: return "Town";
                default: return "City";
            }
        }

        private ExpeditionSite FindSite(string id)
        {
            foreach (var s in DefaultSites.All())
                if (string.Equals(s.Id, id, StringComparison.Ordinal)) return s;
            return null;
        }

        private List<ISettlementActor> ResolveActors(IReadOnlyList<string> ids)
        {
            var result = new List<ISettlementActor>();
            if (ids == null) return result;
            foreach (var id in ids)
            {
                var c = _worldRoster.Get(id);
                if (c != null) result.Add(new Base.CompanionActorAdapter(c, id == ProtagonistId, _cfg));
            }
            return result;
        }

        /// <summary>
        /// Єдиний застосувач наслідку (Поправка №7.8): та сама
        /// <see cref="QuestConsequence"/>, що й квести, тепер несе наслідок
        /// вибору в портретній сцені й завершення глави арки напарника —
        /// один тип даних, один застосувач, а не три копії тієї самої логіки
        /// (Напруга/фракції/лояльність/флаги/предмети/XP), розкидані по
        /// квестовому, сценовому й арковому шляху окремо.
        /// </summary>
        private void ApplyConsequence(QuestConsequence c, string sourceId = "quest")
        {
            if (c == null || c.IsEmpty) return;
            if (c.TensionDelta != 0) _processor.QueueExternal(TensionDriver.QuestChoice, c.TensionDelta);
            foreach (var kv in c.FactionDeltas) ApplyFactionDelta(kv.Key, kv.Value);
            foreach (var kv in c.LoyaltyDeltas) LogLoyaltyChange(ApplyLoyaltyDelta(kv.Key, kv.Value, sourceId));
            foreach (var flag in c.Flags) _flags.Set(flag);
            foreach (var itemId in c.ItemIds) { GrantNamedItemById(itemId); LogEvent("loot.dropped", Args("itemId", itemId, "named", "1")); }
            foreach (var buildingId in c.BuildingIds) GrantBuildingFromConsequence(buildingId);
            if (c.Xp != 0) GrantXp(ProtagonistId, c.Xp);

            ApplyHafiyaGrassBonusToSickChildIfNeeded();
            ApplyBargainedTimeBonusIfNeeded();
            ApplyMyroslavaHintBonusIfNeeded();
        }

        /// <summary>
        /// Поправка №12.7: будівля з наслідку рішення (перша будівля після
        /// прологу, або будівля-нагорода) стає ОДРАЗУ і без ціни — «перше
        /// спільне зусилля громади». Її пост відкривається, і на нього стає
        /// свій іменний (<see cref="OpeningScenes.FirstBuildingKeeperOf"/>), якщо
        /// той вільний і вдома: інакше вибір лишився б без видимого наслідку
        /// до першого ручного призначення (Статут MECH-05). Обидві зміни звучать
        /// у стрічці окремими подіями — жодної тихої зміни міста (MECH-13).
        /// Поправка №12.9: Сторожа (<see cref="DefaultBuildingsType.Watch"/>)
        /// OpensSlotId не має — метод просто зупиняється після
        /// "city.granted", нікого не переставляючи (Захар уже на
        /// council_seat з FirstHourWorld.Build).
        /// </summary>
        private void GrantBuildingFromConsequence(string buildingId)
        {
            if (_works == null || _state == null) return;
            if (!_works.GrantBuilt(buildingId, _state)) return;
            LogEvent("city.granted", Args("buildingId", buildingId));

            var def = DefaultBuildingsType.Get(buildingId);
            string slotId = def?.OpensSlotId;
            string keeperId = OpeningScenes.FirstBuildingKeeperOf(buildingId);
            if (string.IsNullOrEmpty(slotId) || string.IsNullOrEmpty(keeperId)) return;

            var keeper = _worldRoster?.Get(keeperId);
            // Поправка №12.10: будівля, зведена через ЗВИЧАЙНЕ будівництво
            // (не вибір першої будівлі) — напр. Склад пізніше за золото —
            // не мусить силоміць заселяти фахівця, який не прибився до гурту.
            if (keeper == null || keeper.Status == CompanionStatus.NotArrived ||
                !string.IsNullOrEmpty(keeper.AssignedSlotId)) return;
            if (_state.TryAssign(keeperId, slotId) == AssignmentResult.Success)
                LogEvent("city.granted.staffed", Args("companionId", keeperId, "slotId", slotId));
        }

        /// <summary>
        /// Поправка №12.10 (пул прибульців): відповідь Тугарові — останній із
        /// двох виборів, які визначають, хто прибився до гурту (другий —
        /// передісторія, вже застосована на момент цього виклику —
        /// <see cref="BeginOpeningScene"/> читає її з <c>_pendingBackgroundId</c>
        /// ДО побудови сцени). Двоє прибульців лишаються
        /// <see cref="CompanionStatus.Idle"/> (як і раніше — чекають своєї
        /// будівлі), двоє інших фахівців із пулу стають
        /// <see cref="CompanionStatus.NotArrived"/> — назавжди для цього
        /// прогону: ніде не з'являються (пости, перевірки, ростер).
        /// Ідемпотентно неявно: <c>ChooseSceneOption(tugar_offer_choice)</c>
        /// у сцені трапляється рівно раз.
        /// </summary>
        private void ApplyArrivalsPool(string tugarChoiceOptionId)
        {
            var arrivals = ArrivalsPool.Determine(_pendingBackgroundId, tugarChoiceOptionId);
            foreach (var specialistId in ArrivalsPool.AllSpecialistIds)
            {
                var c = _worldRoster?.Get(specialistId);
                if (c == null) continue;
                c.Status = arrivals.Contains(specialistId) ? CompanionStatus.Idle : CompanionStatus.NotArrived;
            }
            LogEvent("arrivals.resolved", Args("fromBackground", arrivals.FromBackground, "fromTugar", arrivals.FromTugar));
        }

        /// <summary>
        /// Сумісність зі старими зліпками (до Поправки №12.10, маркер
        /// "arrivals=1" у ComposeSave відсутній): у допульній грі Гобан-Сайр
        /// і Синдбад не існували взагалі, тож RosterAdapter.RestoreState не
        /// знаходить для них "ros="-запису і лишає обох на дефолтному Idle
        /// щойно збудованого ростера — виглядало б так, наче вони "прибилися"
        /// до гурту, хоча відповіді Тугарові в тій партії не було ніколи.
        /// Чесний відновлений стан старого зліпка: Дід Овсій і Гафія
        /// присутні (як завжди були, і "ros=" зберіг їхній справжній статус
        /// вище), Гобан-Сайр і Синдбад — <see cref="CompanionStatus.NotArrived"/>.
        /// </summary>
        private void CompatApplyArrivalsPoolForOldSave()
        {
            foreach (var specialistId in new[] { ArrivalsPool.GobanId, ArrivalsPool.SindbadId })
            {
                var c = _worldRoster?.Get(specialistId);
                if (c == null) continue;
                if (c.Status == CompanionStatus.Dead || c.Status == CompanionStatus.Antagonist) continue;
                c.Status = CompanionStatus.NotArrived;
            }
        }

        /// <summary>
        /// Чи прибився фахівець із пулу (<see cref="ArrivalsPool"/>) цього
        /// прогону. До того, як вирішено (`ChooseSceneOption` на
        /// <see cref="OpeningScenes.TugarOfferChoiceId"/>) — ростер ще не
        /// поставив нікого в NotArrived, тож <c>true</c> за замовчуванням
        /// (усі четверо існують у ростері з дефолтним Idle до розв'язки).
        /// </summary>
        private bool IsSpecialistArrived(string specialistId)
        {
            var c = _worldRoster?.Get(specialistId);
            return c == null || c.Status != CompanionStatus.NotArrived;
        }

        // ================= Поправка №15.1: пізніше приєднання =================
        //
        // Хто НЕ прибився на старті (ApplyArrivalsPool) не пропав назавжди —
        // приходить пізніше ТРЬОМА шляхами, кожен детермінований:
        // Таверна (ProcessTavernArrivalSchedule, день відомий заздалегідь),
        // зустріч на вилазці (TryBringSpecialistFromExpedition, за точкою
        // ArrivalsPool.ExpeditionSiteOf), рада: прийом переселенців
        // (ProcessSettlersArrivalIfAny). Усі три сходяться в один перехід —
        // BringSpecialistIn — і його ж власний захист "уже прибув" не дає
        // одному фахівцю прийти двічі різними шляхами.

        /// <summary>
        /// Наступний відсутній фахівець за порядком пула
        /// (<see cref="ArrivalsPool.AllSpecialistIds"/> — keeper, healer,
        /// goban, sindbad), або null, якщо відсутніх більше немає. Порядок
        /// пула — те саме, чим Таверна і переселенці ради вирішують, ХТО
        /// саме прийде наступним (на відміну від вилазки — там фахівець
        /// прив'язаний до СВОЄЇ точки, а не до черги).
        /// </summary>
        private string NextAbsentSpecialistId()
        {
            if (_worldRoster == null) return null;
            foreach (var id in ArrivalsPool.AllSpecialistIds)
            {
                var c = _worldRoster.Get(id);
                if (c != null && c.Status == CompanionStatus.NotArrived) return id;
            }
            return null;
        }

        /// <summary>
        /// Приводить фахівця з <see cref="CompanionStatus.NotArrived"/> у
        /// <see cref="CompanionStatus.Idle"/> (той самий перехід, що вже
        /// робить <see cref="ApplyArrivalsPool"/> на старті) і оголошує подію
        /// з іменем (<c>eventKey</c> — один із "arrivals.tavern"/
        /// "arrivals.settlers"/"arrivals.expedition", текст —
        /// UkrainianText.cs, {companion} через ScreenText.ResolveCompanionName,
        /// як і "arrivals.resolved"). No-op (без події, false), якщо він уже
        /// прибув іншим шляхом раніше цього самого прогону — "один фахівець —
        /// один раз" тримається саме тут, в ОДНІЙ точці переходу, а не в
        /// кожному з трьох викликачів окремо.
        /// </summary>
        private bool BringSpecialistIn(string specialistId, string eventKey)
        {
            var c = _worldRoster?.Get(specialistId);
            if (c == null || c.Status != CompanionStatus.NotArrived) return false;
            c.Status = CompanionStatus.Idle;
            LogEvent(eventKey, Args("companionId", specialistId));
            return true;
        }

        /// <summary>
        /// Шлях «зустріч на вилазці»: відповідний фахівець, прив'язаний до
        /// <paramref name="siteId"/> (<see cref="ArrivalsPool.ExpeditionSiteOf"/>),
        /// приєднується до гурту, коли загін повертається САМЕ звідти —
        /// будь-яким підходом (тихий/силовий резолв ЧИ данж: усі чотири
        /// місця, де партія повертається — TickExpeditionReturnIfAny,
        /// ExtractDungeon, AbandonDungeon, FinishDungeonCombat-wipe — звуть
        /// цей метод). siteId — <c>ExpeditionResult.SiteId</c> для тихого/
        /// силового підходу або <c>DungeonRun.SiteId</c> для делву.
        /// </summary>
        private void TryBringSpecialistFromExpedition(string siteId)
        {
            if (string.IsNullOrEmpty(siteId) || _worldRoster == null) return;
            foreach (var id in ArrivalsPool.AllSpecialistIds)
            {
                if (string.Equals(ArrivalsPool.ExpeditionSiteOf(id), siteId, StringComparison.Ordinal))
                {
                    BringSpecialistIn(id, "arrivals.expedition");
                    return;
                }
            }
        }

        /// <summary>
        /// Шлях «рада: прийом переселенців» — коли <c>CityWorks.OrderSettlers</c>
        /// справді дає прихід людей у місто (<see cref="Base.CityWorks.
        /// LastSettlersArrivalDay"/> дорівнює поточній добі, а не лише
        /// замовлений — сама черга/відкат лишаються CityWorks-турботою),
        /// разом із ними приходить наступний відсутній фахівець пула, якщо є.
        /// Викликається рівно раз на добу, з <see cref="AdvanceDay"/>, одразу
        /// після <see cref="Game.Core.Base.SettlementCycle.AdvanceDay"/>
        /// (єдиного способу рухати час — CLAUDE.md).
        /// </summary>
        private void ProcessSettlersArrivalIfAny()
        {
            if (_works == null || _processor == null) return;
            if (_works.LastSettlersArrivalDay != _processor.CurrentDay) return;

            string next = NextAbsentSpecialistId();
            if (next != null) BringSpecialistIn(next, "arrivals.settlers");
        }

        /// <summary>
        /// Шлях «Таверна»: доба приходу оголошується ЗАЗДАЛЕГІДЬ, у день
        /// добудови Таверни (<see cref="Base.DefaultBuildings.Tavern"/>) і
        /// знов після кожного такого приходу, поки хтось іще відсутній.
        /// Викликається рівно раз на добу, з <see cref="AdvanceDay"/>: (1)
        /// якщо сьогодні — саме той день, приводить запланованого (сам
        /// перехід у <see cref="BringSpecialistIn"/> мовчки не подвоює
        /// прихід, якщо того самого фахівця вже привів інший шлях раніше);
        /// (2) якщо зараз нікого не заплановано, Таверна стоїть і хтось іще
        /// відсутній — планує НАСТУПНОГО (порядок пула) на
        /// <see cref="Balance.CityBalance.TavernSpecialistArrivalDays"/> діб
        /// наперед (ПЛЕЙСХОЛДЕР) і оголошує це подією — гравець знає ім'я і
        /// день ДО приходу («у таверні кажуть»).
        /// </summary>
        private void ProcessTavernArrivalSchedule()
        {
            if (_works == null || _processor == null) return;

            if (_pendingTavernSpecialistId != null && _pendingTavernDueDay == _processor.CurrentDay)
            {
                BringSpecialistIn(_pendingTavernSpecialistId, "arrivals.tavern");
                _pendingTavernSpecialistId = null;
            }

            if (_pendingTavernSpecialistId == null && _works.Has(DefaultBuildingsType.Tavern))
            {
                string next = NextAbsentSpecialistId();
                if (next != null)
                {
                    _pendingTavernSpecialistId = next;
                    _pendingTavernDueDay = _processor.CurrentDay + _cfg.City.TavernSpecialistArrivalDays;
                    LogEvent("arrivals.tavern.announced", Args("companionId", next,
                        "days", _cfg.City.TavernSpecialistArrivalDays.ToString(CultureInfo.InvariantCulture)));
                }
            }
        }

        /// <summary>ПЛЕЙСХОЛДЕР: наскільки торг за час (сцена «Сусід з претензією», варіант «bargain») полегшує тихий шлях вузла 1.</summary>
        private const int TugarBargainQuietThresholdRelief = 2;

        /// <summary>Ідемпотентно (як і <see cref="ApplyHafiyaGrassBonusToSickChildIfNeeded"/>): застосовується рівно раз за прогін.</summary>
        private bool _bargainedTimeBonusApplied;

        /// <summary>
        /// Той самий прийом, що <see cref="ApplyHafiyaGrassBonusToSickChildIfNeeded"/>:
        /// сцена сама тільки виставляє прапор (Поправка №7.8), а числове
        /// застосування — тут, поруч із рештою "seamsForD1"-хуків цього
        /// фасаду. Полегшує тихий шлях вузла 1, поки він ще не резолвнутий
        /// (якщо гравець уже пройшов вузол 1 до цієї сцени — бонус тихо не
        /// знаходить об'єкта і не застосовується, що чесно: користь торгу
        /// вже не встигла).
        /// </summary>
        private void ApplyBargainedTimeBonusIfNeeded()
        {
            if (_bargainedTimeBonusApplied) return;
            if (_flags == null || !_flags.Get(OpeningScenes.TugarBargainedTimeFlag)) return;
            if (_processor?.Incidents == null) return;

            foreach (var def in _processor.Incidents.All)
            {
                if (string.Equals(def.Id, "pass_vanguard", StringComparison.Ordinal) &&
                    string.Equals(def.SourceId, "opening.pass", StringComparison.Ordinal))
                {
                    def.QuietPathThreshold = Math.Max(1, def.QuietPathThreshold - TugarBargainQuietThresholdRelief);
                    break;
                }
            }
            _bargainedTimeBonusApplied = true;
        }

        /// <summary>ПЛЕЙСХОЛДЕР (M1.2): наскільки підказка Мирослави про батька полегшує тихий шлях вузла 1 — слабше за торг.</summary>
        private const int MyroslavaHintQuietThresholdRelief = 1;

        /// <summary>Ідемпотентно, як і <see cref="_bargainedTimeBonusApplied"/>: рівно раз за прогін, скидається в NewGame.</summary>
        private bool _myroslavaHintBonusApplied;

        /// <summary>
        /// M1.2: читач прапора <see cref="OpeningScenes.MyroslavaHintFlag"/>. Якщо на розмові з Мирославою
        /// (відкриття, «спитати Мирославу», Good/Best) вона розкрила, чого не договорює батько, тихий шлях
        /// вузла 1 легший на <see cref="MyroslavaHintQuietThresholdRelief"/> — той самий прийом, що й торг
        /// (<see cref="ApplyBargainedTimeBonusIfNeeded"/>); поріг видно в прев'ю рішення заздалегідь (інваріант 8).
        /// </summary>
        private void ApplyMyroslavaHintBonusIfNeeded()
        {
            if (_myroslavaHintBonusApplied) return;
            if (_flags == null || !_flags.Get(OpeningScenes.MyroslavaHintFlag)) return;
            if (_processor?.Incidents == null) return;

            foreach (var def in _processor.Incidents.All)
            {
                if (string.Equals(def.Id, "pass_vanguard", StringComparison.Ordinal) &&
                    string.Equals(def.SourceId, "opening.pass", StringComparison.Ordinal))
                {
                    def.QuietPathThreshold = Math.Max(1, def.QuietPathThreshold - MyroslavaHintQuietThresholdRelief);
                    break;
                }
            }
            _myroslavaHintBonusApplied = true;
        }

        /// <summary>
        /// Поправка №7.8: коли варіант конфронтації Мирослави («звинуватити»/
        /// «погрожувати», «відпустити», або невдалий «переконати») щойно
        /// виставив свій прапор, зрада відбувається НЕГАЙНО тим самим шляхом,
        /// що і природний <see cref="TickDefectionWatch"/> — але за рішенням
        /// гравця в сцені, а не мовчки вночі. Успішний «переконати»
        /// (<see cref="CompanionScenes.MyroslavaConfrontedTrustFlag"/>) нічого
        /// не виконує тут: лояльність уже відновлена наслідком сцени.
        /// </summary>
        private void ApplyBetrayalConfrontationSideEffectsIfNeeded()
        {
            if (_flags == null) return;
            if (_flags.Get(CompanionScenes.MyroslavaDefectionExecutedFlag)) return;

            bool shouldDefect =
                _flags.Get(CompanionScenes.MyroslavaConfrontedProvokedFlag) ||
                _flags.Get(CompanionScenes.MyroslavaConfrontedReleaseFlag) ||
                _flags.Get(CompanionScenes.MyroslavaConfrontedFailedFlag);
            if (!shouldDefect) return;

            var c = _worldRoster?.Get("myroslava");
            if (c == null || c.IsDead || c.Status == CompanionStatus.Antagonist) return;

            Defection.Defect(c, _state);
            _flags.Set(CompanionScenes.MyroslavaDefectionExecutedFlag);
            LogEvent("companion.defected", Args("companionId", "myroslava"));
            var ripple = new RosterDrama(new RosterBonds(null), _cfg).OnBetrayal(_worldRoster, "myroslava");
            LogRipple(ripple);
        }

        /// <summary>
        /// Поправка №7.8: рада Захара (доба 5, увечері) готує відповідний
        /// шлях фіналу. «Загатити річку» додає Готовність тим самим числом,
        /// що указ ради «Готуватись» (<c>CityWorks.OrderPrepareThreat</c>) —
        /// нова окрема шкала тут не потрібна (інваріант 6 вже покритий
        /// Готовністю). «Тримати перевал» лишає прапор для
        /// <see cref="ResolveFinale"/>, який пом'якшує кривавий фінал на
        /// одного рядового ворога (softening — так само, як
        /// <see cref="CompanionScenes.MyroslavaConfrontedReleaseFlag"/> м'якшить
        /// присутність зрадниці).
        /// </summary>
        private void ApplyZakharCouncilSideEffectsIfNeeded()
        {
            if (_flags == null) return;
            if (_flags.Get(CompanionScenes.ZakharPreparedDamFlag) && !_flags.Get("zakhar_dam_bonus_applied"))
            {
                _readiness.Add(_cfg.Readiness.PrepareThreatAmount);
                _flags.Set("zakhar_dam_bonus_applied");
            }
            // ZakharPreparedAssaultFlag сам по собі нічого не рахує тут —
            // його читає ResolveFinale (softening кривавого штурму).
        }

        /// <summary>
        /// Seam B6→D1 (seamsForD1, TEST_BUILD.md §5 рядок B6): трава Гафії
        /// (<see cref="DefaultQuests.HafiyaGrassFoundFlag"/>) полегшує поріг
        /// тихого шляху інциденту <c>sick_child</c> (доба 3) на
        /// <see cref="Game.Core.Balance.QuestBalance.HafiyaGrassBonusToSickChild"/>.
        /// OpeningContent.cs (A1) НЕ чіпаємо — <c>IncidentDefinition</c> об'єкт
        /// мутабельний і живе єдиним екземпляром у <c>_processor.Incidents</c> на
        /// весь прогін (FirstHourWorld.Build викликає OpeningContent.All() рівно
        /// раз), тож зменшення поля тут — застосування "чужого" числа своїм кодом,
        /// а не редагування чужого файлу. Ідемпотентно (<see cref="_hafiyaGrassBonusApplied"/>)
        /// — і на випадок Save/Load ДО доби 3 (прапор персистується, а IncidentTable
        /// ні, тож без повторного виклику при ApplySave бонус губився б).
        /// </summary>
        private void ApplyHafiyaGrassBonusToSickChildIfNeeded()
        {
            if (_hafiyaGrassBonusApplied) return;
            if (_flags == null || !_flags.Get(DefaultQuests.HafiyaGrassFoundFlag)) return;
            if (_processor?.Incidents == null) return;

            // Id "sick_child" НЕ унікальний у таблиці: DefaultIncidents.BuildTable()
            // заводить свій загальний вуличний "sick_child" (SourceId="street",
            // поза сюжетом), а OpeningContent.SickChild() — окремий сюжетний
            // інцидент доби 3 (SourceId="opening.child", ScriptedSource з тим
            // самим Id) з ІНШИМ порогом. Матчити лише по Id — застосувати бонус
            // не до того об'єкта (перший знайдений — вуличний, не сюжетний).
            foreach (var def in _processor.Incidents.All)
            {
                if (string.Equals(def.Id, "sick_child", StringComparison.Ordinal) &&
                    string.Equals(def.SourceId, "opening.child", StringComparison.Ordinal))
                {
                    def.QuietPathThreshold = Math.Max(1, def.QuietPathThreshold - _cfg.Quest.HafiyaGrassBonusToSickChild);
                    break;
                }
            }

            _hafiyaGrassBonusApplied = true;
        }

        private void GrantXp(string companionId, int amount)
        {
            var c = _worldRoster.Get(companionId);
            if (c == null || amount <= 0) return;

            if (string.Equals(companionId, ProtagonistId, StringComparison.Ordinal))
            {
                var result = c.GainXpNoAutoSpend(amount, _cfg);
                if (result.LeveledUp)
                {
                    _points.Grant(ProtagonistId, result.LevelsGained * _cfg.SkillPointsPerLevel);
                    LogEvent("progression.level_up", Args("companionId", ProtagonistId, "level", c.Level.ToString(CultureInfo.InvariantCulture)));
                }
            }
            else
            {
                var result = c.GainXp(amount, _cfg);
                if (result.LeveledUp)
                    LogEvent("progression.level_up", Args("companionId", companionId, "level", c.Level.ToString(CultureInfo.InvariantCulture)));
            }
        }

        /// <summary>
        /// Фікс (25.09.2026, той самий клас бага, що <see cref="LogFactionBandChange"/>
        /// лагодить у фракцій): раніше визнавався ЛИШЕ жорстко зашитий id
        /// "scout_horn" — будь-який інший іменний предмет (напр. готовий
        /// <c>AegisPlate</c>, ніде не викликаний поза тестами) мовчки не
        /// потрапляв у інвентар. Тепер визначення шукається генерично через
        /// <see cref="DefaultItems.AllDefinitions"/> (той самий каталог, що
        /// вже використовує <c>Inventory.RestoreState</c> для відновлення
        /// сейву) — видається БУДЬ-ЯКИЙ знайдений предмет. Бонус
        /// forewarn_boost лишається окремою гілкою ПІСЛЯ загального видавання:
        /// це унікальний для scout_horn код, який Items package свідомо не
        /// вміє застосувати сам (<see cref="ItemWorldEffect"/>), і додавати
        /// generic-обробку world-ефектів тут поза обсягом цього фіксу.
        /// </summary>
        private void GrantNamedItemById(string itemId)
        {
            var definition = DefaultItems.AllDefinitions()
                .Find(d => string.Equals(d.Id, itemId, StringComparison.Ordinal));
            if (definition == null) return;

            _inventory.Add(ItemInstance.NamedFrom(definition));

            if (!string.Equals(itemId, "scout_horn", StringComparison.Ordinal)) return;

            // seamsForD1 B3 (ефект «forewarn_boost», D1b): "наступні 2
            // передвісники — раніше/легше" застосовується через адитивний шов
            // WorldPulse.BoostCharge на єдиний Announces-накопичувач кампанії
            // (Тугар, §3.3) — детально в ItemBalance.ScoutHornForewarnBoostPerCharge.
            //
            // Фікс-ревью D1b (мінор): подія логується, лише якщо BoostCharge
            // реально щось приклав (int applied > 0) — у вузькому вікні, де
            // Тугар уже впритул до Threshold, клямп у WorldPulse зрізає весь
            // буст, і "forewarn_boosted" без цієї перевірки обіцяв би ефект,
            // якого не було (R17 ховає саме число, але не назву події).
            int boost = _cfg.Items.ScoutHornForewarnCharges * _cfg.Items.ScoutHornForewarnBoostPerCharge;
            int applied = _processor?.Pulse?.BoostCharge(OpeningContent.TuharSourceId, boost) ?? 0;
            if (applied > 0)
                LogEvent("item.scout_horn.forewarn_boosted", Args("charges", _cfg.Items.ScoutHornForewarnCharges.ToString(CultureInfo.InvariantCulture)));
        }

        private void ApplyFactionDelta(string factionId, int delta)
        {
            var standing = _factions.Get(factionId);
            if (standing == null) return;
            var before = standing.Band;
            _factions.ApplySocialConsequence(factionId, delta);
            if (standing.Band != before)
                LogEvent("faction.standing_changed", Args("factionId", factionId, "band", standing.Band.ToString()));
        }

        private LoyaltyChange ApplyLoyaltyDelta(string companionId, int delta, string sourceId)
        {
            var c = _worldRoster.Get(companionId);
            return c == null ? default(LoyaltyChange) : c.ApplyLoyaltyDelta(delta, sourceId);
        }

        private void LogLoyaltyChange(LoyaltyChange change)
        {
            if (!change.BandChanged) return;
            LogEvent("loyalty.band_changed", Args("companionId", change.CompanionId, "band", change.To.ToString()));
        }

        private void LogLoyaltyChanges(List<LoyaltyChange> changes)
        {
            if (changes == null) return;
            for (int i = 0; i < changes.Count; i++) LogLoyaltyChange(changes[i]);
        }

        private void ApplyBattleCasualties(BattleResult result)
        {
            if (result.Casualties == null) return;
            foreach (var cas in result.Casualties)
            {
                if (string.IsNullOrEmpty(cas.CompanionId)) continue;

                if (cas.Dead)
                {
                    _rosterAdapter?.Kill(cas.CompanionId);
                    HandleCompanionDeath(cas.CompanionId);
                }
                else if (cas.Downed || cas.HpLost > 0)
                {
                    var tier = cas.Downed ? WoundTier.Serious : WoundTier.Light;
                    LogScarIfGranted(cas.CompanionId, _rosterAdapter?.WoundReporting(cas.CompanionId, cas.HpLost * 5.0, tier));
                }
            }
        }

        /// <summary>
        /// Єдина точка довершення смерті (незалежно від того, хто вже позначив
        /// <see cref="CompanionStatus.Dead"/> — бій вище чи IncidentResolver
        /// всередині DayProcessor.Advance): повернути гір у загальний склад
        /// (<see cref="Inventory.RecoverGearFrom"/>, інакше найменований предмет
        /// зникає назавжди — seamsForD1 B3) і сповістити подіями "companion.died"
        /// та "roster.rippled". Companion.MarkDead() гір не чіпає, тож на момент
        /// виклику знаряддя ще на персонажі.
        /// </summary>
        private void HandleCompanionDeath(string companionId)
        {
            if (string.IsNullOrEmpty(companionId)) return;
            var companion = _worldRoster?.Get(companionId);
            if (companion != null) _inventory?.RecoverGearFrom(companion);
            LogEvent("companion.died", Args("companionId", companionId));
            var ripple = new RosterDrama(new RosterBonds(null), _cfg).OnDeath(_worldRoster, companionId);
            LogRipple(ripple);
        }

        /// <summary>
        /// Фікс-ревью (полірування, ціль 5 «Якість стрічки»): ключ обирається
        /// з типу зв'язку (Kinship/Friction/Neutral) × причини (загибель/
        /// зрада) — шість варіантів у UkrainianText, кожен використовує
        /// companionId (хто реагує) і triggerId (хто загинув/зрадив), а не
        /// один безликий рядок на всю ряб.
        /// </summary>
        private void LogRipple(RippleReport report)
        {
            if (report == null) return;
            string suffix = report.Betrayal ? "betrayal" : "death";
            foreach (var effect in report.Effects)
                LogEvent("roster.rippled." + effect.Bond.ToString().ToLowerInvariant() + "." + suffix,
                    Args("companionId", effect.CompanionId, "triggerId", report.TriggerId,
                        "kinship", effect.Bond.ToString(), "band", effect.Band.ToString()));
        }

        private Combat.PlayerUnitSource ResolvePlayerUnit(string companionId)
        {
            var c = _worldRoster.Get(companionId);
            if (c == null) return null;
            bool protect = string.Equals(companionId, ProtagonistId, StringComparison.Ordinal) && !_ironman;
            return new Combat.PlayerUnitSource(c, ResolveWeaponFor(c), protect);
        }

        /// <summary>
        /// Поправка №19.2: надіта зброя = зброя в бою (<c>ItemDefinition.CombatWeaponId</c>).
        /// Без надітої (або в предмета немає бойового відповідника) — як і раніше, евристика за
        /// скілом (ПЛЕЙСХОЛДЕР зрізу, §9).
        /// </summary>
        private static Combat.WeaponDefinition ResolveWeaponFor(Companion c)
        {
            if (c == null) return DefaultCombatContent.HordeSpear();
            string equippedWeapon = c.Equipment.Get(EquipSlot.Weapon)?.Definition.CombatWeaponId;
            Combat.WeaponDefinition fromGear;
            if (!string.IsNullOrEmpty(equippedWeapon) && DefaultCombatContent.PlayerWeaponCatalog().TryGetValue(equippedWeapon, out fromGear))
                return fromGear;
            int melee = c.Skill(Stats.SkillType.Melee);
            int ranged = c.Skill(Stats.SkillType.Ranged);
            return ranged > melee ? DefaultCombatContent.HordeBow() : DefaultCombatContent.HordeSpear();
        }

        private static readonly Dictionary<string, EnemyDefinition> EnemyCatalog = DefaultCombatContent.EnemyCatalog();

        private static EnemyDefinition ResolveEnemyById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            EnemyDefinition def;
            if (EnemyCatalog.TryGetValue(id, out def)) return def;
            if (EnemyCatalog.TryGetValue("enemy." + id, out def)) return def;
            return null;
        }

        /// <summary>
        /// Фікс-ревью (блокер): чи може цей напарник ще стояти на грід —
        /// той самий вирок, що <c>Base.CompanionActorAdapter.IsPresentInSettlement</c>
        /// уже дає постам і вилазці (не OnMission/Dead/Antagonist). Досі не
        /// існувало жодної точки, що перевіряла це для АВТОСКЛАДЕНОЇ партії
        /// (вузол 1/фінал не дають гравцю обирати склад — на відміну від
        /// вилазки/данжу, де ExpeditionPartyLegality відсікає це ще в Gameplay
        /// до самого виклику).
        /// </summary>
        private bool IsCompanionBattleReady(string companionId)
        {
            var c = _worldRoster?.Get(companionId);
            return c != null && new Game.Core.Base.CompanionActorAdapter(c).IsPresentInSettlement;
        }

        /// <summary>
        /// Поле бою кімнати: шаблон арени за <c>ArenaKey</c> (Поправка №14.4 — стіни,
        /// перепони, бочки, сіно, місця загону й ворогів, підкріплення), або
        /// генератор, якщо шаблону немає.
        /// </summary>
        private BattleSetup BuildRoomBattleSetup(DungeonRoomDefinition room, IReadOnlyList<string> partyIds, BattleOpening opening,
                                                 IReadOnlyList<string> enemyIdsOverride = null)
        {
            // enemyIdsOverride — хто лишився після розмови перед боєм (docs/ABILITIES.md §4.6).
            var enemyIds = enemyIdsOverride ?? room.EnemyIds;
            if (ArenaTemplates.TryGet(room.ArenaKey, out var rows))
            {
                var reinforcements = new List<(int, string)>();
                foreach (var r in room.Reinforcements) reinforcements.Add((r.Round, r.EnemyId));
                return ArenaTemplates.Build(rows, partyIds, enemyIds, _hitRule, opening, reinforcements);
            }
            return BuildBattleSetup(partyIds, enemyIds, 8, 8, opening: opening);
        }

        /// <summary>Старт бою мовою данжу → варіант бою (Поправка №14.1).</summary>
        internal static BattleOpening ToBattleOpening(DungeonBattleStart start)
        {
            switch (start)
            {
                case DungeonBattleStart.Ambush: return BattleOpening.Ambush;
                case DungeonBattleStart.Spotted: return BattleOpening.Spotted;
                case DungeonBattleStart.UnderFire: return BattleOpening.UnderFire;
                case DungeonBattleStart.Encounter: return BattleOpening.Encounter;
                case DungeonBattleStart.Provoked: return BattleOpening.Provoked;
                default: return BattleOpening.FirstStrike;
            }
        }

        private BattleSetup BuildBattleSetup(IReadOnlyList<string> partyIds, IReadOnlyList<string> enemyIds,
            int width, int height, string defectorCompanionId = null, BattleOpening opening = BattleOpening.Encounter)
        {
            // Захист від переповнення сітки: розстановка нижче кладе кожного
            // юніта на свій рядок (py/ey += 2, старт з 1) — фіксований height
            // (8 для вузла 1/данжу, 10 для фіналу за доком) міг не вміщати
          // зрадника ПІСЛЯ повного списку ворогів фіналу (AssaultEnemyCountByBand
            // + Бурунда), і AddUnit падав "тайл зайнятий/непрохідний" на позиції
            // за межами сітки. Висота тому рахується як мінімум запиту й
            // фактичної потреби — детерміновано, без жодної магії.
            int neededEnemyRows = (enemyIds?.Count ?? 0) + (!string.IsNullOrEmpty(defectorCompanionId) ? 1 : 0);
            int neededPartyRows = partyIds?.Count ?? 0;
            int neededRows = System.Math.Max(neededPartyRows, neededEnemyRows);
            height = System.Math.Max(height, neededRows * 2 + 1);

            var setup = new BattleSetup { Width = width, Height = height, HitRule = _hitRule, Opening = opening };
            if (width > 3 && height > 2)
                setup.Cover.Add(new CoverPlacement(new GridPos(width / 2, height / 2), Direction.West, CoverType.Half));

            int py = 1;
            if (partyIds != null)
                for (int i = 0; i < partyIds.Count; i++)
                {
                    setup.PlayerUnits.Add(new PlayerSpawn(partyIds[i], PartySpawnPos(i, ref py, width, height, opening)));
                }

            int nextEnemyRow = PlaceEnemyFormation(setup, enemyIds, width);

            if (!string.IsNullOrEmpty(defectorCompanionId))
            {
                setup.DefectorCompanionId = defectorCompanionId;
                // Зрадник стає окремим "рядом" за тим самим стовпцем A, що й
                // останній парний ворог — так само отримує укриття (owner:
                // "sensible formations with cover"), а не голе поле.
                int x = width - 2;
                var pos = new GridPos(x, nextEnemyRow);
                setup.DefectorPos = pos;
                setup.Cover.Add(new CoverPlacement(pos, Direction.West, CoverType.Half));
            }

            return setup;
        }

        /// <summary>
        /// Де стає i-й боєць загону. Звичайно — стовпцем біля лівого краю. «Оточені»
        /// (Поправка №14.1) — врозкид: лівий край, верхній і нижній край середини
        /// поля, щоб вороги мали фланги. Детерміновано і лівіше ворожих стовпців
        /// (<see cref="PlaceEnemyFormation"/> ставить їх на width−2 і width−4).
        /// </summary>
        private static GridPos PartySpawnPos(int index, ref int py, int width, int height, BattleOpening opening)
        {
            if (opening == BattleOpening.Surrounded && width >= 6 && height >= 4)
            {
                int midX = System.Math.Max(2, width / 2 - 1);
                switch (index % 3)
                {
                    case 1: return new GridPos(midX, 0);
                    case 2: return new GridPos(midX, height - 1);
                }
            }
            var pos = new GridPos(1, py);
            py += 2;
            return pos;
        }

        /// <summary>
        /// Полірування (ціль 3 «Бойові декорації», owner: "Enemy deployments
        /// must be sensible formations with cover (not a single column)").
        /// Раніше ВСІ ворожі юніти стояли одним прямим стовпцем x=width-2, а
        /// укриття на всю арену було ОДНЕ, декоративне, у центрі мапи —
        /// нікого конкретно не захищало (CoverPlacement живе на тайлі
        /// ЗАХИСНИКА, GridMap.CoverAgainst, а не на сусідньому тайлі).
        ///
        /// Тепер — зигзаг у ДВА стовпці (A=width-2, B=width-4): парні індекси
        /// (0,2,4,…) — стовпець A, непарні (1,3,5,…) — стовпець B, ряд
        /// зростає кожні дві позиції.
        ///
        /// Укриття — навмисно НЕ на кожному ворозі: емпірично перевірено
        /// (Row33_Overwatch_Triggered_UnderBloodyPolicy, 5×15-денний
        /// бот-прогін), що укриття на КОЖНОМУ ворозі змінює бойовий розрахунок
        /// ІІ настільки, що дозор жодного разу не спрацьовує за весь прогін —
        /// AI (Core/Combat/CombatAi.cs, крок 8 "TryImprovePosition") починає
        /// щоразу знаходити "кращу позицію" замість того, щоб дійти до кроку 9
        /// (дозор як останній засіб). Непарний індекс (Full-укриття) —
        /// найменша зміна, що й дає формацію "не один стовпець", і зберігає
        /// дозор спостережуваним. Повертає наступний вільний ряд Y у
        /// стовпці A (для зрадника фіналу).
        /// </summary>
        private static int PlaceEnemyFormation(BattleSetup setup, IReadOnlyList<string> enemyIds, int width)
        {
            if (enemyIds == null || enemyIds.Count == 0) return 1;

            int colA = width - 2;
            int colB = width - 4 >= 3 ? width - 4 : colA; // замалі арени — деградуємо до одного стовпця, а не негативних X

            for (int i = 0; i < enemyIds.Count; i++)
            {
                int pairIndex = i / 2;
                int y = 1 + pairIndex * 2;
                int x = (i % 2 == 0 || colB == colA) ? colA : colB;
                var pos = new GridPos(x, y);

                setup.EnemyUnits.Add(new EnemySpawn(enemyIds[i], pos));
                if (i % 2 == 1)
                    setup.Cover.Add(new CoverPlacement(pos, Direction.West, CoverType.Full));
            }

            int lastPairIndex = (enemyIds.Count - 1) / 2;
            return 1 + (lastPairIndex + 1) * 2; // наступний вільний ряд після останньої пари
        }

        /// <summary>
        /// ПЛЕЙСХОЛДЕР-правило мапінгу BattleResult→OutcomeBand (§9): специфікація
        /// не фіксує його явно для жодного з трьох реальних боїв (вузол 1/данж/
        /// фінал) — рішення інтегратора, застосоване однаково для всіх трьох:
        /// Victory без втрат → Best; Victory з даун/смертю → Base; Victory без
        /// даун/смерті, але з ранами → Good; Defeat → Worst; Retreat/Draw → Base.
        /// Відступ перекриває <see cref="OnBattleResolved"/>: поле за ворогом, Worst (Поправка №14.7).
        /// </summary>
        private static OutcomeBand MapBattleBand(BattleResult r)
        {
            if (r.Outcome == BattleOutcome.Defeat) return OutcomeBand.Worst;
            if (r.Outcome != BattleOutcome.Victory) return OutcomeBand.Base;
            if (r.Casualties == null || r.Casualties.Count == 0) return OutcomeBand.Best;

            bool severe = false;
            for (int i = 0; i < r.Casualties.Count; i++)
                if (r.Casualties[i].Dead || r.Casualties[i].Downed) { severe = true; break; }
            return severe ? OutcomeBand.Base : OutcomeBand.Good;
        }

        // ---- сейв власного фрагмента GameSession (§4.8) ----

        private string ComposeSave()
        {
            var head = new System.Text.StringBuilder("gs1");
            head.Append(";state=").Append((int)State);
            head.Append(";protagonist=").Append(ProtagonistId);
            head.Append(";seed=").Append(_seed.ToString(CultureInfo.InvariantCulture));
            head.Append(";hitRule=").Append((int)_hitRule);
            head.Append(";tensionPace=").Append(_tensionPace ? 1 : 0);
            // Які етапи квестів уже прозвучали як «Нова пропозиція» — частина
            // видимої стрічки: без цього після завантаження кожна ще відкрита
            // пропозиція з'являлась у стрічці вдруге.
            var offersLogged = new List<string>(_loggedQuestOfferKeys);
            offersLogged.Sort(StringComparer.Ordinal);
            head.Append(";offersLogged=").Append(string.Join(",", offersLogged));

            // Фікс-ревью (Поправка №7.8, журнал механік тестера): раніше
            // _seenEventKeys НІКОЛИ не потрапляв у сейв — ContinueGame() іде
            // крізь NewGame(SkipCreation:true), яка БЕЗУМОВНО чистить його
            // (свіжий прогін — порожній журнал), а вже ПОТІМ LoadState читає
            // цей самий зліпок. Реальний плейтест 25.09.2026 (MechanicsJournal-
            // CompletionTests) зловив наслідок: гравець, що зберігся й
            // завантажився з головного екрана (єдиний UI-шлях), бачив
            // «Журнал механік» порожнім заново — увесь прогрес, накопичений
            // ДО збереження, тихо губився, хоча сама партія (доба/ростер/
            // будівлі) відновлювалась коректно. Ключі подій безпечні для
            // ',' — самі events лише [a-z0-9._] (LogEvent), той самий
            // принцип, що й offersLogged вище.
            var journalSeen = new List<string>(_seenEventKeys);
            journalSeen.Sort(StringComparer.Ordinal);
            head.Append(";journalSeen=").Append(string.Join(",", journalSeen));
            if (_roller != null) head.Append(";roller=").Append(_roller.CaptureState());
            head.Append(";resume=").Append(_resume == null ? "-" :
                ((int)_resume.Reason).ToString(CultureInfo.InvariantCulture) + "|" +
                ((int)_resume.ReturnState).ToString(CultureInfo.InvariantCulture));
            head.Append(";freeplay=").Append(_freePlay ? 1 : 0);
            head.Append(";summary=").Append(_summaryAcknowledged ? 1 : 0);
            // Фікс-ревью (журнал механік тестера): той самий трап, що
            // journalSeen= вище, окремим полем — night_patrol читає ЦЕЙ
            // прапорець напряму (ExtraSeen), не через _seenEventKeys, і
            // раніше НІКОЛИ не потрапляв у сейв. Гравець, що патрулював ніч
            // 1, зберігся й завантажив партію з титулу, бачив "Патруль"
            // знову непобаченим — журнал і партія розходились між собою.
            head.Append(";patrolled=").Append(_patrolledANight ? 1 : 0);
            // Дебаг 25.09.2026 (аудит сейвів): ironman і фаза доби не входили в
            // зліпок. Після «Продовжити» ContinueGame будує гру з
            // NewGameOptions за замовчуванням — захист протагоніста від смерті
            // мовчки вмикався знову, а заголовок показував «день» замість ночі.
            head.Append(";ironman=").Append(_ironman ? 1 : 0);
            head.Append(";phase=").Append((int)_lastPhase);
            head.Append(";finale=").Append(_finaleResolved ? 1 : 0);
            head.Append(";readiness=").Append(_readiness.CaptureState());
            head.Append(";quests=").Append(_quests.CaptureState());
            head.Append(";factions=").Append(_factions.CaptureState());
            head.Append(";points=").Append(_points.CaptureState());

            // Фікс-ревью D2 (major): особисті арки напарників (_arcRuns/
            // _arcFlags) раніше НІКОЛИ не потрапляли в сейв — NewGame(), крізь
            // який іде кожен ContinueGame()/LoadState()/RestoreFromBlob(),
            // завжди скидав кожну арку в ArcState.Locked/ChapterIndex=0 і
            // чистив _arcFlags, тож будь-яке проміжне збереження мовчки
            // губило прогрес арки: наступний TickCompanionArcs() бачив
            // "Locked" там, де ДО сейву вже було Available/InProgress, і
            // ВІДКРИВАВ ту саму главу ВДРУГЕ (дубль arc.chapter_opened).
            // Формат значення без ';' (щоб не плутати з роздільником полів
            // заголовка вище — той самий принцип, що й у items=/core=, лише
            // без довжина-префіксу, бо тут немає символу '^' усередині):
            // "<arcId>:<state>:<chapterIndex>,..." — '~' — "<flag>,...".
            head.Append(";arc=").Append(CaptureArcState());

            // Фаза F (UI-tour autoplay): ім'я/рід/передісторія протагоніста
            // (R12, _pendingName/_pendingGender/_pendingBackgroundId) раніше
            // НІКОЛИ не потрапляли в сейв — ContinueGame() кличе NewGame(
            // SkipCreation:true), яка свідомо НЕ чіпає ці поля (лишає їх такими,
            // якими вони були на щойно сконструйованому інстансі), тож
            // GetProtagonistCreationView() після Load мовчки повертав дефолти
            // (Gender.Male/"warrior"/null-ім'я), а не те, що гравець обрав на
            // екрані створення. Ім'я — Base64 (UTF-8): гравець вільний ввести
            // будь-які символи в textField (включно з ';'/'='), а формат сейву —
            // рядок полів через ';'.
            head.Append(";pname=").Append(_pendingName == null ? "-" : System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(_pendingName)));
            head.Append(";pgender=").Append((int)_pendingGender);
            head.Append(";pbg=").Append(_pendingBackgroundId ?? "");
            // Поправка №19.3: зовнішність героя; Base64, бо всередині запису є ';' і '='. "-" — дефолтна.
            head.Append(";papp=").Append(_pendingAppearance == null ? "-" :
                System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(_pendingAppearance.Encode())));

            // Поправка №12.10 (пул прибульців): маркер "цей зліпок написаний
            // кодом, що вже знає про Гобана-Сайра/Синдбада". Старі зліпки
            // (до цього поля) не мають "ros="-запису для жодного з двох —
            // RosterAdapter.RestoreState просто не знаходить їх у рядку і
            // лишає обох на дефолтному Idle свіжозбудованого ростера, тобто
            // "присутні", хоча в допульній грі вони взагалі не існували.
            // ApplySave читає маркер і за його відсутності відкочує обох на
            // NotArrived — див. CompatApplyArrivalsPoolForOldSave.
            head.Append(";arrivals=1");

            // Поправка №15.1 (пізніше приєднання, шлях «Таверна»): чи
            // заплановано прихід, і на яку добу — переживає збереження тим
            // самим "-"-сентинелом, що "resume=" вище. Старий зліпок без
            // цього поля читається як "нікого не заплановано" (ApplySave
            // лишає значення, які щойно поставив NewGame) — наступний
            // AdvanceDay сам перепланує, якщо Таверна стоїть і хтось іще
            // відсутній.
            head.Append(";tavernNext=").Append(_pendingTavernSpecialistId == null ? "-" :
                _pendingTavernSpecialistId + ":" + _pendingTavernDueDay.ToString(CultureInfo.InvariantCulture));

            // Довжина-префікс (як і "core=" нижче): Inventory.CaptureState() сам
            // з'єднує предмети через ';' (Inventory.cs), тож наївний
            // headPart.Split(';') у ApplySave інакше сплутав би роздільник
            // предметів із роздільником полів заголовка і губив усі предмети,
            // крім першого (аудит: сташ 2+ предметів після Save/Load).
            string itemsBlob = _inventory.CaptureState();
            head.Append(";items=").Append(itemsBlob.Length.ToString(CultureInfo.InvariantCulture)).Append('^').Append(itemsBlob);

            // Поправка №19.3 (знахідка 07.10.2026): надіте спорядження раніше НІКОЛИ не потрапляло в
            // сейв — після Save/Load речі зникали з людей. Формат: "<id>><предмети ItemCodec>|...",
            // довжина-префікс — бо всередині ';' (той самий прийом, що й items=).
            string gearBlob = CaptureGear();
            head.Append(";gear=").Append(gearBlob.Length.ToString(CultureInfo.InvariantCulture)).Append('^').Append(gearBlob);

            head.Append(";defect=").Append(_defectionWatch.CaptureState());
            // Поправка №14.2: полонені, ті, хто чекає рішення після бою, і переманені.
            head.Append(";prisoners=").Append(_prisoners.CaptureState());
            head.Append(";recruits=").Append(string.Join("/", _recruits));
            head.Append(";surr=").Append(CapturePendingSurrenders());
            // Поправка №14.7: наші бранці і загін, на який іде рейд.
            head.Append(";captives=").Append(_captives.CaptureState());
            head.Append(";raid=").Append(_raidGroupId ?? "-");
            // Поправка №14.6: досьє ворогів.
            head.Append(";dossier=").Append(_dossier.CaptureState());
            head.Append(";bribe=").Append(CaptureBribeRefusals());
            head.Append(";crisis=").Append(_crisis.CaptureState());

            string coreBlob = _processor.SaveState();
            head.Append(";core=").Append(coreBlob.Length.ToString(CultureInfo.InvariantCulture)).Append('^').Append(coreBlob);
            return head.ToString();
        }

        private string CapturePendingSurrenders()
        {
            var parts = new List<string>();
            foreach (var e in _pendingSurrenders)
                parts.Add(e.UnitId.Replace('#', '!') + "," + e.EnemyDefinitionId + ","
                    + System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(e.DisplayName ?? "")).Replace('=', '~') + ","
                    + ((int)e.Rank).ToString(CultureInfo.InvariantCulture) + "," + (e.NeverRecruitable ? "1" : "0"));
            return string.Join("/", parts);
        }

        private void RestorePendingSurrenders(string value)
        {
            _pendingSurrenders.Clear();
            if (string.IsNullOrEmpty(value)) return;
            foreach (var entry in value.Split('/'))
            {
                var f = entry.Split(',');
                if (f.Length < 5) continue;
                _pendingSurrenders.Add(new SurrenderedEnemy
                {
                    UnitId = f[0].Replace('!', '#'),
                    EnemyDefinitionId = f[1],
                    DisplayName = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(f[2].Replace('~', '='))),
                    Rank = (EnemyRank)ParseInt(f[3]),
                    NeverRecruitable = f[4] == "1"
                });
            }
        }

        /// <summary>Переманені — назад у ростер ДО відновлення ядра: ростер відновлює стан лише наявних напарників.</summary>
        private void RestoreRecruits(string value)
        {
            _recruits.Clear();
            if (string.IsNullOrEmpty(value)) return;
            foreach (var entry in value.Split('/'))
            {
                var f = entry.Split(',');
                if (f.Length < 2) continue;
                string name = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(f[1].Replace('~', '=')));
                var companion = BuildRecruit(f[0], name, _recruits.Count);
                if (companion != null && _worldRoster.Get(companion.Id) == null) _worldRoster.Add(companion);
                _recruits.Add(entry);
            }
        }

        /// <summary>Поле "tensionPace" із заголовка зліпка (до ";core="), або null для старого сейву без нього.</summary>
        private static bool? PeekTensionPace(string blob)
        {
            if (string.IsNullOrEmpty(blob)) return null;
            int coreIdx = blob.IndexOf(";core=", StringComparison.Ordinal);
            string head = coreIdx >= 0 ? blob.Substring(0, coreIdx) : blob;
            const string key = ";tensionPace=";
            int idx = head.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return null;
            int start = idx + key.Length;
            return start < head.Length && head[start] == '1';
        }

        private void ApplySave(string blob)
        {
            // Слот з іншим темпом Напруги, ніж світ цієї партії: світ
            // перебудовується тим самим шляхом, що й «Продовжити» (NewGame +
            // застосування зліпка), — пороги смуг і накопичувач кризи живуть
            // у побудові світу, а не в зліпку. Слоти переживають перебудову.
            bool? savedPace = PeekTensionPace(blob);
            if (savedPace.HasValue && savedPace.Value != _tensionPace)
            {
                var keptSlots = new Dictionary<int, string>(_slots);
                NewGame(new NewGameOptions
                {
                    SkipCreation = true, HitRule = _hitRule, Roller = _roller,
                    TestBuildTensionPace = savedPace.Value,
                    TestBuildOneDayConstruction = _works == null || _works.OneDayConstruction
                });
                foreach (var kv in keptSlots) _slots[kv.Key] = kv.Value;
            }

            int coreIdx = blob.IndexOf(";core=", StringComparison.Ordinal);
            string headPart = coreIdx >= 0 ? blob.Substring(0, coreIdx) : blob;
            string corePart = null;

            if (coreIdx >= 0)
            {
                int afterKey = coreIdx + ";core=".Length;
                int caret = blob.IndexOf('^', afterKey);
                int len = ParseInt(blob.Substring(afterKey, caret - afterKey));
                corePart = blob.Substring(caret + 1, len);
            }

            // "items=" так само довжина-префіксований (ComposeSave) і так само
            // вирізається ЦІЛИМ фрагментом ДО наївного Split(';') нижче — інакше
            // ';' усередині Inventory.CaptureState() (роздільник предметів)
            // сплутався б із роздільником полів заголовка (той самий фікс, що
            // й для "core=").
            string gearPart = CutLengthPrefixed(ref headPart, ";gear=");

            string itemsPart = null;
            int itemsIdx = headPart.IndexOf(";items=", StringComparison.Ordinal);
            if (itemsIdx >= 0)
            {
                int afterKey = itemsIdx + ";items=".Length;
                int caret = headPart.IndexOf('^', afterKey);
                int len = ParseInt(headPart.Substring(afterKey, caret - afterKey));
                itemsPart = headPart.Substring(caret + 1, len);
                int afterItems = caret + 1 + len;
                headPart = headPart.Substring(0, itemsIdx) + headPart.Substring(afterItems);
            }

            // Фікс-ревью (Поправка №7.8, save/load): квестова глава арки
            // (наразі лише Максим ч.1 «Не за кров») реєструє своє
            // QuestDefinition у пулі _quests ЛІНИВО, лише всередині
            // BeginArcChapterQuest — а сейв несе тільки questId (§4.8, як і
            // решта визначень контенту). Без цього кроку ";quests=" нижче
            // (QuestLog.RestoreState) мовчки викидає прогін цього квесту на
            // СВІЖОМУ інстансі (RestoreFromBlob/ContinueGame): визначення в
            // його пулі ще нема, тож "quest.maksym.ch1" з сейву тихо зникає,
            // і глава арки лишається InProgress НАЗАВЖДИ (CompleteArcChapterFor
            // ніколи не викликається — лінк questId→companionId теж не
            // персистився). Читаємо "arc=" ДО основного проходу нижче — саме
            // тому, що порядок полів у ComposeSave ставить "quests=" ПЕРЕД
            // "arc=", а реєстрація повинна встигнути ДО RestoreState квестів.
            int arcIdx = headPart.IndexOf(";arc=", StringComparison.Ordinal);
            if (arcIdx >= 0)
            {
                int afterKey = arcIdx + ";arc=".Length;
                int nextSemi = headPart.IndexOf(';', afterKey);
                string arcValue = nextSemi >= 0 ? headPart.Substring(afterKey, nextSemi - afterKey) : headPart.Substring(afterKey);
                ReattachInProgressArcChapterQuests(arcValue, ExtractHeadField(headPart, ";quests="));
            }

            string offersLoggedValue = null;
            string journalSeenValue = null;
            bool arrivalsPoolMarkerPresent = false;
            foreach (var part in headPart.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string key = part.Substring(0, eq);
                string value = part.Substring(eq + 1);

                switch (key)
                {
                    case "offersLogged": offersLoggedValue = value; break;
                    case "journalSeen": journalSeenValue = value; break;
                    case "state": State = (SessionState)ParseInt(value); break;
                    case "seed": ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _seed); break;
                    case "hitRule": _hitRule = (HitRuleKind)ParseInt(value); break;
                    case "roller": _roller?.RestoreState(value); break;
                    case "resume": _resume = value == "-" ? null : ParseResume(value); break;
                    case "freeplay": _freePlay = value == "1"; break;
                    case "summary": _summaryAcknowledged = value == "1"; break;
                    case "patrolled": _patrolledANight = value == "1"; break;
                    case "ironman": _ironman = value == "1"; break;
                    case "phase": _lastPhase = (DayPhase)ParseInt(value); break;
                    case "finale": _finaleResolved = value == "1"; break;
                    case "readiness": _readiness.RestoreState(value); break;
                    case "quests": _quests.RestoreState(value); break;
                    case "factions": _factions.RestoreState(value); break;
                    case "points": _points.RestoreState(value); break;
                    case "defect": _defectionWatch.RestoreState(value); break;
                    case "prisoners": _prisoners.RestoreState(value); break;
                    case "recruits": RestoreRecruits(value); break;
                    case "surr": RestorePendingSurrenders(value); break;
                    case "captives": _captives.RestoreState(value); break;
                    case "raid": _raidGroupId = value == "-" || value.Length == 0 ? null : value; break;
                    case "dossier": _dossier.RestoreState(value); break;
                    case "bribe": RestoreBribeRefusals(value); break;
                    case "crisis": _crisis.RestoreState(value); break;
                    case "arc": RestoreArcState(value); break;
                    case "pname": _pendingName = value == "-" ? null : System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(value)); break;
                    case "pgender": _pendingGender = (Gender)ParseInt(value); _protagonistGender = _pendingGender; break;
                    case "papp":
                        _pendingAppearance = value == "-" ? null :
                            Appearance.Decode(System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(value)));
                        break;
                    case "pbg": if (!string.IsNullOrEmpty(value)) _pendingBackgroundId = value; break;
                    case "arrivals": arrivalsPoolMarkerPresent = value == "1"; break;
                    case "tavernNext":
                        if (string.IsNullOrEmpty(value) || value == "-")
                        {
                            _pendingTavernSpecialistId = null;
                            _pendingTavernDueDay = 0;
                        }
                        else
                        {
                            int colon = value.IndexOf(':');
                            _pendingTavernSpecialistId = colon >= 0 ? value.Substring(0, colon) : value;
                            _pendingTavernDueDay = colon >= 0 ? ParseInt(value.Substring(colon + 1)) : 0;
                        }
                        break;
                }
            }

            _inventory.RestoreState(itemsPart);
            if (corePart != null) _processor.RestoreState(corePart);
            RestoreGear(gearPart);

            // Блокер-фікс (знайдено 25.09.2026, лід): відновлення в СВІЖИЙ
            // інстанс GameSession розходилось із безперервною грою — пости
            // виробляли не те й не тим. _processor.RestoreState вище щойно
            // повернув Companion.AssignedSlotId (бік напарника, RosterAdapter),
            // але бухгалтерія самого слота (AssignmentSlot.AssignedCompanionId,
            // яку читає BaseState.AdvanceCycle) в жоден слепок не пише і не
            // читається — на свіжому BaseState вона лишається порожньою.
            // RestoreSlotOccupancy пересобирає її з уже відновленого ростера
            // ОДРАЗУ тут, поки обидві сторони синхронні.
            _state?.RestoreSlotOccupancy();

            if (!arrivalsPoolMarkerPresent) CompatApplyArrivalsPoolForOldSave();

            // Доважок до "pname=" вище: RosterAdapter.CaptureState() навмисно
            // НЕ пише DisplayName (коментар у RosterAdapter.cs — ім'я/статі/
            // картки приходять із контенту, дублювати їх у сейві означає
            // одного дня розійтися з ним), тож ім'я, яке гравець ввів на
            // екрані створення, треба повернути на об'єкт протагоніста тут
            // окремо — інакше після Load воно тихо відкочується до дефолтного
            // "Провідник"/"Провідниця" з архетипу, хоча сам рядок уже
            // відновлено в _pendingName. НЕ через ProtagonistCreation.Apply —
            // той перезаписує атрибути/скіли пресетом і стер би прогрес
            // білд-планувальника (R11), якого це поле не стосується.
            var protagonist = _worldRoster?.Get(ProtagonistId);
            if (protagonist != null && !string.IsNullOrEmpty(_pendingName))
                protagonist.DisplayName = _pendingName;

            _currentPending = null;
            _currentQuestOffer = null;
            _loggedQuestOfferKeys.Clear();
            if (!string.IsNullOrEmpty(offersLoggedValue))
                foreach (var offerKey in offersLoggedValue.Split(','))
                    if (offerKey.Length > 0) _loggedQuestOfferKeys.Add(offerKey);

            // Журнал механік переживає Save/Load (див. коментар ComposeSave):
            // Clear() тут ідемпотентний і для свіжого інстансу (RestoreFromBlob
            // у щойно сконструйований GameSession, D1: набір і так порожній), і
            // для ContinueGame (NewGame уже почистив його раніше в тому самому
            // виклику) — так само, як _loggedQuestOfferKeys.Clear() вище.
            _seenEventKeys.Clear();
            if (!string.IsNullOrEmpty(journalSeenValue))
                foreach (var seenKey in journalSeenValue.Split(','))
                    if (seenKey.Length > 0) _seenEventKeys.Add(seenKey);
            _dungeon = null;
            _battle = null;
            _battleAutoResolvedThisCall = false;

            // Флаг міг бути виставлений ДО збереження (квест-етап "grass"
            // резолвиться задовго до доби 3) — порог sick_child не входить у
            // жоден зліпок (визначення інцидентів не персистяться), тож без
            // цього виклику бонус мовчки губився б після Save/Load.
            ApplyHafiyaGrassBonusToSickChildIfNeeded();
            // Той самий випадок для торгу з Тугаром: прапор у зліпку є, а поріг
            // тихого шляху вузла 1 — ні (дебаг 25.09.2026).
            ApplyBargainedTimeBonusIfNeeded();
            // M1.2: підказка Мирослави — теж лише прапор у зліпку, поріг вузла 1 до зліпка не входить.
            ApplyMyroslavaHintBonusIfNeeded();
        }

        /// <summary>
        /// Фікс-ревью D2 (major, доважок до ComposeSave): формат
        /// <c>arcId:state:chapterIndex</c>, через кому — для кожного
        /// <see cref="CompanionArcRun"/> у <see cref="_arcRuns"/>, далі '~' і
        /// прапори через кому — для <see cref="_arcFlags"/>. Жодних ';'
        /// усередині (щоб не плутати з роздільником полів заголовка) —
        /// ідентифікатори арок/прапорів (DefaultArcs.cs) лишень [a-z0-9_],
        /// тому ':'/','/'~' безпечні.
        /// </summary>
        private string CaptureArcState()
        {
            var sb = new System.Text.StringBuilder();
            if (_arcRuns != null)
                for (int i = 0; i < _arcRuns.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var run = _arcRuns[i];
                    sb.Append(run.Arc.Id).Append(':').Append((int)run.State).Append(':')
                      .Append(run.ChapterIndex.ToString(CultureInfo.InvariantCulture));
                }
            sb.Append('~');
            bool first = true;
            foreach (var flag in _arcFlags)
            {
                if (!first) sb.Append(',');
                sb.Append(flag);
                first = false;
            }
            return sb.ToString();
        }

        /// <summary>Зворотне до <see cref="CaptureArcState"/> — за Id зіставляє з уже інстанційованими <see cref="_arcRuns"/> (NewGame() будує їх з DefaultArcs.All() ДО ApplySave) і кличе <see cref="CompanionArcRun.RestoreState"/>; невідомі за старим сейвом без "arc=" поля лишає як є (NewGame-дефолт — Locked/0, зворотна сумісність).</summary>
        private void RestoreArcState(string value)
        {
            if (string.IsNullOrEmpty(value) || _arcRuns == null) return;

            int tilde = value.IndexOf('~');
            string runsPart = tilde >= 0 ? value.Substring(0, tilde) : value;
            string flagsPart = tilde >= 0 ? value.Substring(tilde + 1) : string.Empty;

            if (runsPart.Length > 0)
            {
                foreach (var entry in runsPart.Split(','))
                {
                    var bits = entry.Split(':');
                    if (bits.Length < 3) continue;
                    string arcId = bits[0];
                    var state = (ArcState)ParseInt(bits[1]);
                    int chapterIndex = ParseInt(bits[2]);
                    for (int i = 0; i < _arcRuns.Count; i++)
                    {
                        if (_arcRuns[i].Arc.Id != arcId) continue;
                        _arcRuns[i].RestoreState(state, chapterIndex);
                        break;
                    }
                }
            }

            _arcFlags.Clear();
            if (flagsPart.Length > 0)
                foreach (var flag in flagsPart.Split(','))
                    if (!string.IsNullOrEmpty(flag)) _arcFlags.Add(flag);
        }

        /// <summary>
        /// Фікс-ревью (Поправка №7.8, save/load): читає СИРЕ значення "arc="
        /// (той самий формат, що <see cref="RestoreArcState"/> парсить, але тут
        /// лише читання — <see cref="_arcRuns"/> ще не змінюємо, тільки
        /// дивимось у ЇХНІ вже готові <see cref="CompanionArc.Chapters"/>, щоб
        /// дістати companionId/questId ще ДО RestoreArcState). Для поточної
        /// глави, що сейв лишив InProgress і зміст якої — квест
        /// (<see cref="CompanionArcContent.IsQuestChapter"/>), реєструє
        /// визначення квесту в пулі (той самий прийом, що
        /// <see cref="BeginArcChapterQuest"/>) і відновлює лінк
        /// questId→companionId (<see cref="_activeArcChapterQuestCompanion"/>)
        /// — без цього ";quests=" (QuestLog.RestoreState) тихо відкидає прогін
        /// квеста на свіжому інстансі (визначення в його пулі ще нема), а
        /// навіть якби не відкидав — термінал квеста ніколи не завершив би
        /// главу арки без лінку.
        ///
        /// Блокер-фікс (знайдено 25.09.2026, лід): та сама доля чекала на
        /// квест УЖЕ ЗАВЕРШЕНОЇ глави — <see cref="CompanionArcRun.CompleteChapter"/>
        /// одразу зсуває <c>ChapterIndex</c> і збиває <c>State</c> з InProgress,
        /// тож наступного сейву ця глава для гілки вище вже не InProgress, а
        /// сам <c>QuestRun</c> (термінальна стадія типу "revenge_done") усе
        /// одно лежить у "quests=" — на ЖИВІЙ сесії він і далі читається
        /// (визначення зареєстроване назавжди), а на свіжому інстансі
        /// <see cref="QuestLog.RestoreState"/> тихо відкидає його (визначення
        /// нема в пулі), і подальші дні розходяться з безперервною грою.
        /// Тому нижче реєструємо визначення для КОЖНОЇ квестової глави арки
        /// (не лише поточної InProgress), чий QuestId реально зустрічається
        /// серед записів "quests=" — лінк completion-компаньйона це не чіпає:
        /// його отримує лише поточна InProgress глава, як і раніше.
        /// </summary>
        private void ReattachInProgressArcChapterQuests(string arcValue, string questsValue)
        {
            if (string.IsNullOrEmpty(arcValue) || _arcRuns == null) return;

            int tilde = arcValue.IndexOf('~');
            string runsPart = tilde >= 0 ? arcValue.Substring(0, tilde) : arcValue;
            if (runsPart.Length == 0) return;

            foreach (var entry in runsPart.Split(','))
            {
                var bits = entry.Split(':');
                if (bits.Length < 3) continue;
                string arcId = bits[0];
                var state = (ArcState)ParseInt(bits[1]);
                int chapterIndex = ParseInt(bits[2]);

                for (int i = 0; i < _arcRuns.Count; i++)
                {
                    if (_arcRuns[i].Arc.Id != arcId) continue;
                    var chapters = _arcRuns[i].Arc.Chapters;
                    string companionId = _arcRuns[i].Arc.CompanionId;

                    if (state == ArcState.InProgress)
                    {
                        var chapter = chapterIndex >= 0 && chapterIndex < chapters.Count ? chapters[chapterIndex] : null;
                        if (chapter != null && CompanionArcContent.IsQuestChapter(companionId, chapter.Id))
                        {
                            RegisterArcChapterQuestDefinition(chapter.QuestId);
                            _activeArcChapterQuestCompanion[chapter.QuestId] = companionId;
                        }
                    }

                    // Минулі глави тієї самої арки: лінк не потрібен (їхній
                    // квест уже завершив главу за життя джерельної сесії), але
                    // визначення — потрібне, інакше QuestLog.RestoreState
                    // відкине сам запис прогресу нижче.
                    for (int ci = 0; ci < chapters.Count; ci++)
                    {
                        var pastChapter = chapters[ci];
                        if (!CompanionArcContent.IsQuestChapter(companionId, pastChapter.Id)) continue;
                        if (QuestRunPresentInBlob(questsValue, pastChapter.QuestId))
                            RegisterArcChapterQuestDefinition(pastChapter.QuestId);
                    }
                    break;
                }
            }
        }

        /// <summary>Реєструє визначення квестової глави арки в пулі <see cref="_quests"/>, якщо його там ще нема (ідемпотентно).</summary>
        private void RegisterArcChapterQuestDefinition(string questId)
        {
            if (string.IsNullOrEmpty(questId) || _quests.DefinitionOf(questId) != null) return;
            if (questId == DefaultQuests.MaksymCh1Id)
                _quests.RegisterPool(new[] { DefaultQuests.MaksymCh1(_cfg) });
        }

        /// <summary>Чи є запис <paramref name="questId"/> серед прогонів у сирому значенні "quests=" (формат QuestLog.CaptureState: "id&gt;етап&gt;стан" через кому).</summary>
        private static bool QuestRunPresentInBlob(string questsValue, string questId)
        {
            if (string.IsNullOrEmpty(questsValue) || string.IsNullOrEmpty(questId)) return false;
            foreach (var entry in questsValue.Split(','))
            {
                int gt = entry.IndexOf('>');
                string id = gt >= 0 ? entry.Substring(0, gt) : entry;
                if (string.Equals(id, questId, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>Сире значення поля "key" (напр. ";quests=") із заголовка зліпка ДО наступного ';' — той самий прийом, що вже читає "arc=" вище, узагальнений для повторного використання.</summary>
        private static string ExtractHeadField(string headPart, string key)
        {
            int idx = headPart.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return null;
            int afterKey = idx + key.Length;
            int nextSemi = headPart.IndexOf(';', afterKey);
            return nextSemi >= 0 ? headPart.Substring(afterKey, nextSemi - afterKey) : headPart.Substring(afterKey);
        }

        private static SuspendToken ParseResume(string value)
        {
            var parts = value.Split('|');
            if (parts.Length < 2) return null;
            return new SuspendToken((SuspendReason)ParseInt(parts[0]), (SessionState)ParseInt(parts[1]));
        }

        private static int ParseInt(string s)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
        }
    }
}
