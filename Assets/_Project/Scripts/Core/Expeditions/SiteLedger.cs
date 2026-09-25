using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Core.Balance;

namespace Game.Core.Expeditions
{
    /// <summary>
    /// Скільки разів точку вже відпрацьовували. Кожне відпрацювання зрізає здобич на
    /// фіксований крок — до підлоги, нижче якої не падає.
    ///
    /// Це не «рандом на складність», а детерміноване виснаження: одна й та
    /// сама точка перестає годувати, і відряду доводиться шукати нову. Рівно той
    /// самий прийом, що й у повторних звернень по темі в місті — передбачуване
    /// подорожчання замість випадкової відмови.
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

        /// <summary>Множник здобичі з урахуванням виснаження. Рахується ДО реєстрації ходки.</summary>
        public double YieldMultiplier(string siteId, BalanceConfig cfg)
        {
            if (cfg == null) return 1.0;
            double mult = 1.0 - cfg.ExpeditionDepletionStep * TimesWorked(siteId);
            return mult < cfg.ExpeditionDepletionFloor ? cfg.ExpeditionDepletionFloor : mult;
        }

        // ---- зліпок (R15) ----
        //
        // МІНІМАЛЬНО навмисно: лише лічильник ходок на точку. Тримається маленьким,
        // щоб B7 міг розширити його своїм форматом без переписування цього —
        // формат «id:n,id:n,...» без вкладених роздільників це дозволяє.

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
