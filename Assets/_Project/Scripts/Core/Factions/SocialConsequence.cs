using System.Collections.Generic;

namespace Game.Core.Factions
{
    /// <summary>
    /// Единая регистрация соц-последствий выбора (US-10.3): один выбор согласованно
    /// двигает несколько систем — отношения фракций, репутацию/влияние города,
    /// СКРЫТУЮ Напругу (через ThreatSystem) и сюжетные флаги. Социальную рябь игрок
    /// читает на глазах (реакции), гражданскую (Напругу) — нет (US-10.3/11.1).
    /// Чистые данные + Apply; используется и квестами, и действиями совета.
    /// </summary>
    public sealed class SocialConsequence
    {
        public readonly List<KeyValuePair<string, double>> FactionDeltas = new List<KeyValuePair<string, double>>();
        public double ReputationDelta;
        public int InfluenceDelta;
        public double HiddenTensionDelta; // уходит в ThreatSystem (валентность скрыта)
        public readonly List<string> StoryFlags = new List<string>();

        public SocialConsequence Faction(string factionId, double delta)
        {
            FactionDeltas.Add(new KeyValuePair<string, double>(factionId, delta));
            return this;
        }

        public SocialConsequence Reputation(double delta) { ReputationDelta += delta; return this; }
        public SocialConsequence Influence(int delta) { InfluenceDelta += delta; return this; }
        public SocialConsequence Tension(double delta) { HiddenTensionDelta += delta; return this; }
        public SocialConsequence Flag(string flag) { StoryFlags.Add(flag); return this; }

        /// <summary>
        /// Применяет последствие. threats и flags опциональны (null-терпимо). Скрытая
        /// дельта Напряжения идёт через ThreatSystem.ApplyHiddenDelta (может дать всплеск).
        /// </summary>
        public void Apply(FactionRegistry registry, Game.Core.Base.BaseState baseState = null,
                          Game.Core.Threats.ThreatSystem threats = null, ICollection<string> flags = null)
        {
            if (registry != null)
            {
                for (int i = 0; i < FactionDeltas.Count; i++)
                    registry.Adjust(FactionDeltas[i].Key, FactionDeltas[i].Value);
                if (ReputationDelta != 0) registry.AdjustReputation(ReputationDelta);
                if (InfluenceDelta != 0) registry.AddInfluence(InfluenceDelta);
            }

            if (HiddenTensionDelta != 0 && threats != null)
                threats.ApplyHiddenDelta(baseState, HiddenTensionDelta);

            if (flags != null)
                for (int i = 0; i < StoryFlags.Count; i++)
                    flags.Add(StoryFlags[i]);
        }
    }
}
