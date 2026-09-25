using Game.Core.Checks;

namespace Game.Core.Signals
{
    /// <summary>
    /// Доповідь напарника з позиції — діегетична відповідь на «як у мене
    /// справи?» без єдиної панелі (Поправка №3.8).
    ///
    /// Точність іде за тією ж драбиною полос, що й усі перевірки: сильний
    /// профільний напарник дає конкретику, слабкий — розпливчастість,
    /// нікого на позиції — тиша. Це робить порожню позицію не просто
    /// втратою ефекту, а СЛІПОТОЮ.
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

        /// <summary>Позицію ніхто не тримає — місто в цьому домені сліпе.</summary>
        public static PostReport Silent(string positionId, string domainTag)
        {
            return new PostReport(positionId, domainTag, null, OutcomeBand.Worst, true);
        }
    }
}
