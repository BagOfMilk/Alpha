namespace Game.Core.World
{
    /// <summary>
    /// Погода доби (Поправка №21.2). Значення — індекси в таблицях
    /// <see cref="Game.Core.Balance.WeatherBalance"/>: порядок не міняти, нове — лише в кінець.
    /// </summary>
    public enum WeatherKind
    {
        Clear = 0,
        Overcast = 1,
        Rain = 2,
        Fog = 3,
        Storm = 4
    }
}
