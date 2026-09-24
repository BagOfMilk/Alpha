namespace Game.Core.Factions
{
    /// <summary>
    /// Фракция среза (R5): чистые данные, содержание правит не программист.
    /// Своё отношение к игроку живёт отдельно, в <see cref="FactionStanding"/>
    /// (рантайм) — сама фракция его не хранит.
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
