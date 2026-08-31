namespace Game.Core.World
{
    /// <summary>
    /// Источник давления. Ставка (Настойчивость) — ЧИСТАЯ ФУНКЦИЯ от состояния
    /// мира: она меняется от действий игрока, и именно поэтому расчётный срок
    /// «плывёт» после каждого хода, хотя случайности нет ни грамма.
    /// </summary>
    public interface IPressureSource
    {
        string Id { get; }
        WorldEventKind Kind { get; }
        string DomainTag { get; }

        /// <summary>Сколько очков заряда набегает за эту фазу.</summary>
        int InsistencePerDay(PulseContext ctx);

        int Threshold { get; }
        int CooldownDays { get; }

        /// <summary>Неактивный источник не копит и не выдаёт предвестников.</summary>
        bool IsActive(PulseContext ctx);
    }

    /// <summary>Срез мира, от которого зависят ставки накопителей.</summary>
    public readonly struct PulseContext
    {
        public readonly int Day;
        public readonly bool IsNight;
        public readonly int Tier;
        public readonly int TensionBandIndex;
        public readonly bool IsPatrolling;

        public PulseContext(int day, bool isNight, int tier, int tensionBandIndex, bool isPatrolling)
        {
            Day = day;
            IsNight = isNight;
            Tier = tier;
            TensionBandIndex = tensionBandIndex;
            IsPatrolling = isPatrolling;
        }
    }
}
