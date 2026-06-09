using System;
using System.Collections.Generic;
using Game.Core.Stats;

namespace Game.Core.Characters
{
    /// <summary>
    /// Ростовой профиль архетипа: веса распределения очков характеристик при
    /// повышении уровня. Например, у «Снайпера» большой вес Aim, у «Инженера» —
    /// Engineering. Веса нормируются, поэтому абсолютные значения не важны —
    /// важны пропорции.
    /// </summary>
    [Serializable]
    public sealed class GrowthProfile
    {
        private readonly List<KeyValuePair<StatType, double>> _weights
            = new List<KeyValuePair<StatType, double>>();

        public void SetWeight(StatType stat, double weight)
        {
            if (stat == StatType.None || weight <= 0) return;
            _weights.Add(new KeyValuePair<StatType, double>(stat, weight));
        }

        public IReadOnlyList<KeyValuePair<StatType, double>> Weights => _weights;

        /// <summary>
        /// Детерминированно распределяет <paramref name="points"/> очков по статам
        /// согласно весам. Используется метод наибольших остатков (Хэйра), чтобы
        /// сумма выданных очков точно равнялась points без дробей и накопления
        /// ошибки округления.
        /// </summary>
        public StatBlock AllocatePoints(int points)
        {
            var result = new StatBlock();
            if (points <= 0 || _weights.Count == 0) return result;

            double totalWeight = 0;
            for (int i = 0; i < _weights.Count; i++) totalWeight += _weights[i].Value;
            if (totalWeight <= 0) return result;

            // Целые части + сбор остатков.
            var remainders = new List<KeyValuePair<int, double>>(); // index -> остаток
            int assigned = 0;
            for (int i = 0; i < _weights.Count; i++)
            {
                double exact = points * (_weights[i].Value / totalWeight);
                int whole = (int)Math.Floor(exact);
                result.Add(_weights[i].Key, whole);
                assigned += whole;
                remainders.Add(new KeyValuePair<int, double>(i, exact - whole));
            }

            // Раздаём оставшиеся очки тем, у кого наибольший дробный остаток.
            int leftover = points - assigned;
            remainders.Sort((a, b) => b.Value.CompareTo(a.Value));
            for (int i = 0; i < leftover && i < remainders.Count; i++)
                result.Add(_weights[remainders[i].Key].Key, 1);

            return result;
        }
    }
}
