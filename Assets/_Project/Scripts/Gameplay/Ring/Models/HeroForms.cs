using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Game.Gameplay.Ring.Models
{
    /// <summary>
    /// Жіночі форми звертання до героїні (П5, ПМ 12.11.2026: «Жіночі варіанти тексту (`&lt;ключ&gt;.f` …) — так»). Поруч із ключем,
    /// де герой у чоловічому роді («Ти виграв», «хлопче», «я сам»), лежить «ключ.f» з жіночою формою. <see cref="RingText"/>
    /// відступу на базовий ключ не знає, тож вибір робиться тут, а не в екранах: для героїні базовий ключ отримує текст «.f», для
    /// героя — повертається свій. Тоді промо, закулісся, кінцівки й кар'єра беруть звичайний ключ і нічого не знають про стать.
    /// <para>
    /// Форма — лише там, де є обидва ключі: пари статі на кшталт «c.look.sex.m / c.look.sex.f» базового ключа не мають і
    /// не чіпаються. Свій (чоловічий) текст кожного екземпляра <see cref="RingText"/> запам'ятовується окремо, поза словником, при
    /// першому виклику — повторний виклик і новий екземпляр (меню перечитує тексти) безпечні. Хто потім кладе в базовий ключ інший
    /// текст через <see cref="RingText.Add"/>, той для ключів із «.f» його втратить — для таких ключів не робити (ім'я героя
    /// «w.player.name» жіночої форми не має).
    /// </para>
    /// </summary>
    public static class HeroForms
    {
        public const string FemaleSuffix = ".f";

        private static readonly ConditionalWeakTable<RingText, Dictionary<string, string>> Own =
            new ConditionalWeakTable<RingText, Dictionary<string, string>>();

        public static string FemaleKey(string key) { return key + FemaleSuffix; }

        /// <summary>Базові ключі, що мають жіночу форму: є і «ключ», і «ключ.f».</summary>
        public static List<string> FormKeys(IDictionary<string, string> d)
        {
            var res = new List<string>();
            if (d == null) return res;
            foreach (var k in d.Keys)
            {
                if (k.Length <= FemaleSuffix.Length || !k.EndsWith(FemaleSuffix, StringComparison.Ordinal)) continue;
                var bk = k.Substring(0, k.Length - FemaleSuffix.Length);
                if (d.ContainsKey(bk)) res.Add(bk);
            }
            res.Sort(StringComparer.Ordinal);
            return res;
        }

        /// <summary>Підставити форми героїні (<paramref name="female"/>) чи повернути свої. Повертає, скільки ключів має форму.</summary>
        public static int Apply(RingText text, bool female)
        {
            if (text == null) return 0;
            var d = text.AsDictionary();
            var own = Own.GetValue(text, _ => new Dictionary<string, string>(StringComparer.Ordinal));
            var keys = FormKeys(d);
            foreach (var bk in keys)
            {
                string mine;
                if (!own.TryGetValue(bk, out mine)) { mine = d[bk]; own[bk] = mine; }
                d[bk] = female ? d[FemaleKey(bk)] : mine;
            }
            return keys.Count;
        }
    }
}
