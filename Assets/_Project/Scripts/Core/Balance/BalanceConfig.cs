using System;

namespace Game.Core.Balance
{
    /// <summary>
    /// Единая точка настройки баланса. Все «магические числа» собраны здесь,
    /// чтобы можно было крутить экономику и прокачку, не трогая логику.
    /// В Unity это оборачивается ScriptableObject (BalanceConfigAsset), но сам
    /// тип — чистый C#, поэтому тестируется и читается без движка.
    /// </summary>
    [Serializable]
    public sealed class BalanceConfig
    {
        // ---- Прокачка (XP / уровни) ----
        // Порог опыта до следующего уровня: XpBase * (level ^ XpExponent).
        public double XpBase = 100.0;
        public double XpExponent = 1.5;
        public int MaxLevel = 20;

        // Сколько очков характеристик даётся за уровень (распределяется по
        // ростовому профилю архетипа напарника).
        public int StatPointsPerLevel = 3;

        // ---- Работа на базе ----
        // Базовый опыт роли за один цикл (день) активного назначения.
        public int RoleXpPerCycle = 20;

        // Множитель опыта, если стат-склонность напарника хорошо подходит роли.
        // Применяется, когда основной стат слота >= AptitudeMatchThreshold.
        public int AptitudeMatchThreshold = 5;
        public double WellSuitedXpMultiplier = 1.5;

        // ---- Производство ----
        // Глобальный множитель всего производства базы (для общей подкрутки).
        public double GlobalProductionMultiplier = 1.0;

        // Штраф к производству, если у напарника статус "ранен" и он всё ещё
        // назначен (например, лёгкое ранение разрешает работу с пенальти).
        public double InjuredProductionMultiplier = 0.5;

        // ---- Восстановление ----
        // Сколько единиц «здоровья восстановления» снимается с раненого за цикл
        // в лазарете на единицу склонности Medicine назначенного медика.
        public double HealingPerMedicinePoint = 2.0;
        public double BaseHealingPerCycle = 5.0;

        // ---- Содержание / прокорм ----
        // Сколько еды потребляет один член поселения за цикл.
        public int FoodUpkeepPerCompanion = 1;
    }
}
