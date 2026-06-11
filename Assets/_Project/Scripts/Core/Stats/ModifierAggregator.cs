using System;
using System.Collections.Generic;

namespace Game.Core.Stats
{
    /// <summary>
    /// Единый пайплайн модификаторов (GDD §18 US-18.2). Сворачивает базу
    /// (производные из атрибутов) с модификаторами гира/трейтов/шрамов/состояний/
    /// баффов по ключу <see cref="DerivedStat"/>. Один проход — нет двойного счёта,
    /// новый стат подключается в одном месте.
    ///
    /// Итог = round( (база + Σ flat) × (1 + Σ percent/100) ), не ниже нуля.
    /// </summary>
    public static class ModifierAggregator
    {
        /// <summary>Свернуть один производный стат: база + релевантные модификаторы.</summary>
        public static int Resolve(DerivedStat stat, double baseValue, IEnumerable<StatModifier> modifiers)
        {
            double flat = 0;
            double percent = 0;
            if (modifiers != null)
            {
                foreach (var m in modifiers)
                {
                    if (m.Stat != stat) continue;
                    if (m.Mode == ModMode.Flat) flat += m.Value;
                    else percent += m.Value;
                }
            }

            double result = (baseValue + flat) * (1.0 + percent / 100.0);
            if (result < 0) result = 0;
            return (int)Math.Round(result);
        }

        /// <summary>Свернуть сразу все производные статы из словаря баз.</summary>
        public static Dictionary<DerivedStat, int> ResolveAll(
            IReadOnlyDictionary<DerivedStat, double> baseValues, IEnumerable<StatModifier> modifiers)
        {
            var result = new Dictionary<DerivedStat, int>();
            if (baseValues == null) return result;

            // Материализуем модификаторы один раз, чтобы не перебирать ленивый источник по разу на стат.
            List<StatModifier> mods = modifiers != null ? new List<StatModifier>(modifiers) : null;
            foreach (var kv in baseValues)
                result[kv.Key] = Resolve(kv.Key, kv.Value, mods);
            return result;
        }
    }
}
