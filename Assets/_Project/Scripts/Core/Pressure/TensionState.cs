using System;
using System.Collections.Generic;
using Game.Core.Balance;

namespace Game.Core.Pressure
{
    /// <summary>
    /// Прихована шкала «Напруга».
    ///
    /// Ключове архітектурне рішення: <see cref="Value"/> оголошений internal.
    /// Збірка Game.Gameplay фізично не може його прочитати, тому дашборд
    /// метрик неможливо зібрати навіть помилково (US-7.1, US-17.2). Назовні виходить
    /// лише <see cref="Band"/> — полоса, яка живить шар сигналів.
    /// </summary>
    public sealed class TensionState
    {
        private readonly TensionBalance _cfg;
        private readonly List<TensionChange> _dayLedger = new List<TensionChange>();
        /// <summary>
        /// Дробовий залишок — СВІЙ у кожного драйвера. Спільний залишок змішував
        /// внески: дренаж храму −0.5 тонув у фоновому тіку +1.0, сумарна
        /// Напруга виходила вірною, але журнал записував зниження тіку, а
        /// храму — нуль. Журнал драйверів — це відповідь на питання «чому», і він
        /// зобов'язаний відповідати правду.
        /// </summary>
        private readonly Dictionary<TensionDriver, double> _fractions = new Dictionary<TensionDriver, double>();
        private TensionBand _band;

        /// <summary>Сире значення. НЕ показувати гравцю за жодних умов.</summary>
        internal int Value { get; private set; }

        public TensionBand Band => _band;
        public int DaysInCurrentBand { get; private set; }

        /// <summary>Зміна полоси зобов'язана породити сигнал (Поправка №3.4).</summary>
        public event Action<TensionBand, TensionBand> BandChanged;

        /// <summary>Журнал змін за поточний день — для тестів і шару сигналів.</summary>
        internal IReadOnlyList<TensionChange> DayLedger => _dayLedger;

        /// <summary>Дробовий залишок фонового тіку — частина стану, без нього сейв втрачає частки очка.</summary>
        internal double FractionForSave => FractionOf(TensionDriver.CityTierTick);

        internal double FractionOf(TensionDriver driver)
        {
            double f;
            return _fractions.TryGetValue(driver, out f) ? f : 0.0;
        }

        /// <summary>
        /// Залишки решти драйверів — рядком «драйвер:залишок,…» за
        /// зростанням номера драйвера, щоб зліпок був детермінованим.
        /// Залишок тіку сюди не входить: він зберігається окремим полем.
        /// </summary>
        internal string OtherFractionsForSave()
        {
            var keys = new List<TensionDriver>(_fractions.Keys);
            keys.Sort((a, b) => ((int)a).CompareTo((int)b));

            var sb = new System.Text.StringBuilder();
            foreach (var k in keys)
            {
                if (k == TensionDriver.CityTierTick) continue;
                double f = _fractions[k];
                if (f == 0.0) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append((int)k).Append(':').Append(f.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        internal void RestoreOtherFractions(string blob)
        {
            if (string.IsNullOrEmpty(blob)) return;
            foreach (var item in blob.Split(','))
            {
                var f = item.Split(':');
                if (f.Length < 2) continue;
                int id; double v;
                if (!int.TryParse(f[0], System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out id)) continue;
                if (!double.TryParse(f[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out v)) continue;
                _fractions[(TensionDriver)id] = v;
            }
        }

        /// <summary>
        /// Відновлення зі зліпка. Полоса не зберігається, а перераховується зі
        /// значення: інакше сейв, зроблений до правки порогів, повернув би полосу,
        /// якій це значення більше не відповідає.
        /// </summary>
        internal void RestoreForSave(int value, double fraction, int daysInBand)
        {
            Value = Clamp(value);
            _fractions.Clear();
            if (fraction != 0.0) _fractions[TensionDriver.CityTierTick] = fraction;
            _band = _cfg.BandFor(Value);
            DaysInCurrentBand = daysInBand < 0 ? 0 : daysInBand;
            _dayLedger.Clear();
        }

        public TensionState(TensionBalance cfg, int startValue = 0)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            Value = Clamp(startValue);
            _band = _cfg.BandFor(Value);
        }

        /// <summary>Цілочисельна зміна від конкретного драйвера.</summary>
        internal TensionChange Apply(TensionDriver driver, int delta, string sourceId)
        {
            var from = _band;
            if (!_cfg.IsAllowed(driver, delta))
                return Record(new TensionChange(driver, delta, 0, from, from, true, sourceId));

            int applied = Commit(delta);
            return Record(new TensionChange(driver, delta, applied, from, _band, false, sourceId));
        }

        /// <summary>
        /// Дробова зміна (фоновий тік, пасивний дренаж Храму/Укріплень).
        /// Залишок накопичується, тому тік 0.85/день за 20 днів дає рівно 17,
        /// а не 20 округлень підряд.
        /// </summary>
        internal TensionChange ApplyFractional(TensionDriver driver, double delta, string sourceId)
        {
            var from = _band;
            if (!_cfg.IsAllowed(driver, delta))
                return Record(new TensionChange(driver, (int)Math.Round(delta), 0, from, from, true, sourceId));

            double fraction = FractionOf(driver) + delta;
            int whole = (int)fraction;
            _fractions[driver] = fraction - whole;

            int applied = whole != 0 ? Commit(whole) : 0;
            return Record(new TensionChange(driver, whole, applied, from, _band, false, sourceId));
        }

        /// <summary>Викликається процесором дня після всіх кроків.</summary>
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
