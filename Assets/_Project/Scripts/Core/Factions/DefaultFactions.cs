using System.Collections.Generic;

namespace Game.Core.Factions
{
    /// <summary>
    /// Три фракції зрізу (R5). «Ставлення Тугара» — це репутація фракції
    /// tuhar_boyars, а не окрема прихована шкала: окремої TuharStanding немає.
    /// </summary>
    public static class DefaultFactions
    {
        public const string Community = "community";
        public const string TuharBoyars = "tuhar_boyars";
        public const string Horde = "horde";

        public static List<Faction> All() => new List<Faction>
        {
            new Faction(Community, "Громада Тухольщини"),
            new Faction(TuharBoyars, "Бояри Тугара"),
            new Faction(Horde, "Орда (Бурунда)")
        };

        /// <summary>Готовий реєстр зі стартовими (нейтральними) стосунками.</summary>
        public static FactionRegistry NewRegistry(Balance.FactionBalance cfg)
        {
            var reg = new FactionRegistry(cfg);
            foreach (var f in All()) reg.Register(f.Id);
            return reg;
        }
    }
}
