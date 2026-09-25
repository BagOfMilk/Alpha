using System;
using Game.Core.Balance;

namespace Game.Core.Stats
{
    /// <summary>
    /// Десять скілів персонажа. Нуль — осмислене значення («не вміє»), тому
    /// зберігається щільно і завжди: відсутність ключа і нуль не повинні різнитися.
    /// </summary>
    [Serializable]
    public sealed class SkillSet
    {
        // Значення enum розріджені (1–3, 10–13, 20–22), тому індексуємо за позицією
        // у Skills.All, а не за значенням — інакше масив був би дірявим на 23 клітинки.
        private readonly int[] _values = new int[Skills2.Count];

        public int this[SkillType s]
        {
            get
            {
                int i = Skills2.IndexOf(s);
                return i < 0 ? 0 : _values[i];
            }
            set
            {
                int i = Skills2.IndexOf(s);
                if (i >= 0) _values[i] = value;
            }
        }

        /// <summary>
        /// Додає очки іншого набору, затискаючи кожен скіл за шкалою.
        ///
        /// Надлишок понад стелю ЗГОРАЄ, а не переливається в сусідній скіл: інакше
        /// рівень сам вирішував би, чому вчитися, а за GDD Е2.2 це вибір гравця.
        /// Впертися у стелю може тільки профільний скіл архетипу, і тоді
        /// профіль зростання час міняти руками.
        /// </summary>
        public void AddClamped(SkillSet delta, BalanceConfig cfg)
        {
            if (delta == null || cfg == null) return;
            for (int i = 0; i < _values.Length; i++)
                _values[i] = StatScales.ClampSkill(_values[i] + delta._values[i], cfg);
        }

        public SkillSet Clone()
        {
            var copy = new SkillSet();
            Array.Copy(_values, copy._values, _values.Length);
            return copy;
        }

        /// <summary>Скільки скілів прокачано хоч трохи — для планувальника білда.</summary>
        public int AssignedCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _values.Length; i++) if (_values[i] != 0) n++;
                return n;
            }
        }

        public int TotalPoints
        {
            get
            {
                int sum = 0;
                for (int i = 0; i < _values.Length; i++) sum += _values[i];
                return sum;
            }
        }
    }

    /// <summary>Позиції скілів у щільному масиві. Окремо, щоб не рахувати щоразу.</summary>
    internal static class Skills2
    {
        internal static readonly int Count = Skills.All.Length;

        internal static int IndexOf(SkillType s)
        {
            var all = Skills.All;
            for (int i = 0; i < all.Length; i++) if (all[i] == s) return i;
            return -1;
        }
    }
}
