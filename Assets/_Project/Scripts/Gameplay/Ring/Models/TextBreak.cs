using System;
using System.Text;

namespace Game.Gameplay.Ring.Models
{
    /// <summary>
    /// Правило переносу тексту екранів (ПМ 12.11.2026, кадр слоту 28: у рядку поразки «популярність — удруге…» тире переносилось на
    /// початок другого рядка): тире (—, –) рядка не починає — слово перед ним і саме тире стоять разом. Нерозривного пробілу (U+00A0)
    /// у Fixel немає, тож разом їх тримає тег UI Toolkit <c>&lt;nobr&gt;</c> («Keep a segment of text together», підтримувані теги
    /// rich text Unity 6). Розрив лишається після тире. Кличуть <c>MenuKit.Body</c> і всі, хто ставить текст підказки; ту саму
    /// функцію міряє модель переносів у тестах (<c>TtfMetrics.Lines</c> розуміє <c>&lt;nobr&gt;</c>).
    /// </summary>
    public static class TextBreak
    {
        public const string Open = "<nobr>", Close = "</nobr>";

        /// <summary>Тире, що стоїть окремим словом (з пробілами з обох боків): «—» і «–». Діапазон «7–9» — без пробілів, не чіпаємо.</summary>
        public static bool IsDash(string token) { return token == "—" || token == "–"; }

        /// <summary>
        /// «… популярність — удруге …» → «… &lt;nobr&gt;популярність —&lt;/nobr&gt; удруге …». Текст, що вже має теги
        /// <c>&lt;nobr&gt;</c>, і текст без окремого тире повертається як є (повторний виклик нічого не міняє).
        /// </summary>
        public static string GlueDashes(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf(Open, StringComparison.Ordinal) >= 0) return s;
            if (s.IndexOf(" —", StringComparison.Ordinal) < 0 && s.IndexOf(" –", StringComparison.Ordinal) < 0) return s;
            var t = s.Split(' ');
            var sb = new StringBuilder(s.Length + 16);
            for (int i = 0; i < t.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                if (t[i].Length > 0 && i + 1 < t.Length && IsDash(t[i + 1]) && !IsDash(t[i]))
                {
                    sb.Append(Open).Append(t[i]).Append(' ').Append(t[i + 1]).Append(Close);
                    i++;
                }
                else sb.Append(t[i]);
            }
            return sb.ToString();
        }

        /// <summary>Текст без тегів <c>&lt;nobr&gt;</c> (як його бачить гравець) — для вимірів і зліпків тексту екрана.</summary>
        public static string Strip(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('<') < 0) return s;
            return s.Replace(Open, "").Replace(Close, "");
        }
    }
}
