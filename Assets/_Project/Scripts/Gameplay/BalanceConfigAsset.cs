using Game.Core.Balance;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// ScriptableObject-обёртка над <see cref="BalanceConfig"/>. Позволяет
    /// геймдизайнеру крутить весь баланс прямо в инспекторе Unity и иметь
    /// несколько пресетов (например, Easy/Normal/Hard) как отдельные ассеты.
    ///
    /// Создание: ПКМ в Project → Create → Alpha → Balance Config.
    /// </summary>
    [CreateAssetMenu(fileName = "BalanceConfig", menuName = "Alpha/Balance Config", order = 0)]
    public sealed class BalanceConfigAsset : ScriptableObject
    {
        [Header("Прокачка")]
        public double xpBase = 100.0;
        public double xpExponent = 1.5;
        public int maxLevel = 20;
        public int statPointsPerLevel = 3;

        [Header("Работа на базе")]
        public int roleXpPerCycle = 20;
        public int aptitudeMatchThreshold = 5;
        public double wellSuitedXpMultiplier = 1.5;

        [Header("Производство")]
        public double globalProductionMultiplier = 1.0;
        public double injuredProductionMultiplier = 0.5;

        [Header("Восстановление")]
        public double baseHealingPerCycle = 5.0;
        public double healingPerMedicinePoint = 2.0;

        [Header("Содержание")]
        public int foodUpkeepPerCompanion = 1;

        [Header("Городской слой (Поправка №3)")]
        [Tooltip("Необязательно. Если пусто — берутся значения по умолчанию из кода.")]
        public TensionBalanceAsset tension;
        public SignalBalanceAsset signals;

        /// <summary>Преобразует ассет в чистый конфиг для игровой логики.</summary>
        public BalanceConfig ToConfig()
        {
            return new BalanceConfig
            {
                XpBase = xpBase,
                XpExponent = xpExponent,
                MaxLevel = maxLevel,
                StatPointsPerLevel = statPointsPerLevel,
                RoleXpPerCycle = roleXpPerCycle,
                AptitudeMatchThreshold = aptitudeMatchThreshold,
                WellSuitedXpMultiplier = wellSuitedXpMultiplier,
                GlobalProductionMultiplier = globalProductionMultiplier,
                InjuredProductionMultiplier = injuredProductionMultiplier,
                BaseHealingPerCycle = baseHealingPerCycle,
                HealingPerMedicinePoint = healingPerMedicinePoint,
                FoodUpkeepPerCompanion = foodUpkeepPerCompanion,

                Tension = tension != null ? tension.ToConfig() : new Game.Core.Balance.TensionBalance(),
                Signals = signals != null ? signals.ToConfig() : new Game.Core.Balance.SignalBalance()
            };
        }
    }
}
