using System.Collections.Generic;
using Game.Core.Pressure;
using Game.Core.Signals;

namespace Game.Core.Loop
{
    /// <summary>
    /// Итог дня.
    ///
    /// АРХИТЕКТУРНАЯ ГАРАНТИЯ: числовые секции объявлены internal, поэтому
    /// сборка Game.Gameplay не скомпилируется при попытке их прочитать.
    /// Наружу выходит только <see cref="Signals"/> — дашборд метрик невозможно
    /// собрать даже по ошибке (US-7.1, US-17.2, Поправка №3.4).
    /// </summary>
    public sealed class DayReport
    {
        public int Day { get; }
        public DayPhase Phase { get; }

        /// <summary>Числа скрытой шкалы: только для ядра и тестов.</summary>
        internal IReadOnlyList<TensionChange> TensionChanges { get; }

        /// <summary>Единственное, что читает городской UI.</summary>
        public SignalDigest Signals { get; }

        internal DayReport(int day, DayPhase phase,
            IReadOnlyList<TensionChange> tensionChanges, SignalDigest signals)
        {
            Day = day;
            Phase = phase;
            TensionChanges = tensionChanges;
            Signals = signals;
        }
    }
}
