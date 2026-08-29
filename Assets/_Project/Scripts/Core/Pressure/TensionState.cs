using System;
using System.Collections.Generic;
using Game.Core.Balance;

namespace Game.Core.Pressure
{
    /// <summary>
    /// Скрытая шкала «Напряжение».
    ///
    /// Ключевое архитектурное решение: <see cref="Value"/> объявлен internal.
    /// Сборка Game.Gameplay физически не может его прочитать, поэтому дашборд
    /// метрик невозможно собрать даже по ошибке (US-7.1, US-17.2). Наружу уходит
    /// только <see cref="Band"/> — полоса, которая питает слой сигналов.
    /// </summary>
    public sealed class TensionState
    {
        private readonly TensionBalance _cfg;
        private readonly List<TensionChange> _dayLedger = new List<TensionChange>();
        private double _fraction;
        private TensionBand _band;

        /// <summary>Сырое значение. НЕ показывать игроку ни при каких условиях.</summary>
        internal int Value { get; private set; }

        public TensionBand Band => _band;
        public int DaysInCurrentBand { get; private set; }

        /// <summary>Смена полосы обязана породить сигнал (Поправка №3.4).</summary>
        public event Action<TensionBand, TensionBand> BandChanged;

        /// <summary>Журнал изменений за текущий день — для тестов и слоя сигналов.</summary>
        internal IReadOnlyList<TensionChange> DayLedger => _dayLedger;

        public TensionState(TensionBalance cfg, int startValue = 0)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            Value = Clamp(startValue);
            _band = _cfg.BandFor(Value);
        }

        /// <summary>Целочисленное изменение от конкретного драйвера.</summary>
        internal TensionChange Apply(TensionDriver driver, int delta, string sourceId)
        {
            var from = _band;
            if (!_cfg.IsAllowed(driver, delta))
                return Record(new TensionChange(driver, delta, 0, from, from, true, sourceId));

            int applied = Commit(delta);
            return Record(new TensionChange(driver, delta, applied, from, _band, false, sourceId));
        }

        /// <summary>
        /// Дробное изменение (фоновый тик, пассивный дренаж Храма/Укреплений).
        /// Остаток копится, поэтому тик 0.85/день за 20 дней даёт ровно 17,
        /// а не 20 округлений подряд.
        /// </summary>
        internal TensionChange ApplyFractional(TensionDriver driver, double delta, string sourceId)
        {
            var from = _band;
            if (!_cfg.IsAllowed(driver, delta))
                return Record(new TensionChange(driver, (int)Math.Round(delta), 0, from, from, true, sourceId));

            _fraction += delta;
            int whole = (int)_fraction;
            _fraction -= whole;

            int applied = whole != 0 ? Commit(whole) : 0;
            return Record(new TensionChange(driver, whole, applied, from, _band, false, sourceId));
        }

        /// <summary>Вызывается процессором дня после всех шагов.</summary>
        internal void OnDayAdvanced()
        {
            DaysInCurrentBand++;
        }

        internal void BeginDay()
        {
            _dayLedger.Clear();
        }

        private int Commit(int delta)
        {
            int before = Value;
            Value = Clamp(Value + delta);

            var next = _cfg.BandFor(Value);
            if (next != _band)
            {
                var old = _band;
                _band = next;
                DaysInCurrentBand = 0;
                BandChanged?.Invoke(old, next);
            }
            return Value - before;
        }

        private int Clamp(int v)
        {
            if (v < 0) return 0;
            return v > _cfg.Max ? _cfg.Max : v;
        }

        private TensionChange Record(TensionChange change)
        {
            _dayLedger.Add(change);
            return change;
        }
    }
}
