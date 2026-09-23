using System.Collections.Generic;
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
    public sealed class SiteLedger
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
    }
}
