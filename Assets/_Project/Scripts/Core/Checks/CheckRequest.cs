namespace Game.Core.Checks
{
    /// <summary>Опис перевірки: що перевіряємо, яким підходом і з яким порогом.</summary>
    public readonly struct CheckRequest
    {
        public readonly SkillKey Skill;
        public readonly int Threshold;
        public readonly ApproachForm Approach;

        /// <summary>
        /// Ключ теми для обліку повторних звернень: два різних інциденти не повинні
        /// заважати один одному, а третій підхід до однієї фракції за тиждень — повинен.
        /// </summary>
        public readonly string TopicId;

        /// <summary>
        /// Якщо задано — перевірку може пройти тільки той, хто тримає цю позицію.
        /// Порожньо → беруть участь усі присутні (US-2.6: найкращий релевантний скіл).
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
    /// Передперегляд перевірки — те, що гравець БАЧИТЬ до підтвердження (US-2.6, US-17.3).
    /// Тест гарантує: показаний поріг дорівнює застосованому.
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

    /// <summary>Результат резолву.</summary>
    public readonly struct CheckOutcome
    {
        public readonly OutcomeBand Band;
        public readonly int Margin;
        public readonly string ActorId;

        /// <summary>Позицію ніхто не тримав → базовий (найгірший) результат, US-8.2.</summary>
        public readonly bool WasUnmanned;

        /// <summary>Залякування провалилося: додатковий штраф до ставлення.</summary>
        public readonly bool CausedFear;

        /// <summary>Множник ціни для торгівлі (1.0 — без знижки).</summary>
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
