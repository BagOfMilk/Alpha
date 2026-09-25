namespace Game.Core.Combat
{
    /// <summary>
    /// Пастка на тайлі (активка Виживання). Спрацьовує один раз, коли
    /// ворожий пастці юніт ВХОДИТЬ на тайл (рух/ривок/перестановка) —
    /// ПЛЕЙСХОЛДЕР-правило: перевіряється точка прибуття, не кожен крок шляху.
    /// </summary>
    public sealed class Trap
    {
        public GridPos Pos;
        public Side OwnerSide;
        public string Name;

        /// <summary>Здібність, яка поставила пастку, — її ключ називає пастку гравцю (журнал бою, R7); <see cref="Name"/> — тільки для трейсу.</summary>
        public string AbilityId;

        public int Damage;
        public DamageType DamageType = DamageType.True;
        public StatusType StatusOnTrigger = StatusType.None;
    }
}
