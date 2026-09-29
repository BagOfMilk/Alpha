using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.UI.Toolkit
{
    /// <summary>
    /// Тема шапки і стрічки на UI Toolkit (спайк H4, docs/HUD_DESIGN.md §6).
    /// Кольори — ті самі токени <see cref="AlphaSkin"/>, що в IMGUI і в бою:
    /// одна семантична таблиця на всю гру (§6.1, Статут UI-01). Стилі
    /// задаються кодом, а не окремим .uss: палітра живе в одному місці
    /// (AlphaSkin), і лінт перевіряє кожне звернення до стилю.
    /// </summary>
    public static class HudToolkitTheme
    {
        /// <summary>
        /// Тема за замовчуванням Unity (<c>Resources/AlphaHudTheme.tss</c> —
        /// лише <c>@import url("unity-theme://default")</c>): без неї ScrollView
        /// лишається без смуги прокрутки. Текст від неї не залежить — шрифт
        /// задається явно (<see cref="Font"/>).
        /// </summary>
        public const string ThemeResourcePath = "AlphaHudTheme";

        /// <summary>Асет PanelSettings, який створює <c>Editor/HudPanelAssets</c> під час збирання сцени.</summary>
        public const string PanelResourcePath = "AlphaHudPanel";

        /// <summary>
        /// ЄДИНА точка заміни шрифту. Fixel/Noto ще не завантажені (HUD_DESIGN
        /// §12 (б)); коли з'являться — підміна одним рядком тут, наприклад
        /// <c>Resources.Load&lt;Font&gt;("Fonts/FixelText-Regular")</c>.
        /// </summary>
        public static Font LoadFont() => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Розміри — в одиницях еталона 1280×720 (панель множить на масштаб
        // HudLayout.ScaleFor): 18 на 1280 = 22.5 на 1600, як тіло IMGUI (22).
        // Мінімум 18 для будь-якої інформації (§6.2).
        public const float BodySize = 18f;
        public const float TitleSize = 20f;
        public const float ZoneTitleSize = 18f;

        public static Color PanelColor => AlphaSkin.BgPanel;
        public static Color RaisedColor => AlphaSkin.BgRaised;
        public static Color HoverColor => AlphaSkin.BgHover;
        public static Color BorderColor => AlphaSkin.Border;
        public static Color TextColor => AlphaSkin.TextMain;
        public static Color TextDimColor => AlphaSkin.TextDim;
        public static Color AccentColor => AlphaSkin.Accent;
        /// <summary>«Погано, увага» — бурштиново-жовтий, той самий, що пастка в бою (§6.1).</summary>
        public static Color WarningColor => AlphaSkin.BattleTrap;
        /// <summary>«Загроза, незворотне» — червоний ворога (§6.1).</summary>
        public static Color DangerColor => AlphaSkin.BattleEnemySide;

        /// <summary>Колір рамки рядка стрічки за серйозністю (не за рівнем Напруги — P-HUD-02).</summary>
        public static Color SeverityColor(FeedSeverity severity)
        {
            switch (severity)
            {
                case FeedSeverity.Danger: return DangerColor;
                case FeedSeverity.Warning: return WarningColor;
                case FeedSeverity.Mandatory: return AccentColor;
                default: return TextDimColor;
            }
        }

        /// <summary>Форма поруч із кольором — для дальтоніків (§6.1). Символи з WGL4: є у вбудованому шрифті.</summary>
        public static string SeverityMark(FeedSeverity severity)
        {
            switch (severity)
            {
                case FeedSeverity.Danger: return "▲";
                case FeedSeverity.Warning: return "◊";
                case FeedSeverity.Mandatory: return "◊";
                default: return "·";
            }
        }

        // ---------------- дрібні помічники стилю ----------------

        public static void Padding(VisualElement e, float v) => Padding(e, v, v);

        public static void Padding(VisualElement e, float vertical, float horizontal)
        {
            e.style.paddingTop = vertical;
            e.style.paddingBottom = vertical;
            e.style.paddingLeft = horizontal;
            e.style.paddingRight = horizontal;
        }

        public static void Border(VisualElement e, float width, Color color)
        {
            e.style.borderTopWidth = width;
            e.style.borderBottomWidth = width;
            e.style.borderLeftWidth = width;
            e.style.borderRightWidth = width;
            e.style.borderTopColor = color;
            e.style.borderBottomColor = color;
            e.style.borderLeftColor = color;
            e.style.borderRightColor = color;
        }

        public static Label Text(string text, float size, Color color, bool bold = false)
        {
            var label = new Label(text);
            label.style.fontSize = size;
            label.style.color = color;
            label.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginLeft = 0f;
            label.style.marginRight = 0f;
            label.style.marginTop = 0f;
            label.style.marginBottom = 0f;
            label.pickingMode = PickingMode.Ignore;
            return label;
        }
    }
}
