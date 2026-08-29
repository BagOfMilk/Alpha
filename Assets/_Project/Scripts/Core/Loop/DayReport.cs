using System.Collections.Generic;
using Game.Core.Pressure;
using Game.Core.Signals;
using Game.Core.World;

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

        /// <summary>
        /// Исходы инцидентов. ПУБЛИЧНЫ, в отличие от чисел шкал: то, что
        /// произошло, игрок и так видит — скрывать нужно не события, а метрики.
        /// </summary>
        public IReadOnlyList<IncidentOutcome> Incidents { get; }

        /// <summary>Предвестники этой фазы — для отладки и тестов.</summary>
        internal IReadOnlyList<Forewarning> Forewarnings { get; }

        internal DayReport(int day, DayPhase phase,
            IReadOnlyList<TensionChange> tensionChanges, SignalDigest signals,
            IReadOnlyList<IncidentOutcome> incidents, IReadOnlyList<Forewarning> forewarnings)
        {
            Day = day;
            Phase = phase;
            TensionChanges = tensionChanges;
            Signals = signals;
            Incidents = incidents;
            Forewarnings = forewarnings;
        }
    }
}
