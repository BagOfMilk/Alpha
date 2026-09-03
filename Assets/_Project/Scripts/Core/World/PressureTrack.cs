using Game.Core.Balance;

namespace Game.Core.World
{
    /// <summary>
    /// Накопитель давления. Заменяет бросок кубика — и делает это лучше:
    /// броску нечего сказать заранее, а накопителю есть. Заполнение на 55/80/95 %
    /// превращается в предвестники трёх ступеней.
    ///
    /// Ступень считается пройденной только когда игрок её УСЛЫШАЛ. Уровень,
    /// выданный ночью спящему, остаётся неподтверждённым и будет предложен снова:
    /// иначе предупреждение сгорало бы молча, а кризис приходил к тому, кто
    /// ничего не слышал (US-11.2, риск R8).
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

        /// <summary>Наибольшая ступень, которая ДОШЛА до игрока (не путать с достигнутой).</summary>
        internal int DeliveredLevel { get; private set; }

        /// <summary>Календарные сутки, когда игрок услышал третью ступень.</summary>
        internal int DeliveredLevel3Day { get; private set; }
        private bool _deliveredThree;

        public PressureTrack(IPressureSource source, PulseBalance cfg)
        {
            SourceId = source.Id;
            Kind = source.Kind;
            DomainTag = source.DomainTag;
            Threshold = source.Threshold > 0 ? source.Threshold : cfg.DefaultThreshold;
            CooldownDays = source.CooldownDays;
        }

        internal double Fill => Threshold <= 0 ? 0.0 : (double)Charge / Threshold;

        /// <summary>Ступень предвестника по заполнению (0 — молчим).</summary>
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

        /// <summary>Игрок услышал предвестник этой ступени.</summary>
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

        internal bool IsReady(int day, PulseBalance cfg)
        {
            if (Charge < Threshold) return false;

            // Жёсткое последствие обязано быть объявлено И услышано, а после
            // этого игроку даётся окно на реакцию. Ворота стоят ЗДЕСЬ, а не
            // после отбора: иначе накопитель сжигал бы заряд впустую, а
            // инцидент молча пропускался.
            if (Kind == WorldEventKind.Crisis)
            {
                if (!_deliveredThree) return false;
                if (day - DeliveredLevel3Day < cfg.CrisisGraceDays) return false;
            }

            // Первое срабатывание кулдауном не сдерживается. Отдельный флаг, а не
            // «дата в минус бесконечность»: разность с int.MinValue переполняется.
            return !_everFired || day - LastFiredDay >= CooldownDays;
        }

        internal void Fire(int day)
        {
            _everFired = true;

            // Полный разряд, а не перенос остатка: при потолке в два порога
            // насыщенный накопитель иначе сразу снова стоит на третьей ступени,
            // и лестница 1 → 2 → 3 вырождается в непрерывное «скоро».
            Charge = 0;
            LastFiredDay = day;

            DeliveredLevel = 0;
            DeliveredLevel3Day = 0;
            _deliveredThree = false;
        }
    }
}
