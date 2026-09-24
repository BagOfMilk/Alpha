using System;

namespace Game.Core.Factions
{
    /// <summary>
    /// Полоса отношения фракции к игроку — единственное, что уходит наружу
    /// (US-17.2, инвариант 3). Само число (<see cref="FactionStanding.Value"/>)
    /// игроку не показывается никогда.
    /// </summary>
    public enum FactionStandingBand
    {
        Hostile = 0,
        Wary = 1,
        Neutral = 2,
        Awaiting = 3,
        Allied = 4
    }

    /// <summary>
    /// Рантайм-отношение одной фракции (R5). Построена по тому же шаблону, что
    /// <see cref="Game.Core.Pressure.TensionState"/>: сырое значение — internal,
    /// полоса — public, смена полосы обязана породить сигнал (инвариант 4) —
    /// здесь это C#-событие <see cref="BandChanged"/>, тот же приём, каким
    /// Напряжение уже решает эту задачу.
    /// </summary>
    public sealed class FactionStanding
    {
        private readonly Balance.FactionBalance _cfg;
        private FactionStandingBand _band;

        /// <summary>Сырое значение 0..100. НЕ показывать игроку ни при каких условиях.</summary>
        internal int Value { get; private set; }

        public FactionStandingBand Band => _band;

        /// <summary>Инвариант 4: немого перехода полосы не бывает.</summary>
        public event Action<FactionStandingBand, FactionStandingBand> BandChanged;

        public FactionStanding(Balance.FactionBalance cfg, int startValue)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            Value = Clamp(startValue);
            _band = _cfg.BandFor(Value);
        }

        /// <summary>
        /// Двигает отношение. sourceId — контекст для будущего журнала (по
        /// аналогии с TensionChange.SourceId); сам тип его пока не хранит —
        /// у фракций нет дневного леджера, только полоса и сигнал смены.
        /// </summary>
        internal void Apply(int delta, string sourceId)
        {
            if (delta == 0) return;

            int next = Clamp(Value + delta);
            if (next == Value) return;
            Value = next;

            var nextBand = _cfg.BandFor(Value);
            if (nextBand == _band) return;

            var old = _band;
            _band = nextBand;
            BandChanged?.Invoke(old, nextBand);
        }

        /// <summary>
        /// Восстановление из слепка. Полоса не хранится, а пересчитывается из
        /// значения — тем же приёмом, каким это делает TensionState.RestoreForSave:
        /// правка порогов между сохранением и загрузкой не оставит несогласованную
        /// полосу.
        /// </summary>
        internal void RestoreForSave(int value)
        {
            Value = Clamp(value);
            _band = _cfg.BandFor(Value);
        }

        private static int Clamp(int v) => v < 0 ? 0 : (v > 100 ? 100 : v);
    }
}
