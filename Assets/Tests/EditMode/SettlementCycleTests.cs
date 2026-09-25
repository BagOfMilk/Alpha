using System.Linq;
using Game.Core;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Economy;
using Game.Core.Loop;
using Game.Core.Pressure;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Міст між модулем бази і конвеєром дня. До нього в проєкті було два
    /// денних цикли, що не знали одне про одного: виробництво йшло повз конвеєр,
    /// а голод у конвеєрі не тиснув, бо прапорець у нього ніхто не виставляв.
    ///
    /// Тести тут про одне: що тепер спосіб просунути час рівно один і
    /// що добу не можна прожити двічі.
    /// </summary>
    public class SettlementCycleTests
    {
        private static BalanceConfig Cfg() => new BalanceConfig();

        /// <summary>
        /// Баланс, за якого поселення не прогодувати. Ферма дає 14 їжі на
        /// день, тому «нуль у коморах» голоду не створює — голод створює
        /// прокорм, який вищий за виробіток. Саме цю помилку і спіймав перший
        /// прогін цих тестів.
        /// </summary>
        private static BalanceConfig StarvingCfg() => new BalanceConfig { FoodUpkeepPerCompanion = 50 };

        private static SettlementCycle Build(BalanceConfig cfg, int startingFood = 100)
        {
            var roster = new Roster();
            var state = new BaseState(roster, new ResourceLedger(), cfg);

            var arch = new CompanionArchetype("farmer", "Пахарь");
            arch.SetAttribute(AttributeType.Will, 4);
            arch.SetSkill(SkillType.Survival, 6);
            roster.Add(arch.CreateInstance("farmer_1", cfg));

            state.AddSlot(DefaultContent.AllSlots().First(s => s.Id == "settlement_farms"));
            state.TryAssign("farmer_1", "settlement_farms");
            state.Resources.Add(ResourceType.Food, startingFood);

            var production = new ProductionStep(state);
            var processor = new DayProcessor(new TensionState(cfg.Tension), cfg,
                SettlementCycle.BuildSteps(production));
            return new SettlementCycle(state, processor, production);
        }

        /// <summary>Крок бере своє місце з DayStepOrder, а не з магічного числа.</summary>
        [Test]
        public void ProductionStep_TakesOrderFromDayStepOrder()
        {
            var cycle = Build(Cfg());
            Assert.AreEqual(DayStepOrder.Production, cycle.Production.Order);
        }

        /// <summary>
        /// Забута реєстрація кроку — найтихіший з можливих збоїв: дні йдуть,
        /// інциденти трапляються, а база нічого не виробляє і ніхто не їсть.
        /// Тому відмова на старті, а не сюрприз на тридцяту добу.
        /// </summary>
        [Test]
        public void Constructor_Refuses_WhenProductionStepIsNotRegistered()
        {
            var cfg = Cfg();
            var state = new BaseState(new Roster(), new ResourceLedger(), cfg);
            var production = new ProductionStep(state);
            var processor = new DayProcessor(new TensionState(cfg.Tension), cfg, DayProcessor.DefaultSteps());

            Assert.Throws<System.ArgumentException>(() => new SettlementCycle(state, processor, production));
        }

        /// <summary>
        /// Календарна доба — дві фази, виробництво зобов'язане статися один раз.
        /// Без нічного гейта база друкувала б подвійний виробіток, і помітити це
        /// можна було б тільки по гаманцю.
        /// </summary>
        [Test]
        public void CalendarDay_ProducesExactlyOnce()
        {
            var cfg = Cfg();
            var cycle = Build(cfg);

            int before = cycle.State.Resources.Get(ResourceType.Food);
            cycle.AdvanceCalendarDay();
            int afterOneDay = cycle.State.Resources.Get(ResourceType.Food) - before;

            cycle.AdvanceCalendarDay();
            int afterTwoDays = cycle.State.Resources.Get(ResourceType.Food) - before;

            Assert.AreEqual(afterOneDay * 2, afterTwoDays, "вторые сутки дали ровно столько же, сколько первые");
            Assert.AreEqual(2, cycle.State.CurrentCycle, "два цикла за двое суток, а не четыре");
        }

        /// <summary>Вночі пости закриті (US-1.5): цикл не рухається взагалі.</summary>
        [Test]
        public void NightPhase_ProducesNothing()
        {
            var cycle = Build(Cfg());

            int before = cycle.State.Resources.Get(ResourceType.Food);
            cycle.AdvanceDay(DayPhase.Night);

            Assert.AreEqual(before, cycle.State.Resources.Get(ResourceType.Food));
            Assert.AreEqual(0, cycle.State.CurrentCycle);
        }

        /// <summary>
        /// Лічильники різні за змістом, але йдуть у ногу: і конвеєр, і база рахують
        /// ДОБУ.
        ///
        /// Очікування переписане при злитті 23.09.2026. Тест був написаний до
        /// ремонту календаря і вимагав десять — тобто закріплював саме той
        /// дефект, який лінія ремонту лупа закрила: CurrentDay рахував фази,
        /// і кожне вікно «в днях» (кулдауни, вікно повторів, grace кризи) було
        /// вдвічі коротшим за заявлене. Тепер доба додається тільки в денній
        /// фазі, і дві доби — це дві доби.
        /// </summary>
        [Test]
        public void Counters_StayInStep()
        {
            var cycle = Build(Cfg());
            for (int i = 0; i < 5; i++) cycle.AdvanceCalendarDay();

            Assert.AreEqual(5, cycle.Processor.CurrentDay, "сутки конвейера — пять, а не десять фаз");
            Assert.AreEqual(5, cycle.State.CurrentCycle, "циклов производства — тоже пять");
        }

        /// <summary>
        /// Головне, заради чого міст: голод доходить до Напруги. Раніше прапорець у
        /// конвеєр не виставляв ніхто, і Поправка №4 тиснула тільки в тестах.
        /// </summary>
        [Test]
        public void Hunger_ReachesTension_ThroughTheBridge()
        {
            var cfg = StarvingCfg();
            var cycle = Build(cfg, startingFood: 0);

            // Перша доба: прокорм більший за виробіток, прапорець встає на завтра.
            cycle.AdvanceCalendarDay();
            Assert.IsTrue(cycle.State.WasHungryLastCycle, "поселение осталось голодным");

            var reports = cycle.AdvanceCalendarDay();
            var dayLedger = TensionLedgerOf(reports[0]);
            Assert.IsTrue(dayLedger.Any(c => c.Driver == TensionDriver.Hunger),
                "голод должен был поднять Напряжение на следующий день");
        }

        /// <summary>Голод тисне раз на добу, а не двічі: ніч його не повторює.</summary>
        [Test]
        public void Hunger_PressesOncePerCalendarDay()
        {
            var cfg = StarvingCfg();
            var cycle = Build(cfg, startingFood: 0);
            cycle.AdvanceCalendarDay();

            var reports = cycle.AdvanceCalendarDay();
            int day = TensionLedgerOf(reports[0]).Count(c => c.Driver == TensionDriver.Hunger);
            int night = TensionLedgerOf(reports[1]).Count(c => c.Driver == TensionDriver.Hunger);

            Assert.AreEqual(1, day, "днём — один раз");
            Assert.AreEqual(0, night, "ночью — ни разу");
        }

        /// <summary>Голодний день б'є і по виробітку: просідання і Напруга йдуть разом.</summary>
        [Test]
        public void HungryDay_AlsoCutsProduction()
        {
            var fed = Build(Cfg());
            var starving = Build(StarvingCfg(), startingFood: 0);

            fed.AdvanceCalendarDay();
            int fedOutput = fed.Production.LastReport.Produced[ResourceType.Food];

            starving.AdvanceCalendarDay();   // голодна доба
            starving.AdvanceCalendarDay();   // просіла
            int starvingOutput = starving.Production.LastReport.Produced[ResourceType.Food];

            Assert.Less(starvingOutput, fedOutput, "после голодного дня база работает хуже");
        }

        // ================= перенесено зі знесеного другого моста =================
        //
        // До 23.09.2026 у проєкті було два мости виробництва в конвеєр: цей і
        // порт IDailyCycle + SettlementCycleStep. Порт знесений — він робив цикл бази
        // публічним. Його гарантії, яких тут не було, переїхали сюди.

        /// <summary>
        /// У доби через міст є матеріальний підсумок. CalendarDay_ProducesExactlyOnce
        /// порівнює дві доби між собою і пройшов би і при нульовому виробітку —
        /// тут стверджується сам факт: десять діб щось виробили.
        /// </summary>
        [Test]
        public void Cycle_ProducesSomethingToSpend()
        {
            var cycle = Build(Cfg());
            int before = cycle.State.Resources.Get(ResourceType.Food);

            for (int day = 0; day < 10; day++) cycle.AdvanceCalendarDay();

            Assert.Greater(cycle.State.Resources.Get(ResourceType.Food), before,
                "Десять суток через мост обязаны что-то произвести: иначе у дня нет материального результата");
        }

        /// <summary>Виробництво підключається одним шляхом, і вузький набір кроків його не містить.</summary>
        [Test]
        public void BuildSteps_IncludesProduction_DefaultStepsDoesNot()
        {
            var state = new BaseState(new Roster(), new ResourceLedger(), Cfg());
            var production = new ProductionStep(state);

            Assert.IsTrue(SettlementCycle.BuildSteps(production).Contains(production),
                "Штатный набор с базой обязан содержать производство: иначе у суток нет материального итога");
            Assert.IsFalse(DayProcessor.DefaultSteps().Any(s => s.Order == DayStepOrder.Production),
                "Узкий набор остаётся без производства — это нужно тестам, которым база не нужна");
        }

        /// <summary>Рани від кризи сходять з часом — і тільки через міст.</summary>
        [Test]
        public void Cycle_HealsWounds_ThatCrisisLeftBehind()
        {
            var cycle = Build(Cfg());
            var farmer = cycle.State.Roster.Get("farmer_1");
            farmer.InjuryPoints = 20.0;

            for (int day = 0; day < 10; day++) cycle.AdvanceCalendarDay();

            Assert.Less(farmer.InjuryPoints, 20.0,
                "Раны обязаны сходить со временем: без цикла в конвейере тридцать очков от кризиса оставались навсегда");
        }

        /// <summary>
        /// Справжня гарантія US-1.3 — не номер кроку, а те, що дні лікування і
        /// виробництва не піднімають загрозу. Єдине допустиме джерело
        /// руху шкали за ситу спокійну добу — фоновий тик від тіра.
        /// </summary>
        [Test]
        public void Cycle_NeverTouchesTension_WhenFed()
        {
            var cycle = Build(Cfg());
            cycle.State.Roster.Get("farmer_1").InjuryPoints = 20.0;

            for (int day = 0; day < 20; day++)
                foreach (var report in cycle.AdvanceCalendarDay())
                    foreach (var change in report.TensionChanges)
                        Assert.AreEqual(TensionDriver.CityTierTick, change.Driver,
                            "Цикл поселения не имеет права двигать Напряжение");
        }

        private static System.Collections.Generic.IReadOnlyList<TensionChange> TensionLedgerOf(DayReport report)
            => report.TensionChanges;
    }
}
