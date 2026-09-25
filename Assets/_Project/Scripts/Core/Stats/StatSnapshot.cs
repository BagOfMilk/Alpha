using System;
using System.Collections.Generic;

namespace Game.Core.Stats
{
    /// <summary>
    /// Пораховані стати персонажа на момент часу: база плюс усі модифікатори,
    /// згорнуті один раз. Усе, що читає числа персонажа — формули бази, бій,
    /// перевірки — працює зі знімком, а не з сирими полями.
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

        /// <summary>Невідомий ключ — нуль, а не виняток: контент data-driven.</summary>
        public double Get(StatKey key) => _values.TryGetValue(key, out var v) ? v : 0.0;

        /// <summary>Ціле значення. Округлення до парного — як усюди в проєкті.</summary>
        public int GetInt(StatKey key) => (int)Math.Round(Get(key), MidpointRounding.ToEven);

        public int Attribute(AttributeType a) => GetInt(StatKeys.Of(a));
        public int Skill(SkillType s) => GetInt(StatKeys.Of(s));
        public double Derived(DerivedStat d) => Get(StatKeys.Of(d));

        private static readonly StatModifier[] NoModifiers = new StatModifier[0];

        /// <summary>З чого склалося значення — для тултіпа і для аудиту подвійного рахунку.</summary>
        public IReadOnlyList<StatModifier> SourcesOf(StatKey key) =>
            _sources.TryGetValue(key, out var list) ? (IReadOnlyList<StatModifier>)list : NoModifiers;
    }
}
