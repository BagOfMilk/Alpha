using System.Collections.Generic;
using Game.Core.Balance;

namespace Game.Core.Stats
{
    /// <summary>
    /// Единственное место, где атрибут превращается в производную.
    ///
    /// Производные кладутся в БАЗОВЫЙ слой агрегатора, а не считаются в месте
    /// использования. Благодаря этому перк «+3 HP», гир «+1 броня» и состояние
    /// «−20 точности» ложатся поверх них обычными модификаторами, и боевые
    /// формулы не знают об атрибутах вообще.
    ///
    /// Числа — из Приложения Б GDD, все плейсхолдеры, все в BalanceConfig.
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

            // HP почти плоский по атрибутам: US-5.2 требует, чтобы живучесть росла
            // перками и гиром, иначе поздняя игра упирается в статичные атрибуты.
            Set(baseLayer, DerivedStat.MaxHp, cfg.HpBase + str * cfg.HpPerStrength);

            // Пул AP 8–10 (Прил. Б): ловкость добавляет ступенями, а не линейно.
            Set(baseLayer, DerivedStat.MaxAp, cfg.ApBase + (cfg.ApPerAgilityStep > 0 ? (int)(agi / cfg.ApPerAgilityStep) : 0));

            Set(baseLayer, DerivedStat.Accuracy, agi * cfg.AccuracyPerAgility);
            Set(baseLayer, DerivedStat.Defense, cfg.DefenseBase + agi * cfg.DefensePerAgility);
            Set(baseLayer, DerivedStat.Initiative, cfg.InitiativeBase + agi * cfg.InitiativePerAgility + wit * cfg.InitiativePerWits);
            Set(baseLayer, DerivedStat.CritChance, cfg.CritBase + wit * cfg.CritPerWits);
            Set(baseLayer, DerivedStat.CarryCapacity, cfg.CarryBase + str * cfg.CarryPerStrength);

            // Воля сокращает длительность состояний, включая DoT (GDD Э3.1).
            Set(baseLayer, DerivedStat.StatusDurationReduction, wil * cfg.StatusDurationReductionPerWill);

            // Броня и бонус урона — чистые каналы гира: база ноль, всё приходит
            // модификаторами. Ключи заведены, чтобы модификатору было куда лечь.
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
