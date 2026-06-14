using System;
using System.Collections.Generic;

namespace Game.Core.Factions
{
    /// <summary>Полоса общей репутации города (US-10.4: растёт делами).</summary>
    public enum ReputationBand
    {
        Unknown = 0,
        Known = 1,
        Respected = 2,
        Renowned = 3
    }

    /// <summary>
    /// Социальное состояние города (Эпик 10): отношения фракций + общая репутация +
    /// влияние (валюта части действий совета, US-10.4). Чисел игроку не показываем —
    /// наружу идут полосы (US-17.2). Гейтинг контента — через AtLeast (SPOF допустим
    /// для опционального; критический путь за одной фракцией не запирается, US-10.1).
    /// </summary>
    public sealed class FactionRegistry
    {
        private readonly Dictionary<string, FactionStanding> _standings = new Dictionary<string, FactionStanding>();
        private readonly List<FactionStanding> _ordered = new List<FactionStanding>();

        public double Reputation { get; private set; }
        public int Influence { get; private set; }

        public IReadOnlyList<FactionStanding> Standings => _ordered;

        public FactionStanding Register(Faction faction, double startValue = 0)
        {
            if (faction == null || string.IsNullOrEmpty(faction.Id)) return null;
            if (_standings.TryGetValue(faction.Id, out var existing)) return existing;
            var standing = new FactionStanding(faction, startValue);
            _standings.Add(faction.Id, standing);
            _ordered.Add(standing);
            return standing;
        }

        public FactionStanding Get(string factionId)
            => factionId != null && _standings.TryGetValue(factionId, out var s) ? s : null;

        public FactionBand BandOf(string factionId) => Get(factionId)?.Band ?? FactionBand.Neutral;

        /// <summary>Двигает отношение фракции (молча — валентность читается по реакции, US-10.3).</summary>
        public void Adjust(string factionId, double delta) => Get(factionId)?.Add(delta);

        /// <summary>Гейт по полосе: отношение фракции не ниже требуемого (US-10.1/10.2).</summary>
        public bool AtLeast(string factionId, FactionBand band) => BandOf(factionId) >= band;

        // ---- Репутация города ----
        public void AdjustReputation(double delta) => Reputation = Math.Max(0, Math.Min(100, Reputation + delta));

        public ReputationBand RepBand
        {
            get
            {
                if (Reputation < 20) return ReputationBand.Unknown;
                if (Reputation < 50) return ReputationBand.Known;
                if (Reputation < 80) return ReputationBand.Respected;
                return ReputationBand.Renowned;
            }
        }

        // ---- Влияние (валюта совета) ----
        public void AddInfluence(int amount) { if (amount != 0) Influence = Math.Max(0, Influence + amount); }
        public bool CanAfford(int cost) => cost <= 0 || Influence >= cost;

        public bool SpendInfluence(int cost)
        {
            if (!CanAfford(cost)) return false;
            Influence -= cost;
            return true;
        }
    }
}
