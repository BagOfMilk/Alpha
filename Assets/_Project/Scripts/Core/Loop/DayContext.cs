using System;
using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Checks;
using Game.Core.Pressure;
using Game.Core.Settlement;
using Game.Core.Signals;
using Game.Core.World;

namespace Game.Core.Loop
{
    /// <summary>
    /// Стан, який кроки дня читають і доповнюють. Живе один день.
    /// </summary>
    public sealed class DayContext
    {
        public int Day { get; }
        public DayPhase Phase { get; }

        /// <summary>Тір поселення: 1 хутір → 4 містечко. Двигун фонового тику.</summary>
        public int Tier { get; }

        /// <summary>Уклад як індекс (0 Вольниця → 3 Затвор). Повноцінний тип — на Е3.</summary>
        public int OrderLevel { get; }

        public BalanceConfig Balance { get; }
        public TensionState Tension { get; }

        /// <summary>Вночі гравець або спить, або патрулює (Поправка №3.9).</summary>
        public bool IsPatrolling { get; }

        /// <summary>Хто сьогодні в місті і може відповідати за події.</summary>
        public IRosterView Roster { get; }

        public PopulationState Population { get; }

        /// <summary>Пам'ять громади про кров — читається порогами соціальних підходів.</summary>
        internal FearState Fear { get; set; }

        public WorldPulse Pulse { get; }
        public IncidentTable Incidents { get; }
        public IRepeatTracker Repeats { get; }
        public ICasualtySink Casualties { get; }

        /// <summary>Позиції, що дають щоденну доповідь за своїм доменом.</summary>
        public IReadOnlyList<PostDomain> PostDomains { get; set; }

        /// <summary>
        /// Учора поселенню не вистачило їжі (Поправка №4). Порт, а не посилання на
        /// модуль бази: конвеєр дня не повинен знати про BaseState.
        /// </summary>
        public bool IsHungry { get; set; }

        /// <summary>Передвісники цієї фази — їх підхопить шар сигналів.</summary>
        internal List<Forewarning> Forewarnings { get; } = new List<Forewarning>();

        /// <summary>Джерела, що переповнилися в цю фазу.</summary>
        internal List<string> FiredSourceIds { get; } = new List<string>();

        /// <summary>Що відбулося за фазу. Публічно: це вже сталося.</summary>
        internal List<IncidentOutcome> IncidentOutcomes { get; } = new List<IncidentOutcome>();

        /// <summary>
        /// Що місто зробило за добу: добудувало, прийняло людей, втратило їх, виросло.
        /// Назовні виходить лише через шар сигналів (Поправка №6).
        /// </summary>
        internal List<CityEvent> CityEvents { get; } = new List<CityEvent>();

        /// <summary>
        /// Тір, до якого місто доросло сьогодні (0 — не доросло). Сам тір доби
        /// незмінний до кінця фази; процесор застосує новий по її завершенні.
        /// </summary>
        internal int RaiseTierTo { get; set; }

        /// <summary>Заповнюється кроком Signals; виходить назовні у звіті.</summary>
        internal SignalDigest Signals { get; set; }

        /// <summary>Що і коли гравець уже чув — щоб сигнали не перетворювалися на шпалери.</summary>
        internal SignalMemory SignalMemory { get; set; }

        /// <summary>Чи запитувати гравця, як розбиратися з подією.</summary>
        internal bool RequirePlayerDecision { get; set; }

        /// <summary>Подія, що чекає рішення. Публічно: це пропозиція гравцю, а не метрика.</summary>
        public PendingDecision Pending { get; internal set; }

        /// <summary>Сама подія — щоб резолвер отримав її після вибору.</summary>
        internal IncidentDefinition PendingIncident { get; set; }

        /// <summary>
        /// Черга рішень цієї фази (аудит П10): якщо у фазі спрацювало декілька
        /// інцидентів, кожен стає СВОЇМ рішенням по черзі, а не тихо
        /// розбирається за гравця після першого. IncidentStep кладе сюди самі
        /// ПОДІЇ фази (не готові пропозиції); DayProcessor будує
        /// PendingDecision ЛІНИВО, у момент вилучення наступного елемента — інакше
        /// пропозиція для другого і далі інциденту будувалася б за станом
        /// Fear/Repeats ДО того, як розв'язався перший, і показаний поріг
        /// розійшовся б із застосованим (інваріант 8, регресія з ревью А1).
        /// </summary>
        internal Queue<IncidentDefinition> PendingQueue { get; } = new Queue<IncidentDefinition>();

        /// <summary>
        /// Зовнішня черга Напруги (R6): що накопичив DayProcessor.QueueExternal
        /// до цієї фази. TensionTickStep зливає її через наявний драйвер і
        /// очищає — список драйверів лишається закритим (інваріант 5).
        /// </summary>
        internal List<ExternalTensionEntry> ExternalTensionQueue { get; set; }

        public DayContext(int day, DayPhase phase, int tier, int orderLevel,
            BalanceConfig balance, TensionState tension,
            bool isPatrolling = false,
            IRosterView roster = null,
            PopulationState population = null,
            WorldPulse pulse = null,
            IncidentTable incidents = null,
            IRepeatTracker repeats = null,
            ICasualtySink casualties = null)
        {
            Day = day;
            Phase = phase;
            Tier = tier;
            OrderLevel = orderLevel;
            Balance = balance ?? throw new ArgumentNullException(nameof(balance));
            Tension = tension ?? throw new ArgumentNullException(nameof(tension));
            IsPatrolling = isPatrolling;
            Roster = roster;
            Population = population;
            Pulse = pulse;
            Incidents = incidents;
            Repeats = repeats;
            Casualties = casualties;
        }

        public bool IsNight => Phase == DayPhase.Night;
    }

    /// <summary>
    /// Позиція, яка вміє доповідати: домен, навичка і поріг «виразності».
    /// Робітники доповідей не дають — лише напарники (US-7.5, US-8.1).
    /// </summary>
    public sealed class PostDomain
    {
        public string PositionId;
        public string DomainTag;
        public SkillKey Skill;
        public int Threshold = 5;

        public PostDomain() { }

        public PostDomain(string positionId, string domainTag, SkillKey skill, int threshold = 5)
        {
            PositionId = positionId;
            DomainTag = domainTag;
            Skill = skill;
            Threshold = threshold;
        }
    }

    /// <summary>Один крок денного конвеєра. Порядок береться з DayStepOrder.</summary>
    public interface IDayStep
    {
        int Order { get; }
        void Execute(DayContext ctx);
    }

    /// <summary>Один запис зовнішньої черги Напруги (DayProcessor.QueueExternal).</summary>
    internal struct ExternalTensionEntry
    {
        internal readonly Pressure.TensionDriver Driver;
        internal readonly int Amount;

        internal ExternalTensionEntry(Pressure.TensionDriver driver, int amount)
        {
            Driver = driver;
            Amount = amount;
        }
    }
}
