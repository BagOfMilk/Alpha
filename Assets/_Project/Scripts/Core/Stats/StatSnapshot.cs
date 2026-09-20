using System;
using System.Collections.Generic;

namespace Game.Core.Stats
{
    /// <summary>
    /// Посчитанные статы персонажа на момент времени: база плюс все модификаторы,
    /// свёрнутые один раз. Всё, что читает числа персонажа — формулы базы, бой,
    /// проверки — работает со снапшотом, а не с сырыми полями.
    /// </summary>
    public sealed class StatSnapshot
    {
        private readonly Dictionary<StatKey, double> _values;
        private readonly Dictionary<StatKey, List<StatModifier>> _sources;

        internal StatSnapshot(Dictionary<StatKey, double> values,
            Dictionary<StatKey, List<StatModifier>> sources)
        {
            _values = values ?? new Dictionary<StatKey, double>();
            _sources = sources ?? new Dictionary<StatKey, List<StatModifier>>();
        }

        /// <summary>Неизвестный ключ — ноль, а не исключение: контент data-driven.</summary>
        public double Get(StatKey key) => _values.TryGetValue(key, out var v) ? v : 0.0;

        /// <summary>Целое значение. Округление к чётному — как везде в проекте.</summary>
        public int GetInt(StatKey key) => (int)Math.Round(Get(key), MidpointRounding.ToEven);

        public int Attribute(AttributeType a) => GetInt(StatKeys.Of(a));
        public int Skill(SkillType s) => GetInt(StatKeys.Of(s));
        public double Derived(DerivedStat d) => Get(StatKeys.Of(d));

        private static readonly StatModifier[] NoModifiers = new StatModifier[0];

        /// <summary>Из чего сложилось значение — для тултипа и для аудита двойного счёта.</summary>
        public IReadOnlyList<StatModifier> SourcesOf(StatKey key) =>
            _sources.TryGetValue(key, out var list) ? (IReadOnlyList<StatModifier>)list : NoModifiers;
    }
}
