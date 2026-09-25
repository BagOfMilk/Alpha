using System;

namespace Game.Core.Stats
{
    /// <summary>
    /// Чотири атрибути персонажа. Щільний масив, а не словник: значення є
    /// в усіх чотирьох завжди, і «не заданий» тут не має сенсу — на відміну від
    /// скілів, де нуль означає «не вміє».
    /// </summary>
    [Serializable]
    public sealed class AttributeSet
    {
        private readonly int[] _values = new int[5]; // індекс = значення enum, 0 не використовується

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
