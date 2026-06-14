using System;

namespace Game.Core.Factions
{
    /// <summary>
    /// Полоса отношения фракции к игроку (US-17.2: игроку показывается полоса/реплики,
    /// не число). Пороги — в FactionRegistry.
    /// </summary>
    public enum FactionBand
    {
        Hostile = 0, // враждебны (закрыты ветки/цены кусаются/возможна агрессия)
        Cold = 1,
        Neutral = 2,
        Warm = 3,
        Allied = 4   // союзники (патроны, лучшие цены, эксклюзивные опции)
    }

    /// <summary>
    /// Фракция города (Эпик 10): чистые данные. Своё отношение к игроку живёт в
    /// FactionStanding (рантайм), социальные последствия выбора несут council/квест-
    /// данные (SocialConsequence), а не сама фракция. В Unity обернётся ScriptableObject.
    /// </summary>
    [Serializable]
    public sealed class Faction
    {
        public string Id;
        public string DisplayName;
        public string Blurb; // короткий флавор: чьи интересы

        public Faction() { }

        public Faction(string id, string displayName, string blurb = null)
        {
            Id = id;
            DisplayName = displayName;
            Blurb = blurb;
        }
    }

    /// <summary>Рантайм-отношение одной фракции: значение под капотом + читаемая полоса.</summary>
    public sealed class FactionStanding
    {
        public Faction Faction { get; }
        public double Value { get; private set; }

        public FactionStanding(Faction faction, double startValue = 0)
        {
            Faction = faction ?? throw new ArgumentNullException(nameof(faction));
            Value = startValue;
        }

        internal void Add(double delta) => Value = Math.Max(-100, Math.Min(100, Value + delta));

        public FactionBand Band
        {
            get
            {
                if (Value <= -50) return FactionBand.Hostile;
                if (Value < -15) return FactionBand.Cold;
                if (Value <= 15) return FactionBand.Neutral;
                if (Value < 50) return FactionBand.Warm;
                return FactionBand.Allied;
            }
        }
    }
}
