using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Characters.Scars;
using Game.Core.Expeditions;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Шрами на Серйозних/Критичних ранах (R16/G10): вічний трек
    /// <see cref="ScarTrack"/> існував і раніше, але шрами не присвоювалися
    /// НІДЕ — тест ловить саме це.
    /// </summary>
    public class ScarGrantTests
    {
        private static Companion Comp(string id = "guard") =>
            new CompanionArchetype(id, id).CreateInstance(id);

        [Test]
        public void SeriousWound_ThroughRosterAdapter_GrantsExactlyOneScar()
        {
            var roster = new Roster();
            var comp = Comp();
            roster.Add(comp);
            var adapter = new RosterAdapter(roster);

            adapter.Wound(comp.Id, 10, WoundTier.Serious);

            Assert.AreEqual(1, comp.Scars.Count, "Серйозна рана дає рівно один шрам");
        }

        [Test]
        public void LightWound_ThroughRosterAdapter_GrantsNoScar()
        {
            var roster = new Roster();
            var comp = Comp();
            roster.Add(comp);
            var adapter = new RosterAdapter(roster);

            // Без третього аргументу — саме так сьогодні звуть інциденти
            // (спойлені запаси, кризові укуси): поведінка не повинна змінитись.
            adapter.Wound(comp.Id, 10);

            Assert.AreEqual(0, comp.Scars.Count, "Легка рана шраму не лишає (US-2.5/US-4.2)");
        }

        [Test]
        public void RepeatedSeriousWounds_GrantDifferentScars_InCatalogOrder()
        {
            var roster = new Roster();
            var comp = Comp();
            roster.Add(comp);
            var adapter = new RosterAdapter(roster);

            adapter.Wound(comp.Id, 5, WoundTier.Serious);
            adapter.Wound(comp.Id, 5, WoundTier.Serious);

            Assert.AreEqual(2, comp.Scars.Count);
            Assert.AreEqual(DefaultScars.OneEyed().Id, comp.Scars.Scars[0].Id, "перший шрам — перший у каталозі");
            Assert.AreEqual(DefaultScars.Limp().Id, comp.Scars.Scars[1].Id, "другий — другий, без повтору");
        }

        [Test]
        public void SameSetup_TwiceOver_PicksTheSameScar_NoRandomness()
        {
            var a = Comp("a");
            var b = Comp("b");

            DefaultScars.TryGrant(a, WoundTier.Serious, out var scarA);
            DefaultScars.TryGrant(b, WoundTier.Serious, out var scarB);

            Assert.AreEqual(scarA.Id, scarB.Id, "детермінований вибір — однакові вхідні дають однаковий шрам");
        }

        [Test]
        public void CriticalTier_IsAlsoEligible()
        {
            var comp = Comp();
            Assert.IsTrue(DefaultScars.TryGrant(comp, WoundTier.Critical, out var granted));
            Assert.IsNotNull(granted);
        }

        [Test]
        public void ExpeditionForcefulWorst_SeriousWound_GrantsScarOnComplete()
        {
            var roster = new Roster();
            var comp = Comp("scout");
            roster.Add(comp);
            var state = new BaseState(roster, new Game.Core.Economy.ResourceLedger(),
                new Game.Core.Balance.BalanceConfig());

            var result = new ExpeditionResult { SiteId = "x" };
            result.Wounded.Add(new ExpeditionWound { ActorId = "scout", Tier = WoundTier.Serious });
            result.PartyIds.Add("scout");

            ExpeditionRunner.Complete(state, result);

            Assert.AreEqual(1, comp.Scars.Count, "рана з вилазки йде через ту саму точку рішення, що й RosterAdapter.Wound");
        }

        [Test]
        public void ExpeditionLightWound_GrantsNoScar()
        {
            var roster = new Roster();
            var comp = Comp("scout");
            roster.Add(comp);
            var state = new BaseState(roster, new Game.Core.Economy.ResourceLedger(),
                new Game.Core.Balance.BalanceConfig());

            var result = new ExpeditionResult { SiteId = "x" };
            result.Wounded.Add(new ExpeditionWound { ActorId = "scout", Tier = WoundTier.Light });
            result.PartyIds.Add("scout");

            ExpeditionRunner.Complete(state, result);

            Assert.AreEqual(0, comp.Scars.Count);
        }

        /// <summary>
        /// Фікс-ревью (major, §2 №23): WoundReporting — той самий шлях, що і
        /// Wound (ICasualtySink), але повертає дарований шрам, аби GameSession
        /// мав звідки логувати "scar.granted" з усіх трьох реальних точок
        /// ранення (ApplyCrisisBite/ApplyBattleCasualties/ApplyFinaleCost).
        /// </summary>
        [Test]
        public void SeriousWound_ThroughWoundReporting_ReturnsGrantedScar()
        {
            var roster = new Roster();
            var comp = Comp();
            roster.Add(comp);
            var adapter = new RosterAdapter(roster);

            var granted = adapter.WoundReporting(comp.Id, 10, WoundTier.Serious);

            Assert.IsNotNull(granted, "WoundReporting має повертати дарований шрам");
            Assert.AreEqual(DefaultScars.OneEyed().Id, granted.Id);
            Assert.AreEqual(1, comp.Scars.Count, "той самий побічний ефект, що і Wound");
        }

        [Test]
        public void LightWound_ThroughWoundReporting_ReturnsNull()
        {
            var roster = new Roster();
            var comp = Comp();
            roster.Add(comp);
            var adapter = new RosterAdapter(roster);

            var granted = adapter.WoundReporting(comp.Id, 10);

            Assert.IsNull(granted, "Легка рана — жодного шраму, і жодної помилкової події scar.granted");
        }

        [Test]
        public void DeadCompanion_NeverScarred()
        {
            var roster = new Roster();
            var comp = Comp();
            roster.Add(comp);
            var adapter = new RosterAdapter(roster);

            adapter.Kill(comp.Id);
            adapter.Wound(comp.Id, 100, WoundTier.Critical);

            Assert.AreEqual(0, comp.Scars.Count, "мёртвым шрамы уже не нужны — Wound на них не действует вовсе");
        }
    }
}
