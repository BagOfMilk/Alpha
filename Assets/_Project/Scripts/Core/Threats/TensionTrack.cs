using System;
using Game.Core.Balance;

namespace Game.Core.Threats
{
    /// <summary>
    /// Качественная «температура» города — единственное, что уходит наружу
    /// (US-17.2: число игроку не показывается, только полосы и фоновые сигналы).
    /// </summary>
    public enum TensionBand
    {
        Calm = 0,     // спокойно: мелкие инциденты
        Uneasy = 1,   // неспокойно
        Tense = 2,    // напряжённо: организованная преступность
        Critical = 3  // на грани: кризис-события
    }

    /// <summary>
    /// Скрытая шкала «Напряжение» (Эпик 11.1), 0..100 под капотом. Растят её:
    /// (а) фоновый тик от ТИРА города (мягкий — дни лечения/ожидания почти не
    /// двигают, US-1.3), (б) выборы в квестах (валентность скрыта), (в) исходы
    /// внутренних угроз. Исследование и побочка сами по себе НЕ растят.
    /// </summary>
    public sealed class TensionTrack
    {
        private readonly BalanceConfig _cfg;

        /// <summary>Скрытое значение 0..100. В UI не показывать — только Band.</summary>
        public double Value { get; private set; }

        public TensionTrack(BalanceConfig cfg, double start = 0)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            Value = Clamp(start);
        }

        public TensionBand Band
        {
            get
            {
                if (Value >= _cfg.TensionCriticalAt) return TensionBand.Critical;
                if (Value >= _cfg.TensionTenseAt) return TensionBand.Tense;
                if (Value >= _cfg.TensionUneasyAt) return TensionBand.Uneasy;
                return TensionBand.Calm;
            }
        }

        /// <summary>Фоновый тик одного дня: выше тир города — выше фон (US-7.6/11.1).</summary>
        public void TickDay(int cityTier)
            => Add(_cfg.TensionPerDayPerTier * Math.Max(1, cityTier));

        /// <summary>Дельта от выбора/исхода. Может и снижать, и поднимать (US-11.1).</summary>
        public void Add(double delta) => Value = Clamp(Value + delta);

        private static double Clamp(double v) => v < 0 ? 0 : (v > 100 ? 100 : v);
    }
}
