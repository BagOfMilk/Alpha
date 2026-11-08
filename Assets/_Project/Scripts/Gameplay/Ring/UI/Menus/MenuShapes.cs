using System;
using System.Collections.Generic;
using Game.Gameplay.Ring.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.Ring.UI.Menus
{
    /// <summary>
    /// Форми мови меню «Енергія» (ТЗ B08 §2, варіант А ескізу 07.11.2026): пластини зі зрізом 12°, жорсткі тіні без розмиття,
    /// клини, напівтон, канати. Усе малюється кодом (Painter2D) один раз на зміну розміру — рух іде через transform/opacity,
    /// тож щокадрового перемальовування немає (Статут PERF-01). Кольори — лише з <see cref="RingTheme"/>.
    /// </summary>
    public static class MenuShapes
    {
        public static float SlantK { get { return (float)Math.Tan(BrandTokens.SpaceSlantDeg * Math.PI / 180.0); } }

        /// <summary>Трапеція зі зрізаним правим краєм (як пластини меню B01 v2).</summary>
        public static void Trapezoid(Painter2D p, float x, float y, float w, float h, float slantK, Color fill)
        {
            p.fillColor = fill;
            p.BeginPath();
            p.MoveTo(new Vector2(x, y));
            p.LineTo(new Vector2(x + w, y));
            p.LineTo(new Vector2(x + w - slantK * h, y + h));
            p.LineTo(new Vector2(x, y + h));
            p.ClosePath();
            p.Fill();
        }

        /// <summary>Паралелограм (обидва краї під нахилом) — сегменти шкал і плашки колажу.</summary>
        public static void Parallelogram(Painter2D p, float x, float y, float w, float h, float slantK, Color fill)
        {
            float o = slantK * h;
            p.fillColor = fill;
            p.BeginPath();
            p.MoveTo(new Vector2(x + o, y));
            p.LineTo(new Vector2(x + w + o, y));
            p.LineTo(new Vector2(x + w, y + h));
            p.LineTo(new Vector2(x, y + h));
            p.ClosePath();
            p.Fill();
        }

        public static void Polygon(Painter2D p, IList<Vector2> pts, Color fill)
        {
            if (pts.Count < 3) return;
            p.fillColor = fill;
            p.BeginPath();
            p.MoveTo(pts[0]);
            for (int i = 1; i < pts.Count; i++) p.LineTo(pts[i]);
            p.ClosePath();
            p.Fill();
        }

        public static void Line(Painter2D p, float x0, float y0, float x1, float y1, float width, Color c)
        {
            p.strokeColor = c;
            p.lineWidth = width;
            p.BeginPath();
            p.MoveTo(new Vector2(x0, y0));
            p.LineTo(new Vector2(x1, y1));
            p.Stroke();
        }

        public static void Dot(Painter2D p, float cx, float cy, float r, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            p.Arc(new Vector2(cx, cy), r, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.Fill();
        }

        /// <summary>Абсолютний шар на весь батьківський елемент, що не ловить мишу.</summary>
        public static VisualElement Layer()
        {
            var e = new VisualElement();
            e.style.position = Position.Absolute;
            e.style.left = 0f; e.style.right = 0f; e.style.top = 0f; e.style.bottom = 0f;
            e.pickingMode = PickingMode.Ignore;
            return e;
        }
    }

    /// <summary>Стан пластини меню: звичайний, фокус, натиснута, неактивна, помилка (ТЗ B08 §2.3).</summary>
    public enum PlateState { Normal = 0, Focus, Pressed, Disabled, Error }

    /// <summary>
    /// Пластина: трапеція 12°, ліва смужка, обведення папером і жорстка тінь (мова «Енергія»). Колір — за станом; фокус
    /// читається і без кольору — зсувом, товщим обведенням і ширшою формою (Статут ACC-02).
    /// </summary>
    public sealed class SlantPlate : VisualElement
    {
        public const float FocusGrow = 17f;
        private PlateState _state;
        private float _stripe = 10f;
        private bool _outline = true;
        private bool _primary;

        public SlantPlate()
        {
            generateVisualContent = Draw;
            style.overflow = Overflow.Visible;
        }

        public PlateState State
        {
            get { return _state; }
            set { if (_state == value) return; _state = value; MarkDirtyRepaint(); }
        }

        /// <summary>Головна дія (золото навіть без фокусу: «Застосувати»).</summary>
        public bool Primary { get { return _primary; } set { _primary = value; MarkDirtyRepaint(); } }

        public float Stripe { get { return _stripe; } set { _stripe = value; MarkDirtyRepaint(); } }

        public bool Outline { get { return _outline; } set { _outline = value; MarkDirtyRepaint(); } }

        public Color TextColor
        {
            get
            {
                switch (_state)
                {
                    case PlateState.Focus:
                    case PlateState.Pressed: return RingTheme.Bg;
                    case PlateState.Disabled: return RingTheme.Disabled;
                    default: return _primary ? RingTheme.Bg : RingTheme.Text;
                }
            }
        }

        private void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            bool hot = _state == PlateState.Focus || _state == PlateState.Pressed;
            float w = r.width + (hot ? FocusGrow : 0f), h = r.height;   // фокус — і ширша форма (B01 §4: 450 → 467), не лише колір
            if (w < 2f || h < 2f) return;
            var p = ctx.painter2D;
            float k = MenuShapes.SlantK;
            float sh = BrandTokens.SpaceShadowOffset;
            // жорстка тінь (без розмиття) — зсув +6/+6
            MenuShapes.Trapezoid(p, sh, sh, w, h, k, RingTheme.WithAlpha(RingTheme.Bg, hot ? 1f : BrandTokens.AlphaPlate));
            float ow = !_outline ? 0f : hot ? BrandTokens.SpaceStrokeAccent - 1f : BrandTokens.SpaceStroke;
            if (ow > 0f)
            {
                Color oc = _state == PlateState.Error ? RingTheme.Signal : _state == PlateState.Disabled ? RingTheme.Border : RingTheme.Text;
                MenuShapes.Trapezoid(p, -ow, -ow, w + 2f * ow + k * ow, h + 2f * ow, k, oc);
            }
            Color fill;
            switch (_state)
            {
                case PlateState.Focus:
                case PlateState.Pressed: fill = RingTheme.PrimaryAction; break;
                case PlateState.Disabled: fill = RingTheme.Panel; break;
                // непрозоро: у лінійному кольорі навіть 6 % паперу з-під обведення висвітлювали заливку до сірого (кадр 08.11.2026)
                default: fill = _primary ? RingTheme.PrimaryAction : RingTheme.Bg; break;
            }
            MenuShapes.Trapezoid(p, 0f, 0f, w, h, k, fill);
            if (_stripe > 0f)
            {
                Color sc;
                switch (_state)
                {
                    case PlateState.Focus:
                    case PlateState.Pressed: sc = RingTheme.Crimson; break;
                    case PlateState.Error: sc = RingTheme.Signal; break;
                    case PlateState.Disabled: sc = RingTheme.Border; break;
                    default: sc = _primary ? RingTheme.Crimson : RingTheme.TextDim; break;
                }
                MenuShapes.Trapezoid(p, 0f, 0f, _stripe, h, 0f, sc);
            }
        }
    }

    /// <summary>Тло мови «Енергія»: затемнення над 3D, кобальтовий клин, напівтон, кармінні розрізи (ТЗ B08 §2.1).</summary>
    public sealed class MenuBackdrop : VisualElement
    {
        /// <summary>Частка ширини, яку займає клин зліва (титул — 0,46: права третина лишається рингу, B01 §2).</summary>
        public float WedgeWidth = 0.62f;
        public float Scrim = 0.55f;
        public float WedgeAlpha = 0.86f;
        public bool Halftone = true;
        public bool CrimsonCut = true;

        public MenuBackdrop()
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
            if (Scrim > 0f) MenuShapes.Polygon(p, new[] { new Vector2(0, 0), new Vector2(W, 0), new Vector2(W, H), new Vector2(0, H) },
                RingTheme.WithAlpha(RingTheme.Bg, Scrim));
            float ww = W * WedgeWidth;
            // клин: широкий угорі, звужується донизу — діагональ «удару»
            var wedge = new[] { new Vector2(0, 0), new Vector2(ww, 0), new Vector2(ww * 0.62f, H), new Vector2(0, H) };
            MenuShapes.Polygon(p, wedge, RingTheme.WithAlpha(RingTheme.Hero, WedgeAlpha));
            // темна смуга вздовж краю клина — читабельність тексту на ньому (ACC-03)
            var inner = new[] { new Vector2(0, 0), new Vector2(ww * 0.86f, 0), new Vector2(ww * 0.50f, H), new Vector2(0, H) };
            MenuShapes.Polygon(p, inner, RingTheme.WithAlpha(RingTheme.Bg, BrandTokens.AlphaUnderlay));
            if (Halftone)
            {
                // напівтон уздовж краю клина: крапки зменшуються до центру екрана
                float step = Math.Max(10f, H / 64f);
                int rows = (int)(H / step) + 1;
                for (int j = 0; j < rows; j++)
                {
                    float y = j * step;
                    float edge = ww + (ww * 0.62f - ww) * (y / H);   // x краю клина на висоті y
                    float x0 = edge - step * 4f, x1 = edge + step * 7f;
                    int i = 0;
                    for (float x = x0 + (j % 2 == 0 ? 0f : step * 0.5f); x < x1; x += step, i++)
                    {
                        float t = (x - x0) / (x1 - x0);
                        float rad = step * 0.42f * (1f - t);
                        if (rad < 0.8f) continue;
                        MenuShapes.Dot(p, x, y, rad, RingTheme.WithAlpha(RingTheme.HeroText, BrandTokens.AlphaTronDim));
                    }
                }
            }
            if (CrimsonCut)
            {
                // два кармінні розрізи праворуч унизу — кут «удару», не накриває центр
                var cut = new[] { new Vector2(W * 0.84f, H), new Vector2(W, H * 0.58f), new Vector2(W, H * 0.66f), new Vector2(W * 0.90f, H) };
                MenuShapes.Polygon(p, cut, RingTheme.WithAlpha(RingTheme.Villain, 0.9f));
                MenuShapes.Line(p, W * 0.80f, H, W, H * 0.50f, 3f, RingTheme.WithAlpha(RingTheme.Text, BrandTokens.AlphaTronDim));
            }
        }
    }

    /// <summary>
    /// Лінії швидкості — живий фон (повільний дрейф по діагоналі). Малюються раз; рухається весь шар (translate), тож дрейф
    /// нічого не коштує (PERF-01). «Менше руху» — стоять.
    /// </summary>
    public sealed class SpeedLines : VisualElement
    {
        private readonly int _seed;

        public SpeedLines(int seed)
        {
            _seed = seed;
            style.position = Position.Absolute;
            style.left = Length.Percent(-20f); style.top = 0f;
            style.width = Length.Percent(140f); style.bottom = 0f;
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
            uint s = (uint)(_seed * 2654435761u + 12345u);
            for (int i = 0; i < 26; i++)
            {
                s = s * 1664525u + 1013904223u;
                float y = (s >> 8) % 10000 / 10000f * H;
                s = s * 1664525u + 1013904223u;
                float x = (s >> 8) % 10000 / 10000f * W;
                s = s * 1664525u + 1013904223u;
                float len = 60f + (s >> 8) % 10000 / 10000f * 260f;
                float k = 0.36f;   // ≈ 20° — діагональ ліній (декор), не нахил тексту
                MenuShapes.Line(p, x, y, x + len, y - len * k, i % 5 == 0 ? 3f : 1.5f,
                    RingTheme.WithAlpha(i % 4 == 0 ? RingTheme.HeroText : RingTheme.Text, BrandTokens.AlphaTronGhost));
            }
        }
    }

    /// <summary>Сегментна шкала (гучність, прогрес): паралелограми 12°, заповнені — колір дії.</summary>
    public sealed class SegmentBar : VisualElement
    {
        private int _count = 10;
        private float _filled;
        private Color _on = RingTheme.Gold, _off = RingTheme.Border;

        public SegmentBar()
        {
            generateVisualContent = Draw;
            pickingMode = PickingMode.Ignore;
        }

        public void Set(int count, float filled, Color on, Color off)
        {
            _count = Math.Max(1, count); _filled = filled; _on = on; _off = off;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width < 4f || r.height < 2f) return;
            var p = ctx.painter2D;
            float gap = Math.Max(3f, r.width * 0.012f);
            float k = MenuShapes.SlantK;
            float cw = (r.width - k * r.height - gap * (_count - 1)) / _count;
            for (int i = 0; i < _count; i++)
            {
                float x = i * (cw + gap);
                float part = Mathf.Clamp01(_filled - i);
                MenuShapes.Parallelogram(p, x, 0f, cw, r.height, k, _off);
                if (part > 0f) MenuShapes.Parallelogram(p, x, 0f, cw * part, r.height, k, _on);
            }
        }
    }

    /// <summary>Промені позаду печатки на екрані завантаження (дешево: малюються раз, крутиться шар).</summary>
    public sealed class Rays : VisualElement
    {
        public Rays()
        {
            generateVisualContent = Draw;
            pickingMode = PickingMode.Ignore;
        }

        private void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            float cx = r.width * 0.5f, cy = r.height * 0.5f, R = Math.Min(r.width, r.height) * 0.5f;
            if (R < 4f) return;
            var p = ctx.painter2D;
            for (int i = 0; i < 12; i++)
            {
                double a0 = (i * 30.0 - 6.0) * Math.PI / 180.0, a1 = (i * 30.0 + 6.0) * Math.PI / 180.0;
                var tri = new[]
                {
                    new Vector2(cx, cy),
                    new Vector2(cx + (float)Math.Cos(a0) * R, cy + (float)Math.Sin(a0) * R),
                    new Vector2(cx + (float)Math.Cos(a1) * R, cy + (float)Math.Sin(a1) * R),
                };
                MenuShapes.Polygon(p, tri, RingTheme.WithAlpha(RingTheme.Text, BrandTokens.AlphaTronGhost));
            }
        }
    }

    /// <summary>Спінер перевірки: три паралелограми, що «біжать» (кожен кадр — лише opacity, без перемальовування).</summary>
    public sealed class SlashSpinner : VisualElement
    {
        private readonly VisualElement[] _bars = new VisualElement[3];
        private float _t;

        public SlashSpinner()
        {
            style.flexDirection = FlexDirection.Row;
            pickingMode = PickingMode.Ignore;
            for (int i = 0; i < _bars.Length; i++)
            {
                var b = new VisualElement();
                b.style.width = 12f; b.style.height = 26f; b.style.marginRight = 5f;
                int idx = i;
                b.generateVisualContent = ctx =>
                {
                    var rr = _bars[idx].contentRect;
                    MenuShapes.Parallelogram(ctx.painter2D, 0f, 0f, rr.width - MenuShapes.SlantK * rr.height * 2f, rr.height, MenuShapes.SlantK * 2f, RingTheme.Gold);
                };
                _bars[i] = b;
                Add(b);
            }
        }

        public void Tick(float dt)
        {
            _t += dt;
            for (int i = 0; i < _bars.Length; i++)
            {
                float ph = (_t * 2.2f - i * 0.22f) % 1f;
                if (ph < 0f) ph += 1f;
                _bars[i].style.opacity = 0.25f + 0.75f * (ph < 0.5f ? ph * 2f : (1f - ph) * 2f);
            }
        }
    }
}
