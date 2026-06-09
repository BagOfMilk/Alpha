namespace Game.Core.Base
{
    /// <summary>
    /// Рантайм-состояние слота: его определение, открыт ли он и кто назначен.
    /// Хранит только id напарника — сам объект живёт в ростере, чтобы не
    /// дублировать ссылки и упростить сериализацию сейва.
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
