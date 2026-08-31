using System;

namespace Game.Core.Stats
{
    /// <summary>
    /// Десять скилов персонажа. Ноль — осмысленное значение («не умеет»), поэтому
    /// хранится плотно и всегда: отсутствие ключа и ноль не должны различаться.
    /// </summary>
    [Serializable]
    public sealed class SkillSet
    {
        // Значения enum разрежены (1–3, 10–13, 20–22), поэтому индексируем по позиции
        // в Skills.All, а не по значению — иначе массив был бы дырявым на 23 ячейки.
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

        public SkillSet Clone()
        {
            var copy = new SkillSet();
            Array.Copy(_values, copy._values, _values.Length);
            return copy;
        }

        /// <summary>Сколько скилов вкачано хоть на сколько-то — для планировщика билда.</summary>
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

    /// <summary>Позиции скилов в плотном массиве. Отдельно, чтобы не считать каждый раз.</summary>
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
