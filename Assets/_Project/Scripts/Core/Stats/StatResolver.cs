using System.Collections.Generic;
using Game.Core.Balance;

namespace Game.Core.Stats
{
    /// <summary>
    /// Єдиний агрегатор модифікаторів (US-18.2). Рівно одне місце, де числа
    /// персонажа складаються — тому подвійний рахунок неможливий структурно, а не
    /// дисципліною: кожен ефект приходить одним StatModifier від одного
    /// провайдера.
    ///
    /// Порядок згортки (зафіксований тестом):
    ///     (база + Σ Flat) × (1 + Σ PercentAdd) × Π Multiplier
    /// Override перебиває все. Порядок вставки на результат не впливає.
    /// </summary>
    public static class StatResolver
    {
        public static StatSnapshot Resolve(AttributeSet attrs, SkillSet skills,
            IEnumerable<IModifierProvider> providers, BalanceConfig cfg)
        {
            var baseLayer = new Dictionary<StatKey, double>();

            if (attrs != null)
                for (int i = 0; i < Attributes.All.Length; i++)
                {
                    var a = Attributes.All[i];
                    baseLayer[StatKeys.Of(a)] = attrs[a];
                }

            if (skills != null)
                for (int i = 0; i < Skills.All.Length; i++)
                {
                    var s = Skills.All[i];
                    baseLayer[StatKeys.Of(s)] = skills[s];
                }

            if (attrs != null && cfg != null)
                DerivedStats.SeedInto(baseLayer, attrs, cfg);

            var mods = new List<StatModifier>();
            if (providers != null)
                foreach (var p in providers)
                    p?.CollectModifiers(mods);

            var sources = new Dictionary<StatKey, List<StatModifier>>();
            for (int i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                if (m.Key == StatKey.None) continue;
                if (!sources.TryGetValue(m.Key, out var list))
                {
                    list = new List<StatModifier>();
                    sources[m.Key] = list;
                }
                list.Add(m);
            }

            var values = new Dictionary<StatKey, double>();
            foreach (var kv in baseLayer) values[kv.Key] = kv.Value;
            foreach (var kv in sources) if (!values.ContainsKey(kv.Key)) values[kv.Key] = 0.0;

            var keys = new List<StatKey>(values.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                double baseValue = baseLayer.TryGetValue(key, out var b) ? b : 0.0;
                values[key] = Fold(baseValue, sources.TryGetValue(key, out var list) ? list : null);
            }

            return new StatSnapshot(values, sources);
        }

        private static double Fold(double baseValue, List<StatModifier> mods)
        {
            if (mods == null || mods.Count == 0) return baseValue;

            double flat = 0, percent = 0, mult = 1;
            bool hasOverride = false;
            double overrideValue = 0;

            for (int i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                switch (m.Mode)
                {
                    case ModMode.Flat: flat += m.Value; break;
                    case ModMode.PercentAdd: percent += m.Value; break;
                    case ModMode.Multiplier: mult *= m.Value; break;
                    case ModMode.Override:
                        // Останній Override перемагає — але порядок провайдерів
                        // стабільний, тому результат відтворюваний.
                        hasOverride = true;
                        overrideValue = m.Value;
                        break;
                }
            }

            if (hasOverride) return overrideValue;
            return (baseValue + flat) * (1.0 + percent) * mult;
        }
    }
}
