using System;

namespace Game.Core.Balance
{
    /// <summary>
    /// Единая точка настройки баланса (GDD §18 US-18.3). Все «магические числа»
    /// собраны здесь, чтобы крутить экономику, прокачку и бой, не трогая логику.
    /// В Unity оборачивается ScriptableObject (BalanceConfigAsset), но сам тип —
    /// чистый C#, поэтому тестируется и читается без движка.
    ///
    /// Числа — стартовые ПЛЕЙСХОЛДЕРЫ из Приложения Б GDD; тюнятся в плейтесте.
    /// </summary>
    [Serializable]
    public sealed class BalanceConfig
    {
        // ======== Прокачка (XP / уровни) ========
        // Порог опыта до следующего уровня: XpBase * (level ^ XpExponent).
        public double XpBase = 100.0;
        public double XpExponent = 1.5;
        public int MaxLevel = 20;

        // Очки скилов за уровень — игрок распределяет сам (классов нет, респека нет).
        public int SkillPointsPerLevel = 3;

        // ======== Создание персонажа / трейты ========
        // Фикс число активных трейтов-слотов (US-2.4). Шрамы слоты НЕ занимают.
        public int TraitSlots = 4;

        // ======== Производные из атрибутов (база ДО модификаторов) ========
        // Мелкие числа: HP ~6–20, AP ~8–10 (Прил. Б).
        public double HpBase = 4.0;            // HP = HpBase + Strength*HpPerStrength
        public double HpPerStrength = 2.0;     //  → Strength 1..8 даёт HP 6..20

        public double ApBase = 8.0;            // AP = round(ApBase + Agility*ApPerAgility)
        public double ApPerAgility = 0.2;      //  → Agility 0..10 даёт AP 8..10

        public double AccuracyBase = 50.0;     // базовая точность, % (скил оружия добавляется в бою)
        public double AccuracyPerAgility = 2.0;

        public double DefenseBase = 0.0;       // снижает шанс попасть по юниту, %
        public double DefensePerAgility = 1.0;

        public double InitiativePerAgility = 1.0; // инициатива = Agility*.. + Wits*..
        public double InitiativePerWits = 1.0;

        public double CarryBase = 4.0;
        public double CarryPerStrength = 1.0;

        public double CritBase = 5.0;          // базовый шанс крита, %
        public double CritPerAgility = 1.0;

        public double ResolvePerWill = 1.0;    // сокращение длительности состояний (от Воли)

        // ======== Бой (WL3) — ПЛЕЙСХОЛДЕРЫ для боевой итерации (Прил. Б) ========
        // Используются при реализации Эпика 3; сейчас живут как данные и в доках.
        public int CoverHalfHitPenalty = 20;   // −% к шансу попасть из-за полуукрытия
        public int CoverFullHitPenalty = 40;   // −% из-за полного укрытия
        public int GrazeThresholdPercent = 15; // промах в пределах гразы (≤15%) даёт частичный урон
        public int GrazePartialPercent = 50;   // граза = 50% урона
        public int HighHitNoFullMiss = 85;     // ≥85% не может слиться полностью
        public int StrikePerHit = 1;           // Strike-метр: +1 за попадание
        public int StrikeGuaranteeAt = 3;      //  → гарантированный точный удар при 3
        public int ArmorFlatMin = 1;           // плоская броня очень мелкая (1–2), чтобы не обнулять урон
        public int ArmorFlatMax = 2;
        public int DotDamagePerTurn = 2;       // Поджог/Яд: 2–3 урона/ход
        public int DotDurationTurns = 3;       //  × 2–3 хода
        public double VulnerabilityMultiplier = 1.5; // ×1.25–1.5 по уязвимости
        public double ResistMultiplier = 0.5;        // ×0.5–0.75 по резисту
        public int SquadSize = 4;              // отряд из 4 (US-3.5)
        public int DownWindowTurns = 2;        // окно на стабилизацию при дауне (US-4.1)

