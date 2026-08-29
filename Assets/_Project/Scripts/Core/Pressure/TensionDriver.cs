namespace Game.Core.Pressure
{
    /// <summary>
    /// ЗАКРЫТЫЙ список источников, которым позволено двигать «Напряжение»
    /// (Поправка №3.5). Новые системы обязаны модулировать существующие драйверы,
    /// а не добавлять свои: добавление значения сюда — изменение дизайна,
    /// требующее поправки к GDD, а не рефакторинг.
    /// </summary>
    public enum TensionDriver
    {
        None = 0,

        // ---- Повышают ----
        /// <summary>Фоновый тик от тира города — единственный пассивный источник.</summary>
        CityTierTick = 1,
        /// <summary>Выборы в квестах (побочных и основных).</summary>
        QuestChoice = 2,
        /// <summary>Исходы внутренних угроз.</summary>
        ThreatOutcome = 3,
        /// <summary>Стиль прохождения: резня вместо тихого пути (Поправка №1).</summary>
        PlaystyleBlood = 4,

        // ---- Понижают ----
        CouncilRaid = 10,
        CouncilEdict = 11,
        TempleAura = 12,
        Fortifications = 13,
        EventOutcome = 14
    }
}
