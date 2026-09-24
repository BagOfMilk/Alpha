namespace Game.Core.Session
{
    /// <summary>
    /// Стан фасаду (§4.1 TEST_BUILD.md). <c>Scene</c>/<c>Dungeon</c>/<c>Battle</c>
    /// — підвішені стани: денний конвеєр (<see cref="Loop.DayProcessor"/>) НЕ
    /// рухається, поки <see cref="GameSession.State"/> — один із них.
    /// </summary>
    public enum SessionState
    {
        Title,
        Creation,
        Opening,
        Morning,
        Day,
        Decision,
        Evening,
        Night,
        Scene,
        Dungeon,
        Battle,
        Summary,
        FreePlay
    }

    /// <summary>Чому <see cref="GameSession.State"/> став <c>Battle</c> (§4.1).</summary>
    public enum SuspendReason
    {
        /// <summary>Вузол 1, доба 1: кровавий шлях "бій на перевалі" (§3.1).</summary>
        PassVanguardBloody,
        /// <summary>Бойова кімната данжу (Core/Dungeons, §3.4).</summary>
        DungeonCombatRoom,
        /// <summary>Фінал, ніч доби 5: кровавий шлях "тримати перевал" (§3.5).</summary>
        FinaleAssault,
        /// <summary>"Тренувальний бій" з титульного меню — пісочниця, нічого не пише в сейв.</summary>
        TrainingSkirmish
    }

    /// <summary>
    /// Точка підвісу: звідки прийшли в <c>Battle</c> і куди вернутися, коли бій
    /// скінчиться (<see cref="GameSession"/> внутрішній <c>OnBattleResolved</c>).
    /// </summary>
    public sealed class SuspendToken
    {
        public readonly SuspendReason Reason;
        public readonly SessionState ReturnState;

        public SuspendToken(SuspendReason reason, SessionState returnState)
        {
            Reason = reason;
            ReturnState = returnState;
        }
    }
}
