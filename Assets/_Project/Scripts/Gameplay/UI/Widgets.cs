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

        /// <summary>Панель зі шкіряним фоном і великим заголовком над вмістом.</summary>
        public static void Panel(string title, Action drawBody, params GUILayoutOption[] options)
        {
            GUILayout.BeginVertical(GUI.skin.box, options);
            if (!string.IsNullOrEmpty(title)) GUILayout.Label(title, AlphaSkin.Header);
            if (drawBody != null) drawBody();
            GUILayout.EndVertical();
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

        /// <summary>Головна дія екрана — акцентний теплий колір.</summary>
        public static bool PrimaryButton(string label, params GUILayoutOption[] options)
        {
            if (_primaryButton == null)
                _primaryButton = AlphaSkin.ButtonStyle(AlphaSkin.Accent, AlphaSkin.AccentHover, AlphaSkin.AccentActive, AlphaSkin.BgDark);
            return GUILayout.Button(label, _primaryButton, options);
        }

        /// <summary>Другорядна дія — той самий тон, що й базова кнопка скіну.</summary>
        public static bool SecondaryButton(string label, params GUILayoutOption[] options)
        {
            if (_secondaryButton == null)
                _secondaryButton = AlphaSkin.ButtonStyle(AlphaSkin.BgRaised, AlphaSkin.BgHover, AlphaSkin.BgActive, AlphaSkin.TextMain);
            return GUILayout.Button(label, _secondaryButton, options);
        }

        /// <summary>
        /// Кнопка-вкладка/перемикач (E1b): акцентна, коли обрана, інакше
        /// звичайна — те саме, що дав би <c>GUILayout.Toggle(bool,string,
        /// GUIStyle,...)</c>, але без перевантаження, якого немає в стабі
        /// лінту (<c>tools/Game.Gameplay.Lint/UnityEngineStub.cs</c> навмисно
        /// вузький). Клік завжди повертає true — викликач сам присвоює вибір.
        /// </summary>
        public static bool TabButton(string label, bool selected, params GUILayoutOption[] options)
            => selected ? PrimaryButton(label, options) : SecondaryButton(label, options);

        /// <summary>Вкладка своєї ширини, не розтягнута на весь рядок (рядок вкладок переноситься).</summary>
        public static bool CompactTabButton(string label, bool selected)
            => TabButton(label, selected, GUILayout.ExpandWidth(false));

        /// <summary>Скільки місця займе кнопка-вкладка з цим підписом (для переносу рядка вкладок).</summary>
        public static float TabButtonWidth(string label)
        {
            if (_secondaryButton == null)
                _secondaryButton = AlphaSkin.ButtonStyle(AlphaSkin.BgRaised, AlphaSkin.BgHover, AlphaSkin.BgActive, AlphaSkin.TextMain);
            return _secondaryButton.CalcSize(new GUIContent(label)).x + 8f;
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
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
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
        /// </summary>
        public static void Badge(string text, Color32 tint)
        {
            var previous = GUI.backgroundColor;
            GUI.backgroundColor = tint;
            GUILayout.Box(text, Tintable(), GUILayout.ExpandWidth(false));
            GUI.backgroundColor = previous;
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
            SolidRect(rect, new Color32(20, 16, 12, 200));
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
                _tintable.normal.background = TintableTexture();
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
