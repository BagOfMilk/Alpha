namespace Game.Core.Stats
{
    /// <summary>
    /// Похідні числа, які рахуються з атрибутів і далі живуть в
    /// агрегаторі як звичайні стати (US-18.2).
    ///
    /// Важливо: похідна НЕ обчислюється в місці використання. Вона сидиться
    /// в базовий шар агрегатора, і поверх неї лягають модифікатори гіру,
    /// трейтів, шрамів і станів. Інакше перк «+3 HP» нема куди покласти, а
    /// US-5.2 вимагає, щоб живучість росла саме перками і гіром при майже
    /// статичних атрибутах.
    /// </summary>
    public enum DerivedStat
    {
        None = 0,
        MaxHp = 1,                     // GDD: смуга 6–20
        MaxAp = 2,                     // GDD: пул 8–10
        Accuracy = 3,                  // база точності; зброя і скіл додають зверху
        Defense = 4,
        Initiative = 5,                // індивідуальна ініціатива впереміш
        CritChance = 6,
        Armor = 7,                     // плоска броня 1–2; канал гіру
        CarryCapacity = 8,
        StatusDurationReduction = 9,   // Воля скорочує тривалість станів
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
