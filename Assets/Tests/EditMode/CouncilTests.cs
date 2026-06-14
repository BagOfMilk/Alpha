using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Combat;
using Game.Core.Council;
using Game.Core.Economy;
using Game.Core.Factions;
using Game.Core.Threats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>Совет (Эпик 8.4): валидация цены/КД, эффекты по системам, доход инвестиции, баф вылазке.</summary>
    public class CouncilTests
    {
        private static ThreatSystem Threats(double startTension = 50) =>
            new ThreatSystem(new BalanceConfig(), new ScriptedRng(), new List<IncidentDefinition>(), null, startTension);

        private static (Council council, FactionRegistry reg, ResourceLedger ledger, ThreatSystem threats)
            Make(int gold = 100, int influence = 5, double startTension = 50)
        {
            var reg = DefaultFactions.NewRegistry(influence);
            var ledger = new ResourceLedger();
            ledger.Add(ResourceType.Gold, gold);
            var threats = Threats(startTension);
            var council = DefaultCouncil.NewCouncil(reg, ledger, threats);
            return (council, reg, ledger, threats);
        }

        [Test]
        public void Execute_UnknownAction_Rejected()
        {
            var (council, _, _, _) = Make();
            Assert.AreEqual(CouncilActionResult.UnknownAction, council.Execute("ghost").Result);
        }

        [Test]
        public void Cooldown_BlocksReuse_TicksDownOverDays()
        {
            var (council, _, _, _) = Make();
            Assert.IsTrue(council.Execute(DefaultCouncil.Raid).Success);
            Assert.AreEqual(6, council.CooldownRemaining(DefaultCouncil.Raid));
            Assert.AreEqual(CouncilActionResult.OnCooldown, council.Execute(DefaultCouncil.Raid).Result);

            council.TickDays(5);
            Assert.AreEqual(1, council.CooldownRemaining(DefaultCouncil.Raid));
            council.TickDays(1);
            Assert.AreEqual(0, council.CooldownRemaining(DefaultCouncil.Raid));
            Assert.IsTrue(council.Execute(DefaultCouncil.Raid).Success, "после КД снова доступно");
        }

        [Test]
        public void Raid_LowersTension_PaysCosts_ShiftsFactions()
        {
            var (council, reg, ledger, threats) = Make(gold: 100, influence: 5, startTension: 50);
            var r = council.Execute(DefaultCouncil.Raid);

            Assert.IsTrue(r.Success);
            Assert.AreEqual(38, threats.Tension.Value);                       // 50 − 12
            Assert.AreEqual(80, ledger.Get(ResourceType.Gold));              // 100 − 20
            Assert.AreEqual(4, reg.Influence);                               // 5 − 1
            Assert.AreEqual(6, reg.Get(DefaultFactions.Garrison).Value);     // силовики довольны
            Assert.AreEqual(-8, reg.Get(DefaultFactions.FreeFolk).Value);    // вольные в ярости
        }

        [Test]
        public void Execute_CannotAfford_GoldOrInfluence()
        {
            var (poorGold, _, _, _) = Make(gold: 5, influence: 5);
            Assert.AreEqual(CouncilActionResult.CannotAffordGold, poorGold.Execute(DefaultCouncil.Raid).Result);

            var (poorInfl, _, _, _) = Make(gold: 100, influence: 0);
            Assert.AreEqual(CouncilActionResult.CannotAffordInfluence, poorInfl.Execute(DefaultCouncil.Raid).Result);
        }

        [Test]
        public void PrepareThreat_RaisesReadiness()
        {
            var (council, _, _, threats) = Make();
            Assert.AreEqual(0, threats.Readiness.Value);
            Assert.IsTrue(council.Execute(DefaultCouncil.Prepare).Success);
            Assert.AreEqual(10, threats.Readiness.Value); // ReadinessGain
        }

        [Test]
        public void Diplomacy_RaisesTargetFaction_RejectsNoTarget()
        {
            var (council, reg, _, _) = Make();
            Assert.AreEqual(CouncilActionResult.InvalidTarget, council.Execute(DefaultCouncil.Diplomacy).Result);

            var r = council.Execute(DefaultCouncil.Diplomacy, DefaultFactions.Commune);
            Assert.IsTrue(r.Success);
            Assert.AreEqual(10, reg.Get(DefaultFactions.Commune).Value); // TargetFactionDelta
            Assert.AreEqual(2, reg.Reputation);                          // Social.Reputation
        }

        [Test]
        public void Decree_TradesOneFactionForAnother()
        {
            var (council, reg, _, _) = Make();
            Assert.IsTrue(council.Execute(DefaultCouncil.Decree).Success);
            Assert.AreEqual(8, reg.Get(DefaultFactions.Traders).Value);
            Assert.AreEqual(-6, reg.Get(DefaultFactions.Commune).Value);
        }

        [Test]
        public void Investment_PaysGoldOverTime_ThenStops()
        {
            var (council, _, ledger, _) = Make(gold: 100);
            Assert.IsTrue(council.Execute(DefaultCouncil.Investment).Success);
            Assert.AreEqual(60, ledger.Get(ResourceType.Gold)); // 100 − 40

            council.TickDays(5);  // 5 × 8 = 40
            Assert.AreEqual(100, ledger.Get(ResourceType.Gold));
            council.TickDays(10); // осталось 5 дней дохода: +40, потом сухо
            Assert.AreEqual(140, ledger.Get(ResourceType.Gold));
            council.TickDays(10);
            Assert.AreEqual(140, ledger.Get(ResourceType.Gold), "доход кончился");
        }

        [Test]
        public void OutfitExpedition_SetsAndConsumesBuff()
        {
            var (council, _, _, _) = Make();
            Assert.IsNull(council.PendingExpeditionBuff);
            Assert.IsTrue(council.Execute(DefaultCouncil.Outfit).Success);
            Assert.IsNotNull(council.PendingExpeditionBuff);
            Assert.AreEqual(10, council.PendingExpeditionBuff.AccuracyBonus);

            var buff = council.ConsumeExpeditionBuff();
            Assert.AreEqual(30, buff.BonusLootGold);
            Assert.IsNull(council.PendingExpeditionBuff, "потребляется один раз");
        }

        [Test]
        public void NullThreats_RaidStillPaysAndShiftsFactions()
        {
            // threats == null: эффект Напряжения мягко пропускается, остальное работает.
            var reg = DefaultFactions.NewRegistry(5);
            var ledger = new ResourceLedger();
            ledger.Add(ResourceType.Gold, 100);
            var council = DefaultCouncil.NewCouncil(reg, ledger, threats: null);

            Assert.IsTrue(council.Execute(DefaultCouncil.Raid).Success);
            Assert.AreEqual(80, ledger.Get(ResourceType.Gold));
            Assert.AreEqual(6, reg.Get(DefaultFactions.Garrison).Value);
        }
    }
}
