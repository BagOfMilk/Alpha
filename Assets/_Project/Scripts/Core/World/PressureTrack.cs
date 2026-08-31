using Game.Core.Balance;

namespace Game.Core.World
{
    /// <summary>
    /// Накопитель давления. Заменяет бросок кубика — и делает это лучше:
    /// броску нечего сказать заранее, а накопителю есть. Заполнение на 55/80/95 %
    /// превращается в предвестники трёх ступеней.
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

        /// <summary>Последний выданный уровень предвестника — чтобы не повторяться.</summary>
        internal int AnnouncedLevel { get; set; }

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

        internal bool IsReady(int day)
        {
            if (Charge < Threshold) return false;
            // Первое срабатывание кулдауном не сдерживается. Отдельный флаг, а не
            // «дата в минус бесконечность»: разность с int.MinValue переполняется.
            return !_everFired || day - LastFiredDay >= CooldownDays;
        }

        /// <summary>
        /// Ступень предвестника, на которой накопитель сработал в последний раз.
        /// Нужна отдельным полем: Fire сбрасывает AnnouncedLevel, а разбор
        /// инцидента идёт следующим шагом дня и обязан знать, было ли объявление.
        /// </summary>
        internal int LevelAtLastFire { get; private set; }

        internal void Fire(int day)
        {
            _everFired = true;
            LevelAtLastFire = AnnouncedLevel;
            Charge -= Threshold;
            if (Charge < 0) Charge = 0;
            LastFiredDay = day;
            AnnouncedLevel = 0;
        }
    }
}
