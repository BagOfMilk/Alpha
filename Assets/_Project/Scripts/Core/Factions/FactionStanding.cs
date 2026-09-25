using System;

namespace Game.Core.Factions
{
    /// <summary>
    /// Полоса ставлення фракції до гравця — єдине, що виходить назовні
    /// (US-17.2, інваріант 3). Саме число (<see cref="FactionStanding.Value"/>)
    /// гравцю не показується ніколи.
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
    /// Рантайм-відношення однієї фракції (R5). Побудована за тим самим шаблоном, що
    /// <see cref="Game.Core.Pressure.TensionState"/>: сире значення — internal,
    /// полоса — public, зміна полоси зобов'язана породити сигнал (інваріант 4) —
    /// тут це C#-подія <see cref="BandChanged"/>, той самий прийом, яким
    /// Напруга вже вирішує це завдання.
    /// </summary>
    public sealed class FactionStanding
    {
        private readonly Balance.FactionBalance _cfg;
        private FactionStandingBand _band;

        /// <summary>Сире значення 0..100. НЕ показувати гравцю за жодних умов.</summary>
        internal int Value { get; private set; }

        public FactionStandingBand Band => _band;

        /// <summary>Інваріант 4: німого переходу полоси не буває.</summary>
        public event Action<FactionStandingBand, FactionStandingBand> BandChanged;

        public FactionStanding(Balance.FactionBalance cfg, int startValue)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            Value = Clamp(startValue);
            _band = _cfg.BandFor(Value);
        }

        /// <summary>
        /// Рухає відношення. sourceId — контекст для майбутнього журналу (за
        /// аналогією з TensionChange.SourceId); сам тип його поки не зберігає —
        /// у фракцій немає денного леджера, тільки полоса і сигнал зміни.
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
        /// Відновлення зі зліпка. Полоса не зберігається, а перераховується зі
        /// значення — тим самим прийомом, яким це робить TensionState.RestoreForSave:
        /// правка порогів між збереженням і завантаженням не лишить неузгоджену
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
