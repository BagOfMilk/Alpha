using System;
using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Checks;
using Game.Core.Pressure;
using Game.Core.Settlement;
using Game.Core.Story;
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

        /// <summary>
        /// Память общины о крови. Живёт с кампанией и попадает в слепок: иначе
        /// загрузка была бы бесплатным способом снять цену кровавого пути.
        /// </summary>
        public FearState Fear { get; set; } = new FearState();
        public WorldPulse Pulse { get; set; }
        public IncidentTable Incidents { get; set; }
        public IRepeatTracker Repeats { get; set; }

        /// <summary>
        /// Городские работы (стройка, совет, пришлые) для слепка. Порт, а не
        /// ссылка: процессор не знает, что за ним модуль базы и экономика.
        /// </summary>
        public IStateBlob CityState { get; set; }
        public ICasualtySink Casualties { get; set; }
        public IReadOnlyList<PostDomain> PostDomains { get; set; }

        /// <summary>
        /// Партия в поле (Поправка №5.6 п. 4). Необязателен: без него город
        /// живёт как раньше, просто никто никуда не уходит.
        /// </summary>
        public Expeditions.ExpeditionParty Party { get; set; }

        /// <summary>
        /// Кошелёк, база и ролевой опыт (Foundation/A1, закрывает D10): без этого
        /// свойства слепок хранил календарь и Напряжение, а хозяйство игрока —
        /// нет, и загрузка возвращала игру без гроша в кармане.
        ///
        /// ПОРТ, а не тип базы: конвейер дня не должен знать про Game.Core.Base
        /// (охранитель Loop_DoesNotReferenceBase — направление зависимости
        /// «Base знает про Loop, Loop про Base — никогда»). В игре сюда попадает
        /// сам BaseState — он реализует IStateBlob точно так же, как CityWorks
        /// уже делает это для CityState ниже.
        /// </summary>
        public IStateBlob Economy { get; set; }

        /// <summary>Истощение точек вылазки (R15) — теперь тоже часть слепка.</summary>
        public Expeditions.SiteLedger Sites { get; set; }

        /// <summary>Сюжетные флаги (R3): чистый контейнер, событий сам не эмитит.</summary>
        public StoryFlags Flags { get; set; }

        private readonly List<ExternalTensionEntry> _externalTension = new List<ExternalTensionEntry>();

        /// <summary>
        /// Единственный узаконенный мостик R6: внешние системы (квесты, указы
        /// совета — придут в следующих пакетах) не трогают Напряжение прямо, а
        /// кладут заявку сюда. Она применяется через СУЩЕСТВУЮЩИЙ драйвер на
        /// тике следующей фазы (TensionTickStep) — список драйверов остаётся
        /// закрытым (инвариант 5), новый узел это не добавляет.
        /// </summary>
        public void QueueExternal(TensionDriver driver, int amount)
        {
            if (amount == 0) return;
            _externalTension.Add(new ExternalTensionEntry(driver, amount));
        }

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

        /// <summary>
        /// Стандартный набор шагов дня — без производства.
        ///
        /// Материальный итог суток (произвели, поели, подлечились) подключается
        /// ровно одним путём: <c>SettlementCycle.BuildSteps</c> в модуле базы.
        /// Второй мост — порт в этом слое — снесён 23.09.2026: он делал цикл
        /// базы публичным и позволял прокрутить сутки в обход конвейера. Без
        /// производства конвейер работает как на Э0 — это нужно узким тестам,
        /// которым база не нужна.
        /// </summary>
        public static IEnumerable<IDayStep> DefaultSteps()
        {
            var steps = new List<IDayStep>
            {
                new HungerStep(),
                new TensionTickStep(),
                new PulseStep(),
                new IncidentStep(),
                new SignalStep()
            };

            return steps;
        }

        /// <summary>
        /// Слепок городского слоя одной строкой. Отдаётся наружу непрозрачным:
        /// сборка Game.Gameplay кладёт его в систему сохранений, но прочитать
        /// оттуда скрытые числа случайно не может (см. SettlementSave).
        ///
        /// Слепок НЕ несёт очередь решений фазы (<see cref="AwaitsDecision"/>):
        /// сохранение посреди открытого решения молча потеряло бы все ещё не
        /// разобранные инциденты этой фазы при восстановлении (найдено ревью А1).
        /// Сегодняшние вызывающие (Alpha.Play/Alpha.Sim) всегда дренируют очередь
        /// до конца перед сохранением; когда появится сохранение из любого места
        /// экрана (GameSession), эта проверка обязана либо уйти, либо очередь
        /// обязана попасть в блоб — что раньше.
        /// </summary>
        public string SaveState()
        {
            if (AwaitsDecision)
                throw new InvalidOperationException(
                    "Нельзя сохранять посреди решения фазы: сначала ResolvePending " +
                    "до конца очереди (AwaitsDecision должен стать false).");
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
                RequirePlayerDecision = RequirePlayerDecision,
                Fear = Fear,
                ExternalTensionQueue = DrainExternalTension()
            };

            // Первая половина конвейера — до хода игрока.
            RunSteps(ctx, 0, DayStepOrder.PlayerResolution);

            if (TryDequeueNextPending(ctx))
            {
                // Сутки остановлены. Сигналы намеренно НЕ собираются: они
                // описывают финальное состояние дня, а день ещё не случился.
                _awaiting = ctx;
                return BuildReport(ctx);
            }

            return Finish(ctx);
        }

        /// <summary>
        /// Ход игрока: выбранный путь разбирается. Если очередь решений фазы
        /// (аудит П10) ещё не пуста — следующий инцидент фазы становится новым
        /// Pending, и AwaitsDecision остаётся true; сутки доигрываются до конца
        /// только когда очередь опустела.
        /// </summary>
        public DayReport ResolvePending(IncidentPath path)
        {
            if (_awaiting == null)
                throw new InvalidOperationException("Нечего решать: конвейер не остановлен.");

            var ctx = _awaiting;

            var outcome = World.IncidentResolver.Resolve(
                ctx.PendingIncident, ctx.Roster, ctx.Repeats, ctx.Casualties,
                ctx.Population, ctx.Tension, ctx.Day, ctx.Balance, path, ctx.Fear);

            ctx.IncidentOutcomes.Add(outcome);
            ctx.Pending = null;
            ctx.PendingIncident = null;

            if (TryDequeueNextPending(ctx))
                return BuildReport(ctx);

            _awaiting = null;
            return Finish(ctx);
        }

        /// <summary>
        /// D1/R8: розв'язок поточного очікуваного рішення заданою НАПРЯМУ
        /// полосою, а не перевіркою навички. Єдиний легальний споживач —
        /// GameSession, коли пороговий інцидент денного конвеєра (вузол 1 "бій
        /// на перевалі", фінал доби 5) кровавим шляхом веде на справжній
        /// тактичний бій замість перевірки: полоса приходить із
        /// BattleResult→OutcomeBand, а не з CheckResolver, і викликати звичний
        /// ResolvePending для неї не можна — той сам порахує перевірку ще раз і
        /// застосує Напругу вдруге. Напруга рухається ТИМ САМИМ правилом, що і
        /// звичний шлях (<see cref="IncidentDefinition.TensionByBand"/>, індекс —
        /// полоса, знак дельти вибирає драйвер із закритого списку — інваріант
        /// 5): дві точки резолву лишаються однією конвенцією. Рана виконавцю й
        /// страх громади для цього шляху вже рахує сам викликач (бій ранить
        /// лише через RosterAdapter.Wound, Р5) — тут це НЕ повторюється.
        /// </summary>
        public DayReport ResolvePendingWithBand(OutcomeBand band)
        {
            if (_awaiting == null)
                throw new InvalidOperationException(
                    "Нечего решать: конвейер не остановлен.");

            var ctx = _awaiting;
            var incident = ctx.PendingIncident;

            if (incident != null)
            {
                var deltas = incident.TensionByBand;
                if (deltas != null && deltas.Length > 0)
                {
                    int index = (int)band;
                    if (index >= deltas.Length) index = deltas.Length - 1;
                    int delta = deltas[index];
                    if (delta != 0)
                    {
                        var driver = delta > 0 ? TensionDriver.ThreatOutcome : TensionDriver.EventOutcome;
                        _tension.Apply(driver, delta, "incident:" + incident.Id);
                    }
                }

                ctx.IncidentOutcomes.Add(new IncidentOutcome(incident.Id, incident.TopicId, incident.DomainTag,
                    band, wasUnmanned: false, wasCrisis: false, affectedActorId: null, bite: null,
                    populationLost: 0, causedFear: false, peopleArrived: 0));
            }

            ctx.Pending = null;
            ctx.PendingIncident = null;

            if (TryDequeueNextPending(ctx))
                return BuildReport(ctx);

            _awaiting = null;
            return Finish(ctx);
        }

        /// <summary>
        /// Следующее решение очереди фазы — в Pending. false, если очередь пуста.
        ///
        /// Предложение строится ЗДЕСЬ, а не заранее в IncidentStep: только так
        /// оно читает Fear/Repeats в том состоянии, в каком они окажутся к
        /// моменту показа — включая эффект уже разрешённых решений этой же
        /// фазы (регрессия из ревью А1, см. комментарий в IncidentStep.Execute).
        /// </summary>
        private static bool TryDequeueNextPending(DayContext ctx)
        {
            if (ctx.PendingQueue.Count == 0) return false;
            var incident = ctx.PendingQueue.Dequeue();
            ctx.PendingIncident = incident;
            ctx.Pending = IncidentStep.BuildOffer(ctx, incident);
            return true;
        }

        private List<ExternalTensionEntry> DrainExternalTension()
        {
            if (_externalTension.Count == 0) return null;
            var copy = new List<ExternalTensionEntry>(_externalTension);
            _externalTension.Clear();
            return copy;
        }

        private DayReport Finish(DayContext ctx)
        {
            RunSteps(ctx, DayStepOrder.PlayerResolution, int.MaxValue);

            // Город дорос до нового тира (Поправка №6.4). Меняется после фазы:
            // внутри суток тир — константа, иначе тик Напряжения и таблица
            // событий считались бы по разным тирам в одной фазе.
            if (ctx.RaiseTierTo > Tier) Tier = ctx.RaiseTierTo;

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
