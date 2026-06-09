using System;
using System.Collections.Generic;

namespace Game.Core.Stats
{
    /// <summary>
    /// Контейнер значений характеристик. Sparse: хранит только заданные статы,
    /// отсутствующие считаются нулём. Поддерживает сложение/масштабирование,
    /// что удобно для наложения бонусов экипировки, трейтов и временных эффектов.
    /// </summary>
    [Serializable]
    public sealed class StatBlock
    {
        private readonly Dictionary<StatType, int> _values;

        public StatBlock()
        {
            _values = new Dictionary<StatType, int>();
        }

        public StatBlock(IEnumerable<KeyValuePair<StatType, int>> values) : this()
        {
            if (values == null) return;
            foreach (var kv in values)
                Set(kv.Key, kv.Value);
        }

        public IReadOnlyDictionary<StatType, int> Values => _values;

        public int Get(StatType stat)
        {
            return _values.TryGetValue(stat, out var v) ? v : 0;
        }

        public void Set(StatType stat, int value)
        {
            if (stat == StatType.None) return;
            if (value == 0)
            {
                _values.Remove(stat);
                return;
            }
            _values[stat] = value;
        }

        public void Add(StatType stat, int delta)
        {
            if (stat == StatType.None || delta == 0) return;
            Set(stat, Get(stat) + delta);
        }

        /// <summary>Прибавляет все статы другого блока поверх текущего.</summary>
        public void AddFrom(StatBlock other)
        {
            if (other == null) return;
            foreach (var kv in other._values)
                Add(kv.Key, kv.Value);
        }

        /// <summary>Возвращает новый блок = this + other (this не меняется).</summary>
        public StatBlock Plus(StatBlock other)
        {
            var result = Clone();
            result.AddFrom(other);
            return result;
        }

        public StatBlock Clone()
        {
            var clone = new StatBlock();
            foreach (var kv in _values)
                clone._values[kv.Key] = kv.Value;
            return clone;
        }
    }
}
