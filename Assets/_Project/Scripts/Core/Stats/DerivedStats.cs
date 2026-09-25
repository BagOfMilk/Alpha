using System.Collections.Generic;
using Game.Core.Balance;

namespace Game.Core.Stats
{
    /// <summary>
    /// Єдине місце, де атрибут перетворюється на похідну.
    ///
    /// Похідні кладуться в БАЗОВИЙ шар агрегатора, а не рахуються в місці
    /// використання. Завдяки цьому перк «+3 HP», гір «+1 броня» і стан
    /// «−20 точності» лягають поверх них звичайними модифікаторами, і бойові
    /// формули не знають про атрибути взагалі.
    ///
    /// Числа — з Додатку Б GDD, усі плейсхолдери, усі в BalanceConfig.
    /// </summary>
    public static class DerivedStats
    {
        public static void SeedInto(Dictionary<StatKey, double> baseLayer, AttributeSet attrs, BalanceConfig cfg)
        {
            if (baseLayer == null || attrs == null || cfg == null) return;

            int str = attrs[AttributeType.Strength];
            int agi = attrs[AttributeType.Agility];
            int wit = attrs[AttributeType.Wits];
            int wil = attrs[AttributeType.Will];

            // HP майже плоский за атрибутами: US-5.2 вимагає, щоб живучість росла
            // перками і гіром, інакше пізня гра впирається в статичні атрибути.
            Set(baseLayer, DerivedStat.MaxHp, cfg.HpBase + str * cfg.HpPerStrength);

            // Пул AP 8–10 (Дод. Б): спритність додає ступенями, а не лінійно.
            Set(baseLayer, DerivedStat.MaxAp, cfg.ApBase + (cfg.ApPerAgilityStep > 0 ? (int)(agi / cfg.ApPerAgilityStep) : 0));

            Set(baseLayer, DerivedStat.Accuracy, agi * cfg.AccuracyPerAgility);
            Set(baseLayer, DerivedStat.Defense, cfg.DefenseBase + agi * cfg.DefensePerAgility);
            Set(baseLayer, DerivedStat.Initiative, cfg.InitiativeBase + agi * cfg.InitiativePerAgility + wit * cfg.InitiativePerWits);
            Set(baseLayer, DerivedStat.CritChance, cfg.CritBase + wit * cfg.CritPerWits);
            Set(baseLayer, DerivedStat.CarryCapacity, cfg.CarryBase + str * cfg.CarryPerStrength);

            // Воля скорочує тривалість станів, включно з DoT (GDD Е3.1).
            Set(baseLayer, DerivedStat.StatusDurationReduction, wil * cfg.StatusDurationReductionPerWill);

            // Броня і бонус шкоди — чисті канали гіру: база нуль, усе приходить
            // модифікаторами. Ключі заведені, щоб модифікатору було куди лягти.
            Set(baseLayer, DerivedStat.Armor, 0);
            Set(baseLayer, DerivedStat.DamageBonus, 0);
            Set(baseLayer, DerivedStat.MoveApPerTile, cfg.MoveApPerTileBase);
        }

        private static void Set(Dictionary<StatKey, double> layer, DerivedStat stat, double value)
        {
            layer[StatKeys.Of(stat)] = value;
        }
    }
}
