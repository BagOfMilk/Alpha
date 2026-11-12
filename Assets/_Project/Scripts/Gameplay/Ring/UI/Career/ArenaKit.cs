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
        /// <summary>Підпис клавіші головної дії на золотій плиті: «Enter» або «A» — за пристроєм, яким гравець грав останнім (P3 геймпад).</summary>
        public static string ConfirmHint(RingText t) { return t.Get(Menus.RingMenuInput.PadLast ? "m.hint.pad_a" : "m.hint.enter"); }

        /// <summary>Рядок клавіш екрана вибору (промо, ворожнеча): клавіатурний чи геймпадний — за останнім пристроєм.</summary>
        public static string ChooseHint(RingText t) { return t.Get(Menus.RingMenuInput.PadLast ? "m.hint.pad_choose" : "ui.hint.keys"); }

        // ---- розкладка (еталон 900; B03 §2: значення 1080p × 0,833) ----
        public const float Edge = CareerTileLayout.Edge;          // безпечні боки ACC-04
        public const float Top = 45f;
        public const float StripW = 410f, StripH = 106f;
        public const float SecondY = 162f;      // друга плита під стрічкою
        public const float RopesY = 668f;
        public const float HeaderY = 694f;
        public const float RowY = 743f, TileH = CareerTileLayout.TileH;
        public const float GoldW = CareerTileLayout.GoldW, GoldWNarrow = CareerTileLayout.GoldWNarrow;
        public const float NarrowWidth = CareerTileLayout.NarrowWidth;
        // Плитки ряду: вужчі за CompactTileW в один ряд (16:9 — ~183) — компактні; текст, що не вміщається в TileH, піднімає ряд
        // не більш ніж на TileGrowMax (кадри слоту 27, 09.11.2026: назви рвались посеред слова, шанси лягали під смугу).
        public const float CompactTileW = CareerTileLayout.CompactTileW, TileGrowMax = CareerTileLayout.TileGrowMax;
        // Шухляда тижня не налазить на афішу, доки під афішею лишається хоч стільки (далі — прокрутка); менше — до стрічки суперника.
        public const float DrawerMinRoom = 150f;
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

        /// <summary>
        /// Смуга прокрутки мовою «Арена»: тонка темна рейка, золотий повзунок, без стрілок. Системна світло-сіра смуга зі стрілками
        /// випадала зі стилю (шухляда «Розвиток» на кінці сезону — кадри слоту 27, 09.11.2026). Елементи — за класами USS рушія.
        /// </summary>
        public static void ThinScroller(ScrollView s)
        {
            var vs = s.Q(className: "unity-scroll-view__vertical-scroller") ?? s.Q(className: "unity-scroller--vertical");
            if (vs == null) return;
            vs.style.width = 6f; vs.style.minWidth = 6f;
            vs.style.backgroundColor = RingTheme.WithAlpha(RingTheme.Bg, BrandTokens.AlphaShade);
            NoBorder(vs);
            Hide(vs.Q(className: "unity-scroller__low-button"));
            Hide(vs.Q(className: "unity-scroller__high-button"));
            var slider = vs.Q(className: "unity-scroller__slider");
            if (slider != null) { slider.style.marginTop = 0f; slider.style.marginBottom = 0f; slider.style.marginLeft = 0f; slider.style.marginRight = 0f; }
            var tracker = vs.Q(className: "unity-base-slider__tracker");
            if (tracker != null) { tracker.style.backgroundColor = RingTheme.WithAlpha(RingTheme.Bg, 0f); NoBorder(tracker); }
            var dragger = vs.Q(className: "unity-base-slider__dragger");
            if (dragger != null)
            {
                dragger.style.backgroundColor = RingTheme.Gold;
                NoBorder(dragger);
                dragger.style.left = 0f; dragger.style.width = 6f;
                dragger.style.marginLeft = 0f; dragger.style.marginRight = 0f;
            }
            Hide(vs.Q(className: "unity-base-slider__dragger-border"));
        }

        private static void Hide(VisualElement e) { if (e != null) e.style.display = DisplayStyle.None; }

        private static void NoBorder(VisualElement e)
        {
            e.style.borderTopWidth = 0f; e.style.borderBottomWidth = 0f; e.style.borderLeftWidth = 0f; e.style.borderRightWidth = 0f;
        }

        /// <summary>Підпис-мітка (display ВЕЛИКИМИ, розряджено) — «ГОЛОВА», «ПОПУЛЯРНІСТЬ».</summary>
        public static Label Tag(string text, Color color, float size = BrandTokens.TypeLabel)
        {
            return MenuKit.Display(text, size, color, RingFontRole.Display700, 0.12f);
        }

        /// <summary>
        /// Ім'я й мітка ролі в одному рядку стрічки (рев'ю 08.11.2026: довге ім'я наїжджало на мітку — нічого їх не міряло).
        /// Після розкладки, доки ім'я не влазить до мітки з проміжком: (1) зменшити кегль імені пропорційно, не нижче
        /// <paramref name="minSize"/>; (2) сховати <paramref name="optional"/> (ґімік у мітці); (3) обрізати ім'я по ширині.
        /// <paramref name="nameLeft"/> — ім'я ліворуч, мітка праворуч (стрічка гравця), інакше дзеркально (суперник).
        /// </summary>
        public static void FitNameAndTag(VisualElement box, Label name, VisualElement tag, VisualElement optional, bool nameLeft, float size, float minSize)
        {
            float cur = size;
            int stage = 0;
            box.RegisterCallback<GeometryChangedEvent>(e =>
            {
                Rect n = name.layout, t = tag.layout;
                if (float.IsNaN(n.width) || float.IsNaN(t.width) || n.width <= 0f) return;
                float avail = nameLeft ? t.x - n.x - 14f : (n.x + n.width) - (t.x + t.width) - 14f;
                if (stage >= 3 || n.width <= avail + 0.5f) return;
                if (stage == 0 && cur > minSize)
                {
                    cur = Mathf.Max(minSize, (float)Math.Floor(cur * avail / n.width));
                    name.style.fontSize = cur;
                    name.style.letterSpacing = new Length(cur * 0.01f);
                    if (cur <= minSize) stage = 1;
                    return;
                }
                if (stage <= 1 && optional != null) { optional.style.display = DisplayStyle.None; stage = 2; return; }
                name.style.width = Mathf.Max(40f, avail);
                name.style.overflow = Overflow.Hidden;
                stage = 3;
            });
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
    public sealed class ActionTile : VisualElement, IPadTarget
    {
        private readonly ArenaPlate _plate;
        private readonly Label _title, _titleProbe, _effect, _risk, _key;
        private readonly Label[] _legend = new Label[3];
        private readonly CheckMark _check;
        private readonly VisualElement _keyBox, _chances, _legendRow, _parts, _body, _head, _face;
        private readonly List<Label> _partLabels = new List<Label>();
        private readonly bool _enabled;
        private bool _compact;
        private float _bodyTop = CareerTileLayout.BodyTop, _titleSize = CareerTileLayout.TitleSize;
        private bool _focus, _selected;
        public Action OnClick;
        /// <summary>Наведення мишею або фокус без миші: екран показує фокус плитки й опис у рядку-підказці.</summary>
        public Action OnHover;
        /// <summary>Мишу прибрали з плитки або фокус пішов: екран повертає рядок-підказку.</summary>
        public Action OnUnhover;
        /// <summary>Ранг цілі фокусу без миші (<see cref="IPadTarget.PadRank"/>): дія — 1; «відмовитись» (зійти з вечора, пропустити титул) — 6.</summary>
        public int PadRankValue = 1;
        /// <summary>Висота тексту плитки змінилась (перенос рядків, компактний режим) — ряд перераховує свою висоту.</summary>
        public Action OnContentChanged;
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
            // Усе видиме — у шарі _face; фокус зсуває лише його. Сама плитка (зона наведення) стоїть: коли зсувалась плитка,
            // курсор у нижніх 7 px випадав з неї — фокус знімався, плитка падала під курсор — і так щокадру (рев'ю 08.11.2026).
            _face = new VisualElement();
            _face.style.position = Position.Absolute;
            _face.style.left = 0f; _face.style.top = 0f; _face.style.right = 0f; _face.style.bottom = 0f;
            _face.style.overflow = Overflow.Visible;
            _face.pickingMode = PickingMode.Ignore;
            Add(_face);
            _plate = new ArenaPlate { Fill = RingTheme.Panel, Stripe = RingTheme.Gold, StripeW = 6f };
            _plate.pickingMode = PickingMode.Ignore;
            _face.Add(_plate);
            var body = new VisualElement();
            body.style.position = Position.Absolute;
            body.style.left = CareerTileLayout.BodyLeft; body.style.right = CareerTileLayout.BodyRight;
            body.style.top = CareerTileLayout.BodyTop; body.style.bottom = CareerTileLayout.BodyBottom;
            body.pickingMode = PickingMode.Ignore;
            _face.Add(body);
            _body = body;
            var head = ArenaKit.Row();
            _head = head;
            head.style.alignItems = Align.FlexStart;
            // Назва — завжди один рядок (ПМ 11.11.2026: «жодного розриву посеред слова; назва плитки — один рядок»): довша за
            // місце — кегль меншає пропорційно, не нижче 15 (CareerTileLayout.FitTitle); охоронець CareerTextFitTests стежить,
            // щоб кожна назва влазила на 15 у найвужчу плитку. Невидима проба — та сама назва базовим кеглем, її ширина й є мірою.
            _title = MenuKit.Display(title, CareerTileLayout.TitleSize, RingTheme.Text, RingFontRole.Display800, CareerTileLayout.TitleTrack);
            _title.style.flexShrink = 1f; _title.style.flexGrow = 1f; _title.style.minWidth = 0f;
            _title.style.overflow = Overflow.Hidden;
            head.Add(_title);
            _titleProbe = MenuKit.Display(title, CareerTileLayout.TitleSize, RingTheme.Text, RingFontRole.Display800, CareerTileLayout.TitleTrack);
            _titleProbe.style.position = Position.Absolute;
            _titleProbe.style.left = 0f; _titleProbe.style.top = 0f;
            _titleProbe.style.opacity = 0f;
            _keyBox = new VisualElement();
            _keyBox.style.width = CareerTileLayout.KeyBox; _keyBox.style.height = CareerTileLayout.KeyBox; _keyBox.style.flexShrink = 0f;
            _keyBox.style.justifyContent = Justify.Center; _keyBox.style.alignItems = Align.Center;
            _keyBox.style.marginLeft = CareerTileLayout.KeyGap;
            _keyBox.pickingMode = PickingMode.Ignore;
            _key = MenuKit.Display(key > 0 ? key.ToString() : "", 21f, RingTheme.Bg, RingFontRole.Display900, 0f);
            _key.style.unityTextAlign = TextAnchor.MiddleCenter;
            _keyBox.Add(_key);
            if (key > 0) head.Add(_keyBox);
            body.Add(head);
            _effect = MenuKit.Body(effect ?? "", BrandTokens.TypeMinInfo, RingTheme.TextDim);
            _effect.style.marginTop = CareerTileLayout.LineGap;
            body.Add(_effect);
            // ефект частинами (SetEffectParts): рядок із переносом лише між частинами, кожна — без переносу всередині
            _parts = new VisualElement();
            _parts.style.flexDirection = FlexDirection.Row;
            _parts.style.flexWrap = Wrap.Wrap;
            _parts.style.marginTop = CareerTileLayout.LineGap;
            // проміжок — лівим відступом кожної частини, а сам рядок зсунуто на нього ліворуч: перша частина рядка стає до краю,
            // і проміжок не їсть місця на краях (лише між частинами)
            _parts.style.marginLeft = -CareerTileLayout.EffectPartGap;
            _parts.style.flexShrink = 0f;
            _parts.style.display = DisplayStyle.None;
            _parts.pickingMode = PickingMode.Ignore;
            body.Add(_parts);
            _risk = MenuKit.Body(!enabled && !string.IsNullOrEmpty(reason) ? reason : risk ?? "", BrandTokens.TypeMinInfo, RingTheme.Signal);
            _risk.style.display = string.IsNullOrEmpty(_risk.text) ? DisplayStyle.None : DisplayStyle.Flex;
            body.Add(_risk);
            // рядки тримають свою висоту: стиснуті flex-ом, вони малювали текст поза собою — назва вилазила над плиткою, рядок
            // тривоги — під неї (кадри слоту 27); скільки тексту насправді, каже NeededHeight
            head.style.flexShrink = 0f; _effect.style.flexShrink = 0f; _risk.style.flexShrink = 0f;
            // Смуга шансів — у потоці під текстом, на всю ширину, а під нею легенда смуг («≤6 42%», «7–9 42%», «10+ 17%») кольорами
            // сегментів (ПМ 11.11.2026: «смужка шансів іде після тексту в потоці, а не поверх»; ACC-02: число поруч зі смугою).
            _chances = new VisualElement();
            _chances.style.marginTop = CareerTileLayout.ChancesGap;
            _chances.style.height = CareerTileLayout.ChancesH; _chances.style.flexShrink = 0f;
            _chances.style.flexDirection = FlexDirection.Row;
            _chances.style.display = DisplayStyle.None;
            _chances.pickingMode = PickingMode.Ignore;
            body.Add(_chances);
            _legendRow = new VisualElement();
            _legendRow.style.flexDirection = FlexDirection.Row;
            _legendRow.style.justifyContent = Justify.SpaceBetween;
            _legendRow.style.marginTop = CareerTileLayout.LegendGap; _legendRow.style.flexShrink = 0f;
            _legendRow.style.display = DisplayStyle.None;
            _legendRow.pickingMode = PickingMode.Ignore;
            for (int i = 0; i < 3; i++)
            {
                _legend[i] = MenuKit.Display("", BrandTokens.TypeMinInfo, RingTheme.Text, RingFontRole.Display700, 0f);
                _legendRow.Add(_legend[i]);
            }
            body.Add(_legendRow);
            _face.Add(_titleProbe);
            EventCallback<GeometryChangedEvent> changed = e => { if (OnContentChanged != null) OnContentChanged(); };
            head.RegisterCallback(changed); _effect.RegisterCallback(changed); _risk.RegisterCallback(changed); _legendRow.RegisterCallback(changed);
            _parts.RegisterCallback(changed);
            _title.RegisterCallback<GeometryChangedEvent>(e => FitTitle());
            _titleProbe.RegisterCallback<GeometryChangedEvent>(e => FitTitle());
            _check = new CheckMark();   // галочка малюється: у display-шрифті немає U+2713 (перевірено fontTools 08.11.2026)
            _check.style.position = Position.Absolute;
            _check.style.right = 50f; _check.style.top = 14f;   // ліворуч від ключа: унизу лягала на рядок ефекту (кадр 4:3)
            _check.style.display = DisplayStyle.None;
            _face.Add(_check);
            RegisterCallback<PointerEnterEvent>(e => { if (PadFocus.MouseMayHover && OnHover != null) OnHover(); });
            RegisterCallback<PointerLeaveEvent>(e => { if (PadFocus.MouseMayHover && OnUnhover != null) OnUnhover(); });
            RegisterCallback<PointerDownEvent>(e => { if (_enabled && e.button == 0 && OnClick != null) OnClick(); });
            Sync();
        }

        public bool Enabled { get { return _enabled; } }

        // ---- ціль фокусу без миші (P3 геймпад, 11.11.2026): той самий фокус і той самий опис, що при наведенні ----
        bool IPadTarget.PadEnabled { get { return _enabled && OnClick != null; } }
        string IPadTarget.PadKey { get { return "tile:" + _title.text; } }
        int IPadTarget.PadRank { get { return PadRankValue; } }

        void IPadTarget.PadHot(bool on)
        {
            if (on)
            {
                if (OnHover != null) OnHover();
                else Focused = true;
            }
            else
            {
                Focused = false;
                if (OnUnhover != null) OnUnhover();
            }
        }

        void IPadTarget.PadActivate() { if (_enabled && OnClick != null) OnClick(); }
        bool IPadTarget.PadStep(int delta) { return false; }

        /// <summary>
        /// Компактна плитка (вузька панель — 4:3 чи великий масштаб інтерфейсу; кадри 08.11.2026: на ~120 одиниць ширини назви
        /// рвались посеред слова й вилазили за плитку): кегль назви менший, ключ-цифра сидить на верхньому краї плитки, а не в рядку.
        /// </summary>
        /// <returns>true — режим змінився (розкладка тексту оновиться в наступному кадрі).</returns>
        public bool SetCompact(bool on)
        {
            if (_compact == on) return false;
            _compact = on;
            float size = on ? CareerTileLayout.CompactTitleSize : CareerTileLayout.TitleSize;
            SetTitleSize(size);
            _titleProbe.style.fontSize = size;
            _titleProbe.style.letterSpacing = new Length(size * CareerTileLayout.TitleTrack);
            _body.style.left = on ? CareerTileLayout.CompactBodyLeft : CareerTileLayout.BodyLeft;
            _body.style.right = on ? CareerTileLayout.CompactBodyRight : CareerTileLayout.BodyRight;
            _bodyTop = on ? CareerTileLayout.CompactBodyTop : CareerTileLayout.BodyTop;
            _body.style.top = _bodyTop;
            _keyBox.RemoveFromHierarchy();
            if (on)
            {
                _keyBox.style.position = Position.Absolute;
                _keyBox.style.top = -11f; _keyBox.style.right = 14f;
                _keyBox.style.width = CareerTileLayout.CompactKeyBox; _keyBox.style.height = CareerTileLayout.CompactKeyBox;
                _keyBox.style.marginLeft = 0f;
                _key.style.fontSize = 17f;
                _face.Add(_keyBox);
            }
            else
            {
                _keyBox.style.position = Position.Relative;
                _keyBox.style.width = CareerTileLayout.KeyBox; _keyBox.style.height = CareerTileLayout.KeyBox;
                _keyBox.style.marginLeft = CareerTileLayout.KeyGap;
                _key.style.fontSize = 21f;
                _head.Add(_keyBox);
            }
            return true;
        }

        /// <summary>
        /// Висота, якої плитці треба на весь текст: відступ згори + рядки (назва, ефект, ризик) + низ (зі смугою шансів — місце під
        /// неї, щоб рядок не лягав під смугу). 0 — розкладки ще немає.
        /// </summary>
        public float NeededHeight()
        {
            float y = 0f;
            if (!Bottom(_head, ref y) || !Bottom(_parts.style.display.value != DisplayStyle.None ? _parts : _effect, ref y)) return 0f;
            if (_risk.style.display.value != DisplayStyle.None && !Bottom(_risk, ref y)) return 0f;
            if (_legendRow.style.display.value != DisplayStyle.None && !Bottom(_legendRow, ref y)) return 0f;
            return _bodyTop + y + CareerTileLayout.BodyBottom;
        }

        /// <summary>
        /// Ефект частинами замість одного рядка (бонуси ґіміка «Сила +2», «Спритність −1»): частини стоять рядком і
        /// переносяться лише між собою. Порожній список — назад до звичайного ефекту.
        /// </summary>
        public void SetEffectParts(IList<string> parts)
        {
            _parts.Clear();
            _partLabels.Clear();
            bool any = parts != null && parts.Count > 0;
            if (any)
                foreach (var p in parts)
                {
                    var l = MenuKit.Body(p ?? "", BrandTokens.TypeMinInfo, RingTheme.TextDim);
                    l.style.whiteSpace = WhiteSpace.NoWrap;
                    l.style.flexShrink = 0f;
                    l.style.marginLeft = CareerTileLayout.EffectPartGap;
                    _parts.Add(l);
                    _partLabels.Add(l);
                }
            _parts.style.display = any ? DisplayStyle.Flex : DisplayStyle.None;
            _effect.style.display = any ? DisplayStyle.None : DisplayStyle.Flex;
            Sync();
        }

        /// <summary>Назва в один рядок: базовий кегль або менший — за шириною проби (див. <see cref="CareerTileLayout.FitTitle"/>).</summary>
        private void FitTitle()
        {
            float baseSize = _compact ? CareerTileLayout.CompactTitleSize : CareerTileLayout.TitleSize;
            float s = CareerTileLayout.FitTitle(baseSize, _titleProbe.layout.width, _title.layout.width);
            if (float.IsNaN(s) || Math.Abs(s - _titleSize) < 0.01f) return;
            SetTitleSize(s);
        }

        private void SetTitleSize(float s)
        {
            _titleSize = s;
            _title.style.fontSize = s;
            _title.style.letterSpacing = new Length(s * CareerTileLayout.TitleTrack);
        }

        private static bool Bottom(VisualElement e, ref float y)
        {
            var r = e.layout;
            if (float.IsNaN(r.height)) return false;
            if (r.yMax > y) y = r.yMax;
            return true;
        }

        /// <summary>
        /// Смуга шансів 2d6 (частки з 36): промах — сигнал, частково — папір, успіх — золото; під смугою — легенда тими ж
        /// кольорами в тому ж порядку (<paramref name="missText"/> «≤6 42%», <paramref name="partialText"/> «7–9 42%»,
        /// <paramref name="strongText"/> «10+ 17%»; ACC-02 — число поруч зі смугою, не лише колір).
        /// </summary>
        public void SetChances(int miss36, int partial36, int strong36, string missText, string partialText, string strongText)
        {
            _chances.Clear();
            int total = Math.Max(1, miss36 + partial36 + strong36);
            Add3(miss36, total, RingTheme.Signal);
            Add3(partial36, total, RingTheme.Paper);
            Add3(strong36, total, RingTheme.Gold);
            _chances.style.display = DisplayStyle.Flex;
            _legend[0].text = RingTheme.Caps(missText ?? "");
            _legend[1].text = RingTheme.Caps(partialText ?? "");
            _legend[2].text = RingTheme.Caps(strongText ?? "");
            _legendRow.style.display = DisplayStyle.Flex;
            Sync();
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
            foreach (var l in _partLabels) l.style.color = _effect.style.color;
            _keyBox.style.backgroundColor = !_enabled ? RingTheme.Disabled : darkText ? RingTheme.Bg : RingTheme.Gold;
            _key.style.color = darkText ? RingTheme.Gold : RingTheme.Bg;
            // легенда шансів кольорами сегментів; на світлій заливці (фокус, вибір) — темна, інакше золото зникало б на золоті
            _legend[0].style.color = darkText ? RingTheme.Bg : RingTheme.Signal;
            _legend[1].style.color = darkText ? RingTheme.Bg : RingTheme.Paper;
            _legend[2].style.color = darkText ? RingTheme.Bg : RingTheme.Gold;
            _check.style.display = _selected ? DisplayStyle.Flex : DisplayStyle.None;
            _check.Ink = darkText ? RingTheme.Bg : RingTheme.Text;
            _face.style.translate = new Translate(new Length(0f), new Length(_focus && _enabled && !MenuAnimator.Reduced ? -7f : 0f));
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
    public sealed class GoldPlate : VisualElement, IPadTarget
    {
        // 36 → не нижче 26 (У0, ПМ 09.11.2026); ті самі числа міряє охоронець CareerTextFitTests
        private const float WordSize = CareerTileLayout.WordSize, WordMin = CareerTileLayout.WordMin, WordTrack = CareerTileLayout.WordTrack;
        private const float WordLeft = CareerTileLayout.WordLeft, WordRight = CareerTileLayout.WordRight;
        private const float WordTop = CareerTileLayout.WordTop, WordBottom = CareerTileLayout.WordBottom;
        private readonly bool _enabled;
        private readonly ArenaPlate _plate;
        private readonly Label _word, _probe;
        private readonly Action _onClick;
        private readonly string _text;
        private float _size = WordSize, _fitW = -1f;

        public GoldPlate(string text, string keyHint, Action onClick, bool enabled = true)
        {
            _enabled = enabled;
            _onClick = onClick;
            _text = text ?? "";
            style.overflow = Overflow.Visible;
            _plate = new ArenaPlate { Fill = enabled ? RingTheme.Gold : RingTheme.PanelRaised, Mirror = true };
            Add(_plate);
            Color ink = enabled ? RingTheme.Bg : RingTheme.Disabled;
            _word = MenuKit.Display(text, WordSize, ink, RingFontRole.Display900, WordTrack);
            _word.style.whiteSpace = WhiteSpace.Normal;
            _word.style.position = Position.Absolute;
            _word.style.left = WordLeft; _word.style.right = WordRight; _word.style.top = WordTop;
            Add(_word);
            // Найдовше слово тим самим кеглем, невидиме: плита вужча за слово — кегль меншає, а не рветься посеред слова
            // («ВОРОЖНЕ/ЧА», «ТИТУЛЬНИ/Й» при 4:3 — кадри слоту 27, 09.11.2026); рядків більше, ніж уміщає висота, — теж.
            _probe = MenuKit.Display(LongestWord(text), WordSize, ink, RingFontRole.Display900, WordTrack);
            _probe.style.position = Position.Absolute;
            _probe.style.left = 0f; _probe.style.top = 0f;
            _probe.style.opacity = 0f;
            Add(_probe);
            RegisterCallback<GeometryChangedEvent>(e => FitWord());
            _word.RegisterCallback<GeometryChangedEvent>(e => FitWord());
            _probe.RegisterCallback<GeometryChangedEvent>(e => FitWord());
            var hint = MenuKit.Display(keyHint ?? "", BrandTokens.TypeLabel, enabled ? RingTheme.Bg : RingTheme.Disabled, RingFontRole.Display800, 0.12f);
            hint.style.position = Position.Absolute;
            hint.style.right = 18f; hint.style.bottom = 10f;
            Add(hint);
            RegisterCallback<PointerDownEvent>(e => { if (_enabled && e.button == 0 && onClick != null) onClick(); });
            RegisterCallback<PointerEnterEvent>(e => { if (_enabled && PadFocus.MouseMayHover) { _plate.Outline = RingTheme.Text; _plate.Redraw(); } });
            RegisterCallback<PointerLeaveEvent>(e => { if (PadFocus.MouseMayHover) { _plate.Outline = null; _plate.Redraw(); } });
        }

        // ---- ціль фокусу без миші: головна дія екрана (ранг 2), фокус — та сама обводка, що наведення ----
        bool IPadTarget.PadEnabled { get { return _enabled && _onClick != null; } }
        string IPadTarget.PadKey { get { return "gold:" + _text; } }
        int IPadTarget.PadRank { get { return 2; } }

        void IPadTarget.PadHot(bool on)
        {
            _plate.Outline = on ? (Color?)RingTheme.Text : null;
            _plate.Redraw();
        }

        void IPadTarget.PadActivate() { if (_enabled && _onClick != null) _onClick(); }
        bool IPadTarget.PadStep(int delta) { return false; }

        /// <summary>
        /// Кегль напису під плиту: ширина слова-проби ∝ кеглю, тож за один крок — пропорційно; зайвий рядок — мінус 2. Нова ширина
        /// плити (інша роздільність, вузька панель) — підбір заново від повного кегля. Не менше <see cref="WordMin"/>.
        /// </summary>
        private void FitWord()
        {
            float w = layout.width, h = layout.height, pw = _probe.layout.width, wh = _word.layout.height;
            if (float.IsNaN(w) || w <= 0f || float.IsNaN(pw) || pw <= 0f || float.IsNaN(wh)) return;
            if (Mathf.Abs(w - _fitW) > 1f)
            {
                _fitW = w;
                if (_size < WordSize) { SetWordSize(WordSize); return; }
            }
            float availW = w - WordLeft - WordRight, availH = h - WordTop - WordBottom;
            float s = _size;
            if (pw > availW + 0.5f) s = (float)Math.Floor(_size * availW / pw);
            else if (wh > availH + 0.5f) s = _size - 2f;
            s = Mathf.Max(WordMin, s);
            if (s < _size) SetWordSize(s);
        }

        private void SetWordSize(float s)
        {
            _size = s;
            foreach (var l in new[] { _word, _probe })
            {
                l.style.fontSize = s;
                l.style.letterSpacing = new Length(s * WordTrack);
            }
        }

        private static string LongestWord(string text)
        {
            string best = "";
            foreach (var part in (text ?? "").Split(' '))
                if (part.Length > best.Length) best = part;
            return best;
        }
    }

    /// <summary>Вкладка-акордеон шапки ряду («РОЗВИТОК 6», «ВИТРАТИ 2»): плита зі зрізом, число золотом; відкрита — обводка паперу.</summary>
    public sealed class TabPlate : VisualElement, IPadTarget
    {
        private readonly ArenaPlate _plate;
        private readonly Action _onClick;
        private readonly string _text;

        // ---- ціль фокусу без миші: фокус — заливка наведення кнопок (RingTheme.Hover); відкрита — як була, обводка ----
        bool IPadTarget.PadEnabled { get { return _onClick != null; } }
        string IPadTarget.PadKey { get { return "tab:" + _text; } }
        int IPadTarget.PadRank { get { return 4; } }
        void IPadTarget.PadHot(bool on) { _plate.Fill = on ? RingTheme.Hover : RingTheme.Panel; _plate.Redraw(); }
        void IPadTarget.PadActivate() { if (_onClick != null) _onClick(); }
        bool IPadTarget.PadStep(int delta) { return false; }

        public TabPlate(string text, int number, bool open, Action onClick, bool goldStripe)
        {
            _onClick = onClick;
            _text = text ?? "";
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.height = 40f;
            style.paddingLeft = 18f; style.paddingRight = 26f;
            style.marginLeft = 10f;
            style.overflow = Overflow.Visible;
            var plate = new ArenaPlate { Fill = RingTheme.Panel, Stripe = goldStripe ? (Color?)RingTheme.Gold : null, StripeW = 5f, Outline = open ? (Color?)RingTheme.Text : null, OutlineW = 2f };
            _plate = plate;
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
