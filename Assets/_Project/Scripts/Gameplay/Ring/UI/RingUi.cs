using System;
using Game.Core.Ring;
using Game.Gameplay.Ring.Models;
using Game.Gameplay.UI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.Ring.UI
{
    /// <summary>Що екран знає про кампанію: сама кампанія, тексти, кубик і вихід «Заново».</summary>
    public sealed class RingUiContext
    {
        public RingCampaign Campaign;
        public RingText Text;
        public Action Restart;
        /// <summary>Кар'єра (null — зріз ворожнечі): матч тоді показує прийоми набору борця.</summary>
        public Game.Core.Ring.Career.CareerSession Career;
        /// <summary>Кінець матчу «Далі»: кар'єра підміняє Campaign.FinishMatch своїм підсумком.</summary>
        public Action FinishMatch;
        /// <summary>«Далі» на підготовці до матчу ворожнечі: кар'єра починає матч через StartFeudMatch (промінь надії, бонуси промо).</summary>
        public Action StartFeudMatch;
        /// <summary>«Далі» на розвитку/кінцівці ворожнечі: кар'єра закриває велике шоу (FinishBigShow) замість реваншу й «Заново».</summary>
        public Action AfterDevelopment;
        /// <summary>Хост кар'єри (сейв, меню, вигляд героя); null у зрізі ворожнечі.</summary>
        public ICareerHost Host;
        /// <summary>Кубик і кінець сцени для екрана діалогу, якщо це не промо ворожнечі (сцена закулісся кар'єри).</summary>
        public Game.Core.Randomness.IDiceRoller PromoDice;
        public Action FinishPromo;
    }

    /// <summary>Спільна основа екрана «Рингу»: корінь, перемальовування, гарячі клавіші, годинник кадру.</summary>
    public abstract class RingScreenView
    {
        protected readonly RingUiContext Ctx;
        public VisualElement Root { get; protected set; }

        protected RingScreenView(RingUiContext ctx)
        {
            Ctx = ctx;
            Root = RingUi.Column();
            Root.style.position = Position.Absolute;
            Root.style.left = 0f; Root.style.right = 0f; Root.style.top = 0f; Root.style.bottom = 0f;
            // З 3D — напівпрозоре тло: ринг і зала видно крізь екрани ворожнечі й розвитку (огляд 04.11.2026: суцільні й громіздкі).
            Root.style.backgroundColor = Stage.RingStageHub.Has3D ? RingTheme.WithAlpha(RingUi.Bg, BrandTokens.AlphaShade) : RingUi.Bg;
        }

        /// <summary>Перемалювати вміст за поточним станом моделі.</summary>
        public abstract void Refresh();

        /// <summary>Цифри 1–9 (варіанти, дії, нагороди).</summary>
        public virtual void OnDigit(int n) { }

        /// <summary>Enter або Пробіл — «Далі» / головна дія екрана.</summary>
        public virtual void OnConfirm() { }

        /// <summary>Клавіша L — повний журнал (матч).</summary>
        public virtual void OnToggleLog() { }

        /// <summary>
        /// Esc / «Назад»: екран закриває верхнє своє (шухляду, розгорнуту картку) і повертає true; false — закривати нічого,
        /// тоді хост відкриває паузу (ПМ 08.11.2026: «Esc спершу закриває верхнє … і лише тоді, коли закривати нічого, відкриває паузу»).
        /// </summary>
        public virtual bool OnBack() { return false; }

        /// <summary>Кожен кадр (для ходу суперника з паузою).</summary>
        public virtual void Tick(float unscaledDeltaTime) { }

        /// <summary>
        /// Екран приймає фокус без миші (хрестовина, стік, стрілки; A / Enter натискає ціль у фокусі; P3 геймпад, 11.11.2026).
        /// Вимкнено за замовчуванням: екран вмикає сам, коли всі його дії мають ціль фокусу (кнопки <see cref="RingUi"/>,
        /// плитки, плити й <see cref="PadMark"/>).
        /// </summary>
        public virtual bool PadEnabled { get { return false; } }

        private PadFocus _pad;
        private VisualElement _padRoot;

        /// <summary>Фокус без миші цього екрана (цілі збираються з <see cref="Root"/>).</summary>
        public PadFocus Pad
        {
            get
            {
                if (_pad == null || _padRoot != Root) { _pad = new PadFocus(Root); _padRoot = Root; }
                return _pad;
            }
        }
    }

    /// <summary>
    /// Палітра й помічники стилю екранів «Рингу». Кольори — теплі, темні (сцена — арена за лаштунками); шрифт один —
    /// <see cref="HudToolkitTheme.LoadFont"/> (Fixel). Розміри — в одиницях еталона 1280×720: панель множить на масштаб.
    /// </summary>
    public static class RingUi
    {
        // Фасад RingTheme (ТЗ B00 §1, Статут вигляду): імена ті самі, значення — з токенів арт-директора. Зеленого й бурштинового
        // більше немає: успіх і перевага — золото, шкода й провал — сигнал.
        public static readonly Color Bg = RingTheme.Bg;
        public static readonly Color Panel = RingTheme.Panel;
        public static readonly Color PanelRaised = RingTheme.PanelRaised;
        public static readonly Color PanelHover = RingTheme.Hover;
        public static readonly Color Border = RingTheme.Border;
        public static readonly Color Text = RingTheme.Text;
        public static readonly Color Dim = RingTheme.TextDim;
        public static readonly Color Gold = RingTheme.Gold;
        public static readonly Color Good = RingTheme.Gold;
        public static readonly Color Bad = RingTheme.Signal;
        public static readonly Color Warn = RingTheme.Gold;
        public static readonly Color Disabled = RingTheme.Disabled;
        public static readonly Color Face = RingTheme.HeroText;
        public static readonly Color Heel = RingTheme.VillainText;
        public static readonly Color Rebel = RingTheme.Rebel;
        public static readonly Color Overlay = RingTheme.WithAlpha(RingTheme.Scrim, BrandTokens.AlphaScrim);

        public static Color PathColor(RingPath p)
        {
            switch (p)
            {
                case RingPath.Face: return Face;
                case RingPath.Heel: return Heel;
                default: return Rebel;
            }
        }

        public static Color TierColor(RollTier t)
        {
            switch (t)
            {
                case RollTier.Strong: return RingTheme.Strong;
                case RollTier.Partial: return RingTheme.Partial;
                default: return RingTheme.Miss;
            }
        }

        // ---------------- будівельні блоки ----------------

        public static VisualElement Column()
        {
            var e = new VisualElement();
            e.style.flexDirection = FlexDirection.Column;
            ApplyFont(e);
            return e;
        }

        public static VisualElement Row()
        {
            var e = new VisualElement();
            e.style.flexDirection = FlexDirection.Row;
            return e;
        }

        private static void ApplyFont(VisualElement e)
        {
            RingFonts.Apply(e, RingFontRole.Text);   // шрифт — лише через роль (ТЗ B00 §2)
            e.style.color = Text;
        }

        /// <summary>
        /// Картка (мова «Арена», B08 §0.1 / B03): кутаста плита без заокруглень, тонка обводка, кармінна смужка ліворуч — як плити
        /// меню й хабу тижня (власник 08.11.2026: «давай дизайн більш схожий на реслінг»).
        /// </summary>
        public static VisualElement Card(float pad = 12f)
        {
            var e = Column();
            e.style.backgroundColor = Stage.RingStageHub.Has3D ? RingTheme.WithAlpha(Panel, BrandTokens.AlphaPanel) : Panel;
            HudToolkitTheme.Border(e, 1f, Border);
            e.style.borderLeftWidth = 4f;
            e.style.borderLeftColor = RingTheme.Crimson;
            HudToolkitTheme.Padding(e, pad, pad + 2f);
            return e;
        }

        /// <summary>Напівпрозорий фон панелі: з 3D-рингом крізь неї видно арену, а не чорний лист.</summary>
        public static void Glass(VisualElement e, float alpha = BrandTokens.AlphaGlass)
        {
            if (!Stage.RingStageHub.Has3D) return;
            e.style.backgroundColor = RingTheme.WithAlpha(Panel, alpha);
        }

        public static Label Lbl(string text, float size = 17f, Color? color = null, bool bold = false)
        {
            return HudToolkitTheme.Text(text, size, color ?? Text, bold);
        }

        /// <summary>Поля екрана: відступ від краю.</summary>
        public static void HudPad(VisualElement e) { HudToolkitTheme.Padding(e, 22f, 34f); }

        /// <summary>
        /// Статут TYPE-02: ВЕЛИКІ display — лише для ≤2 слів (мітки, імена). Довше — речення, sentence case у ролі text.
        /// Словом рахується відрізок із літерою чи цифрою («—», «·» — ні). Рев'ю 08.11.2026: підказка реакції матчу
        /// («Тайгер: «…». Як відповісти?») і підказка натиску стали довгими реченнями ВЕЛИКИМИ — важко читати.
        /// </summary>
        public static bool ShortCaption(string text) { return RingCaption.IsShort(text); }

        /// <summary>Заголовок за TYPE-02: коротка мітка — display ВЕЛИКИМИ, речення — Fixel жирним того ж кольору.</summary>
        private static Label CaptionOrSentence(string text, float displaySize, float textSize, RingFontRole role, float tracking)
        {
            Label l;
            if (ShortCaption(text)) l = Menus.MenuKit.Display(text, displaySize, Gold, role, tracking);
            else l = Menus.MenuKit.Body(text, textSize, Gold, true);
            l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }

        /// <summary>Заголовок екрана — display Black ВЕЛИКИМИ курсивом (як титр-плита меню); речення — Fixel жирним.</summary>
        public static Label Title(string text)
        {
            bool caps = ShortCaption(text);
            var l = CaptionOrSentence(text, 28f, 22f, RingFontRole.Display900, 0.02f);
            if (caps) l.style.unityFontStyleAndWeight = FontStyle.Italic;
            return l;
        }

        public static Label Heading(string text)
        {
            var l = CaptionOrSentence(text, 21f, 18f, RingFontRole.Display800, 0.04f);
            l.style.marginBottom = 6f;
            return l;
        }

        public static void Grow(VisualElement e, float factor)
        {
            e.style.flexGrow = factor;
            e.style.flexShrink = 1f;
        }

        public static void Gap(VisualElement e, float bottom) { e.style.marginBottom = bottom; }

        /// <summary>Смужка: фон + заповнення на частку 0…1.</summary>
        public static VisualElement Bar(float frac, Color fill, float height = 10f)
        {
            if (frac < 0f) frac = 0f;
            if (frac > 1f) frac = 1f;
            var track = new VisualElement();
            track.style.height = height;
            track.style.backgroundColor = RingTheme.Bg;
            track.style.flexDirection = FlexDirection.Row;
            HudToolkitTheme.Border(track, 1f, Border);
            var f = new VisualElement();
            f.style.width = new Length(frac * 100f, LengthUnit.Percent);
            f.style.backgroundColor = fill;
            track.Add(f);
            return track;
        }

        /// <summary>
        /// Кнопка-картка. Текст може бути багаторядковим. enabled=false — сіра, без дії (причина вже в тексті).
        /// accent — колір лівої смуги (шлях, ціна).
        /// </summary>
        public static Button MakeButton(string text, Action onClick, bool enabled = true, Color? accent = null, float size = 17f)
        {
            return Make(text, onClick, enabled, accent, size, enabled ? PanelRaised : Panel, PanelHover, enabled ? Text : Disabled);
        }

        /// <summary>Кнопка «Арени»: кутаста плита (без заокруглень), смужка-акцент ліворуч, наведення — світліша плита.</summary>
        private static Button Make(string text, Action onClick, bool enabled, Color? accent, float size, Color back, Color hover, Color textColor)
        {
            var b = new Button();
            b.text = text;
            b.style.fontSize = size;
            b.style.unityTextAlign = TextAnchor.MiddleLeft;
            b.style.whiteSpace = WhiteSpace.Normal;
            RingFonts.Apply(b, RingFontRole.Text);
            HudToolkitTheme.Padding(b, 8f, 12f);
            b.style.marginLeft = 0f; b.style.marginRight = 0f; b.style.marginTop = 0f; b.style.marginBottom = 6f;
            HudToolkitTheme.Border(b, 1f, enabled ? Border : RingTheme.WithAlpha(Border, 0.4f));
            b.style.borderLeftWidth = 4f;
            b.style.borderLeftColor = accent ?? (enabled ? Gold : Disabled);
            b.style.backgroundColor = back;
            b.style.color = textColor;
            bool live = enabled && onClick != null;
            if (live)
            {
                b.clicked += onClick;
                // поки фокус без миші (P3 геймпад), нерухомий курсор не перехоплює підсвіт
                b.RegisterCallback<PointerEnterEvent>(e => { if (PadFocus.MouseMayHover) b.style.backgroundColor = hover; });
                b.RegisterCallback<PointerLeaveEvent>(e => { if (PadFocus.MouseMayHover) b.style.backgroundColor = back; });
            }
            // ціль фокусу без миші: доступна — підсвіт, як наведення; недоступна — рамка кольору тексту (видно, де фокус; У0 11.11.2026)
            Color frame = enabled ? Border : RingTheme.WithAlpha(Border, 0.4f);
            PadMark.Mark(b, live ? onClick : null, "btn:" + text, 3, on =>
            {
                if (live) { b.style.backgroundColor = on ? hover : back; return; }
                Color c = on ? Text : frame;
                b.style.borderTopColor = c; b.style.borderRightColor = c; b.style.borderBottomColor = c;
            });
            return b;
        }

        /// <summary>
        /// Головна дія («Далі», «На ринг») — золота плита з написом display Black ВЕЛИКИМИ, як золота плита хабу (B03): золото —
        /// головна дія гравця. Недоступна — темна плита й сірий текст.
        /// </summary>
        public static Button PrimaryButton(string text, Action onClick, bool enabled = true)
        {
            // TYPE-02: «Далі», «На ринг» — display ВЕЛИКИМИ; довший напис («Далі — до наступного промо») — Fixel жирним
            bool caps = ShortCaption(text);
            var b = Make(caps ? RingTheme.Caps(text) : text, onClick, enabled, enabled ? RingTheme.Crimson : Disabled, caps ? 24f : 19f,
                enabled ? Gold : Panel, enabled ? RingTheme.Paper : Panel, enabled ? Bg : Disabled);
            RingFonts.Apply(b, caps ? RingFontRole.Display900 : RingFontRole.TextBold);
            b.style.unityTextAlign = TextAnchor.MiddleCenter;
            b.style.borderLeftWidth = 8f;
            b.style.marginTop = 8f;
            var pad = PadMark.Of(b);
            if (pad != null) pad.Rank = 2;      // головна дія — перша ціль фокусу
            return b;
        }

        /// <summary>Скрол-обгортка для довгих списків.</summary>
        public static ScrollView Scroll()
        {
            var s = new ScrollView(ScrollViewMode.Vertical);
            s.style.flexGrow = 1f;
            return s;
        }

        // ---------------- компактний вигляд кар'єри (власник 04.11.2026: «зроби значно меньше та зручніше») ----------------

        /// <summary>Вузька колонка праворуч, решта екрана — 3D (тиждень, кінець сезону, ворожнеча, розвиток у кар'єрі).</summary>
        public static VisualElement SideColumn(VisualElement root)
        {
            root.style.flexDirection = FlexDirection.Row;
            root.style.justifyContent = Justify.FlexEnd;
            root.style.backgroundColor = Color.clear;
            var col = Column();
            col.style.width = new Length(36f, LengthUnit.Percent);
            col.style.minWidth = 360f;
            col.style.maxWidth = 470f;
            root.Add(col);
            return col;
        }

        /// <summary>Скляна картка компактного вигляду.</summary>
        public static VisualElement GlassCard(float pad = 8f)
        {
            var c = Card(pad);
            Glass(c, BrandTokens.AlphaGlass);
            return c;
        }

        /// <summary>Заголовок розділу компактного вигляду.</summary>
        public static Label Section(string text)
        {
            var l = CaptionOrSentence(text, BrandTokens.TypeLabel, BrandTokens.TypeLabel, RingFontRole.Display800, 0.1f);
            l.style.marginTop = 6f;
            l.style.marginBottom = 3f;
            return l;
        }

        /// <summary>Рядок-підказка: опис дії при наведенні на однорядкову кнопку.</summary>
        public static Label HintLine()
        {
            var h = Lbl("", 12f, Dim);
            h.style.whiteSpace = WhiteSpace.Normal;
            h.style.minHeight = 30f;
            h.style.marginTop = 4f;
            return h;
        }

        /// <summary>Однорядкова кнопка; <paramref name="hintText"/> — у рядок-підказку <paramref name="hint"/> при наведенні.</summary>
        public static Button SmallButton(string text, Action onClick, bool enabled, Color? accent, Label hint = null, string hintText = null)
        {
            var b = MakeButton(text, onClick, enabled, accent, 13f);
            b.style.paddingTop = 3f; b.style.paddingBottom = 3f; b.style.paddingLeft = 8f; b.style.paddingRight = 8f;
            b.style.marginBottom = 3f;
            b.style.marginRight = 4f;
            b.style.borderLeftWidth = 3f;
            if (hint != null && !string.IsNullOrEmpty(hintText))
            {
                b.RegisterCallback<PointerEnterEvent>(e => { if (PadFocus.MouseMayHover) { hint.text = hintText; hint.style.color = Text; } });
                b.RegisterCallback<PointerLeaveEvent>(e => { if (PadFocus.MouseMayHover) hint.text = ""; });
                PadMark.AddHot(b, on => { hint.text = on ? hintText : ""; if (on) hint.style.color = Text; });
            }
            return b;
        }

        /// <summary>Шкала одним рядком: назва, значення й тонка смужка.</summary>
        public static VisualElement MeterRow(string name, string value, float frac, Color fill)
        {
            var col = Column();
            var row = Row();
            row.style.justifyContent = Justify.SpaceBetween;
            row.Add(Lbl(name, 12f));
            row.Add(Lbl(value, 12f, Gold, true));
            col.Add(row);
            var bar = Bar(frac, fill, 4f);
            bar.style.marginBottom = 4f;
            col.Add(bar);
            return col;
        }

        /// <summary>Півпрозорий шар на весь екран із центрованою панеллю (модальні реакція й докрутка).</summary>
        public static VisualElement Modal(out VisualElement panel)
        {
            var shade = Column();
            shade.style.position = Position.Absolute;
            shade.style.left = 0f; shade.style.right = 0f; shade.style.top = 0f; shade.style.bottom = 0f;
            shade.style.backgroundColor = Overlay;
            shade.style.justifyContent = Justify.Center;
            shade.style.alignItems = Align.Center;
            panel = Card(18f);
            panel.style.width = new Length(52f, LengthUnit.Percent);
            panel.style.minWidth = 420f;
            panel.style.maxWidth = 760f;
            panel.style.backgroundColor = PanelRaised;
            HudToolkitTheme.Border(panel, 2f, Gold);
            panel.style.borderLeftWidth = 8f;
            panel.style.borderLeftColor = RingTheme.Crimson;
            shade.Add(panel);
            return shade;
        }
    }
}
