using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Characters;

namespace Game.Core.Companions
{
    /// <summary>Один отклик ряби: кто и на сколько сдвинул лояльность и почему.</summary>
    public sealed class RippleEffect
    {
        public string CompanionId;
        public int LoyaltyDelta;
        public BondType Bond;
        public string Note;

        public RippleEffect(string id, int delta, BondType bond, string note)
        {
            CompanionId = id;
            LoyaltyDelta = delta;
            Bond = bond;
            Note = note;
        }
    }

    public sealed class RippleReport
    {
        public string TriggerId;
        public bool Betrayal;
        public readonly List<RippleEffect> Effects = new List<RippleEffect>();
    }

    /// <summary>
    /// Рябь по ростеру от смерти/предательства (US-9.6): идёт по ценностным связям —
    /// соратник павшего проседает по лояльности (скорбит), соперник не скорбит.
    /// **Ограждение от каскада**: тяжёлый отклик получают не более MaxRippleTargets
    /// соратников (по порядку), остальные — лёгкий — чтобы одна смерть не рушила весь
    /// ростер. Детерминировано (порядок ростера).
    /// </summary>
    public sealed class RosterDrama
    {
        private readonly RosterBonds _bonds;
        private readonly BalanceConfig _cfg;

        public RosterDrama(RosterBonds bonds, BalanceConfig cfg)
        {
            _bonds = bonds ?? new RosterBonds(null);
            _cfg = cfg ?? new BalanceConfig();
        }

        public RippleReport OnDeath(Roster roster, string deceasedId)
            => Ripple(roster, deceasedId, betrayal: false);

        public RippleReport OnBetrayal(Roster roster, string betrayerId)
            => Ripple(roster, betrayerId, betrayal: true);

        private RippleReport Ripple(Roster roster, string triggerId, bool betrayal)
        {
            var report = new RippleReport { TriggerId = triggerId, Betrayal = betrayal };
            var trigger = roster?.Get(triggerId);
            // Ушедшие к врагу в ряби не участвуют: их «скорбь» — ложная строка отчёта
            // и бессмысленный сдвиг лояльности того, кто уже не в отряде.
            if (trigger == null) return report;

            int heavyHit = betrayal ? _cfg.BetrayalKinLoyaltyHit : _cfg.MournLoyaltyHit;
            int mournedHeavily = 0;

            foreach (var c in roster.All)
            {
                if (c == trigger || !c.IsAlive || c.Status == CompanionStatus.Antagonist) continue;
                var bond = _bonds.Between(c, trigger);

                int delta;
                string note;
                if (bond == BondType.Kinship)
                {
                    if (mournedHeavily < _cfg.MaxRippleTargets)
                    {
                        delta = -heavyHit;
                        note = betrayal ? "чувствует себя преданным" : "скорбит";
                        mournedHeavily++;
                    }
                    else
                    {
                        delta = -_cfg.NeutralDeathLoyaltyHit;
                        note = "тронут (каскад огорожен)";
                    }
                }
                else if (bond == BondType.Friction)
                {
                    delta = _cfg.RivalDeathLoyaltyRelief;
                    note = betrayal ? "не удивлён" : "не скорбит";
                }
                else
                {
                    delta = -_cfg.NeutralDeathLoyaltyHit;
                    note = "тронут";
                }

                c.AdjustLoyalty(delta);
                report.Effects.Add(new RippleEffect(c.Id, delta, bond, note));
            }
            return report;
        }
    }
}
