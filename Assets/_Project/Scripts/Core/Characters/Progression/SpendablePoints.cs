using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.Core.Loop;

namespace Game.Core.Characters.Progression
{
    /// <summary>
    /// Банк очок скілів, які ще НЕ витрачені (R11/G19). Протагоніст банкує їх
    /// замість авто-витрати (<see cref="Companion.GainXpNoAutoSpend"/>), а
    /// планувальник білда (<c>BuildPlanner.Preview/Commit</c>) бере
    /// <c>pointsAvailable</c> звідси — API планувальника вже готове до цього
    /// (він і раніше брав це число параметром, тому не змінюється).
    ///
    /// Мапа id→очки, а не одне число: у майбутньому банкувати може не лише
    /// протагоніст (наприклад, напарник з відповідним трейтом), тож ключ —
    /// companionId з самого початку, а не здогад «точно один банк на гру».
    /// </summary>
    public sealed class SpendablePoints : IStateBlob
    {
        private readonly Dictionary<string, int> _points = new Dictionary<string, int>(StringComparer.Ordinal);

        public int Get(string companionId)
        {
            if (string.IsNullOrEmpty(companionId)) return 0;
            return _points.TryGetValue(companionId, out var n) ? n : 0;
        }

        /// <summary>Кладёт очки на банк. Отрицательное/нулевое amount игнорируется.</summary>
        public void Grant(string companionId, int amount)
        {
            if (string.IsNullOrEmpty(companionId) || amount <= 0) return;
            _points[companionId] = Get(companionId) + amount;
        }

        /// <summary>
        /// Списывает очки. Атомарно: если их не хватает, банк не трогается и
        /// возвращается false — так же, как <c>ResourceLedger.TrySpend</c>
        /// не оставляет кошелёк в промежуточном состоянии при отказе.
        /// </summary>
        public bool Spend(string companionId, int amount)
        {
            if (string.IsNullOrEmpty(companionId) || amount < 0) return false;
            int have = Get(companionId);
            if (have < amount) return false;

            if (amount == 0) return true;
            _points[companionId] = have - amount;
            return true;
        }

        // ---- слепок ----

        public string CaptureState()
        {
            var keys = new List<string>(_points.Keys);
            keys.Sort(StringComparer.Ordinal);

            var sb = new StringBuilder();
            for (int i = 0; i < keys.Count; i++)
            {
                if (_points[keys[i]] == 0) continue; // ноль восстановится дефолтом — не стоит хранить
                if (sb.Length > 0) sb.Append(',');
                sb.Append(keys[i]).Append(':').Append(_points[keys[i]].ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        public void RestoreState(string blob)
        {
            _points.Clear();
            if (string.IsNullOrEmpty(blob)) return;

            foreach (var entry in blob.Split(','))
            {
                int colon = entry.IndexOf(':');
                if (colon <= 0) continue;

                int n;
                if (!int.TryParse(entry.Substring(colon + 1), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out n)) continue;
                _points[entry.Substring(0, colon)] = n;
            }
        }
    }
}
