namespace Game.Core.Loop
{
    /// <summary>
    /// Порядок кроків доби — В ОДНОМУ МІСЦІ, щоб зв'язки між системами читалися
    /// з одного екрана і не заводились приховані залежності (інваріант проєкту).
    ///
    /// Чому саме так:
    /// • похідні рахуються рівно один раз, у кроці Derived — подвійний рахунок
    ///   неможливий (US-18.2);
    /// • Напруга тикає ДО Пульсу, щоб сьогоднішня полоса зважувала
    ///   сьогоднішню таблицю інцидентів;
    /// • інциденти резолвляться ПІСЛЯ — їхній наслідок впливає на ваги завтра, а не
    ///   сьогодні: зворотного зв'язку всередині одного дня немає;
    /// • Healing стоїть після Напруги і її не чіпає — це гарантія US-1.3
    ///   («дні лікування не ростять загрозу»), закріплена тестом;
    /// • сигнали — останніми, за фінальним станом доби.
    ///
    /// Кроки, позначені [ПІЗНІШЕ], зарезервовані за своїми етапами.
    /// </summary>
    public static class DayStepOrder
    {
        public const int Clock = 0;
        public const int Construction = 100;   // міські роботи: рада, будівництво, храм (Поправка №6)
        public const int Production = 200;
        public const int Population = 300;     // люди приходять і йдуть, тір (Поправка №6)
        public const int Derived = 400;        // [ПІЗНІШЕ] Е3
        public const int Hunger = 450;         // голодний день тисне до тика Напруги
        public const int Tension = 500;
        /// <summary>Готовність громади до фіналу (Поправка №7, R8) — накопичується паралельно Напрузі.</summary>
        public const int Readiness = 550;
        public const int Obligations = 600;    // [ПІЗНІШЕ] Е4
        public const int Pulse = 700;          // [ПІЗНІШЕ] Е1
        public const int Incidents = 800;      // [ПІЗНІШЕ] Е1
        /// <summary>Хід гравця: конвеєр зупиняється і чекає рішення.</summary>
        public const int PlayerResolution = 850;
        public const int Healing = 900;
        public const int Signals = 1000;
        public const int Report = 1100;
    }

    /// <summary>Фаза доби. Ніч отримує свою поведінку на етапі Е1.</summary>
    public enum DayPhase
    {
        Day = 0,
        Night = 1
    }
}
