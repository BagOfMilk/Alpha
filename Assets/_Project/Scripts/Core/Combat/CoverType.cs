namespace Game.Core.Combat
{
    /// <summary>
    /// Укриття: знижує ШАНС ПОПАСТИ по тому, хто захищається (напів/повне, числа — в
    /// CombatBalance), а не додає окремий «захист».
    /// </summary>
    public enum CoverType
    {
        None = 0,
        Half = 1,
        Full = 2
    }

    /// <summary>Сторона тайла, на якій стоїть елемент укриття.</summary>
    public enum Direction
    {
        North = 0, // +Y
        East = 1,  // +X
        South = 2, // −Y
        West = 3   // −X
    }
}
