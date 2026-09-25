namespace Game.Core.Base
{
    /// <summary>
    /// Рантайм-стан слота: його визначення, чи відкритий він і хто призначений.
    /// Зберігає лише id напарника — сам об'єкт живе в ростері, щоб не
    /// дублювати посилання і спростити серіалізацію сейва.
    /// </summary>
    public sealed class AssignmentSlot
    {
        public AssignmentSlotDefinition Definition { get; }
        public bool Unlocked { get; set; }
        public string AssignedCompanionId { get; internal set; }

        public AssignmentSlot(AssignmentSlotDefinition definition)
        {
            Definition = definition;
            Unlocked = definition != null && definition.UnlockedByDefault;
        }

        public string Id => Definition?.Id;
        public bool IsOccupied => !string.IsNullOrEmpty(AssignedCompanionId);
    }
}
