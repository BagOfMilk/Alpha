using System;

namespace Game.Core.Base
{
    /// <summary>
    /// Загортає <see cref="BaseState"/> у порт <c>Game.Core.Loop.IStateBlob</c>
    /// для <c>DayProcessor.Economy</c> (Foundation/A1, закриває D10).
    ///
    /// Рівно той самий прийом, яким <c>RosterAdapter</c> (SettlementAdapters.cs)
    /// загортає <c>Roster</c>, не змушуючи сам Roster реалізовувати
    /// контракти Loop. Тут — та сама схема для BaseState: сам BaseState
    /// навмисно не реалізує жодного Loop-контракту (охоронець
    /// BaseState_HasNoBackdoorToAdvanceTime), а його CaptureState/RestoreState —
    /// звичайні публічні методи, які цей адаптер просто делегує.
    /// </summary>
    public sealed class EconomyBlob : Loop.IStateBlob
    {
        private readonly BaseState _state;

        public EconomyBlob(BaseState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public string CaptureState() => _state.CaptureState();
        public void RestoreState(string blob) => _state.RestoreState(blob);
    }
}
