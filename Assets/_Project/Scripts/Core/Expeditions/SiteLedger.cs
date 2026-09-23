using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Core.Balance;

namespace Game.Core.Expeditions
{
    /// <summary>
    /// Сколько раз точку уже отрабатывали. Каждая отработка срезает добычу на
    /// фиксированный шаг — до пола, ниже которого не падает.
    ///
    /// Это не «рандом на сложности», а детерминированное истощение: одна и та
    /// же точка перестаёт кормить, и отряду приходится искать новую. Ровно тот
    /// же приём, что у повторных обращений по теме в городе — предсказуемое
    /// удорожание вместо случайного отказа.
    /// </summary>
    public sealed class SiteLedger : Loop.IStateBlob
    {
        private readonly Dictionary<string, int> _worked = new Dictionary<string, int>();

        public int TimesWorked(string siteId)
        {
            if (string.IsNullOrEmpty(siteId)) return 0;
            return _worked.TryGetValue(siteId, out var n) ? n : 0;
        }

        public void Register(string siteId)
        {
            if (string.IsNullOrEmpty(siteId)) return;
            _worked[siteId] = TimesWorked(siteId) + 1;
        }

        /// <summary>Множитель добычи с учётом истощения. Считается ДО регистрации ходки.</summary>
        public double YieldMultiplier(string siteId, BalanceConfig cfg)
        {
            if (cfg == null) return 1.0;
            double mult = 1.0 - cfg.ExpeditionDepletionStep * TimesWorked(siteId);
            return mult < cfg.ExpeditionDepletionFloor ? cfg.ExpeditionDepletionFloor : mult;
        }

        // ---- слепок (R15) ----
        //
        // МИНИМАЛЬНО нарочно: только счётчик ходок на точку. Держится маленьким,
        // чтобы B7 мог расширить его своим форматом без переписывания этого —
        // формат «id:n,id:n,...» без вложенных разделителей это позволяет.

        public string CaptureState()
        {
            var keys = new List<string>(_worked.Keys);
            keys.Sort(StringComparer.Ordinal);

            var parts = new List<string>(keys.Count);
            for (int i = 0; i < keys.Count; i++)
                parts.Add(keys[i] + ":" + _worked[keys[i]].ToString(CultureInfo.InvariantCulture));
            return string.Join(",", parts.ToArray());
        }

        public void RestoreState(string blob)
        {
            _worked.Clear();
            if (string.IsNullOrEmpty(blob)) return;

            foreach (var entry in blob.Split(','))
            {
                int colon = entry.IndexOf(':');
                if (colon <= 0) continue;

                int n;
                if (!int.TryParse(entry.Substring(colon + 1), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out n)) continue;
                _worked[entry.Substring(0, colon)] = n;
            }
        }
    }
}
