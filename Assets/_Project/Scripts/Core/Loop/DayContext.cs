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
    /// Состояние, которое шаги дня читают и дополняют. Живёт один день.
    /// </summary>
    public sealed class DayContext
    {
        public int Day { get; }
        public DayPhase Phase { get; }

        /// <summary>Тир поселения: 1 хутор → 4 городок. Двигатель фонового тика.</summary>
        public int Tier { get; }

        /// <summary>Уклад как индекс (0 Вольница → 3 Затвор). Полноценный тип — на Э3.</summary>
        public int OrderLevel { get; }

        public BalanceConfig Balance { get; }
        public TensionState Tension { get; }

        /// <summary>Ночью игрок либо спит, либо патрулирует (Поправка №3.9).</summary>
        public bool IsPatrolling { get; }

        /// <summary>Кто сегодня в городе и может отвечать за события.</summary>
        public IRosterView Roster { get; }

        public PopulationState Population { get; }
        public WorldPulse Pulse { get; }
        public IncidentTable Incidents { get; }
        public IRepeatTracker Repeats { get; }
        public ICasualtySink Casualties { get; }

        /// <summary>Позиции, дающие ежедневный доклад по своему домену.</summary>
        public IReadOnlyList<PostDomain> PostDomains { get; set; }

        /// <summary>
        /// Вчера поселению не хватило еды (Поправка №4). Порт, а не ссылка на
        /// модуль базы: конвейер дня не должен знать про BaseState.
        /// </summary>
        public bool IsHungry { get; set; }

        /// <summary>Предвестники этой фазы — их подхватит слой сигналов.</summary>
        internal List<Forewarning> Forewarnings { get; } = new List<Forewarning>();

        /// <summary>Источники, переполнившиеся в эту фазу.</summary>
        internal List<string> FiredSourceIds { get; } = new List<string>();

        /// <summary>Что произошло за фазу. Публично: это уже случилось.</summary>
        internal List<IncidentOutcome> IncidentOutcomes { get; } = new List<IncidentOutcome>();

        /// <summary>Заполняется шагом Signals; уходит наружу в отчёте.</summary>
        internal SignalDigest Signals { get; set; }

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
    /// Позиция, которая умеет докладывать: домен, навык и порог «внятности».
    /// Рабочие докладов не дают — только напарники (US-7.5, US-8.1).
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

    /// <summary>Один шаг дневного конвейера. Порядок берётся из DayStepOrder.</summary>
    public interface IDayStep
    {
        int Order { get; }
        void Execute(DayContext ctx);
    }
}
