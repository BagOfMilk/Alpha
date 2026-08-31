namespace Game.Core.Loop
{
    /// <summary>
    /// Порядок шагов дня — В ОДНОМ МЕСТЕ, чтобы связи между системами читались
    /// с одного экрана и не заводились скрытые зависимости (инвариант проекта).
    ///
    /// Почему именно так:
    /// • производные считаются ровно один раз, в шаге Derived — двойной счёт
    ///   невозможен (US-18.2);
    /// • Напряжение тикает ДО Пульса, чтобы сегодняшняя полоса взвешивала
    ///   сегодняшнюю таблицу инцидентов;
    /// • инциденты резолвятся ПОСЛЕ — их исход влияет на веса завтра, а не
    ///   сегодня: обратной связи внутри одного дня нет;
    /// • Healing стоит после Напряжения и его не трогает — это гарантия US-1.3
    ///   («дни лечения не растят угрозу»), закреплённая тестом;
    /// • сигналы — последними, по финальному состоянию дня.
    ///
    /// Шаги, помеченные [ПОЗЖЕ], зарезервированы за своими этапами.
    /// </summary>
    public static class DayStepOrder
    {
        public const int Clock = 0;
        public const int Construction = 100;   // [ПОЗЖЕ] Э3
        public const int Production = 200;
        public const int Population = 300;     // [ПОЗЖЕ] Э3
        public const int Derived = 400;        // [ПОЗЖЕ] Э3
        public const int Hunger = 450;         // голодный день давит до тика Напряжения
        public const int Tension = 500;
        public const int Obligations = 600;    // [ПОЗЖЕ] Э4
        public const int Pulse = 700;          // [ПОЗЖЕ] Э1
        public const int Incidents = 800;      // [ПОЗЖЕ] Э1
        public const int Healing = 900;
        public const int Signals = 1000;
        public const int Report = 1100;
    }

    /// <summary>Фаза суток. Ночь получает своё поведение на этапе Э1.</summary>
    public enum DayPhase
    {
        Day = 0,
        Night = 1
    }
}
