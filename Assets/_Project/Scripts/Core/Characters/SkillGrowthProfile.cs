using System;
using System.Collections.Generic;
using Game.Core.Stats;

namespace Game.Core.Characters
{
    /// <summary>
    /// Ростовой профиль архетипа: веса, по которым очки уровня ложатся на скилы.
    ///
    /// Именно на СКИЛЫ, а не на статы вообще: по GDD Э2.1 атрибуты почти
    /// статичны и поднимаются только крафтом аугмента, поэтому уровню больше
    /// нечего раздавать. Раньше один профиль кормил и боевые статы, и ролевые
    /// склонности — отсюда и брался перекос шкал.
    ///
    /// Веса нормируются: важны пропорции, а не абсолютные значения.
    /// </summary>
    [Serializable]
    public sealed class SkillGrowthProfile
    {
        private readonly List<KeyValuePair<SkillType, double>> _weights
            = new List<KeyValuePair<SkillType, double>>();

        public void SetWeight(SkillType skill, double weight)
        {
            if (skill == SkillType.None || weight <= 0) return;
            _weights.Add(new KeyValuePair<SkillType, double>(skill, weight));
        }

        public IReadOnlyList<KeyValuePair<SkillType, double>> Weights => _weights;

        /// <summary>
        /// Детерминированно распределяет <paramref name="points"/> очков по скилам
        /// согласно весам. Метод наибольших остатков (Хэйра): сумма выданных очков
        /// точно равна points, без дробей и накопления ошибки округления.
        ///
        /// Возвращает ПРИРОСТ, а не итог — зажимать по потолку шкалы будет тот,
        /// кто его применит (см. SkillSet.AddClamped).
        /// </summary>
        public SkillSet AllocatePoints(int points)
        {
            var result = new SkillSet();
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
                var skill = _weights[i].Key;
                result[skill] = result[skill] + whole;
                assigned += whole;
                remainders.Add(new KeyValuePair<int, double>(i, exact - whole));
            }

            // Раздаём оставшиеся очки тем, у кого наибольший дробный остаток.
            int leftover = points - assigned;
            remainders.Sort((a, b) => b.Value.CompareTo(a.Value));
            for (int i = 0; i < leftover && i < remainders.Count; i++)
            {
                var skill = _weights[remainders[i].Key].Key;
                result[skill] = result[skill] + 1;
            }

            return result;
        }
    }
}
