using System;
using System.Collections.Generic;
using Game.Core.Balance;

namespace Game.Core.World
{
    /// <summary>Что накопители выдали за фазу.</summary>
    public readonly struct PulseTick
    {
        public readonly IReadOnlyList<string> FiredSourceIds;
        public readonly IReadOnlyList<Forewarning> Forewarnings;

        public PulseTick(IReadOnlyList<string> fired, IReadOnlyList<Forewarning> forewarnings)
        {
            FiredSourceIds = fired;
            Forewarnings = forewarnings;
        }
    }

    /// <summary>
    /// Пульс мира: параллельные накопители давления вместо планировщика событий.
    ///
    /// Полностью детерминирован. Непредсказуемость даёт не случайность, а
    /// неполнота информации: накопителей несколько, ставки у них разные и
    /// меняются от действий игрока, пороги и заряды скрыты, а список активных
    /// источников игрок узнаёт только через предвестники.
    /// </summary>
    public sealed class WorldPulse
    {
        private readonly List<IPressureSource> _sources = new List<IPressureSource>();
        private readonly Dictionary<string, PressureTrack> _tracks = new Dictionary<string, PressureTrack>();
        private readonly PulseBalance _cfg;

        public WorldPulse(PulseBalance cfg)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        }

        public void AddSource(IPressureSource source)
        {
            if (source == null || _tracks.ContainsKey(source.Id)) return;
            _sources.Add(source);
            _tracks.Add(source.Id, new PressureTrack(source, _cfg));
        }

        internal IReadOnlyDictionary<string, PressureTrack> Tracks => _tracks;

        /// <summary>Сколько накопителей реально работает сейчас — для инварианта «не меньше трёх».</summary>
        public int CountActive(PulseContext ctx)
        {
            int n = 0;
            for (int i = 0; i < _sources.Count; i++)
                if (_sources[i].IsActive(ctx)) n++;
            return n;
        }

        public PulseTick Advance(PulseContext ctx)
        {
            var forewarnings = new List<Forewarning>();

            // Накопление. Порядок обхода не влияет на результат: каждый источник
            // копит независимо, а ничьи при отборе разрешаются по Id.
            for (int i = 0; i < _sources.Count; i++)
            {
                var source = _sources[i];
                var track = _tracks[source.Id];

                if (!source.IsActive(ctx))
                    continue;

                track.Accumulate(source.InsistencePerDay(ctx), _cfg);

                // Кандидат в предвестники. Ступень НЕ засчитывается здесь:
                // засчитает её тот, кто доставит сигнал игроку (шаг Pulse).
                //
                // Выдаётся РОВНО ОДНА ступень за раз, а не текущая. Иначе
                // накопитель, проскочивший за ночь два порога, доносит сразу
                // третью, и вторая — единственная, обязанная назвать место —
                // теряется молча. Лестницу нужно проходить, а не перепрыгивать.
                int level = track.ForewarnLevel(_cfg);
                if (source.Announces && level > track.DeliveredLevel)
                    forewarnings.Add(new Forewarning(track.SourceId, track.DeliveredLevel + 1, track.DomainTag));
            }

            // Отбор сработавших: самые заполненные первыми, ничьи — по Id.
            var ready = new List<PressureTrack>();
            foreach (var source in _sources)
            {
                var track = _tracks[source.Id];
                if (source.IsActive(ctx) && track.IsReady(ctx.Day, _cfg))
                    ready.Add(track);
            }

            ready.Sort((a, b) =>
            {
                int byFill = b.Fill.CompareTo(a.Fill);
                if (byFill != 0) return byFill;
                return string.CompareOrdinal(a.SourceId, b.SourceId);
            });

            int budget = ctx.IsNight ? _cfg.MaxFiresPerNight : _cfg.MaxFiresPerDay;
            var fired = new List<string>();
            for (int i = 0; i < ready.Count && fired.Count < budget; i++)
            {
                ready[i].Fire(ctx.Day);
                fired.Add(ready[i].SourceId);
            }

            forewarnings.Sort((a, b) => string.CompareOrdinal(a.SourceId, b.SourceId));
            return new PulseTick(fired, forewarnings);
        }

        /// <summary>
        /// Отметить, что предвестники дошли до игрока. Вызывает шаг дня: только он
        /// знает, спал игрок или патрулировал.
        /// </summary>
        public void MarkDelivered(IReadOnlyList<Forewarning> delivered, int day)
        {
            if (delivered == null) return;
            for (int i = 0; i < delivered.Count; i++)
                if (_tracks.TryGetValue(delivered[i].SourceId, out var track))
                    track.MarkDelivered(delivered[i].Level, day);
        }

        /// <summary>Ступень, которую игрок УСЛЫШАЛ — для проверок и тестов.</summary>
        internal int DeliveredLevelOf(string sourceId)
        {
            return _tracks.TryGetValue(sourceId, out var track) ? track.DeliveredLevel : 0;
        }

        /// <summary>
        /// D1b (seamsForD1 B3, іменний предмет «Ріг вивідника», ефект
        /// «forewarn_boost»): додатковий, ЧИСТО АДИТИВНИЙ сейв — не змінює
        /// жодного існуючого члена/поведінки. Одноразово підкидає заряд
        /// накопичувачу так, ніби він сам накопичив трохи більше за день
        /// (той самий шлях, що й <see cref="PressureTrack.Accumulate"/> уже
        /// зве кожен тік) — WorldPulse.Advance сам гарантує "рівно одна
        /// ступінь за тік" (коментар вище), тож великий разовий заряд
        /// природно розтягується на кілька НАСТУПНИХ попереджень, що
        /// приходять РАНІШЕ, ніж прийшли б без нього, а не миттєво всі одразу.
        /// Немає джерела випадковості (R1) — сума фіксована й детермінована.
        /// </summary>
        public void BoostCharge(string sourceId, int amount)
        {
            if (amount <= 0) return;
            if (_tracks.TryGetValue(sourceId, out var track))
                track.Accumulate(amount, _cfg);
        }
    }
}
