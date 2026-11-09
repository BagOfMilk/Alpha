using System;
using System.Collections.Generic;
using Game.Gameplay.Ring.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.Ring.UI.Menus
{
    /// <summary>
    /// Пауза в грі (ТЗ B01 §6.2, мова «Енергія» B08, палітра «Арена»): затемнення <c>Scrim</c> α <c>AlphaScrim</c>, кармінна смуга навскіс,
    /// заголовок «ПАУЗА» display 62 <c>Gold</c> у нахиленій пластині, по центру три пластини сходинкою — «Продовжити»,
    /// «Налаштування» (той самий <see cref="SettingsPanel"/>, що на титулі), «На титул» (з попередженням, якщо поточне не
    /// збережеться). Час і звук зали стоять, поки пауза відкрита (це робить хост). Нових механік немає.
    /// </summary>
    public sealed class PauseOverlay : IMenuNavigable
    {
        private enum Item { Resume = 0, Settings, Title }

        private const float PlateW = 440f, PlateH = 60f, PlateGap = 10f, Step = 14f, FocusShift = 25f;

        public readonly VisualElement Root;
        private readonly RingText _t;
        private readonly IMenuHost _menu;
        private readonly MenuAnimator _anim = new MenuAnimator();
        private readonly MenuFocus _focus = new MenuFocus();
        private readonly List<MenuButton> _plates = new List<MenuButton>();
        private readonly VisualElement _col, _head, _band, _footer;
        private readonly Label _hints;
        private readonly Action _resume, _toTitle;
        private readonly Func<string> _leaveBodyKey;
        private MenuDialog _dialog;
        private SettingsPanel _settings;
        private bool _closing;

        /// <param name="context">Рядок під заголовком («Сезон 1 · Тиждень 3»), може бути порожнім.</param>
        /// <param name="leaveBodyKey">Ключ пояснення в діалозі «На титул?» — питається в мить натискання (стан гри міг змінитись).</param>
        /// <param name="resume">Пауза закрилась (після анімації виходу).</param>
        /// <param name="toTitle">Гравець підтвердив «На титул» (хост зберігає й веде на титул).</param>
        public PauseOverlay(RingText text, IMenuHost menu, string context, Func<string> leaveBodyKey, Action resume, Action toTitle)
        {
            _t = text; _menu = menu; _leaveBodyKey = leaveBodyKey; _resume = resume; _toTitle = toTitle;
            Root = MenuShapes.Layer();
            Root.pickingMode = PickingMode.Position;           // під паузою екран гри не натискається
            Root.style.backgroundColor = RingTheme.WithAlpha(RingTheme.Scrim, BrandTokens.AlphaScrim);

            _band = new PauseBand();
            Root.Add(_band);

            _col = new VisualElement();
            _col.style.position = Position.Absolute;
            _col.style.left = Length.Percent(50f);
            _col.style.marginLeft = -(PlateW + 2f * Step) * 0.5f;
            _col.style.top = 0f; _col.style.bottom = 0f;
            _col.style.justifyContent = Justify.Center;
            _col.style.alignItems = Align.FlexStart;
            _col.pickingMode = PickingMode.Ignore;
            Root.Add(_col);

            // заголовок: золоте слово в нахиленій пластині (B01 §6.2)
            _head = new VisualElement();
            _head.style.width = 330f; _head.style.height = 86f;
            _head.style.justifyContent = Justify.Center;
            _head.style.overflow = Overflow.Visible;
            _head.pickingMode = PickingMode.Ignore;
            var plate = new SlantPlate();
            plate.style.position = Position.Absolute;
            plate.style.left = 0f; plate.style.top = 0f; plate.style.right = 0f; plate.style.bottom = 0f;
            plate.pickingMode = PickingMode.Ignore;
            _head.Add(plate);
            var word = MenuKit.Display(_t.Get("m.pause.title"), 62f, RingTheme.Gold, RingFontRole.Display900, 0.04f);
            word.style.unityFontStyleAndWeight = FontStyle.Italic;
            word.style.marginLeft = 38f;
            _head.Add(word);
            _col.Add(_head);
            if (!string.IsNullOrEmpty(context))
            {
                var ctx = MenuKit.Display(context, 18f, RingTheme.TextDim, RingFontRole.Display700, 0.14f);
                ctx.style.marginTop = 14f; ctx.style.marginLeft = 6f;
                MenuKit.Underlay(ctx);
                _col.Add(ctx);
            }
            var gap = new VisualElement(); gap.style.height = 26f; gap.pickingMode = PickingMode.Ignore;
            _col.Add(gap);

            var enabled = new List<bool>();
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var b = MenuKit.Button(_t.Get(ItemKey((Item)i)), PlateW, PlateH);
                b.style.marginBottom = PlateGap;
                b.style.marginLeft = i * Step;                  // сходинка — мова «Енергія»
                if ((Item)i == Item.Title) b.Plate.Stripe = 10f;
                b.OnHover = () => { if (RingMenuInput.MouseArmed && Idle && _focus.Set(idx)) { SyncFocus(); RingMenuServices.Play(MenuSfx.Move); } };
                b.OnClick = () => { if (Idle) { _focus.Set(idx); SyncFocus(); Activate(); } };
                _plates.Add(b);
                enabled.Add(true);
                _col.Add(b);
            }
            _focus.Reset(enabled, 0);   // за замовчуванням — «Продовжити»: Esc/Start/Enter одразу повертають у гру

            _footer = MenuKit.Footer(out _hints, null);
            // під паузою — ряд плиток тижня: напівпрозора підкладка змішувала підказку з їхнім текстом (кадр слоту 27, 09.11.2026)
            _hints.style.backgroundColor = RingTheme.WithAlpha(RingTheme.Bg, BrandTokens.AlphaPlate);
            Root.Add(_footer);
            UpdateHints(MenuDevice.Keyboard);
            SyncFocus(true);
            PlayIn();
        }

        /// <summary>Пауза приймає дії (не закривається й над нею немає налаштувань чи діалогу).</summary>
        private bool Idle { get { return !_closing && _dialog == null && _settings == null; } }

        /// <summary>Налаштування відкриті поверх паузи (хост тримає меню в еталонному масштабі).</summary>
        public bool SettingsOpen { get { return _settings != null; } }

        /// <summary>Для огляду меню.</summary>
        public SettingsPanel Settings { get { return _settings; } }
        public bool HasDialog { get { return _dialog != null; } }
        public bool Closing { get { return _closing; } }

        private static string ItemKey(Item it)
        {
            switch (it)
            {
                case Item.Resume: return "m.pause.resume";
                case Item.Settings: return "m.pause.settings";
                default: return "m.pause.totitle";
            }
        }

        // ---------------- рух ----------------

        private void PlayIn()
        {
            _anim.FromTo(Root, MenuProp.Opacity, 0f, 1f, BrandTokens.MotionMenuPlateInMs, Ease.OutCubic);
            _anim.FromTo(_band, MenuProp.X, -90f, 0f, BrandTokens.MotionMenuWipeMs, Ease.OutCubic);
            _anim.FromTo(_head, MenuProp.X, -140f, 0f, BrandTokens.MotionMenuPlateInMs, Ease.OutBack);
            for (int i = 0; i < _plates.Count; i++)
            {
                float d = BrandTokens.MotionMenuStaggerMs + MenuMotion.StaggerMs(i);
                _anim.FromTo(_plates[i], MenuProp.X, -160f, 0f, BrandTokens.MotionMenuPlateInMs, Ease.OutBack, d);
                _anim.FromTo(_plates[i], MenuProp.Rotate, -6f, 0f, BrandTokens.MotionMenuPlateInMs, Ease.OutBack, d);
                _anim.FromTo(_plates[i], MenuProp.Opacity, 0f, 1f, BrandTokens.MotionMenuPlateInMs, Ease.OutCubic, d);
            }
        }

        private void SyncFocus(bool instant = false)
        {
            for (int i = 0; i < _plates.Count; i++)
            {
                bool f = i == _focus.Index;
                var b = _plates[i];
                b.State = f ? PlateState.Focus : PlateState.Normal;
                float x = f ? FocusShift : 0f;
                if (instant) { _anim.Set(b.Plate, MenuProp.X, x); _anim.Set(b.Text, MenuProp.X, x); _anim.Set(b.Hint, MenuProp.X, x); }
                else
                {
                    _anim.To(b.Plate, MenuProp.X, x, BrandTokens.MotionMenuFocusMs, Ease.OutCubic);
                    _anim.To(b.Text, MenuProp.X, x, BrandTokens.MotionMenuFocusMs, Ease.OutCubic);
                    _anim.To(b.Hint, MenuProp.X, x, BrandTokens.MotionMenuFocusMs, Ease.OutCubic);
                }
                b.SetHint(f ? _t.Get("m.hint.enter") : null);
            }
        }

        private void UpdateHints(MenuDevice d)
        {
            if (_hints == null) return;
            _hints.text = _t.Get(d == MenuDevice.Gamepad ? "m.footer.pause.pad" : "m.footer.pause.keys");
        }

        /// <summary>
        /// Під налаштуваннями й діалогом пауза не просвічує (кадри 08.11.2026: пластини паузи проступали крізь кнопки діалогу,
        /// а текст пояснення наїжджав на «Продовжити»). Смуга лишається під діалогом — тло.
        /// </summary>
        private void ShowPauseUi(bool on, bool keepBand = false)
        {
            var d = on ? DisplayStyle.Flex : DisplayStyle.None;
            _col.style.display = d; _footer.style.display = d;
            _band.style.display = on || keepBand ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ---------------- дії ----------------

        private void Activate()
        {
            if (_focus.Index < 0) return;
            var b = _plates[_focus.Index];
            b.State = PlateState.Pressed;
            _anim.FromTo(b, MenuProp.Scale, 0.98f, 1f, BrandTokens.MotionMenuPressMs, Ease.OutCubic);
            switch ((Item)_focus.Index)
            {
                case Item.Resume:
                    Close();
                    break;
                case Item.Settings:
                    RingMenuServices.Play(MenuSfx.Confirm);
                    OpenSettings();
                    break;
                case Item.Title:
                    RingMenuServices.Play(MenuSfx.Confirm);
                    AskTitle();
                    break;
            }
        }

        /// <summary>Вихід з паузи: пластини виїжджають ліворуч (150 мс), шар гасне, далі хост повертає час і звук.</summary>
        public void Close()
        {
            if (_closing) return;
            _closing = true;
            RingMenuServices.Play(MenuSfx.Back);
            for (int i = 0; i < _plates.Count; i++)
                _anim.To(_plates[i], MenuProp.X, -PlateW - 120f, BrandTokens.MotionMenuPlateOutMs, Ease.InCubic, i * 25f);
            _anim.To(_head, MenuProp.X, -200f, BrandTokens.MotionMenuPlateOutMs, Ease.InCubic);
            // дія — на згасанні шару (воно є і в «Менше руху», де зсуви миттєві)
            _anim.To(Root, MenuProp.Opacity, 0f, BrandTokens.MotionMenuPlateOutMs, Ease.InCubic, 50f, () =>
            {
                Root.RemoveFromHierarchy();
                if (_resume != null) _resume();
            });
        }

        private void OpenSettings()
        {
            ShowPauseUi(false);
            _settings = new SettingsPanel(_t, _menu, _anim, () =>
            {
                _settings.Root.RemoveFromHierarchy();
                _anim.Forget(_settings.Root);
                _settings = null;
                ShowPauseUi(true);
                SyncFocus();
            });
            Root.Add(_settings.Root);
        }

        private void AskTitle()
        {
            string bodyKey = _leaveBodyKey != null ? _leaveBodyKey() : PauseRules.LeaveSafeKey;
            bool lossy = bodyKey != PauseRules.LeaveSafeKey;
            ShowPauseUi(false, true);
            _dialog = new MenuDialog(_anim, _t.Get("m.pause.leave.title"), _t.Get(bodyKey),
                new[]
                {
                    new KeyValuePair<string, Action>(_t.Get("m.pause.leave.yes"), () => { _dialog = null; _closing = true; if (_toTitle != null) _toTitle(); }),
                    new KeyValuePair<string, Action>(_t.Get("m.pause.leave.no"), () => { _dialog = null; ShowPauseUi(true); SyncFocus(); }),
                }, 1, lossy ? 1 : 0);   // якщо щось втратиться — фокус на «Ні, лишитися»
            Root.Add(_dialog.Root);
        }

        // ---------------- ввід ----------------

        public void OnMenu(RingAction action, MenuDevice device)
        {
            if (_closing) return;
            UpdateHints(device);
            if (_dialog != null && !_dialog.Closed) { _dialog.OnMenu(action, device); return; }
            if (_settings != null) { _settings.OnMenu(action, device); return; }
            switch (action)
            {
                case RingAction.Up: if (_focus.Move(-1)) { SyncFocus(); RingMenuServices.Play(MenuSfx.Move); } break;
                case RingAction.Down: if (_focus.Move(1)) { SyncFocus(); RingMenuServices.Play(MenuSfx.Move); } break;
                case RingAction.Confirm: Activate(); break;
                case RingAction.Back: Close(); break;   // Esc/B на корені паузи — назад у гру
            }
        }

        /// <summary>Esc на корені паузи (хост кличе, коли «Назад» перепризначено з Esc на іншу клавішу).</summary>
        public bool AtRoot { get { return Idle; } }

        public void Tick(float dt)
        {
            _anim.Tick(dt * 1000f);
            if (_settings != null) _settings.Tick(dt);
        }

        // ---------------- для огляду (-ring-menu-shots) ----------------

        public void ShotFocus(int i) { if (_focus.Set(Math.Max(0, Math.Min(i, _plates.Count - 1)))) SyncFocus(); }
    }

    /// <summary>Кармінна смуга навскіс через центр екрана (за колонкою паузи) з чорним краєм і золотою ниткою (палітра «Арена»).</summary>
    public sealed class PauseBand : VisualElement
    {
        public PauseBand()
        {
            style.position = Position.Absolute;
            style.left = 0f; style.right = 0f; style.top = 0f; style.bottom = 0f;
            pickingMode = PickingMode.Ignore;
            generateVisualContent = Draw;
            RegisterCallback<GeometryChangedEvent>(e => MarkDirtyRepaint());
        }

        private void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            float W = r.width, H = r.height;
            if (W < 4f || H < 4f) return;
            var p = ctx.painter2D;
            float k = MenuShapes.SlantK;
            float bw = Math.Min(620f, W * 0.62f);
            float x0 = W * 0.5f - bw * 0.5f - H * k * 0.5f;     // нахил 12° на всю висоту, центр смуги — центр екрана
            // майже щільна (як клин налаштувань): крізь 0,78 яскраві речі зали — екран ноутбука — читались як глюк (кадр 08.11.2026)
            MenuShapes.Parallelogram(p, x0, 0f, bw, H, k, RingTheme.WithAlpha(MenuPalette.Wedge, 0.88f));
            MenuShapes.Parallelogram(p, x0 + bw - 2f, 0f, 14f, H, k, RingTheme.WithAlpha(MenuPalette.Grit, 0.95f));      // чорний край
            MenuShapes.Parallelogram(p, x0 + bw + 14f, 0f, 3f, H, k, MenuPalette.Trim);                                   // золота нитка
            MenuShapes.Parallelogram(p, x0 + 14f, 0f, 4f, H, k, RingTheme.WithAlpha(RingTheme.Text, BrandTokens.AlphaTronDim));
        }
    }
}
