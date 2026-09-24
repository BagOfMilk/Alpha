using System;
using System.Reflection;
using Game.Core.Characters.Creation;
using Game.Core.Randomness;
using Game.Core.Session;
using Game.Core.Session.Views;
using Game.Gameplay.Combat;
using Game.Gameplay.Text;
using Game.Gameplay.UI;
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
    public sealed class GameShell : MonoBehaviour
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
        private readonly HubScreen _hub = new HubScreen();
        private readonly DecisionScreen _decision = new DecisionScreen();
        private readonly NightScreen _night = new NightScreen();
        private readonly DungeonScreen _dungeon = new DungeonScreen();
        private readonly BattleScreen _battleFallback = new BattleScreen();
        private readonly SummaryScreen _summary = new SummaryScreen();
        private readonly EscapeMenuScreen _escape = new EscapeMenuScreen();

        private bool _escapeOpen;
        private SeededDiceRoller _roller;

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
            HardExit.Now(0); // TerminateProcess: Environment.Exit зависал, Application.Quit падал (см. HardExit)
            return false; // формальність — рядком вище процес уже завершено
        }

        private static readonly MethodInfo FeedVillageStageMethod = ResolveBridgeMethod("VillageStageBridge", "Feed");
        private static readonly MethodInfo FindBattlePresenterMethod = ResolveBridgeMethod("PresenterDiscoveryBridge", "FindBattlePresenter");
        private static readonly MethodInfo FindPortraitProviderMethod = ResolveBridgeMethod("PresenterDiscoveryBridge", "FindPortraitProvider");

        private void Awake()
        {
            _roller = new SeededDiceRoller(1);
            Session = new GameSession(_roller);

            DiscoverPresenters();

            Application.wantsToQuit += HandleWantsToQuit; // §HandleWantsToQuit
        }

        private void OnDestroy()
        {
            Application.wantsToQuit -= HandleWantsToQuit;
        }

        /// <summary>
        /// Фаза F (UI-tour autoplay): гачок для <c>AutoplayGameDriver</c>, щоб
        /// той міг перемкнути вкладку РЕАЛЬНОГО <see cref="HubScreen"/> (поле
        /// приватне — екран сам вирішує, яку вкладку малювати) і зняти
        /// скріншот кожної, не тримаючи власної копії стану екрана.
        /// </summary>
        public void SetHubTab(int tab) => _hub.SetTab(tab);

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
        }

        private void OnGUI()
        {
            GUI.skin = AlphaSkin.Build();
            if (Session == null) return;

            var state = Session.State;

            bool escapeEligible = state != SessionState.Title && state != SessionState.Creation &&
                                   state != SessionState.Scene && state != SessionState.Battle;

            // Event-based, не сирий Input.GetKeyDown (фікс-ревью, блокер):
            // OnGUI викликається кілька разів за кадр (Layout, сама подія
            // KeyDown, Repaint, ...), і Input.GetKeyDown лишається true в
            // УСІХ цих проходах, тоді як Event.current.type == KeyDown —
            // тільки в одному, тому цей перемикач тепер спрацьовує рівно раз
            // на фізичне натискання. GameShell — єдиний власник _escapeOpen:
            // EscapeMenuScreen/Widgets.Modal більше не чіпають Escape самі
            // (див. коментар в EscapeMenuScreen.Draw).
            var escEvt = Event.current;
            bool escapePressed = escEvt != null && escEvt.type == EventType.KeyDown && escEvt.keyCode == KeyCode.Escape;
            if (escapeEligible && escapePressed)
            {
                _escapeOpen = !_escapeOpen;
                escEvt.Use();
            }

            bool escapeShown = _escapeOpen && escapeEligible;
            bool wasEnabled = GUI.enabled;
            GUI.enabled = !escapeShown;
            DrawStateScreen(state);
            GUI.enabled = wasEnabled;

            if (escapeShown)
                _escape.Draw(this);
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
                    _creation.Draw(this);
                    break;
                case SessionState.Scene:
                case SessionState.Opening:
                    _scene.Draw(this);
                    break;
                case SessionState.Battle:
                    DrawBattle();
                    break;
                case SessionState.Summary:
                    DrawFullScreen(() => _summary.Draw(this));
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

        /// <summary>Хаб-стани (Morning/Day/Decision/Evening/Night/Dungeon/FreePlay) — спільна шапка + стрічка подій навколо власного вмісту екрана.</summary>
        private void DrawHubLike(SessionState state)
        {
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
        }

        private void DrawHubBody(SessionState state)
        {
            switch (state)
            {
                case SessionState.Morning:
                case SessionState.FreePlay:
                    _hub.Draw(this);
                    break;
                case SessionState.Day:
                case SessionState.Decision:
                {
                    // Під час Decision конвеєр дня зупинений — за модалкою (нижче)
                    // видно той самий хаб, що й перед AdvanceDay, але вимкнений:
                    // IMGUI-модалка сама не блокує клік крізь фон (немає стека
                    // фокусу), тому фон вимикається явно на час показу.
                    bool wasEnabled = GUI.enabled;
                    GUI.enabled = state != SessionState.Decision;
                    _hub.Draw(this);
                    GUI.enabled = wasEnabled;
                    break;
                }
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
            GUILayout.Label(UkrainianText.Get("resource.materials", g) + ": " + economy.Materials, AlphaSkin.Body, GUILayout.ExpandWidth(false));
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
                LastMessage = ex.Message;
            }
        }

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
                LastMessage = ex.Message;
                return fallback;
            }
        }

        public void SetEscapeOpen(bool open) => _escapeOpen = open;

        private void FeedVillageStage()
        {
            FeedVillageStageMethod?.Invoke(null, new object[] { Session });
        }

        /// <summary>
        /// Рефлексія на клас у ЦІЙ ЖЕ збірці (Game.Gameplay), виключений з
        /// Game.Gameplay.Lint через глибокі виклики рушія (Object.
        /// FindObjectsByType/Light/Camera тощо) — GameShell.cs лінтується й не
        /// може посилатись на такий тип напряму (той самий приём, що вже
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
