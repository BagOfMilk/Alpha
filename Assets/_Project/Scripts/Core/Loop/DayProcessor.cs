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
    /// Просуває час. Заміняє нескінченний хвіст із пост-кроків впорядкованим
    /// конвеєром: порядок задається <see cref="DayStepOrder"/> і сортується один
    /// раз при створенні, тому кроки не можуть тихо помінятися місцями.
    ///
    /// Повністю детермінований: однакові входи дають однаковий хід кампанії
    /// (Поправка №3.3).
    /// </summary>
    public sealed class DayProcessor
    {
        private readonly List<IDayStep> _steps;
        private readonly BalanceConfig _balance;
        private readonly TensionState _tension;

        public int CurrentDay { get; private set; }

        /// <summary>Тір поселення: хутір 1 → містечко 4.</summary>
        public int Tier { get; set; } = 1;

        /// <summary>Уклад як індекс; повноцінний тип з'явиться на Е3.</summary>
        public int OrderLevel { get; set; } = 1;

        public TensionState Tension => _tension;

        /// <summary>Пам'ять шару сигналів. Живе разом з кампанією і потрапляє в зліпок.</summary>
        internal Signals.SignalMemory SignalMemory { get; } = new Signals.SignalMemory();

        // ---- Порти міського шару. Необов'язкові: без них конвеєр
        //      працює як на Е0, що зручно для вузьких тестів. ----
        public IRosterView Roster { get; set; }
        public PopulationState Population { get; set; }

        /// <summary>
        /// Пам'ять громади про кров. Живе з кампанією і потрапляє в зліпок: інакше
        /// завантаження було б безкоштовним способом зняти ціну кривавого шляху.
        /// </summary>
        public FearState Fear { get; set; } = new FearState();
        public WorldPulse Pulse { get; set; }
        public IncidentTable Incidents { get; set; }
        public IRepeatTracker Repeats { get; set; }

        /// <summary>
        /// Міські роботи (будівництво, рада, прибульці) для зліпка. Порт, а не
        /// посилання: процесор не знає, що за ним модуль бази й економіка.
        /// </summary>
        public IStateBlob CityState { get; set; }
        public ICasualtySink Casualties { get; set; }
        public IReadOnlyList<PostDomain> PostDomains { get; set; }

        /// <summary>
        /// Партія в полі (Поправка №5.6 п. 4). Необов'язковий: без нього місто
        /// живе як раніше, просто ніхто нікуди не йде.
        /// </summary>
        public Expeditions.ExpeditionParty Party { get; set; }

        /// <summary>
        /// Гаманець, база і рольовий досвід (Foundation/A1, закриває D10): без цієї
        /// властивості зліпок зберігав календар і Напругу, а господарство гравця —
        /// ні, і завантаження повертало гру без копійки в кишені.
        ///
        /// ПОРТ, а не тип бази: конвеєр дня не повинен знати про Game.Core.Base
        /// (охоронець Loop_DoesNotReferenceBase — напрямок залежності
        /// «Base знає про Loop, Loop про Base — ніколи»). У грі сюди потрапляє
        /// сам BaseState — він реалізує IStateBlob точнісінько так само, як CityWorks
        /// уже робить це для CityState нижче.
        /// </summary>
        public IStateBlob Economy { get; set; }

        /// <summary>Виснаження точок вилазки (R15) — тепер теж частина зліпка.</summary>
        public Expeditions.SiteLedger Sites { get; set; }

        /// <summary>Сюжетні прапорці (R3): чистий контейнер, подій сам не емітить.</summary>
        public StoryFlags Flags { get; set; }

        private readonly List<ExternalTensionEntry> _externalTension = new List<ExternalTensionEntry>();

        /// <summary>
        /// Єдиний узаконений місток R6: зовнішні системи (квести, укази
        /// ради — прийдуть у наступних пакетах) не чіпають Напругу напряму, а
        /// кладуть заявку сюди. Вона застосовується через НАЯВНИЙ драйвер на
        /// тику наступної фази (TensionTickStep) — список драйверів лишається
        /// закритим (інваріант 5), новий вузол цього не додає.
        /// </summary>
        public void QueueExternal(TensionDriver driver, int amount)
        {
            if (amount == 0) return;
            _externalTension.Add(new ExternalTensionEntry(driver, amount));
        }

        /// <summary>
        /// Квест підняв Напругу, а доба вже закрита (між Advance() —
        /// день дописано, наступний ще не почато). Це ЄДИНИЙ безпечний
        /// вхід у цьому вікні (G22): заявка йде містком R6 і лягає на тик
        /// НАСТУПНОЇ фази (TensionTickStep, ДО SignalStep), тому зміну
        /// полоси почують у тому самому звіті, де вона сталася (інваріант 4).
        ///
        /// Прямий <see cref="TensionDrivers.QuestChoice"/> на <see cref="Tension"/>
        /// у цьому ж вікні небезпечний: наступний Advance() починається з
        /// <c>TensionState.BeginDay()</c>, який стирає денний журнал ДО
        /// того, як SignalStep встигає його прочитати, — полоса змінюється
        /// насправді, а мандатний сигнал G21 не будується взагалі, бо
        /// будувати його нема з чого. Раніше так робив CampaignSimulator
        /// (AggressiveChoices) — 200-добова кампанія втрачала рівно два переходи
        /// полоси (доби 20 і 40), і <c>CampaignPacingTests.Pacing_
        /// BandChangeIsNeverMute</c> це ловив.
        /// </summary>
        public void QueueQuestChoice(TensionDrivers.ChoiceWeight weight)
        {
            QueueExternal(TensionDriver.QuestChoice, TensionDrivers.Weight(weight, _balance.Tension));
        }

        /// <summary>Той самий місток для наслідку, що розряджає обстановку — див. <see cref="QueueQuestChoice"/>.</summary>
        public void QueueEventOutcome(TensionDrivers.ChoiceWeight weight)
        {
            QueueExternal(TensionDriver.EventOutcome, -TensionDrivers.Weight(weight, _balance.Tension));
        }

        /// <summary>
        /// Копія ще не осушеної черги QueueExternal — лише для зліпка
        /// (D1a, шов, задокументований прямо в SaveState() нижче). Заявка,
        /// покладена сюди МІЖ фазами (наприклад, наслідком квесту, вжитим
        /// у Morning до AdvanceDay), раніше губилася при save/load: SaveState
        /// не зберігав <see cref="_externalTension"/> взагалі.
        /// </summary>
        internal IReadOnlyList<ExternalTensionEntry> PeekExternalTensionForSave() => _externalTension;

        /// <summary>Відновити чергу QueueExternal зі зліпка (див. PeekExternalTensionForSave).</summary>
        internal void RestoreExternalTensionForSave(List<ExternalTensionEntry> entries)
        {
            _externalTension.Clear();
            if (entries != null) _externalTension.AddRange(entries);
        }

        /// <summary>Вночі: патрулювати замість сну (Поправка №3.9).</summary>
        public bool IsPatrolling { get; set; }

        /// <summary>Поселенню не вистачило їжі в минулому циклі (Поправка №4).</summary>
        public bool IsHungry { get; set; }

        /// <summary>
        /// Чи запитувати гравця, як розбиратися з подією.
        ///
        /// Вимкнено за замовчуванням НАВМИСНЕ: увімкнення змінює контракт виклику
        /// (Advance може повернути неповну добу), а інтерфейсу, який показав
        /// би пропозицію, ще немає. Архітектура закладена зараз — поки на
        /// Advance() не зав'язалися наступні етапи, — а момент перемикання
        /// лишається рішенням власника.
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

            // Стабільне сортування: при рівному Order порядок додавання зберігається.
            _steps.Sort((a, b) => a.Order.CompareTo(b.Order));
        }

        /// <summary>Кроки в порядку виконання — для тестів і налагодження.</summary>
        public IReadOnlyList<IDayStep> Steps => _steps;

        /// <summary>
        /// Стандартний набір кроків дня — без виробництва.
        ///
        /// Матеріальний підсумок доби (виробили, поїли, підлікувалися) підключається
        /// рівно одним шляхом: <c>SettlementCycle.BuildSteps</c> у модулі бази.
        /// Другий міст — порт у цьому шарі — знесений 23.09.2026: він робив цикл
        /// бази публічним і дозволяв прокрутити добу в обхід конвеєра. Без
        /// виробництва конвеєр працює як на Е0 — це потрібно вузьким тестам,
        /// яким база не потрібна.
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
        /// Зліпок міського шару одним рядком. Віддається назовні непрозорим:
        /// збірка Game.Gameplay кладе його в систему збережень, але прочитати
        /// звідти приховані числа випадково не може (див. SettlementSave).
        ///
        /// Зліпок НЕ несе чергу рішень фази (<see cref="AwaitsDecision"/>):
        /// збереження посеред відкритого рішення мовчки загубило б усі ще не
        /// розібрані інциденти цієї фази при відновленні (знайдено ревью А1).
        /// Черга QueueExternal (D1a) у блоб ПОТРАПЛЯЄ — див. "extq=" у
        /// SettlementSave.Capture/Restore: раніше ця заявка губилася мовчки
        /// при save/load посеред Morning (до AdvanceDay її ще не дренує
        /// ніхто), тепер блоб несе копію, а не дренує її сам (дренує
        /// її лише Advance()).
        /// </summary>
        public string SaveState()
        {
            if (AwaitsDecision)
                throw new InvalidOperationException(
                    "Не можна зберігати посеред рішення фази: спершу розв'яжи всі рішення, що чекають.");
            return SettlementSave.Capture(this);
        }

        /// <summary>Відновити стан зі зліпка, зробленого SaveState.</summary>
        public void RestoreState(string blob)
        {
            SettlementSave.Restore(this, blob);
        }

        internal void RestoreDay(int day)
        {
            CurrentDay = day < 0 ? 0 : day;
        }

        /// <summary>Чи чекає конвеєр ходу гравця прямо зараз.</summary>
        public bool AwaitsDecision
        {
            get { return _awaiting != null; }
        }

        public DayReport Advance(DayPhase phase = DayPhase.Day)
        {
            if (_awaiting != null)
                throw new InvalidOperationException(
                    "Доба не завершена: спершу розв'яжи рішення, що чекає.");

            // Календарна доба починається з денної фази; ніч належить тій
            // самій добі. Інакше лічильник рахує ФАЗИ, і кожне вікно «в днях»
            // (кулдауни, вікно повторів, grace кризи) удвічі коротше заявленого.
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

            // Перша половина конвеєра — до ходу гравця.
            RunSteps(ctx, 0, DayStepOrder.PlayerResolution);

            if (TryDequeueNextPending(ctx))
            {
                // Доба зупинена. Сигнали навмисно НЕ збираються: вони
                // описують фінальний стан дня, а день ще не стався.
                _awaiting = ctx;
                return BuildReport(ctx);
            }

            return Finish(ctx);
        }

        /// <summary>
        /// Хід гравця: обраний шлях розбирається. Якщо черга рішень фази
        /// (аудит П10) ще не порожня — наступний інцидент фази стає новим
        /// Pending, і AwaitsDecision лишається true; доба дограється до кінця
        /// лише коли черга спорожніла.
        /// </summary>
        public DayReport ResolvePending(IncidentPath path)
        {
            if (_awaiting == null)
                throw new InvalidOperationException("Немає рішення, що чекало б на розв'язок.");

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
                    "Немає рішення, що чекало б на розв'язок.");

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
        /// Наступне рішення черги фази — в Pending. false, якщо черга порожня.
        ///
        /// Пропозиція будується САМЕ ТУТ, а не заздалегідь в IncidentStep: тільки так
        /// вона читає Fear/Repeats у тому стані, у якому вони опиняться до
        /// моменту показу — включно з ефектом уже розв'язаних рішень цієї ж
        /// фази (регресія з ревью А1, див. коментар в IncidentStep.Execute).
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

            // Місто доросло до нового тіру (Поправка №6.4). Змінюється після фази:
            // усередині доби тір — константа, інакше тик Напруги і таблиця
            // подій рахувалися б за різними тірами в одній фазі.
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
            // Копії: звіт не повинен змінюватися, коли почнеться наступний день.
            var ledger = new List<TensionChange>(_tension.DayLedger);
            var incidents = new List<IncidentOutcome>(ctx.IncidentOutcomes);
            var forewarnings = new List<Forewarning>(ctx.Forewarnings);
            return new DayReport(ctx.Day, ctx.Phase, ledger, ctx.Signals, incidents,
                forewarnings, ctx.Pending);
        }

        /// <summary>Прокрутити N днів поспіль (кнопка «чекати», US-1.3).</summary>
        public List<DayReport> Advance(int days, DayPhase phase = DayPhase.Day)
        {
            var reports = new List<DayReport>();
            for (int i = 0; i < days; i++)
                reports.Add(Advance(phase));
            return reports;
        }

        /// <summary>
        /// Повна доба: день, потім ніч. Ніч — вікно загроз, тому її не можна
        /// пропустити: можна лише спати (і втратити сигнали) або патрулювати.
        /// </summary>
        public List<DayReport> AdvanceFullDay()
        {
            return new List<DayReport> { Advance(DayPhase.Day), Advance(DayPhase.Night) };
        }
    }
}
