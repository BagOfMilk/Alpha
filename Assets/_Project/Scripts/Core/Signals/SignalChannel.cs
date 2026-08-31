namespace Game.Core.Signals
{
    /// <summary>
    /// Канал, по которому игрок получает знание о состоянии мира.
    /// Каналы добавляются вместе со своим этапом: пустой канал без ветки
    /// в композиторе — это «стат, который никто не читает» (запрещено).
    /// Полный список каналов слоя — в docs/SETTLEMENT_LAYER.md.
    /// </summary>
    public enum SignalChannel
    {
        /// <summary>Реплика горожанина: то, что слышно на улице.</summary>
        CitizenLine = 0,
        /// <summary>Реплика напарника: он говорит прямо и по делу.</summary>
        CompanionLine = 1,
        /// <summary>Визуал города: процветание, упадок, наложения.</summary>
        Moodboard = 2,

        /// <summary>Предвестник: что-то зреет. Три ступени громкости.</summary>
        Forewarning = 3,

        /// <summary>Доклад напарника с позиции — сводка по его домену.</summary>
        PostReport = 4,

        /// <summary>Фон без адресата: звуки, погода, ночь.</summary>
        Ambient = 5
    }

    /// <summary>
    /// Насколько громко сигнал требует внимания. Определяет отбор в бюджет дня.
    /// </summary>
    public enum SignalUrgency
    {
        Ambient = 0,
        Notable = 1,
        Alarming = 2,
        Imminent = 3
    }
}
