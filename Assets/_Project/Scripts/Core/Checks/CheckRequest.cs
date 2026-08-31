namespace Game.Core.Checks
{
    /// <summary>Описание проверки: что проверяем, каким подходом и с каким порогом.</summary>
    public readonly struct CheckRequest
    {
        public readonly SkillKey Skill;
        public readonly int Threshold;
        public readonly ApproachForm Approach;

        /// <summary>
        /// Ключ темы для учёта повторных обращений: два разных инцидента не должны
        /// мешать друг другу, а третий подход к одной фракции за неделю — должен.
        /// </summary>
        public readonly string TopicId;

        /// <summary>
        /// Если задано — проверку может пройти только тот, кто держит эту позицию.
        /// Пусто → участвуют все присутствующие (US-2.6: лучший релевантный скил).
        /// </summary>
        public readonly string RequiredPositionId;

        public CheckRequest(SkillKey skill, int threshold, ApproachForm approach = ApproachForm.Neutral,
            string topicId = null, string requiredPositionId = null)
        {
            Skill = skill;
            Threshold = threshold;
            Approach = approach;
            TopicId = topicId;
            RequiredPositionId = requiredPositionId;
        }
    }

    /// <summary>
    /// Предпросмотр проверки — то, что игрок ВИДИТ до подтверждения (US-2.6, US-17.3).
    /// Тест гарантирует: показанный порог равен применённому.
    /// </summary>
    public readonly struct CheckPreview
    {
        public readonly int EffectiveThreshold;
        public readonly int BestValue;
        public readonly string BestActorId;
        public readonly bool HasCandidate;
        public readonly OutcomeBand ExpectedBand;

        public CheckPreview(int effectiveThreshold, int bestValue, string bestActorId,
            bool hasCandidate, OutcomeBand expectedBand)
        {
            EffectiveThreshold = effectiveThreshold;
            BestValue = bestValue;
            BestActorId = bestActorId;
            HasCandidate = hasCandidate;
            ExpectedBand = expectedBand;
        }

        public int Margin => BestValue - EffectiveThreshold;
    }

    /// <summary>Результат резолва.</summary>
    public readonly struct CheckOutcome
    {
        public readonly OutcomeBand Band;
        public readonly int Margin;
        public readonly string ActorId;

        /// <summary>Позицию никто не держал → базовый (худший) исход, US-8.2.</summary>
        public readonly bool WasUnmanned;

        /// <summary>Запугивание провалилось: добавочный штраф к отношению.</summary>
        public readonly bool CausedFear;

        /// <summary>Множитель цены для торговли (1.0 — без скидки).</summary>
        public readonly double PriceMultiplier;

        public CheckOutcome(OutcomeBand band, int margin, string actorId,
            bool wasUnmanned, bool causedFear, double priceMultiplier)
        {
            Band = band;
            Margin = margin;
            ActorId = actorId;
            WasUnmanned = wasUnmanned;
            CausedFear = causedFear;
            PriceMultiplier = priceMultiplier;
        }
    }
}
