using UnityEngine;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Рантайм-шкурка IMGUI для всієї сцени «Гра»: тепла темна палітра
    /// карпатського села, великі шрифти (тіло 20–24, заголовки 30+ — читно
    /// з відстані на 1280×720..2560×1440), вбудований runtime-шрифт Unity
    /// (рендерить кириличні літери без жодного завантаженого файлу).
    ///
    /// ЧОМУ ТУТ, А НЕ В <c>Editor/</c>. Обидва критики поправки №7 знайшли
    /// той самий розрив: якщо покласти генератор шкурки в <c>Editor/</c>,
    /// його забере <c>includePlatforms=["Editor"]</c> асмдефа й гравець
    /// побачить сіру дефолтну GUI без жодного попередження (R18). Файл —
    /// рантайм-скрипт <c>Game.Gameplay</c> навмисно.
    ///
    /// Текстури — суцільна заливка 2×2 (<see cref="Texture2D.SetPixels"/> +
    /// <see cref="Texture2D.Apply"/>), тому що стилю box/button/window
    /// потрібен лише колір фону, а не картинка: жодних завантажень, жодних
    /// файлів поза кодом.
    ///
    /// Побудований <see cref="GUISkin"/> покриває базові слоти (box/button/
    /// window/textField/scrollbars) з станами normal/hover/active. Заголовки
    /// й підзаголовки — окремі іменовані стилі (<see cref="Header"/>,
    /// <see cref="SubHeader"/>, <see cref="Body"/>, <see cref="Tooltip"/>),
    /// бо в <see cref="GUISkin"/> немає власного «header»-слоту — простіше й
    /// чесніше тримати їх статичними властивостями поруч, ніж вигадувати
    /// customStyles-довідник заради двох записів.
    /// </summary>
    public static class AlphaSkin
    {
        // ================= палітра =================
        // Тепла темна палітра карпатського села: дерево, вогонь у печі,
        // жодного холодного синього чи неонового акценту.
        public static readonly Color32 BgDark = new Color32(24, 19, 15, 255);
        public static readonly Color32 BgPanel = new Color32(40, 32, 25, 235);
        public static readonly Color32 BgRaised = new Color32(58, 45, 34, 255);
        public static readonly Color32 BgHover = new Color32(80, 60, 42, 255);
        public static readonly Color32 BgActive = new Color32(100, 72, 46, 255);
        public static readonly Color32 Accent = new Color32(198, 140, 64, 255);
        public static readonly Color32 AccentHover = new Color32(214, 158, 80, 255);
        public static readonly Color32 AccentActive = new Color32(168, 112, 48, 255);
        public static readonly Color32 Danger = new Color32(150, 46, 38, 255);
        public static readonly Color32 DangerHover = new Color32(178, 60, 48, 255);
        public static readonly Color32 DangerActive = new Color32(120, 34, 28, 255);
        public static readonly Color32 TextMain = new Color32(236, 224, 206, 255);
        public static readonly Color32 TextDim = new Color32(176, 158, 138, 255);
        public static readonly Color32 Border = new Color32(16, 12, 10, 255);

        public const int BodyFontSize = 22;
        public const int HeaderFontSize = 34;
        public const int SubHeaderFontSize = 26;

        private static GUISkin _skin;
        private static GUIStyle _header;
        private static GUIStyle _subHeader;
        private static GUIStyle _body;
        private static GUIStyle _tooltip;

        /// <summary>Побудований скін, з кешем — генерувати текстури щокадру нема сенсу.</summary>
        public static GUISkin Build()
        {
            if (_skin != null) return _skin;

            var skin = ScriptableObject.CreateInstance<GUISkin>();
            skin.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            skin.label = Body;
            skin.box = PanelStyle();
            skin.button = ButtonStyle(BgRaised, BgHover, BgActive, TextMain);
            skin.window = PanelStyle();
            skin.textField = FieldStyle();
            skin.horizontalScrollbar = TrackStyle();
            skin.horizontalScrollbarThumb = ThumbStyle();
            skin.verticalScrollbar = TrackStyle();
            skin.verticalScrollbarThumb = ThumbStyle();

            _skin = skin;
            return skin;
        }

        /// <summary>Великий заголовок екрана (30+, тут 34) — над панеллю, не всередині кнопки.</summary>
        public static GUIStyle Header
        {
            get
            {
                if (_header == null)
                {
                    _header = TextOnlyStyle(HeaderFontSize, TextMain);
                    _header.fontStyle = FontStyle.Bold;
                }
                return _header;
            }
        }

        /// <summary>Підзаголовок секції всередині панелі — трохи менший за Header.</summary>
        public static GUIStyle SubHeader
        {
            get
            {
                if (_subHeader == null)
                {
                    _subHeader = TextOnlyStyle(SubHeaderFontSize, Accent);
                    _subHeader.fontStyle = FontStyle.Bold;
                }
                return _subHeader;
            }
        }

        /// <summary>Тіло тексту (20–24, тут 22) — основний розмір читання на дистанції.</summary>
        public static GUIStyle Body
        {
            get
            {
                if (_body == null) _body = TextOnlyStyle(BodyFontSize, TextMain);
                return _body;
            }
        }

        /// <summary>Тьмяний дрібний підпис-підказка — менший за тіло, притишений колір.</summary>
        public static GUIStyle Tooltip
        {
            get
            {
                if (_tooltip == null)
                {
                    _tooltip = TextOnlyStyle(BodyFontSize - 4, TextDim);
                    _tooltip.fontStyle = FontStyle.Italic;
                }
                return _tooltip;
            }
        }

        /// <summary>Суцільна заливка 2×2 під фон стилю: усе, що потрібно box/button/window/textField.</summary>
        public static Texture2D SolidTexture(Color32 color)
        {
            var texture = new Texture2D(2, 2);
            Color c = color;
            texture.SetPixels(new[] { c, c, c, c });
            texture.Apply();
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        // ================= стилі =================

        private static GUIStyle TextOnlyStyle(int fontSize, Color32 textColor)
        {
            var style = new GUIStyle
            {
                fontSize = fontSize,
                wordWrap = true,
                richText = true,
                alignment = TextAnchor.UpperLeft
            };
            style.normal.textColor = textColor;
            style.padding = new RectOffset(2, 2, 2, 2);
            return style;
        }

        /// <summary>Панель/вікно: тільки фон і рамкові відступи — сама панель не клікабельна, станів немає.</summary>
        private static GUIStyle PanelStyle()
        {
            var style = new GUIStyle
            {
                fontSize = BodyFontSize,
                wordWrap = true,
                richText = true,
                padding = new RectOffset(18, 18, 16, 16),
                margin = new RectOffset(6, 6, 6, 6)
            };
            style.normal.background = SolidTexture(BgPanel);
            style.normal.textColor = TextMain;
            return style;
        }

        /// <summary>
        /// Клікабельний елемент з трьома станами (normal/hover/active) — кнопки
        /// й поля вводу просять однаково. Публічний: <c>Widgets</c> будує на
        /// цьому власні варіанти primary/secondary/danger (§ Widgets.cs), не
        /// дублюючи текстурну заливку.
        /// </summary>
        public static GUIStyle ButtonStyle(Color32 normalBg, Color32 hoverBg, Color32 activeBg, Color32 textColor)
        {
            var style = new GUIStyle
            {
                fontSize = BodyFontSize,
                wordWrap = false,
                richText = false,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(16, 16, 10, 10),
                margin = new RectOffset(4, 4, 4, 4)
            };
            style.normal.background = SolidTexture(normalBg);
            style.normal.textColor = textColor;
            style.hover.background = SolidTexture(hoverBg);
            style.hover.textColor = textColor;
            style.active.background = SolidTexture(activeBg);
            style.active.textColor = textColor;
            return style;
        }

        private static GUIStyle FieldStyle()
        {
            var style = ButtonStyle(BgDark, BgDark, BgDark, TextMain);
            style.alignment = TextAnchor.UpperLeft;
            style.wordWrap = false;
            return style;
        }

        private static GUIStyle TrackStyle()
        {
            var style = new GUIStyle { padding = new RectOffset(0, 0, 0, 0) };
            style.normal.background = SolidTexture(BgDark);
            return style;
        }

        private static GUIStyle ThumbStyle()
        {
            var style = new GUIStyle { padding = new RectOffset(0, 0, 0, 0) };
            style.normal.background = SolidTexture(AccentSoftTone());
            style.hover.background = SolidTexture(Accent);
            style.active.background = SolidTexture(AccentActive);
            return style;
        }

        private static Color32 AccentSoftTone()
        {
            return new Color32(140, 100, 58, 255);
        }
    }
}
