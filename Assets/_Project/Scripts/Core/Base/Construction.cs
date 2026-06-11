namespace Game.Core.Base
{
    /// <summary>
    /// Идущая стройка/апгрейд здания (GDD §7.3). Тикает днями на продвижении
    /// времени; имеет 5 визуальных стадий по таймеру (<see cref="Stage"/>). По
    /// завершении может открыть связанную позицию (<see cref="UnlocksSlotId"/>).
    /// </summary>
    public sealed class Construction
    {
        public string Id;
        public string DisplayName;
        public BaseSectionType Section;
        public double TotalDays;
        public double RemainingDays;

        /// <summary>Позиция, открываемая по завершении стройки (опц.).</summary>
        public string UnlocksSlotId;

        public Construction(string id, string displayName, BaseSectionType section,
                            double totalDays, string unlocksSlotId = null)
        {
            Id = id;
            DisplayName = displayName;
            Section = section;
            TotalDays = totalDays;
            RemainingDays = totalDays;
            UnlocksSlotId = unlocksSlotId;
        }

        public bool IsComplete => RemainingDays <= 0;

        /// <summary>Текущая визуальная стадия 0..(stages-1) по прогрессу таймера.</summary>
        public int Stage(int stages)
        {
            if (stages <= 1 || TotalDays <= 0) return 0;
            double progress = 1.0 - RemainingDays / TotalDays;
            if (progress < 0) progress = 0;
            if (progress > 1) progress = 1;
            int s = (int)(progress * stages);
            return s >= stages ? stages - 1 : s;
        }
    }
}
