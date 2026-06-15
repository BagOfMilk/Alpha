using Game.Core;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Companions;
using Game.Core.Economy;
using Game.Core.Items;
using Game.Core.Stats;
using Game.Core.Traits;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Драма ростера (Эпик 9): ценностные связи, рябь от смерти/предательства с
    /// ограждением от каскада, уход в антагонисты с гиром/уровнями и возврат гира.
    /// </summary>
    public class CompanionDramaTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();

        private static Companion WithValues(Roster roster, string id, bool protagonist, params string[] values)
        {
            var c = new Companion(id, new AttributeBlock(3, 3, 3, 3), 4) { IsProtagonist = protagonist };
            var t = new Trait("vt_" + id, id);
            foreach (var v in values) t.WithValue(v);
            c.Traits.TryAdd(t);
            roster.Add(c);
            return c;
        }

        // ---- Ценностные связи ----
        [Test]
        public void ValueSystem_Shared_Kinship_Opposed_Friction()
        {
            var vs = DefaultValues.System();
            Assert.AreEqual(BondType.Kinship, vs.Bond(new[] { "mercy" }, new[] { "mercy", "duty" }));
            Assert.AreEqual(BondType.Friction, vs.Bond(new[] { "mercy" }, new[] { "ruthless" }));
            Assert.AreEqual(BondType.Neutral, vs.Bond(new[] { "duty" }, new[] { "profit" }));
        }

        [Test]
        public void RosterBonds_FromDefaultContent_KinAndRivals()
        {
            var roster = new Roster();
            foreach (var bg in DefaultContent.AllBackgrounds()) roster.Add(bg.CreateInstance(bg.Id, Cfg));
            var bonds = new RosterBonds(DefaultValues.System());

            // Медик (mercy) и переговорщик (mercy) — соратники; боец (ruthless/freedom) — соперник.
            Assert.AreEqual(BondType.Kinship, bonds.Between(roster.Get("medic"), roster.Get("negotiator")));
            Assert.AreEqual(BondType.Friction, bonds.Between(roster.Get("medic"), roster.Get("brawler")));
            Assert.AreEqual(BondType.Friction, bonds.Between(roster.Get("leader"), roster.Get("brawler")));

            CollectionAssert.Contains(bonds.AlliesOf(roster, roster.Get("medic")), roster.Get("negotiator"));
            CollectionAssert.Contains(bonds.RivalsOf(roster, roster.Get("medic")), roster.Get("brawler"));
        }

        // ---- Рябь от смерти ----
        [Test]
        public void OnDeath_KinMourns_RivalReliefs_NeutralDips()
        {
            var roster = new Roster();
            var dead = WithValues(roster, "dead", false, "mercy");
            var kin = WithValues(roster, "kin", false, "mercy");
            var rival = WithValues(roster, "rival", false, "ruthless");
            var neutral = WithValues(roster, "neutral", false, "profit");
            dead.Kill();

            var drama = new RosterDrama(new RosterBonds(DefaultValues.System()), Cfg);
            drama.OnDeath(roster, "dead");

            Assert.AreEqual(50 - Cfg.MournLoyaltyHit, kin.Loyalty, "соратник скорбит");
            Assert.AreEqual(50 + Cfg.RivalDeathLoyaltyRelief, rival.Loyalty, "соперник не скорбит");
            Assert.AreEqual(50 - Cfg.NeutralDeathLoyaltyHit, neutral.Loyalty, "нейтрал слегка тронут");
        }

        [Test]
        public void OnDeath_CascadeGuard_LimitsHeavyRipples()
        {
            var cfg = new BalanceConfig { MaxRippleTargets = 3, MournLoyaltyHit = 8, NeutralDeathLoyaltyHit = 2 };
            var roster = new Roster();
            WithValues(roster, "dead", false, "mercy").Kill();
            for (int i = 0; i < 5; i++) WithValues(roster, "kin" + i, false, "mercy"); // 5 соратников

            var report = new RosterDrama(new RosterBonds(DefaultValues.System()), cfg).OnDeath(roster, "dead");

            int heavy = 0, light = 0;
            foreach (var e in report.Effects)
            {
                if (e.LoyaltyDelta == -8) heavy++;
                else if (e.LoyaltyDelta == -2) light++;
            }
            Assert.AreEqual(3, heavy, "тяжёлый отклик ограничен MaxRippleTargets");
            Assert.AreEqual(2, light, "остальные соратники — лёгкий отклик (каскад огорожен)");
        }

        [Test]
        public void OnBetrayal_KinFeelBetrayed()
        {
            var cfg = new BalanceConfig { BetrayalKinLoyaltyHit = 10, MaxRippleTargets = 3 };
            var roster = new Roster();
            WithValues(roster, "traitor", false, "mercy");
            var kin = WithValues(roster, "kin", false, "mercy");

            new RosterDrama(new RosterBonds(DefaultValues.System()), cfg).OnBetrayal(roster, "traitor");
            Assert.AreEqual(50 - 10, kin.Loyalty);
        }

        // ---- Предательство → антагонист-босс ----
        [Test]
        public void ShouldDefect_OnlyLowLoyalty_NonProtagonist()
        {
            var roster = new Roster();
            var low = WithValues(roster, "low", false);
            low.AdjustLoyalty(-30); // 50 → 20 = Resentful
            var content = WithValues(roster, "content", false); // 50 = Steady
            var prot = WithValues(roster, "prot", true);
            prot.AdjustLoyalty(-40); // даже на дне

            Assert.IsTrue(DefectionSystem.ShouldDefect(low));
            Assert.IsFalse(DefectionSystem.ShouldDefect(content), "Steady — не уходит");
            Assert.IsFalse(DefectionSystem.ShouldDefect(prot), "протагонист не предаёт (US-4.4)");
        }

        [Test]
        public void Defect_SnapshotsGearAndLevel_GoesAntagonist()
        {
            var roster = new Roster();
            var c = WithValues(roster, "c", false);
            c.Equipment.Equip(ItemInstance.NamedFrom(DefaultItems.Widowmaker()));
            c.AdjustLoyalty(-30);

            var rec = DefectionSystem.Defect(c, null);

            Assert.AreEqual(CompanionStatus.Antagonist, c.Status);
            Assert.AreEqual(c.Level, rec.Level);
            Assert.IsNotNull(rec.Weapon);
            Assert.AreEqual(StatusType.Bleeding, rec.Weapon.StatusOnHit, "ушёл со своим именным оружием");
            Assert.AreEqual(1, rec.CapturedGear.Count);
        }

        [Test]
        public void BuildBossUnit_IsEnemy_WithGear_DiesForGood()
        {
            var roster = new Roster();
            var c = WithValues(roster, "c", false);
            c.Equipment.Equip(ItemInstance.NamedFrom(DefaultItems.Widowmaker()));

            var boss = DefectionSystem.BuildBossUnit(c, Cfg);
            Assert.AreEqual(Side.Enemy, boss.Side);
            Assert.IsNotNull(boss.Weapon);
            Assert.AreEqual(StatusType.Bleeding, boss.Weapon.StatusOnHit);
            Assert.IsFalse(boss.Profile.CanBeDowned, "босс умирает насовсем → гир возвращается");
        }

        [Test]
        public void ReturnGearOnKill_BanksCapturedGear()
        {
            var roster = new Roster();
            var c = WithValues(roster, "c", false);
            c.Equipment.Equip(ItemInstance.NamedFrom(DefaultItems.Widowmaker()));
            c.Equipment.Equip(ItemInstance.NamedFrom(DefaultItems.AegisPlate()));
            var rec = DefectionSystem.Defect(c, null);

            var inventory = new Inventory();
            DefectionSystem.ReturnGearOnKill(rec, inventory);
            Assert.AreEqual(2, inventory.Count, "убийство босса вернуло весь его надетый гир (US-9.4)");
        }
    }
}
