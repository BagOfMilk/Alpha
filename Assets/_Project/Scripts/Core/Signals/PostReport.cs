using Game.Core.Checks;

namespace Game.Core.Signals
{
    /// <summary>
    /// Доклад напарника с позиции — диегетичный ответ на «как у меня дела?»
    /// без единой панели (Поправка №3.8).
    ///
    /// Точность идёт по той же лестнице полос, что и все проверки: сильный
    /// профильный напарник даёт конкретику, слабый — расплывчатость,
    /// никого на позиции — тишина. Это делает пустую позицию не просто
    /// потерей эффекта, а СЛЕПОТОЙ.
    /// </summary>
    public readonly struct PostReport
    {
        public readonly string PositionId;
        public readonly string DomainTag;
        public readonly string ActorId;
        public readonly OutcomeBand Accuracy;
        public readonly bool IsSilent;

        public PostReport(string positionId, string domainTag, string actorId,
            OutcomeBand accuracy, bool isSilent)
        {
            PositionId = positionId;
            DomainTag = domainTag;
            ActorId = actorId;
            Accuracy = accuracy;
            IsSilent = isSilent;
        }

        /// <summary>Позицию никто не держит — город в этом домене слеп.</summary>
        public static PostReport Silent(string positionId, string domainTag)
        {
            return new PostReport(positionId, domainTag, null, OutcomeBand.Worst, true);
        }
    }
}
