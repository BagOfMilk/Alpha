namespace Game.Core.Pressure
{
    /// <summary>
    /// Запис у денному журналі: хто, скільки запросив і скільки реально отримав.
    /// Потрібна для тестів-гарантій (наприклад «дні лікування не ростять загрозу»)
    /// і для шару сигналів — за зміною полоси він зобов'язаний видати сигнал.
    /// </summary>
    public readonly struct TensionChange
    {
        public readonly TensionDriver Driver;
        public readonly int Requested;
        public readonly int Applied;
        public readonly TensionBand From;
        public readonly TensionBand To;
        /// <summary>Драйвер опинився поза білим списком — зміну не застосовано.</summary>
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
