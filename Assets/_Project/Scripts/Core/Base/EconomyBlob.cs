using System;

namespace Game.Core.Base
{
    /// <summary>
    /// Заворачивает <see cref="BaseState"/> в порт <c>Game.Core.Loop.IStateBlob</c>
    /// для <c>DayProcessor.Economy</c> (Foundation/A1, закрывает D10).
    ///
    /// Ровно тот же приём, каким <c>RosterAdapter</c> (SettlementAdapters.cs)
    /// заворачивает <c>Roster</c>, не заставляя сам Roster реализовывать
    /// контракты Loop. Здесь — та же схема для BaseState: сам BaseState
    /// намеренно не реализует ни один Loop-контракт (охранитель
    /// BaseState_HasNoBackdoorToAdvanceTime), а его CaptureState/RestoreState —
    /// обычные публичные методы, которые этот адаптер просто делегирует.
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
