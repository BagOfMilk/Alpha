using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Characters;

namespace Game.Core.Companions
{
    /// <summary>Один отклик ряби: кто и на сколько сдвинул лояльность и почему.</summary>
    public sealed class RippleEffect
    {
        public readonly string CompanionId;
        public readonly BondType Bond;
        public readonly string Note;
        /// <summary>Итоговая полоса — то, что в принципе можно показать игроку.</summary>
        public readonly LoyaltyBand Band;

        public RippleEffect(string companionId, BondType bond, string note, LoyaltyBand band)
        {
            CompanionId = companionId;
            Bond = bond;
            Note = note;
            Band = band;
        }
    }

    public sealed class RippleReport
    {
        public string TriggerId;
        public bool Betrayal;
        public readonly List<RippleEffect> Effects = new List<RippleEffect>();
    }

    /// <summary>
    /// Рябь по ростеру от смерти/предательства (US-9.6, порт B4): идёт по
    /// ценностным связям — соратник павшего проседает по лояльности (скорбит),
    /// соперник не скорбит. Ограждение от каскада: тяжёлый отклик получают не
    /// более <c>MaxRippleTargets</c> соратников (по порядку ростера — детермин.
    /// инвариант 1), остальные — лёгкий, чтобы одна смерть не рушила весь состав.
    ///
    /// Метод — internal (не <see cref="Companion.ApplyLoyaltyDelta"/> напрямую,
    /// но тот же контур инварианта 3): <see cref="RippleEffect"/> не несёт сырых
    /// чисел, только полосу, однако сама операция — внутренняя механика Core,
    /// а не то, что Game.Gameplay дёргает напрямую (она идёт через будущий
    /// GameSession-факад D1).
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

        internal RippleReport OnDeath(Roster roster, string deceasedId)
            => Ripple(roster, deceasedId, betrayal: false);

        internal RippleReport OnBetrayal(Roster roster, string betrayerId)
            => Ripple(roster, betrayerId, betrayal: true);

        private RippleReport Ripple(Roster roster, string triggerId, bool betrayal)
        {
            var report = new RippleReport { TriggerId = triggerId, Betrayal = betrayal };
            var trigger = roster?.Get(triggerId);
            // Ушедшие к врагу в ряби не участвуют: их "скорбь" — ложная строка
            // отчёта и бессмысленный сдвиг лояльности того, кого уже нет в строю.
            if (trigger == null) return report;

            var social = _cfg.CompanionSocial;
            int heavyHit = betrayal ? social.BetrayalKinLoyaltyHit : social.MournLoyaltyHit;
            int mournedHeavily = 0;

            foreach (var c in roster.All)
            {
                if (c == trigger || c.IsDead || c.Status == CompanionStatus.Antagonist) continue;
                var bond = _bonds.Between(c, trigger);

                int delta;
                string note;
                if (bond == BondType.Kinship)
                {
                    if (mournedHeavily < social.MaxRippleTargets)
                    {
                        delta = -heavyHit;
                        note = betrayal ? "чувствует себя преданным" : "скорбит";
                        mournedHeavily++;
                    }
                    else
                    {
                        delta = -social.NeutralDeathLoyaltyHit;
                        note = "тронут (каскад огорожен)";
                    }
                }
                else if (bond == BondType.Friction)
                {
                    delta = social.RivalDeathLoyaltyRelief;
                    note = betrayal ? "не удивлён" : "не скорбит";
                }
                else
                {
                    delta = -social.NeutralDeathLoyaltyHit;
                    note = "тронут";
                }

                var change = c.ApplyLoyaltyDelta(delta, betrayal ? "roster_drama:betrayal" : "roster_drama:death");
                report.Effects.Add(new RippleEffect(c.Id, bond, note, change.To));
            }
            return report;
        }
    }
}
