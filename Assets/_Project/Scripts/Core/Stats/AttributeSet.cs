using System;

namespace Game.Core.Stats
{
    /// <summary>
    /// Четыре атрибута персонажа. Плотный массив, а не словарь: значения есть
    /// у всех четырёх всегда, и «не задан» здесь не имеет смысла — в отличие от
    /// скилов, где ноль означает «не умеет».
    /// </summary>
    [Serializable]
    public sealed class AttributeSet
    {
        private readonly int[] _values = new int[5]; // индекс = значение enum, 0 не используется

        public AttributeSet() { }

        public AttributeSet(int strength, int agility, int wits, int will)
        {
            this[AttributeType.Strength] = strength;
            this[AttributeType.Agility] = agility;
            this[AttributeType.Wits] = wits;
            this[AttributeType.Will] = will;
        }

        public int this[AttributeType a]
        {
            get => a == AttributeType.None ? 0 : _values[(int)a];
            set { if (a != AttributeType.None) _values[(int)a] = value; }
        }

        public AttributeSet Clone()
        {
            var copy = new AttributeSet();
            Array.Copy(_values, copy._values, _values.Length);
            return copy;
        }

        public int Total
        {
            get
            {
                int sum = 0;
                for (int i = 0; i < Attributes.All.Length; i++) sum += this[Attributes.All[i]];
                return sum;
            }
        }
    }
}
