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

        // Сколько очков СКИЛОВ даётся за уровень (распределяется по ростовому
        // профилю архетипа). Атрибуты уровень не трогает — US-2.1: их поднимает
        // только крафт аугмента, иначе поздняя игра упирается в потолок 1–10.
        public int SkillPointsPerLevel = 3;

        // ---- Работа на базе ----
        // Базовый опыт роли за один цикл (день) активного назначения.
        public int RoleXpPerCycle = 20;

        // Множитель опыта, если напарник хорошо подходит роли. Применяется,
        // когда профильный скил слота >= SkillMatchThreshold. Порог осмыслен:
        // половина мастерства на единственной шкале 0–10.
        public int SkillMatchThreshold = 5;
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

        /// <summary>Множитель выработки в день после голодного (Поправка №4).</summary>
        public double HungryProductionMultiplier = 0.5;

        /// <summary>Множитель ролевого опыта в день после голодного (Поправка №4).</summary>
        public double HungryRoleXpMultiplier = 0.5;

        // ---- Производные от атрибутов (GDD Э2.1, числа — Приложение Б) ----
        // Все плейсхолдеры. Считаются один раз в DerivedStats и дальше живут
        // в агрегаторе как обычные статы, чтобы перки и гир могли их менять.

        public double HpBase = 6.0;
        public double HpPerStrength = 1.0;
        public double ApBase = 8.0;
        public double ApPerAgilityStep = 3.0;
        public double AccuracyPerAgility = 1.0;
        public double DefenseBase = 0.0;
        public double DefensePerAgility = 1.0;
        public double InitiativeBase = 0.0;
        public double InitiativePerAgility = 1.0;
        public double InitiativePerWits = 1.0;
        public double CritBase = 5.0;
        public double CritPerWits = 1.0;
        public double CarryBase = 10.0;
        public double CarryPerStrength = 2.0;
        public double StatusDurationReductionPerWill = 0.25;
        public double MoveApPerTileBase = 1.0;

        /// <summary>Сколько трейтов держится активными одновременно (US-2.4, ПЛЕЙСХОЛДЕР).</summary>
        public int TraitSlots = 4;

        /// <summary>Границы шкал: атрибуты 1–10, скилы 0–10.</summary>
        public int MinAttribute = 1;
        public int MaxAttribute = 10;
        public int MaxSkillLevel = 10;

        // ---- Вылазки (Э6.2, Приложение А) ----
        // Заглушка данжа — единственный кран материалов: город их не
        // производит вовсе. Здесь же живёт ось Поправки №1 «время против риска».

        /// <summary>Размер отряда. Больше четырёх на точку не ходит (US-8.3).</summary>
        public int ExpeditionPartyMax = 4;

        /// <summary>Каждая полоса исхода над Базовой добавляет столько долей базы.</summary>
        public double ExpeditionYieldPerBand = 0.5;

        /// <summary>Каждая отработка точки срезает добычу на эту долю.</summary>
        public double ExpeditionDepletionStep = 0.25;

        /// <summary>Ниже этого множителя истощение не опускает точку.</summary>
        public double ExpeditionDepletionFloor = 0.25;

        /// <summary>Ранений при силовом подходе: провал и обычный исход.</summary>
        public int ExpeditionForcefulWoundsOnWorst = 2;
        public int ExpeditionForcefulWoundsOnBase = 1;

        /// <summary>Тихий путь ранит только при провале — и только одного.</summary>
        public int ExpeditionQuietWoundsOnWorst = 1;

        /// <summary>Во сколько очков восстановления обходится лёгкая рана.</summary>
        public double ExpeditionLightWoundPoints = 20.0;

        /// <summary>То же для серьёзной. По US-4.2 именно она оставляет шрам.</summary>
        public double ExpeditionSeriousWoundPoints = 60.0;

        // ---- Городской слой (Поправка №3) ----
        // Секции вынесены в отдельные классы: у каждой свой SO-ассет,
        // чтобы дизайнер правил их независимо и в Play-режиме (US-18.3).

        /// <summary>Скрытая шкала «Напряжение»: пороги полос, тик, белый список драйверов.</summary>
        public TensionBalance Tension = new TensionBalance();

        /// <summary>Слой сигналов: бюджет внимания и правило «нет немого перехода».</summary>
        public SignalBalance Signals = new SignalBalance();

        /// <summary>Детерминированные проверки и полосы исхода.</summary>
        public CheckBalance Checks = new CheckBalance();

        /// <summary>Накопители давления вместо броска кубика.</summary>
        public PulseBalance Pulse = new PulseBalance();
    }
}
