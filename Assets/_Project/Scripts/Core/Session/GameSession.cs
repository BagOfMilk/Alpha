using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Characters.Build;
using Game.Core.Characters.Creation;
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

        /// <summary>
        /// Major-фікс ревью (§2 №27, seamsForD1 пакета B4): особисті арки
        /// напарників (<see cref="CompanionArc"/>/<see cref="CompanionArcRun"/>)
        /// існували з пакета B4, але жодного разу не інстанціювались і не
        /// тікались з GameSession — Refresh() не звав ніхто, подія
        /// "arc.chapter_opened" не могла піти в DayLog. Тут лише гейтинг
        /// (лояльність/прапор) тікається щоденно (<see cref="TickCompanionArcs"/>) —
        /// реальний ЗМІСТ глави (Begin/CompleteChapter через квест з
        /// ArcChapter.QuestId) лишається відкритим гачком для пакета змісту
        /// (та сама межа декаплінгу, яку документує сам CompanionArc: "содержание
        /// главы играет вызывающий, через будущий QuestRun, B6/D1").
        /// </summary>
        private List<CompanionArcRun> _arcRuns;

        /// <summary>Прапори гейтингу арок (ArcChapter.RequiresFlag/SetsFlag) — окремі від StoryFlags: без Begin/CompleteChapter (гачок вище) їх ще нікому виставляти.</summary>
        private readonly HashSet<string> _arcFlags = new HashSet<string>();

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
        private bool _ironman;
        private CombatState _battle;
        private SuspendToken _resume;

        /// <summary>
        /// Фикс-ревью D1b (мажор): чи саме ЦЕЙ бій довела до кінця команда
        /// <see cref="CombatAutoResolve"/> (а не покрокові команди гравця). Раніше
        /// подія "combat.autoresolved" вибиралась за SuspendReason.TrainingSkirmish
        /// — тобто за тим, ЩО за бій (тренувальний), а не ЯК саме його завершили,
        /// тож для будь-якого справжнього кампанійного бою (кровавий вузол 1,
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
        private string _pendingBackgroundId = "warrior";
        private Gender _protagonistGender = Gender.Male;

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
        private string _lastLoggedQuestOfferKey;
        private DayReportView _lastDayReport;
        private DayPhase _lastPhase = DayPhase.Day;
        private bool _summaryAcknowledged;
        private bool _freePlay;
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
            _world = FirstHourWorld.Build(tier: 1, requirePlayerDecision: true, balance: _cfg);
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

            _arcFlags.Clear();
            _arcRuns = new List<CompanionArcRun>();
            foreach (var arc in DefaultArcs.All())
                _arcRuns.Add(new CompanionArcRun(arc, _arcFlags));

            _slots.Clear();
            _dayLog.Clear();
            _dayLogVersion++;
            _currentPending = null;
            _currentQuestOffer = null;
            _lastLoggedQuestOfferKey = null;
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

            _hitRule = o.HitRule;
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

            if (o.SkipCreation)
            {
                BeginOpeningScene();
            }
            else
            {
                State = SessionState.Creation;
                _pendingName = null;
                _pendingGender = Gender.Male;
                _pendingBackgroundId = Backgrounds.All()[0].Id;
            }
        }

        /// <summary>
        /// Занести в пам'ять сесії готовий слепок диска (той самий рядок,
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
        /// замість чесного <c>Title</c>. Тепер слепок читається ДО <see cref="NewGame"/>
        /// (і без нього рано виходимо, не чіпаючи стан), а після — повертається
        /// назад у словник для <see cref="LoadState"/>.
        /// </summary>
        public bool ContinueGame(int slot, IDiceRoller roller = null)
        {
            RequireState(SessionState.Title);

            string blob;
            if (!_slots.TryGetValue(slot, out blob) || string.IsNullOrEmpty(blob)) return false;

            NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold, Roller = roller });
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
                AvailableBackgrounds = ids
            };
        }

        // =====================================================================
        // Scene (§3.0/§3.1: відкриття та розв'язка вузла 1)
        // =====================================================================

        private void BeginOpeningScene()
        {
            BeginScene(OpeningScenes.NeighbourWithADemand(), SessionState.Morning);
        }

        private void BeginScene(Scene scene, SessionState afterState)
        {
            _scenePlayback = new ScenePlayback(scene);
            _sceneReturn = afterState;
            State = SessionState.Scene;
            _lastFramedActorId = null;
            _lastFramedSecondActorId = null;
        }

        public SceneStepView AdvanceScene()
        {
            RequireState(SessionState.Scene);
            if (_scenePlayback == null) throw new InvalidOperationException("Немає активної сцени.");

            _scenePlayback.Next();
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

            if (_scenePlayback.IsFinished)
            {
                if (view.TransitionKey == "to.node1.pass") _flags.Set("tugar_offer_seen");
                State = _sceneReturn;
                _scenePlayback = null;
                LogEvent("scene.finished", Args("transition", view.TransitionKey));
            }
            return view;
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
            var r = _works.OrderRaid(_state, _processor.CurrentDay, _cfg, _factions);
            if (r == CouncilOrderResult.Queued) LogEvent("council.raid.ordered");
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
            var r = _works.OrderDecree(_state, _processor, _factions, favoredFactionId, costFactionId, _processor.CurrentDay, _cfg);
            if (r == CouncilOrderResult.Applied)
                LogEvent("council.decree", Args("favored", favoredFactionId, "cost", costFactionId ?? string.Empty));
            return r;
        }

        public CouncilOrderResult OrderDiplomacy(string factionId)
        {
            RequireMorningOrFreePlay();
            var r = _works.OrderDiplomacy(_state, _factions, factionId, _processor.CurrentDay, _cfg);
            if (r == CouncilOrderResult.Applied) LogEvent("council.diplomacy", Args("factionId", factionId));
            return r;
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
                var firstRoom = rooms != null && rooms.Count > 0 ? BuildDungeonRoomView(rooms[0]) : null;
                return new ExpeditionPreviewView { SiteId = siteId, Approach = approach, IsDelve = true, FirstRoom = firstRoom, Days = 2 };
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
                ExpectedMaterials = preview.Materials,
                ExpectedGold = preview.Gold,
                ExpectedWounded = preview.ExpectedWounded,
                IsDelve = false
            };
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
            // безумовно на першому ж відправленні (навіть на ІНШУ площадку) і
            // губився назавжди, ніколи не діставшись тієї, на яку був
            // замовлений (CityWorks.OrderOutfitExpedition документує це саме
            // так: "разовый бонус следующей вилазке НА ПЛОЩАДКУ siteId").
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
            var run = _quests.Get(questId) ?? _quests.Start(questId);
            if (run == null || run.Current == null) return null;

            var stage = run.Current;
            var offer = new QuestOfferView { Kind = "Quest", TopicId = run.Def.Id, QuestId = questId, Stage = run.CurrentIndex };

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
            if (!string.Equals(_lastLoggedQuestOfferKey, offerKey, StringComparison.Ordinal))
            {
                LogEvent("quest.offered", Args("questId", questId, "stage", run.CurrentIndex.ToString(CultureInfo.InvariantCulture)));
                _lastLoggedQuestOfferKey = offerKey;
            }
            return offer;
        }

        public DayReportView ResolveQuestChoice(int optionIndex)
        {
            RequireAnyState(SessionState.Morning, SessionState.Evening, SessionState.Night);
            if (_currentQuestOffer == null) throw new InvalidOperationException("Немає активної пропозиції квесту.");
            var run = _quests.Get(_currentQuestOffer.QuestId);
            if (run == null) throw new InvalidOperationException("Квест не знайдено.");

            QuestStepReport step = run.Current.Kind == QuestStageKind.Choice
                ? run.Choose(optionIndex, _flags)
                : run.ResolveCheck(_rosterView, _repeats, _processor.CurrentDay, _cfg);

            ApplyQuestConsequence(step.Consequence);

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

            _inventory.Remove(item);
            var previous = c.Equipment.Equip(item);
            if (previous != null) _inventory.Add(previous);

            LogEvent("equip.changed", Args("companionId", companionId, "itemId", item.Definition.Id, "slot", slot.ToString()));
            return true;
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
        /// Відновлення з готового слепка (той самий рядок, що повертає
        /// <see cref="SaveState"/>), а не з внутрішнього слота цього екземпляра.
        /// Потрібне для акцептансу D1 "SaveState → LoadState У НОВОМУ
        /// екземплярі GameSession": слоти (<see cref="_slots"/>) — пам'ять
        /// одного інстансу, а фактичний файл на диску — робота викликача
        /// (Alpha.Play/Unity SaveLoadScreen), який і передає рядок сюди.
        /// </summary>
        public void RestoreFromBlob(string blob)
        {
            if (string.IsNullOrEmpty(blob)) throw new ArgumentException("Порожній слепок", nameof(blob));
            ApplySave(blob);
            LogEvent("game.loaded", Args("slot", "external"));
        }

        /// <summary>Сташ поселення для UI/тестів (§4.1 Equip/CraftUpgrade адресують предмети звідси за InstanceId).</summary>
        public IReadOnlyList<ItemInstance> GetStash() => _inventory.Items;

        /// <summary>
        /// Тестовий гачок IVT (<c>AssemblyInfo.cs</c>: <c>Game.Tests.EditMode</c>
        /// бачить <c>internal</c>-члени <c>Game.Core.*</c> — той самий підхід,
        /// що вже застосований до <c>Companion.Loyalty</c>): перевірити, що
        /// ціна кровавого шляху вузла 1 (PlaystyleBlood/CausedFear, D1b) реально
        /// дійшла до прихованих шкал, БЕЗ появи жодного числа в публічному View
        /// (R17) — жоден офіційний контракт §4.2 цього не показує навмисно.
        /// </summary>
        internal int DebugTensionValue => _processor?.Tension?.Value ?? 0;
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

            TickExpeditionReturnIfAny();

            // Час рухає ЛИШЕ SettlementCycle (CLAUDE.md §"Время идёт только
            // через SettlementCycle") — саме він переносить прапор голоду
            // (BaseState.WasHungryLastCycle → DayProcessor.IsHungry) перед
            // кроком конвеєра. Прямий викл _processor.Advance() лишав голод
            // непідключеним: HungerStep читав би завжди застаріле значення.
            var report = _cycle.AdvanceDay(DayPhase.Day);
            LogEvent("day.advanced", Args("day", report.Day.ToString(CultureInfo.InvariantCulture), "phase", report.Phase.ToString()));
            TranslateReport(report);
            ApplyCycleReport(_world.Cycle.Production.LastReport);
            _lastDayReport = BuildDayReportView(report);
            SettleAfterDayReport(report);

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
                // замінює справжній тактичний бій), але дві ціни кровавого
                // шляху, що не залежать від того, ЯК саме розв'язано кровавий
                // вибір (перевіркою чи боєм), лишаються тими самими, що й для
                // будь-якого іншого інциденту з HasBloodyPath (IncidentResolver.
                // ApplyBloodCost): драйвер PlaystyleBlood закритого переліку
                // (інваріант 5) і пам'ять страху громади (CausedFear — кроваве
                // рішення само лякає, незалежно від виходу бою). Рана виконавцю
                // тут НЕ дублюється: справжні втрати вже рахує ApplyBattleCasualties
                // після резолву бою (RosterAdapter.Wound/Kill), а не абстрактний
                // "казуальний" удар check.ActorId, якого при бою просто немає.
                //
                // Фикс-ревью D1b: раніше тут стояв _processor.QueueExternal(...),
                // а це — мостик R6, який за контрактом TensionTickStep дренує
                // заявку лише на ПЕРШОМУ тіку НАСТУПНОЇ фази, тоді як
                // IncidentResolver.ApplyBloodCost для будь-якого іншого
                // кровавого інциденту застосовує PlaystyleBlood СИНХРОННО, в
                // тому самому виклику, що й Напругу полоси виходу. Викликаємо
                // TensionState.Apply напряму (internal, той самий Game.Core,
                // що й IncidentResolver) — так ціна крові лягає атомарно з
                // рештою наслідків цього ж вузла, а не фазою пізніше.
                _processor.Tension.Apply(TensionDriver.PlaystyleBlood, _cfg.Tension.BloodDeltaPerNode,
                    "blood:" + incidentId);
                _processor.Fear?.Remember(_processor.CurrentDay, _cfg.Checks);

                var setup = BuildBattleSetup(new[] { ProtagonistId, "maksym", "myroslava" },
                    new[] { "horde_scout", "horde_scout" }, 8, 8);
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
                    _state.Resources.TrySpend(ResourceType.Gold, 15); // ПЛЕЙСХОЛДЕР-ціна (§9, як і решта чисел зрізу)
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

        public DayReportView ResolveFinale(IncidentPath path)
        {
            RequireState(SessionState.Night);
            if (_processor.CurrentDay != 5)
                throw new InvalidOperationException("Фінал лише на добу 5, вночі.");
            if (_finaleResolved)
                throw new InvalidOperationException("Фінал уже розв'язано.");

            if (path == IncidentPath.Bloody)
            {
                bool myroslavaDefected = _flags.Get(PassVanguardOutcome.DefectorSeededFlag);
                var plan = Finale.BuildAssault(_readiness.Band, myroslavaDefected ? "myroslava" : null, _cfg.Readiness);

                // Фікс-ревью (блокер, знайдено тур-автоплеєм): партія тут була
                // жорстко "{ProtagonistId, "maksym"}" незалежно від того, чи
                // Максим ще живий на добу 5 — вузол 1 (доба 1) може поранити
                // або вбити його ще на самому початку, а фінал однаково
                // виставляв його на грід. Той самий allow-list присутності, що
                // вже фільтрує пости й вилазку (RosterAdapter.IsPresentInSettlement),
                // тепер фільтрує й фінальну партію.
                var partyIds = new List<string> { ProtagonistId };
                if (IsCompanionBattleReady("maksym")) partyIds.Add("maksym");

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
                var setup = BuildBattleSetup(_dungeon.PartyIds, room.EnemyIds, 8, 8);
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
            LogEvent("dungeon.extract", Args("materials", rep.Materials.ToString(CultureInfo.InvariantCulture),
                "gold", rep.Gold.ToString(CultureInfo.InvariantCulture)));

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
        /// би ті самі кнопки "тихо/кроваво" ЗНОВУ й отримував
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
                UnbankedMaterials = _dungeon.UnbankedMaterials,
                CurrentRoom = _dungeon.CurrentCleared ? null : BuildDungeonRoomView(_dungeon.CurrentRoom),
                Outcome = _dungeon.Outcome.ToString(),
                AwaitingBattle = _dungeon.AwaitingBattle
            };
        }

        private DungeonRoomView BuildDungeonRoomView(DungeonRoomDefinition room)
        {
            if (room == null) return null;

            var view = new DungeonRoomView
            {
                Id = room.Id,
                DisplayName = room.DisplayNameKey,
                Type = room.Kind == DungeonRoomKind.Cache ? "Treasure" : room.Kind.ToString()
            };

            if (room.Kind == DungeonRoomKind.Combat && room.QuietChecks.Count > 0)
            {
                var req = room.QuietChecks[0];
                view.HasQuietBypass = true;
                view.QuietSkillKey = req.Skill.Id;
                view.QuietThreshold = _dungeon != null ? _dungeon.EffectiveQuietThreshold(req) : req.Threshold;
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
        /// доказу бою поза <see cref="BattleView.Log"/> (сирими рядками для
        /// гравця, не для тесту покриття). Кожен запис класифікується за
        /// <see cref="AttackRecord.IsReaction"/> — прапором, який ставить сам
        /// <c>CombatState</c> у точці народження запису (ReactToMovement),
        /// а не позиційним порівнянням AttackerId із тим, хто мав хід на
        /// момент виклику команди.
        ///
        /// Фикс-ревью D1b (блокер): стара эвристика ("AttackerId != actingUnitId
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
        /// Фикс-ревью D1b (блокер): раніше кликав лише <see cref="CombatAi.AutoResolve"/>
        /// і одразу <see cref="AfterCombatAction"/> — жоден AttackRecord, зіграний
        /// ІІ за ОБИДВІ сторони на шляху до результату, не діставався DayLog, хоча
        /// саме автобій (не покрокова команда) — панівний спосіб розв'язки бою в
        /// "one-game" проходженні (кровавий вузол 1, бойові кімнати данжу,
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

        public int PreviewHitChance(string attackerId, string targetId)
        {
            if (_battle == null) return 0;
            var a = _battle.GetUnit(attackerId);
            var t = _battle.GetUnit(targetId);
            if (a == null || t == null) return 0;
            return _battle.HitChancePreview(a, t);
        }

        public BattleView GetBattleView()
        {
            if (_battle == null) return null;

            var units = new List<BattleUnitView>();
            foreach (var u in _battle.Units)
            {
                bool fromDefector = u.Side == Side.Enemy && !string.IsNullOrEmpty(u.SourceCompanionId);
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
                    ApReserved = u.IsOverwatching && u.Weapon != null ? u.Weapon.ApCost : 0,
                    IsOverwatching = u.IsOverwatching,
                    Statuses = MapStatuses(u),
                    IsDowned = u.LifeState == UnitLifeState.Downed,
                    HitChancePreview = 0
                });
            }

            var reachable = new List<GridPosView>();
            if (_battle.Current != null && _battle.Current.IsActive)
                foreach (var kv in _battle.ReachableFor(_battle.Current))
                    reachable.Add(new GridPosView(kv.Key.X, kv.Key.Y));

            var initiative = new List<string>();
            if (_battle.TurnOrder != null)
                foreach (var u in _battle.TurnOrder) initiative.Add(u.Id);

            var cover = new List<string>();
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
                    walkable.Add(_battle.Map.IsWalkable(pos));
                }

            return new BattleView
            {
                Round = _battle.Round,
                Outcome = _battle.Outcome.ToString(),
                Grid = new BattleGridView { Width = _battle.Map.Width, Height = _battle.Map.Height, TileCover = cover, TileWalkable = walkable },
                Units = units,
                ReachableTiles = reachable,
                CurrentUnitId = _battle.Current != null && _battle.Current.IsActive ? _battle.Current.Id : null,
                InitiativeOrder = initiative,
                Log = _battle.Log,
                IsHitRulePercent = _battle.IsHitRulePercent
            };
        }

        private static List<string> MapStatuses(CombatUnit u)
        {
            var list = new List<string>();
            foreach (var s in u.Statuses) list.Add(s.Type.ToString());
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
        private void OnBattleResolved()
        {
            if (_battle == null || _resume == null || _battle.Outcome == CombatOutcome.Ongoing) return;

            var result = BattleResult.From(_battle);
            var band = MapBattleBand(result);
            var reason = _resume.Reason;
            var returnState = _resume.ReturnState;

            if (reason != SuspendReason.TrainingSkirmish)
                ApplyBattleCasualties(result);

            bool autoResolved = _battleAutoResolvedThisCall;
            _battleAutoResolvedThisCall = false;
            LogEvent(autoResolved ? "combat.autoresolved" : "combat.battle.resolved",
                Args("outcome", result.Outcome.ToString(), "rounds", result.Rounds.ToString(CultureInfo.InvariantCulture), "reason", reason.ToString()));

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
                    FinishDungeonCombat(band, result);
                    break;
                case SuspendReason.FinaleAssault:
                    CompleteFinale(band, wasBloody: true);
                    break;
                case SuspendReason.TrainingSkirmish:
                    break; // пісочниця: State вже Title/ReturnState, кампанію не чіпаємо
            }
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
                FinaleOutcomeKey = _finaleOutcomeKey
            };
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
                Materials = _state.Resources.Get(ResourceType.Materials),
                Food = _state.Resources.Get(ResourceType.Food)
            };
        }

        public CityView GetCityView()
        {
            var built = new List<BuildingView>();
            foreach (var id in _works.Built) built.Add(new BuildingView { Id = id, StageOf = 5 });

            var inProgress = new List<BuildingView>();
            foreach (var def in DefaultBuildingsType.All())
                if (_works.IsBuilding(def.Id)) inProgress.Add(new BuildingView { Id = def.Id, StageOf = _works.StageOf(def.Id) });

            return new CityView
            {
                Built = built,
                InProgress = inProgress,
                RaidReady = _works.RaidReady(_processor.CurrentDay, _cfg),
                SettlersReady = _works.Has(DefaultBuildingsType.CouncilHall)
            };
        }

        public RosterView GetRosterView()
        {
            var list = new List<CompanionSummary>();
            foreach (var c in _worldRoster.All)
            {
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
                    Loyalty = c.Card != null && c.Card.CanBeCompanion ? (LoyaltyBand?)c.LoyaltyBand : null,
                    Equipped = equipped,
                    ScarCount = c.Scars.Count
                });
            }
            return new RosterView { Companions = list };
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
                    LogEvent(AdjustPassVanguardTopicIfMaksymDead(req.TopicId),
                        Args("channel", req.Channel.ToString(), "urgency", req.Urgency.ToString(),
                        "subject", req.SubjectId, "delta", req.IsDelta ? "1" : "0"));
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
                        "chapterId", run.CurrentChapter?.Id ?? string.Empty));
            }
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
            try { _slots[-1] = ComposeSave(); }
            catch { /* автосейв best-effort — провал не повинен рвати денний конвеєр */ }
        }

        private void TickExpeditionReturnIfAny()
        {
            if (!_party.IsAway) return;
            bool arrived = _party.TickDay();
            if (!arrived) return;

            ExpeditionResult result;
            var returned = _party.Return(_state, out result);
            ExpeditionRunner.Complete(_state, result, _works);
            LogEvent("expedition.returned", Args("siteId", result?.SiteId, "band", result?.Band.ToString()));

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
                    ExpectedBand = opt.ExpectedBand.ToString()
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

        private void ApplyQuestConsequence(QuestConsequence c)
        {
            if (c == null || c.IsEmpty) return;
            if (c.TensionDelta != 0) _processor.QueueExternal(TensionDriver.QuestChoice, c.TensionDelta);
            foreach (var kv in c.FactionDeltas) ApplyFactionDelta(kv.Key, kv.Value);
            foreach (var kv in c.LoyaltyDeltas) LogLoyaltyChange(ApplyLoyaltyDelta(kv.Key, kv.Value, "quest"));
            foreach (var flag in c.Flags) _flags.Set(flag);
            foreach (var itemId in c.ItemIds) { GrantNamedItemById(itemId); LogEvent("loot.dropped", Args("itemId", itemId, "named", "1")); }
            if (c.Xp != 0) GrantXp(ProtagonistId, c.Xp);

            ApplyHafiyaGrassBonusToSickChildIfNeeded();
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

        private void GrantNamedItemById(string itemId)
        {
            if (!string.Equals(itemId, "scout_horn", StringComparison.Ordinal)) return;

            _inventory.Add(ItemInstance.NamedFrom(DefaultItems.ScoutHorn(_cfg.Items)));

            // seamsForD1 B3 (ефект «forewarn_boost», D1b): "наступні 2
            // передвісники — раніше/легше" застосовується через адитивний шов
            // WorldPulse.BoostCharge на єдиний Announces-накопичувач кампанії
            // (Тугар, §3.3) — детально в ItemBalance.ScoutHornForewarnBoostPerCharge.
            //
            // Фикс-ревью D1b (мінор): подія логується, лише якщо BoostCharge
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

        private void LogRipple(RippleReport report)
        {
            if (report == null) return;
            foreach (var effect in report.Effects)
                LogEvent("roster.rippled", Args("companionId", effect.CompanionId, "kinship", effect.Bond.ToString()));
        }

        private Combat.PlayerUnitSource ResolvePlayerUnit(string companionId)
        {
            var c = _worldRoster.Get(companionId);
            if (c == null) return null;
            bool protect = string.Equals(companionId, ProtagonistId, StringComparison.Ordinal) && !_ironman;
            return new Combat.PlayerUnitSource(c, ResolveWeaponFor(c), protect);
        }

        /// <summary>
        /// ПЛЕЙСХОЛДЕР (як і решта чисел зрізу, §9): Combat не читає
        /// Companion.Equipment (Items — окрема система статів, не бойової зброї
        /// з дальністю/дамагом), тож бойову зброю обирає евристика за скілом,
        /// доки контент не заведе окрему прив'язку "надітий предмет → зброя бою".
        /// Відкрите питання лишене явно, як і в seamsForD1 пакета B1.
        /// </summary>
        private static Combat.WeaponDefinition ResolveWeaponFor(Companion c)
        {
            if (c == null) return DefaultCombatContent.HordeSpear();
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

        private BattleSetup BuildBattleSetup(IReadOnlyList<string> partyIds, IReadOnlyList<string> enemyIds,
            int width, int height, string defectorCompanionId = null)
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

            var setup = new BattleSetup { Width = width, Height = height, HitRule = _hitRule };
            if (width > 3 && height > 2)
                setup.Cover.Add(new CoverPlacement(new GridPos(width / 2, height / 2), Direction.West, CoverType.Half));

            int py = 1;
            if (partyIds != null)
                foreach (var id in partyIds)
                {
                    setup.PlayerUnits.Add(new PlayerSpawn(id, new GridPos(1, py)));
                    py += 2;
                }

            int ey = 1;
            if (enemyIds != null)
                foreach (var id in enemyIds)
                {
                    setup.EnemyUnits.Add(new EnemySpawn(id, new GridPos(width - 2, ey)));
                    ey += 2;
                }

            if (!string.IsNullOrEmpty(defectorCompanionId))
            {
                setup.DefectorCompanionId = defectorCompanionId;
                setup.DefectorPos = new GridPos(width - 2, ey);
            }

            return setup;
        }

        /// <summary>
        /// ПЛЕЙСХОЛДЕР-правило мапінгу BattleResult→OutcomeBand (§9): специфікація
        /// не фіксує його явно для жодного з трьох реальних боїв (вузол 1/данж/
        /// фінал) — рішення інтегратора, застосоване однаково для всіх трьох:
        /// Victory без втрат → Best; Victory з даун/смертю → Base; Victory без
        /// даун/смерті, але з ранами → Good; Defeat → Worst; Retreat/Draw → Base.
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
            if (_roller != null) head.Append(";roller=").Append(_roller.CaptureState());
            head.Append(";resume=").Append(_resume == null ? "-" :
                ((int)_resume.Reason).ToString(CultureInfo.InvariantCulture) + "|" +
                ((int)_resume.ReturnState).ToString(CultureInfo.InvariantCulture));
            head.Append(";freeplay=").Append(_freePlay ? 1 : 0);
            head.Append(";summary=").Append(_summaryAcknowledged ? 1 : 0);
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

            // Довжина-префікс (як і "core=" нижче): Inventory.CaptureState() сам
            // з'єднує предмети через ';' (Inventory.cs), тож наївний
            // headPart.Split(';') у ApplySave інакше сплутав би роздільник
            // предметів із роздільником полів заголовка і губив усі предмети,
            // крім першого (аудит: сташ 2+ предметів після Save/Load).
            string itemsBlob = _inventory.CaptureState();
            head.Append(";items=").Append(itemsBlob.Length.ToString(CultureInfo.InvariantCulture)).Append('^').Append(itemsBlob);

            head.Append(";defect=").Append(_defectionWatch.CaptureState());
            head.Append(";crisis=").Append(_crisis.CaptureState());

            string coreBlob = _processor.SaveState();
            head.Append(";core=").Append(coreBlob.Length.ToString(CultureInfo.InvariantCulture)).Append('^').Append(coreBlob);
            return head.ToString();
        }

        private void ApplySave(string blob)
        {
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

            foreach (var part in headPart.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string key = part.Substring(0, eq);
                string value = part.Substring(eq + 1);

                switch (key)
                {
                    case "state": State = (SessionState)ParseInt(value); break;
                    case "seed": ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _seed); break;
                    case "hitRule": _hitRule = (HitRuleKind)ParseInt(value); break;
                    case "roller": _roller?.RestoreState(value); break;
                    case "resume": _resume = value == "-" ? null : ParseResume(value); break;
                    case "freeplay": _freePlay = value == "1"; break;
                    case "summary": _summaryAcknowledged = value == "1"; break;
                    case "finale": _finaleResolved = value == "1"; break;
                    case "readiness": _readiness.RestoreState(value); break;
                    case "quests": _quests.RestoreState(value); break;
                    case "factions": _factions.RestoreState(value); break;
                    case "points": _points.RestoreState(value); break;
                    case "defect": _defectionWatch.RestoreState(value); break;
                    case "crisis": _crisis.RestoreState(value); break;
                    case "arc": RestoreArcState(value); break;
                    case "pname": _pendingName = value == "-" ? null : System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(value)); break;
                    case "pgender": _pendingGender = (Gender)ParseInt(value); _protagonistGender = _pendingGender; break;
                    case "pbg": if (!string.IsNullOrEmpty(value)) _pendingBackgroundId = value; break;
                }
            }

            _inventory.RestoreState(itemsPart);
            if (corePart != null) _processor.RestoreState(corePart);

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
            _lastLoggedQuestOfferKey = null;
            _dungeon = null;
            _battle = null;
            _battleAutoResolvedThisCall = false;

            // Флаг міг бути виставлений ДО збереження (квест-етап "grass"
            // резолвиться задовго до доби 3) — порог sick_child не входить у
            // жоден слепок (визначення інцидентів не персистяться), тож без
            // цього виклику бонус мовчки губився б після Save/Load.
            ApplyHafiyaGrassBonusToSickChildIfNeeded();
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
