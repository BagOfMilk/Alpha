namespace Game.Core.Signals
{
    /// <summary>
    /// Канал, яким гравець отримує знання про стан світу.
    /// Канали додаються разом зі своїм етапом: порожній канал без гілки
    /// в композиторі — це «стат, який ніхто не читає» (заборонено).
    /// Повний список каналів шару — в docs/SETTLEMENT_LAYER.md.
    /// </summary>
    public enum SignalChannel
    {
        /// <summary>Репліка городянина: те, що чутно на вулиці.</summary>
        CitizenLine = 0,
        /// <summary>Репліка напарника: він говорить прямо і по суті.</summary>
        CompanionLine = 1,
        /// <summary>Візуал міста: процвітання, занепад, накладення.</summary>
        Moodboard = 2,

        /// <summary>Передвісник: щось зріє. Три ступені гучності.</summary>
        Forewarning = 3,

        /// <summary>Доповідь напарника з позиції — зведення по його домену.</summary>
        PostReport = 4,

        /// <summary>Фон без адресата: звуки, погода, ніч.</summary>
        Ambient = 5
    }

    /// <summary>
    /// Наскільки гучно сигнал вимагає уваги. Визначає відбір у бюджет дня.
    /// </summary>
    public enum SignalUrgency
    {
        Ambient = 0,
        Notable = 1,
        Alarming = 2,
        Imminent = 3
    }
}
