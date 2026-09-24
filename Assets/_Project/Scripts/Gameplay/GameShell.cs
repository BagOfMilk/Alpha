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

        private static readonly MethodInfo FeedVillageStageMethod = ResolveBridgeMethod("VillageStageBridge", "Feed");
        private static readonly MethodInfo FindBattlePresenterMethod = ResolveBridgeMethod("PresenterDiscoveryBridge", "FindBattlePresenter");
        private static readonly MethodInfo FindPortraitProviderMethod = ResolveBridgeMethod("PresenterDiscoveryBridge", "FindPortraitProvider");

        private void Awake()
        {
            _roller = new SeededDiceRoller(1);
            Session = new GameSession(_roller);

            if (AutoplayBootstrap.RequestedFromCommandLine())
                AutoplayBootstrap.Driver = new AutoplayGameDriver();

            DiscoverPresenters();
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

        private void OnGUI()
        {
            GUI.skin = AlphaSkin.Build();
            if (Session == null) return;

            var state = Session.State;

            bool escapeEligible = state != SessionState.Title && state != SessionState.Creation &&
                                   state != SessionState.Scene && state != SessionState.Battle;
            if (escapeEligible && Input.GetKeyDown(KeyCode.Escape))
                _escapeOpen = !_escapeOpen;

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

        private void DrawBattle()
        {
            if (BattlePresenter != null)
            {
                if (!BattlePresenter.IsActive) BattlePresenter.Enter(Session);
                BattlePresenter.DrawHud(Session);
            }
            else
            {
                DrawFullScreen(() => _battleFallback.Draw(this));
            }
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
            GUILayout.Label(view.Phase.ToString(), AlphaSkin.Body, GUILayout.ExpandWidth(false));
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
                for (int i = log.Count - 1; i >= 0; i--)
                    GUILayout.Label(ScreenText.EventLine(log[i], ProtagonistGender, roster), AlphaSkin.Body);
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