        // -- Реализация боя (ядро WL3) --
        public int MoveApCostPerTile = 1;          // движение ~1 AP/клетка (Прил. Б)
        public int AccuracyPerWeaponSkill = 3;     // +точность за очко скила оружия
        public int DistancePenaltyPerTile = 5;     // −% за тайл дальше оптимала оружия
        public int HitChanceMin = 1;               // клампы шанса до правил пола/гразы
        public int HitChanceMax = 99;
        public int StabilizeApCost = 4;            // стабилизация дауна (активка Медицины)
        public int ResolvePerStatusTurnReduction = 3;   // каждые N Resolve → −1 ход состояния (мин 1)
        public int SuppressionAccuracyPenalty = 15;     // Подавление: −точность
        public double SuppressionMoveCostMultiplier = 1.5; // Подавление: движение дороже (округление вверх)
        public int MarkedHitBonus = 10;                 // Метка: +шанс попасть по цели
        public int KnockdownDefensePenalty = 10;        // Сбит с ног: −защита цели (не ниже 0)
        public int StandUpApCost = 2;                   // Сбит с ног: встать в начале хода стоит AP

        // ======== Кампания / экспедиции ========
        // Айронмен (US-16.1): протагонист теряет сюжетную защиту — его смерть = game over.
        public bool Ironman = false;
        // Доля HP, ниже которой выживший после боя получает Лёгкое ранение (без шрама).
        public double LightInjuryHpFraction = 0.5;
        // XP каждому выжившему за победную вылазку (US-5.1: опыт с боёв).
        public int XpPerExpeditionVictory = 80;

        // ======== Напарники: драма ростера (Эпик 9) ========
        public int MournLoyaltyHit = 8;          // соратник скорбит по павшему (рябь по связям, US-9.6)
        public int NeutralDeathLoyaltyHit = 2;   // нейтральный — лёгкая просадка
        public int RivalDeathLoyaltyRelief = 2;  // соперник не скорбит (лёгкое облегчение)
        public int BetrayalKinLoyaltyHit = 10;   // соратник предателя чувствует себя преданным
        public int MaxRippleTargets = 3;         // ограждение от каскада: не более N тяжёлых откликов на событие

        // ======== Ранения и восстановление (Эпик 4) ========
        // 3 тира ранений → дни лечения (2/5/10).
        public int InjuryDaysLight = 2;
        public int InjuryDaysSerious = 5;
        public int InjuryDaysCritical = 10;
        // Шрам присваивается только Серьёзным+ ранением (US-2.5/4.2).
        public bool ScarOnSeriousOrAbove = true;
        // Скорость лечения «дней восстановления» за один игровой день.
        public double NaturalRecoveryPerDay = 1.0;        // тикает даже без лазарета
        public double InfirmaryRecoveryPerDay = 1.0;      // лазарет с медиком на посту
        public double MedicRecoveryPerSkillPoint = 0.2;   // + от скила Медицина медика

        // ======== Скрытые шкалы (Эпик 11) ========
        // «Напряжение» (внутреннее). Игроку число НЕ показывается (US-17.2) —
        // наружу идут только качественные полосы и фоновые сигналы.
        public double TensionPerDayPerTier = 0.2; // фоновый тик: тир города × N в день (мягко, US-1.3)
        public double TensionUneasyAt = 25;       // полосы: Calm < Uneasy < Tense < Critical
        public double TensionTenseAt = 50;
        public double TensionCriticalAt = 75;
        public double IncidentChanceBase = 4;       // % шанс инцидента в день при нулевом Напряжении
        public double IncidentChancePerTension = 0.4; // +% за пункт Напряжения (частота растёт, US-11.1)
        public double CrisisExodusPopulation = 3;   // кризис «отток»: −население
        public int MaxCityTier = 4;                 // тиров города 3–4 (US-7.6)

        // «Готовность» (внешнее/финал, US-11.4). Тоже скрыта.
        public double ReadinessPerPreparation = 10; // действие совета «Подготовка к угрозе» (вход)
        public double ReadinessPerFortification = 15; // вклад построенных Укреплений
        public double ReadinessBracedAt = 25;       // полосы: Unprepared < Braced < Fortified
        public double ReadinessFortifiedAt = 60;

        // ======== База: население и стройка ========
        public double PopulationGrowthPerDay = 0.25;  // пассивный рост (US-7.5); ускоряется Таверной/жильём
        public int ConstructionSmallDays = 4;         // малая стройка 3–5 дней (Прил. Б)
        public int ConstructionLargeDays = 10;        // крупная 8–12 дней
        public int ConstructionStages = 5;            // 5 визуальных стадий (US-7.3)

        /// <summary>Поверхностная копия (все поля — значимые типы), чтобы не мутировать ассет-пресет.</summary>
        public BalanceConfig Clone() => (BalanceConfig)MemberwiseClone();
    }
}
