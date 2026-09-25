namespace Game.Core.Combat
{
    /// <summary>
    /// Бойові стани: набір з 7 + Шред як окрема механіка зниження
    /// броні (на юніті накопичується лічильник шреду, не статус). Накладення
    /// гарантоване; Воля (похідна StatusDurationReduction) скорочує
    /// тривалість, мінімум 1 хід.
    ///
    /// У зрізі механічно реалізовані: Придушення, Кровотеча, Підпал.
    /// Решта значень оголошені під ітерацію здібностей.
    /// </summary>
    public enum StatusType
    {
        None = 0,
        Bleeding = 1,    // Кровотеча — урон/хід, ігнорує броню, True-урон
        Stunned = 2,     // Оглушення — пропуск ходу / втрата AP [ітерація здібностей]
        Suppressed = 3,  // Придушення — −точність + дорожчий рух, до кінця наст. ходу
        KnockedDown = 4, // Збитий з ніг — −захист, AP щоб встати [ітерація здібностей]
        Marked = 5,      // Мітка — +шанс по цілі [ітерація здібностей]
        Burning = 6,     // Підпал — урон вогнем/хід (DoT, множиться резистом вогню)
        Poisoned = 7     // Отрута — урон токсином/хід, обходить броню [ітерація здібностей]
    }

    /// <summary>Активний екземпляр стану на юніті: тип, залишок ходів, DoT-параметри.</summary>
    public sealed class StatusInstance
    {
        public StatusType Type;
        public int RemainingTurns;
        public int DotDamagePerTurn; // 0 — не DoT
        public DamageType DotType = DamageType.True;

        public StatusInstance(StatusType type, int remainingTurns, int dotDamagePerTurn = 0,
                              DamageType dotType = DamageType.True)
        {
            Type = type;
            RemainingTurns = remainingTurns;
            DotDamagePerTurn = dotDamagePerTurn;
            DotType = dotType;
        }
    }
}
