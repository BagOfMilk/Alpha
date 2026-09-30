using UnityEngine;
// BattleLogKind живе в Game.Gameplay (батьківський неймспейс тут НЕ
// підключається неявно) — потрібен для BattleLogColor нижче, спільної для
// HUD (BattleHudScreen) і фолбеку (BattleScreen), щоб журнал бою фарбувався
// однаково на обох екранах (Бій v2, аудит HUD п.«уніфікований стиль журналу»).
using Game.Gameplay;

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
        // ================= палітра (UI v2, орієнтир BG3 — власник 30.09.2026) =================
        // Майже чорне тепле тло, пергаментний текст, тонкі бронзові рамки.
        // Бронза — НЕ яскраве золото: золото 255,204,77 у бою вже означає
        // «поточний юніт / крит», бурштин — пастку й попередження (Статут
        // UI-01 «один колір — один сенс»). Бронза в рамках — лише оздоба,
        // сенсу не несе. Контраст тексту на панелі: TextMain ≈13:1,
        // TextDim ≈8:1, TextMuted ≈5.7:1 (WCAG AA ≥ 4.5:1).
        public static readonly Color32 BgDark = new Color32(15, 12, 9, 255);
        public static readonly Color32 BgPanel = new Color32(23, 18, 14, 240);
        public static readonly Color32 BgRaised = new Color32(37, 29, 22, 255);
        public static readonly Color32 BgHover = new Color32(50, 39, 28, 255);
        public static readonly Color32 BgActive = new Color32(18, 14, 11, 255);
        /// <summary>Головна дія / обране: бронзово-золота заливка (не бойове золото).</summary>
        public static readonly Color32 Accent = new Color32(201, 164, 92, 255);
        public static readonly Color32 AccentHover = new Color32(221, 187, 116, 255);
        public static readonly Color32 AccentActive = new Color32(158, 127, 69, 255);
        /// <summary>Червоно-чорна пара, як у вишивці: заливка ризикової дії.</summary>
        public static readonly Color32 Danger = new Color32(139, 42, 30, 255);
        public static readonly Color32 DangerHover = new Color32(168, 54, 42, 255);
        public static readonly Color32 DangerActive = new Color32(110, 32, 22, 255);
        /// <summary>Червоний для ТЕКСТУ на темному тлі (заливка <see cref="Danger"/> як текст нечитна).</summary>
        public static readonly Color32 DangerTextColor = new Color32(222, 110, 90, 255);
        /// <summary>Пергамент — основний текст.</summary>
        public static readonly Color32 TextMain = new Color32(230, 219, 194, 255);
        /// <summary>Другорядний текст — підказки, підписи.</summary>
        public static readonly Color32 TextDim = new Color32(203, 172, 149, 255);
        /// <summary>Приглушений — вимкнене, «обрано раніше»; не нижче 4.5:1.</summary>
        public static readonly Color32 TextMuted = new Color32(175, 150, 120, 255);
        /// <summary>Рамки UI Toolkit (шапка/стрічка) — та сама темна бронза, що й у IMGUI.</summary>
        public static readonly Color32 Border = new Color32(90, 70, 48, 255);

        // Бронза рамок і оздоб (без сенсу — лише обрис).
        public static readonly Color32 Bronze = new Color32(168, 139, 90, 255);
        public static readonly Color32 BronzeHi = new Color32(212, 183, 122, 255);
        public static readonly Color32 BronzeLo = new Color32(90, 70, 48, 255);
        public static readonly Color32 LineInner = new Color32(58, 46, 32, 255);
        public static readonly Color32 Outline = new Color32(7, 5, 4, 255);
        /// <summary>Термін словника в тексті (разом із підкресленням — не лише колір).</summary>
        public static readonly Color32 Term = new Color32(217, 164, 65, 255);
        /// <summary>Затемнення під модалкою.</summary>
        public static readonly Color32 ModalDim = new Color32(0, 0, 0, 170);

        // ================= палітра бою (Бій v2, docs/COMBAT_V2.md §2) =================
        // Один сталий сенс на весь екран бою: колір ніколи не переозначається
        // для іншої мети в тому самому кадрі (Статут UI-01). Значення —
        // точний переклад таблиці §2 (RGB 0..1) у байти.
        public static readonly Color32 BattlePlayerSide = new Color32(77, 153, 255, 255);
        public static readonly Color32 BattleEnemySide = new Color32(235, 71, 56, 255);
        public static readonly Color32 BattleDefectorSide = new Color32(179, 102, 242, 255);
        public static readonly Color32 BattleCurrentUnit = new Color32(255, 204, 77, 255);
        /// <summary>Текст на золотому тлі поточного юніта (§2 «поточний — золоте тло + темний текст»): світлий TextMain на BattleCurrentUnit нечитний (аудит знімків, п.2 «жовтий на жовтому»).</summary>
        public static readonly Color32 BattleCurrentUnitText = new Color32(43, 33, 24, 255);
        /// <summary>
        /// Приглушені тони сторони — тло бейджа НЕ-поточного юніта в стрічці
        /// ініціативи (Бій v2, раунд 2, аудит знімків п.2): насичені
        /// <see cref="BattlePlayerSide"/>/<see cref="BattleEnemySide"/> поруч
        /// зі світлим текстом читались гірше, ніж приглушений тон + білий
        /// текст — і залишають насичені кольори унікальними для арени
        /// (кільця, укриття), не плутаючи їх з рядовим бейджем черги ходу.
        /// </summary>
        public static readonly Color32 BattlePlayerSideMuted = new Color32(38, 64, 104, 255);
        public static readonly Color32 BattleEnemySideMuted = new Color32(102, 46, 40, 255);
        public static readonly Color32 BattleDefectorSideMuted = new Color32(78, 52, 100, 255);
        public static readonly Color32 BattleMiss = new Color32(179, 179, 179, 255);
        public static readonly Color32 BattleGraze = new Color32(230, 217, 191, 255);
        public static readonly Color32 BattleHit = new Color32(255, 255, 255, 255);
        public static readonly Color32 BattleCrit = new Color32(255, 204, 77, 255);
        public static readonly Color32 BattleHeal = new Color32(115, 230, 115, 255);
        public static readonly Color32 BattleStatus = new Color32(191, 153, 255, 255);
        public static readonly Color32 BattleOverwatch = new Color32(89, 217, 242, 255);
        /// <summary>Своя пастка на арені: мітка і підсвітка клітинки одного бурштину.</summary>
        public static readonly Color32 BattleTrap = new Color32(250, 158, 20, 235);
        /// <summary>Дальність озброєної здібності (§2 «бузковий») — заливка тайла, HUD тут не малює, лишень тримає токен поруч з рештою бойової палітри.</summary>
        public static readonly Color32 BattleAbilityRange = new Color32(166, 128, 242, 255);

        // UI v2: трохи легший набір, але не нижче правила проєкту (тіло 20–22,
        // мінімум 18 на 1280×720 — HUD_DESIGN §6.2). Заголовки — антиква.
        public const int BodyFontSize = 21;
        public const int HeaderFontSize = 30;
        public const int SubHeaderFontSize = 24;
        public const int CaptionFontSize = 18;
        /// <summary>Ім'я юніта над головою на арені (§3: «15 px, на темній підкладці»).</summary>
        public const int OverlayNameFontSize = 15;

        private static GUISkin _skin;
        private static GUIStyle _header;
        private static GUIStyle _subHeader;
        private static GUIStyle _body;
        private static GUIStyle _tooltip;
        private static GUIStyle _hintLine;
        private static GUIStyle _dangerText;
        private static GUIStyle _critText;
        private static GUIStyle _overlayName;
        private static GUIStyle _windowTitle;
        private static GUIStyle _tooltipPanel;
        private static GUIStyle _inset;
        private static GUIStyle _tabOn;
        private static GUIStyle _tabOff;
        private static GUIStyle _option;
        private static GUIStyle _termText;
        private static Texture2D _dividerLine;
        private static Texture2D _dividerDiamond;

        /// <summary>Побудований скін, з кешем — генерувати текстури щокадру нема сенсу.</summary>
        public static GUISkin Build()
        {
            if (_skin != null) return _skin;

            var skin = ScriptableObject.CreateInstance<GUISkin>();
            // Поправка №12.2: Fixel Text для всього інтерфейсу; без асета HudArt —
            // вбудований шрифт. Знаків, яких немає у Fixel (▸ ▾ ✓), динамічний
            // шрифт бере з системних (Editor/ThirdPartyUiImportSettings).
            var art = HudArt.Current;
            skin.font = art != null && art.UiRegular != null
                ? art.UiRegular
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            skin.label = Body;
            skin.box = PanelStyle();
            skin.button = ButtonStyle(BgRaised, BgHover, BgActive, TextMain);
            skin.window = PanelStyle();
            skin.textField = FieldStyle();
            skin.textArea = FieldStyle();
            skin.horizontalScrollbar = TrackStyle();
            skin.horizontalScrollbarThumb = ThumbStyle();
            skin.verticalScrollbar = TrackStyle();
            skin.verticalScrollbarThumb = ThumbStyle();

            _skin = skin;
            return skin;
        }

        // ================= шрифти (UI v2) =================

        /// <summary>
        /// Антиква заголовків: Noto Serif Bold (<see cref="HudArt.HeadingSerif"/>),
        /// до перезбирання сцени — Noto Serif Regular (<see cref="HudArt.Paper"/>)
        /// із синтетичним жирним; без асета — шрифт скіну.
        /// </summary>
        public static Font HeadingFont
        {
            get
            {
                var art = HudArt.Current;
                if (art == null) return null;
                return art.HeadingSerif != null ? art.HeadingSerif : art.Paper;
            }
        }

        /// <summary>Чи треба домальовувати жирність (є лише Regular-файл антикви).</summary>
        private static bool HeadingNeedsSyntheticBold
        {
            get
            {
                var art = HudArt.Current;
                return art == null || art.HeadingSerif == null;
            }
        }

        /// <summary>Напівжирний Fixel для виділеного рядка (замість синтетичного жирного); null — шрифт скіну.</summary>
        public static Font StrongFont => ButtonFont;

        /// <summary>Напівжирний Fixel для кнопок і вкладок; null — шрифт скіну.</summary>
        private static Font ButtonFont
        {
            get
            {
                var art = HudArt.Current;
                return art != null ? art.UiSemiBold : null;
            }
        }

        private static GUIStyle HeadingStyle(int size, Color32 color)
        {
            var style = TextOnlyStyle(size, color);
            style.font = HeadingFont;
            style.fontStyle = HeadingNeedsSyntheticBold ? FontStyle.Bold : FontStyle.Normal;
            return style;
        }

        /// <summary>Великий заголовок екрана (антиква 30) — над панеллю, не всередині кнопки.</summary>
        public static GUIStyle Header
        {
            get
            {
                if (_header == null) _header = HeadingStyle(HeaderFontSize, TextMain);
                return _header;
            }
        }

        /// <summary>Назва вікна — та сама антиква, по центру (над роздільником-ромбом).</summary>
        public static GUIStyle WindowTitle
        {
            get
            {
                if (_windowTitle == null)
                {
                    _windowTitle = HeadingStyle(HeaderFontSize, TextMain);
                    _windowTitle.alignment = TextAnchor.MiddleCenter;
                }
                return _windowTitle;
            }
        }

        /// <summary>Підзаголовок секції всередині панелі — антиква 24, світла бронза.</summary>
        public static GUIStyle SubHeader
        {
            get
            {
                if (_subHeader == null) _subHeader = HeadingStyle(SubHeaderFontSize, BronzeHi);
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

        /// <summary>
        /// Дрібний підпис-підказка — менший за тіло, другорядний колір. UI v2:
        /// без курсиву — курсив лишається лише реплікам і цитатам (UI-10), а
        /// синтетичний нахил кирилиці ламає форми д/т/п/г.
        /// </summary>
        public static GUIStyle Tooltip
        {
            get
            {
                if (_tooltip == null) _tooltip = TextOnlyStyle(CaptionFontSize, TextDim);
                return _tooltip;
            }
        }

        /// <summary>
        /// Бій v2, раунд 2 (аудит знімків, п.4/п.5: «доданки звичайним світлим
        /// текстом ≥17px без курсиву», «Правило влучання... не курсивом,
        /// читабельно»): те саме місце, що <see cref="Tooltip"/>, але БЕЗ
        /// курсиву й світлим (<see cref="TextMain"/>, не притишеним
        /// <see cref="TextDim"/>) кольором — для важливої інформації, яку
        /// гравець зважує ДО кліку, а не для другорядної підказки-опису.
        /// </summary>
        public static GUIStyle HintLine
        {
            get
            {
                if (_hintLine == null) _hintLine = TextOnlyStyle(CaptionFontSize, TextMain);
                return _hintLine;
            }
        }

        /// <summary>Червоний текст без фону — відмова дії/причина недоступності (Поправка №1, «шлях завжди видно»), поруч зі звичайним <see cref="Tooltip"/>.</summary>
        public static GUIStyle DangerText
        {
            get
            {
                if (_dangerText == null) _dangerText = TextOnlyStyle(BodyFontSize, DangerTextColor);
                return _dangerText;
            }
        }

        /// <summary>Крит — жирний і більший (§2: «золотий, жирний, більший»), той самий колір, що <see cref="BattleCrit"/>.</summary>
        public static GUIStyle CritText
        {
            get
            {
                if (_critText == null)
                {
                    _critText = TextOnlyStyle(BodyFontSize + 6, BattleCrit);
                    _critText.fontStyle = FontStyle.Bold;
                    _critText.alignment = TextAnchor.MiddleCenter;
                }
                return _critText;
            }
        }

        /// <summary>Ім'я юніта над головою на арені (§3): 15px, центровано, колір підставляє викликач (сторона юніта).</summary>
        public static GUIStyle OverlayName
        {
            get
            {
                if (_overlayName == null)
                {
                    _overlayName = TextOnlyStyle(OverlayNameFontSize, TextMain);
                    _overlayName.alignment = TextAnchor.MiddleCenter;
                    _overlayName.wordWrap = false;
                }
                return _overlayName;
            }
        }

        /// <summary>
        /// Один колір на весь сенс рядка журналу/спливаючого напису (§2),
        /// спільний для HUD (<c>BattleHudScreen.DrawLogPanel</c>) і фолбеку
        /// (<c>BattleScreen.DrawLog</c>) — журнал не має читатись по-різному
        /// залежно від того, який презентер зараз активний.
        /// </summary>
        public static Color32 BattleLogColor(BattleLogKind kind)
        {
            switch (kind)
            {
                case BattleLogKind.Miss: return BattleMiss;
                case BattleLogKind.Graze: return BattleGraze;
                case BattleLogKind.Hit: return BattleHit;
                case BattleLogKind.Crit: return BattleCrit;
                case BattleLogKind.Heal: return BattleHeal;
                case BattleLogKind.Status: return BattleStatus;
                case BattleLogKind.Overwatch: return BattleOverwatch;
                case BattleLogKind.Ability: return AccentHover;
                case BattleLogKind.Downed:
                case BattleLogKind.Death: return BattleEnemySide;
                case BattleLogKind.Victory: return BattleHeal;
                case BattleLogKind.Defeat: return BattleEnemySide;
                case BattleLogKind.Rejection: return DangerTextColor;
                case BattleLogKind.Round: return TextDim;
                default: return TextMain;
            }
        }

        /// <summary>Суцільна заливка 2×2 — підкладки й тонування через GUI.color (без рамки).</summary>
        public static Texture2D SolidTexture(Color32 color)
        {
            return SkinTextures.Solid(color);
        }

        // ================= UI v2: рамки BG3 =================

        /// <summary>
        /// Панель-підказка (наведення на термін, ресурс, здібність): темніша
        /// за звичайну панель, тонка бронзова рамка, без кутових шпильок.
        /// </summary>
        public static GUIStyle TooltipPanel
        {
            get
            {
                if (_tooltipPanel == null)
                {
                    var frame = BaseFrame(32, 4, new Color32(14, 11, 8, 250), new Color32(10, 8, 6, 250));
                    _tooltipPanel = FramedStyle(frame, new RectOffset(16, 16, 12, 12));
                    _tooltipPanel.normal.textColor = TextMain;
                    _tooltipPanel.fontSize = CaptionFontSize;
                    _tooltipPanel.wordWrap = true;
                    _tooltipPanel.richText = true;
                }
                return _tooltipPanel;
            }
        }

        /// <summary>Вставка: поле, список, картка всередині панелі — на щабель темніша, внутрішня тінь.</summary>
        public static GUIStyle Inset
        {
            get
            {
                if (_inset == null)
                {
                    var frame = BaseFrame(24, 2, new Color32(10, 8, 6, 255), new Color32(16, 13, 10, 255));
                    frame.Line = LineInner;
                    frame.Inner = new Color32(0, 0, 0, 120);
                    _inset = FramedStyle(frame, new RectOffset(14, 14, 10, 10));
                    _inset.normal.textColor = TextMain;
                    _inset.fontSize = BodyFontSize;
                    _inset.wordWrap = true;
                    _inset.richText = true;
                }
                return _inset;
            }
        }

        /// <summary>
        /// Вкладка: обрана зливається з панеллю й має світлу бронзову рамку,
        /// необрана — на щабель темніша з тьмяною рамкою та другорядним текстом.
        /// </summary>
        public static GUIStyle TabStyle(bool selected)
        {
            if (selected)
            {
                if (_tabOn == null)
                {
                    var frame = BaseFrame(32, 4, new Color32(62, 48, 33, 255), new Color32(40, 31, 22, 255));
                    frame.Line = BronzeHi;
                    frame.Glow = new Color32(212, 183, 122, 70);
                    frame.GlowWidth = 3;
                    _tabOn = ClickableStyle(frame, frame, frame, TextMain, TextMain);
                }
                return _tabOn;
            }

            if (_tabOff == null)
            {
                var normal = BaseFrame(32, 4, new Color32(24, 19, 14, 255), new Color32(17, 13, 10, 255));
                normal.Line = BronzeLo;
                var hover = normal;
                hover.Line = Bronze;
                hover.Glow = new Color32(212, 183, 122, 30);
                hover.GlowWidth = 3;
                var active = normal;
                active.FillTop = BgActive;
                active.FillBottom = BgActive;
                _tabOff = ClickableStyle(normal, hover, active, TextDim, TextMain);
            }
            return _tabOff;
        }

        /// <summary>
        /// Рядок варіанта в діалозі/рішенні (як у BG3 і Rogue Trader): без
        /// рамки в спокої, при наведенні — тепла підкладка й бронзовий край.
        /// Текст ліворуч, переноситься.
        /// </summary>
        public static GUIStyle OptionRow
        {
            get
            {
                if (_option == null)
                {
                    var normal = BaseFrame(32, 3, new Color32(20, 16, 12, 200), new Color32(17, 13, 10, 200));
                    normal.Line = new Color32(58, 46, 32, 255);
                    normal.Outer = new Color32(0, 0, 0, 0);
                    var hover = BaseFrame(32, 3, new Color32(48, 37, 26, 235), new Color32(36, 28, 20, 235));
                    hover.Line = BronzeHi;
                    hover.Glow = new Color32(212, 183, 122, 50);
                    hover.GlowWidth = 4;
                    var active = hover;
                    active.FillTop = new Color32(30, 23, 17, 240);
                    active.FillBottom = active.FillTop;
                    _option = ClickableStyle(normal, hover, active, TextMain, BronzeHi);
                    _option.alignment = TextAnchor.MiddleLeft;
                    _option.wordWrap = true;
                    _option.padding = new RectOffset(14, 14, 10, 10);
                    _option.font = null; // тіло репліки — звичайний Fixel, не напівжирний
                }
                return _option;
            }
        }

        /// <summary>Текст із живими термінами словника: тіло, rich text увімкнено.</summary>
        public static GUIStyle TermText
        {
            get
            {
                if (_termText == null) _termText = TextOnlyStyle(BodyFontSize, TextMain);
                return _termText;
            }
        }

        /// <summary>Лінія роздільника: бронза, що згасає до країв.</summary>
        public static Texture2D DividerLine
        {
            get
            {
                if (_dividerLine == null) _dividerLine = SkinTextures.FadeLine(Bronze);
                return _dividerLine;
            }
        }

        /// <summary>Ромб-«ружа» посередині роздільника.</summary>
        public static Texture2D DividerDiamond
        {
            get
            {
                if (_dividerDiamond == null) _dividerDiamond = SkinTextures.Diamond(13, BronzeHi, BronzeLo, BronzeHi);
                return _dividerDiamond;
            }
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

        /// <summary>
        /// Базова рамка UI v2: темний контур, бронзова лінія 1 px, внутрішня
        /// темна лінія, вертикальний градієнт заливки (угорі світліше).
        /// </summary>
        private static SkinTextures.Frame BaseFrame(int size, int chamfer, Color32 fillTop, Color32 fillBottom)
        {
            return new SkinTextures.Frame
            {
                Size = size,
                Chamfer = chamfer,
                Outer = Outline,
                Line = Bronze,
                LineWidth = 1,
                Inner = LineInner,
                FillTop = fillTop,
                FillBottom = fillBottom
            };
        }

        /// <summary>Стиль без станів (панель, підказка, вставка) з 9-slice рамкою.</summary>
        private static GUIStyle FramedStyle(SkinTextures.Frame frame, RectOffset padding)
        {
            var style = new GUIStyle
            {
                padding = padding,
                margin = new RectOffset(6, 6, 6, 6)
            };
            int b = SkinTextures.SliceBorder(frame);
            style.border = new RectOffset(b, b, b, b);
            style.normal.background = SkinTextures.Build(frame);
            return style;
        }

        /// <summary>Клікабельний стиль із трьома станами рамки (спокій/наведення/натискання).</summary>
        private static GUIStyle ClickableStyle(SkinTextures.Frame normal, SkinTextures.Frame hover, SkinTextures.Frame active,
                                               Color32 textColor, Color32 hoverTextColor)
        {
            var style = new GUIStyle
            {
                fontSize = BodyFontSize,
                wordWrap = false,
                richText = false,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(18, 18, 10, 10),
                margin = new RectOffset(4, 4, 4, 4),
                font = ButtonFont
            };
            int b = Max3(SkinTextures.SliceBorder(normal), SkinTextures.SliceBorder(hover), SkinTextures.SliceBorder(active));
            style.border = new RectOffset(b, b, b, b);
            style.normal.background = SkinTextures.Build(normal);
            style.normal.textColor = textColor;
            style.hover.background = SkinTextures.Build(hover);
            style.hover.textColor = hoverTextColor;
            style.active.background = SkinTextures.Build(active);
            style.active.textColor = hoverTextColor;
            style.focused.background = style.normal.background;
            style.focused.textColor = textColor;
            return style;
        }

        /// <summary>
        /// Панель/вікно: зрізані кути, подвійна рамка, бронзові шпильки в кутах,
        /// ледь світліший верх — сама панель не клікабельна, станів немає.
        /// </summary>
        private static GUIStyle PanelStyle()
        {
            var frame = BaseFrame(48, 7, new Color32(30, 24, 18, BgPanel.a), new Color32(18, 14, 11, BgPanel.a));
            frame.CornerStuds = true;
            frame.Stud = new Color32(212, 183, 122, 200);
            var style = FramedStyle(frame, new RectOffset(22, 22, 18, 18));
            style.fontSize = BodyFontSize;
            style.wordWrap = true;
            style.richText = true;
            style.normal.textColor = TextMain;
            return style;
        }

        /// <summary>
        /// Кнопка з трьома станами. Сигнатура та сама, що до UI v2 (нею
        /// користуються Widgets і екрани): кольори задають заливку станів, а
        /// рамка — бронза, яка при наведенні світлішає й дає м'яке сяйво
        /// всередину. Якщо всі три кольори однакові — це «тиха» мітка у
        /// вигляді кнопки (вимкнена дія): тьмяна рамка, жодного сяйва.
        /// </summary>
        public static GUIStyle ButtonStyle(Color32 normalBg, Color32 hoverBg, Color32 activeBg, Color32 textColor)
        {
            bool quiet = SameColor(normalBg, hoverBg) && SameColor(normalBg, activeBg);

            var normal = BaseFrame(32, 4, Shade(normalBg, 1.18f), Shade(normalBg, 0.82f));
            var hover = BaseFrame(32, 4, Shade(hoverBg, 1.22f), Shade(hoverBg, 0.86f));
            var active = BaseFrame(32, 4, Shade(activeBg, 0.95f), Shade(activeBg, 0.8f));

            if (quiet)
            {
                normal.Line = BronzeLo;
                hover = normal;
                active = normal;
            }
            else
            {
                hover.Line = BronzeHi;
                hover.Glow = new Color32(230, 205, 150, 55);
                hover.GlowWidth = 4;
                active.Line = BronzeLo;
            }

            var style = ClickableStyle(normal, hover, active, textColor, textColor);
            if (!quiet) style.active.textColor = textColor;
            return style;
        }

        private static GUIStyle FieldStyle()
        {
            var normal = BaseFrame(24, 2, new Color32(10, 8, 6, 255), new Color32(16, 13, 10, 255));
            normal.Line = LineInner;
            var focus = normal;
            focus.Line = Bronze;
            var style = ClickableStyle(normal, normal, focus, TextMain, TextMain);
            style.focused.background = SkinTextures.Build(focus);
            style.alignment = TextAnchor.MiddleLeft;
            style.font = null;
            style.padding = new RectOffset(12, 12, 8, 8);
            return style;
        }

        /// <summary>
        /// Фікс-ревью (блокер, раунд 2, знайдено QA): без <c>fixedWidth</c>/
        /// <c>fixedHeight</c> Unity малює смугу прокрутки шириною/висотою 0 —
        /// не тьмяну, а буквально відсутню. UI v2: тонша «нитка» (12 px), але
        /// не нуль — прокрутку видно.
        /// </summary>
        private const float ScrollbarThickness = 12f;

        private static GUIStyle TrackStyle()
        {
            var frame = BaseFrame(16, 2, new Color32(12, 10, 7, 220), new Color32(12, 10, 7, 220));
            frame.Line = LineInner;
            frame.Outer = new Color32(0, 0, 0, 0);
            var style = FramedStyle(frame, new RectOffset(0, 0, 0, 0));
            style.margin = new RectOffset(0, 0, 0, 0);
            style.fixedWidth = ScrollbarThickness;
            style.fixedHeight = ScrollbarThickness;
            return style;
        }

        private static GUIStyle ThumbStyle()
        {
            var normal = BaseFrame(16, 2, new Color32(120, 98, 64, 255), new Color32(84, 66, 44, 255));
            normal.Line = BronzeLo;
            var hover = BaseFrame(16, 2, new Color32(168, 139, 90, 255), new Color32(120, 98, 64, 255));
            hover.Line = Bronze;
            var style = new GUIStyle
            {
                padding = new RectOffset(0, 0, 0, 0),
                fixedWidth = ScrollbarThickness,
                fixedHeight = ScrollbarThickness
            };
            int b = SkinTextures.SliceBorder(normal);
            style.border = new RectOffset(b, b, b, b);
            style.normal.background = SkinTextures.Build(normal);
            style.hover.background = SkinTextures.Build(hover);
            style.active.background = style.hover.background;
            return style;
        }

        /// <summary>Світліше/темніше в межах 0..255, альфа та сама.</summary>
        private static Color32 Shade(Color32 c, float k)
        {
            return new Color32(ClampByte(c.r * k), ClampByte(c.g * k), ClampByte(c.b * k), c.a);
        }

        private static byte ClampByte(float v) => (byte)(v < 0f ? 0f : (v > 255f ? 255f : v));

        private static bool SameColor(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

        private static int Max3(int a, int b, int c)
        {
            int m = a > b ? a : b;
            return m > c ? m : c;
        }
    }
}
