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

                int level = track.ForewarnLevel(_cfg);
                if (level > track.AnnouncedLevel)
                {
                    track.AnnouncedLevel = level;
                    forewarnings.Add(new Forewarning(track.SourceId, level, track.DomainTag));
                }
            }

            // Отбор сработавших: самые заполненные первыми, ничьи — по Id.
            var ready = new List<PressureTrack>();
            foreach (var source in _sources)
            {
                var track = _tracks[source.Id];
                if (source.IsActive(ctx) && track.IsReady(ctx.Day))
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

        /// <summary>Ступень предвестника источника — для проверки «кризис был объявлен».</summary>
        internal int AnnouncedLevelOf(string sourceId)
        {
            return _tracks.TryGetValue(sourceId, out var track) ? track.AnnouncedLevel : 0;
        }
    }
}
