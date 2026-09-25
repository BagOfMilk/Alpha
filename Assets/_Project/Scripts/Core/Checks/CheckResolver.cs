using System;
using System.Collections.Generic;
using Game.Core.Balance;

namespace Game.Core.Checks
{
    /// <summary>
    /// Єдина точка резолву перевірок у грі: інциденти, доповіді з постів,
    /// соціальні підходи — усе проходить тут.
    ///
    /// Костей немає (US-2.6): «скіл ≥ порога». Розширення — полоси наслідку за
    /// запасом над порогом (Поправка №3.7). Поріг завжди можна подивитися заздалегідь через
    /// <see cref="Preview"/>, і він дорівнює тому, що застосує <see cref="Resolve"/>.
    /// </summary>
    public static class CheckResolver
    {
        /// <summary>Що гравець бачить до підтвердження.</summary>
        public static CheckPreview Preview(CheckRequest request, IRosterView roster,
            IRepeatTracker repeats, int day, BalanceConfig balance)
        {
            if (balance == null) throw new ArgumentNullException(nameof(balance));

            var cfg = balance.Checks;
            int threshold = EffectiveThreshold(request, repeats, day, cfg);

            ISettlementActor best = null;
            int bestValue = 0;

            var candidates = Candidates(request, roster);
            for (int i = 0; i < candidates.Count; i++)
            {
                var actor = candidates[i];
                int value = actor.GetCheckValue(request.Skill, request.Approach) + actor.GetTraitModifier(request.Skill);

                // Нічиї розв'язуються за Id — інакше результат залежав би від порядку
                // у списку, і відтворюваність кампанії поїхала б.
                if (best == null || value > bestValue ||
                    (value == bestValue && string.CompareOrdinal(actor.Id, best.Id) < 0))
                {
                    best = actor;
                    bestValue = value;
                }
            }

            if (best == null)
                return new CheckPreview(threshold, 0, null, false, OutcomeBand.Worst);

            var band = BandFor(bestValue - threshold, request.Approach, cfg);
            return new CheckPreview(threshold, bestValue, best.Id, true, band);
        }

        /// <summary>Резолв. Поріг рахується тим самим кодом, що й у передперегляді.</summary>
        public static CheckOutcome Resolve(CheckRequest request, IRosterView roster,
            IRepeatTracker repeats, int day, BalanceConfig balance)
        {
            var preview = Preview(request, roster, repeats, day, balance);
            repeats?.Register(request.TopicId, day);

            var cfg = balance.Checks;

            if (!preview.HasCandidate)
            {
                // Ніхто не тримає позицію — базовий (найгірший) результат (US-8.2).
                return new CheckOutcome(OutcomeBand.Worst, 0, null,
                    wasUnmanned: true, causedFear: false, priceMultiplier: 1.0);
            }

            bool fear = request.Approach == ApproachForm.Intimidate && preview.ExpectedBand == OutcomeBand.Worst;

            double price = 1.0;
            if (request.Approach == ApproachForm.Trade)
                price = 1.0 - cfg.TradeBandDiscount * (int)preview.ExpectedBand;

            return new CheckOutcome(preview.ExpectedBand, preview.Margin, preview.BestActorId,
                wasUnmanned: false, causedFear: fear, priceMultiplier: price);
        }

        /// <summary>
        /// Полоса за запасом. Підхід змінює форму драбини, а не саме число:
        /// Переконання піднімає підлогу і опускає стелю (страховка від хвостів),
        /// Залякування подовжує обидва хвости, Торгівля драбину не чіпає.
        /// </summary>
        public static OutcomeBand BandFor(int margin, ApproachForm approach, CheckBalance cfg)
        {
            int bestMargin = cfg.BestMargin;
            if (approach == ApproachForm.Intimidate)
                bestMargin -= cfg.IntimidateBestBonus;

            OutcomeBand band;
            if (margin < 0) band = OutcomeBand.Worst;
            else if (margin < cfg.GoodMargin) band = OutcomeBand.Base;
            else if (margin < bestMargin) band = OutcomeBand.Good;
            else band = OutcomeBand.Best;

            if (approach == ApproachForm.Persuade)
            {
                if (band == OutcomeBand.Worst && margin >= -cfg.PersuadeCushion)
                    band = OutcomeBand.Base;
                if (band == OutcomeBand.Best)
                    band = OutcomeBand.Good;
            }

            return band;
        }

        private static int EffectiveThreshold(CheckRequest request, IRepeatTracker repeats,
            int day, CheckBalance cfg)
        {
            int attempts = repeats?.AttemptsInWindow(request.TopicId, day, cfg.RepeatWindowDays) ?? 0;
            return request.Threshold + cfg.RepeatPenaltyStep * attempts;
        }

        private static IReadOnlyList<ISettlementActor> Candidates(CheckRequest request, IRosterView roster)
        {
            if (roster == null) return Array.Empty<ISettlementActor>();

            var present = roster.PresentActors;
            if (present == null) return Array.Empty<ISettlementActor>();

            if (string.IsNullOrEmpty(request.RequiredPositionId))
            {
                var all = new List<ISettlementActor>();
                for (int i = 0; i < present.Count; i++)
                    if (present[i] != null && present[i].IsPresentInSettlement)
                        all.Add(present[i]);
                return all;
            }

            // Позицію тримає той, хто на ній стоїть. Протагоніст — теж, але
            // тільки там, де він сьогодні перебуває: ПРИСУТНІСТЬ є маневром, а не
            // всюдисущістю.
            //
            // Раніше тут стояло «|| a.IsProtagonist» за буквою US-8.2. Один цей
            // рядок робив недосяжними одразу три механіки: «незайнята позиція
            // → Найгірша полоса», мовчання доповіді з порожнього поста і «сліпоту»
            // Поправки №3.8. Лідер виграв би КОЖНУ перевірку на КОЖНІЙ позиції,
            // і розстановка переставала б бути рішенням.
            //
            // Суперечність живе в самому GDD: US-8.2 вимагає і «протагоніст —
            // теж кандидат», і «якщо позицію ніхто не тримає → найгірший результат»,
            // а друге при першому математично недосяжне. Правило пріоритету
            // проєкту розв'язує суперечку: поправки старші за GDD.
            var candidates = new List<ISettlementActor>();
            for (int i = 0; i < present.Count; i++)
            {
                var a = present[i];
                if (a == null || !a.IsPresentInSettlement) continue;

                if (string.Equals(a.HeldPositionId, request.RequiredPositionId, StringComparison.Ordinal))
                    candidates.Add(a);
            }
            return candidates;
        }
    }

    /// <summary>
    /// Облік повторних звернень: третій підхід до однієї теми за тиждень дорожчий за
    /// перший. Захист від спаму діалогів без жодної випадковості.
    /// </summary>
    public interface IRepeatTracker
    {
        int AttemptsInWindow(string topicId, int day, int windowDays);
        void Register(string topicId, int day);
    }
}
