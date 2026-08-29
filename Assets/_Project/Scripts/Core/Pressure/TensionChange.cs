namespace Game.Core.Pressure
{
    /// <summary>
    /// Запись в дневном журнале: кто, сколько запросил и сколько реально получил.
    /// Нужна для тестов-гарантий (например «дни лечения не растят угрозу»)
    /// и для слоя сигналов — по смене полосы он обязан выдать сигнал.
    /// </summary>
    public readonly struct TensionChange
    {
        public readonly TensionDriver Driver;
        public readonly int Requested;
        public readonly int Applied;
        public readonly TensionBand From;
        public readonly TensionBand To;
        /// <summary>Драйвер оказался вне белого списка — изменение не применено.</summary>
        public readonly bool Rejected;
        public readonly string SourceId;

        public TensionChange(TensionDriver driver, int requested, int applied,
            TensionBand from, TensionBand to, bool rejected, string sourceId)
        {
            Driver = driver;
            Requested = requested;
            Applied = applied;
            From = from;
            To = to;
            Rejected = rejected;
            SourceId = sourceId;
        }

        public bool ChangedBand => From != To;
    }
}
