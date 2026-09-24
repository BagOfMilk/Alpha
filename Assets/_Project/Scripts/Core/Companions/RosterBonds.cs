using System.Collections.Generic;
using Game.Core.Characters;

namespace Game.Core.Companions
{
    /// <summary>
    /// Связи ростера по ценностям (US-9.6, порт B4): вычисляются на лету из
    /// тегов ценностей трейтов напарников через <see cref="Companion.Traits"/>
    /// (<c>TraitSlots.Values</c>) — НЕ хранимая NxN-матрица. Даёт affinity
    /// между двумя и списки соратников/соперников одного напарника (для ряби
    /// в <see cref="RosterDrama"/> и баентера в <see cref="Banter"/>).
    /// </summary>
    public sealed class RosterBonds
    {
        private readonly ValueSystem _values;

        public RosterBonds(ValueSystem values)
        {
            _values = values ?? new ValueSystem();
        }

        public BondType Between(Companion a, Companion b)
        {
            if (a == null || b == null || a == b) return BondType.Neutral;
            return _values.Bond(a.Traits.Values, b.Traits.Values);
        }

        /// <summary>
        /// Живые напарники (кроме антагонистов — они больше не "свои", см. B4
        /// аудит §4.5) с заданным типом связи к <paramref name="c"/> (кроме него).
        /// </summary>
        public List<Companion> WithBond(Roster roster, Companion c, BondType bond)
        {
            var result = new List<Companion>();
            if (roster == null || c == null) return result;
            foreach (var other in roster.All)
            {
                if (other == c || other.IsDead || other.Status == CompanionStatus.Antagonist) continue;
                if (Between(c, other) == bond) result.Add(other);
            }
            return result;
        }

        public List<Companion> AlliesOf(Roster roster, Companion c) => WithBond(roster, c, BondType.Kinship);
        public List<Companion> RivalsOf(Roster roster, Companion c) => WithBond(roster, c, BondType.Friction);
    }
}
