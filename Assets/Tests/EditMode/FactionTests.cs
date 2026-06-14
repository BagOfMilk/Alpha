using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Combat;
using Game.Core.Factions;
using Game.Core.Threats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>Фракции (Эпик 10): полосы отношения, гейтинг, репутация/влияние, соц-последствия.</summary>
    public class FactionTests
    {
        [Test]
        public void Standing_StartsNeutral_BandsByThreshold()
        {
            var reg = DefaultFactions.NewRegistry();
            Assert.AreEqual(FactionBand.Neutral, reg.BandOf(DefaultFactions.Garrison));

            reg.Adjust(DefaultFactions.Garrison, 20);   // 20 → Warm (>15)
            Assert.AreEqual(FactionBand.Warm, reg.BandOf(DefaultFactions.Garrison));
            reg.Adjust(DefaultFactions.Garrison, 40);   // 60 → Allied (>=50)
            Assert.AreEqual(FactionBand.Allied, reg.BandOf(DefaultFactions.Garrison));

            reg.Adjust(DefaultFactions.FreeFolk, -60);  // −60 → Hostile (<=-50)
            Assert.AreEqual(FactionBand.Hostile, reg.BandOf(DefaultFactions.FreeFolk));
        }

        [Test]
        public void Standing_ClampedTo_PlusMinus100()
        {
            var reg = DefaultFactions.NewRegistry();
            reg.Adjust(DefaultFactions.Traders, 999);
            Assert.AreEqual(100, reg.Get(DefaultFactions.Traders).Value);
            reg.Adjust(DefaultFactions.Traders, -999);
            Assert.AreEqual(-100, reg.Get(DefaultFactions.Traders).Value);
        }

        [Test]
        public void AtLeast_GatesByBand()
        {
            var reg = DefaultFactions.NewRegistry();
            Assert.IsFalse(reg.AtLeast(DefaultFactions.Traders, FactionBand.Warm));
            reg.Adjust(DefaultFactions.Traders, 20);
            Assert.IsTrue(reg.AtLeast(DefaultFactions.Traders, FactionBand.Warm));
            Assert.IsFalse(reg.AtLeast(DefaultFactions.Traders, FactionBand.Allied));
        }

        [Test]
        public void Reputation_BandsAndClamp()
        {
            var reg = DefaultFactions.NewRegistry();
            Assert.AreEqual(ReputationBand.Unknown, reg.RepBand);
            reg.AdjustReputation(55);
            Assert.AreEqual(ReputationBand.Respected, reg.RepBand);
            reg.AdjustReputation(999);
            Assert.AreEqual(100, reg.Reputation);
            Assert.AreEqual(ReputationBand.Renowned, reg.RepBand);
        }

        [Test]
        public void Influence_AddSpend_NoOverspend()
        {
            var reg = DefaultFactions.NewRegistry(startingInfluence: 3);
            Assert.IsTrue(reg.SpendInfluence(2));
            Assert.AreEqual(1, reg.Influence);
            Assert.IsFalse(reg.SpendInfluence(5), "нельзя уйти в минус");
            Assert.AreEqual(1, reg.Influence);
        }

        [Test]
        public void SocialConsequence_MovesMultipleSystems_AtOnce()
        {
            var reg = DefaultFactions.NewRegistry(startingInfluence: 1);
            var threats = new ThreatSystem(new BalanceConfig(), new ScriptedRng(),
                new List<IncidentDefinition>(), null, startingTension: 40);
            var flags = new HashSet<string>();

            new SocialConsequence()
                .Faction(DefaultFactions.Commune, +5)
                .Reputation(+3).Influence(+2).Tension(-10).Flag("helped_commune")
                .Apply(reg, baseState: null, threats: threats, flags: flags);

            Assert.AreEqual(5, reg.Get(DefaultFactions.Commune).Value);
            Assert.AreEqual(3, reg.Reputation);
            Assert.AreEqual(3, reg.Influence);                       // 1 + 2
            Assert.AreEqual(30, threats.Tension.Value);              // скрытая дельта −10
            Assert.IsTrue(flags.Contains("helped_commune"));
        }
    }
}
