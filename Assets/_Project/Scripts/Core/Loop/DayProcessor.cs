using System;
using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Checks;
using Game.Core.Pressure;
using Game.Core.Settlement;
using Game.Core.World;

namespace Game.Core.Loop
{
    /// <summary>
    /// Продвигает время. Заменяет бесконечный хвост из пост-шагов упорядоченным
    /// конвейером: порядок задаётся <see cref="DayStepOrder"/> и сортируется один
    /// раз при создании, поэтому шаги не могут молча поменяться местами.
    ///
    /// Полностью детерминирован: одинаковые входы дают одинаковый ход кампании
    /// (Поправка №3.3).
    /// </summary>
    public sealed class DayProcessor
    {
        private readonly List<IDayStep> _steps;
        private readonly BalanceConfig _balance;
        private readonly TensionState _tension;

        public int CurrentDay { get; private set; }

        /// <summary>Тир поселения: хутор 1 → городок 4.</summary>
        public int Tier { get; set; } = 1;

        /// <summary>Уклад как индекс; полноценный тип появится на Э3.</summary>
        public int OrderLevel { get; set; } = 1;

        public TensionState Tension => _tension;

        // ---- Порты городского слоя. Необязательны: без них конвейер
        //      работает как на Э0, что удобно для узких тестов. ----
        public IRosterView Roster { get; set; }
        public PopulationState Population { get; set; }
        public WorldPulse Pulse { get; set; }
        public IncidentTable Incidents { get; set; }
        public IRepeatTracker Repeats { get; set; }
        public ICasualtySink Casualties { get; set; }
        public IReadOnlyList<PostDomain> PostDomains { get; set; }

        /// <summary>Ночью: патрулировать вместо сна (Поправка №3.9).</summary>
        public bool IsPatrolling { get; set; }

        public DayProcessor(TensionState tension, BalanceConfig balance, IEnumerable<IDayStep> steps)
        {
            _tension = tension ?? throw new ArgumentNullException(nameof(tension));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));

            _steps = new List<IDayStep>();
            if (steps != null)
                foreach (var s in steps)
                    if (s != null) _steps.Add(s);

            // Стабильная сортировка: при равном Order порядок добавления сохраняется.
            _steps.Sort((a, b) => a.Order.CompareTo(b.Order));
        }

        /// <summary>Шаги в порядке исполнения — для тестов и отладки.</summary>
        public IReadOnlyList<IDayStep> Steps => _steps;

        /// <summary>Стандартный набор шагов этапа Э0.</summary>
        public static IEnumerable<IDayStep> DefaultSteps()
        {
            return new IDayStep[]
            {
                new TensionTickStep(),
                new PulseStep(),
                new IncidentStep(),
                new SignalStep()
            };
        }

        public DayReport Advance(DayPhase phase = DayPhase.Day)
        {
            CurrentDay++;
            _tension.BeginDay();

            var ctx = new DayContext(CurrentDay, phase, Tier, OrderLevel, _balance, _tension,
                IsPatrolling, Roster, Population, Pulse, Incidents, Repeats, Casualties)
            {
                PostDomains = PostDomains
            };

            for (int i = 0; i < _steps.Count; i++)
                _steps[i].Execute(ctx);

            _tension.OnDayAdvanced();

            // Копии: отчёт не должен меняться, когда начнётся следующий день.
            var ledger = new List<TensionChange>(_tension.DayLedger);
            var incidents = new List<IncidentOutcome>(ctx.IncidentOutcomes);
            var forewarnings = new List<Forewarning>(ctx.Forewarnings);
            return new DayReport(ctx.Day, ctx.Phase, ledger, ctx.Signals, incidents, forewarnings);
        }

        /// <summary>Прокрутить N дней подряд (кнопка «ждать», US-1.3).</summary>
        public List<DayReport> Advance(int days, DayPhase phase = DayPhase.Day)
        {
            var reports = new List<DayReport>();
            for (int i = 0; i < days; i++)
                reports.Add(Advance(phase));
            return reports;
        }

        /// <summary>
        /// Полные сутки: день, затем ночь. Ночь — окно угроз, поэтому её нельзя
        /// пропустить: можно только спать (и потерять сигналы) или патрулировать.
        /// </summary>
        public List<DayReport> AdvanceFullDay()
        {
            return new List<DayReport> { Advance(DayPhase.Day), Advance(DayPhase.Night) };
        }
    }
}
