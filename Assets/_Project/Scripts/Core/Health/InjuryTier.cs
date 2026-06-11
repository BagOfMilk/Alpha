namespace Game.Core.Health
{
    /// <summary>
    /// Три тира ранений (Эпик 4). Серьёзное+ присваивает вечный шрам (US-4.2) и
    /// даёт больше дней восстановления (US-4.3); лёгкое шрамов не даёт.
    /// </summary>
    public enum InjuryTier
    {
        None = 0,
        Light = 1,
        Serious = 2,
        Critical = 3
    }
}
