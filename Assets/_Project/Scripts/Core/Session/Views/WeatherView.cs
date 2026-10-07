namespace Game.Core.Session.Views
{
    /// <summary>
    /// Погода надворі і прогноз на завтра (Поправка №21.2). Лише назви станів
    /// (<c>Game.Core.World.WeatherKind</c>: «Clear», «Rain»…), без чисел: множники —
    /// внутрішня кухня, гравець бачить їхній наслідок у прев'ю й звітах.
    /// Прогноз точний — календар детермінований.
    /// </summary>
    public sealed class WeatherView
    {
        public string Today;
        public string Tomorrow;
    }
}
