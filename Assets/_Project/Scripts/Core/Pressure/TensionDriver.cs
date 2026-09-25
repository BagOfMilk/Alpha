namespace Game.Core.Pressure
{
    /// <summary>
    /// ЗАКРИТИЙ список джерел, яким дозволено рухати «Напругу»
    /// (Поправка №3.5). Нові системи зобов'язані модулювати наявні драйвери,
    /// а не додавати свої: додавання значення сюди — зміна дизайну,
    /// що вимагає поправки до GDD, а не рефакторинг.
    /// </summary>
    public enum TensionDriver
    {
        None = 0,

        // ---- Підвищують ----
        /// <summary>Фоновий тик від тіра міста — єдине пасивне джерело.</summary>
        CityTierTick = 1,
        /// <summary>Вибори в квестах (побічних і основних).</summary>
        QuestChoice = 2,
        /// <summary>Наслідки внутрішніх загроз.</summary>
        ThreatOutcome = 3,
        /// <summary>Стиль проходження: різанина замість тихого шляху (Поправка №1).</summary>
        PlaystyleBlood = 4,
        /// <summary>Голодний день у поселенні (Поправка №4) — стимул виходити назовні.</summary>
        Hunger = 5,

        // ---- Знижують ----
        CouncilRaid = 10,
        CouncilEdict = 11,
        TempleAura = 12,
        Fortifications = 13,
        EventOutcome = 14
    }
}
