using System;

namespace Game.Core.Stats
{
    /// <summary>
    /// Значения четырёх атрибутов. В отличие от старого разреженного мешка статов
    /// набор фиксирован — атрибутов всегда ровно четыре. Задаются при создании
    /// персонажа и почти не меняются (рост — только аугментом в Лаборатории).
    /// </summary>
    [Serializable]
    public sealed class AttributeBlock
    {
        public int Strength;
        public int Agility;
        public int Wits;
        public int Will;

        public AttributeBlock() { }

        public AttributeBlock(int strength, int agility, int wits, int will)
        {
            Strength = strength;
            Agility = agility;
            Wits = wits;
            Will = will;
        }

        public int Get(AttributeType attribute)
        {
            switch (attribute)
            {
                case AttributeType.Strength: return Strength;
                case AttributeType.Agility: return Agility;
                case AttributeType.Wits: return Wits;
                case AttributeType.Will: return Will;
                default: return 0;
            }
        }

        public void Set(AttributeType attribute, int value)
        {
            switch (attribute)
            {
                case AttributeType.Strength: Strength = value; break;
                case AttributeType.Agility: Agility = value; break;
                case AttributeType.Wits: Wits = value; break;
                case AttributeType.Will: Will = value; break;
            }
        }

        /// <summary>Прибавляет delta к атрибуту (рост от аугмента; обычно редко).</summary>
        public void Add(AttributeType attribute, int delta) => Set(attribute, Get(attribute) + delta);

        public AttributeBlock Clone() => new AttributeBlock(Strength, Agility, Wits, Will);
    }
}
