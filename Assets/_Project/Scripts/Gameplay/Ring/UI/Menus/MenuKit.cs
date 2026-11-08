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


        /// <summary>
        /// Заголовок екрана — титр-плита (палітра «Арена»; власник 08.11.2026 про колаж літер: «Стиль надпису "завантаження"
        /// теж наче з дитячої гри, точно не реслінг брутальність»): одне слово display 900 курсивом ВЕЛИКИМИ, кольору паперу, з
        /// жорсткою кармінною тінню, на чорній плиті зі зрізом 12°, кармінна смуга зліва, золота нитка внизу — як титр бою на
        /// трансляції. Колаж з різнокольорових вирізок прибрано. <paramref name="seed"/> лишився для сумісності викликів.
        /// </summary>
        public static VisualElement Collage(string text, float size, int seed = 0)
        {
            var box = new VisualElement();
            box.pickingMode = PickingMode.Ignore;
            box.style.flexDirection = FlexDirection.Row;
            box.style.alignSelf = Align.FlexStart;
            box.style.overflow = Overflow.Visible;
            var slab = new TitleSlab();
            slab.style.position = Position.Absolute;
            slab.style.left = 0f; slab.style.top = 0f; slab.style.right = 0f; slab.style.bottom = 0f;
            box.Add(slab);
            var wrap = new VisualElement();
            wrap.pickingMode = PickingMode.Ignore;
            wrap.style.paddingLeft = size * 0.5f; wrap.style.paddingRight = size * 0.75f;
            wrap.style.paddingTop = size * 0.06f; wrap.style.paddingBottom = size * 0.1f;
            var shadow = Display(text, size, RingTheme.Crimson, RingFontRole.Display900, 0.02f);
            shadow.style.unityFontStyleAndWeight = FontStyle.Italic;
            shadow.style.position = Position.Absolute;
            shadow.style.left = size * 0.5f; shadow.style.top = size * 0.06f;
            shadow.style.translate = new Translate(new Length(size * 0.06f), new Length(size * 0.07f));
            wrap.Add(shadow);
            var word = Display(text, size, RingTheme.Paper, RingFontRole.Display900, 0.02f);
            word.style.unityFontStyleAndWeight = FontStyle.Italic;
            wrap.Add(word);
            box.Add(wrap);
            return box;
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
            Underlay(hints);
            f.Add(hints);
            if (!string.IsNullOrEmpty(right)) { var r = Body(right, BrandTokens.TypeLabel, RingTheme.TextDim); Underlay(r); f.Add(r); }
            return f;
        }

        /// <summary>Підкладка під текстом над живим фоном (Статут ACC-03): тло α AlphaUnderlay, відступи.</summary>
        public static void Underlay(VisualElement e)
        {
            e.style.backgroundColor = RingTheme.WithAlpha(RingTheme.Bg, BrandTokens.AlphaUnderlay);
            e.style.paddingLeft = 10f; e.style.paddingRight = 10f; e.style.paddingTop = 3f; e.style.paddingBottom = 3f;
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
