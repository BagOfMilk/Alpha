namespace Game.Core.Stats
{
    /// <summary>
    /// Четыре атрибута (GDD Э2.1). Почти статичная база: в уровнях НЕ растут,
    /// поднимаются только крафтом аугмента в Лаборатории.
    ///
    /// Шкала 1–10. Атрибуты задают производные (HP, AP, точность, защита,
    /// инициатива, крит, сопротивление состояниям) и добирают число в
    /// соц-проверках под подход.
    /// </summary>
    public enum AttributeType
    {
        None = 0,
        Strength = 1,  // сила — HP, перенос, ближний бой
        Agility = 2,   // ловкость — AP, точность, защита, инициатива
        Wits = 3,      // смекалка — крит, инициатива, убеждение
        Will = 4       // воля — сопротивление состояниям, запугивание
    }

    public static class Attributes
    {
        /// <summary>Все атрибуты без None — в порядке объявления.</summary>
        public static readonly AttributeType[] All =
        {
            AttributeType.Strength, AttributeType.Agility, AttributeType.Wits, AttributeType.Will
        };

        public static string DisplayName(AttributeType a)
        {
            switch (a)
            {
                case AttributeType.Strength: return "Сила";
                case AttributeType.Agility: return "Ловкость";
                case AttributeType.Wits: return "Смекалка";
                case AttributeType.Will: return "Воля";
                default: return "—";
            }
        }
    }
}
