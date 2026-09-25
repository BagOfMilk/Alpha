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
    /// Порт «кого можна вбити». Тримається окремо від резолвера, щоб криза
    /// не знала нічого про модель персонажа — вона ще буде переписана.
    /// </summary>
    public interface ICasualtySink
    {
        /// <summary>Живі непротагоністи, відсортовані детерміновано.</summary>
        IReadOnlyList<string> KillableActorIds { get; }

        /// <summary>Хто тримає позицію цього домену (може бути null).</summary>
        string ActorOnPosition(string positionId);

        void Kill(string actorId);

        /// <summary>
        /// Тір за замовчуванням — Light (R16/B7): наявні виклики цього методу
        /// не знають тіра і не повинні почати видавати шрами заднім числом —
        /// це рішення власника щодо конкретних інцидентів, а не побічний ефект
        /// правки контракту.
        /// </summary>
        void Wound(string actorId, double injuryPoints, WoundTier tier = WoundTier.Light);
    }

    /// <summary>
    /// Резолв інцидента: перевірка → полоса наслідку → наслідки.
    ///
    /// Криза б'є по-справжньому (US-11.1), але в жорсткість вбудовані огородження:
    /// протагоніст недоторканний, останнього напарника не вбивають, а замість
    /// вбивства обирається відтік населення. Жорсткість не повинна перетворюватися
    /// на софт-лок.
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

            // Тихий шлях — основний спосіб розібратися (Поправка №1), але не
            // єдиний: кривавий був виписаний у контенті і до появи точки
            // рішення не читався жодним рядком коду.
            bool bloody = path == Loop.IncidentPath.Bloody && incident.HasBloodyPath;
            var request = BuildRequest(incident, path, fear, day, balance);

            var check = CheckResolver.Resolve(request, roster, repeats, day, balance);

            ApplyTension(incident, check.Band, tension, balance);

            // Ціна крові. До цього кривавий шлях був строго вигідніший за тихий:
            // він вирішував справу тим самим наслідком, але нічого не коштував, і
            // перший же гравець зробив би висновок «гра про різанину» — рівно
            // навпаки Поправці №1.
            if (bloody) ApplyBloodCost(incident, check, casualties, tension, balance);

            // Боїться громада і від крові, і від провального залякування.
            bool scared = bloody || check.CausedFear;
            if (scared && fear != null) fear.Remember(day, balance.Checks);

            if (!incident.IsCrisis)
            {
                // Хороший розбір може привести людей: знайшли зниклого — а з
                // ним і тих, хто прибився по дорозі. Поганий розбір не приводить
                // нікого: чутка про місто, де не справляються, відлякує.
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
        /// Перевірка під обраний шлях. Кривавого може не бути — тоді тихий.
        ///
        /// Страх громади входить у поріг САМЕ ТУТ, а не в резолвері перевірок,
        /// бо і передперегляд (точка рішення), і резолв будують запит цим
        /// самим методом: інваріант 8 вимагає, щоб показаний поріг дорівнював
        /// застосованому, включно з надбавкою за вчорашню кров.
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

        /// <summary>Домовляються словом. Залякування страхом не дешевшає — див. FearState.</summary>
        private static bool IsSocial(ApproachForm approach)
        {
            return approach == ApproachForm.Persuade || approach == ApproachForm.Trade;
        }

        /// <summary>
        /// Кров коштує трьох речей одразу: Напруги за своїм драйвером, рани
        /// виконавцю і пам'яті громади (її ставить викликач).
        /// </summary>
        private static void ApplyBloodCost(IncidentDefinition incident, CheckOutcome check,
            ICasualtySink casualties, TensionState tension, BalanceConfig balance)
        {
            // Стиль проходження — окремий драйвер ЗАКРИТОГО списку (інваріант 5):
            // різанина ростить Напругу сама по собі, чим би не закінчився розбір.
            if (tension != null)
                tension.Apply(TensionDriver.PlaystyleBlood, balance.Tension.BloodDeltaPerNode,
                    "blood:" + incident.Id);

            // Рана дістається тому, хто ходив у справу. Якщо на посту не стояв
            // ніхто, ранити нікого: за пусту позицію вже призначена Найгірша полоса.
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

            // Наслідок загрози вміє і підіймати, і опускати — але через РІЗНІ
            // драйвери, бо білий список дозволяє кожному лише одну сторону.
            var driver = delta > 0 ? TensionDriver.ThreatOutcome : TensionDriver.EventOutcome;
            tension.Apply(driver, delta, "incident:" + incident.Id);
        }

        private static IncidentOutcome ResolveCrisis(IncidentDefinition incident, CheckOutcome check,
            ICasualtySink casualties, PopulationState population, bool causedFear)
        {
            // Хороший розбір пом'якшує удар: криза невідворотна, але не зобов'язана
            // бути максимально жорстокою при підготовленому місті.
            var bite = check.Band >= OutcomeBand.Good ? CrisisBite.WoundCompanion : incident.Bite;

            string victim = null;
            int lost = 0;

            if (bite == CrisisBite.KillCompanion || bite == CrisisBite.WoundCompanion)
            {
                victim = PickVictim(incident, casualties);

                if (victim == null)
                {
                    // Нікого чіпати — б'ємо по населенню. Огородження від софт-локу.
                    bite = CrisisBite.PopulationOutflow;
                }
                else if (bite == CrisisBite.KillCompanion)
                {
                    // Останнього живого напарника не вбиваємо ніколи.
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
        /// Вибір жертви детермінований: спочатку той, хто тримав релевантну
        /// позицію (він був на передньому краї), інакше перший за Id. Жодного
        /// «випадково хтось помер» — це було б нечесно за повного детермінізму.
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
