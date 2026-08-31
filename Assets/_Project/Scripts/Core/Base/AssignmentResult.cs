namespace Game.Core.Base
{
    /// <summary>Результат попытки разблокировать закрытый слот.</summary>
    public enum UnlockResult
    {
        Success = 0,
        SlotNotFound = 1,
        AlreadyUnlocked = 2,
        NoPriceDefined = 3, // цена не проставлена — открыть нечем
        CannotAfford = 4
    }

    /// <summary>Результат попытки назначить напарника на слот.</summary>
    public enum AssignmentResult
    {
        Success = 0,
        SlotNotFound = 1,
        SlotLocked = 2,
        SlotOccupied = 3,
        CompanionNotFound = 4,
        CompanionUnavailable = 5 // в вылазке и т.п. — работать на базе не может
    }
}
