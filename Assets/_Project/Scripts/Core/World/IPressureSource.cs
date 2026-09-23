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

        /// <summary>
        /// Выдаёт ли источник предвестники.
        ///
        /// Почти всегда да: угроза, о которой не предупредили, — нечестная.
        /// Но авторский узел по расписанию слухом не предваряется: его
        /// «предупреждение» — сама сцена. Без этого различия поставленная
        /// сцена сыпала лестницей слухов о себе самой, и настоящие предвестники
        /// тонули в ней (поймано прогоном среза 23.09.2026).
        /// </summary>
        bool Announces { get; }
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
