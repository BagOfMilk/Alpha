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
    /// <para>
    /// Те саме для роздільника частин « · » (огляд слоту 30, ПМ: рядок-сповіщення тижня переносився на «· ажіотаж на старті +2»):
    /// крапка-роздільник рядка не починає, розрив — після неї.
    /// </para>
    /// <para>
    /// І для числа зі знаком (огляд слоту 31: «ворожнеча» в кінці рядка, «+1» на початку наступного): «+1», «−2», «+₴40» стоять разом
    /// зі словом перед ними. Слово й усі такі знаки за ним — один нерозривний шматок («ворожнеча +1 ·»).
    /// </para>
    /// </summary>
    public static class TextBreak
    {
        public const string Open = "<nobr>", Close = "</nobr>";

        /// <summary>Тире, що стоїть окремим словом (з пробілами з обох боків): «—» і «–». Діапазон «7–9» — без пробілів, не чіпаємо.</summary>
        public static bool IsDash(string token) { return token == "—" || token == "–"; }

        /// <summary>Роздільник частин «·», що стоїть окремим словом. «а·б» без пробілів — не роздільник-слово.</summary>
        public static bool IsSeparator(string token) { return token == "·"; }

        /// <summary>
        /// Число зі знаком окремим словом: «+1», «−2», «-3», «+₴40», «+2,» (розділовий знак у кінці не заважає). Знак без цифри
        /// («+», «−») — ні.
        /// </summary>
        public static bool IsSignedNumber(string token)
        {
            if (token == null || token.Length < 2) return false;
            char c = token[0];
            if (c != '+' && c != '−' && c != '-') return false;
            int k = 1;
            if (token[k] == '₴') k++;   // «+₴40» — гроші
            return k < token.Length && char.IsDigit(token[k]);
        }

        /// <summary>Слово, що рядка не починає: тире, роздільник чи число зі знаком.</summary>
        private static bool IsGlueMark(string token) { return IsDash(token) || IsSeparator(token) || IsSignedNumber(token); }

        /// <summary>
        /// «… популярність — удруге …» → «… &lt;nobr&gt;популярність —&lt;/nobr&gt; удруге …». Текст, що вже має теги
        /// <c>&lt;nobr&gt;</c>, і текст без окремого тире повертається як є (повторний виклик нічого не міняє).
        /// </summary>
        public static string GlueDashes(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf(Open, StringComparison.Ordinal) >= 0) return s;
            if (s.IndexOf(" —", StringComparison.Ordinal) < 0 && s.IndexOf(" –", StringComparison.Ordinal) < 0
                && s.IndexOf(" ·", StringComparison.Ordinal) < 0 && s.IndexOf(" +", StringComparison.Ordinal) < 0
                && s.IndexOf(" −", StringComparison.Ordinal) < 0 && s.IndexOf(" -", StringComparison.Ordinal) < 0) return s;
            var t = s.Split(' ');
            var sb = new StringBuilder(s.Length + 16);
            for (int i = 0; i < t.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                // слово й усі знаки за ним, що рядка не починають («ворожнеча +1 ·», «+3 ·» на початку тексту), — один шматок
                int j = i + 1;
                if (t[i].Length > 0)
                    while (j < t.Length && IsGlueMark(t[j])) j++;
                if (j > i + 1)
                {
                    sb.Append(Open).Append(t[i]);
                    for (int k = i + 1; k < j; k++) sb.Append(' ').Append(t[k]);
                    sb.Append(Close);
                    i = j - 1;
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
