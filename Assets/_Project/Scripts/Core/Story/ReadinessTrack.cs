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
    /// ЯКІР: скільки підготовки громада встигла накопичити до ночі фіналу.
    /// СПОЖИВАЧ: <see cref="Finale.BuildDam"/>/<see cref="Finale.BuildAssault"/>
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

        /// <summary>
        /// Скільки віх Готовності вже зараховано (кожен непорожній <see cref="Add"/>
        /// — одна віха: вилазка/квест/стройка/страх/указ ради). Гравцю показується
        /// (§4.2 <c>ReadinessView.MilestonesReached</c>) — сама лише лічильник подій,
        /// не приховане число (allow-list §4.9), тому internal тут не потрібен.
        /// </summary>
        public int MilestonesReached { get; private set; }

        public ReadinessBand Band => _band;

        /// <summary>Зміна полоси зобов'язана породити сигнал (інваріант 4).</summary>
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
        /// викликач — завжди Game.Core (той самий прийом, що в TensionDrivers).
        /// </summary>
        internal void Add(int amount)
        {
            if (amount == 0) return;

            MilestonesReached++;

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

        /// <summary>
        /// "<c>значення|лічильник_віх</c>" (§4.8: "внутрішнє значення + лічильник
        /// досягнутих позначок") — MilestonesReached персистується тут же, а не
        /// перераховується з нуля, інакше після Save/Load лічильник завжди
        /// показував би 0, хай скільки віх було зараховано до збереження.
        /// </summary>
        public string CaptureState() =>
            Value.ToString(CultureInfo.InvariantCulture) + "|" + MilestonesReached.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Полоса НЕ зберігається окремо, а перераховується зі значення — так
        /// само, як TensionState.RestoreForSave: якщо пороги зміняться між
        /// патчами, стара збірка не поверне полосу, якій значення більше не
        /// відповідає. Подія на відновленні НЕ емітиться (те саме рішення, що
        /// й у TensionState) — завантаження не подія гри, а її продовження.
        /// Формат зворотно сумісний зі старим "<c>значення</c>" без лічильника
        /// (частина без '|' — лічильник тоді 0).
        /// </summary>
        public void RestoreState(string blob)
        {
            string valuePart = blob;
            string countPart = null;
            if (!string.IsNullOrEmpty(blob))
            {
                int bar = blob.IndexOf('|');
                if (bar >= 0)
                {
                    valuePart = blob.Substring(0, bar);
                    countPart = blob.Substring(bar + 1);
                }
            }

            int v;
            Value = int.TryParse(valuePart, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) && v > 0 ? v : 0;
            _band = _cfg.BandFor(Value);

            int reached;
            MilestonesReached = countPart != null &&
                int.TryParse(countPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out reached) && reached > 0
                ? reached : 0;
        }
    }
}
