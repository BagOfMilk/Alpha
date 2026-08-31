using System;
using System.Collections.Generic;
using Game.Core.Balance;

namespace Game.Core.Checks
{
    /// <summary>
    /// Единственная точка резолва проверок в игре: инциденты, доклады с постов,
    /// социальные подходы — всё проходит здесь.
    ///
    /// Костей нет (US-2.6): «скил ≥ порога». Расширение — полосы исхода по запасу
    /// над порогом (Поправка №3.7). Порог всегда можно посмотреть заранее через
    /// <see cref="Preview"/>, и он равен тому, что применит <see cref="Resolve"/>.
    /// </summary>
    public static class CheckResolver
    {
        /// <summary>Что игрок видит до подтверждения.</summary>
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
                int value = actor.GetCheckValue(request.Skill) + actor.GetTraitModifier(request.Skill);

                // Ничьи разрешаются по Id — иначе результат зависел бы от порядка
                // в списке, и воспроизводимость кампании поехала бы.
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

        /// <summary>Резолв. Порог считается тем же кодом, что и в предпросмотре.</summary>
        public static CheckOutcome Resolve(CheckRequest request, IRosterView roster,
            IRepeatTracker repeats, int day, BalanceConfig balance)
        {
            var preview = Preview(request, roster, repeats, day, balance);
            repeats?.Register(request.TopicId, day);

            var cfg = balance.Checks;

            if (!preview.HasCandidate)
            {
                // Никто не держит позицию — базовый (худший) исход (US-8.2).
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
        /// Полоса по запасу. Подход меняет форму лестницы, а не само число:
        /// Убеждение поднимает пол и опускает потолок (страховка от хвостов),
        /// Запугивание удлиняет оба хвоста, Торговля лестницу не трогает.
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

            // Позицию держит кто-то один, НО протагонист может вмешаться всегда:
            // «протагонист — тоже кандидат» (US-8.2). Без этого лидер оказался бы
            // бессилен в собственном городе.
            var candidates = new List<ISettlementActor>();
            for (int i = 0; i < present.Count; i++)
            {
                var a = present[i];
                if (a == null || !a.IsPresentInSettlement) continue;

                bool onPost = string.Equals(a.HeldPositionId, request.RequiredPositionId, StringComparison.Ordinal);
                if (onPost || a.IsProtagonist) candidates.Add(a);
            }
            return candidates;
        }
    }

    /// <summary>
    /// Учёт повторных обращений: третий подход к одной теме за неделю дороже
    /// первого. Защита от спама диалогов без всякой случайности.
    /// </summary>
    public interface IRepeatTracker
    {
        int AttemptsInWindow(string topicId, int day, int windowDays);
        void Register(string topicId, int day);
    }
}
