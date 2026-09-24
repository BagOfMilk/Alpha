namespace Game.Core.Combat
{
    /// <summary>
    /// Ловушка на тайле (активка Выживания). Срабатывает один раз, когда
    /// вражеский ловушке юнит ВХОДИТ на тайл (движение/рывок/перестановка) —
    /// ПЛЕЙСХОЛДЕР-правило: проверяется точка прибытия, не каждый шаг пути.
    /// </summary>
    public sealed class Trap
    {
        public GridPos Pos;
        public Side OwnerSide;
        public string Name;
        public int Damage;
        public DamageType DamageType = DamageType.True;
        public StatusType StatusOnTrigger = StatusType.None;
    }
}
