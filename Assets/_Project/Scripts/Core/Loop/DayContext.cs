using System;
using Game.Core.Balance;
using Game.Core.Pressure;
using Game.Core.Signals;

namespace Game.Core.Loop
{
    /// <summary>
    /// Состояние, которое шаги дня читают и дополняют. Живёт один день.
    /// </summary>
    public sealed class DayContext
    {
        public int Day { get; }
        public DayPhase Phase { get; }

        /// <summary>Тир поселения: 1 хутор → 4 городок. Двигатель фонового тика.</summary>
        public int Tier { get; }

        /// <summary>Уклад как индекс (0 Вольница → 3 Затвор). Полноценный тип — на Э3.</summary>
        public int OrderLevel { get; }

        public BalanceConfig Balance { get; }
        public TensionState Tension { get; }

        /// <summary>Заполняется шагом Signals; уходит наружу в отчёте.</summary>
        internal SignalDigest Signals { get; set; }

        public DayContext(int day, DayPhase phase, int tier, int orderLevel,
            BalanceConfig balance, TensionState tension)
        {
            Day = day;
            Phase = phase;
            Tier = tier;
            OrderLevel = orderLevel;
            Balance = balance ?? throw new ArgumentNullException(nameof(balance));
            Tension = tension ?? throw new ArgumentNullException(nameof(tension));
        }
    }

    /// <summary>Один шаг дневного конвейера. Порядок берётся из DayStepOrder.</summary>
    public interface IDayStep
    {
        int Order { get; }
        void Execute(DayContext ctx);
    }
}
