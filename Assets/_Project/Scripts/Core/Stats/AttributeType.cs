namespace Game.Core.Stats
{
    /// <summary>
    /// Чотири атрибути (GDD Е2.1). Майже статична база: за рівнями НЕ ростуть,
    /// підіймаються лише крафтом аугмента в Лабораторії.
    ///
    /// Шкала 1–10. Атрибути задають похідні (HP, AP, точність, захист,
    /// ініціатива, крит, опір станам) і додають число в
    /// соц-перевірках під підхід.
    /// </summary>
    public enum AttributeType
    {
        None = 0,
        Strength = 1,  // сила — HP, перенесення, ближній бій
        Agility = 2,   // спритність — AP, точність, захист, ініціатива
        Wits = 3,      // кмітливість — крит, ініціатива, переконання
        Will = 4       // воля — опір станам, залякування
    }

    public static class Attributes
    {
        /// <summary>Усі атрибути без None — у порядку оголошення.</summary>
        public static readonly AttributeType[] All =
        {
            AttributeType.Strength, AttributeType.Agility, AttributeType.Wits, AttributeType.Will
        };

        public static string DisplayName(AttributeType a)
        {
            switch (a)
            {
                case AttributeType.Strength: return "Сила";
                case AttributeType.Agility: return "Спритність";
                case AttributeType.Wits: return "Кмітливість";
                case AttributeType.Will: return "Воля";
                default: return "—";
            }
        }
    }
}
