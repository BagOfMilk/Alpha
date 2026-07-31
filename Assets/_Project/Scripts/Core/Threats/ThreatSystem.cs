using System;
using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Checks;
using Game.Core.Combat;

namespace Game.Core.Threats
{
    /// <summary>Одноразовый авторский всплеск при пересечении порога Напряжения снизу вверх (US-11.1).</summary>
    public sealed class ThresholdSpike
    {
        public double Threshold;
        public IncidentDefinition Incident;

        public ThresholdSpike(double threshold, IncidentDefinition incident)
        {
            Threshold = threshold;
            Incident = incident;
        }
    }

    /// <summary>
    /// Движок угроз (Эпик 11): держит обе скрытые шкалы, тикает Напряжение по дням
    /// (фон от тира города), катит инциденты (частота растёт с Напряжением, состав —
    /// по полосе), резолвит их позициями (US-8.2) и применяет непредотвратимые
    /// кризис-эффекты. Подключается к BaseState и тикается из AdvanceDays.
    /// RNG инъецируется — поведение детерминировано в тестах.
    /// </summary>
    public sealed class ThreatSystem
    {
        private readonly BalanceConfig _cfg;
        private readonly IRng _rng;
        private readonly List<IncidentDefinition> _pool;
        private readonly List<ThresholdSpike> _spikes;
        private readonly HashSet<double> _firedSpikes = new HashSet<double>();

        public TensionTrack Tension { get; }
        public ReadinessTrack Readiness { get; }

        /// <summary>Открытые кризисами враждебные группировки (вход в Эпик 10).</summary>
        public readonly List<string> UnlockedHostileFactions = new List<string>();

        public ThreatSystem(BalanceConfig cfg, IRng rng,
                            List<IncidentDefinition> incidentPool, List<ThresholdSpike> spikes = null,
                            double startingTension = 0)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _pool = incidentPool ?? new List<IncidentDefinition>();
            _spikes = spikes ?? new List<ThresholdSpike>();
            Tension = new TensionTrack(cfg, startingTension);
            Readiness = new ReadinessTrack(cfg);
        }

        /// <summary>
        /// Скрытая дельта Напряжения от выбора в квесте / исхода события (валентность
        /// игроку не показывается, US-11.1). Пересечение порога может дать всплеск.
        /// </summary>
        public IncidentReport ApplyHiddenDelta(BaseState baseState, double delta)
        {
            Tension.Add(delta);
            return FireSpikeIfCrossed(baseState);
        }

        /// <summary>Тик за продвижение времени: фон + роллы инцидентов. Вызывается из BaseState.AdvanceDays.</summary>
        internal void TickDays(BaseState baseState, int days, CycleReport report)
        {
            for (int day = 0; day < days; day++)
            {
                Tension.TickDay(baseState.CityTier);

                var spike = FireSpikeIfCrossed(baseState);
                if (spike != null) report.Incidents.Add(spike);

                double chance = _cfg.IncidentChanceBase + Tension.Value * _cfg.IncidentChancePerTension;
                if (_rng.D100() <= chance)
                {
                    var incident = PickWeighted(AllowedNow());
                    if (incident != null)
                        report.Incidents.Add(Resolve(baseState, incident));
                }
            }
            report.TensionBand = Tension.Band;
        }

        // ---- Внутренности ----
        /// <summary>Состав пула по полосе: ниже полоса — мельче инциденты (US-11.1).</summary>
        private List<IncidentDefinition> AllowedNow()
        {
            var band = Tension.Band;
            var allowed = new List<IncidentDefinition>();
            for (int i = 0; i < _pool.Count; i++)
            {
                var inc = _pool[i];
                bool ok = inc.Severity == IncidentSeverity.Minor
                          || (inc.Severity == IncidentSeverity.Organized && band >= TensionBand.Tense)
                          || (inc.Severity == IncidentSeverity.Crisis && band >= TensionBand.Critical);
                if (ok) allowed.Add(inc);
            }
            return allowed;
        }

