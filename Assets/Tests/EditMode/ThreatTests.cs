using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Economy;
using Game.Core.Stats;
using Game.Core.Threats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Скрытые шкалы (Эпик 11): мягкий фоновый тик от тира, полосы, инциденты по
    /// весам и полосе, резолв позициями (US-8.2), непредотвратимые кризисы,
    /// одноразовые пороговые всплески, «Готовность».
    /// </summary>
    public class ThreatTests
    {
        private static (BaseState baseState, Roster roster, BalanceConfig cfg) MakeBase(BalanceConfig cfg = null)
        {
            cfg = cfg ?? new BalanceConfig();
            var roster = new Roster();
            var baseState = new BaseState(roster, new ResourceLedger(), cfg);
            return (baseState, roster, cfg);
        }

        private static Companion AddComp(Roster roster, string id, SkillType skill = SkillType.None,
                                         int level = 0, bool protagonist = false)
        {
            var c = new Companion(id, new AttributeBlock(3, 3, 3, 3), 4) { IsProtagonist = protagonist };
            if (skill != SkillType.None && level > 0) c.Skills.Set(skill, level);
            roster.Add(c);
            return c;
        }

        private static IncidentDefinition Minor(string id = "minor", int weight = 3) =>
            new IncidentDefinition(id, id, IncidentSeverity.Minor)
            { Skill = SkillType.Survival, Threshold = 2, TensionOnSuccess = -3, TensionOnFailure = 4, Weight = weight };

        // ---- Шкала Напряжения ----
        [Test]
        public void Tension_BackgroundTick_IsSoft_AndScalesWithTier()
        {
            var cfg = new BalanceConfig();
            var t1 = new TensionTrack(cfg);
            for (int i = 0; i < 10; i++) t1.TickDay(1);
            Assert.AreEqual(2.0, t1.Value, 0.001, "10 дней простоя ≈ +2 — мягкое давление (US-1.3)");

            var t3 = new TensionTrack(cfg);
            t3.TickDay(3);
            Assert.AreEqual(0.6, t3.Value, 0.001, "выше тир города — выше фон (US-7.6)");
        }

        [Test]
        public void Tension_Clamps_And_Bands()
        {
            var cfg = new BalanceConfig();
            var t = new TensionTrack(cfg);
            t.Add(-50);
            Assert.AreEqual(0, t.Value);
            Assert.AreEqual(TensionBand.Calm, t.Band);

            t.Add(30);
            Assert.AreEqual(TensionBand.Uneasy, t.Band);
            t.Add(25);
            Assert.AreEqual(TensionBand.Tense, t.Band);
            t.Add(150);
            Assert.AreEqual(100, t.Value);
            Assert.AreEqual(TensionBand.Critical, t.Band);
        }

        [Test]
        public void Readiness_Accumulates_IntoBands()
        {
            var cfg = new BalanceConfig();
            var r = new ReadinessTrack(cfg);
            Assert.AreEqual(ReadinessBand.Unprepared, r.Band);

            r.AddPreparation(); // 10
            r.AddPreparation(); // 20
            r.AddPreparation(); // 30
            Assert.AreEqual(ReadinessBand.Braced, r.Band);

            r.AddFortification(); // 45
            r.AddFortification(); // 60
            Assert.AreEqual(ReadinessBand.Fortified, r.Band);
        }

        [Test]
        public void CityTier_Advance_CappedByConfig()
        {
            var (baseState, _, cfg) = MakeBase();
            for (int i = 0; i < 10; i++) baseState.AdvanceCityTier();
            Assert.AreEqual(cfg.MaxCityTier, baseState.CityTier);
        }

        // ---- Интеграция с AdvanceDays ----
        [Test]
        public void AdvanceDays_TicksTension_QuietWhenRollsHigh()
        {
            var (baseState, _, cfg) = MakeBase();
            // ScriptedRng без значений → D100 = 50 > шанс при низком Напряжении → тихо.
            var threats = new ThreatSystem(cfg, new ScriptedRng(), new List<IncidentDefinition> { Minor() });
            baseState.AttachThreats(threats);

            var report = baseState.AdvanceDays(10);

            Assert.AreEqual(2.0, threats.Tension.Value, 0.001);
            Assert.AreEqual(0, report.Incidents.Count);
            Assert.AreEqual(TensionBand.Calm, report.TensionBand);
        }

        // ---- Инциденты: резолв позициями (US-8.2) ----
        private static (BaseState baseState, ThreatSystem threats, Roster roster) IncidentBase(
            double startTension, bool manSlot, int skillLevel, params int[] rng)
        {
            var (baseState, roster, cfg) = MakeBase();
            baseState.AddSlot(new AssignmentSlotDefinition("dock", "Склад", BaseSectionType.Storehouse)
            { RelevantSkill = SkillType.Survival });
            if (manSlot)
            {
                AddComp(roster, "keeper", SkillType.Survival, skillLevel);
                baseState.TryAssign("keeper", "dock");
            }
            var threats = new ThreatSystem(cfg, new ScriptedRng(rng),
                new List<IncidentDefinition> { Minor() }, startingTension: startTension);
            baseState.AttachThreats(threats);
            return (baseState, threats, roster);
        }

        [Test]
        public void Incident_MannedSlot_SkilledResolver_LowersTension()
        {
            // День: тик 40→40.2, шанс ≈ 20 → ролл 10 запускает; вес: Range(1,3)=1.
            var (baseState, threats, _) = IncidentBase(40, manSlot: true, skillLevel: 3, rng: new[] { 10, 1 });

            var report = baseState.AdvanceDays(1);

            Assert.AreEqual(1, report.Incidents.Count);
            var inc = report.Incidents[0];
            Assert.AreEqual("keeper", inc.ResolvedById);
            Assert.IsTrue(inc.Success);
            Assert.Less(threats.Tension.Value, 40, "успешный резолв снижает Напряжение");
        }

        [Test]
        public void Incident_MannedSlot_WeakResolver_Fails()
        {
            var (baseState, threats, _) = IncidentBase(40, manSlot: true, skillLevel: 1, rng: new[] { 10, 1 });

            var report = baseState.AdvanceDays(1);

            var inc = report.Incidents[0];
            Assert.AreEqual("keeper", inc.ResolvedById);
            Assert.IsFalse(inc.Success, "скил ниже порога — детерминированный провал (US-2.6)");
            Assert.Greater(threats.Tension.Value, 40);
        }

        [Test]
        public void Incident_UnmannedSlot_WorstOutcome()
        {
            var (baseState, threats, _) = IncidentBase(40, manSlot: false, skillLevel: 0, rng: new[] { 10, 1 });

            var report = baseState.AdvanceDays(1);

            var inc = report.Incidents[0];
            Assert.IsNull(inc.ResolvedById, "позицию никто не держит");
            Assert.IsFalse(inc.Success, "базовый (худший) исход — стимул занимать позиции (US-8.2)");
            Assert.Greater(threats.Tension.Value, 40);
        }

        // ---- Состав пула по полосе ----
        [Test]
        public void Severity_Filter_CalmGetsMinor_CriticalCanGetCrisis()
        {
            var pool = new List<IncidentDefinition>
            {
                Minor("m", weight: 3),
                new IncidentDefinition("org", "org", IncidentSeverity.Organized) { Skill = SkillType.Survival, Weight = 2 },
                new IncidentDefinition("cri", "cri", IncidentSeverity.Crisis) { Skill = SkillType.Survival, Weight = 1 }
            };

            // Calm (старт 0): допустимы только Minor; вес-ролл 3 из 3 → "m".
            var (calmBase, _, _) = MakeBaseWithPool(0, pool, 1, 3);
            var calmReport = calmBase.AdvanceDays(1);
            Assert.AreEqual(IncidentSeverity.Minor, calmReport.Incidents[0].Severity);

            // Critical (старт 80): допустимы все; вес-ролл 6 из 6 → кризис.
            var (criticalBase, _, _) = MakeBaseWithPool(80, pool, 1, 6);
            var criticalReport = criticalBase.AdvanceDays(1);
            Assert.AreEqual(IncidentSeverity.Crisis, criticalReport.Incidents[0].Severity);
        }

        private static (BaseState, ThreatSystem, Roster) MakeBaseWithPool(
            double start, List<IncidentDefinition> pool, params int[] rng)
        {
            var (baseState, roster, cfg) = MakeBase();
            var threats = new ThreatSystem(cfg, new ScriptedRng(rng), pool, startingTension: start);
            baseState.AttachThreats(threats);
            return (baseState, threats, roster);
        }

        // ---- Кризисы (непредотвратимые) ----
        [Test]
        public void Crisis_Exodus_DropsPopulation()
        {
            var (baseState, roster, cfg) = MakeBase();
            baseState.AdvanceDays(12); // население ≈ 3
            double before = baseState.Population;

            var pogrom = new IncidentDefinition("pogrom", "Погром", IncidentSeverity.Crisis)
            { Skill = SkillType.Persuasion, Crisis = CrisisEffect.PopulationExodus, TensionOnFailure = 12 };
            var threats = new ThreatSystem(cfg, new ScriptedRng(1, 1),
                new List<IncidentDefinition> { pogrom }, startingTension: 80);
            baseState.AttachThreats(threats);

            var report = baseState.AdvanceDays(1);

            Assert.AreEqual(CrisisEffect.PopulationExodus, report.Incidents[0].CrisisApplied);
            Assert.Less(baseState.Population, before, "отток населения — жёсткое последствие");
        }

        [Test]
        public void Crisis_KillCompanion_SparesProtagonist()
        {
            var (baseState, roster, cfg) = MakeBase();
            AddComp(roster, "prot", protagonist: true);
            AddComp(roster, "redshirt");

            var strike = new IncidentDefinition("strike", "Удар", IncidentSeverity.Crisis)
            { Skill = SkillType.Survival, Crisis = CrisisEffect.KillCompanion, TensionOnFailure = 10 };
            var threats = new ThreatSystem(cfg, new ScriptedRng(1, 1, 0),
                new List<IncidentDefinition> { strike }, startingTension: 80);
            baseState.AttachThreats(threats);

            var report = baseState.AdvanceDays(1);

            Assert.AreEqual("redshirt", report.Incidents[0].CrisisVictimId);
            Assert.IsFalse(roster.Get("redshirt").IsAlive);
            Assert.IsTrue(roster.Get("prot").IsAlive, "протагонист не может быть жертвой кризиса (US-4.4)");
        }

        // ---- Пороговые всплески ----
        [Test]
        public void ThresholdSpike_FiresOnceOnUpwardCrossing()
        {
            var (baseState, roster, cfg) = MakeBase();
            var spikeIncident = Minor("spike_inc");
            var threats = new ThreatSystem(cfg, new ScriptedRng(),
                new List<IncidentDefinition>(), // пустой пул — только всплеск
                new List<ThresholdSpike> { new ThresholdSpike(50, spikeIncident) },
                startingTension: 45);
            baseState.AttachThreats(threats);

            var fired = threats.ApplyHiddenDelta(baseState, 10); // 45 → 55: пересекли 50
            Assert.IsNotNull(fired);
            Assert.AreEqual("spike_inc", fired.IncidentId);

            Assert.IsNull(threats.ApplyHiddenDelta(baseState, 5), "всплеск одноразовый");
            threats.ApplyHiddenDelta(baseState, -40); // упали ниже порога…
            Assert.IsNull(threats.ApplyHiddenDelta(baseState, 30), "…повторное пересечение не триггерит");
        }
    }
}
