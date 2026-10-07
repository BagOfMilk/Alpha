using System;
using System.Collections.Generic;
using Game.Core.Characters.Build;
using Game.Core.Characters.Creation;
using Game.Core.Session;
using Game.Core.Session.Views;
using Game.Gameplay.Text;
using Game.Gameplay.UI;
using Game.Gameplay.Walk;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Шар «на вимогу» поза HUD (docs/UX_DESIGN.md, Поправка №13; власник,
    /// 30.09.2026: «прибери найбільшу частину табличок… досі неможна зайти до
    /// будівлі і поговорити з персонажем»). Уранці й у вільній грі гравець
    /// бачить село: будівля — місце з дверима, всередині станції; людина —
    /// розмова; панелі — на клавішу (C, J, N, F10). Вкладок хаба більше
    /// немає. Моделі панелей — чисті (<c>UI/Models/Ux*</c>), тут лише
    /// керування шарами, введення і виклик рендера.
    /// </summary>
    public sealed partial class GameShell : IUxHost, IUxRenderHost
    {
        private readonly UxLayerState _layers = new UxLayerState();
        private readonly UxPanelState _panelState = new UxPanelState();
        private readonly UxInlineRefusals _refusals = new UxInlineRefusals();
        private readonly UxPanelView _panelView = new UxPanelView();
        private UxToastQueue _toasts;
        private string _panelContext;

        private UxConfirm _pendingConfirm;
        private Func<UxOutcome> _pendingConfirmRun;
        private string _pendingConfirmActionId;

        /// <summary>Огляд міста (Tab): підписи всіх місць зі станом (UX_DESIGN §3.6).</summary>
        public bool Overview { get; private set; }

        /// <summary>Довідка клавіш (F1).</summary>
        public bool KeysHelpOpen { get; private set; }

        /// <summary>«Лялька» спорядження й кузня (клавіша I, Поправка №19.3) — екран на UI Toolkit.</summary>
        public bool InventoryOpen { get; set; }

        public UxPanelId OpenPanelId => _layers.OpenPanel;
        public string OpenPanelContext => _panelContext;

        /// <summary>Герой у кімнаті будівлі: id будівлі або null (село).</summary>
        public string InteriorBuildingId => _layers.InteriorId;

        /// <summary>Запит «увійти» / «вийти» для HeroWalker (той веде героя і перемикає розташування).</summary>
        public string PendingEnter { get; private set; }
        public bool PendingExit { get; private set; }

        /// <summary>Модель відкритої панелі (для автотуру: знайти дію за id).</summary>
        public UxPanelModel CurrentPanelModel()
        {
            return _layers.OpenPanel == UxPanelId.None ? null : UxPanelFactory.Build(this, _layers.OpenPanel, _panelContext);
        }

        // ===================== IUxHost =====================

        Gender IUxHost.Gender => ProtagonistGender;
        public UxPanelState PanelState => _panelState;

        public void OpenPanel(UxPanelId panel, string context)
        {
            if (panel == UxPanelId.None) { ClosePanel(); return; }
            _layers.OpenPanelOf(panel);
            _panelContext = context;
            _refusals.Clear();
            ClearPendingConfirm();
        }

        public void WalkTo(string placeId)
        {
            ClosePanel();
            RequestWalkTo(placeId);
        }

        public void BeginScene(SceneStepView first)
        {
            ClosePanel();
            _scene.Begin(first);
        }

        public IReadOnlyList<SaveSlotView> SaveSlots()
        {
            var list = new List<SaveSlotView>();
            foreach (var h in SaveFileStore.ListHeaders())
                list.Add(new SaveSlotView { Slot = h.Slot, Occupied = h.Occupied, Headline = h.Headline, Day = h.Day });
            return list;
        }

        public UxOutcome SaveToSlot(int slot)
        {
            string blob;
            var outcome = UxCommandRunner.Run(() => Session.SaveState(slot), null, ProtagonistGender == Gender.Female, out blob);
            if (!outcome.Ok || blob == null) return outcome;
            // Айронмен (M1.10): ядро саме знає, що місце одне, — файл лягає туди, куди воно записало.
            slot = Session.ResolveSaveSlot(slot);
            try
            {
                var view = Session.CurrentView;
                SaveFileStore.Write(slot, blob, view.TensionBand, view.Day);
            }
            catch (System.IO.IOException ex)
            {
                return UxOutcome.Refused(UkrainianText.Format("ui.save.failed", ProtagonistGender, "reason", ex.Message));
            }
            Toast(UkrainianText.Format("game.saved", ProtagonistGender, "slot", ScreenText.SavedToLabel(slot, ProtagonistGender)));
            return UxOutcome.Success();
        }

        /// <summary>
        /// Завантаження — у СВІЖУ сесію, а не в поточну: завантаження в ту саму
        /// сесію маскує непіймане стан (FreshSessionRestoreTests, 25.09.2026).
        /// </summary>
        public UxOutcome LoadSlot(int slot)
        {
            var fresh = new GameSession(_roller);
            var outcome = UxCommandRunner.Run(() =>
            {
                string blob = SaveFileStore.Read(slot);
                fresh.PreloadSlot(slot, blob);
                fresh.ContinueGame(slot, _roller);
            }, ProtagonistGender == Gender.Female);
            if (!outcome.Ok) return outcome;
            Session = fresh;
            ProtagonistGender = Session.GetProtagonistCreationView()?.Gender ?? ProtagonistGender;
            _panelState.Reset();
            _layers.ExitInterior();
            _persistedAutosaveVersion = Session.AutosaveVersion;
            SetEscapeOpen(false);
            FeedVillageStage();
            Toast(UkrainianText.Format("ux.save.loaded", ProtagonistGender, "slot", ScreenText.SavedToLabel(slot, ProtagonistGender)));
            return UxOutcome.Success();
        }

        // ===================== IUxRenderHost =====================

        SessionState IUxRenderHost.State => Session.State;
        bool IUxRenderHost.Female => ProtagonistGender == Gender.Female;
        UxInlineRefusals IUxRenderHost.Refusals => _refusals;

        public void RunAction(UxAction action)
        {
            if (action == null) return;
            if (action.Confirm != null)
            {
                _pendingConfirm = action.Confirm;
                _pendingConfirmActionId = action.Id;
                var captured = action;
                _pendingConfirmRun = () => UxCommandRunner.Invoke(captured, Session.State, ProtagonistGender == Gender.Female);
                _layers.AskConfirm();
                return;
            }
            Report(action.Id, UxCommandRunner.Invoke(action, Session.State, ProtagonistGender == Gender.Female));
        }

        /// <summary>Дія автотуру за id у відкритій панелі — той самий шлях, що клік (UI-14).</summary>
        public UxOutcome InvokePanelAction(string actionId, bool confirm = true)
        {
            var model = CurrentPanelModel();
            if (model == null) return UxOutcome.Refused(null);
            foreach (var card in model.Cards)
                foreach (var a in card.Actions)
                    if (a.Id == actionId)
                    {
                        // Підтвердження автотур дає явно (confirm) — той самий крок, що кнопка діалогу.
                        if (a.Confirm != null && !confirm) return UxOutcome.Refused(null);
                        var outcome = UxCommandRunner.Invoke(a, Session.State, ProtagonistGender == Gender.Female);
                        Report(a.Id, outcome);
                        return outcome;
                    }
            return UxOutcome.Refused(null);
        }

        public void Link(string placeId) => WalkTo(placeId);

        public void ClosePanel()
        {
            _layers.ClosePanel();
            _panelContext = null;
            ClearPendingConfirm();
        }

        public void DrawExtras(UxPanelId panel)
        {
            if (panel != UxPanelId.Veche) return;
            // Полонені (№14.2) і «наші в полоні» (№14.7) — долю вирішує віче.
            PrisonersPanel.Draw(this, ProtagonistGender);
            CaptivesPanel.Draw(this, ProtagonistGender);
        }

        private void Report(string actionId, UxOutcome outcome)
        {
            _refusals.Record(actionId, outcome);
            if (outcome.Ok)
            {
                LastMessage = string.Empty;
                FeedVillageStage();
            }
            else if (!string.IsNullOrEmpty(outcome.Refusal))
            {
                Toast(outcome.Refusal);
                if (!string.IsNullOrEmpty(outcome.RawError)) Debug.LogWarning("[команда] " + outcome.RawError);
            }
        }

        private void Toast(string text)
        {
            if (_toasts == null) _toasts = new UxToastQueue(() => Time.realtimeSinceStartup);
            _toasts.Push(text);
        }

        /// <summary>
        /// Підтвердження незворотного поза панелями (UX-12): «Кинути» данж,
        /// кривавий фінал. Той самий діалог, що й для дій карток.
        /// </summary>
        public void AskConfirm(UxConfirm confirm, Action run)
        {
            if (confirm == null || run == null) return;
            _pendingConfirm = confirm;
            _pendingConfirmActionId = "confirm";
            _pendingConfirmRun = () => { run(); return UxOutcome.Success(); };
            _layers.AskConfirm();
        }

        /// <summary>
        /// Шари поверх екранів вечора, ночі й данжу: панель (збереження з
        /// Esc, C, J), діалог підтвердження, тост. У селі це малює <see cref="DrawWorld"/>.
        /// </summary>
        private void DrawOverlays()
        {
            var g = ProtagonistGender;
            float w = Screen.width, h = Screen.height;
            if (_layers.OpenPanel != UxPanelId.None)
            {
                var frame = HudLayout.For(w, h);
                float top = ToolkitHudActive ? frame.Header.Y + frame.Header.Height + 8f : 80f;
                var panelRect = new Rect(16f, top, Math.Max(360f, Math.Min(w * 0.55f, w - 48f)), Math.Max(200f, h - top - 24f));
                _panelView.Draw(panelRect, UxPanelFactory.Build(this, _layers.OpenPanel, _panelContext), _panelContext, this, g);
            }
            string toast = _toasts != null ? _toasts.Current : null;
            if (!string.IsNullOrEmpty(toast))
            {
                var style = new GUIStyle(AlphaSkin.Body) { wordWrap = true, alignment = TextAnchor.MiddleCenter };
                float tw = Math.Min(w - 32f, 760f);
                var toastRect = new Rect((w - tw) * 0.5f, h - 90f, tw, 54f);
                Widgets.SolidRect(toastRect, AlphaSkin.BgDark);
                GUI.Label(toastRect, toast, style);
            }
            if (_layers.ConfirmPending && _pendingConfirm != null) DrawConfirm(g);
        }

        private void ClearPendingConfirm()
        {
            _pendingConfirm = null;
            _pendingConfirmRun = null;
            _pendingConfirmActionId = null;
            _layers.ResolveConfirm();
        }

        // ===================== світ: місця, вхід, вихід =====================

        /// <summary>E чи клік по місцю: будівля — увійти, вихід — вийти, решта — панель місця.</summary>
        public void Interact(WalkPlace place)
        {
            if (place == null) return;
            switch (place.Kind)
            {
                case PlaceKind.Building:
                    if (place.Panel == UxPanelId.None) RequestEnter(place.BuildingId);
                    else OpenPanel(place.Panel, place.TargetId);
                    break;
                case PlaceKind.Exit:
                    RequestExit();
                    break;
                default:
                    OpenPanel(place.Panel, place.TargetId);
                    break;
            }
        }

        /// <summary>«E»: взаємодія з тим місцем, біля якого стоїть герой.</summary>
        public void InteractNearby() => Interact(NearbyPlace);

        public void RequestEnter(string buildingId)
        {
            if (!CanExplore || string.IsNullOrEmpty(buildingId)) return;
            ClosePanel();
            PendingEnter = buildingId;
        }

        public void RequestExit()
        {
            if (!_layers.InInterior) return;
            ClosePanel();
            PendingExit = true;
        }

        /// <summary>HeroWalker: герой уже в кімнаті / уже в селі.</summary>
        public void MarkInterior(string buildingId)
        {
            PendingEnter = null;
            _layers.EnterInterior(buildingId);
        }

        public void MarkVillage()
        {
            PendingExit = false;
            PendingEnter = null;
            _layers.ExitInterior();
        }

        // ===================== клавіші =====================

        /// <summary>Клавіші шару місць (UX_DESIGN §3.6): E, Tab, N, C, J, F10, F1. Кожна — одного власника (UxKeyMap).</summary>
        private bool HandleWorldKeys(Event evt)
        {
            if (evt == null || evt.type != EventType.KeyDown || !CanExplore) return false;
            if (_layers.ConfirmPending) return false;
            switch (evt.keyCode)
            {
                case KeyCode.E:
                    if (NearbyPlace == null) return false;
                    InteractNearby();
                    return true;
                case KeyCode.Tab:
                    Overview = !Overview;
                    return true;
                case KeyCode.N: TogglePanel(UxPanelId.DutyBoard); return true;
                case KeyCode.C: TogglePanel(UxPanelId.People); return true;
                case KeyCode.J: TogglePanel(UxPanelId.Journal); return true;
                case KeyCode.I:
                    InventoryOpen = !InventoryOpen;
                    if (InventoryOpen) ClosePanel();
                    return true;
                case KeyCode.F10: TogglePanel(UxPanelId.MechanicsJournal); return true;
                case KeyCode.F1: KeysHelpOpen = !KeysHelpOpen; return true;
                default: return false;
            }
        }

        private void TogglePanel(UxPanelId panel)
        {
            if (_layers.OpenPanel == panel) ClosePanel();
            else OpenPanel(panel, null);
        }

        /// <summary>Esc спершу знімає підтвердження, потім панель, довідку; і лише тоді — пауза. З будівлі Esc не виводить.</summary>
        private bool EscapeClosesLayer()
        {
            if (_layers.ConfirmPending) { ClearPendingConfirm(); return true; }
            if (InventoryOpen) { InventoryOpen = false; return true; }
            if (_layers.OpenPanel != UxPanelId.None) { ClosePanel(); return true; }
            if (KeysHelpOpen) { KeysHelpOpen = false; return true; }
            return false;
        }

        // ===================== малювання світу =====================

        /// <summary>
        /// Уранці й у вільній грі: село (чи кімната), ліворуч — відкрита
        /// панель, унизу — підказка місця, швидкі панелі й «Почати день».
        /// </summary>
        private void DrawWorld()
        {
            var g = ProtagonistGender;
            float w = Screen.width, h = Screen.height;
            ExploreUiRects.Clear();
            if (InventoryOpen)
            {
                // «Лялька» (UI Toolkit) на весь екран: IMGUI малюється поверх неї, тож тут — нічого,
                // і клік крізь екран не веде героя.
                ExploreUiRects.Add(new Rect(0f, 0f, w, h));
                return;
            }

            float top;
            float rightReserve;
            if (ToolkitHudActive)
            {
                var frame = HudLayout.For(w, h, exploring: true);
                ExploreUiRects.Add(new Rect(frame.Header.X, frame.Header.Y, frame.Header.Width, frame.Header.Height));
                ExploreUiRects.Add(new Rect(frame.Feed.X, frame.Feed.Y, frame.Feed.Width, frame.Feed.Height));
                top = frame.Header.Y + frame.Header.Height + 8f;
                rightReserve = frame.Feed.Width + 24f;
            }
            else
            {
                var headerRect = new Rect(0f, 0f, w, 72f);
                GUILayout.BeginArea(headerRect);
                DrawTopBar();
                GUILayout.EndArea();
                ExploreUiRects.Add(headerRect);
                top = 80f;
                rightReserve = 24f;
            }

            float barH = 146f;
            float barW = Math.Min(w - 32f, 1100f);
            var bar = new Rect((w - barW) * 0.5f, h - barH - 6f, barW, barH);

            if (_layers.OpenPanel != UxPanelId.None)
            {
                float panelW = Math.Min(w * 0.55f, w - rightReserve - 32f);
                var panelRect = new Rect(16f, top, Math.Max(360f, panelW), Math.Max(200f, bar.y - top - 8f));
                var model = UxPanelFactory.Build(this, _layers.OpenPanel, _panelContext);
                _panelView.Draw(panelRect, model, _panelContext, this, g);
                ExploreUiRects.Add(panelRect);
            }

            DrawWorldBar(bar, g);
            ExploreUiRects.Add(bar);

            string toast = _toasts != null ? _toasts.Current : null;
            if (!string.IsNullOrEmpty(toast))
            {
                var style = new GUIStyle(AlphaSkin.Body) { wordWrap = true, alignment = TextAnchor.MiddleCenter };
                float tw = Math.Min(barW, 760f);
                var toastRect = new Rect((w - tw) * 0.5f, bar.y - 64f, tw, 54f);
                Widgets.SolidRect(toastRect, AlphaSkin.BgDark);
                GUI.Label(toastRect, toast, style);
            }

            if (KeysHelpOpen) DrawKeysHelp(g);
            if (_layers.ConfirmPending && _pendingConfirm != null) DrawConfirm(g);
        }

        private void DrawWorldBar(Rect bar, Gender g)
        {
            GUILayout.BeginArea(bar, GUI.skin.box);
            var place = NearbyPlace;
            if (place != null)
            {
                int stage = PlaceStage(place);
                string label = UkrainianText.Format("ux.world.prompt", g,
                    "place", VillagePlaces.Describe(place, g, stage, Session.GetRosterView()),
                    "verb", UkrainianText.Get(VerbKey(place), g));
                if (Widgets.PrimaryWrapButton(label)) InteractNearby();
            }
            else
            {
                GUILayout.Label(UkrainianText.Get(_layers.InInterior ? "ux.world.inside_hint" : "ux.world.nothing_near", g), AlphaSkin.Tooltip);
            }

            GUILayout.BeginHorizontal();
            if (_layers.InInterior && Widgets.SecondaryButton(UkrainianText.Get("ux.world.exit", g), GUILayout.ExpandWidth(false)))
                RequestExit();
            if (Widgets.SecondaryButton(UkrainianText.Get("ux.world.people", g), GUILayout.ExpandWidth(false))) TogglePanel(UxPanelId.People);
            if (Widgets.SecondaryButton(UkrainianText.Get("ux.world.journal", g), GUILayout.ExpandWidth(false))) TogglePanel(UxPanelId.Journal);
            if (Widgets.SecondaryButton(UkrainianText.Get("ux.world.duty", g), GUILayout.ExpandWidth(false))) TogglePanel(UxPanelId.DutyBoard);
            GUILayout.FlexibleSpace();
            if (Widgets.PrimaryButton(UkrainianText.Get("ui.start_day", g), GUILayout.ExpandWidth(false)))
                RequestStartDay();
            GUILayout.EndHorizontal();
            GUILayout.Label(UkrainianText.Get("ux.world.keys", g), AlphaSkin.HintLine);
            GUILayout.EndArea();
        }

        /// <summary>Стадія будівлі місця (для підпису «будується, k з 5»).</summary>
        private int PlaceStage(WalkPlace place)
        {
            if (place == null || string.IsNullOrEmpty(place.BuildingId)) return 0;
            bool built;
            return UxBricks.Stage(Session.GetCityView(), place.BuildingId, out built);
        }

        private static string VerbKey(WalkPlace place)
        {
            switch (place.Kind)
            {
                case PlaceKind.Building: return place.Panel == UxPanelId.None ? "ux.verb.enter" : "ux.verb.look";
                case PlaceKind.Plot: return "ux.verb.build";
                case PlaceKind.Person: return "ux.verb.talk";
                case PlaceKind.Exit: return "ux.verb.exit";
                default: return "ux.verb.open";
            }
        }

        private void DrawConfirm(Gender g)
        {
            var confirm = _pendingConfirm;
            Widgets.Modal(confirm.Question, () =>
            {
                foreach (var loss in confirm.Losses)
                    GUILayout.Label("— " + loss, AlphaSkin.Body);
                GUILayout.Space(12f);
                GUILayout.BeginHorizontal();
                if (Widgets.PrimaryButton(UkrainianText.Get("ux.common.cancel", g))) ClearPendingConfirm();
                if (Widgets.DangerButton(confirm.ConfirmVerb))
                {
                    var run = _pendingConfirmRun;
                    string id = _pendingConfirmActionId;
                    ClearPendingConfirm();
                    if (run != null) Report(id, run());
                }
                GUILayout.EndHorizontal();
            });
        }

        private void DrawKeysHelp(Gender g)
        {
            Widgets.Modal(UkrainianText.Get("ux.keys.title", g), () =>
            {
                foreach (var key in new[] { "ux.keys.move", "ux.keys.click", "ux.keys.e", "ux.keys.tab", "ux.keys.panels", "ux.keys.esc", "ux.keys.f10" })
                    GUILayout.Label(UkrainianText.Get(key, g), AlphaSkin.Body);
                GUILayout.Space(8f);
                if (Widgets.PrimaryButton(UkrainianText.Get("ux.common.close", g))) KeysHelpOpen = false;
            });
        }

        // ===================== «Почати день» =====================

        /// <summary>
        /// «Почати день» з м'якою перевіркою (UX_DESIGN §5.3, UX-12): порожні
        /// відкриті пости, невитрачені очки розвитку → «Почати все одно» /
        /// «Повернутися». Автотур кличе <see cref="StartDay(GameSession)"/> напряму.
        /// </summary>
        public void RequestStartDay()
        {
            var warnings = UxPreflight.Check(Session, ProtagonistGender);
            if (warnings.Count == 0) { StartDayNow(); return; }
            _pendingConfirm = new UxConfirm(UkrainianText.Get("ux.preflight.question", ProtagonistGender),
                UkrainianText.Get("ux.preflight.verb", ProtagonistGender), warnings);
            _pendingConfirmActionId = "start_day";
            _pendingConfirmRun = () => { StartDayNow(); return UxOutcome.Success(); };
            _layers.AskConfirm();
        }

        private void StartDayNow()
        {
            ClosePanel();
            if (_layers.InInterior) RequestExit();
            TryRun(() => StartDay(Session));
        }

        /// <summary>
        /// Дія «Почати день»: підтвердити ранок і прокрутити день до першого
        /// рішення або до вечора. Раніше кнопка лише підтверджувала ранок, а
        /// сам день (<see cref="GameSession.AdvanceDay"/>) не кликав ніхто —
        /// людина застрягала на першому ранку (аудит журналу 25.09.2026). І
        /// кнопка, і водій автотуру йдуть через цей метод.
        /// </summary>
        public static void StartDay(GameSession s)
        {
            if (s.State == SessionState.Morning || s.State == SessionState.FreePlay)
                s.ConfirmMorning();
            if (s.State == SessionState.Day)
                s.AdvanceDay();
        }
    }
}
