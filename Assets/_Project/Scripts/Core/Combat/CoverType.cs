namespace Game.Core.Combat
{
    /// <summary>
    /// Укрытие (US-3.3). В WL3-модели укрытие снижает ШАНС ПОПАСТЬ по защищающемуся
    /// (полу −20 / полное −40, числа в BalanceConfig), а не прибавляет «защиту».
    /// </summary>
    public enum CoverType
    {
        None = 0,
        Half = 1,
        Full = 2
    }

    /// <summary>Сторона тайла, на которой стоит элемент укрытия.</summary>
    public enum Direction
    {
        North = 0, // +Y
        East = 1,  // +X
        South = 2, // −Y
        West = 3   // −X
    }
}
