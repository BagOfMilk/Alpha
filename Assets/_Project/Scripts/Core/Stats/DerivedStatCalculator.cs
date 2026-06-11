using System;
using System.Collections.Generic;
using Game.Core.Balance;

namespace Game.Core.Stats
{
    /// <summary>
    /// Считает БАЗОВЫЕ производные статы из четырёх атрибутов по коэффициентам
    /// <see cref="BalanceConfig"/> (GDD §2.1). Возвращает базу ДО модификаторов —
    /// поверх неё гир/трейты/шрамы/состояния накладываются через
    /// <see cref="ModifierAggregator"/>. Атрибут → число считается ровно здесь
    /// («один эффект — одна система»).
    /// </summary>
    public static class DerivedStatCalculator
    {
        public static double BaseValue(DerivedStat stat, AttributeBlock attr, BalanceConfig cfg)
        {
            if (attr == null || cfg == null) return 0;
            switch (stat)
            {
                case DerivedStat.MaxHp:        return cfg.HpBase + attr.Strength * cfg.HpPerStrength;
                case DerivedStat.ActionPoints: return cfg.ApBase + attr.Agility * cfg.ApPerAgility;
                case DerivedStat.Accuracy:     return cfg.AccuracyBase + attr.Agility * cfg.AccuracyPerAgility;
                case DerivedStat.Defense:      return cfg.DefenseBase + attr.Agility * cfg.DefensePerAgility;
                case DerivedStat.Initiative:   return attr.Agility * cfg.InitiativePerAgility + attr.Wits * cfg.InitiativePerWits;
                case DerivedStat.Carry:        return cfg.CarryBase + attr.Strength * cfg.CarryPerStrength;
                case DerivedStat.CritChance:   return cfg.CritBase + attr.Agility * cfg.CritPerAgility;
                case DerivedStat.Armor:        return 0; // база 0; броня приходит от гира (модификатор)
                case DerivedStat.Resolve:      return attr.Will * cfg.ResolvePerWill;
                default:                       return 0;
            }
        }

        /// <summary>Все производные статы базой (до модификаторов), для агрегатора.</summary>
        public static Dictionary<DerivedStat, double> BaseValues(AttributeBlock attr, BalanceConfig cfg)
        {
            var result = new Dictionary<DerivedStat, double>();
            foreach (DerivedStat stat in Enum.GetValues(typeof(DerivedStat)))
            {
                if (stat == DerivedStat.None) continue;
                result[stat] = BaseValue(stat, attr, cfg);
            }
            return result;
        }
    }
}
