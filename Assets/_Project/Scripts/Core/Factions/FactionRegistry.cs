using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Game.Core.Factions
{
    /// <summary>
    /// Реестр отношений фракций (R5) — часть слепка кампании (IStateBlob), тем
    /// же приёмом, каким CityWorks уже отдаёт себя в CityState.
    /// </summary>
    public sealed class FactionRegistry : Loop.IStateBlob
    {
        private readonly Balance.FactionBalance _cfg;
        private readonly Dictionary<string, FactionStanding> _byId = new Dictionary<string, FactionStanding>(StringComparer.Ordinal);
        private readonly List<string> _order = new List<string>();

        public FactionRegistry(Balance.FactionBalance cfg)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        }

        public IReadOnlyList<string> FactionIds => _order;

        public FactionStanding Register(string factionId, int? startValue = null)
        {
            if (string.IsNullOrEmpty(factionId)) return null;
            if (_byId.TryGetValue(factionId, out var existing)) return existing;

            var standing = new FactionStanding(_cfg, startValue ?? _cfg.StartingStanding);
            _byId.Add(factionId, standing);
            _order.Add(factionId);
            return standing;
        }

        public FactionStanding Get(string factionId)
            => factionId != null && _byId.TryGetValue(factionId, out var s) ? s : null;

        public FactionStandingBand BandOf(string factionId) => Get(factionId)?.Band ?? FactionStandingBand.Neutral;

        /// <summary>
        /// Единственная точка, которой позволено двигать отношение фракции извне
        /// (Decree/Diplomacy/квесты/угрозы — все проходят здесь, а не лезут в
        /// FactionStanding.Apply напрямую, он internal).
        /// </summary>
        public void ApplySocialConsequence(string factionId, int delta) => Get(factionId)?.Apply(delta, "social");

        // ---- слепок: f:<id>:<value>,... ----
        public string CaptureState()
        {
            var sb = new StringBuilder();
            var ids = new List<string>(_order);
            ids.Sort(StringComparer.Ordinal);

            for (int i = 0; i < ids.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(ids[i]).Append(':').Append(_byId[ids[i]].Value.ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        public void RestoreState(string blob)
        {
            if (string.IsNullOrEmpty(blob)) return;

            foreach (var part in blob.Split(','))
            {
                var f = part.Split(':');
                if (f.Length < 2) continue;

                if (_byId.TryGetValue(f[0], out var standing) &&
                    int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                {
                    standing.RestoreForSave(value);
                }
            }
        }
    }
}
