namespace Game.Core.Stats
{
    /// <summary>
    /// Производные числа, которые считаются из атрибутов и дальше живут в
    /// агрегаторе как обычные статы (US-18.2).
    ///
    /// Важно: производная НЕ вычисляется в месте использования. Она сидируется
    /// в базовый слой агрегатора, и поверх неё ложатся модификаторы гира,
    /// трейтов, шрамов и состояний. Иначе перк «+3 HP» некуда положить, а
    /// US-5.2 требует, чтобы живучесть росла именно перками и гиром при почти
    /// статичных атрибутах.
    /// </summary>
    public enum DerivedStat
    {
        None = 0,
        MaxHp = 1,                     // GDD: полоса 6–20
        MaxAp = 2,                     // GDD: пул 8–10
        Accuracy = 3,                  // база точности; оружие и скил добавляют сверху
        Defense = 4,
        Initiative = 5,                // индивидуальная инициатива вперемешку
        CritChance = 6,
        Armor = 7,                     // плоская броня 1–2; канал гира
        CarryCapacity = 8,
        StatusDurationReduction = 9,   // Воля сокращает длительность состояний
        DamageBonus = 10,
        MoveApPerTile = 11
    }

    public static class DerivedStatCatalog
    {
        public static readonly DerivedStat[] All =
        {
            DerivedStat.MaxHp, DerivedStat.MaxAp, DerivedStat.Accuracy, DerivedStat.Defense,
            DerivedStat.Initiative, DerivedStat.CritChance, DerivedStat.Armor,
            DerivedStat.CarryCapacity, DerivedStat.StatusDurationReduction,
            DerivedStat.DamageBonus, DerivedStat.MoveApPerTile
        };
    }
}
