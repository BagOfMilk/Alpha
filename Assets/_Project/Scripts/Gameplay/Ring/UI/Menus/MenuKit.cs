using System;
using System.Collections.Generic;
using Game.Gameplay.Ring.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.Ring.UI.Menus
{
    /// <summary>Що анімується: зсув, масштаб, поворот, прозорість — лише transform/opacity (ТЗ B08 §3, PERF-01).</summary>
    public enum MenuProp { X = 0, Y, Scale, Rotate, Opacity }

    /// <summary>
    /// Аніматор меню: криві з <see cref="MenuMotion"/>, тривалості з токенів. Кожен кадр змінює лише transform/opacity
    /// елементів, у яких щось рухається. «Менше руху» (<see cref="Reduced"/>) — переходи без перельоту й зсувів, лише
    /// короткі згасання.
    /// </summary>
    public sealed class MenuAnimator
    {
        private sealed class Motion
        {
            public VisualElement E;
            public float X, Y, S = 1f, R, O = 1f;
            public bool Dirty;
        }

        private sealed class Track
        {
            public Motion M;
            public MenuProp Prop;
            public MenuTween Tw;
            public float T;
            public Action Done;
        }

        /// <summary>Режим «Менше руху» (Налаштування → Доступність).</summary>
        public static bool Reduced;

        private readonly Dictionary<VisualElement, Motion> _m = new Dictionary<VisualElement, Motion>();
        private readonly List<Track> _tracks = new List<Track>();

        public bool Busy { get { return _tracks.Count > 0; } }

        private Motion Get(VisualElement e)
        {
            Motion m;
            if (!_m.TryGetValue(e, out m)) { m = new Motion { E = e }; _m[e] = m; }
            return m;
        }

        public float Value(VisualElement e, MenuProp p)
        {
            var m = Get(e);
            switch (p)
            {
                case MenuProp.X: return m.X;
                case MenuProp.Y: return m.Y;
                case MenuProp.Scale: return m.S;
                case MenuProp.Rotate: return m.R;
                default: return m.O;
            }
        }

        public void Set(VisualElement e, MenuProp p, float v)
        {
            Cancel(e, p);
            var m = Get(e);
            Write(m, p, v);
            ApplyNow(m);
        }

        public void To(VisualElement e, MenuProp p, float to, float ms, Ease ease, float delayMs = 0f, Action done = null)
        {
            FromTo(e, p, Value(e, p), to, ms, ease, delayMs, done);
        }

        public void FromTo(VisualElement e, MenuProp p, float from, float to, float ms, Ease ease, float delayMs = 0f, Action done = null)
        {
            Cancel(e, p);
            var m = Get(e);
            if (Reduced && p != MenuProp.Opacity)
            {
                // без руху: одразу кінцеве значення
                Write(m, p, to);
                ApplyNow(m);
                if (done != null) done();
                return;
            }
            if (Reduced) { ease = Ease.Linear; ms = Math.Min(ms, BrandTokens.MotionMenuFocusMs); delayMs = 0f; }
            Write(m, p, from);
            ApplyNow(m);
            _tracks.Add(new Track { M = m, Prop = p, Tw = new MenuTween(from, to, ms, ease, delayMs), T = 0f, Done = done });
        }

        /// <summary>Забути елемент (його прибрали з екрана): треки й стан не тримають посилання.</summary>
        public void Forget(VisualElement e)
        {
            for (int i = _tracks.Count - 1; i >= 0; i--) if (_tracks[i].M.E == e) _tracks.RemoveAt(i);
            _m.Remove(e);
        }

        public void Cancel(VisualElement e, MenuProp p)
        {
            for (int i = _tracks.Count - 1; i >= 0; i--)
                if (_tracks[i].M.E == e && _tracks[i].Prop == p) _tracks.RemoveAt(i);
        }

        public void Tick(float dtMs)
        {
            if (_tracks.Count == 0) return;
            List<Action> done = null;
            for (int i = _tracks.Count - 1; i >= 0; i--)
            {
                var t = _tracks[i];
                t.T += dtMs;
                Write(t.M, t.Prop, t.Tw.Value(t.T));
                if (!t.Tw.Done(t.T)) continue;
                Write(t.M, t.Prop, t.Tw.To);
                _tracks.RemoveAt(i);
                if (t.Done != null) (done ?? (done = new List<Action>())).Add(t.Done);
            }
            foreach (var m in _m.Values) if (m.Dirty) ApplyNow(m);
            if (done != null) foreach (var d in done) d();
        }

        private static void Write(Motion m, MenuProp p, float v)
        {
            switch (p)
            {
                case MenuProp.X: m.X = v; break;
                case MenuProp.Y: m.Y = v; break;
                case MenuProp.Scale: m.S = v; break;
                case MenuProp.Rotate: m.R = v; break;
                default: m.O = v; break;
            }
            m.Dirty = true;
        }

        private static void ApplyNow(Motion m)
        {
            m.Dirty = false;
            m.E.style.translate = new Translate(new Length(m.X), new Length(m.Y));
            m.E.style.scale = new Scale(new Vector2(m.S, m.S));
            m.E.style.rotate = new Rotate(Angle.Degrees(m.R));
            m.E.style.opacity = m.O;
        }
    }

    /// <summary>Будівельні блоки меню «Енергія»: написи ролей Статуту, колажний заголовок, підвал, пластина-кнопка.</summary>
    public static class MenuKit
    {
        public static Label Display(string text, float size, Color color, RingFontRole role = RingFontRole.Display800, float tracking = 0.05f)
        {
            var l = new Label(RingTheme.Caps(text));
            RingFonts.Apply(l, role);
            l.style.fontSize = size;
            l.style.color = color;
            l.style.letterSpacing = new Length(size * tracking);
            l.style.unityTextAlign = TextAnchor.MiddleLeft;
            l.style.whiteSpace = WhiteSpace.NoWrap;
            l.style.marginLeft = 0f; l.style.marginRight = 0f; l.style.marginTop = 0f; l.style.marginBottom = 0f;
            l.style.paddingLeft = 0f; l.style.paddingRight = 0f; l.style.paddingTop = 0f; l.style.paddingBottom = 0f;
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        /// <summary>Речення й підказки — Fixel (Статут TYPE-02: ВЕЛИКІ лише для ≤2 слів).</summary>
        public static Label Body(string text, float size, Color color, bool bold = false)
        {
            var l = new Label(text);
            RingFonts.Apply(l, bold ? RingFontRole.TextBold : RingFontRole.Text);
            l.style.fontSize = size;
            l.style.color = color;
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginLeft = 0f; l.style.marginRight = 0f; l.style.marginTop = 0f; l.style.marginBottom = 0f;
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        private static readonly float[] CollageSize = { 1f, 0.80f, 0.92f, 0.76f, 0.98f, 0.84f };
        private static readonly float[] CollageDy = { 0f, 0.14f, -0.08f, 0.18f, 0.02f, -0.04f };

        /// <summary>
        /// Колаж заголовка (мова «Енергія»): кожна літера — на своїй вирізці зі зрізом 12°, розмір і висота трохи різні, кольори
        /// плашок — папір / тло / золото / кармін / кобальт по колу (жодного нового кольору). Текст не обертається (запобіжник
        /// читабельності, RING_WRESTLING_UI_REFERENCES), базова лінія одна. Лише для заголовків ≤2 слів (TYPE-02).
        /// </summary>
        public static VisualElement Collage(string text, float size, int seed = 0)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexEnd;
            row.pickingMode = PickingMode.Ignore;
            string caps = RingTheme.Caps(text) ?? "";
            int n = 0;
            foreach (char ch in caps)
            {
                if (ch == ' ') { var sp = new VisualElement(); sp.style.width = size * 0.3f; row.Add(sp); continue; }
                int k = (n + seed) % CollageSize.Length;
                int c = (n + seed) % 5;
                n++;
                Color bg, fg;
                switch (c)
                {
                    case 0: bg = RingTheme.Paper; fg = RingTheme.Bg; break;
                    case 1: bg = RingTheme.Bg; fg = RingTheme.Paper; break;
                    case 2: bg = RingTheme.Gold; fg = RingTheme.Bg; break;
                    case 3: bg = RingTheme.Crimson; fg = RingTheme.Paper; break;
                    default: bg = RingTheme.Hero; fg = RingTheme.Paper; break;   // кобальт — холодна палітра варіанта А
                }
                float px = size * CollageSize[k];
                var cell = new VisualElement();
                cell.pickingMode = PickingMode.Ignore;
                cell.style.paddingLeft = px * 0.16f; cell.style.paddingRight = px * 0.22f;
                cell.style.paddingTop = px * 0.02f; cell.style.paddingBottom = px * 0.02f;
                cell.style.marginRight = -px * 0.04f;
                cell.style.translate = new Translate(new Length(0f), new Length(size * CollageDy[k]));
                Color cbg = bg;
                cell.generateVisualContent = ctx =>
                {
                    // малюємо під усім елементом разом із відступами: локальні координати 0…layout
                    float w = cell.layout.width, h = cell.layout.height;
                    if (w < 2f || h < 2f) return;
                    float sh = BrandTokens.SpaceShadowOffset;
                    MenuShapes.Parallelogram(ctx.painter2D, sh, sh, w - MenuShapes.SlantK * h, h, MenuShapes.SlantK, RingTheme.Bg);
                    MenuShapes.Parallelogram(ctx.painter2D, 0f, 0f, w - MenuShapes.SlantK * h, h, MenuShapes.SlantK, cbg);
                };
                var l = Display(ch.ToString(), px, fg, RingFontRole.Display900, 0f);
                l.style.unityTextAlign = TextAnchor.MiddleCenter;
                cell.Add(l);
                row.Add(cell);
            }
            return row;
        }

        /// <summary>Підвал: підказки клавіш ліворуч, версія праворуч — у безпечній зоні (ACC-04: низ ≥ 45, боки ≥ 80 @900).</summary>
        public static VisualElement Footer(out Label hints, string right)
        {
            var f = new VisualElement();
            f.style.position = Position.Absolute;
            f.style.left = 80f; f.style.right = 80f; f.style.bottom = 45f;
            f.style.flexDirection = FlexDirection.Row;
            f.style.justifyContent = Justify.SpaceBetween;
            f.pickingMode = PickingMode.Ignore;
            hints = Body("", BrandTokens.TypeLabel, RingTheme.TextDim);
            f.Add(hints);
            if (!string.IsNullOrEmpty(right)) f.Add(Body(right, BrandTokens.TypeLabel, RingTheme.TextDim));
            return f;
        }

        /// <summary>
        /// Пластина-кнопка з написом (display ExtraBold, ВЕЛИКІ) і необов'язковою підказкою праворуч. Висота ≥ 64 px @1080p
        /// (ціль XAG): на еталоні 900 — 60.
        /// </summary>
        public static MenuButton Button(string text, float width, float height = 60f, string hint = null)
        {
            return new MenuButton(text, width, height, hint);
        }
    }

    /// <summary>Пластина-кнопка меню: стан, напис і підказка, події миші (наведення = фокус, клік = дія).</summary>
    public sealed class MenuButton : VisualElement
    {
        public readonly SlantPlate Plate;
        public readonly Label Text;
        public readonly Label Hint;
        public Action OnHover;
        public Action OnClick;
        private bool _enabled = true;

        public MenuButton(string text, float width, float height, string hint)
        {
            style.width = width;
            style.height = height;
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.overflow = Overflow.Visible;
            Plate = new SlantPlate();
            Plate.style.position = Position.Absolute;
            Plate.style.left = 0f; Plate.style.top = 0f; Plate.style.right = 0f; Plate.style.bottom = 0f;
            Plate.pickingMode = PickingMode.Ignore;
            Add(Plate);
            Text = MenuKit.Display(text, Math.Min(BrandTokens.TypeMenu, height * 0.56f), RingTheme.Text, RingFontRole.Display800, 0.05f);
            Text.style.marginLeft = 33f;
            Text.style.flexGrow = 1f;
            Add(Text);
            Hint = MenuKit.Display(hint ?? "", BrandTokens.TypeLabel, RingTheme.TextDim, RingFontRole.Display700, 0.08f);
            Hint.style.marginRight = height * MenuShapes.SlantK + 22f;
            Hint.style.display = string.IsNullOrEmpty(hint) ? DisplayStyle.None : DisplayStyle.Flex;
            Add(Hint);
            RegisterCallback<PointerEnterEvent>(e => { if (_enabled && OnHover != null) OnHover(); });
            // На відпусканні над кнопкою (ClickEvent), а не на натисканні: на дотику палець, що почав прокрутку чи з'їхав, не тисне
            // кнопку випадково (аудит 08.11.2026, В4).
            RegisterCallback<ClickEvent>(e => { if (_enabled && e.button == 0 && OnClick != null) OnClick(); });
        }

        public bool Enabled { get { return _enabled; } set { _enabled = value; if (!value) State = PlateState.Disabled; else if (State == PlateState.Disabled) State = PlateState.Normal; } }

        public PlateState State
        {
            get { return Plate.State; }
            set
            {
                Plate.State = value;
                Text.style.color = Plate.TextColor;
                bool hot = value == PlateState.Focus || value == PlateState.Pressed;
                Hint.style.color = hot ? RingTheme.Bg : RingTheme.TextDim;
            }
        }

        public void SetHint(string hint)
        {
            Hint.text = RingTheme.Caps(hint ?? "");
            Hint.style.display = string.IsNullOrEmpty(hint) ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }
}
