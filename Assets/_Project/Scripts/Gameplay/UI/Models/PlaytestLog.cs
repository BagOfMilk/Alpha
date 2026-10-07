using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Game.Gameplay.UI
{
    /// <summary>Тип нотатки тестера — клавіші 1–5 у вікні нотатки (docs/PLAYTEST.md).</summary>
    public enum PlaytestCategory
    {
        Bug = 1,      // зламано: не працює, застрягло, виняток
        Visual = 2,   // вигляд: модель, текстура, анімація, світло
        Balance = 3,  // числа: занадто легко/важко/дорого
        Ux = 4,       // незрозуміло, незручно, бракує підказки
        Idea = 5      // побажання, не баг
    }

    /// <summary>Контекст, який нотатка бере сама — тестер пише лише суть.</summary>
    public sealed class PlaytestContext
    {
        public string State;
        public int Day;
        public string Phase;
        public string Place;       // будівля/панель/екран
        public string Weather;
        public string GraphicsTier;
        public float Fps;
        public IReadOnlyList<string> RecentEvents; // останні ключі журналу DayLog
    }

    /// <summary>
    /// Журнал плейтесту (практика «вбудований репортер»: одна клавіша, скриншот, стан і збереження додаються
    /// самі; розбір — пачкою після сесії). Чистий C#: форматує заголовок сесії, рядок нотатки й зведення
    /// помилок рушія, які збираються автоматично. Файли пише <c>Gameplay/Playtest/PlaytestReporter</c>.
    /// Охоронець — <c>PlaytestLogTests</c>.
    /// </summary>
    public static class PlaytestLog
    {
        public const int MaxNoteLength = 500;

        /// <summary>Відкрите вікно нотатки: клавіші гри (ходьба, Esc, гарячі клавіші бою) мовчать, поки тестер пише.</summary>
        public static bool NoteOpen { get; set; }
        public const int RecentEventCount = 12;

        public static string CategoryLabel(PlaytestCategory c)
        {
            switch (c)
            {
                case PlaytestCategory.Bug: return "БАГ";
                case PlaytestCategory.Visual: return "ВИГЛЯД";
                case PlaytestCategory.Balance: return "БАЛАНС";
                case PlaytestCategory.Ux: return "UX";
                default: return "ІДЕЯ";
            }
        }

        /// <summary>Клавіша 1–5 → тип; інше — null.</summary>
        public static PlaytestCategory? CategoryForDigit(int digit) =>
            digit >= 1 && digit <= 5 ? (PlaytestCategory?)digit : null;

        /// <summary>Тека сесії: дата й час старту, сортується за часом.</summary>
        public static string SessionFolderName(DateTime started) =>
            started.ToString("yyyy-MM-dd_HH-mm", CultureInfo.InvariantCulture);

        /// <summary>Ім'я файлів нотатки: 001, 002… (скриншот <c>001.jpg</c>, зліпок <c>001.save.txt</c>).</summary>
        public static string NoteId(int index) => index.ToString("000", CultureInfo.InvariantCulture);

        public static string SessionHeader(string commit, DateTime started, string system, string resolution)
        {
            var sb = new StringBuilder();
            sb.Append("# Плейтест ").Append(started.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append('\n');
            sb.Append("- Білд: `").Append(string.IsNullOrEmpty(commit) ? "невідомий" : commit).Append("`\n");
            sb.Append("- Система: ").Append(system ?? "—").Append('\n');
            sb.Append("- Екран: ").Append(resolution ?? "—").Append('\n');
            sb.Append('\n');
            sb.Append("| № | Тип | Нотатка | Де | Доба · фаза | Скриншот | Зліпок |\n");
            sb.Append("|---|---|---|---|---|---|---|\n");
            return sb.ToString();
        }

        /// <summary>Рядок таблиці нотаток (markdown); переноси й «|» у тексті не ламають таблицю.</summary>
        public static string NoteRow(int index, PlaytestCategory category, string text, PlaytestContext ctx, bool hasShot, bool hasSave)
        {
            string id = NoteId(index);
            string where = ctx == null ? "—" : Clean(ctx.Place ?? ctx.State ?? "—");
            string when = ctx == null ? "—" : "д" + ctx.Day + " · " + Clean(ctx.Phase ?? "—");
            return "| " + id + " | " + CategoryLabel(category) + " | " + Clean(Trim(text)) + " | " + where + " | " + when
                   + " | " + (hasShot ? "[" + id + ".jpg](" + id + ".jpg)" : "—")
                   + " | " + (hasSave ? "[" + id + ".save.txt](" + id + ".save.txt)" : "—") + " |\n";
        }

        /// <summary>Повний контекст нотатки — окремим файлом <c>NNN.txt</c> (читається лише коли треба).</summary>
        public static string NoteDetails(int index, PlaytestCategory category, string text, PlaytestContext ctx)
        {
            var sb = new StringBuilder();
            sb.Append(NoteId(index)).Append(" · ").Append(CategoryLabel(category)).Append('\n');
            sb.Append(Trim(text)).Append("\n\n");
            if (ctx != null)
            {
                sb.Append("Стан: ").Append(ctx.State).Append('\n');
                sb.Append("Доба: ").Append(ctx.Day).Append(", фаза: ").Append(ctx.Phase).Append('\n');
                sb.Append("Де: ").Append(ctx.Place).Append('\n');
                sb.Append("Погода: ").Append(ctx.Weather).Append('\n');
                sb.Append("Графіка: ").Append(ctx.GraphicsTier).Append(", кадрів/с: ")
                  .Append(ctx.Fps.ToString("0", CultureInfo.InvariantCulture)).Append('\n');
                if (ctx.RecentEvents != null && ctx.RecentEvents.Count > 0)
                    sb.Append("Останні події: ").Append(string.Join(", ", ToArray(ctx.RecentEvents))).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Помилки рушія (Application.logMessageReceived) збираються самі, без нотатки: однакові — один рядок
        /// з лічильником. Ключ — перший рядок повідомлення.
        /// </summary>
        public static string ErrorKey(string message)
        {
            if (string.IsNullOrEmpty(message)) return "(порожнє)";
            int nl = message.IndexOf('\n');
            string first = nl >= 0 ? message.Substring(0, nl) : message;
            return first.Length > 200 ? first.Substring(0, 200) : first;
        }

        public static string ErrorsReport(IReadOnlyList<KeyValuePair<string, int>> errors, IDictionary<string, string> firstStack)
        {
            var sb = new StringBuilder("# Помилки рушія за сесію\n\n");
            if (errors == null || errors.Count == 0) return sb.Append("Немає.\n").ToString();
            foreach (var e in errors)
            {
                sb.Append("- **×").Append(e.Value).Append("** ").Append(Clean(e.Key)).Append('\n');
                string stack;
                if (firstStack != null && firstStack.TryGetValue(e.Key, out stack) && !string.IsNullOrEmpty(stack))
                    sb.Append("  `").Append(Clean(ErrorKey(stack))).Append("`\n");
            }
            return sb.ToString();
        }

        private static string Trim(string text)
        {
            text = (text ?? string.Empty).Trim();
            return text.Length > MaxNoteLength ? text.Substring(0, MaxNoteLength) + "…" : text;
        }

        private static string Clean(string s) =>
            (s ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Replace("|", "/");

        private static string[] ToArray(IReadOnlyList<string> list)
        {
            var a = new string[list.Count];
            for (int i = 0; i < a.Length; i++) a[i] = list[i];
            return a;
        }
    }
}
