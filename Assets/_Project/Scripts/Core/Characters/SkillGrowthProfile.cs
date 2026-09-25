using System;
using System.Collections.Generic;
using Game.Core.Stats;

namespace Game.Core.Characters
{
    /// <summary>
    /// Ростовий профіль архетипу: ваги, за якими очки рівня лягають на скіли.
    ///
    /// Саме на СКІЛИ, а не на стати взагалі: за GDD Е2.1 атрибути майже
    /// статичні й піднімаються тільки крафтом аугмента, тому рівню більше
    /// нічого роздавати. Раніше один профіль годував і бойові стати, і рольові
    /// схильності — звідси й брався перекіс шкал.
    ///
    /// Ваги нормуються: важливі пропорції, а не абсолютні значення.
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
        /// Детерміновано розподіляє <paramref name="points"/> очок за скілами
        /// згідно з вагами. Метод найбільших залишків (Хейра): сума виданих очок
        /// точно дорівнює points, без дробів і накопичення похибки округлення.
        ///
        /// Повертає ПРИРІСТ, а не підсумок — затискати за стелею шкали буде той,
        /// хто його застосує (див. SkillSet.AddClamped).
        /// </summary>
        public SkillSet AllocatePoints(int points)
        {
            var result = new SkillSet();
            if (points <= 0 || _weights.Count == 0) return result;

            double totalWeight = 0;
            for (int i = 0; i < _weights.Count; i++) totalWeight += _weights[i].Value;
            if (totalWeight <= 0) return result;

            // Цілі частини + збір залишків.
            var remainders = new List<KeyValuePair<int, double>>(); // index -> залишок
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

            // Роздаємо очки, що лишилися, тим, у кого найбільший дробовий залишок.
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
