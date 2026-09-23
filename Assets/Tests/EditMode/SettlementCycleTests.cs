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
    /// Мост между модулем базы и конвейером дня. До него в проекте было два
    /// дневных цикла, не знающих друг о друге: производство шло мимо конвейера,
    /// а голод в конвейере не давил, потому что флаг в него никто не выставлял.
    ///
    /// Тесты здесь про одно: что теперь способ продвинуть время ровно один и
    /// что сутки нельзя прожить дважды.
    /// </summary>
    public class SettlementCycleTests
    {
        private static BalanceConfig Cfg() => new BalanceConfig();

        /// <summary>
        /// Баланс, при котором поселение не прокормить. Ферма даёт 14 еды в
        /// день, поэтому «ноль в закромах» голода не создаёт — голод создаёт
        /// прокорм, который выше выработки. Ровно эту ошибку и поймал первый
        /// прогон этих тестов.
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

        /// <summary>Шаг берёт своё место из DayStepOrder, а не из магического числа.</summary>
        [Test]
        public void ProductionStep_TakesOrderFromDayStepOrder()
        {
            var cycle = Build(Cfg());
            Assert.AreEqual(DayStepOrder.Production, cycle.Production.Order);
        }

        /// <summary>
        /// Забытая регистрация шага — самый тихий из возможных сбоев: дни идут,
        /// инциденты случаются, а база ничего не производит и никто не ест.
        /// Поэтому отказ на старте, а не сюрприз на тридцатый день.
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
        /// Календарные сутки — две фазы, производство обязано случиться один раз.
        /// Без ночного гейта база печатала бы двойную выработку, и заметить это
        /// можно было бы только по кошельку.
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

        /// <summary>Ночью позиции закрыты (US-1.5): цикл не двигается вовсе.</summary>
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
        /// Счётчики разные по смыслу, но идут в ногу: и конвейер, и база считают
        /// СУТКИ.
        ///
        /// Ожидание переписано при слиянии 23.09.2026. Тест был написан до
        /// починки календаря и требовал десять — то есть закреплял ровно тот
        /// дефект, который линия ремонта лупа закрыла: CurrentDay считал фазы,
        /// и каждое окно «в днях» (кулдауны, окно повторов, grace кризиса) было
        /// вдвое короче заявленного. Теперь сутки прибавляются только в дневной
        /// фазе, и двое суток — это двое суток.
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
        /// Главное, ради чего мост: голод доходит до Напряжения. Раньше флаг в
        /// конвейер не выставлял никто, и Поправка №4 давила только в тестах.
        /// </summary>
        [Test]
        public void Hunger_ReachesTension_ThroughTheBridge()
        {
            var cfg = StarvingCfg();
            var cycle = Build(cfg, startingFood: 0);

            // Первые сутки: прокорм больше выработки, флаг встаёт на завтра.
            cycle.AdvanceCalendarDay();
            Assert.IsTrue(cycle.State.WasHungryLastCycle, "поселение осталось голодным");

            var reports = cycle.AdvanceCalendarDay();
            var dayLedger = TensionLedgerOf(reports[0]);
            Assert.IsTrue(dayLedger.Any(c => c.Driver == TensionDriver.Hunger),
                "голод должен был поднять Напряжение на следующий день");
        }

        /// <summary>Голод давит раз в сутки, а не дважды: ночь его не повторяет.</summary>
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

        /// <summary>Голодный день бьёт и по выработке: просадка и Напряжение идут вместе.</summary>
        [Test]
        public void HungryDay_AlsoCutsProduction()
        {
            var fed = Build(Cfg());
            var starving = Build(StarvingCfg(), startingFood: 0);

            fed.AdvanceCalendarDay();
            int fedOutput = fed.Production.LastReport.Produced[ResourceType.Food];

            starving.AdvanceCalendarDay();   // голодные сутки
            starving.AdvanceCalendarDay();   // просевшие
            int starvingOutput = starving.Production.LastReport.Produced[ResourceType.Food];

            Assert.Less(starvingOutput, fedOutput, "после голодного дня база работает хуже");
        }

        // ================= перенесено со снесённого второго моста =================
        //
        // До 23.09.2026 в проекте было два моста производства в конвейер: этот и
        // порт IDailyCycle + SettlementCycleStep. Порт снесён — он делал цикл базы
        // публичным. Его гарантии, которых здесь не было, переехали сюда.

        /// <summary>
        /// У суток через мост есть материальный итог. CalendarDay_ProducesExactlyOnce
        /// сравнивает двое суток между собой и прошёл бы и при нулевой выработке —
        /// здесь утверждается сам факт: десять суток что-то произвели.
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

        /// <summary>Производство подключается одним путём, и узкий набор шагов его не содержит.</summary>
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

        /// <summary>Раны от кризиса сходят со временем — и только через мост.</summary>
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
        /// Настоящая гарантия US-1.3 — не номер шага, а то, что дни лечения и
        /// производства не растят угрозу. Единственный допустимый источник
        /// движения шкалы за сытые спокойные сутки — фоновый тик от тира.
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