        private IncidentDefinition PickWeighted(List<IncidentDefinition> candidates)
        {
            if (candidates.Count == 0) return null;
            int total = 0;
            for (int i = 0; i < candidates.Count; i++) total += Math.Max(1, candidates[i].Weight);
            int roll = _rng.Range(1, total);
            int acc = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                acc += Math.Max(1, candidates[i].Weight);
                if (roll <= acc) return candidates[i];
            }
            return candidates[candidates.Count - 1];
        }

        /// <summary>
        /// Резолв инцидента (US-8.2): занятая позиция с релевантным скилом → её
        /// напарник проходит детерминированную проверку; никого — худший исход.
        /// Кризис-эффект применяется независимо от проверки (непредотвратим).
        /// </summary>
        private IncidentReport Resolve(BaseState baseState, IncidentDefinition incident)
        {
            var report = new IncidentReport
            {
                IncidentId = incident.Id,
                DisplayName = incident.DisplayName,
                Severity = incident.Severity
            };

            Companion resolver = FindResolver(baseState, incident.Skill);
            if (resolver != null)
            {
                var check = CheckResolver.Resolve(new[] { resolver }, incident.Skill, incident.Threshold);
                report.ResolvedById = resolver.Id;
                report.Success = check.Success;
                if (check.Success && incident.XpOnSuccess > 0)
                    resolver.GainXp(incident.XpOnSuccess, _cfg); // XP с событий (US-5.1)
            }
            else
            {
                report.Success = false; // стимул занимать позиции
            }

            Tension.Add(report.Success ? incident.TensionOnSuccess : incident.TensionOnFailure);

            if (incident.Crisis != CrisisEffect.None)
                ApplyCrisis(baseState, incident, report);

            return report;
        }

        private static Companion FindResolver(BaseState baseState, Stats.SkillType skill)
        {
            foreach (var slot in baseState.Slots)
            {
                if (!slot.Unlocked || !slot.IsOccupied) continue;
                if (slot.Definition.RelevantSkill != skill) continue;
                var companion = baseState.Roster.Get(slot.AssignedCompanionId);
                if (companion != null && companion.IsAlive) return companion;
            }
            return null;
        }

        private void ApplyCrisis(BaseState baseState, IncidentDefinition incident, IncidentReport report)
        {
            switch (incident.Crisis)
            {
                case CrisisEffect.PopulationExodus:
                    baseState.RemovePopulation(_cfg.CrisisExodusPopulation);
                    report.CrisisApplied = CrisisEffect.PopulationExodus;
                    break;

                case CrisisEffect.KillCompanion:
                {
                    // Жертва — живой НЕ-протагонист (US-4.4 защищает лидера и тут).
                    var candidates = new List<Companion>();
                    foreach (var c in baseState.Roster.All)
                        if (c.IsAlive && !c.IsProtagonist) candidates.Add(c);
                    if (candidates.Count > 0)
                    {
                        var victim = candidates[_rng.Range(0, candidates.Count - 1)];
                        if (victim.IsAssigned) baseState.Unassign(victim.AssignedSlotId);
                        victim.Kill();
                        report.CrisisApplied = CrisisEffect.KillCompanion;
                        report.CrisisVictimId = victim.Id;
                    }
                    break;
                }

                case CrisisEffect.HostileFaction:
                    if (!UnlockedHostileFactions.Contains(incident.Id))
                        UnlockedHostileFactions.Add(incident.Id);
                    report.CrisisApplied = CrisisEffect.HostileFaction;
                    break;
            }
        }

        /// <summary>Одноразовые авторские всплески на пересечении порогов снизу вверх.</summary>
        private IncidentReport FireSpikeIfCrossed(BaseState baseState)
        {
            for (int i = 0; i < _spikes.Count; i++)
            {
                var spike = _spikes[i];
                if (_firedSpikes.Contains(spike.Threshold)) continue;
                if (Tension.Value < spike.Threshold) continue;
                _firedSpikes.Add(spike.Threshold);
                return Resolve(baseState, spike.Incident);
            }
            return null;
        }
    }
}
