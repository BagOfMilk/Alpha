using System;
using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Checks;
using Game.Core.Pressure;
using Game.Core.Settlement;

namespace Game.Core.World
{
    /// <summary>
    /// Порт «кого можно убить». Держится отдельно от резолвера, чтобы кризис
    /// не знал ничего о модели персонажа — она ещё будет переписана.
    /// </summary>
    public interface ICasualtySink
    {
        /// <summary>Живые непротагонисты, отсортированные детерминированно.</summary>
        IReadOnlyList<string> KillableActorIds { get; }

        /// <summary>Кто держит позицию этого домена (может быть null).</summary>
        string ActorOnPosition(string positionId);

        void Kill(string actorId);
        void Wound(string actorId, double injuryPoints);
    }

    /// <summary>
    /// Резолв инцидента: проверка → полоса исхода → последствия.
    ///
    /// Кризис бьёт по-настоящему (US-11.1), но в жёсткость встроены ограждения:
    /// протагонист неприкосновенен, последнего напарника не убивают, а вместо
    /// убийства выбирается отток населения. Жёсткость не должна превращаться
    /// в софт-лок.
    /// </summary>
    public static class IncidentResolver
    {
        public static IncidentOutcome Resolve(
            IncidentDefinition incident,
            IRosterView roster,
            IRepeatTracker repeats,
            ICasualtySink casualties,
            PopulationState population,
            TensionState tension,
            int day,
            BalanceConfig balance)
        {
            if (incident == null) throw new ArgumentNullException(nameof(incident));
            if (balance == null) throw new ArgumentNullException(nameof(balance));

            // Тихий путь — основной способ разобраться (Поправка №1).
            var request = new CheckRequest(
                incident.QuietPathSkill,
                incident.QuietPathThreshold,
                incident.QuietPathApproach,
                incident.TopicId,
                incident.RelevantPositionId);

            var check = CheckResolver.Resolve(request, roster, repeats, day, balance);

            ApplyTension(incident, check.Band, tension, balance);

            if (!incident.IsCrisis)
            {
                return new IncidentOutcome(incident.Id, incident.TopicId, incident.DomainTag,
                    check.Band, check.WasUnmanned, false, null, null, 0);
            }

            return ResolveCrisis(incident, check, casualties, population);
        }

        private static void ApplyTension(IncidentDefinition incident, OutcomeBand band,
            TensionState tension, BalanceConfig balance)
        {
            if (tension == null) return;

            var deltas = incident.TensionByBand;
            if (deltas == null || deltas.Length == 0) return;

            int index = (int)band;
            if (index >= deltas.Length) index = deltas.Length - 1;
            int delta = deltas[index];
            if (delta == 0) return;

            // Исход угрозы умеет и поднимать, и опускать — но через РАЗНЫЕ драйверы,
            // потому что белый список разрешает каждому только одну сторону.
            var driver = delta > 0 ? TensionDriver.ThreatOutcome : TensionDriver.EventOutcome;
            tension.Apply(driver, delta, "incident:" + incident.Id);
        }

        private static IncidentOutcome ResolveCrisis(IncidentDefinition incident, CheckOutcome check,
            ICasualtySink casualties, PopulationState population)
        {
            // Хороший разбор смягчает удар: кризис непредотвратим, но не обязан
            // быть максимально жестоким при подготовленном городе.
            var bite = check.Band >= OutcomeBand.Good ? CrisisBite.WoundCompanion : incident.Bite;

            string victim = null;
            int lost = 0;

            if (bite == CrisisBite.KillCompanion || bite == CrisisBite.WoundCompanion)
            {
                victim = PickVictim(incident, casualties);

                if (victim == null)
                {
                    // Некого трогать — бьём по населению. Ограждение от софт-лока.
                    bite = CrisisBite.PopulationOutflow;
                }
                else if (bite == CrisisBite.KillCompanion)
                {
                    // Последнего живого напарника не убиваем никогда.
                    if (casualties.KillableActorIds.Count <= 1)
                    {
                        bite = CrisisBite.WoundCompanion;
                        casualties.Wound(victim, 30);
                    }
                    else
                    {
                        casualties.Kill(victim);
                    }
                }
                else
                {
                    casualties.Wound(victim, 30);
                }
            }

            if (bite == CrisisBite.PopulationOutflow && population != null)
                lost = population.Remove(incident.PopulationLoss);

            return new IncidentOutcome(incident.Id, incident.TopicId, incident.DomainTag,
                check.Band, check.WasUnmanned, true, victim, bite, lost);
        }

        /// <summary>
        /// Выбор жертвы детерминирован: сначала тот, кто держал релевантную
        /// позицию (он был на переднем крае), иначе первый по Id. Никакого
        /// «случайно кто-то умер» — это было бы нечестно при полном детерминизме.
        /// </summary>
        private static string PickVictim(IncidentDefinition incident, ICasualtySink casualties)
        {
            if (casualties == null) return null;

            var killable = casualties.KillableActorIds;
            if (killable == null || killable.Count == 0) return null;

            if (!string.IsNullOrEmpty(incident.RelevantPositionId))
            {
                string onPost = casualties.ActorOnPosition(incident.RelevantPositionId);
                if (!string.IsNullOrEmpty(onPost) && Contains(killable, onPost))
                    return onPost;
            }

            string best = null;
            for (int i = 0; i < killable.Count; i++)
                if (best == null || string.CompareOrdinal(killable[i], best) < 0)
                    best = killable[i];
            return best;
        }

        private static bool Contains(IReadOnlyList<string> list, string value)
        {
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i], value, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
