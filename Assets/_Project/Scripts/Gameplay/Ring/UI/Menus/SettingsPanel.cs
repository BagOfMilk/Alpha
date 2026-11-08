using System;
using System.Collections.Generic;
using Game.Gameplay.Ring.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.Ring.UI.Menus
{
    /// <summary>
    /// Налаштування (ТЗ B08 §4; власник 07.11.2026 — вкладки Графіка, Звук, Керування, Доступність, Мова): рядки-пластини
    /// мови «Енергія», кнопки «За замовчуванням», «Скасувати», «Застосувати», діалог при виході з незбереженими змінами.
    /// Гучність чути одразу (тестовий звук шини); усе інше діє після «Застосувати» і зберігається між запусками.
    /// </summary>
    public sealed class SettingsPanel : IMenuNavigable
    {
        private enum Tab { Graphics = 0, Sound, Controls, Access, Language }

        private const float RowW = 900f, RowH = 52f, RowGap = 8f, RowStep = 8f, ValueX = 430f;

        public readonly VisualElement Root;
        private readonly RingText _t;
        private readonly IMenuHost _menu;
        private readonly MenuAnimator _anim;
        private readonly Action _close;
        private readonly List<MenuButton> _tabs = new List<MenuButton>();
        private readonly List<SettingRow> _rows = new List<SettingRow>();
        private readonly List<MenuButton> _buttons = new List<MenuButton>();
        private readonly MenuFocus _focus = new MenuFocus();
        private readonly VisualElement _rowsBox;
        private readonly Label _hints, _toast, _note;
        private Tab _tab;
        private MenuDialog _dialog;
        private RingAction? _capturing;
        private float _toastMs = -1f;
        private MenuDevice _device;

        private RingSettingsModel M { get { return _menu.Settings.Model; } }
        private RingSettingsValues D { get { return M.Draft; } }

        public SettingsPanel(RingText text, IMenuHost menu, MenuAnimator anim, Action close)
        {
            _t = text; _menu = menu; _anim = anim; _close = close;
            M.BeginEdit();
            Root = MenuShapes.Layer();
            Root.pickingMode = PickingMode.Position;
            Root.Add(new MenuBackdrop { Scrim = 0.82f, WedgeWidth = 0.62f, WedgeAlpha = 0.82f, CrimsonCut = true });
            Root.Add(new SpeedLines(11));

            var head = MenuKit.Collage(_t.Get("m.set.title"), 56f, 2);
            head.style.position = Position.Absolute;
            head.style.left = 80f; head.style.top = 44f;
            Root.Add(head);

            var tabsRow = new VisualElement();
            tabsRow.style.position = Position.Absolute;
            tabsRow.style.left = 80f; tabsRow.style.top = 134f;
            tabsRow.style.flexDirection = FlexDirection.Row;
            tabsRow.style.alignItems = Align.Center;
            for (int i = 0; i < 5; i++)
            {
                int idx = i;
                var b = MenuKit.Button(_t.Get(TabKey((Tab)i)), TabWidth((Tab)i), 40f);
                b.Text.style.fontSize = 22f;
                b.Text.style.marginLeft = 16f;
                b.Plate.Stripe = 0f;
                b.style.marginRight = 10f;
                b.OnClick = () => SwitchTab((Tab)idx, true);
                _tabs.Add(b);
                tabsRow.Add(b);
            }
            _note = MenuKit.Display("Q · E", BrandTokens.TypeLabel, RingTheme.TextDim, RingFontRole.Display700, 0.1f);
            _note.style.marginLeft = 8f;
            tabsRow.Add(_note);
            Root.Add(tabsRow);

            _rowsBox = new VisualElement();
            _rowsBox.style.position = Position.Absolute;
            _rowsBox.style.left = 80f; _rowsBox.style.top = 200f;
            _rowsBox.pickingMode = PickingMode.Ignore;
            Root.Add(_rowsBox);

            var btnRow = new VisualElement();
            btnRow.style.position = Position.Absolute;
            btnRow.style.right = 80f; btnRow.style.top = 776f;
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.alignItems = Align.Center;
            _toast = MenuKit.Display("", 24f, RingTheme.Gold, RingFontRole.Display900, 0.06f);
            _toast.style.marginRight = 24f;
            _toast.style.opacity = 0f;
            btnRow.Add(_toast);
            AddButton(btnRow, "m.set.defaults", 270f, Defaults);
            AddButton(btnRow, "m.set.cancel", 200f, Cancel);
            var apply = AddButton(btnRow, "m.set.apply", 240f, Apply);
            apply.Plate.Primary = true;
            Root.Add(btnRow);

            Root.Add(MenuKit.Footer(out _hints, null));
            SwitchTab(Tab.Graphics, false);
            _anim.FromTo(Root, MenuProp.Opacity, 0f, 1f, BrandTokens.MotionMenuWipeMs, Ease.OutCubic);
            _anim.FromTo(head, MenuProp.X, -120f, 0f, BrandTokens.MotionMenuPlateInMs, Ease.OutBack);
        }

        private MenuButton AddButton(VisualElement row, string key, float w, Action act)
        {
            var b = MenuKit.Button(_t.Get(key), w, 52f);
            b.Text.style.fontSize = 26f;
            b.Text.style.marginLeft = 22f;
            b.style.marginLeft = 12f;
            int idx = _buttons.Count;
            b.OnHover = () => { if (RingMenuInput.MouseArmed && _dialog == null && _capturing == null && _focus.Set(_rows.Count + idx)) { Sync(); RingMenuServices.Play(MenuSfx.Move); } };
            b.OnClick = () => { if (_dialog == null && _capturing == null) act(); };
            _buttons.Add(b);
            row.Add(b);
            return b;
        }

        private static string TabKey(Tab t)
        {
            switch (t)
            {
                case Tab.Graphics: return "m.set.tab.graphics";
                case Tab.Sound: return "m.set.tab.sound";
                case Tab.Controls: return "m.set.tab.controls";
                case Tab.Access: return "m.set.tab.access";
                default: return "m.set.tab.language";
            }
        }

        private static float TabWidth(Tab t) { return t == Tab.Access ? 190f : t == Tab.Controls ? 176f : 140f; }

        // ---------------- вкладки й рядки ----------------

        private void SwitchTab(Tab t, bool sound)
        {
            if (_capturing != null) return;
            _tab = t;
            for (int i = 0; i < _tabs.Count; i++) _tabs[i].State = i == (int)t ? PlateState.Focus : PlateState.Normal;
            if (sound) RingMenuServices.Play(MenuSfx.Tab);
            BuildRows();
        }

        private void BuildRows(bool keepFocus = false)
        {
            int keep = keepFocus ? _focus.Index : 0;
            foreach (var old in _rows) { _anim.Forget(old); _anim.Forget(old.Plate); _anim.Forget(old.Content); }
            _rowsBox.Clear();
            _rows.Clear();
            switch (_tab)
            {
                case Tab.Graphics:
                    Choice("m.set.display", () => _t.Get(D.Display == RingDisplayMode.Windowed ? "m.val.windowed" : "m.val.fullscreen"),
                        d => { D.Display = D.Display == RingDisplayMode.Windowed ? RingDisplayMode.Fullscreen : RingDisplayMode.Windowed; });
                    Choice("m.set.resolution", () => _menu.Settings.ResolutionLabel(D.Resolution, _t.Get("m.val.native")),
                        d => { int n = _menu.Settings.Resolutions.Count; D.Resolution = Wrap(D.Resolution + 1 + d, n + 1) - 1; });
                    Choice("m.set.graphics", () => _t.Get(GraphicsKey(D.Graphics)), d => { D.Graphics = (RingGraphics)Wrap((int)D.Graphics + d, 4); });
                    Choice("m.set.vsync", () => _t.Get(D.VSync ? "m.val.on" : "m.val.off"), d => { D.VSync = !D.VSync; });
                    Choice("m.set.fps", () => D.FrameCap > 0 ? D.FrameCap.ToString() : _t.Get("m.val.unlimited"),
                        d => { int i = Array.IndexOf(RingSettingsValues.FrameCaps, D.FrameCap); D.FrameCap = RingSettingsValues.FrameCaps[Wrap(i + d, RingSettingsValues.FrameCaps.Length)]; });
                    break;
                case Tab.Sound:
                    Slider("m.set.vol.master", RingBus.Master, () => D.Master, v => D.Master = v);
                    Slider("m.set.vol.music", RingBus.Music, () => D.Music, v => D.Music = v);
                    Slider("m.set.vol.sfx", RingBus.Sfx, () => D.Sfx, v => D.Sfx = v);
                    Slider("m.set.vol.voice", RingBus.Voice, () => D.Voice, v => D.Voice = v);
                    Choice("m.set.bgaudio", () => _t.Get(D.MuteInBackground ? "m.val.muted" : "m.val.plays"),
                        d => { D.MuteInBackground = !D.MuteInBackground; _menu.Settings.PreviewAudio(D); });
                    break;
                case Tab.Controls:
                    foreach (RingAction a in Enum.GetValues(typeof(RingAction))) Key(a);
                    break;
                case Tab.Access:
                    Choice("m.set.reducemotion", () => _t.Get(D.ReduceMotion ? "m.val.yes" : "m.val.no"), d => { D.ReduceMotion = !D.ReduceMotion; });
                    Choice("m.set.shake", () => _t.Get(D.ScreenShake ? "m.val.on" : "m.val.off"), d => { D.ScreenShake = !D.ScreenShake; });
                    Choice("m.set.flashes", () => _t.Get(D.Flashes ? "m.val.on" : "m.val.off"), d => { D.Flashes = !D.Flashes; });
                    Choice("m.set.uiscale", () => D.UiScale + " %",
                        d => { int i = Array.IndexOf(RingSettingsValues.UiScales, D.UiScale); D.UiScale = RingSettingsValues.UiScales[Wrap(i + d, RingSettingsValues.UiScales.Length)]; });
                    break;
                default:
                    Choice("m.set.language", () => _t.Get("m.val.lang." + D.Language), d => { });
                    break;
            }
            var enabled = new List<bool>();
            for (int i = 0; i < _rows.Count + _buttons.Count; i++) enabled.Add(true);
            _focus.Reset(enabled, keepFocus ? Math.Max(0, keep) : 0);
            string noteKey = _tab == Tab.Controls ? "m.set.note.pad" : _tab == Tab.Language ? "m.set.note.lang" : _tab == Tab.Sound ? "m.set.note.sound"
                : _tab == Tab.Access ? "m.set.note.access" : null;
            if (noteKey != null)
            {
                var n = MenuKit.Body(_t.Get(noteKey), BrandTokens.TypeLabel, RingTheme.TextDim);
                n.style.marginTop = 6f;
                n.style.maxWidth = RowW;
                _rowsBox.Add(n);
            }
            if (!keepFocus)
                for (int i = 0; i < _rows.Count; i++)
                    _anim.FromTo(_rows[i], MenuProp.X, -100f, 0f, BrandTokens.MotionMenuPlateInMs, Ease.OutBack, MenuMotion.StaggerMs(i) * 0.5f);
            Sync();
        }

        private static int Wrap(int v, int n) { return n <= 0 ? 0 : ((v % n) + n) % n; }

        private static string GraphicsKey(RingGraphics g)
        {
            switch (g)
            {
                case RingGraphics.Low: return "m.val.gfx.low";
                case RingGraphics.Medium: return "m.val.gfx.medium";
                case RingGraphics.High: return "m.val.gfx.high";
                default: return "m.val.gfx.auto";
            }
        }

        private SettingRow NewRow(string nameKey)
        {
            var r = new SettingRow(_t.Get(nameKey), RowW, RowH, ValueX);
            r.style.marginBottom = RowGap;
            r.style.marginLeft = _rows.Count * RowStep;
            int idx = _rows.Count;
            r.OnHover = () => { if (RingMenuInput.MouseArmed && _dialog == null && _capturing == null && _focus.Set(idx)) { Sync(); RingMenuServices.Play(MenuSfx.Move); } };
            _rows.Add(r);
            _rowsBox.Add(r);
            return r;
        }

        private void Choice(string nameKey, Func<string> value, Action<int> change)
        {
            var r = NewRow(nameKey);
            r.Kind = SettingRow.RowKind.Choice;
            r.ValueText = value;
            r.Change = d => { change(d); RingMenuServices.Play(MenuSfx.Toggle); };
        }

        private void Slider(string nameKey, RingBus bus, Func<int> get, Action<int> set)
        {
            var r = NewRow(nameKey);
            r.Kind = SettingRow.RowKind.Slider;
            r.ValueText = () => get() * 10 + " %";
            r.Fraction = () => get();
            r.SetAbs = v => { int now = get(); if (v != now) r.Change(v - now); };
            r.Change = d =>
            {
                int v = Math.Max(0, Math.Min(RingSettingsValues.VolumeSteps, get() + d));
                if (v == get()) return;
                set(v);
                _menu.Settings.PreviewAudio(D);           // гучність — одразу, ще до «Застосувати»
                RingMenuServices.Play(MenuSfx.Slider);
                if (RingMenuServices.Audio != null) RingMenuServices.Audio.TestBus(bus);
            };
        }

        private void Key(RingAction a)
        {
            var r = NewRow("m.act." + a.ToString().ToLowerInvariant());
            r.Kind = SettingRow.RowKind.Key;
            r.ValueText = () => _capturing == a ? _t.Get("m.set.presskey") : KeyLabel(D.Keys.ContainsKey(a) ? D.Keys[a] : "");
            r.Change = d => BeginCapture(a);
        }

        /// <summary>Назва клавіші для гравця (ключі <c>m.key.*</c>; інакше — ім'я клавіші великими).</summary>
        public string KeyLabel(string keyName)
        {
            if (string.IsNullOrEmpty(keyName)) return "—";
            string k = "m.key." + keyName;
            if (_t.Has(k)) return _t.Get(k);
            if (keyName.StartsWith("Alpha") && keyName.Length == 6) return keyName.Substring(5);
            return keyName.ToUpperInvariant();
        }

        // ---------------- фокус і дії ----------------

        private bool OnButtons { get { return _focus.Index >= _rows.Count; } }

        private void Sync()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                bool f = i == _focus.Index;
                _rows[i].Refresh(f, _capturing != null && f);
                _anim.To(_rows[i].Plate, MenuProp.X, f ? 25f : 0f, BrandTokens.MotionMenuFocusMs, Ease.OutCubic);
                _anim.To(_rows[i].Content, MenuProp.X, f ? 25f : 0f, BrandTokens.MotionMenuFocusMs, Ease.OutCubic);
            }
            for (int i = 0; i < _buttons.Count; i++)
                _buttons[i].State = _focus.Index == _rows.Count + i ? PlateState.Focus : PlateState.Normal;
            UpdateHints();
        }

        private void UpdateHints()
        {
            bool pad = _device == MenuDevice.Gamepad;
            _note.text = pad ? "LB · RB" : "Q · E";
            if (_capturing != null) { _hints.text = _t.Get("m.footer.capture"); return; }
            _hints.text = _t.Get(pad ? "m.footer.set.pad" : "m.footer.set.keys");
        }

        private void BeginCapture(RingAction a)
        {
            _capturing = a;
            _menu.MenuInput.Captured = null;
            _menu.MenuInput.Capturing = true;
            RingMenuServices.Play(MenuSfx.Confirm);
            Sync();
        }

        private void EndCapture()
        {
            var a = _capturing.Value;
            var key = _menu.MenuInput.Captured;
            _capturing = null;
            _menu.MenuInput.Captured = null;
            if (key.HasValue && RingMenuInput.IsReserved(key.Value))
            {
                RingMenuServices.Play(MenuSfx.Error);
                Toast(_t.Get("m.set.reserved"));
            }
            else if (key.HasValue && !RingMenuInput.IsCancel(key.Value))
            {
                var swapped = M.Rebind(a, key.Value.ToString());
                RingMenuServices.Play(MenuSfx.Toggle);
                if (swapped.HasValue) Toast(_t.Get("m.set.swapped"));
            }
            else RingMenuServices.Play(MenuSfx.Back);
            Sync();
        }

        private void Apply()
        {
            M.Apply(_menu.Settings.Store);
            _menu.Settings.ApplyAll(true);
            _menu.SettingsApplied();
            RingMenuServices.Play(MenuSfx.Confirm);
            Toast(_t.Get("m.set.applied"));
            Sync();
        }

        private void Cancel()
        {
            M.Cancel();
            _menu.Settings.PreviewAudio(M.Applied);
            RingMenuServices.Play(MenuSfx.Back);
            BuildRows(true);
        }

        private void Defaults()
        {
            M.ResetDraft();
            _menu.Settings.PreviewAudio(D);
            RingMenuServices.Play(MenuSfx.Toggle);
            Toast(_t.Get("m.set.defaulted"));
            BuildRows(true);
        }

        private void Toast(string s)
        {
            _toast.text = RingTheme.Caps(s);
            _toastMs = 0f;
            _anim.FromTo(_toast, MenuProp.Opacity, 0f, 1f, BrandTokens.MotionMenuFocusMs, Ease.OutCubic);
            _anim.FromTo(_toast, MenuProp.Scale, 1.25f, 1f, BrandTokens.MotionStingerInMs, Ease.OutBack);
        }

        /// <summary>Вихід (Esc/B): незбережені зміни — діалог «Застосувати / Відкинути / Повернутися».</summary>
        private void Back()
        {
            if (!M.IsDirty) { Close(); return; }
            _dialog = new MenuDialog(_anim, _t.Get("m.dirty.title"), _t.Get("m.dirty.body"),
                new[]
                {
                    new KeyValuePair<string, Action>(_t.Get("m.set.apply"), () => { _dialog = null; Apply(); Close(); }),
                    new KeyValuePair<string, Action>(_t.Get("m.dirty.discard"), () => { _dialog = null; M.Cancel(); _menu.Settings.PreviewAudio(M.Applied); Close(); }),
                    new KeyValuePair<string, Action>(_t.Get("m.dirty.stay"), () => { _dialog = null; Sync(); }),
                }, 2, 0);
            Root.Add(_dialog.Root);
        }

        private void Close()
        {
            RingMenuServices.Play(MenuSfx.Back);
            _anim.To(Root, MenuProp.Opacity, 0f, BrandTokens.MotionMenuPlateOutMs, Ease.InCubic, 0f, _close);
        }

        public bool Dirty { get { return M.IsDirty; } }
        public bool HasDialog { get { return _dialog != null; } }

        public void OnMenu(RingAction action, MenuDevice device)
        {
            _device = device;
            if (_dialog != null) { _dialog.OnMenu(action, device); return; }
            if (_capturing != null) return;    // ввід зараз ловить клавішу (RingMenuInput.Capturing)
            switch (action)
            {
                case RingAction.Up: if (_focus.Move(-1)) { Sync(); RingMenuServices.Play(MenuSfx.Move); } break;
                case RingAction.Down: if (_focus.Move(1)) { Sync(); RingMenuServices.Play(MenuSfx.Move); } break;
                case RingAction.Left:
                    if (OnButtons) { if (_focus.Index > _rows.Count && _focus.Set(_focus.Index - 1)) { Sync(); RingMenuServices.Play(MenuSfx.Move); } }
                    else if (_rows[_focus.Index].Kind != SettingRow.RowKind.Key) { _rows[_focus.Index].Change(-1); Sync(); }
                    break;
                case RingAction.Right:
                    if (OnButtons) { if (_focus.Set(Math.Min(_rows.Count + _buttons.Count - 1, _focus.Index + 1))) { Sync(); RingMenuServices.Play(MenuSfx.Move); } }
                    else if (_rows[_focus.Index].Kind != SettingRow.RowKind.Key) { _rows[_focus.Index].Change(1); Sync(); }
                    break;
                case RingAction.Confirm:
                    if (OnButtons)
                    {
                        int b = _focus.Index - _rows.Count;
                        if (b == 0) Defaults(); else if (b == 1) Cancel(); else Apply();
                    }
                    else if (_rows[_focus.Index].Kind != SettingRow.RowKind.Slider) { _rows[_focus.Index].Change(1); Sync(); }
                    break;
                case RingAction.TabPrev: SwitchTab((Tab)Wrap((int)_tab - 1, 5), true); break;
                case RingAction.TabNext: SwitchTab((Tab)Wrap((int)_tab + 1, 5), true); break;
                case RingAction.Back: Back(); break;
            }
        }

        public void Tick(float dt)
        {
            if (_capturing != null && !_menu.MenuInput.Capturing) EndCapture();
            if (_toastMs >= 0f)
            {
                _toastMs += dt * 1000f;
                if (_toastMs > 1400f) { _toastMs = -1f; _anim.To(_toast, MenuProp.Opacity, 0f, 300f, Ease.InCubic); }
            }
        }

        // ---------------- для огляду (-ring-menu-shots) ----------------

        public void ShotTab(int t) { SwitchTab((Tab)Wrap(t, 5), true); }
        public void ShotFocus(int i) { if (_focus.Set(Math.Max(0, Math.Min(i, _rows.Count + _buttons.Count - 1)))) Sync(); }
        public int RowCount { get { return _rows.Count; } }
    }

    /// <summary>Рядок налаштування: пластина, назва ліворуч, значення праворуч (шкала / ‹ вибір › / клавіша).</summary>
    public sealed class SettingRow : VisualElement
    {
        public enum RowKind { Choice = 0, Slider, Key }

        public readonly SlantPlate Plate;
        public readonly VisualElement Content;
        private readonly Label _name, _value, _left, _right;
        private readonly SegmentBar _bar;
        private readonly VisualElement _keycap;
        public RowKind Kind;
        public Func<string> ValueText;
        public Func<float> Fraction;
        public Action<int> Change;
        public Action<int> SetAbs;
        public Action OnHover;

        public SettingRow(string name, float width, float height, float valueX)
        {
            style.width = width;
            style.height = height;
            style.overflow = Overflow.Visible;
            Plate = new SlantPlate { Stripe = 8f };
            Plate.style.position = Position.Absolute;
            Plate.style.left = 0f; Plate.style.top = 0f; Plate.style.right = 0f; Plate.style.bottom = 0f;
            Plate.pickingMode = PickingMode.Ignore;
            Add(Plate);
            Content = MenuShapes.Layer();
            Content.style.flexDirection = FlexDirection.Row;
            Content.style.alignItems = Align.Center;
            Add(Content);
            _name = MenuKit.Display(name, 26f, RingTheme.Text, RingFontRole.Display800, 0.05f);
            _name.style.position = Position.Absolute;
            _name.style.left = 30f;
            _name.style.top = (height - 32f) * 0.5f;
            Content.Add(_name);
            var val = new VisualElement();
            val.style.position = Position.Absolute;
            val.style.left = valueX; val.style.top = 0f; val.style.bottom = 0f; val.style.right = height * MenuShapes.SlantK + 30f;
            val.style.flexDirection = FlexDirection.Row;
            val.style.alignItems = Align.Center;
            Content.Add(val);
            _left = MenuKit.Display("‹", 30f, RingTheme.TextDim, RingFontRole.Display900, 0f);
            _left.pickingMode = PickingMode.Position;
            _left.style.width = 34f;
            _left.style.flexShrink = 0f;
            _left.style.unityTextAlign = TextAnchor.MiddleCenter;
            _left.RegisterCallback<ClickEvent>(e => { if (Change != null && Kind != RowKind.Key) Change(-1); Refresh(true, false); });
            val.Add(_left);
            _bar = new SegmentBar();
            _bar.style.width = 250f; _bar.style.height = 18f; _bar.style.marginRight = 14f;
            _bar.style.flexShrink = 0f;
            _bar.pickingMode = PickingMode.Position;
            _bar.RegisterCallback<PointerDownEvent>(e =>
            {
                if (SetAbs == null || _bar.layout.width < 2f) return;
                float f = Mathf.Clamp01(e.localPosition.x / _bar.layout.width);
                SetAbs((int)Math.Round(f * RingSettingsValues.VolumeSteps));
                Refresh(true, false);
            });
            val.Add(_bar);
            _keycap = new VisualElement();
            _keycap.style.paddingLeft = 14f; _keycap.style.paddingRight = 14f; _keycap.style.paddingTop = 2f; _keycap.style.paddingBottom = 2f;
            _keycap.style.borderTopWidth = 2f; _keycap.style.borderBottomWidth = 2f; _keycap.style.borderLeftWidth = 2f; _keycap.style.borderRightWidth = 2f;
            _keycap.style.borderTopLeftRadius = BrandTokens.SpaceRadiusControl; _keycap.style.borderTopRightRadius = BrandTokens.SpaceRadiusControl;
            _keycap.style.borderBottomLeftRadius = BrandTokens.SpaceRadiusControl; _keycap.style.borderBottomRightRadius = BrandTokens.SpaceRadiusControl;
            _keycap.pickingMode = PickingMode.Ignore;
            _keycap.style.flexShrink = 0f;
            val.Add(_keycap);
            _value = MenuKit.Display("", 24f, RingTheme.Text, RingFontRole.Display800, 0.06f);
            _keycap.Add(_value);
            _right = MenuKit.Display("›", 30f, RingTheme.TextDim, RingFontRole.Display900, 0f);
            _right.pickingMode = PickingMode.Position;
            _right.style.width = 34f;
            _right.style.flexShrink = 0f;
            _right.style.unityTextAlign = TextAnchor.MiddleCenter;
            _right.RegisterCallback<ClickEvent>(e => { if (Change != null && Kind != RowKind.Key) Change(1); Refresh(true, false); });
            val.Add(_right);
            RegisterCallback<PointerEnterEvent>(e => { if (OnHover != null) OnHover(); });
            // клік по назві: вибір — наступне значення, клавіша — «натисни клавішу»
            _name.pickingMode = PickingMode.Position;
            _name.RegisterCallback<ClickEvent>(e => { if (Change != null && Kind != RowKind.Slider) { Change(1); Refresh(true, false); } });
            _keycap.pickingMode = PickingMode.Position;
            _keycap.RegisterCallback<ClickEvent>(e => { if (Change != null && Kind == RowKind.Key) Change(1); });
        }

        public void Refresh(bool focus, bool capturing)
        {
            Plate.State = focus ? PlateState.Focus : PlateState.Normal;
            Color fg = focus ? RingTheme.Bg : RingTheme.Text;
            _name.style.color = fg;
            _value.text = RingTheme.Caps(ValueText != null ? ValueText() : "");
            _value.style.color = fg;
            bool slider = Kind == RowKind.Slider, key = Kind == RowKind.Key;
            _bar.style.display = slider ? DisplayStyle.Flex : DisplayStyle.None;
            if (slider)
            {
                float f = Fraction != null ? Fraction() : 0f;
                _bar.Set(RingSettingsValues.VolumeSteps, f, focus ? RingTheme.Bg : RingTheme.Gold,
                    focus ? RingTheme.WithAlpha(RingTheme.Bg, BrandTokens.AlphaTronDim) : RingTheme.Border);
            }
            _left.style.display = key ? DisplayStyle.None : DisplayStyle.Flex;
            _right.style.display = key ? DisplayStyle.None : DisplayStyle.Flex;
            _left.style.color = focus ? RingTheme.Bg : RingTheme.TextDim;
            _right.style.color = focus ? RingTheme.Bg : RingTheme.TextDim;
            Color cap = key ? (capturing ? RingTheme.Signal : fg) : Color.clear;
            _keycap.style.borderTopColor = cap; _keycap.style.borderBottomColor = cap;
            _keycap.style.borderLeftColor = cap; _keycap.style.borderRightColor = cap;
            if (capturing) _value.style.color = focus ? RingTheme.Bg : RingTheme.Signal;
        }
    }
}
