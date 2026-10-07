using System;
using UnityEngine;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Дрібні IMGUI-хелпери, спільні для всіх майбутніх екранів хаба: панелі,
    /// кнопки трьох намірів, рядки-підписи, скрол-списки, модалка, підказка,
    /// індикатор стадій, тег-чіп, відповідна розкладка 1280×720..2560×1440.
    ///
    /// НІЧОГО ТУТ НЕ ЗНАЄ ПРО GameSession — це набір інструментів малювання,
    /// не логіка екранів. Майбутні *Screen.cs (Е1) будуть викликати ці методи,
    /// передаючи вже готові рядки й прапорці, а не рахувати щось самі.
    /// </summary>
    public static class Widgets
    {
        private static GUIStyle _primaryButton;
        private static GUIStyle _secondaryButton;
        private static GUIStyle _dangerButton;
        private static GUIStyle _disabledButton;

        /// <summary>
        /// Стиль з білою заливкою: множення на <see cref="GUI.backgroundColor"/>
        /// дає точний колір без окремої текстури на кожен відтінок — саме так
        /// тонуються бейджі й піпси, чий колір залежить від даних під час
        /// показу (полоса виходу, прогрес), а не відомий наперед, як у кнопок.
        /// </summary>
        private static GUIStyle _tintable;
        private static Texture2D _whiteTexture;

        // ================= панелі та секції =================

        /// <summary>
        /// Панель-вікно (UI v2, орієнтир BG3): рамка з кутовими шпильками, назва
        /// антиквою по центру і роздільник-ромб під нею, далі вміст.
        /// </summary>
        public static void Panel(string title, Action drawBody, params GUILayoutOption[] options)
        {
            GUILayout.BeginVertical(GUI.skin.box, options);
            if (!string.IsNullOrEmpty(title))
            {
                GUILayout.Label(title, AlphaSkin.WindowTitle);
                Divider();
            }
            if (drawBody != null) drawBody();
            GUILayout.EndVertical();
        }

        /// <summary>
        /// Роздільник: бронзова лінія, що згасає до країв, з ромбом-«ружею»
        /// посередині. Займає рядок потоку GUILayout; малюється лише в Repaint.
        /// </summary>
        public static void Divider()
        {
            const float height = 16f;
            var rect = GUILayoutUtility.GetRect(0f, height, GUILayout.ExpandWidth(true), GUILayout.Height(height));
            var evt = Event.current;
            if (evt == null || evt.type != EventType.Repaint) return;
            DividerAt(rect);
        }

        /// <summary>Той самий роздільник за готовими координатами (абсолютні екрани, підказки).</summary>
        public static void DividerAt(Rect rect)
        {
            float cy = rect.y + rect.height * 0.5f;
            var line = new Rect(rect.x + 8f, cy, rect.width - 16f, 1f);
            GUI.DrawTexture(line, AlphaSkin.DividerLine, ScaleMode.StretchToFill);
            const float d = 13f;
            GUI.DrawTexture(new Rect(rect.x + rect.width * 0.5f - d * 0.5f, cy - d * 0.5f, d, d),
                AlphaSkin.DividerDiamond, ScaleMode.StretchToFill);
        }

        /// <summary>Секція всередині панелі — підзаголовок, без власного фону й рамки.</summary>
        public static void Section(string title, Action drawBody)
        {
            GUILayout.BeginVertical();
            if (!string.IsNullOrEmpty(title)) GUILayout.Label(title, AlphaSkin.SubHeader);
            if (drawBody != null) drawBody();
            GUILayout.EndVertical();
        }

        // ================= кнопки =================

        /// <summary>Звук натискання (віха M1.19): кнопка кладе такт у чергу, режисер звуку грає.</summary>
        private static bool Clicked(bool pressed, SoundCue cue)
        {
            if (pressed) SoundSettings.Request(cue);
            return pressed;
        }

        /// <summary>Головна дія екрана — акцентний теплий колір.</summary>
        public static bool PrimaryButton(string label, params GUILayoutOption[] options)
        {
            if (_primaryButton == null)
                _primaryButton = AlphaSkin.ButtonStyle(AlphaSkin.Accent, AlphaSkin.AccentHover, AlphaSkin.AccentActive, AlphaSkin.BgDark);
            return Clicked(GUILayout.Button(label, _primaryButton, options), SoundCue.UiConfirm);
        }

        private static GUIStyle _primaryWrapButton;

        /// <summary>
        /// Головна дія з довгим підписом (варіант репліки): текст переноситься
        /// на новий рядок, а не розпирає панель за край екрана — на 1280×720
        /// перший варіант вибору першої будівлі обрізався (тур 29.09.2026).
        /// </summary>
        public static bool PrimaryWrapButton(string label, params GUILayoutOption[] options)
        {
            if (_primaryButton == null)
                _primaryButton = AlphaSkin.ButtonStyle(AlphaSkin.Accent, AlphaSkin.AccentHover, AlphaSkin.AccentActive, AlphaSkin.BgDark);
            if (_primaryWrapButton == null)
                _primaryWrapButton = new GUIStyle(_primaryButton) { wordWrap = true };
            return Clicked(GUILayout.Button(label, _primaryWrapButton, options), SoundCue.UiConfirm);
        }

        /// <summary>Другорядна дія — той самий тон, що й базова кнопка скіну.</summary>
        public static bool SecondaryButton(string label, params GUILayoutOption[] options)
        {
            if (_secondaryButton == null)
                _secondaryButton = AlphaSkin.ButtonStyle(AlphaSkin.BgRaised, AlphaSkin.BgHover, AlphaSkin.BgActive, AlphaSkin.TextMain);
            return Clicked(GUILayout.Button(label, _secondaryButton, options), SoundCue.UiClick);
        }

        /// <summary>
        /// Кнопка-вкладка/перемикач (E1b): те саме, що дав би <c>GUILayout.Toggle(bool,string,
        /// GUIStyle,...)</c>, але без перевантаження, якого немає в стабі
        /// лінту (<c>tools/Game.Gameplay.Lint/UnityEngineStub.cs</c> навмисно
        /// вузький). Клік завжди повертає true — викликач сам присвоює вибір.
        /// UI v2: обрана — світліша, зі світлою бронзовою рамкою і сяйвом;
        /// необрана — темніша, тьмяна рамка, другорядний текст.
        /// </summary>
        public static bool TabButton(string label, bool selected, params GUILayoutOption[] options)
            => Clicked(GUILayout.Button(label, AlphaSkin.TabStyle(selected), options), SoundCue.UiToggle);

        /// <summary>Вкладка своєї ширини, не розтягнута на весь рядок (рядок вкладок переноситься).</summary>
        public static bool CompactTabButton(string label, bool selected)
            => TabButton(label, selected, GUILayout.ExpandWidth(false));

        /// <summary>Скільки місця займе кнопка-вкладка з цим підписом (для переносу рядка вкладок).</summary>
        public static float TabButtonWidth(string label)
        {
            return AlphaSkin.TabStyle(false).CalcSize(new GUIContent(label)).x + 8f;
        }

        /// <summary>Незворотна/ризикова дія (кроваво, підтвердження) — темно-червоний тон.</summary>
        public static bool DangerButton(string label, params GUILayoutOption[] options)
        {
            if (_dangerButton == null)
                _dangerButton = AlphaSkin.ButtonStyle(AlphaSkin.Danger, AlphaSkin.DangerHover, AlphaSkin.DangerActive, AlphaSkin.TextMain);
            return GUILayout.Button(label, _dangerButton, options);
        }

        /// <summary>
        /// Кнопка, яку не можна натиснути, з видимою причиною поруч. Столп
        /// «шлях завжди видно» (Поправка №1) стосується й інтерфейсу: гравець
        /// бачить, ЧОМУ дія закрита, а не натискає навмання.
        /// </summary>
        public static void DisabledButton(string label, string reason, params GUILayoutOption[] options)
        {
            if (_disabledButton == null)
                _disabledButton = AlphaSkin.ButtonStyle(AlphaSkin.BgDark, AlphaSkin.BgDark, AlphaSkin.BgDark, AlphaSkin.TextDim);

            // Мітка у вигляді кнопки, а не кнопка з GUI.enabled = false: Unity
            // малює вимкнені елементи напівпрозорими разом із тлом, і на 3D-сцені
            // їх неможливо було прочитати (власник, 25.09.2026: «а що це
            // прозорим?»). Мітка не клікається і лишається непрозорою.
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _disabledButton, options);
            if (!string.IsNullOrEmpty(reason)) GUILayout.Label(reason, AlphaSkin.Tooltip);
            GUILayout.EndHorizontal();
        }

        // ================= рядки та списки =================

        /// <summary>Підпис зліва, значення праворуч — картки статусу, гаманець, лічильники.</summary>
        public static void LabeledRow(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, AlphaSkin.Body, GUILayout.ExpandWidth(true));
            GUILayout.Label(value, AlphaSkin.Body, GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();
        }

        /// <summary>Початок скрол-списку — тонка обгортка над GUILayout, щоб екрани не пам'ятали сигнатуру.</summary>
        public static Vector2 ScrollListBegin(Vector2 scrollPosition, params GUILayoutOption[] options)
        {
            return GUILayout.BeginScrollView(scrollPosition, options);
        }

        public static void ScrollListEnd()
        {
            GUILayout.EndScrollView();
        }

        // ================= модалка та підказка =================

        /// <summary>
        /// Затемнення на весь екран + панель посередині. <paramref name="onClose"/>
        /// — намір закрити (Esc), не сама дія закриття: модалка не вирішує,
        /// що робити з рішенням гравця, лише повідомляє про запит.
        /// </summary>
        public static void Modal(string title, Action drawBody, Action onClose = null)
        {
            var backdrop = new Rect(0f, 0f, Screen.width, Screen.height);
            var previousColor = GUI.color;
            GUI.color = AlphaSkin.ModalDim;
            GUI.DrawTexture(backdrop, TintableTexture(), ScaleMode.StretchToFill);
            GUI.color = previousColor;

            float width = Clamp(Screen.width * 0.5f, 480f, 900f);
            float height = Clamp(Screen.height * 0.5f, 320f, 640f);
            var area = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

            GUILayout.BeginArea(area);
            Panel(title, drawBody);
            GUILayout.EndArea();

            // Event-based, не сирий Input.GetKeyDown (фікс-ревью): останній
            // лишається true впродовж усіх OnGUI-проходів кадру (Layout, сама
            // подія, Repaint, ...), тому raw-polling викликав би onClose кілька
            // разів за одне фізичне натискання. Event.current.type == KeyDown
            // істинний лише під час ЄДИНОГО проходу, що відповідає цій самій
            // події.
            if (onClose != null)
            {
                var evt = Event.current;
                if (evt != null && evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
                {
                    evt.Use();
                    onClose();
                }
            }
        }

        private static GUIStyle _tipTitle;
        private static GUIStyle _tipCurrent;
        private static GUIStyle _tipOther;

        /// <summary>
        /// Підказка під якорем (UI v2): панель-підказка шкурки. Якщо рядків
        /// більше одного, перший — назва антиквою з роздільником під нею;
        /// рядок <paramref name="currentIndex"/> — напівжирний пергамент
        /// (поточна полоса драбини), решта — другорядним кольором.
        /// Притискається до країв екрана.
        /// </summary>
        public static void HoverTip(Rect anchor, System.Collections.Generic.IReadOnlyList<string> lines, int currentIndex)
        {
            if (lines == null || lines.Count == 0) return;
            EnsureTipStyles();

            bool hasTitle = lines.Count > 1;
            const float lineHeight = AlphaSkin.BodyFontSize + 10f;
            const float dividerHeight = 14f;
            var pad = AlphaSkin.TooltipPanel.padding;

            float width = 160f;
            for (int i = 0; i < lines.Count; i++)
            {
                float w = TipStyle(i, hasTitle, currentIndex, lines.Count).CalcSize(new GUIContent(lines[i] ?? string.Empty)).x;
                if (w > width) width = w;
            }
            width = Clamp(width + pad.left + pad.right + 4f, 180f, 560f);
            float height = lines.Count * lineHeight + (hasTitle ? dividerHeight : 0f) + pad.top + pad.bottom;

            float x = Clamp(anchor.x, 8f, Math.Max(8f, Screen.width - width - 8f));
            float y = anchor.y + anchor.height + 6f;
            if (y + height > Screen.height - 8f) y = Math.Max(8f, anchor.y - height - 6f);
            var box = new Rect(x, y, width, height);

            GUI.Box(box, GUIContent.none, AlphaSkin.TooltipPanel);

            float cy = box.y + pad.top;
            for (int i = 0; i < lines.Count; i++)
            {
                var row = new Rect(box.x + pad.left, cy, box.width - pad.left - pad.right, lineHeight);
                GUI.Label(row, lines[i] ?? string.Empty, TipStyle(i, hasTitle, currentIndex, lines.Count));
                cy += lineHeight;
                if (hasTitle && i == 0)
                {
                    DividerAt(new Rect(box.x + pad.left, cy - 2f, box.width - pad.left - pad.right, dividerHeight));
                    cy += dividerHeight;
                }
            }
        }

        private static GUIStyle TipStyle(int index, bool hasTitle, int currentIndex, int count)
        {
            if (hasTitle && index == 0) return _tipTitle;
            return index == currentIndex || count == 1 ? _tipCurrent : _tipOther;
        }

        private static void EnsureTipStyles()
        {
            if (_tipTitle != null) return;

            _tipTitle = new GUIStyle(AlphaSkin.SubHeader) { wordWrap = false, fontSize = AlphaSkin.BodyFontSize + 1 };
            _tipCurrent = new GUIStyle(AlphaSkin.Body) { wordWrap = false, font = AlphaSkin.StrongFont };
            _tipCurrent.normal.textColor = AlphaSkin.TextMain;
            _tipOther = new GUIStyle(AlphaSkin.Body) { wordWrap = false };
            _tipOther.normal.textColor = AlphaSkin.TextDim;
        }

        /// <summary>Дрібний притишений рядок-підказка — під полем, під кнопкою, де завгодно.</summary>
        public static void TooltipLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            GUILayout.Label(text, AlphaSkin.Tooltip);
        }

        // ================= індикатори =================

        /// <summary>
        /// Стадії/кроки квадратиками зліва праворуч: заповнені — акцентним
        /// кольором, порожні — тьмяним. Квадратик, не юнікод-гліф (●/○):
        /// вбудований шрифт Unity гарантовано рендерить кирилицю (перевірено
        /// цим самим білдом), але не гарантує довільні символи поза нею.
        /// </summary>
        public static void ProgressPips(int current, int total, float pipSize = 18f)
        {
            if (total <= 0) return;

            GUILayout.BeginHorizontal();
            for (int i = 0; i < total; i++)
            {
                var previous = GUI.backgroundColor;
                GUI.backgroundColor = i < current ? AlphaSkin.Accent : AlphaSkin.BgRaised;
                GUILayout.Box(string.Empty, Tintable(), GUILayout.Width(pipSize), GUILayout.Height(pipSize));
                GUI.backgroundColor = previous;
                GUILayout.Space(4f);
            }
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// Тег-чіп: короткий підпис на кольоровому тлі — так полоси, показані
        /// словами (band-як-слово, R17/Поправка №3.7), лишаються видимими
        /// одним погладом, а не читанням тексту.
        ///
        /// <paramref name="textColor"/> (Бій v2, раунд 2, фікс-ревью «жовтий
        /// текст на жовтому/синій на синьому», аудит знімків п.2): бейдж
        /// малює текст сталим <see cref="AlphaSkin.TextMain"/>, який на
        /// світлому тлі (золото поточного юніта, приглушений тон сторони)
        /// зливається з фоном — виклик, якому потрібен інший контраст,
        /// підставляє свій колір, не мутуючи спільний кешований стиль
        /// назавжди (значення повертається одразу після малювання).
        /// </summary>
        public static void Badge(string text, Color32 tint, Color32? textColor = null)
        {
            var style = Tintable();
            var previousText = style.normal.textColor;
            if (textColor.HasValue) style.normal.textColor = textColor.Value;

            var previousBg = GUI.backgroundColor;
            GUI.backgroundColor = tint;
            GUILayout.Box(text, style, GUILayout.ExpandWidth(false));
            GUI.backgroundColor = previousBg;

            style.normal.textColor = previousText;
        }

        /// <summary>
        /// Бій v2, раунд 2 (§2 «поточний — золоте тло + темний текст + рамка»):
        /// той самий <see cref="Badge"/>, обгорнутий тонкою рамкою кольору
        /// <paramref name="borderTint"/> — другий канал розрізнення поточного
        /// юніта в стрічці ініціативи, крім кольору тла (аудит знімків: колір
        /// сам по собі губився серед інших бейджів того самого тону).
        /// </summary>
        public static void BorderedBadge(string text, Color32 bgTint, Color32 textColor, Color32 borderTint)
        {
            GUILayout.BeginVertical(BorderStyle(borderTint), GUILayout.ExpandWidth(false));
            Badge(text, bgTint, textColor);
            GUILayout.EndVertical();
        }

        /// <summary>
        /// Фікс-ревью (major, раунд 2, знайдено QA): ширина <see cref="Badge"/>
        /// для гравця, що загортає ряд бейджів (черга ходу бою — до 6+ юнітів,
        /// довгі імена на кшталт "Розвідник орди"), щоб не впертися суцільним
        /// рядком у праву межу панелі HUD і не обрізати останні бейджі за
        /// кадром (значення "margin" не входить у CalcSize, тому додаємо його
        /// подвоєним вручну — той самий відступ, що Tintable().margin).
        /// </summary>
        public static float BadgeWidth(string text)
        {
            return Tintable().CalcSize(new GUIContent(text ?? string.Empty)).x + Tintable().margin.left + Tintable().margin.right;
        }

        /// <summary>
        /// Лівий+правий padding <see cref="Panel"/> (GUI.skin.box — §AlphaSkin.
        /// PanelStyle) у пікселях: те, наскільки вміст панелі вужчий за саму
        /// панель. Викликачі, що самі загортають рядок по ширині (§ фікс-ревью
        /// <c>BattleHudScreen.DrawInitiativeStrip</c>), рахують доступну ширину
        /// звідси, а не дублюють число "18+18" магічною константою.
        /// </summary>
        public static float PanelContentInset()
        {
            var box = GUI.skin != null ? GUI.skin.box : null;
            return box != null ? box.padding.left + box.padding.right : 36f;
        }

        // ================= абсолютне позиціонування (Бій v2) =================
        // На відміну від решти файлу (усе інше — GUILayout, автоматичний
        // потік), оверлеї над бійцями на арені (BattleHudScreen.DrawOverlays)
        // і спливаючі написи малюються за готовими екранними координатами
        // (BattleUnitOverlay/BattleFloatingText, GUI-простір) — їм потрібен
        // GUI.* напряму, не GUILayout.

        /// <summary>Суцільний прямокутник довільним кольором за готовими координатами — підкладка під ім'я юніта, трек смужки HP.</summary>
        public static void SolidRect(Rect rect, Color32 tint)
        {
            var previous = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(rect, TintableTexture(), ScaleMode.StretchToFill);
            GUI.color = previous;
        }

        /// <summary>
        /// Смужка прогресу (HP/AP) за готовими координатами: темний трек на
        /// всю ширину + заповнена частка зверху. Той самий принцип, що
        /// <see cref="ProgressPips"/>/<c>DrawFractionBar</c>, але для оверлеїв
        /// над бійцями, де GUILayout не підходить (позиція — не потік).
        /// </summary>
        public static void FilledBarAt(Rect rect, float fraction, Color32 fillTint)
        {
            SolidRect(rect, new Color32(AlphaSkin.BgDark.r, AlphaSkin.BgDark.g, AlphaSkin.BgDark.b, 210));
            float f = Clamp(fraction, 0f, 1f);
            if (f <= 0f) return;
            var filled = new Rect(rect.x, rect.y, rect.width * f, rect.height);
            SolidRect(filled, fillTint);
        }

        // ================= відповідна розкладка =================

        /// <summary>Масштаб від контрольної ширини 1280 — на 2560×1440 елементи не тонуть у порожньому полі.</summary>
        public static float ScaleForScreen()
        {
            return Clamp(Screen.width / 1280f, 1f, 2f);
        }

        /// <summary>Прямокутник по центру екрана — та сама математика, що в <see cref="Modal"/>, для власних панелей.</summary>
        public static Rect CenteredRect(float width, float height)
        {
            return new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
        }

        /// <summary>Бічний відступ, що росте з роздільною здатністю (16px на контрольній ширині 1280).</summary>
        public static float ScreenPadding()
        {
            return 16f * ScaleForScreen();
        }

        // ================= внутрішнє =================

        private static GUIStyle _borderStyle;
        private static Color32 _borderStyleTint;

        /// <summary>
        /// Стиль-«рамка» для <see cref="BorderedBadge"/>: суцільне тло
        /// кольору рамки під тонким відступом, крізь який проглядає вкладений
        /// <see cref="Badge"/> — той самий трюк, що обвідка картки в решті
        /// застосунку, лише в один колір без окремої 9-slice текстури.
        /// </summary>
        private static GUIStyle BorderStyle(Color32 tint)
        {
            if (_borderStyle == null || !ColorsEqual(_borderStyleTint, tint))
            {
                _borderStyle = new GUIStyle
                {
                    padding = new RectOffset(2, 2, 2, 2),
                    margin = new RectOffset(2, 2, 2, 2)
                };
                _borderStyle.normal.background = AlphaSkin.SolidTexture(tint);
                _borderStyleTint = tint;
            }
            return _borderStyle;
        }

        private static bool ColorsEqual(Color32 a, Color32 b)
            => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

        private static GUIStyle Tintable()
        {
            if (_tintable == null)
            {
                _tintable = new GUIStyle
                {
                    fontSize = AlphaSkin.BodyFontSize - 4,
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = false,
                    padding = new RectOffset(10, 10, 4, 4),
                    margin = new RectOffset(2, 2, 2, 2)
                };
                // UI v2: чіп у рамці — світла сіра рамка й білий градієнт,
                // помножені на тон, дають темніший обрис і живу заливку того
                // самого кольору (один колір — один сенс, рамка лише форма).
                var chip = new SkinTextures.Frame
                {
                    Size = 16,
                    Chamfer = 2,
                    Outer = new Color32(40, 40, 40, 255),
                    Line = new Color32(165, 165, 165, 255),
                    LineWidth = 1,
                    Inner = new Color32(235, 235, 235, 255),
                    FillTop = new Color32(255, 255, 255, 255),
                    FillBottom = new Color32(222, 222, 222, 255)
                };
                int b = SkinTextures.SliceBorder(chip);
                _tintable.border = new RectOffset(b, b, b, b);
                _tintable.normal.background = SkinTextures.Build(chip);
                _tintable.normal.textColor = AlphaSkin.TextMain;
            }
            return _tintable;
        }

        /// <summary>
        /// Суцільна БІЛА текстура: помножена на <see cref="GUI.backgroundColor"/>,
        /// дає точно цей колір. Одна текстура на все — OnGUI кличе цей метод
        /// щокадру, і створювати нову щоразу означало б смітити пам'ять.
        /// </summary>
        private static Texture2D TintableTexture()
        {
            if (_whiteTexture == null) _whiteTexture = AlphaSkin.SolidTexture(new Color32(255, 255, 255, 255));
            return _whiteTexture;
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            return value > max ? max : value;
        }
    }
}
