using Game.Core.Balance;

namespace Game.Core.World
{
    /// <summary>
    /// Накопичувач тиску. Замінює кидок кубика — і робить це краще:
    /// кидку нема чого сказати заздалегідь, а накопичувачу є. Заповнення на 55/80/95 %
    /// перетворюється на передвісники трьох ступенів.
    ///
    /// Ступінь вважається пройденою тільки коли гравець її ПОЧУВ. Рівень,
    /// виданий вночі сплячому, лишається непідтвердженим і буде запропонований знову:
    /// інакше попередження згорало б мовчки, а криза приходила б до того, хто
    /// нічого не чув (US-11.2, ризик R8).
    /// </summary>
    public sealed class PressureTrack
    {
        public string SourceId { get; }
        public WorldEventKind Kind { get; }
        public string DomainTag { get; }

        internal int Charge { get; private set; }
        internal int Threshold { get; }

        public int LastFiredDay { get; private set; }
        private bool _everFired;
        public int CooldownDays { get; }

        /// <summary>Найбільша ступінь, яка ДІЙШЛА до гравця (не плутати з досягнутою).</summary>
        internal int DeliveredLevel { get; private set; }

        /// <summary>Календарна доба, коли гравець почув третю ступінь.</summary>
        internal int DeliveredLevel3Day { get; private set; }
        private bool _deliveredThree;

        internal bool EverFiredForSave => _everFired;
        internal bool DeliveredThreeForSave => _deliveredThree;

        /// <summary>Відновлення зі зліпка: без нього заряди після завантаження — нулі.</summary>
        internal void RestoreForSave(int charge, int lastFiredDay, bool everFired,
            int deliveredLevel, int deliveredLevel3Day, bool deliveredThree)
        {
            Charge = charge < 0 ? 0 : charge;
            LastFiredDay = lastFiredDay;
            _everFired = everFired;
            DeliveredLevel = deliveredLevel;
            DeliveredLevel3Day = deliveredLevel3Day;
            _deliveredThree = deliveredThree;
        }

        public PressureTrack(IPressureSource source, PulseBalance cfg)
        {
            SourceId = source.Id;
            Kind = source.Kind;
            DomainTag = source.DomainTag;
            Threshold = source.Threshold > 0 ? source.Threshold : cfg.DefaultThreshold;
            CooldownDays = source.CooldownDays;
        }

        internal double Fill => Threshold <= 0 ? 0.0 : (double)Charge / Threshold;

        /// <summary>Ступінь передвісника за заповненням (0 — мовчимо).</summary>
        internal int ForewarnLevel(PulseBalance cfg)
        {
            double fill = Fill;
            if (fill >= cfg.Forewarn3At) return 3;
            if (fill >= cfg.Forewarn2At) return 2;
            if (fill >= cfg.Forewarn1At) return 1;
            return 0;
        }

        internal void Accumulate(int amount, PulseBalance cfg)
        {
            if (amount <= 0) return;
            Charge += amount;

            int cap = Threshold * cfg.ChargeCapMultiplier;
            if (Charge > cap) Charge = cap;
        }

        /// <summary>Гравець почув передвісник цієї ступені.</summary>
        internal void MarkDelivered(int level, int day)
        {
            if (level <= DeliveredLevel) return;
            DeliveredLevel = level;
            if (level >= 3 && !_deliveredThree)
            {
                _deliveredThree = true;
                DeliveredLevel3Day = day;
            }
        }

        /// <summary>
        /// Драбина кризи мовчить, поки криза, що спрацювала, стоїть на відкаті
        /// довше, ніж їй потрібно дійти до третьої ступені й вижидати вікно на
        /// реакцію (<see cref="PulseBalance.CrisisLadderLeadDays"/> +
        /// <see cref="PulseBalance.CrisisGraceDays"/>). Заряд при цьому копиться
        /// як і раніше — змінюється лише те, КОЛИ гравець чує попередження,
        /// а не коли криза б'є. Перша криза (відкату ще не було) та
        /// решта видів загроз не зачеплені.
        /// </summary>
        internal bool HoldsForewarnings(int day, PulseBalance cfg)
        {
            if (Kind != WorldEventKind.Crisis || !_everFired) return false;
            int cooldownLeft = CooldownDays - (day - LastFiredDay);
            return cooldownLeft > cfg.CrisisGraceDays + cfg.CrisisLadderLeadDays;
        }

        internal bool IsReady(int day, PulseBalance cfg)
        {
            if (Charge < Threshold) return false;

            // Жорсткий наслідок зобов'язаний бути оголошений І почутий, а після
            // цього гравцю дається вікно на реакцію. Ворота стоять САМЕ ТУТ, а не
            // після відбору: інакше накопичувач спалював би заряд намарно, а
            // інцидент мовчки пропускався. Другі ворота того самого сенсу —
            // «чи є джерелу чим спрацювати» — стоять у WorldPulse.Advance:
            // таблицю інцидентів знає крок дня, а не трек.
            if (Kind == WorldEventKind.Crisis)
            {
                if (!_deliveredThree) return false;
                if (day - DeliveredLevel3Day < cfg.CrisisGraceDays) return false;
            }

            // Перше спрацювання відкатом не стримується. Окремий прапорець, а не
            // «дата в мінус нескінченність»: різниця з int.MinValue переповнюється.
            return !_everFired || day - LastFiredDay >= CooldownDays;
        }

        internal void Fire(int day)
        {
            _everFired = true;

            // Повний розряд, а не перенесення залишку: за стелі у два пороги
            // насичений накопичувач інакше одразу знову стоїть на третій ступені,
            // і драбина 1 → 2 → 3 вироджується в безперервне «скоро».
            Charge = 0;
            LastFiredDay = day;

            DeliveredLevel = 0;
            DeliveredLevel3Day = 0;
            _deliveredThree = false;
        }
    }
}
