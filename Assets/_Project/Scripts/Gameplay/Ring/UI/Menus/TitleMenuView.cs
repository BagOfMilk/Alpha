using System;
using System.Collections.Generic;
using Game.Gameplay.Ring.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.Ring.UI.Menus
{
    /// <summary>
    /// Екран входу = титул (рішення власника 07.11.2026: «Титул = вхід»; ТЗ B01 v2 + B08, мова «Енергія»). Акаунтів у грі
    /// немає, тож «логін» — це вхід у кар'єру: «Продовжити» перевіряє сейв (стан «перевірка» зі спінером), зіпсований сейв —
    /// помилка (тремтіння, сигнал-смужка, звук), успіх — розріз і екран завантаження. Позаду — 3D-зала (ринг у правій третині).
    /// </summary>
    public sealed class TitleMenuView : CareerScreenView, IMenuNavigable
    {
        private enum Item { Continue = 0, New, Settings, Credits, Quit }
        private enum Mode { Intro, Idle, Checking, Leaving, Sub }

        private const float PlateW = 450f, PlateH = 60f, PlateGap = 10f, Step = 14f, FocusShift = 25f;

        private readonly IMenuHost _menu;
        private readonly MenuAnimator _anim = new MenuAnimator();
        private readonly MenuFocus _focus = new MenuFocus();
        private readonly List<MenuButton> _plates = new List<MenuButton>();
        private readonly List<Item> _items = new List<Item>();
        private VisualElement _lockup, _seal, _frame, _sub, _column, _status, _footer, _lines;
        private MenuBackdrop _back;
        private Label _word, _hints, _statusText;
        private SlashSpinner _spinner;
        private MenuDialog _dialog;
        private SettingsPanel _settings;
        private CreditsPanel _credits;
        private Mode _mode = Mode.Intro;
        private float _clock, _checkClock, _errorClock = -1f, _drift;
        private string _pendingError, _summary;
        private bool _built;

        public TitleMenuView(RingUiContext ctx, IMenuHost menu) : base(ctx)
        {
            _menu = menu;
            Root.style.backgroundColor = Color.clear;
        }

        /// <summary>Для огляду: зараз відкритий підекран (налаштування, титри, діалог) — бот кличе його.</summary>
        public SettingsPanel Settings { get { return _settings; } }
        public bool Idle { get { return _mode == Mode.Idle && _dialog == null && !_anim.Busy; } }

        public override void Refresh()
        {
            if (_built) return;    // екран будується раз: рух живе в аніматорі, перебудова зламала б його
            _built = true;
            Root.Clear();
            Build();
            PlayIntro();
            // титул — меню: музика зали (якщо гравець повернувся з тижня) стихає, грає тема входу
            RingMenuServices.MenuMusicActive = true;
            if (RingMenuServices.GameMusic != null) RingMenuServices.GameMusic(false);
            RingMenuServices.Music(MenuMusic.Login);
        }

        private void Build()
        {
            _back = new MenuBackdrop { WedgeWidth = CareerTileLayout.TitleWedge, Scrim = 0.18f, WedgeAlpha = 0.78f, CrimsonCut = true };
            Root.Add(_back);
            _lines = new SpeedLines(3);
            Root.Add(_lines);

            // ---- лого-локап (B01 §3): печатка + канатна рамка + живе слово «РИНГ» ----
            _lockup = new VisualElement();
            _lockup.style.position = Position.Absolute;
            _lockup.style.left = 80f; _lockup.style.top = 70f;
            _lockup.style.flexDirection = FlexDirection.Row;
            _lockup.style.alignItems = Align.Center;
            _lockup.pickingMode = PickingMode.Ignore;
            _seal = new VisualElement();
            _seal.style.width = 98f; _seal.style.height = 98f; _seal.style.marginRight = 17f;
            if (RingMenuArt.Seal != null) _seal.style.backgroundImage = new StyleBackground(RingMenuArt.Seal);
            _lockup.Add(_seal);
            _frame = new VisualElement();
            _frame.style.width = 492f; _frame.style.height = 151f;
            _frame.style.justifyContent = Justify.Center;
            _frame.style.alignItems = Align.Center;
            if (RingMenuArt.RopeFrame != null) _frame.style.backgroundImage = new StyleBackground(RingMenuArt.RopeFrame);
            _word = MenuKit.Display(T.Get("c.game.title"), 132f, RingTheme.Text, RingFontRole.Display900, 0.02f);
            _word.style.unityFontStyleAndWeight = FontStyle.Italic;
            // тінь слова — чорна: кармінна зливалась би з кармінним клином (палітра «Арена»)
            var shadow = MenuKit.Display(T.Get("c.game.title"), 132f, RingTheme.Bg, RingFontRole.Display900, 0.02f);
            shadow.style.unityFontStyleAndWeight = FontStyle.Italic;
            shadow.style.position = Position.Absolute;
            shadow.style.translate = new Translate(new Length(BrandTokens.SpaceShadowOffset), new Length(BrandTokens.SpaceShadowOffset * 1.25f));
            var wordBox = new VisualElement();
            wordBox.pickingMode = PickingMode.Ignore;
            wordBox.Add(shadow);
            wordBox.Add(_word);
            _frame.Add(wordBox);
            _lockup.Add(_frame);
            Root.Add(_lockup);
            _sub = MenuKit.Display(T.Get("m.title.sub"), 18f, RingTheme.TextDim, RingFontRole.Display700, 0.14f);
            _sub.style.position = Position.Absolute;
            _sub.style.left = 95f; _sub.style.top = 240f;
            Root.Add(_sub);
            // на вузькій панелі (4:3) підпис виходив за клин (кадр m4x3\053 слоту 30): клин розширюється до кінця підпису
            _sub.RegisterCallback<GeometryChangedEvent>(e => FitWedge());
            Root.RegisterCallback<GeometryChangedEvent>(e => FitWedge());

            // ---- колонка пластин (B01 §4) зі сходинкою (мова «Енергія») ----
            _column = new VisualElement();
            _column.style.position = Position.Absolute;
            _column.style.left = 80f; _column.style.bottom = 900f - 783f - PlateGap;   // нижній край останньої пластини — 783 (B01 §4)
            _column.pickingMode = PickingMode.Ignore;
            Root.Add(_column);
            _items.Clear();
            _summary = _menu.HasSave ? _menu.SaveSummary : null;
            if (_menu.HasSave) _items.Add(Item.Continue);
            _items.Add(Item.New); _items.Add(Item.Settings); _items.Add(Item.Credits); _items.Add(Item.Quit);
            var enabled = new List<bool>();
            for (int i = 0; i < _items.Count; i++)
            {
                int idx = i;
                var it = _items[i];
                string hint = it == Item.Continue ? _summary : null;
                var b = MenuKit.Button(T.Get(ItemKey(it)), PlateW, PlateH, hint);
                b.style.marginBottom = PlateGap;
                b.style.marginLeft = i * Step;
                if (it == Item.Quit) b.Plate.Stripe = 10f;
                b.OnHover = () => { if (RingMenuInput.MouseArmed && _mode == Mode.Idle && _dialog == null && _focus.Set(idx)) { SyncFocus(); RingMenuServices.Play(MenuSfx.Move); } };
                b.OnClick = () => { if (_mode == Mode.Idle && _dialog == null) { _focus.Set(idx); SyncFocus(); Activate(); } };
                _plates.Add(b);
                enabled.Add(true);
                _column.Add(b);
            }
            _focus.Reset(enabled, 0);   // за замовчуванням: «Продовжити» є — він, інакше «Нова кар'єра» (B01 §4)

            // ---- рядок стану: перевірка сейву / помилка ----
            _status = new VisualElement();
            _status.style.position = Position.Absolute;
            _status.style.left = 80f; _status.style.top = 800f;   // під колонкою (низ пластин — 783 @900)
            _status.style.flexDirection = FlexDirection.Row;
            MenuKit.Underlay(_status);
            _status.style.alignItems = Align.Center;
            _status.pickingMode = PickingMode.Ignore;
            _spinner = new SlashSpinner();
            _spinner.style.marginRight = 12f;
            _status.Add(_spinner);
            _statusText = MenuKit.Body("", BrandTokens.TypeBody, RingTheme.Text, true);
            _status.Add(_statusText);
            ShowStatus(false);
            Root.Add(_status);

            _footer = MenuKit.Footer(out _hints, _menu.Version);
            Root.Add(_footer);
            // клік мишею під час появи — пропуск (як будь-яка клавіша)
            Root.RegisterCallback<PointerDownEvent>(e => { if (_mode == Mode.Intro) SkipIntro(); }, TrickleDown.TrickleDown);
            UpdateHints(MenuDevice.Keyboard);
            SyncFocus(true);
        }

        /// <summary>Клин тла — не вужчий, ніж треба, щоб підпис лежав на ньому з запасом (CareerTileLayout.FitTitleWedge).</summary>
        private void FitWedge()
        {
            if (_back == null || _sub == null || _sub.style.display.value == DisplayStyle.None) return;
            var sub = _sub.layout;
            if (float.IsNaN(sub.width) || sub.width <= 0f) return;
            float w = CareerTileLayout.FitTitleWedge(Root.layout.width, Root.layout.height, sub.xMax, sub.yMax);
            if (Math.Abs(w - _back.WedgeWidth) < 0.001f) return;
            _back.WedgeWidth = w;
            _back.MarkDirtyRepaint();
        }

        private static string ItemKey(Item it)
        {
            switch (it)
            {
                case Item.Continue: return "m.title.continue";
                case Item.New: return "m.title.new";
                case Item.Settings: return "m.title.settings";
                case Item.Credits: return "m.title.credits";
                default: return "m.title.quit";
            }
        }

        // ---------------- рух ----------------

        /// <summary>Поява ≤ 2 с (B01 §5): печатка, рамка, удар слова, підпис, пластини з перельотом, підвал. Будь-яка дія — пропуск.</summary>
        private void PlayIntro()
        {
            _mode = Mode.Intro;
            _anim.FromTo(_seal, MenuProp.Opacity, 0f, 1f, 200f, Ease.OutCubic, 300f);
            _anim.FromTo(_seal, MenuProp.Scale, 0.85f, 1f, 200f, Ease.OutCubic, 300f);
            _anim.FromTo(_frame, MenuProp.Opacity, 0f, 1f, 400f, Ease.OutCubic, 400f);
            _anim.FromTo(_frame, MenuProp.X, -60f, 0f, 400f, Ease.OutCubic, 400f);
            _anim.FromTo(_word, MenuProp.Opacity, 0f, 1f, 1f, Ease.Linear, 800f);
            _anim.FromTo(_word, MenuProp.Scale, 1.25f, 1f, BrandTokens.MotionStingerInMs, Ease.OutBack, 800f,
                () => { _shakeLockup = 250f; RingMenuServices.Play(MenuSfx.Transition); });
            _anim.FromTo(_sub, MenuProp.Opacity, 0f, 1f, 200f, Ease.OutCubic, 1000f);
            for (int i = 0; i < _plates.Count; i++)
            {
                float d = 1200f + MenuMotion.StaggerMs(i);
                // «Енергія»: пластина влітає з-за лівого краю під кутом і пружно сідає
                _anim.FromTo(_plates[i], MenuProp.X, -160f, 0f, BrandTokens.MotionMenuPlateInMs, Ease.OutBack, d);
                _anim.FromTo(_plates[i], MenuProp.Rotate, -6f, 0f, BrandTokens.MotionMenuPlateInMs, Ease.OutBack, d);
                _anim.FromTo(_plates[i], MenuProp.Opacity, 0f, 1f, BrandTokens.MotionMenuPlateInMs, Ease.OutCubic, d);
            }
            _anim.FromTo(_footer, MenuProp.Opacity, 0f, 1f, 200f, Ease.OutCubic, 1800f, () => { if (_mode == Mode.Intro) _mode = Mode.Idle; });
        }

        private float _shakeLockup;
        private bool _statusShown;

        private void ShowStatus(bool on)
        {
            _statusShown = on;
            _status.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void SkipIntro()
        {
            foreach (var e in new[] { _seal, _frame, _word, _sub, _footer })
            {
                _anim.Set(e, MenuProp.Opacity, 1f);
                _anim.Set(e, MenuProp.Scale, 1f);
                _anim.Set(e, MenuProp.X, 0f);
            }
            foreach (var p in _plates)
            {
                _anim.Set(p, MenuProp.X, 0f);
                _anim.Set(p, MenuProp.Rotate, 0f);
                _anim.Set(p, MenuProp.Opacity, 1f);
            }
            _mode = Mode.Idle;
        }

        private void SyncFocus(bool instant = false)
        {
            for (int i = 0; i < _plates.Count; i++)
            {
                bool f = i == _focus.Index;
                var b = _plates[i];
                if (b.State != PlateState.Error || f) b.State = f ? PlateState.Focus : PlateState.Normal;
                float x = f ? FocusShift : 0f;
                if (instant) { _anim.Set(b.Plate, MenuProp.X, x); _anim.Set(b.Text, MenuProp.X, x); _anim.Set(b.Hint, MenuProp.X, x); }
                else
                {
                    _anim.To(b.Plate, MenuProp.X, x, BrandTokens.MotionMenuFocusMs, Ease.OutCubic);
                    _anim.To(b.Text, MenuProp.X, x, BrandTokens.MotionMenuFocusMs, Ease.OutCubic);
                    _anim.To(b.Hint, MenuProp.X, x, BrandTokens.MotionMenuFocusMs, Ease.OutCubic);
                }
                // підказка праворуч у пластині фокусу (B01 §4): Enter або підсумок сейву
                if (_items[i] == Item.Continue) b.SetHint(_summary);
                else b.SetHint(f ? T.Get("m.hint.enter") : null);
            }
        }

        private void UpdateHints(MenuDevice d)
        {
            if (_hints == null) return;
            _hints.text = T.Get(d == MenuDevice.Gamepad ? "m.footer.title.pad" : "m.footer.title.keys");
        }

        // ---------------- дії ----------------

        private void Activate()
        {
            if (_focus.Index < 0) return;
            ClearError();
            var b = _plates[_focus.Index];
            b.State = PlateState.Pressed;
            _anim.FromTo(b, MenuProp.Scale, 0.98f, 1f, BrandTokens.MotionMenuPressMs, Ease.OutCubic);
            switch (_items[_focus.Index])
            {
                case Item.Continue:
                    RingMenuServices.Play(MenuSfx.Confirm);
                    BeginCheck();
                    break;
                case Item.New:
                    RingMenuServices.Play(MenuSfx.Confirm);
                    if (!_menu.HasSave) { Leave(false); break; }
                    // нова кар'єра перепише наявний сейв після першого кроку — спершу питаємо
                    _dialog = new MenuDialog(_anim, T.Get("m.new.title"), T.Get("m.new.body"),
                        new[]
                        {
                            new KeyValuePair<string, Action>(T.Get("m.new.yes"), () => { _dialog = null; ShowColumn(true); Leave(false); }),
                            new KeyValuePair<string, Action>(T.Get("m.new.no"), () => { _dialog = null; ShowColumn(true); SyncFocus(); }),
                        }, 1, 1);
                    ShowColumn(false);
                    Root.Add(_dialog.Root);
                    break;
                case Item.Settings:
                    RingMenuServices.Play(MenuSfx.Confirm);
                    OpenSettings();
                    break;
                case Item.Credits:
                    RingMenuServices.Play(MenuSfx.Confirm);
                    OpenCredits();
                    break;
                case Item.Quit:
                    RingMenuServices.Play(MenuSfx.Confirm);
                    _dialog = new MenuDialog(_anim, T.Get("m.quit.title"), T.Get("m.quit.body"),
                        new[]
                        {
                            new KeyValuePair<string, Action>(T.Get("m.quit.yes"), () => { _dialog = null; _menu.Quit(); }),
                            new KeyValuePair<string, Action>(T.Get("m.quit.no"), () => { _dialog = null; ShowColumn(true); SyncFocus(); }),
                        }, 1, 1);
                    ShowColumn(false);
                    Root.Add(_dialog.Root);
                    break;
            }
        }

        /// <summary>Стан «перевірка»: спінер щонайменше 0,45 с (щоб не блимав), сейв перевіряється без зміни гри.</summary>
        private void BeginCheck()
        {
            _mode = Mode.Checking;
            _checkClock = 0f;
            string err;
            _pendingError = _menu.CheckSave(out err) ? null : err ?? "m.error.save";
            ShowStatus(true);
            _spinner.style.display = DisplayStyle.Flex;
            _statusText.text = T.Get("m.title.checking");
            _statusText.style.color = RingTheme.Text;
            _anim.FromTo(_status, MenuProp.Opacity, 0f, 1f, BrandTokens.MotionMenuFocusMs, Ease.OutCubic);
        }

        private void FinishCheck()
        {
            if (_pendingError == null) { Leave(true); return; }
            // помилка: тремтіння пластини, сигнал-смужка й обведення, звук помилки, речення пояснення
            var b = _plates[_focus.Index];
            b.State = PlateState.Error;
            _errorClock = 0f;
            _spinner.style.display = DisplayStyle.None;
            _statusText.text = T.Get(_pendingError);
            _statusText.style.color = RingTheme.Danger;
            RingMenuServices.Play(MenuSfx.Error);
            _mode = Mode.Idle;
        }

        /// <summary>Успіх: пластини виїжджають ліворуч (150 мс), звук входу, хост робить розріз і завантаження.</summary>
        private void Leave(bool continueSave)
        {
            _mode = Mode.Leaving;
            ShowStatus(false);
            if (continueSave) RingMenuServices.Play(MenuSfx.LoginOk);
            for (int i = 0; i < _plates.Count; i++)
            {
                _anim.To(_plates[i], MenuProp.X, -PlateW - 120f, BrandTokens.MotionMenuPlateOutMs, Ease.InCubic, i * 25f);
                _anim.To(_plates[i], MenuProp.Opacity, 0f, BrandTokens.MotionMenuPlateOutMs, Ease.InCubic, i * 25f);
            }
            _anim.To(_footer, MenuProp.Opacity, 0f, BrandTokens.MotionMenuPlateOutMs, Ease.InCubic, 0f,
                () => _menu.StartGame(continueSave));
        }

        /// <summary>Під налаштуваннями й титрами титул не просвічує (кадр 08.11.2026): ховаємо лого, колонку, рядок стану, підвал.</summary>
        private void ShowTitleUi(bool on)
        {
            var d = on ? DisplayStyle.Flex : DisplayStyle.None;
            _lockup.style.display = d; _sub.style.display = d; _column.style.display = d; _footer.style.display = d;
            if (!on) ShowStatus(false);
        }

        /// <summary>Під діалогом (вихід, нова кар'єра) колонка пластин і підвал не просвічують — кадри 08.11.2026: кнопки діалогу
        /// лягали на пластини титулу. Лого лишається.</summary>
        private void ShowColumn(bool on)
        {
            var d = on ? DisplayStyle.Flex : DisplayStyle.None;
            _column.style.display = d; _footer.style.display = d;
        }

        private void OpenSettings()
        {
            _mode = Mode.Sub;
            ShowTitleUi(false);
            _settings = new SettingsPanel(T, _menu, _anim, () =>
            {
                _settings.Root.RemoveFromHierarchy();
                _settings = null;
                _mode = Mode.Idle;
                ShowTitleUi(true);
                RingMenuServices.Music(MenuMusic.Login);
                SyncFocus();
            });
            Root.Add(_settings.Root);
            RingMenuServices.Music(MenuMusic.Settings);
        }

        private void OpenCredits()
        {
            _mode = Mode.Sub;
            ShowTitleUi(false);
            _credits = new CreditsPanel(T, _anim, () =>
            {
                _credits.Root.RemoveFromHierarchy();
                _credits = null;
                _mode = Mode.Idle;
                ShowTitleUi(true);
                SyncFocus();
            });
            Root.Add(_credits.Root);
        }

        // ---------------- ввід ----------------

        public void OnMenu(RingAction action, MenuDevice device)
        {
            UpdateHints(device);
            if (_dialog != null && !_dialog.Closed) { _dialog.OnMenu(action, device); return; }
            if (_settings != null) { _settings.OnMenu(action, device); return; }
            if (_credits != null) { _credits.OnMenu(action, device); return; }
            if (_mode == Mode.Intro) { SkipIntro(); return; }
            if (_mode != Mode.Idle) return;
            switch (action)
            {
                case RingAction.Up: if (_focus.Move(-1)) { ClearError(); SyncFocus(); RingMenuServices.Play(MenuSfx.Move); } break;
                case RingAction.Down: if (_focus.Move(1)) { ClearError(); SyncFocus(); RingMenuServices.Play(MenuSfx.Move); } break;
                case RingAction.Confirm: ClearError(); Activate(); break;
                // Esc/B на корені меню — нічого (B01 §4): вихід лише пунктом «Вийти»
            }
        }

        private void ClearError()
        {
            if (_errorClock < 0f && !_statusShown) return;
            _errorClock = -1f;
            ShowStatus(false);
            foreach (var p in _plates) if (p.State == PlateState.Error) p.State = PlateState.Normal;
        }

        // Старий шлях клавіш хоста (цифри, Enter) для бота огляду кар'єри: Enter = дія пластини фокусу.
        public override void OnConfirm() { OnMenu(RingAction.Confirm, MenuDevice.Keyboard); }

        public override void OnDigit(int n) { }

        public override void Tick(float dt)
        {
            float ms = dt * 1000f;
            _clock += ms;
            _anim.Tick(ms);
            if (_settings != null) _settings.Tick(dt);
            if (_mode == Mode.Checking)
            {
                _spinner.Tick(dt);
                _checkClock += ms;
                if (_checkClock >= 450f) FinishCheck();
            }
            if (_errorClock >= 0f && _focus.Index >= 0)
            {
                _errorClock += ms;
                float dx = MenuMotion.Shake(_errorClock, BrandTokens.MotionMenuShakeMs, MenuAnimator.Reduced ? 0f : 9f);
                _anim.Set(_plates[_focus.Index], MenuProp.X, dx);
                if (_errorClock >= BrandTokens.MotionMenuShakeMs) _errorClock = BrandTokens.MotionMenuShakeMs;   // далі стоїть; помилку знімає дія гравця
            }
            if (_shakeLockup > 0f)
            {
                _shakeLockup -= ms;
                float s = MenuAnimator.Reduced ? 0f : MenuMotion.Shake(250f - _shakeLockup, 250f, 4f);
                _lockup.style.translate = new Translate(new Length(s), new Length(-s * 0.5f));
            }
            // живий фон: лінії швидкості повільно пливуть по діагоналі (24 с на цикл); «Менше руху» — стоять
            if (!MenuAnimator.Reduced && _lines != null)
            {
                _drift = (_drift + dt / 24f) % 1f;
                float ph = (float)Math.Sin(_drift * Math.PI * 2.0);
                _lines.style.translate = new Translate(new Length(ph * 60f), new Length(-ph * 22f));
            }
        }
    }
}
