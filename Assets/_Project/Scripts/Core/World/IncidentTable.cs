using System.Collections.Generic;
using Game.Core.Pressure;

namespace Game.Core.World
{
    /// <summary>
    /// Пул інцидентів, зважений полосою Напруги і тіром міста (US-11.3).
    /// Відбір детермінований: за рівної ваги перемагає менший Id.
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

        /// <summary>
        /// Які інциденти взагалі можливі за цих умов. Пустий sourceId —
        /// «будь-яке джерело»: так пул поводиться, як і раніше, у вузьких тестах.
        /// </summary>
        public List<IncidentDefinition> Eligible(TensionBand band, int tier, bool isNight,
            bool crisisOnly, string sourceId = null)
        {
            var result = new List<IncidentDefinition>();
            for (int i = 0; i < _all.Count; i++)
            {
                var d = _all[i];
                if (d.IsCrisis != crisisOnly) continue;
                if (!string.IsNullOrEmpty(sourceId) && !string.IsNullOrEmpty(d.SourceId)
                    && d.SourceId != sourceId) continue;
                if (band < d.MinBand || band > d.MaxBand) continue;
                if (tier < d.MinTier) continue;
                if (d.NightOnly && !isNight) continue;
                result.Add(d);
            }
            return result;
        }

        /// <summary>
        /// Вибір із підхожих. Вага множиться на «зсув» полоси: чим напруженіше
        /// місто, тим важче те, що вилазить (US-11.1: низьке → дрібниця,
        /// високе → організована злочинність).
        /// </summary>
        public IncidentDefinition Pick(TensionBand band, int tier, bool isNight, bool crisisOnly,
            int selector, string sourceId = null)
        {
            var pool = Eligible(band, tier, isNight, crisisOnly, sourceId);
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

            // Детермінований вибір: селектор приходить зі стану світу
            // (номер дня і заряд накопичувача), а не з генератора випадкових чисел.
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
