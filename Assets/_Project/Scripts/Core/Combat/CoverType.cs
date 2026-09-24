namespace Game.Core.Combat
{
    /// <summary>
    /// Укрытие: снижает ШАНС ПОПАСТЬ по защищающемуся (полу/полное, числа — в
    /// CombatBalance), а не прибавляет отдельную «защиту».
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
