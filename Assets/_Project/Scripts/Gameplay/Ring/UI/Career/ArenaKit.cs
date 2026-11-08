using System;
using System.Collections.Generic;
using Game.Core.Ring;
using Game.Gameplay.Ring.Models;
using Game.Gameplay.Ring.UI.Menus;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.Ring.UI
{
    /// <summary>
    /// Складники екранів кар'єри в мові меню «Арена» (ТЗ B03 v2 + палітра B08 §0.1, власник 08.11.2026: «давай дизайн більш
    /// схожий на реслінг»): той самий каркас, що й HUD матчу — стрічки борців по боках, табличка кроку в центрі, одна паперова
    /// афіша, канати, ряд плиток дій і золота плита внизу праворуч. Розміри — в еталоні 900 рядків (панель масштабує сама).
    /// Плити малюються Painter2D один раз на зміну розміру (PERF-01); кольори — лише токени.
    /// </summary>
    public static class ArenaKit
    {
        // ---- розкладка (еталон 900; B03 §2: значення 1080p × 0,833) ----
        public const float Edge = 80f;          // безпечні боки ACC-04
        public const float Top = 45f;
        public const float StripW = 410f, StripH = 106f;
        public const float SecondY = 162f;      // друга плита під стрічкою
        public const float RopesY = 668f;
        public const float HeaderY = 694f;
        public const float RowY = 743f, TileH = 112f;
        public const float GoldW = 273f, GoldWNarrow = 210f;
        public const float NarrowWidth = 1450f;
        // Низ екрана — відлік ЗНИЗУ: при «Розмірі інтерфейсу» 90–120 % логічна висота панелі 750–1000, а не 900
        public const float RowBottom = 45f, HeaderBottom = 164f, RopesBottom = 210f; // ширша панель — золота плита повна; вужча (4:3, великий масштаб) — вужча

        /// <summary>Колір обводки й смужки за роллю (герой — кобальт, лиходій — кармін, бунтар — рожевий): мітка сторони, не оформлення.</summary>
        public static Color RoleColor(RingPath p)
        {
            switch (p)
            {
                case RingPath.Face: return RingTheme.Hero;
                case RingPath.Heel: return RingTheme.Villain;
                default: return RingTheme.Rebel;
            }
        }

        /// <summary>Колір слова ролі на темному (текстовий варіант кольору сторони — контраст ≥ 4,5).</summary>
        public static Color RoleText(RingPath p)
        {
            switch (p)
            {
                case RingPath.Face: return RingTheme.HeroText;
                case RingPath.Heel: return RingTheme.VillainText;
                default: return RingTheme.Rebel;
            }
        }

        public static VisualElement Abs(float? left, float? top, float? right, float? bottom, float? width = null, float? height = null)
        {
            var e = new VisualElement();
            e.style.position = Position.Absolute;
            if (left.HasValue) e.style.left = left.Value;
            if (top.HasValue) e.style.top = top.Value;
            if (right.HasValue) e.style.right = right.Value;
            if (bottom.HasValue) e.style.bottom = bottom.Value;
            if (width.HasValue) e.style.width = width.Value;
            if (height.HasValue) e.style.height = height.Value;
            e.pickingMode = PickingMode.Ignore;
            return e;
        }

        public static void Place(VisualElement e, float? left, float? top, float? right, float? bottom, float? width = null, float? height = null)
        {
            e.style.position = Position.Absolute;
            if (left.HasValue) e.style.left = left.Value;
            if (top.HasValue) e.style.top = top.Value;
            if (right.HasValue) e.style.right = right.Value;
            if (bottom.HasValue) e.style.bottom = bottom.Value;
            if (width.HasValue) e.style.width = width.Value;
            if (height.HasValue) e.style.height = height.Value;
        }

        public static VisualElement Row(float gap = 0f)
        {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row;
            r.style.alignItems = Align.Center;
            r.pickingMode = PickingMode.Ignore;
            if (gap > 0f) r.style.marginBottom = gap;
            return r;
        }

        public static VisualElement Spacer()
        {
            var s = new VisualElement();
            s.style.flexGrow = 1f;
            s.pickingMode = PickingMode.Ignore;
            return s;
        }

        /// <summary>Підпис-мітка (display ВЕЛИКИМИ, розряджено) — «ГОЛОВА», «ПОПУЛЯРНІСТЬ».</summary>
        public static Label Tag(string text, Color color, float size = BrandTokens.TypeLabel)
        {
            return MenuKit.Display(text, size, color, RingFontRole.Display700, 0.12f);
        }

        /// <summary>Число / коротке слово display Black.</summary>
        public static Label Big(string text, float size, Color color, bool italic = false)
        {
            var l = MenuKit.Display(text, size, color, RingFontRole.Display900, 0.01f);
            if (italic) l.style.unityFontStyleAndWeight = FontStyle.Italic;
            return l;
        }

        /// <summary>«₴ 32»: знак гривні — Fixel (у display його немає), цифри — display Black (B03 §2).</summary>
        public static VisualElement Money(int amount, float size, Color color)
        {
            var r = Row();
            r.style.alignItems = Align.FlexEnd;
            var sign = MenuKit.Body("₴", size * 0.62f, color, true);
            sign.style.marginRight = 3f;
            sign.style.marginBottom = size * 0.08f;
            r.Add(sign);
            r.Add(Big(amount.ToString(), size, color));
            return r;
        }

        /// <summary>Сегментна шкала (популярність, довіра, ворожнеча, зони тіла).</summary>
        public static SegmentBar Segments(int count, int filled, Color on, float width, float height = 15f, Color? off = null)
        {
            var bar = new SegmentBar();
            bar.style.width = width; bar.style.height = height;
            bar.Set(count, filled, on, off ?? RingTheme.Border);
            return bar;
        }

        /// <summary>Ім'я NPC «Матео «Колібрі» Рамос» → прізвисько «Колібрі» і ім'я «Матео Рамос» (стрічка суперника: прізвисько великим).</summary>
        public static void SplitNick(string full, out string nick, out string name)
        {
            nick = full ?? ""; name = "";
            if (string.IsNullOrEmpty(full)) return;
            int a = full.IndexOf('«'), b = a >= 0 ? full.IndexOf('»', a + 1) : -1;
            if (a < 0 || b < 0) return;
            nick = full.Substring(a + 1, b - a - 1).Trim();
            name = (full.Substring(0, a) + " " + full.Substring(b + 1)).Trim();
            while (name.Contains("  ")) name = name.Replace("  ", " ");
        }
    }

    /// <summary>
    /// Плита «Арени»: темна заливка зі зрізом 12°, жорстка тінь, обводка (колір ролі) і смужка на зовнішньому краї. Дзеркальна —
    /// для правого боку екрана (зріз ліворуч, смужка праворуч).
    /// </summary>
    public sealed class ArenaPlate : VisualElement
    {
        public Color Fill = RingTheme.WithAlpha(RingTheme.Panel, BrandTokens.AlphaPanel);
        public Color? Outline;
        public float OutlineW = 3f;
        public Color? Stripe;
        public float StripeW = 7f;
        public bool Mirror;
        public bool Shadow = true;
        public bool SlantBoth;

        public ArenaPlate()
        {
            style.position = Position.Absolute;
            style.left = 0f; style.top = 0f; style.right = 0f; style.bottom = 0f;
            style.overflow = Overflow.Visible;
            pickingMode = PickingMode.Ignore;
            generateVisualContent = Draw;
            RegisterCallback<GeometryChangedEvent>(e => MarkDirtyRepaint());
        }

        public void Redraw() { MarkDirtyRepaint(); }

        private Vector2[] Shape(float x, float y, float w, float h, float k)
        {
            float o = k * h;
            if (SlantBoth) return new[] { new Vector2(x + o, y), new Vector2(x + w, y), new Vector2(x + w - o, y + h), new Vector2(x, y + h) };
            if (Mirror) return new[] { new Vector2(x + o, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x, y + h) };
            return new[] { new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w - o, y + h), new Vector2(x, y + h) };
        }

        private void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            float w = r.width, h = r.height;
            if (w < 4f || h < 4f) return;
            var p = ctx.painter2D;
            float k = MenuShapes.SlantK;
            if (Shadow)
            {
                float sh = BrandTokens.SpaceShadowOffset;
                MenuShapes.Polygon(p, Shape(sh, sh, w, h, k), RingTheme.WithAlpha(RingTheme.Black, BrandTokens.AlphaUnderlay));
            }
            if (Outline.HasValue && OutlineW > 0f)
            {
                float ow = OutlineW;
                MenuShapes.Polygon(p, Shape(-ow, -ow, w + 2f * ow, h + 2f * ow, k), Outline.Value);
            }
            MenuShapes.Polygon(p, Shape(0f, 0f, w, h, k), Fill);
            if (Stripe.HasValue && StripeW > 0f)
            {
                if (Mirror) MenuShapes.Polygon(p, new[] { new Vector2(w - StripeW, 0f), new Vector2(w, 0f), new Vector2(w, h), new Vector2(w - StripeW, h) }, Stripe.Value);
                else MenuShapes.Polygon(p, new[] { new Vector2(0f, 0f), new Vector2(StripeW, 0f), new Vector2(StripeW, h), new Vector2(0f, h) }, Stripe.Value);
            }
        }
    }

    /// <summary>
    /// Паперова афіша (єдина паперова поверхня екрана, B03): жорстка тінь, фаска 12° у верхньому лівому куті; за бажанням —
    /// рваний нижній край (детермінована хвиля, без випадковості).
    /// </summary>
    public sealed class PaperSheet : VisualElement
    {
        public bool Torn = true;
        public bool Chamfer = true;

        public PaperSheet()
        {
            style.position = Position.Absolute;
            style.left = 0f; style.top = 0f; style.right = 0f; style.bottom = 0f;
            style.overflow = Overflow.Visible;
            pickingMode = PickingMode.Ignore;
            generateVisualContent = Draw;
            RegisterCallback<GeometryChangedEvent>(e => MarkDirtyRepaint());
        }

        private List<Vector2> Outline(float dx, float dy, float w, float h)
        {
            float c = Chamfer ? 30f : 0f;
            var pts = new List<Vector2> { new Vector2(dx + c, dy), new Vector2(dx + w, dy) };
            if (!Torn) { pts.Add(new Vector2(dx + w, dy + h)); pts.Add(new Vector2(dx, dy + h)); }
            else
            {
                const float step = 14f, amp = 4f;
                pts.Add(new Vector2(dx + w, dy + h - amp));
                int n = (int)(w / step);
                for (int i = n; i >= 0; i--)
                {
                    float x = dx + i * (w / Math.Max(1, n));
                    float y = dy + h - amp + ((i % 2 == 0) ? amp : -amp);
                    pts.Add(new Vector2(x, y));
                }
            }
            pts.Add(new Vector2(dx, dy + c * MenuShapes.SlantK * 4f));
            return pts;
        }

        private void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            float w = r.width, h = r.height;
            if (w < 8f || h < 8f) return;
            var p = ctx.painter2D;
            float sh = BrandTokens.SpaceShadowOffset;
            MenuShapes.Polygon(p, Outline(sh, sh, w, h), RingTheme.WithAlpha(RingTheme.Black, BrandTokens.AlphaUnderlay));
            MenuShapes.Polygon(p, Outline(0f, 0f, w, h), RingTheme.Paper);
        }
    }

    /// <summary>Наліпка-загроза («У ФОРМІ · СИЛЬНІШИЙ НА 1»): сигнальна плита навскіс, текст кольору тла (4,9 : 1), B03 §2.</summary>
    public sealed class Sticker : VisualElement
    {
        public Sticker(string text)
        {
            style.flexDirection = FlexDirection.Row;
            style.alignSelf = Align.FlexStart;
            style.overflow = Overflow.Visible;
            pickingMode = PickingMode.Ignore;
            var plate = new ArenaPlate { Fill = RingTheme.Signal, Shadow = true, SlantBoth = true };
            Add(plate);
            var l = MenuKit.Display(text, 19f, RingTheme.Bg, RingFontRole.Display900, 0.04f);
            l.style.marginLeft = 22f; l.style.marginRight = 26f; l.style.marginTop = 6f; l.style.marginBottom = 6f;
            Add(l);
        }
    }

    /// <summary>Канати над рядом дій (B03 §2): три лінії — папір, кармін, папір — і подушки на кінцях (папір із кармінною стяжкою).</summary>
    public sealed class RopeLine : VisualElement
    {
        public RopeLine()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent = Draw;
            RegisterCallback<GeometryChangedEvent>(e => MarkDirtyRepaint());
        }

        private void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            float w = r.width;
            if (w < 20f) return;
            var p = ctx.painter2D;
            float y0 = 4f;
            var paper = RingTheme.WithAlpha(RingTheme.Paper, BrandTokens.AlphaTronDim + 0.3f);
            MenuShapes.Line(p, 8f, y0, w - 8f, y0, 3f, paper);
            MenuShapes.Line(p, 8f, y0 + 6f, w - 8f, y0 + 6f, 3f, RingTheme.Crimson);
            MenuShapes.Line(p, 8f, y0 + 12f, w - 8f, y0 + 12f, 3f, paper);
            foreach (float x in new[] { 0f, w - 10f })
            {
                MenuShapes.Polygon(p, new[] { new Vector2(x, 0f), new Vector2(x + 10f, 0f), new Vector2(x + 10f, 22f), new Vector2(x, 22f) }, RingTheme.Paper);
                MenuShapes.Polygon(p, new[] { new Vector2(x, 9f), new Vector2(x + 10f, 9f), new Vector2(x + 10f, 13f), new Vector2(x, 13f) }, RingTheme.Crimson);
            }
        }
    }

    /// <summary>Лялька травм (B03 §2): голова, корпус, ноги; зона з ушкодженням — сигнальна, здорова — темна. Число й метр поруч (колір не єдиний носій).</summary>
    public sealed class InjuryDoll : VisualElement
    {
        private readonly bool[] _hurt = new bool[3];

        public InjuryDoll()
        {
            style.width = 36f; style.height = 70f;
            pickingMode = PickingMode.Ignore;
            generateVisualContent = Draw;
        }

        public void Set(int zone, bool hurt) { if (zone >= 0 && zone < 3) { _hurt[zone] = hurt; MarkDirtyRepaint(); } }

        private void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            Color off = RingTheme.Border, on = RingTheme.Signal;
            MenuShapes.Dot(p, 18f, 8f, 8f, _hurt[0] ? on : off);
            MenuShapes.Polygon(p, new[] { new Vector2(6f, 19f), new Vector2(30f, 19f), new Vector2(27f, 44f), new Vector2(9f, 44f) }, _hurt[1] ? on : off);
            MenuShapes.Polygon(p, new[] { new Vector2(9f, 46f), new Vector2(17f, 46f), new Vector2(16f, 70f), new Vector2(9f, 70f) }, _hurt[2] ? on : off);
            MenuShapes.Polygon(p, new[] { new Vector2(19f, 46f), new Vector2(27f, 46f), new Vector2(27f, 70f), new Vector2(20f, 70f) }, _hurt[2] ? on : off);
        }
    }

    /// <summary>
    /// Плитка дії ряду (B03 §2–§3): ключ-цифра в золотому квадраті, назва display, ефект, ризик окремим рядком, смуга шансів.
    /// Стани: звичайна (смужка золота) · фокус (золота заливка, зсув угору) · вибрана (заливка кольору <see cref="SelectColor"/> + галочка) ·
    /// недоступна (сіра смужка, причина сигналом). Наведення — фокус і опис у рядку-підказці; клік — дія.
    /// </summary>
    public sealed class ActionTile : VisualElement
    {
        private readonly ArenaPlate _plate;
        private readonly Label _title, _effect, _risk, _key;
        private readonly CheckMark _check;
        private readonly VisualElement _keyBox, _chances, _body, _head;
        private readonly bool _enabled;
        private bool _compact;
        private bool _focus, _selected;
        public Action OnClick;
        public Action OnHover;
        public Color Accent = RingTheme.Gold;
        public Color SelectColor = RingTheme.Gold;
        /// <summary>Вибрана плитка світла (золото) — текст темний; темна заливка ролі (кобальт, кармін) — текст світлий.</summary>
        public bool SelectDarkText = true;

        public ActionTile(int key, string title, string effect, string risk, bool enabled, string reason = null)
        {
            _enabled = enabled;
            style.flexGrow = 1f; style.flexShrink = 1f; style.flexBasis = 0f;
            style.height = ArenaKit.TileH;
            style.marginRight = 9f;
            style.overflow = Overflow.Visible;
            _plate = new ArenaPlate { Fill = RingTheme.Panel, Stripe = RingTheme.Gold, StripeW = 6f };
            Add(_plate);
            var body = new VisualElement();
            body.style.position = Position.Absolute;
            body.style.left = 18f; body.style.right = 22f; body.style.top = 10f; body.style.bottom = 8f;
            body.pickingMode = PickingMode.Ignore;
            Add(body);
            _body = body;
            var head = ArenaKit.Row();
            _head = head;
            head.style.alignItems = Align.FlexStart;
            _title = MenuKit.Display(title, 21f, RingTheme.Text, RingFontRole.Display800, 0.03f);
            _title.style.whiteSpace = WhiteSpace.Normal;
            _title.style.flexShrink = 1f; _title.style.flexGrow = 1f;
            head.Add(_title);
            _keyBox = new VisualElement();
            _keyBox.style.width = 30f; _keyBox.style.height = 30f; _keyBox.style.flexShrink = 0f;
            _keyBox.style.justifyContent = Justify.Center; _keyBox.style.alignItems = Align.Center;
            _keyBox.style.marginLeft = 6f;
            _keyBox.pickingMode = PickingMode.Ignore;
            _key = MenuKit.Display(key > 0 ? key.ToString() : "", 21f, RingTheme.Bg, RingFontRole.Display900, 0f);
            _key.style.unityTextAlign = TextAnchor.MiddleCenter;
            _keyBox.Add(_key);
            if (key > 0) head.Add(_keyBox);
            body.Add(head);
            _effect = MenuKit.Body(effect ?? "", BrandTokens.TypeMinInfo, RingTheme.TextDim);
            _effect.style.marginTop = 4f;
            body.Add(_effect);
            _risk = MenuKit.Body(!enabled && !string.IsNullOrEmpty(reason) ? reason : risk ?? "", BrandTokens.TypeMinInfo, RingTheme.Signal);
            _risk.style.display = string.IsNullOrEmpty(_risk.text) ? DisplayStyle.None : DisplayStyle.Flex;
            body.Add(_risk);
            _chances = new VisualElement();
            _chances.style.position = Position.Absolute;
            _chances.style.left = 18f; _chances.style.bottom = 10f; _chances.style.height = 7f; _chances.style.width = 120f;
            _chances.style.flexDirection = FlexDirection.Row;
            _chances.style.display = DisplayStyle.None;
            _chances.pickingMode = PickingMode.Ignore;
            Add(_chances);
            _check = new CheckMark();   // галочка малюється: у display-шрифті немає U+2713 (перевірено fontTools 08.11.2026)
            _check.style.position = Position.Absolute;
            _check.style.right = 50f; _check.style.top = 14f;   // ліворуч від ключа: унизу лягала на рядок ефекту (кадр 4:3)
            _check.style.display = DisplayStyle.None;
            Add(_check);
            RegisterCallback<PointerEnterEvent>(e => { if (OnHover != null) OnHover(); });
            RegisterCallback<PointerDownEvent>(e => { if (_enabled && e.button == 0 && OnClick != null) OnClick(); });
            Sync();
        }

        public bool Enabled { get { return _enabled; } }

        /// <summary>
        /// Компактна плитка (вузька панель — 4:3 чи великий масштаб інтерфейсу; кадри 08.11.2026: на ~120 одиниць ширини назви
        /// рвались посеред слова й вилазили за плитку): кегль назви менший, ключ-цифра сидить на верхньому краї плитки, а не в рядку.
        /// </summary>
        public void SetCompact(bool on)
        {
            if (_compact == on) return;
            _compact = on;
            _title.style.fontSize = on ? 17f : 21f;
            _body.style.left = on ? 12f : 18f;
            _body.style.right = on ? 14f : 22f;
            _body.style.top = on ? 14f : 10f;
            _keyBox.RemoveFromHierarchy();
            if (on)
            {
                _keyBox.style.position = Position.Absolute;
                _keyBox.style.top = -11f; _keyBox.style.right = 14f;
                _keyBox.style.width = 24f; _keyBox.style.height = 24f;
                _keyBox.style.marginLeft = 0f;
                _key.style.fontSize = 17f;
                Add(_keyBox);
            }
            else
            {
                _keyBox.style.position = Position.Relative;
                _keyBox.style.width = 30f; _keyBox.style.height = 30f;
                _keyBox.style.marginLeft = 6f;
                _key.style.fontSize = 21f;
                _head.Add(_keyBox);
            }
        }

        /// <summary>Смуга шансів 2d6 (частки з 36): промах — сигнал, частково — папір, успіх — золото (ACC-02: число в ефекті поруч).</summary>
        public void SetChances(int miss36, int partial36, int strong36)
        {
            _chances.Clear();
            int total = Math.Max(1, miss36 + partial36 + strong36);
            Add3(miss36, total, RingTheme.Signal);
            Add3(partial36, total, RingTheme.Paper);
            Add3(strong36, total, RingTheme.Gold);
            _chances.style.display = DisplayStyle.Flex;
        }

        private void Add3(int part, int total, Color c)
        {
            if (part <= 0) return;
            var s = new VisualElement();
            s.style.width = Length.Percent(part * 100f / total);
            s.style.backgroundColor = c;
            s.style.marginRight = 2f;
            s.pickingMode = PickingMode.Ignore;
            _chances.Add(s);
        }

        public bool Focused { get { return _focus; } set { if (_focus == value) return; _focus = value; Sync(); } }
        public bool Selected { get { return _selected; } set { if (_selected == value) return; _selected = value; Sync(); } }

        public void Sync()
        {
            bool hot = _enabled && (_focus || _selected);
            Color fill = !_enabled ? RingTheme.WithAlpha(RingTheme.Panel, BrandTokens.AlphaPanel)
                : _selected ? SelectColor : _focus ? RingTheme.Gold : RingTheme.Panel;
            _plate.Fill = fill;
            _plate.Stripe = !_enabled ? RingTheme.Disabled : hot ? (Color?)null : Accent;
            _plate.Outline = _focus && _enabled ? (Color?)RingTheme.Text : null;
            _plate.Redraw();
            bool darkText = hot && (!_selected || SelectDarkText);
            Color text = !_enabled ? RingTheme.Disabled : darkText ? RingTheme.Bg : RingTheme.Text;
            _title.style.color = text;
            _effect.style.color = !_enabled ? RingTheme.Disabled : darkText ? RingTheme.Bg : RingTheme.TextDim;
            _keyBox.style.backgroundColor = !_enabled ? RingTheme.Disabled : darkText ? RingTheme.Bg : RingTheme.Gold;
            _key.style.color = darkText ? RingTheme.Gold : RingTheme.Bg;
            _check.style.display = _selected ? DisplayStyle.Flex : DisplayStyle.None;
            _check.Ink = darkText ? RingTheme.Bg : RingTheme.Text;
            style.translate = new Translate(new Length(0f), new Length(_focus && _enabled && !MenuAnimator.Reduced ? -7f : 0f));
        }
    }

    /// <summary>Галочка вибраної плитки (два відрізки Painter2D).</summary>
    public sealed class CheckMark : VisualElement
    {
        private Color _ink = RingTheme.Bg;

        public CheckMark()
        {
            style.width = 24f; style.height = 20f;
            pickingMode = PickingMode.Ignore;
            generateVisualContent = Draw;
        }

        public Color Ink { set { _ink = value; MarkDirtyRepaint(); } }

        private void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            MenuShapes.Line(p, 2f, 10f, 9f, 18f, 4f, _ink);
            MenuShapes.Line(p, 9f, 18f, 22f, 2f, 4f, _ink);
        }
    }

    /// <summary>Золота плита головної дії (B03: «ПЕРЕЙТИ ДО ВЕЧОРА», «ДАЛІ», «ПОЧАТИ КАР'ЄРУ»): лівий край скошений, слово display Black, «ENTER».</summary>
    public sealed class GoldPlate : VisualElement
    {
        private readonly bool _enabled;
        private readonly ArenaPlate _plate;

        public GoldPlate(string text, string keyHint, Action onClick, bool enabled = true)
        {
            _enabled = enabled;
            style.overflow = Overflow.Visible;
            _plate = new ArenaPlate { Fill = enabled ? RingTheme.Gold : RingTheme.PanelRaised, Mirror = true };
            Add(_plate);
            var word = MenuKit.Display(text, 36f, enabled ? RingTheme.Bg : RingTheme.Disabled, RingFontRole.Display900, 0.01f);
            word.style.whiteSpace = WhiteSpace.Normal;
            word.style.position = Position.Absolute;
            word.style.left = 40f; word.style.right = 16f; word.style.top = 12f;
            Add(word);
            var hint = MenuKit.Display(keyHint ?? "", BrandTokens.TypeLabel, enabled ? RingTheme.Bg : RingTheme.Disabled, RingFontRole.Display800, 0.12f);
            hint.style.position = Position.Absolute;
            hint.style.right = 18f; hint.style.bottom = 10f;
            Add(hint);
            RegisterCallback<PointerDownEvent>(e => { if (_enabled && e.button == 0 && onClick != null) onClick(); });
            RegisterCallback<PointerEnterEvent>(e => { if (_enabled) { _plate.Outline = RingTheme.Text; _plate.Redraw(); } });
            RegisterCallback<PointerLeaveEvent>(e => { _plate.Outline = null; _plate.Redraw(); });
        }
    }

    /// <summary>Вкладка-акордеон шапки ряду («РОЗВИТОК 6», «ВИТРАТИ 2»): плита зі зрізом, число золотом; відкрита — обводка паперу.</summary>
    public sealed class TabPlate : VisualElement
    {
        public TabPlate(string text, int number, bool open, Action onClick, bool goldStripe)
        {
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.height = 40f;
            style.paddingLeft = 18f; style.paddingRight = 26f;
            style.marginLeft = 10f;
            style.overflow = Overflow.Visible;
            var plate = new ArenaPlate { Fill = RingTheme.Panel, Stripe = goldStripe ? (Color?)RingTheme.Gold : null, StripeW = 5f, Outline = open ? (Color?)RingTheme.Text : null, OutlineW = 2f };
            Add(plate);
            var l = MenuKit.Display(text, 19f, RingTheme.Text, RingFontRole.Display800, 0.06f);
            Add(l);
            var n = MenuKit.Display(number.ToString(), 21f, goldStripe ? RingTheme.Gold : RingTheme.TextDim, RingFontRole.Display900, 0f);
            n.style.marginLeft = 14f;
            Add(n);
            RegisterCallback<PointerDownEvent>(e => { if (e.button == 0 && onClick != null) onClick(); });
        }
    }
}
