namespace Game.Core.Base
{
    /// <summary>Результат спроби розблокувати закритий слот.</summary>
    public enum UnlockResult
    {
        Success = 0,
        SlotNotFound = 1,
        AlreadyUnlocked = 2,
        NoPriceDefined = 3, // ціна не проставлена — відкрити нічим
        CannotAfford = 4
    }

    /// <summary>Результат спроби призначити напарника на слот.</summary>
    public enum AssignmentResult
    {
        Success = 0,
        SlotNotFound = 1,
        SlotLocked = 2,
        SlotOccupied = 3,
        CompanionNotFound = 4,
        CompanionUnavailable = 5 // у вилазці тощо — працювати на базі не може
    }
}
