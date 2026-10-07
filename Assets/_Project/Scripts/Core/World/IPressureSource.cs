namespace Game.Core.World
{
    /// <summary>
    /// Джерело тиску. Ставка (Наполегливість) — ЧИСТА ФУНКЦІЯ від стану
    /// світу: вона змінюється від дій гравця, і саме тому розрахунковий строк
    /// «пливе» після кожного ходу, хоча випадковості нема ні грама.
    /// </summary>
    public interface IPressureSource
    {
        string Id { get; }
        WorldEventKind Kind { get; }
        string DomainTag { get; }

        /// <summary>Скільки очок заряду набігає за цю фазу.</summary>
        int InsistencePerDay(PulseContext ctx);

        int Threshold { get; }
        int CooldownDays { get; }

        /// <summary>Неактивне джерело не накопичує і не видає передвісників.</summary>
        bool IsActive(PulseContext ctx);

        /// <summary>
        /// Чи видає джерело передвісники.
        ///
        /// Майже завжди так: загроза, про яку не попередили, — нечесна.
        /// Але авторський вузол за розкладом чуткою не передвіщується: його
        /// «попередження» — сама сцена. Без цієї відмінності поставлена
        /// сцена сипала драбиною чуток про себе саму, і справжні передвісники
        /// тонули в ній (спіймано прогоном зрізу 23.09.2026).
        /// </summary>
        bool Announces { get; }
    }

    /// <summary>
    /// Джерело, що може «тліти»: воно тікає (рахується активним — інваріант 2),
    /// але ще не озброєне — не видає передвісників і не спрацьовує, а заряд
    /// не піднімається вище стелі <see cref="SmolderCeiling"/>.
    ///
    /// Навіщо: інваріант 2 вимагає не менше трьох накопичувачів, що тікають
    /// ЗАВЖДИ, а передвісник «не бреше» (SETTLEMENT_LAYER §5.1) — загроза, якої
    /// у цій обстановці ще не може бути, не сповіщається. Нічна злочинність
    /// вдень і криза в спокійному місті дрімають під стелею нижче першого
    /// ступеня передвісника: заряд є, голосу — нема.
    /// </summary>
    public interface ISmolderingSource : IPressureSource
    {
        /// <summary>Чи тліє джерело в цій обстановці (тоді воно не озброєне).</summary>
        bool IsSmoldering(PulseContext ctx);

        /// <summary>
        /// Стеля заряду на тлінні — частка порогу. Має лежати нижче першого
        /// ступеня передвісника (<see cref="Balance.PulseBalance.Forewarn1At"/>),
        /// інакше тління саме стало б голосом. Охоронець —
        /// <c>PressureInvariantTests</c>.
        /// </summary>
        double SmolderCeiling { get; }
    }

    /// <summary>
    /// Зріз світу, від якого залежать ставки накопичувачів.
    ///
    /// Інваріант 2: кожен накопичувач читає СВОЮ змінну, а не одну спільну —
    /// інакше, прочитавши одне число, гравець читає весь Пульс (AUDIT G11).
    /// Вулиця читає людність і голод (<see cref="CrowdBand"/>, <see cref="IsHungry"/>),
    /// ніч — Уклад і патруль (<see cref="OrderLevel"/>, <see cref="IsPatrolling"/>),
    /// криза — полосу Напруги (<see cref="TensionBandIndex"/>). Нових шкал тут
    /// нема: усе це вже лежить у конвеєрі дня.
    /// </summary>
    public readonly struct PulseContext
    {
        public readonly int Day;
        public readonly bool IsNight;
        public readonly int Tier;
        public readonly int TensionBandIndex;
        public readonly bool IsPatrolling;

        /// <summary>Учора поселенню не вистачило їжі (Поправка №4).</summary>
        public readonly bool IsHungry;

        /// <summary>Полоса людності 0..4 (<c>PopulationState.CrowdBand</c>); 2 — стартове село.</summary>
        public readonly int CrowdBand;

        /// <summary>Уклад: 0 Вольниця → 3 Затвор; 1 — стартовий.</summary>
        public readonly int OrderLevel;

        /// <summary>
        /// Надбавка погоди до нічної ставки у відсотках (Поправка №21.2): туман і буря
        /// ховають злодіїв. Нуль — як у ясну ніч; так само поводиться і default-структура.
        /// Погода не має свого накопичувача — вона лише модулює нічний (інваріант 5).
        /// </summary>
        public readonly int NightWeatherBonusPercent;

        public PulseContext(int day, bool isNight, int tier, int tensionBandIndex, bool isPatrolling,
            bool isHungry = false, int crowdBand = 2, int orderLevel = 1, int nightWeatherBonusPercent = 0)
        {
            NightWeatherBonusPercent = nightWeatherBonusPercent;
            Day = day;
            IsNight = isNight;
            Tier = tier;
            TensionBandIndex = tensionBandIndex;
            IsPatrolling = isPatrolling;
            IsHungry = isHungry;
            CrowdBand = crowdBand;
            OrderLevel = orderLevel;
        }
    }
}
