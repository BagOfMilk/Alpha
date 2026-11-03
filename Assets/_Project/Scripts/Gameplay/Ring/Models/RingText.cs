using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Core.Ring;

namespace Game.Gameplay.Ring.Models
{
    /// <summary>
    /// Словник видимого тексту «Рингу»: рядки ворожнечі (strings_uk.json) + рядки екранів (ui_uk.json). Жодного
    /// українського рядка в коді екранів — лише ключі. Відсутній ключ повертається як ⟦ключ⟧ і запам'ятовується
    /// (<see cref="Missing"/>): тест проходить кампанію й вимагає, щоб порожнього не лишилось.
    /// </summary>
    public sealed class RingText
    {
        private readonly Dictionary<string, string> _d = new Dictionary<string, string>();
        private readonly HashSet<string> _missing = new HashSet<string>();

        public static RingText FromJson(params string[] jsons)
        {
            var t = new RingText();
            if (jsons != null)
                foreach (var j in jsons)
                {
                    if (string.IsNullOrEmpty(j)) continue;
                    foreach (var kv in MiniJson.ParseObject(j))
                        t._d[kv.Key] = Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
                }
            return t;
        }

        public void Add(string key, string value) { _d[key] = value; }

        public bool Has(string key) { return key != null && _d.ContainsKey(key); }

        public int Count { get { return _d.Count; } }

        /// <summary>Словник для PromoValidator.</summary>
        public IDictionary<string, string> AsDictionary() { return _d; }

        /// <summary>Ключі, яких попросили, а тексту нема.</summary>
        public IReadOnlyCollection<string> Missing { get { return _missing; } }

        public string Get(string key)
        {
            if (key == null) return "";
            string v;
            if (_d.TryGetValue(key, out v)) return v;
            _missing.Add(key);
            return "⟦" + key + "⟧";
        }

        public string Format(string key, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, Get(key), args);
        }
    }

    /// <summary>Відсотки з тридцять шостих: цілі, сума рівно 100 (метод найбільших залишків).</summary>
    public static class RingChances
    {
        public static int[] Percent(int[] chances36)
        {
            int n = chances36.Length;
            var res = new int[n];
            int total = 0;
            for (int i = 0; i < n; i++) total += chances36[i];
            if (total <= 0) return res;
            var rem = new int[n];
            int sum = 0;
            for (int i = 0; i < n; i++)
            {
                res[i] = chances36[i] * 100 / total;
                rem[i] = chances36[i] * 100 % total;
                sum += res[i];
            }
            while (sum < 100)
            {
                int best = 0;
                for (int i = 1; i < n; i++) if (rem[i] > rem[best]) best = i;
                res[best]++;
                rem[best] = -1;
                sum++;
            }
            return res;
        }

        /// <summary>Знак модифікатора: «+1», «−1», «0».</summary>
        public static string Signed(int v)
        {
            if (v > 0) return "+" + v.ToString(CultureInfo.InvariantCulture);
            if (v < 0) return "−" + (-v).ToString(CultureInfo.InvariantCulture);
            return "0";
        }
    }
}
