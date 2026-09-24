using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Characters.Traits;
using Game.Core.Companions;
using Game.Core.Economy;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Драма ростера (B4, порт US-9.4/9.6 на модель Э2): ценностные связи, рябь
    /// от смерти/предательства с ограждением от каскада, уход в антагонисты.
    /// Портировано из архивной линии (commit 20b8dcf) на текущий Companion —
    /// без Equipment/Combat (B3/B1, недоступны в этом воркчасте).
    /// </summary>
    public class CompanionDramaTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();

        private static Companion WithValues(Roster roster, string id, params string[] values)
        {
            var arch = new CompanionArchetype(id, id);
            var trait = new TraitDefinition("vt_" + id, id);
            foreach (var v in values) trait.WithValue(v);
            arch.AddStartingTrait(trait);
            var c = arch.CreateInstance(id, Cfg);
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
            var medic = WithValues(roster, "medic", DefaultValues.Mercy);
            var negotiator = WithValues(roster, "negotiator", DefaultValues.Mercy, DefaultValues.Duty);
            var brawler = WithValues(roster, "brawler", DefaultValues.Ruthless, DefaultValues.Freedom);
            var leader = WithValues(roster, "leader", DefaultValues.Order, DefaultValues.Duty);

            var bonds = new RosterBonds(DefaultValues.System());

            Assert.AreEqual(BondType.Kinship, bonds.Between(medic, negotiator));
            Assert.AreEqual(BondType.Friction, bonds.Between(medic, brawler));
            Assert.AreEqual(BondType.Friction, bonds.Between(leader, brawler));

            CollectionAssert.Contains(bonds.AlliesOf(roster, medic), negotiator);
            CollectionAssert.Contains(bonds.RivalsOf(roster, medic), brawler);
        }

        // ---- Рябь от смерти ----
        [Test]
        public void OnDeath_KinMourns_RivalReliefs_NeutralDips()
        {
            var roster = new Roster();
            var dead = WithValues(roster, "dead", "mercy");
            var kin = WithValues(roster, "kin", "mercy");
            var rival = WithValues(roster, "rival", "ruthless");
            var neutral = WithValues(roster, "neutral", "profit");
            dead.MarkDead();

            var drama = new RosterDrama(new RosterBonds(DefaultValues.System()), Cfg);
            drama.OnDeath(roster, "dead");

            Assert.AreEqual(50 - Cfg.CompanionSocial.MournLoyaltyHit, kin.Loyalty, "соратник скорбит");
            Assert.AreEqual(50 + Cfg.CompanionSocial.RivalDeathLoyaltyRelief, rival.Loyalty, "соперник не скорбит");
            Assert.AreEqual(50 - Cfg.CompanionSocial.NeutralDeathLoyaltyHit, neutral.Loyalty, "нейтрал слегка тронут");
        }

        [Test]
        public void OnDeath_CascadeGuard_LimitsHeavyRipples()
        {
            var cfg = new BalanceConfig();
            cfg.CompanionSocial.MaxRippleTargets = 3;
            cfg.CompanionSocial.MournLoyaltyHit = 8;
            cfg.CompanionSocial.NeutralDeathLoyaltyHit = 2;

            var roster = new Roster();
            WithValues(roster, "dead", "mercy").MarkDead();
            for (int i = 0; i < 5; i++) WithValues(roster, "kin" + i, "mercy"); // 5 соратников

            var report = new RosterDrama(new RosterBonds(DefaultValues.System()), cfg).OnDeath(roster, "dead");

            int heavy = 0, light = 0;
            foreach (var e in report.Effects)
            {
                if (e.Note == "скорбит") heavy++;
                else if (e.Note == "тронут (каскад огорожен)") light++;
            }
            Assert.AreEqual(3, heavy, "тяжёлый отклик ограничен MaxRippleTargets");
            Assert.AreEqual(2, light, "остальные соратники — лёгкий отклик (каскад огорожен)");
        }

        [Test]
        public void OnBetrayal_KinFeelBetrayed()
        {
            var cfg = new BalanceConfig();
            cfg.CompanionSocial.BetrayalKinLoyaltyHit = 10;
            cfg.CompanionSocial.MaxRippleTargets = 3;

            var roster = new Roster();
            WithValues(roster, "traitor", "mercy");
            var kin = WithValues(roster, "kin", "mercy");

            new RosterDrama(new RosterBonds(DefaultValues.System()), cfg).OnBetrayal(roster, "traitor");
            Assert.AreEqual(50 - 10, kin.Loyalty);
        }

        [Test]
        public void OnDeath_AntagonistNeverParticipatesInRipple()
        {
            // Ушедший к врагу не скорбит и не получает рябь — он уже не "свой"
            // (B4-аудит §4.5).
            var roster = new Roster();
            WithValues(roster, "dead", "mercy").MarkDead();
            var gone = WithValues(roster, "gone", "mercy");
            Defection.Defect(gone);

            var report = new RosterDrama(new RosterBonds(DefaultValues.System()), Cfg).OnDeath(roster, "dead");
            Assert.IsFalse(report.Effects.Exists(e => e.CompanionId == "gone"),
                "антагонист не участвует в ряби ростера");
        }

        // ---- Предательство → антагонист ----
        [Test]
        public void ShouldDefect_OnlyLowLoyalty_NonProtagonist()
        {
            var roster = new Roster();
            var low = WithValues(roster, "low");
            low.ApplyLoyaltyDelta(-30); // 50 -> 20 = Resentful
            var content = WithValues(roster, "content"); // 50 = Steady
            var prot = WithValues(roster, "prot");
            prot.ApplyLoyaltyDelta(-40); // даже на дне

            Assert.IsTrue(Defection.ShouldDefect(low, isProtagonist: false,
                consecutiveDaysAtOrBelowResentful: 99, defectorSeeded: false, cfg: Cfg));
            Assert.IsFalse(Defection.ShouldDefect(content, isProtagonist: false,
                consecutiveDaysAtOrBelowResentful: 99, defectorSeeded: false, cfg: Cfg), "Steady — не уходит");
            Assert.IsFalse(Defection.ShouldDefect(prot, isProtagonist: true,
                consecutiveDaysAtOrBelowResentful: 99, defectorSeeded: false, cfg: Cfg), "протагонист не предаёт (US-4.4)");
        }

        [Test]
        public void ShouldDefect_ByDurationThreshold_NotBeforeNDays()
        {
            var roster = new Roster();
            var c = WithValues(roster, "c");
            c.ApplyLoyaltyDelta(-30); // Resentful

            Assert.IsFalse(Defection.ShouldDefect(c, false, Cfg.CompanionSocial.DefectionDaysAtLowLoyalty - 1, false, Cfg));
            Assert.IsTrue(Defection.ShouldDefect(c, false, Cfg.CompanionSocial.DefectionDaysAtLowLoyalty, false, Cfg));
        }

        [Test]
        public void ShouldDefect_BySeededFlag_EvenBeforeNDays()
        {
            var roster = new Roster();
            var c = WithValues(roster, "c");
            c.ApplyLoyaltyDelta(-30); // Resentful

            Assert.IsFalse(Defection.ShouldDefect(c, false, 0, defectorSeeded: false, cfg: Cfg));
            Assert.IsTrue(Defection.ShouldDefect(c, false, 0, defectorSeeded: true, cfg: Cfg),
                "флаг defector_seeded + низкая полоса дефектит без ожидания N дней");
        }

        [Test]
        public void Defect_SnapshotsLevel_GoesAntagonist_LeavesPost()
        {
            var cfg = new BalanceConfig();
            var roster = new Roster();
            var state = new BaseState(roster, new ResourceLedger(), cfg);
            state.AddSlot(new AssignmentSlotDefinition("post", "Пост", BaseSectionType.Council));

            var c = WithValues(roster, "c");
            state.TryAssign("c", "post");
            c.ApplyLoyaltyDelta(-30);

            var rec = Defection.Defect(c, state);

            Assert.AreEqual(CompanionStatus.Antagonist, c.Status);
            Assert.AreEqual("c", rec.CompanionId);
            Assert.AreEqual(c.Level, rec.Level);
            Assert.IsNull(c.AssignedSlotId, "антагонист снят с поста");
            Assert.IsNull(state.GetSlot("post").AssignedCompanionId, "пост освобождён");
        }

        [Test]
        public void DefectionWatch_CountsConsecutiveDaysAtLowBand_ResetsOnRecovery()
        {
            var roster = new Roster();
            var c = WithValues(roster, "c");
            c.ApplyLoyaltyDelta(-30); // Resentful
            var watch = new DefectionWatch();

            watch.Tick(roster);
            watch.Tick(roster);
            Assert.AreEqual(2, watch.DaysAtOrBelowResentful("c"));

            c.ApplyLoyaltyDelta(30); // назад к 50 = Steady
            watch.Tick(roster);
            Assert.AreEqual(0, watch.DaysAtOrBelowResentful("c"), "восстановление сбрасывает счётчик");
        }

        [Test]
        public void DefectionWatch_SaveRoundTrip()
        {
            var roster = new Roster();
            var c = WithValues(roster, "c");
            c.ApplyLoyaltyDelta(-30);
            var watch = new DefectionWatch();
            watch.Tick(roster);
            watch.Tick(roster);
            watch.Tick(roster);

            var reloaded = new DefectionWatch();
            reloaded.RestoreState(watch.CaptureState());

            Assert.AreEqual(3, reloaded.DaysAtOrBelowResentful("c"));
        }
    }
}
