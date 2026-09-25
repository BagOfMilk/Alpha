using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Characters;

namespace Game.Core.Companions
{
    /// <summary>Один відгук ряби: хто і на скільки зсунув лояльність і чому.</summary>
    public sealed class RippleEffect
    {
        public readonly string CompanionId;
        public readonly BondType Bond;
        public readonly string Note;
        /// <summary>Підсумкова смуга — те, що в принципі можна показати гравцю.</summary>
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
    /// Рябь по ростеру від смерті/зради (US-9.6, порт B4): йде за
    /// ціннісними зв'язками — соратник загиблого просідає за лояльністю (скорбить),
    /// суперник не скорбить. Огородження від каскаду: важкий відгук отримують не
    /// більше <c>MaxRippleTargets</c> соратників (за порядком ростера — детермін.
    /// інваріант 1), решта — легкий, щоб одна смерть не рушила весь склад.
    ///
    /// Метод — internal (не <see cref="Companion.ApplyLoyaltyDelta"/> напряму,
    /// а той самий контур інваріанта 3): <see cref="RippleEffect"/> не несе сирих
    /// чисел, тільки смугу, однак сама операція — внутрішня механіка Core,
    /// а не те, що Game.Gameplay смикає напряму (вона йде через майбутній
    /// GameSession-фасад D1).
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
            // Ті, хто пішов до ворога, у ряби не беруть участь: їхня "скорбота" — хибний рядок
            // звіту і безглуздий зсув лояльності того, кого вже немає в строю.
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
