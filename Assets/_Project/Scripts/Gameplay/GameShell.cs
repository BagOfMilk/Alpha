using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Core.Characters.Creation;
using Game.Core.Randomness;
using Game.Core.Session;
using Game.Core.Session.Views;
using Game.Gameplay.Combat;
using Game.Gameplay.Text;
using Game.Gameplay.UI;
using Game.Gameplay.UI.Toolkit;
using Game.Gameplay.Walk;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Оболонка всієї гри (пакет E1b): один <c>MonoBehaviour</c> тримає
    /// <c>GameSession</c> і диспетчерить *Screen.cs за <see cref="GameSession.State"/>.
    /// Малює лише IMGUI (<see cref="AlphaSkin"/>/<see cref="Widgets"/>) — жодної
    /// логіки гри тут немає, лише виклики команд <c>GameSession</c> і читання
    /// View-шару (інваріант "Gameplay читає лише GameSession", CLAUDE.md).
    /// </summary>
    public sealed partial class GameShell : MonoBehaviour
    {
        public GameSession Session { get; private set; }
        public Gender ProtagonistGender { get; set; } = Gender.Male;
        public string LastMessage { get; private set; } = string.Empty;
        public IBattlePresenter BattlePresenter { get; private set; }
        public IPortraitProvider PortraitProvider { get; private set; }
        public IDiceRoller Roller => _roller;

        private readonly TitleScreen _title = new TitleScreen();
        private readonly CreationScreen _creation = new CreationScreen();
        private readonly SceneScreen _scene = new SceneScreen();

        /// <summary>
        /// Тур-автоплей (Поправка №7.8, п.4): цей самий екземпляр малює
        /// <c>OnGUI</c> — водій просуває сцену/вибір через нього
        /// (<c>SceneScreen.DriverAdvance/DriverChoose/DriverContinueConsequence</c>),
        /// а не напряму через <c>Session</c>, інакше в екрана з'явився б
        /// ДРУГИЙ, розсинхронізований курсор (див. коментар над цими
        /// методами в SceneScreen.cs).
        /// </summary>
        public SceneScreen Scene => _scene;
        private readonly DecisionScreen _decision = new DecisionScreen();
        private readonly NightScreen _night = new NightScreen();
        private readonly DungeonScreen _dungeon = new DungeonScreen();
        private readonly BattleScreen _battleFallback = new BattleScreen();
        private readonly SummaryScreen _summary = new SummaryScreen();
        private readonly EscapeMenuScreen _escape = new EscapeMenuScreen();

        private bool _escapeOpen;
        private SessionState _lastUxState;
        private SeededDiceRoller _roller;

        // ===================== HUD: UI Toolkit або IMGUI (спайк H4) =====================
        //
        // Рішення власника 29.09.2026 («3. спробуємо»): шапка і стрічка міста —
        // на UI Toolkit (Gameplay/UI/Toolkit/HudToolkitView), бій лишається на
        // IMGUI. Прапорець командного рядка -imgui-hud повертає старі
        // DrawTopBar/DrawEventFeed без змін — для порівняння за п'ятьма
        // критеріями docs/HUD_DESIGN.md §8 (зокрема час кадру, критерій 4).

        /// <summary>Прапорець командного рядка: лишити шапку і стрічку на IMGUI.</summary>
        public const string ImguiHudFlag = "-imgui-hud";

        private HudToolkitView _toolkitHud;

        /// <summary>Вид шапки і стрічки на UI Toolkit; null — IMGUI (прапорець або UI Toolkit не піднявся).</summary>
        public HudToolkitView ToolkitHud => _toolkitHud;

        /// <summary>Шапку і стрічку малює UI Toolkit — IMGUI їх не малює.</summary>
        public bool ToolkitHudActive => _toolkitHud != null && _toolkitHud.IsReady;

        /// <summary>Підпис режиму для логу автотуру.</summary>
        public string HudModeLabel => ToolkitHudActive ? "UI Toolkit" : "IMGUI";

        /// <summary>
        /// Екран міста з шапкою і стрічкою зараз на екрані — ті самі стани, що
        /// <see cref="DrawStateScreen"/> веде в <see cref="DrawHubLike"/>
        /// (і не панель результату бою, яка тримає екран бою довше за State).
        /// </summary>
        public bool CityHudVisible
        {
            get
            {
                if (Session == null) return false;
                if (BattlePresenter != null && BattlePresenter.IsActive && BattlePresenter.ResultPending) return false;
                switch (Session.State)
                {
                    case SessionState.Morning:
                    case SessionState.Day:
                    case SessionState.Decision:
                    case SessionState.Evening:
                    case SessionState.Night:
                    case SessionState.Dungeon:
                    case SessionState.FreePlay:
                        return true;
                    default:
                        return false;
                }
            }
        }

        private static bool HasCommandLineArg(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (args[i] == flag) return true;
            return false;
        }

        /// <summary>
        /// Безпечна точка виходу: <see cref="TitleScreen"/>/<see cref="EscapeMenuScreen"/>
        /// більше не кличуть рушійний вихід напряму зсередини <c>OnGUI</c> —
        /// того самого кадрового вікна, у якому URP ще не завершив Submit
        /// поточного кадру. Прапорець ставиться з OnGUI, сам вихід
        /// відкладається до наступного <see cref="Update"/>. Це другий шар
        /// поверх головного фіксу нижче (<see cref="HandleWantsToQuit"/>) —
        /// сам механізм виходу той самий (root-cause evidence там).
        /// </summary>
        private bool _quitRequested;

        public void RequestQuit() => _quitRequested = true;

        /// <summary>
        /// Root-cause фікс краху при виході (доказ — Windows Event Log,
        /// Application/Id=1000, 24.09.2026, ~22 запуски поспіль): будь-який
        /// шлях, що доходить до нативного teardown рушія після
        /// <c>Application.Quit</c>, падає в UnityPlayer.dll на тій самій
        /// детермінованій адресі — незалежно від графічного API (перевірено
        /// і форсований D3D11, і штатний D3D12 — та сама адреса) і незалежно
        /// від того, що встигло намалюватись (той самий крах на голому
        /// титулі, без жодного кадру бою чи портрета). Це баг самого
        /// нативного teardown, не код гри — тому лагодиться не там, де
        /// щось руйнується руками (PortraitRig/BattleArenaController тут ні
        /// до чого), а тим, що рушій узагалі НЕ пускається цим шляхом:
        /// <c>Application.wantsToQuit</c> — найперша подія послідовності
        /// виходу (до <c>Application.quitting</c>, до самого teardown) і
        /// єдина, що ловить УСІ тригери — не лише кнопку "Вихід", а й
        /// Alt+F4/закриття вікна/сигнал ОС. <see cref="Environment.Exit"/>
        /// тут завершує процес одразу, той самий спосіб, що вже підтверджено
        /// в <see cref="AutoplayBootstrap.Finish"/> (0 крашів на 6 запусках
        /// проти 100% крашів через Application.Quit).
        /// </summary>
        private bool HandleWantsToQuit()
        {
            HardExit.Now(0); // TerminateProcess: Environment.Exit зависав, Application.Quit падав (див. HardExit)
            return false; // формальність — рядком вище процес уже завершено
        }

        private static readonly MethodInfo FeedVillageStageMethod = ResolveBridgeMethod("VillageStageBridge", "Feed");
        private static readonly MethodInfo FindBattlePresenterMethod = ResolveBridgeMethod("PresenterDiscoveryBridge", "FindBattlePresenter");
        private static readonly MethodInfo FindPortraitProviderMethod = ResolveBridgeMethod("PresenterDiscoveryBridge", "FindPortraitProvider");
        // Поправка №19.3: створення героя на UI Toolkit з 3D-прев'ю (UI.Toolkit.CreationToolkitView, поза лінтом).
        private static readonly MethodInfo CreateCreationOverlayMethod = ResolveBridgeMethod("UI.Toolkit.CreationToolkitView", "TryCreate");
        private IShellOverlay _creationOverlay;
        // Поправка №19.3: «лялька» спорядження й кузня (UI.Toolkit.InventoryToolkitView, клавіша I).
        private static readonly MethodInfo CreateInventoryOverlayMethod = ResolveBridgeMethod("UI.Toolkit.InventoryToolkitView", "TryCreate");
        private IShellOverlay _inventoryOverlay;

        private void Awake()
        {
            // Поправка №21.1: рівень графіки — збережений вибір або автопідбір під цю машину.
            GraphicsTier.InitOnce();
            _roller = new SeededDiceRoller(1);
            Session = new GameSession(_roller);

            DiscoverPresenters();

            if (!HasCommandLineArg(ImguiHudFlag))
            {
                _toolkitHud = HudToolkitView.TryCreate(this); // null → лишаємось на IMGUI
                if (CreateCreationOverlayMethod != null)
                    _creationOverlay = CreateCreationOverlayMethod.Invoke(null, new object[] { this }) as IShellOverlay;
                if (CreateInventoryOverlayMethod != null)
                    _inventoryOverlay = CreateInventoryOverlayMethod.Invoke(null, new object[] { this }) as IShellOverlay;
            }

            Application.wantsToQuit += HandleWantsToQuit; // §HandleWantsToQuit
        }

        private void OnDestroy()
        {
            Application.wantsToQuit -= HandleWantsToQuit;
            if (_toolkitHud != null) _toolkitHud.Dispose();
            _toolkitHud = null;
            if (_creationOverlay != null) _creationOverlay.Dispose();
            _creationOverlay = null;
            if (_inventoryOverlay != null) _inventoryOverlay.Dispose();
            _inventoryOverlay = null;
        }

        /// <summary>Після Update усіх компонентів (зокрема автотуру): шапка бачить стан цього кадру.</summary>
        private void LateUpdate()
        {
            if (_toolkitHud != null) _toolkitHud.Tick();
            if (_creationOverlay != null) _creationOverlay.Tick();
            if (_inventoryOverlay != null) _inventoryOverlay.Tick();
            else if (InventoryOpen) InventoryOpen = false; // без UI Toolkit «ляльки» немає — лишається Склад
        }

        // ===================== село (CRPG) =====================
        //
        // Власник, 25.09.2026: «Я хотів шоб я міг бігати як у CRPG»; 30.09.2026:
        // «прибери найбільшу частину табличок… досі неможна зайти до будівлі і
        // поговорити з персонажем». Уранці й у вільній грі село — вигляд за
        // замовчуванням (вкладок хаба більше немає): WASD або клік мишею,
        // Shift — біг; біля будівлі, станції чи людини — «E». Сам рух —
        // компонент сцени HeroWalker (читає Exploring, пише NearbyPlace).

        /// <summary>Герой ходить селом (завжди, коли <see cref="CanExplore"/>).</summary>
        public bool Exploring { get; private set; }

        /// <summary>Місце, біля якого стоїть герой (пише HeroWalker щокадру); null — поруч нічого.</summary>
        public WalkPlace NearbyPlace { get; private set; }

        /// <summary>Усі місця, до яких зараз можна підійти (село чи кімната) — пише HeroWalker; автотур шукає тут будівлю й людину.</summary>
        public IReadOnlyList<WalkPlace> Places { get; private set; }

        public void SetPlaces(IReadOnlyList<WalkPlace> places) => Places = places;

        /// <summary>Запит «дійти до місця» (автотур): HeroWalker забирає його і будує шлях, як на клік мишею.</summary>
        public string PendingWalkTarget { get; private set; }

        /// <summary>Стан героя одним рядком (позиція, маршрут) — пише HeroWalker; автотур кладе його в лог, коли герой не дійшов.</summary>
        public string WalkDebug => WalkDebugSource != null ? WalkDebugSource() : string.Empty;

        /// <summary>Хто вміє описати стан героя (HeroWalker); рядок будується лише на читання.</summary>
        public Func<string> WalkDebugSource { get; set; }

        /// <summary>Прямокутники інтерфейсу прогулянки (координати GUI): клік по них — не команда «йти».</summary>
        public readonly List<Rect> ExploreUiRects = new List<Rect>();

        /// <summary>Гуляти можна вранці й у вільній грі — там, де гравець сам розпоряджається часом.</summary>
        public bool CanExplore =>
            Session != null &&
            (Session.State == SessionState.Morning || Session.State == SessionState.FreePlay) &&
            !(BattlePresenter != null && BattlePresenter.IsActive);

        public void SetExploring(bool on)
        {
            Exploring = on && CanExplore;
            if (!Exploring)
            {
                NearbyPlace = null;
                PendingWalkTarget = null;
                ExploreUiRects.Clear();
            }
        }

        public void SetNearbyPlace(WalkPlace place) => NearbyPlace = Exploring ? place : null;

        public void RequestWalkTo(string placeId) => RequestWalkTo(placeId, interactOnArrival: false);

        /// <summary>Повести героя до місця; <paramref name="interactOnArrival"/> — по прибутті взаємодіяти, як клік по місцю.</summary>
        public void RequestWalkTo(string placeId, bool interactOnArrival)
        {
            PendingWalkTarget = Exploring ? placeId : null;
            _walkInteract = PendingWalkTarget != null && interactOnArrival;
        }

        private bool _walkInteract;

        /// <summary>Запит повороту камери (−1 — Q, +1 — E) для HeroWalker: автотур обертає камеру тим самим шляхом, що клавіші.</summary>
        private int _cameraTurn;

        public void RequestCameraTurn(int direction) => _cameraTurn = Math.Sign(direction);

        public int ConsumeCameraTurn()
        {
            int turn = _cameraTurn;
            _cameraTurn = 0;
            return turn;
        }

        public string ConsumeWalkRequest(out bool interactOnArrival)
        {
            var target = PendingWalkTarget;
            interactOnArrival = _walkInteract;
            PendingWalkTarget = null;
            _walkInteract = false;
            return target;
        }

        /// <summary>
        /// Пости постановки (Presenters.cs — контракт E1b/E2, § "Cross-package
        /// seams"): реалізації шукаються у сцені під час виконання, тому
        /// GameShell не тримає жорсткого посилання на конкретний тип E2.
        /// </summary>
        private void DiscoverPresenters()
        {
            BattlePresenter = FindBattlePresenterMethod?.Invoke(null, null) as IBattlePresenter;
            PortraitProvider = FindPortraitProviderMethod?.Invoke(null, null) as IPortraitProvider;
        }

        /// <summary>§_quitRequested — безпечна точка виходу (не OnGUI/корутина, прив'язана до рендеру).</summary>
        private void Update()
        {
            if (_quitRequested) HardExit.Now(0); // §HandleWantsToQuit — той самий безпечний вихід (HardExit)
            PersistAutosaveIfNew();
        }

        /// <summary>Версія автосейву, уже записана на диск (<see cref="GameSession.AutosaveVersion"/>).</summary>
        private int _persistedAutosaveVersion;

        /// <summary>
        /// Ранковий автосейв ядро кладе лише в пам'ять сесії — файл пишемо тут,
        /// щойно з'явилась нова версія. Раніше на диск не потрапляв жоден
        /// автосейв, і «Продовжити» після перезапуску його не знаходило.
        /// </summary>
        private void PersistAutosaveIfNew()
        {
            if (Session == null || Session.AutosaveVersion == _persistedAutosaveVersion) return;
            _persistedAutosaveVersion = Session.AutosaveVersion;
            string blob = Session.AutosaveBlob;
            if (blob == null) return;
            var view = Session.CurrentView;
            try
            {
                SaveFileStore.Write(SaveFileStore.AutosaveSlot, blob, view?.TensionBand, view?.Day ?? 0);
            }
            catch (System.IO.IOException ex)
            {
                LastMessage = UkrainianText.Format("ui.save.failed", ProtagonistGender, "reason", ex.Message);
            }
        }

        private void OnGUI()
        {
            GUI.skin = AlphaSkin.Build();
            if (Session == null) return;
            if (PortraitProvider != null) PortraitProvider.ProtagonistGender = ProtagonistGender;

            // Поправка №7.8, п.1 (тест-збірка): у стані Evening сесія НІКОМУ
            // сама не штовхає сценарний зміст (особисті арки напарників,
            // «Нічна розмова»/тиха перевірка Мирослави доба 3, рада Захара
            // доба 5) — GameSession лише ДОЗВОЛЯЄ його викликати
            // (RequireAnyState), а хто саме й коли викликає — вирішує
            // сторона, що керує сесією (у headless-прогонах це
            // BotRunner.MaybeOfferQuest/MaybeAdvanceArcChapterScene/
            // MaybeOfferScriptedScene). У Unity-збірці керує гравець, тож цю
            // саму роль тут бере оболонка — інакше жоден із цих сценаріїв
            // ніколи не показався б людині, хоч ядро повністю готове його
            // зіграти.
            RouteOfferedSceneContentIfAvailable();

            var state = Session.State;

            // Esc працює скрізь, крім титулу (у титулу своє меню): у створенні героя,
            // сценах і на підсумку — пауза з «У головне меню» (Поправка №22.1).
            bool escapeEligible = state != SessionState.Title;

            // Event-based, не сирий Input.GetKeyDown (фікс-ревью, блокер):
            // OnGUI викликається кілька разів за кадр (Layout, сама подія
            // KeyDown, Repaint, ...), і Input.GetKeyDown лишається true в
            // УСІХ цих проходах, тоді як Event.current.type == KeyDown —
            // тільки в одному, тому цей перемикач тепер спрацьовує рівно раз
            // на фізичне натискання. GameShell — єдиний власник _escapeOpen:
            // EscapeMenuScreen/Widgets.Modal більше не чіпають Escape самі
            // (див. коментар в EscapeMenuScreen.Draw).
            var escEvt = Event.current;
            bool escapePressed = escEvt != null && escEvt.type == EventType.KeyDown && escEvt.keyCode == KeyCode.Escape
                                 && !PlaytestLog.NoteOpen; // Esc у вікні нотатки плейтесту — її, не пауза
            if (escapeEligible && escapePressed && !_escapeOpen && state != SessionState.Battle && EscapeClosesLayer())
            {
                escEvt.Use();
                escapePressed = false;
            }
            if (escapeEligible && escapePressed)
            {
                // Бій v2 (docs/COMBAT_V2.md §3, доручення власника 25.09.2026 —
                // «коли наступив хід опонентів гра тупа зупинилась»): Esc у
                // бою раніше не робив НІЧОГО (Battle був виключений з
                // escapeEligible вище) — «Esc» не скасовував озброєну дію і
                // не відкривав меню паузи, єдиний вихід із зависання був
                // Alt+F4. Тепер: озброєна дія (Дозор/здібність) → скасувати
                // її (той самий жест, що ПКМ); інакше — звичайне меню паузи.
                bool armedCancelled = false;
                // ResultPending — модалка перемоги/поразки вже сама показує
                // єдиний вихід («Далі»/AcknowledgeResult): відкривати поверх
                // неї ще й меню паузи — плутанина, яку модалку закривати
                // першою (закрити ВЖЕ відкрите меню паузи Esc усе одно може —
                // гравець ніколи не застрягає без виходу).
                bool battleResultPending = BattlePresenter != null && BattlePresenter.ResultPending;
                bool suppressPauseToggle = state == SessionState.Battle && !_escapeOpen && battleResultPending;

                if (state == SessionState.Battle && !_escapeOpen && !battleResultPending)
                {
                    var battleInput = BattlePresenter as IBattleInput;
                    if (battleInput != null && battleInput.Armed != ArmedAction.None)
                    {
                        battleInput.CancelArmed();
                        armedCancelled = true;
                    }
                }

                if (!armedCancelled && !suppressPauseToggle) _escapeOpen = !_escapeOpen;
                escEvt.Use();
            }

            bool escapeShown = _escapeOpen && escapeEligible;

            // Село: уранці й у вільній грі — завжди (вкладок хаба немає). Коли
            // стан іде з ранку, панель закривається. Клавіші шару місць — та
            // сама подієва обробка, що й Escape вище (рівно раз на натискання).
            if (Exploring && !CanExplore) SetExploring(false);
            if (!Exploring && CanExplore) SetExploring(true);
            if (state != _lastUxState)
            {
                if (Ux.OpenPanel != UxPanelId.None && !CanExplore) ClosePanel();
                _lastUxState = state;
            }
            var keyEvt = Event.current;
            if (!escapeShown && HandleWorldKeys(keyEvt)) keyEvt.Use();

            bool wasEnabled = GUI.enabled;
            // Бій стоїть, поки відкрите меню паузи (презентер сам нічого не знає про Esc).
            if (BattlePresenter is IBattleInput pauseTarget) pauseTarget.Paused = escapeShown;

            GUI.enabled = !escapeShown;
            DrawStateScreen(state);
            GUI.enabled = wasEnabled;

            if (!escapeShown) DrawHudHoverTip();

            if (escapeShown)
                _escape.Draw(this);
        }

        /// <summary>
        /// Підказка при наведенні на шапку UI Toolkit — драбина полос Напруги
        /// (Поправка №12.1: слово постійно + сусідні полоси при наведенні, без
        /// чисел і стрілок) або назва ресурсу під значком. Малюється тут, в IMGUI,
        /// бо IMGUI лягає поверх UI Toolkit і сховав би підказку під панеллю хаба.
        /// </summary>
        private void DrawHudHoverTip()
        {
            if (!ToolkitHudActive) return;
            var lines = _toolkitHud.HoverTipLines;
            if (lines == null || lines.Count == 0) return;

            // UI v2: панель-підказка шкурки (рамка BG3) замість заливки з
            // чотирма лініями; ширина — справжнім виміром шрифту, а не оцінкою.
            Widgets.HoverTip(_toolkitHud.HoverTipGuiRect, lines, _toolkitHud.HoverTipCurrentIndex);
        }

        private void DrawStateScreen(SessionState state)
        {
            // Фіх-ревью (Фаза F, знайдено тур-автоплеєм): бій може
            // розв'язатись СИНХРОННО всередині будь-якої Combat*-команди
            // (GameSession.OnBattleResolved зсуває State ДАЛІ в тому самому
            // виклику — §4.1) — без цієї перевірки панель результату бою
            // (BattlePresenter.ResultPending) НІКОЛИ не встигала б
            // відмалюватись жодному гравцю: диспетчер нижче перемикався б на
            // наступний екран за той самий кадр, у якому бій щойно скінчився,
            // і BattleHudScreen.DrawResultPanel лишалась мертвим кодом.
            // Презентер сам знімає активність (TeardownAndDeactivate) лише
            // коли гравець підтвердить панель ("Далі" → AcknowledgeResult) —
            // доти тримаємо його на екрані, незалежно від того, куди вже
            // пішов Session.State.
            if (BattlePresenter != null && BattlePresenter.IsActive && BattlePresenter.ResultPending)
            {
                DrawBattle();
                return;
            }

            switch (state)
            {
                case SessionState.Title:
                    _title.Draw(this);
                    break;
                case SessionState.Creation:
                    // UI Toolkit з 3D-прев'ю, якщо є; IMGUI-екран — фолбек (-imgui-hud або немає набору).
                    if (_creationOverlay == null || !_creationOverlay.Handles(state))
                        _creation.Draw(this);
                    DrawOverlays();
                    break;
                case SessionState.Scene:
                case SessionState.Opening:
                    _scene.Draw(this);
                    DrawOverlays();
                    break;
                case SessionState.Battle:
                    DrawBattle();
                    break;
                case SessionState.Summary:
                    DrawFullScreen(() => _summary.Draw(this));
                    // Esc → «Збереження» відкриває панель — без шарів вона не малювалась.
                    DrawOverlays();
                    break;
                default:
                    DrawHubLike(state);
                    break;
            }
        }

        /// <summary>
        /// Фікс-ревью (major): E2's IBattlePresenter — код іншого пакета,
        /// викликаний напряму, поза TryRun/TryRun&lt;T&gt; (котрі ловлять лише
        /// InvalidOperationException GameSession, тут не той випадок — це
        /// виняток самого презентера). Без guard'а падіння Enter/DrawHud
        /// вивалилось би з OnGUI назовні й повторювалось би щокадру (IsActive
        /// так і не встановився б), замість тихого відкату до IMGUI-фолбека,
        /// як робить решта команд цього файлу.
        /// </summary>
        private void DrawBattle()
        {
            if (BattlePresenter != null)
            {
                try
                {
                    if (!BattlePresenter.IsActive) BattlePresenter.Enter(Session);
                    BattlePresenter.DrawHud(Session);
                    return;
                }
                catch (Exception ex)
                {
                    Debug.LogError(ex);
                    LastMessage = ex.Message;
                }
            }

            DrawFullScreen(() => _battleFallback.Draw(this));
        }

        /// <summary>
        /// Порядок і гейти — буквально ті самі, що <c>BotRunner.DoTick</c>
        /// (<c>case SessionState.Evening</c>): особисті арки Мирослави й
        /// Максима (сцена — глава 1/епілог), квестова глава Максима (реєструє
        /// й одразу пропонує; далі гравець резолвить її вкладкою «Квести»,
        /// як і Гафіїн квест), «Нічна розмова»/тиха перевірка Мирослави доба
        /// 3, рада Захара доба 5. Кожен крок сам собою гейтований (флаг/день/
        /// <c>IsArcChapterAvailable</c>) — повторний виклик того самого кадру,
        /// коли попередній крок УЖЕ перевів сесію зі стану Evening (сцена
        /// почалась), нешкідливий но-оп: наступний крок просто не пробує
        /// нічого, побачивши чужий стан. Публічний і викликається З ДВОХ
        /// місць (фікс-ревью, блокер, знайдено QA): звідси — щокадру для
        /// реального гравця, і <c>AutoplayGameDriver</c> — напряму й
        /// синхронно, БЕЗ очікування кадру (див. коментар у
        /// <c>AutoplayGameDriver</c> над викликом). Кадровий каданс OnGUI
        /// (Layout/Repaint-події) у фоновому (без фокуса вікна) прогоні
        /// виявився недетермінованим — той самий білд на тих самих вхідних
        /// даних інколи не встигав дати OnGUI жодного проходу за
        /// WaitFrames(2), тож «Нічна розмова»/рада Захара/Максимів квест
        /// мовчки пропускались у частині прогонів і показувались в інших
        /// (QA: 0/3 незалежних повторних прогони проти 2/2 авторських —
        /// однаковий сід, той самий build 0a5cfe0). Виклик, гейтований лише
        /// станом сесії (а не кадром), ідемпотентний для обох викликачів.
        /// </summary>
        public void RouteOfferedSceneContentIfAvailable()
        {
            if (Session.State != SessionState.Evening) return;
            TryBeginArcSceneIfAvailable("myroslava");

            if (Session.State != SessionState.Evening) return;
            TryBeginArcSceneIfAvailable("maksym");

            if (Session.State != SessionState.Evening) return;
            TryBeginArcQuestIfAvailable("maksym");

            if (Session.State != SessionState.Evening) return;
            TryRun(() => Session.OfferMyroslavaEveningScene(), null);

            if (Session.State != SessionState.Evening) return;
            TryRun(() => Session.OfferZakharCouncilScene(), null);
        }

        /// <summary>Глава арки, чий зміст — сцена з вибором (<see cref="GameSession.BeginArcChapterScene"/>) — лише коли вона щойно доступна.</summary>
        private void TryBeginArcSceneIfAvailable(string companionId)
        {
            if (!Session.IsArcChapterAvailable(companionId) || !Session.IsArcChapterSceneContent(companionId)) return;
            TryRun(() => Session.BeginArcChapterScene(companionId), null);
        }

        /// <summary>
        /// Глава арки, чий зміст — квест (<see cref="GameSession.BeginArcChapterQuest"/>,
        /// наразі лише Максим ч.1): реєструє визначення в пулі й пропонує
        /// перший етап — сесія лишається в Evening (на відміну від сценової
        /// глави, це НЕ SessionState.Scene), тож подальші етапи гравець
        /// резолвить на Дошці оголошень чи в розмові з Максимом (<c>UxQuestCards</c>), тим
        /// самим шляхом, що й Гафіїн квест.
        /// </summary>
        private void TryBeginArcQuestIfAvailable(string companionId)
        {
            if (!Session.IsArcChapterAvailable(companionId) || !Session.IsArcChapterQuestContent(companionId)) return;
            TryRun(() => Session.BeginArcChapterQuest(companionId), null);
        }

        /// <summary>Хаб-стани (Morning/Day/Decision/Evening/Night/Dungeon/FreePlay) — спільна шапка + стрічка подій навколо власного вмісту екрана.</summary>
        private void DrawHubLike(SessionState state)
        {
            if (Exploring && (state == SessionState.Morning || state == SessionState.FreePlay))
            {
                DrawWorld();
                return;
            }

            if (ToolkitHudActive)
            {
                // Шапку і стрічку малює UI Toolkit; IMGUI отримує лише тіло —
                // прямокутник, що не перетинається з ними (HudLayout, критерій 5).
                var body = HudLayout.For(Screen.width, Screen.height).Body;
                GUILayout.BeginArea(new Rect(body.X, body.Y, body.Width, body.Height));
                GUILayout.BeginVertical();
                // LastMessage не йде в шапку (HUD_DESIGN §4.2): відмова лишається
                // рядком над вмістом екрана, як і досі, — лише вже не в шапці.
                if (!string.IsNullOrEmpty(LastMessage))
                    GUILayout.Label(LastMessage, AlphaSkin.Tooltip);
                DrawHubBody(state);
                GUILayout.EndVertical();
                GUILayout.EndArea();

                if (state == SessionState.Decision)
                    Widgets.Modal(UkrainianText.Get("ui.decision.title", ProtagonistGender), () => _decision.DrawBody(this));
                DrawOverlays();
                return;
            }

            var area = new Rect(0f, 0f, Screen.width, Screen.height);
            GUILayout.BeginArea(area);
            GUILayout.BeginVertical();

            DrawTopBar();

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(Screen.width * 0.7f), GUILayout.ExpandHeight(true));
            DrawHubBody(state);
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawEventFeed();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUILayout.EndArea();

            if (state == SessionState.Decision)
                Widgets.Modal(UkrainianText.Get("ui.decision.title", ProtagonistGender), () => _decision.DrawBody(this));
            DrawOverlays();
        }

        private void DrawHubBody(SessionState state)
        {
            switch (state)
            {
                case SessionState.Morning:
                case SessionState.FreePlay:
                case SessionState.Day:
                case SessionState.Decision:
                    // Вкладок хаба немає: за модалкою рішення видно саме село.
                    break;
                case SessionState.Evening:
                case SessionState.Night:
                    _night.Draw(this);
                    break;
                case SessionState.Dungeon:
                    _dungeon.Draw(this);
                    break;
            }
        }

        private void DrawFullScreen(Action body)
        {
            var area = new Rect(0f, 0f, Screen.width, Screen.height);
            GUILayout.BeginArea(area);
            body();
            GUILayout.EndArea();
        }

        private void DrawTopBar()
        {
            var view = Session.CurrentView;
            var economy = Session.GetEconomyView();
            var g = ProtagonistGender;

            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label(UkrainianText.Format("ui.topbar.day", g, "day", view.Day.ToString()), AlphaSkin.SubHeader, GUILayout.ExpandWidth(false));
            // Фікс-ревью (major): тут стояв сирий view.Phase.ToString() — енум-
            // назва ("Day"/"Night") друкувалась прямо в HUD англійською на
            // майже кожному екрані після відкриття (R7 такого не дозволяє).
            //
            // Фікс-ревью (minor, раунд 2, знайдено QA): view.Phase — це
            // внутрішня модель ДВОХ фаз конвеєра дня і ще не перемикається на
            // Day, доки гравець не натисне "Почати день" (HubScreen.Draw —
            // ця кнопка малюється однаково на Morning І на FreePlay, обидва
            // стани йдуть тим самим _hub.Draw) — GameSession лишає Phase=Night
            // до ConfirmMorning/AdvanceDay, тож хаб-екран "Доба N, Почати
            // день" показував суперечливе "Ніч" у шапці. На обох цих станах
            // підпис веде Session.State (ранок настав для гравця вже зараз),
            // а не внутрішня фаза конвеєра.
            string phaseKey;
            if (Session.State == SessionState.Morning || Session.State == SessionState.FreePlay)
                phaseKey = "ui.topbar.phase.morning";
            else
                phaseKey = view.Phase == Game.Core.Loop.DayPhase.Night
                    ? "ui.topbar.phase.night"
                    : "ui.topbar.phase.day";
            GUILayout.Label(UkrainianText.Get(phaseKey, g), AlphaSkin.Body, GUILayout.ExpandWidth(false));
            GUILayout.Space(12f);
            GUILayout.Label(UkrainianText.Format("ui.topbar.mood", g, "band", ScreenText.MoodChip(view.TensionBand, g)), AlphaSkin.Body, GUILayout.ExpandWidth(false));
            GUILayout.Label(UkrainianText.Format("ui.topbar.crowd", g, "band", ScreenText.CrowdChip(view.CrowdBand, g)), AlphaSkin.Body, GUILayout.ExpandWidth(false));
            GUILayout.Label(UkrainianText.Format("ui.topbar.tier", g, "tier", view.Tier.ToString()), AlphaSkin.Body, GUILayout.ExpandWidth(false));
            if (view.IsPatrolling)
                Widgets.Badge(UkrainianText.Get("ui.topbar.patrolling", g), AlphaSkin.Accent);
            if (view.IsFreePlay)
                Widgets.Badge(UkrainianText.Get("ui.topbar.freeplay", g), AlphaSkin.BgRaised);

            GUILayout.FlexibleSpace();
            GUILayout.Label(UkrainianText.Get("resource.gold", g) + ": " + economy.Gold, AlphaSkin.Body, GUILayout.ExpandWidth(false));
            // Поправка №12.5: два компоненти — окремими числами (назви ≤ 12 знаків, шапка 1280 px).
            GUILayout.Label(UkrainianText.Get("resource.build_component", g) + ": " + economy.BuildComponent, AlphaSkin.Body, GUILayout.ExpandWidth(false));
            GUILayout.Label(UkrainianText.Get("resource.craft_component", g) + ": " + economy.CraftComponent, AlphaSkin.Body, GUILayout.ExpandWidth(false));
            GUILayout.Label(UkrainianText.Get("resource.food", g) + ": " + economy.Food, AlphaSkin.Body, GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(LastMessage))
                GUILayout.Label(LastMessage, AlphaSkin.Tooltip);
        }

        private Vector2 _feedScroll;

        private void DrawEventFeed()
        {
            Widgets.Panel(UkrainianText.Get("ui.feed.title", ProtagonistGender), () =>
            {
                var log = Session.DayLog;
                if (log == null || log.Count == 0)
                {
                    GUILayout.Label(UkrainianText.Get("ui.feed.empty", ProtagonistGender), AlphaSkin.Tooltip);
                    return;
                }

                var roster = Session.GetRosterView();
                _feedScroll = Widgets.ScrollListBegin(_feedScroll, GUILayout.ExpandHeight(true));
                // Ціль 5 «Якість стрічки»: однакові рядки підряд згортаються в
                // один із "×N" (ScreenText.BuildFeedLines) замість того, щоб
                // топити стрічку буквальними повторами того самого тексту.
                var lines = ScreenText.BuildFeedLines(log, ProtagonistGender, roster);
                foreach (var line in lines)
                {
                    string text = line.Count > 1
                        ? line.Text + " " + UkrainianText.Format("ui.feed.repeat", ProtagonistGender, "count", line.Count.ToString())
                        : line.Text;
                    // Фікс-ревью (minor, знайдено QA): групова реакція складу
                    // (FeedLine.AlsoNames, §ScreenText.BuildFeedLines) — імена
                    // решти реагуючих одним переліком поруч із першим рядком,
                    // а не окремим рядком на кожне ім'я.
                    if (line.AlsoNames != null && line.AlsoNames.Count > 0)
                        text += " (" + UkrainianText.Format("ui.feed.also", ProtagonistGender,
                            "names", string.Join(", ", line.AlsoNames)) + ")";
                    GUILayout.Label(text, AlphaSkin.Body);
                }
                Widgets.ScrollListEnd();
            }, GUILayout.ExpandHeight(true));
        }

        // ===================== виконання команд =====================

        /// <summary>Виконує команду GameSession, ловить InvalidOperationException (недоступна у цьому стані) і показує її текстом замість падіння екрана.</summary>
        public void TryRun(Action action)
        {
            if (action == null) return;
            try
            {
                action();
                LastMessage = string.Empty;
                FeedVillageStage();
            }
            catch (InvalidOperationException ex)
            {
                LastMessage = HumanRefusal(ex);
            }
        }

        /// <summary>
        /// Команда, що відмовляє КОДОМ, а не винятком (AssignmentResult,
        /// BuildOrderResult, CouncilOrderResult, DispatchResult, CraftResult):
        /// <paramref name="failureText"/> повертає текст відмови або null для
        /// успіху, і відмову видно рядком під екраном. Раніше екрани кликали
        /// звичайний <see cref="TryRun"/>, код губився, і клік на відкаті чи на
        /// закритому пості просто нічого не робив (дебаг 25.09.2026).
        /// </summary>
        public T TryRunReported<T>(Func<T> action, Func<T, string> failureText, T fallback = default)
        {
            if (action == null) return fallback;
            try
            {
                var result = action();
                string failure = failureText != null ? failureText(result) : null;
                LastMessage = failure ?? string.Empty;
                FeedVillageStage();
                return result;
            }
            catch (InvalidOperationException ex)
            {
                LastMessage = HumanRefusal(ex);
                return fallback;
            }
        }

        /// <summary>
        /// Відмова ядра людською мовою замість сирого винятку («Команда
        /// недоступна у стані Evening…» → «Це можна зробити лише вранці або у
        /// вільній грі.»); сирий текст — лише в лог (docs/UX_DESIGN.md UX-11).
        /// </summary>
        private string HumanRefusal(Exception ex)
        {
            Debug.LogWarning("[команда] " + ex.Message);
            string text = UxErrorText.Humanize(ex.Message, ProtagonistGender == Gender.Female);
            // У селі рядка під екраном немає — відмова приходить тостом (UX_DESIGN §5.15).
            if (Exploring) Toast(text);
            return text;
        }

        /// <summary>Підтвердження дії, у якої немає власного рядка в стрічці (напр. збереження на диск).</summary>
        public void Notify(string message) => LastMessage = message ?? string.Empty;

        public T TryRun<T>(Func<T> action, T fallback = default)
        {
            if (action == null) return fallback;
            try
            {
                var result = action();
                LastMessage = string.Empty;
                FeedVillageStage();
                return result;
            }
            catch (InvalidOperationException ex)
            {
                LastMessage = HumanRefusal(ex);
                return fallback;
            }
        }

        public void SetEscapeOpen(bool open) => _escapeOpen = open;

        /// <summary>Меню Escape відкрите — герой на прогулянці стоїть.</summary>
        public bool EscapeOpen => _escapeOpen;

        private bool _villageFeedFailureLogged;

        /// <summary>
        /// Сцена села — декорація: її збій не сміє зірвати команду гравця. Раніше
        /// виняток мосту (рефлексія → TargetInvocationException) вилітав з TryRun
        /// назовні — на кнопці «Тренувальний бій» з титулу щоразу (світу ще немає).
        /// </summary>
        /// <summary>Вибір рівня графіки з меню (Поправка №21.1): діє одразу, село перемальовується.</summary>
        public void SetGraphics(GraphicsLevel level)
        {
            if (GraphicsTier.Current == level) return;
            GraphicsTier.Set(level);
            FeedVillageStage();
        }

        private void FeedVillageStage()
        {
            try
            {
                FeedVillageStageMethod?.Invoke(null, new object[] { Session });
            }
            catch (TargetInvocationException ex)
            {
                if (_villageFeedFailureLogged) return;
                _villageFeedFailureLogged = true;
                Debug.LogWarning("[Село] сцену не оновлено: " + (ex.InnerException ?? ex));
            }
        }

        /// <summary>
        /// Рефлексія на клас у ЦІЙ ЖЕ збірці (Game.Gameplay), виключений з
        /// Game.Gameplay.Lint через глибокі виклики рушія (Object.
        /// FindObjectsByType/Light/Camera тощо) — GameShell.cs лінтується й не
        /// може посилатись на такий тип напряму (той самий прийом, що вже
        /// з'єднує Editor/GameSceneBuilder.cs із Game.Gameplay.Editor.
        /// BattleArenaBuilder, §5 TEST_BUILD.md).
        /// </summary>
        private static MethodInfo ResolveBridgeMethod(string typeName, string methodName)
        {
            var type = Type.GetType("Game.Gameplay." + typeName + ", Game.Gameplay");
            return type?.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        }
    }
}
