namespace Game.Core.World
{
    /// <summary>
    /// Джерело тиску. Ставка (Наполегливість) — ЧИСТА ФУНКЦІЯ від стану
    /// світу: вона змінюється від дій гравця, і саме тому розрахунковий строк
    /// «пливе» після кожного ходу, хоча випадковості нема ні грама.
    /// </summary>
    public interface IPressureSource
    {
        string Id { get; }
        WorldEventKind Kind { get; }
        string DomainTag { get; }

        /// <summary>Скільки очок заряду набігає за цю фазу.</summary>
        int InsistencePerDay(PulseContext ctx);

        int Threshold { get; }
        int CooldownDays { get; }

        /// <summary>Неактивне джерело не накопичує і не видає передвісників.</summary>
        bool IsActive(PulseContext ctx);

        /// <summary>
        /// Чи видає джерело передвісники.
        ///
        /// Майже завжди так: загроза, про яку не попередили, — нечесна.
        /// Але авторський вузол за розкладом чуткою не передвіщується: його
        /// «попередження» — сама сцена. Без цієї відмінності поставлена
        /// сцена сипала драбиною чуток про себе саму, і справжні передвісники
        /// тонули в ній (спіймано прогоном зрізу 23.09.2026).
        /// </summary>
        bool Announces { get; }
    }

    /// <summary>Зріз світу, від якого залежать ставки накопичувачів.</summary>
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
