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
            Root.style.backgroundColor = Stage.RingStageHub.Has3D ? new Color(RingUi.Bg.r, RingUi.Bg.g, RingUi.Bg.b, 0.45f) : RingUi.Bg;
        }

        /// <summary>Перемалювати вміст за поточним станом моделі.</summary>
        public abstract void Refresh();

        /// <summary>Цифри 1–9 (варіанти, дії, нагороди).</summary>
        public virtual void OnDigit(int n) { }

        /// <summary>Enter або Пробіл — «Далі» / головна дія екрана.</summary>
        public virtual void OnConfirm() { }

        /// <summary>Клавіша L — повний журнал (матч).</summary>
        public virtual void OnToggleLog() { }

        /// <summary>Кожен кадр (для ходу суперника з паузою).</summary>
        public virtual void Tick(float unscaledDeltaTime) { }
    }

    /// <summary>
    /// Палітра й помічники стилю екранів «Рингу». Кольори — теплі, темні (сцена — арена за лаштунками); шрифт один —
    /// <see cref="HudToolkitTheme.LoadFont"/> (Fixel). Розміри — в одиницях еталона 1280×720: панель множить на масштаб.
    /// </summary>
    public static class RingUi
    {
        public static readonly Color Bg = new Color(0.055f, 0.05f, 0.065f, 1f);
        public static readonly Color Panel = new Color(0.10f, 0.09f, 0.115f, 1f);
        public static readonly Color PanelRaised = new Color(0.145f, 0.13f, 0.165f, 1f);
        public static readonly Color PanelHover = new Color(0.22f, 0.19f, 0.15f, 1f);
        public static readonly Color Border = new Color(0.30f, 0.27f, 0.22f, 1f);
        public static readonly Color Text = new Color(0.93f, 0.90f, 0.84f, 1f);
        public static readonly Color Dim = new Color(0.60f, 0.57f, 0.52f, 1f);
        public static readonly Color Gold = new Color(0.92f, 0.76f, 0.40f, 1f);
        public static readonly Color Good = new Color(0.45f, 0.78f, 0.45f, 1f);
        public static readonly Color Bad = new Color(0.88f, 0.33f, 0.30f, 1f);
        public static readonly Color Warn = new Color(0.95f, 0.70f, 0.25f, 1f);
        public static readonly Color Disabled = new Color(0.40f, 0.38f, 0.36f, 1f);
        public static readonly Color Face = new Color(0.40f, 0.72f, 0.95f, 1f);
        public static readonly Color Heel = new Color(0.90f, 0.35f, 0.33f, 1f);
        public static readonly Color Rebel = new Color(0.62f, 0.85f, 0.40f, 1f);
        public static readonly Color Overlay = new Color(0f, 0f, 0f, 0.72f);

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
                case RollTier.Strong: return Good;
                case RollTier.Partial: return Warn;
                default: return Bad;
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
            var font = HudToolkitTheme.LoadFont();
            if (font != null) e.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(font));
            e.style.color = Text;
        }

        public static VisualElement Card(float pad = 12f)
        {
            var e = Column();
            e.style.backgroundColor = Stage.RingStageHub.Has3D ? new Color(Panel.r, Panel.g, Panel.b, 0.86f) : Panel;
            HudToolkitTheme.Border(e, 1f, Border);
            e.style.borderTopLeftRadius = 6f; e.style.borderTopRightRadius = 6f;
            e.style.borderBottomLeftRadius = 6f; e.style.borderBottomRightRadius = 6f;
            HudToolkitTheme.Padding(e, pad, pad + 2f);
            return e;
        }

        /// <summary>Напівпрозорий фон панелі: з 3D-рингом крізь неї видно арену, а не чорний лист.</summary>
        public static void Glass(VisualElement e, float alpha = 0.80f)
        {
            if (!Stage.RingStageHub.Has3D) return;
            e.style.backgroundColor = new Color(Panel.r, Panel.g, Panel.b, alpha);
        }

        public static Label Lbl(string text, float size = 17f, Color? color = null, bool bold = false)
        {
            return HudToolkitTheme.Text(text, size, color ?? Text, bold);
        }

        /// <summary>Поля екрана: відступ від краю.</summary>
        public static void HudPad(VisualElement e) { HudToolkitTheme.Padding(e, 22f, 34f); }

        public static Label Title(string text) { return Lbl(text, 26f, Gold, true); }

        public static Label Heading(string text)
        {
            var l = Lbl(text, 18f, Gold, true);
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
            track.style.backgroundColor = new Color(0.04f, 0.04f, 0.05f, 1f);
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
            var b = new Button();
            b.text = text;
            b.style.fontSize = size;
            b.style.unityTextAlign = TextAnchor.MiddleLeft;
            b.style.whiteSpace = WhiteSpace.Normal;
            var font = HudToolkitTheme.LoadFont();
            if (font != null) b.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(font));
            HudToolkitTheme.Padding(b, 8f, 12f);
            b.style.marginLeft = 0f; b.style.marginRight = 0f; b.style.marginTop = 0f; b.style.marginBottom = 6f;
            b.style.borderTopLeftRadius = 4f; b.style.borderTopRightRadius = 4f;
            b.style.borderBottomLeftRadius = 4f; b.style.borderBottomRightRadius = 4f;
            HudToolkitTheme.Border(b, 1f, enabled ? Border : new Color(Border.r, Border.g, Border.b, 0.4f));
            b.style.borderLeftWidth = 4f;
            b.style.borderLeftColor = accent ?? (enabled ? Gold : Disabled);
            var back = enabled ? PanelRaised : Panel;
            b.style.backgroundColor = back;
            b.style.color = enabled ? Text : Disabled;
            if (enabled && onClick != null)
            {
                b.clicked += onClick;
                b.RegisterCallback<PointerEnterEvent>(e => b.style.backgroundColor = PanelHover);
                b.RegisterCallback<PointerLeaveEvent>(e => b.style.backgroundColor = back);
            }
            return b;
        }

        /// <summary>Велика кнопка головної дії («Далі», «Кинути»).</summary>
        public static Button PrimaryButton(string text, Action onClick, bool enabled = true)
        {
            var b = MakeButton(text, onClick, enabled, Gold, 20f);
            b.style.unityTextAlign = TextAnchor.MiddleCenter;
            b.style.marginTop = 8f;
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
            Glass(c, 0.84f);
            return c;
        }

        /// <summary>Заголовок розділу компактного вигляду.</summary>
        public static Label Section(string text)
        {
            var l = Lbl(text, 13f, Gold, true);
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
                b.RegisterCallback<PointerEnterEvent>(e => { hint.text = hintText; hint.style.color = Text; });
                b.RegisterCallback<PointerLeaveEvent>(e => { hint.text = ""; });
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
            shade.Add(panel);
            return shade;
        }
    }
}
