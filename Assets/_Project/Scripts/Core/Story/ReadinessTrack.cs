using System;
using System.Globalization;
using Game.Core.Balance;

namespace Game.Core.Story
{
    /// <summary>Полоса Готовності громади до фіналу (R8, Поправка №7).</summary>
    public enum ReadinessBand
    {
        Unprepared = 0,
        Bracing = 1,
        Ready = 2,
        Fortified = 3
    }

    /// <summary>
    /// Готовність громади (R8) — скрита шкала того ж роду, що й Напруга
    /// (інваріант 2: третій+ активний накопичувач зі своєю ставкою).
    ///
    /// ЯКОРЬ: скільки підготовки громада встигла накопити до ночі фіналу.
    /// ПОТРЕБИТЕЛЬ: <see cref="Finale.BuildDam"/>/<see cref="Finale.BuildAssault"/>
    /// зсувають пороги й склад ворога за <see cref="Band"/>. СИГНАЛ: подія на
    /// кожну зміну полоси через <see cref="BandChanged"/> (інваріант 4) — той
    /// самий шаблон, що в <c>TensionState</c>/майбутньої <c>FactionStanding</c>.
    /// </summary>
    public sealed class ReadinessTrack : Loop.IStateBlob
    {
        private readonly ReadinessBalance _cfg;
        private ReadinessBand _band;

        /// <summary>Сире значення. Гравцю НЕ показується ніколи — лише полоса.</summary>
        internal int Value { get; private set; }

        public ReadinessBand Band => _band;

        /// <summary>Смена полосы обязана породить сигнал (інваріант 4).</summary>
        public event Action<ReadinessBand, ReadinessBand> BandChanged;

        public ReadinessTrack(ReadinessBalance cfg)
        {
            _cfg = cfg ?? new ReadinessBalance();
            _band = _cfg.BandFor(0);
        }

        /// <summary>
        /// Додати підготовку за віхою (вилазка/квест/стройка/страх/указ ради) —
        /// викликає <see cref="ReadinessTickStep"/> для тих віх, що видно з
        /// конвеєра дня, або D1 напряму для тих, що трапляються поза конвеєром
        /// (вилазка, квест — R6). Internal: число — не для Game.Gameplay,
        /// викликач — завжди Game.Core (той самий приём, що в TensionDrivers).
        /// </summary>
        internal void Add(int amount)
        {
            if (amount == 0) return;

            int before = Value;
            Value += amount;
            if (Value < 0) Value = 0;
            if (Value == before) return;

            var next = _cfg.BandFor(Value);
            if (next != _band)
            {
                var old = _band;
                _band = next;
                BandChanged?.Invoke(old, next);
            }
        }

        // ---- слепок ----

        public string CaptureState() => Value.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Полоса НЕ зберігається окремо, а перераховується зі значення — так
        /// само, як TensionState.RestoreForSave: якщо пороги зміняться між
        /// патчами, стара сборка не поверне полосу, якій значення більше не
        /// відповідає. Подія на відновленні НЕ емітиться (те саме рішення, що
        /// й у TensionState) — завантаження не подія гри, а її продовження.
        /// </summary>
        public void RestoreState(string blob)
        {
            int v;
            Value = int.TryParse(blob, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) && v > 0 ? v : 0;
            _band = _cfg.BandFor(Value);
        }
    }
}
