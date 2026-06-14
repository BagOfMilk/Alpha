using System.Collections.Generic;

namespace Game.Core.Factions
{
    /// <summary>
    /// Сид-фракции прототипа (Эпик 10, ПЛЕЙСХОЛДЕР-флавор постапока; полная
    /// тематическая переименовка — отдельный контент-пасс). Держим тут, чтобы не
    /// трогать общий DefaultContent.cs. Интересы заданы так, чтобы действия совета
    /// давали честный размен (Облава радует порядок, злит вольных — US-8.4).
    /// </summary>
    public static class DefaultFactions
    {
        public const string Garrison = "garrison";   // Гарнизон: порядок, силовики — любят облавы
        public const string Traders = "traders";     // Торговая гильдия: деньги, рынок
        public const string FreeFolk = "free_folk";  // Вольные: окраины/подполье — ненавидят облавы
        public const string Commune = "commune";     // Община: жители/взаимопомощь

        public static List<Faction> All() => new List<Faction>
        {
            new Faction(Garrison, "Гарнизон", "Силовики и порядок. Уважают жёсткую руку."),
            new Faction(Traders, "Торговая гильдия", "Купцы и склады. Любят стабильность и прибыль."),
            new Faction(FreeFolk, "Вольные", "Окраины и подполье. Не терпят облав и комендантского часа."),
            new Faction(Commune, "Община", "Жители и взаимопомощь. Ценят защиту и заботу.")
        };

        /// <summary>Готовый реестр со стартовыми (нейтральными) отношениями + затравкой влияния.</summary>
        public static FactionRegistry NewRegistry(int startingInfluence = 0)
        {
            var reg = new FactionRegistry();
            foreach (var f in All()) reg.Register(f);
            if (startingInfluence != 0) reg.AddInfluence(startingInfluence);
            return reg;
        }
    }
}
