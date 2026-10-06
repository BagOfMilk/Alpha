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
    public sealed partial class GameShell : IUxHost, IUxRenderHost, IWorldInput
    {
        private readonly UxPanelState _panelState = new UxPanelState();
        private readonly UxInlineRefusals _refusals = new UxInlineRefusals();
        private readonly UxPanelView _panelView = new UxPanelView();
        private UxToastQueue _toasts;
        private UxInputCore _ux;

        /// <summary>
        /// Шари, панель і підтвердження — у спільній чистій моделі (<see cref="UxInputCore"/>):
        /// людина, автотур і headless-бот кличуть ті самі методи (Поправка №18, паритет входу).
        /// </summary>
        public UxInputCore Ux
        {
            get
            {
                if (_ux != null) return _ux;
                _ux = new UxInputCore(this);
                _ux.Reported += Report;
                _ux.Opened += _refusals.Clear;
                return _ux;
            }
        }

        /// <summary>Вхід у панелі для автотуру (UI-14) — той самий, що клік.</summary>
        public IUxInput UxInput => Ux;

        /// <summary>Огляд міста (Tab): підписи всіх місць зі станом (UX_DESIGN §3.6).</summary>
        public bool Overview { get; private set; }

        /// <summary>Довідка клавіш (F1).</summary>
        public bool KeysHelpOpen { get; private set; }

        public UxPanelId OpenPanelId => Ux.OpenPanel;
        public string OpenPanelContext => Ux.Context;

        /// <summary>Герой у кімнаті будівлі: id будівлі або null (село).</summary>
        public string InteriorBuildingId => Ux.Layers.InteriorId;

        /// <summary>Запит «увійти» / «вийти» для HeroWalker (той веде героя і перемикає розташування).</summary>
        public string PendingEnter { get; private set; }
        public bool PendingExit { get; private set; }

        /// <summary>Модель відкритої панелі (для автотуру: знайти дію за id).</summary>
        public UxPanelModel CurrentPanelModel() => Ux.CurrentModel();

        // ===================== IWorldInput =====================

        /// <summary>Місце під курсором — пише HeroWalker.</summary>
        public string HoveredPlaceId { get; set; }
        public bool InInterior => Ux.Layers.InInterior;
        string IWorldInput.InteriorId => InteriorBuildingId;

        /// <summary>Увійти: швидко (затемнення одразу) або пішки до дверей і увійти, як клік по будівлі.</summary>
        public void RequestEnter(string buildingId, bool quick)
        {
            // Як клік по дверях: лише зведена вхідна будівля і лише з села (огляд 06.10.2026 —
            // швидкий вхід інакше заводив у кімнату незведеної будівлі чи з однієї кімнати в іншу).
            var building = BuildingCatalog.Get(buildingId);
            if (building == null || !building.Enterable || InInterior || !UxBricks.IsBuilt(Session.GetCityView(), buildingId)) return;
            if (quick) RequestEnter(buildingId);
            else RequestWalkTo(VillagePlaces.BuildingPrefix + buildingId, interactOnArrival: true);
        }

        // ===================== IUxHost =====================

        Gender IUxHost.Gender => ProtagonistGender;
        public UxPanelState PanelState => _panelState;

        public void OpenPanel(UxPanelId panel, string context) => Ux.Open(panel, context);

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
            Ux.ClosePanel();
            Ux.Layers.ExitInterior();
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

        public void RunAction(UxAction action) => Ux.Run(action);

        /// <summary>
        /// Дія автотуру за id у відкритій панелі — той самий шлях, що клік (UI-14).
        /// Незворотну дію автотур підтверджує явно (<paramref name="confirm"/>) —
        /// той самий крок, що кнопка діалогу.
        /// </summary>
        public UxOutcome InvokePanelAction(string actionId, bool confirm = true)
        {
            var outcome = Ux.Invoke(actionId);
            if (!outcome.AwaitingConfirm) return outcome;
            if (!confirm) { Ux.CancelConfirm(); return UxOutcome.Refused(null); }
            return Ux.Confirm();
        }

        public void Link(string placeId) => WalkTo(placeId);

        /// <summary>
        /// «У головне меню» (UX_DESIGN §5.10, §5.14): оболонка відкидає поточну
        /// сесію і створює свіжу в стані «Титул» — як на старті гри. Ядро не
        /// змінюється. Незбережене — лише після підтвердження в меню паузи.
        /// </summary>
        public void ReturnToTitle()
        {
            SetEscapeOpen(false);
            Ux.ClosePanel();
            Ux.Layers.ExitInterior();
            _panelState.Reset();
            _scene.Begin(null);
            Session = new GameSession(_roller);
            _persistedAutosaveVersion = Session.AutosaveVersion;
            LastMessage = string.Empty;
            FeedVillageStage();
        }

        public void ClosePanel() => Ux.ClosePanel();

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
            Ux.Ask(confirm, "confirm", () => { run(); return UxOutcome.Success(); });
        }

        /// <summary>
        /// Шари поверх екранів вечора, ночі й данжу: панель (збереження з
        /// Esc, C, J), діалог підтвердження, тост. У селі це малює <see cref="DrawWorld"/>.
        /// </summary>
        private void DrawOverlays()
        {
            var g = ProtagonistGender;
            float w = Screen.width, h = Screen.height;
            if (Ux.OpenPanel != UxPanelId.None)
            {
                var frame = HudLayout.For(w, h);
                float top = ToolkitHudActive ? frame.Header.Y + frame.Header.Height + 8f : 80f;
                var panelRect = new Rect(16f, top, Math.Max(360f, Math.Min(w * 0.55f, w - 48f)), Math.Max(200f, h - top - 24f));
                _panelView.Draw(panelRect, Ux.CurrentModel(), Ux.Context, this, g);
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
            if (Ux.ConfirmPending) DrawConfirm(g);
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
            if (!Ux.Layers.InInterior) return;
            ClosePanel();
            PendingExit = true;
        }

        /// <summary>HeroWalker: герой уже в кімнаті / уже в селі.</summary>
        public void MarkInterior(string buildingId)
        {
            PendingEnter = null;
            Ux.Layers.EnterInterior(buildingId);
        }

        public void MarkVillage()
        {
            PendingExit = false;
            PendingEnter = null;
            Ux.Layers.ExitInterior();
        }

        // ===================== клавіші =====================

        /// <summary>
        /// Клавіші шару місць (UX_DESIGN §3.6): F, Tab, N, C, J, F10, F1, Enter. Кожна — одного власника (UxKeyMap).
        /// Взаємодія — F (Поправка №18.5): Q/E обертають камеру, як у бою й у Wasteland 3 (UX-17).
        /// </summary>
        private bool HandleWorldKeys(Event evt)
        {
            if (evt == null || evt.type != EventType.KeyDown || !CanExplore) return false;
            if (Ux.ConfirmPending) return false;
            switch (evt.keyCode)
            {
                case KeyCode.F:
                    if (NearbyPlace == null) return false;
                    InteractNearby();
                    return true;
                case KeyCode.Tab:
                    Overview = !Overview;
                    return true;
                case KeyCode.N: TogglePanel(UxPanelId.DutyBoard); return true;
                case KeyCode.C: TogglePanel(UxPanelId.People); return true;
                case KeyCode.J: TogglePanel(UxPanelId.Journal); return true;
                case KeyCode.F10: TogglePanel(UxPanelId.MechanicsJournal); return true;
                case KeyCode.F1: KeysHelpOpen = !KeysHelpOpen; return true;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    // Головна кнопка фази (HP-2, Поправка №18): лише коли зверху світ — з панелі Enter не стартує день.
                    if (Ux.OpenPanel != UxPanelId.None) return false;
                    RequestStartDay();
                    return true;
                default: return false;
            }
        }

        private void TogglePanel(UxPanelId panel)
        {
            if (Ux.OpenPanel == panel) ClosePanel();
            else OpenPanel(panel, null);
        }

        /// <summary>Esc спершу знімає підтвердження, потім панель, довідку; і лише тоді — пауза. З будівлі Esc не виводить.</summary>
        private bool EscapeClosesLayer()
        {
            if (Ux.ConfirmPending) { Ux.CancelConfirm(); return true; }
            if (Ux.OpenPanel != UxPanelId.None) { ClosePanel(); return true; }
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

            if (Ux.OpenPanel != UxPanelId.None)
            {
                float panelW = Math.Min(w * 0.55f, w - rightReserve - 32f);
                var panelRect = new Rect(16f, top, Math.Max(360f, panelW), Math.Max(200f, bar.y - top - 8f));
                _panelView.Draw(panelRect, Ux.CurrentModel(), Ux.Context, this, g);
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
            if (Ux.ConfirmPending) DrawConfirm(g);
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
                GUILayout.Label(UkrainianText.Get(Ux.Layers.InInterior ? "ux.world.inside_hint" : "ux.world.nothing_near", g), AlphaSkin.Tooltip);
            }

            GUILayout.BeginHorizontal();
            if (Ux.Layers.InInterior && Widgets.SecondaryButton(UkrainianText.Get("ux.world.exit", g), GUILayout.ExpandWidth(false)))
                RequestExit();
            if (Widgets.SecondaryButton(UkrainianText.Get("ux.world.people", g), GUILayout.ExpandWidth(false))) TogglePanel(UxPanelId.People);
            if (Widgets.SecondaryButton(UkrainianText.Get("ux.world.journal", g), GUILayout.ExpandWidth(false))) TogglePanel(UxPanelId.Journal);
            if (Widgets.SecondaryButton(UkrainianText.Get("ux.world.duty", g), GUILayout.ExpandWidth(false))) TogglePanel(UxPanelId.DutyBoard);
            GUILayout.FlexibleSpace();
            if (Widgets.PrimaryButton(UkrainianText.Get("ux.world.start_day", g), GUILayout.ExpandWidth(false)))
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
            var confirm = Ux.PendingConfirm;
            Widgets.Modal(confirm.Question, () =>
            {
                foreach (var loss in confirm.Losses)
                    GUILayout.Label("— " + loss, AlphaSkin.Body);
                GUILayout.Space(12f);
                GUILayout.BeginHorizontal();
                if (Widgets.PrimaryButton(UkrainianText.Get("ux.common.cancel", g))) Ux.CancelConfirm();
                if (Widgets.DangerButton(confirm.ConfirmVerb)) Ux.Confirm();
                GUILayout.EndHorizontal();
            });
        }

        private void DrawKeysHelp(Gender g)
        {
            Widgets.Modal(UkrainianText.Get("ux.keys.title", g), () =>
            {
                foreach (var key in new[] { "ux.keys.move", "ux.keys.click", "ux.keys.e", "ux.keys.enter", "ux.keys.tab", "ux.keys.panels", "ux.keys.esc", "ux.keys.f10" })
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
            Ux.Ask(new UxConfirm(UkrainianText.Get("ux.preflight.question", ProtagonistGender),
                UkrainianText.Get("ux.preflight.verb", ProtagonistGender), warnings),
                "start_day", () => { StartDayNow(); return UxOutcome.Success(); });
        }

        private void StartDayNow()
        {
            ClosePanel();
            if (Ux.Layers.InInterior) RequestExit();
            TryRun(() => StartDay(Session));
        }

        /// <summary>
        /// Дія «Почати день»: підтвердити ранок і прокрутити день до першого
        /// рішення або до вечора. Раніше кнопка лише підтверджувала ранок, а
        /// сам день (<see cref="GameSession.AdvanceDay"/>) не кликав ніхто —
        /// людина застрягала на першому ранку (аудит журналу 25.09.2026). І
        /// кнопка, і водій автотуру йдуть через цей метод.
        /// </summary>
        public static void StartDay(GameSession s) => UxPhaseButton.StartDay(s);
    }
}
