using System.Collections.Generic;
using Game.Core.Pressure;

namespace Game.Core.World
{
    /// <summary>
    /// Пул инцидентов, взвешенный полосой Напряжения и тиром города (US-11.3).
    /// Отбор детерминированный: при равном весе побеждает меньший Id.
    /// </summary>
    public sealed class IncidentTable
    {
        private readonly List<IncidentDefinition> _all = new List<IncidentDefinition>();

        public IReadOnlyList<IncidentDefinition> All => _all;

        public void Add(IncidentDefinition definition)
        {
            if (definition != null) _all.Add(definition);
        }

        public void AddRange(IEnumerable<IncidentDefinition> definitions)
        {
            if (definitions == null) return;
            foreach (var d in definitions) Add(d);
        }

        /// <summary>Какие инциденты вообще возможны в этих условиях.</summary>
        public List<IncidentDefinition> Eligible(TensionBand band, int tier, bool isNight, bool crisisOnly)
        {
            var result = new List<IncidentDefinition>();
            for (int i = 0; i < _all.Count; i++)
            {
                var d = _all[i];
                if (d.IsCrisis != crisisOnly) continue;
                if (band < d.MinBand || band > d.MaxBand) continue;
                if (tier < d.MinTier) continue;
                if (d.NightOnly && !isNight) continue;
                result.Add(d);
            }
            return result;
        }

        /// <summary>
        /// Выбор из подходящих. Вес умножается на «сдвиг» полосы: чем напряжённее
        /// город, тем тяжелее то, что вылезает (US-11.1: низкое → мелочь,
        /// высокое → организованная преступность).
        /// </summary>
        public IncidentDefinition Pick(TensionBand band, int tier, bool isNight, bool crisisOnly, int selector)
        {
            var pool = Eligible(band, tier, isNight, crisisOnly);
            if (pool.Count == 0) return null;

            pool.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

            int total = 0;
            var weights = new int[pool.Count];
            for (int i = 0; i < pool.Count; i++)
            {
                int w = pool[i].Weight + (int)band * (pool[i].MinBand > TensionBand.Calm ? 5 : 0);
                if (w < 1) w = 1;
                weights[i] = w;
                total += w;
            }

            // Детерминированный выбор: селектор приходит из состояния мира
            // (номер дня и заряд накопителя), а не из генератора случайных чисел.
            int point = total <= 0 ? 0 : ((selector % total) + total) % total;
            for (int i = 0; i < pool.Count; i++)
            {
                point -= weights[i];
                if (point < 0) return pool[i];
            }
            return pool[pool.Count - 1];
        }
    }
}
