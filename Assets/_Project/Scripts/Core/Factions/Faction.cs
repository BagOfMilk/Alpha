namespace Game.Core.Factions
{
    /// <summary>
    /// Фракція зрізу (R5): чисті дані, вміст править не програміст.
    /// Своє ставлення до гравця живе окремо, в <see cref="FactionStanding"/>
    /// (рантайм) — сама фракція його не зберігає.
    /// </summary>
    public sealed class Faction
    {
        public string Id;
        public string DisplayName;

        public Faction() { }

        public Faction(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }
    }
}
