using System;
using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Characters;
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

        /// <summary>
        /// Тир по умолчанию — Light (R16/B7): существующие вызовы этого метода
        /// не знают тира и не должны начать выдавать шрамы задним числом —
        /// это решение владельца по конкретным инцидентам, не побочный эффект
        /// правки контракта.
        /// </summary>
        void Wound(string actorId, double injuryPoints, WoundTier tier = WoundTier.Light);
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
            BalanceConfig balance,
            Loop.IncidentPath path = Loop.IncidentPath.Quiet,
            FearState fear = null)
        {
            if (incident == null) throw new ArgumentNullException(nameof(incident));
            if (balance == null) throw new ArgumentNullException(nameof(balance));

            // Тихий путь — основной способ разобраться (Поправка №1), но не
            // единственный: кровавый был выписан в контенте и до появления точки
            // решения не читался ни одной строкой кода.
            bool bloody = path == Loop.IncidentPath.Bloody && incident.HasBloodyPath;
            var request = BuildRequest(incident, path, fear, day, balance);

            var check = CheckResolver.Resolve(request, roster, repeats, day, balance);

            ApplyTension(incident, check.Band, tension, balance);

            // Цена крови. До этого кровавый путь был строго выгоднее тихого:
            // он решал дело тем же исходом, но ничего не стоил, и первый же
            // игрок сделал бы вывод «игра про резню» — ровно наоборот Поправке №1.
            if (bloody) ApplyBloodCost(incident, check, casualties, tension, balance);

            // Боится община и от крови, и от провалившегося запугивания.
            bool scared = bloody || check.CausedFear;
            if (scared && fear != null) fear.Remember(day, balance.Checks);

            if (!incident.IsCrisis)
            {
                // Хороший разбор может привести людей: нашли пропавшего — а с
                // ним и тех, кто прибился по дороге. Плохой разбор не приводит
                // никого: слух о городе, где не справляются, отпугивает.
                int arrived = 0;
                if (check.Band >= OutcomeBand.Good && incident.ArrivalsOnGood > 0 && population != null)
                {
                    population.Add(incident.ArrivalsOnGood);
                    arrived = incident.ArrivalsOnGood;
                }

                return new IncidentOutcome(incident.Id, incident.TopicId, incident.DomainTag,
                    check.Band, check.WasUnmanned, false, null, null, 0, scared, arrived);
            }

            return ResolveCrisis(incident, check, casualties, population, scared);
        }

        /// <summary>
        /// Проверка под выбранный путь. Кровавого может не быть — тогда тихий.
        ///
        /// Страх общины входит в порог ЗДЕСЬ, а не в резолвере проверок,
        /// потому что и предпросмотр (точка решения), и резолв строят запрос
        /// этим же методом: инвариант 8 требует, чтобы показанный порог был
        /// равен применённому, включая надбавку за вчерашнюю кровь.
        /// </summary>
        internal static CheckRequest BuildRequest(IncidentDefinition incident, Loop.IncidentPath path,
            FearState fear = null, int day = 0, BalanceConfig balance = null)
        {
            bool bloody = path == Loop.IncidentPath.Bloody && incident.HasBloodyPath;

            var skill = bloody ? incident.BloodyPathSkill : incident.QuietPathSkill;
            int threshold = bloody ? incident.BloodyPathThreshold : incident.QuietPathThreshold;
            var approach = bloody ? ApproachForm.Intimidate : incident.QuietPathApproach;

            if (fear != null && balance != null && IsSocial(approach))
                threshold += fear.PenaltyOn(day, balance.Checks);

            return new CheckRequest(skill, threshold, approach,
                incident.TopicId, incident.RelevantPositionId);
        }

        /// <summary>Договариваются словом. Запугивание страхом не дешевеет — см. FearState.</summary>
        private static bool IsSocial(ApproachForm approach)
        {
            return approach == ApproachForm.Persuade || approach == ApproachForm.Trade;
        }

        /// <summary>
        /// Кровь стоит трёх вещей сразу: Напряжения по своему драйверу, раны
        /// исполнителю и памяти общины (её ставит вызывающий).
        /// </summary>
        private static void ApplyBloodCost(IncidentDefinition incident, CheckOutcome check,
            ICasualtySink casualties, TensionState tension, BalanceConfig balance)
        {
            // Стиль прохождения — отдельный драйвер ЗАКРЫТОГО списка (инвариант 5):
            // резня растит Напряжение сама по себе, чем бы ни кончился разбор.
            if (tension != null)
                tension.Apply(TensionDriver.PlaystyleBlood, balance.Tension.BloodDeltaPerNode,
                    "blood:" + incident.Id);

            // Рана достаётся тому, кто ходил в дело. Если на посту не стоял никто,
            // ранить некого: за пустую позицию уже назначена Худшая полоса.
            if (casualties != null && !string.IsNullOrEmpty(check.ActorId))
                casualties.Wound(check.ActorId, balance.Checks.BloodyPathInjury);
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
            ICasualtySink casualties, PopulationState population, bool causedFear)
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
                check.Band, check.WasUnmanned, true, victim, bite, lost, causedFear);
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
