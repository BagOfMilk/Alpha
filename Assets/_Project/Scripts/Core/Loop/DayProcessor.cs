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

        /// <summary>Память слоя сигналов. Живёт вместе с кампанией и попадает в слепок.</summary>
        internal Signals.SignalMemory SignalMemory { get; } = new Signals.SignalMemory();

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

        /// <summary>Поселению не хватило еды в прошлом цикле (Поправка №4).</summary>
        public bool IsHungry { get; set; }

        /// <summary>
        /// Спрашивать ли игрока, как разбираться с событием.
        ///
        /// Выключено по умолчанию НАМЕРЕННО: включение меняет контракт вызова
        /// (Advance может вернуть неполные сутки), а интерфейса, который показал
        /// бы предложение, ещё нет. Архитектура заложена сейчас — пока на
        /// Advance() не завязались следующие этапы, — а момент переключения
        /// остаётся решением владельца.
        /// </summary>
        public bool RequirePlayerDecision { get; set; }

        private DayContext _awaiting;

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
                new HungerStep(),
                new TensionTickStep(),
                new PulseStep(),
                new IncidentStep(),
                new SignalStep()
            };
        }

        /// <summary>
        /// Слепок городского слоя одной строкой. Отдаётся наружу непрозрачным:
        /// сборка Game.Gameplay кладёт его в систему сохранений, но прочитать
        /// оттуда скрытые числа случайно не может (см. SettlementSave).
        /// </summary>
        public string SaveState()
        {
            return SettlementSave.Capture(this);
        }

        /// <summary>Восстановить состояние из слепка, сделанного SaveState.</summary>
        public void RestoreState(string blob)
        {
            SettlementSave.Restore(this, blob);
        }

        internal void RestoreDay(int day)
        {
            CurrentDay = day < 0 ? 0 : day;
        }

        /// <summary>Ждёт ли конвейер хода игрока прямо сейчас.</summary>
        public bool AwaitsDecision
        {
            get { return _awaiting != null; }
        }

        public DayReport Advance(DayPhase phase = DayPhase.Day)
        {
            if (_awaiting != null)
                throw new InvalidOperationException(
                    "Сутки не закончены: конвейер ждёт решения. Сначала ResolvePending.");

            // Календарные сутки начинаются с дневной фазы; ночь принадлежит тем
            // же суткам. Иначе счётчик считает ФАЗЫ, и каждое окно «в днях»
            // (кулдауны, окно повторов, grace кризиса) вдвое короче заявленного.
            if (phase == DayPhase.Day) CurrentDay++;
            _tension.BeginDay();

            var ctx = new DayContext(CurrentDay, phase, Tier, OrderLevel, _balance, _tension,
                IsPatrolling, Roster, Population, Pulse, Incidents, Repeats, Casualties)
            {
                PostDomains = PostDomains,
                IsHungry = IsHungry,
                SignalMemory = SignalMemory,
                RequirePlayerDecision = RequirePlayerDecision
            };

            // Первая половина конвейера — до хода игрока.
            RunSteps(ctx, 0, DayStepOrder.PlayerResolution);

            if (ctx.Pending != null)
            {
                // Сутки остановлены. Сигналы намеренно НЕ собираются: они
                // описывают финальное состояние дня, а день ещё не случился.
                _awaiting = ctx;
                return BuildReport(ctx);
            }

            return Finish(ctx);
        }

        /// <summary>
        /// Ход игрока: выбранный путь разбирается, и сутки доигрываются до конца.
        /// </summary>
        public DayReport ResolvePending(IncidentPath path)
        {
            if (_awaiting == null)
                throw new InvalidOperationException("Нечего решать: конвейер не остановлен.");

            var ctx = _awaiting;
            _awaiting = null;

            var outcome = World.IncidentResolver.Resolve(
                ctx.PendingIncident, ctx.Roster, ctx.Repeats, ctx.Casualties,
                ctx.Population, ctx.Tension, ctx.Day, ctx.Balance, path);

            ctx.IncidentOutcomes.Add(outcome);
            ctx.Pending = null;
            ctx.PendingIncident = null;

            return Finish(ctx);
        }

        private DayReport Finish(DayContext ctx)
        {
            RunSteps(ctx, DayStepOrder.PlayerResolution, int.MaxValue);

            if (ctx.Phase == DayPhase.Day) _tension.OnDayAdvanced();
            return BuildReport(ctx);
        }

        private void RunSteps(DayContext ctx, int fromOrderInclusive, int toOrderExclusive)
        {
            for (int i = 0; i < _steps.Count; i++)
            {
                int order = _steps[i].Order;
                if (order < fromOrderInclusive || order >= toOrderExclusive) continue;
                _steps[i].Execute(ctx);
            }
        }

        private DayReport BuildReport(DayContext ctx)
        {
            // Копии: отчёт не должен меняться, когда начнётся следующий день.
            var ledger = new List<TensionChange>(_tension.DayLedger);
            var incidents = new List<IncidentOutcome>(ctx.IncidentOutcomes);
            var forewarnings = new List<Forewarning>(ctx.Forewarnings);
            return new DayReport(ctx.Day, ctx.Phase, ledger, ctx.Signals, incidents,
                forewarnings, ctx.Pending);
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
