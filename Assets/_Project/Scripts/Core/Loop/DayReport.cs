using System.Collections.Generic;
using Game.Core.Pressure;
using Game.Core.Signals;
using Game.Core.World;

namespace Game.Core.Loop
{
    /// <summary>
    /// Підсумок дня.
    ///
    /// АРХІТЕКТУРНА ГАРАНТІЯ: числові секції оголошені internal, тому
    /// збірка Game.Gameplay не скомпілюється при спробі їх прочитати.
    /// Назовні виходить тільки <see cref="Signals"/> — дашборд метрик неможливо
    /// зібрати навіть помилково (US-7.1, US-17.2, Поправка №3.4).
    /// </summary>
    public sealed class DayReport
    {
        public int Day { get; }
        public DayPhase Phase { get; }

        /// <summary>Числа прихованої шкали: тільки для ядра і тестів.</summary>
        internal IReadOnlyList<TensionChange> TensionChanges { get; }

        /// <summary>Єдине, що читає міський UI.</summary>
        public SignalDigest Signals { get; }

        /// <summary>
        /// Наслідки інцидентів. ПУБЛІЧНІ, на відміну від чисел шкал: те, що
        /// сталося, гравець і так бачить — приховувати треба не події, а метрики.
        /// </summary>
        public IReadOnlyList<IncidentOutcome> Incidents { get; }

        /// <summary>Передвісники цієї фази — для налагодження і тестів.</summary>
        internal IReadOnlyList<Forewarning> Forewarnings { get; }

        /// <summary>
        /// Подія, що чекає ходу гравця. Поки вона тут, доба не закінчена:
        /// конвеєр зупинений на кроці PlayerResolution.
        /// </summary>
        public PendingDecision Pending { get; }

        /// <summary>Звіт неповний: день чекає рішення.</summary>
        public bool AwaitsDecision => Pending != null;

        internal DayReport(int day, DayPhase phase,
            IReadOnlyList<TensionChange> tensionChanges, SignalDigest signals,
            IReadOnlyList<IncidentOutcome> incidents, IReadOnlyList<Forewarning> forewarnings,
            PendingDecision pending = null)
        {
            Pending = pending;
            Day = day;
            Phase = phase;
            TensionChanges = tensionChanges;
            Signals = signals;
            Incidents = incidents;
            Forewarnings = forewarnings;
        }
    }
}
