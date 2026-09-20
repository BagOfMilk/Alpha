using System.Collections.Generic;
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
    /// Сутки с материальным результатом.
    ///
    /// Цикл поселения существовал, но из конвейера дня не вызывался: девяносто
    /// суток хроники проходили так, что ни одно число, которое игрок мог бы
    /// потратить, не менялось, а очки ранения не сходили никогда.
    /// </summary>
    public class SettlementCycleStepTests
    {
        private static BalanceConfig Cfg() => new BalanceConfig();

        private static (BaseState state, Roster roster) BuildBase(BalanceConfig cfg)
        {
            var roster = new Roster();
            var arch = new CompanionArchetype("worker", "worker");
            arch.BaseStats.Set(StatType.Logistics, 8);
            arch.BaseStats.Set(StatType.Survival, 8);
            roster.Add(arch.CreateInstance("worker"));

            var state = new BaseState(roster, new ResourceLedger(), cfg);

            var slot = DefaultContent.AllSlots().First(s => s.OutputKind == SlotOutputKind.Resource);
            state.AddSlot(slot);
            state.TryAssign("worker", slot.Id);

            return (state, roster);
        }

        private static DayProcessor BuildProcessor(BalanceConfig cfg, BaseState baseState)
        {
            var steps = new List<IDayStep>(DayProcessor.DefaultSteps());
            steps.Add(new SettlementCycleStep(baseState));

            return new DayProcessor(new TensionState(cfg.Tension), cfg, steps) { Tier = 1 };
        }

        [Test]
        public void Cycle_AsDayStep_ProducesSomethingToSpend()
        {
            var cfg = Cfg();
            var (baseState, _) = BuildBase(cfg);
            var p = BuildProcessor(cfg, baseState);

            var slot = DefaultContent.AllSlots().First(x => x.OutputKind == SlotOutputKind.Resource);
            int before = baseState.Resources.Get(slot.OutputResource);

            for (int day = 0; day < 10; day++) p.AdvanceFullDay();

            Assert.Greater(baseState.Resources.Get(slot.OutputResource), before,
                "Десять суток в конвейере обязаны что-то произвести: иначе у дня нет материального результата");
        }

        [Test]
        public void Cycle_RunsOncePerCalendarDay_NotPerPhase()
        {
            var cfg = Cfg();
            var (baseState, _) = BuildBase(cfg);
            var p = BuildProcessor(cfg, baseState);

            for (int day = 0; day < 7; day++) p.AdvanceFullDay();

            Assert.AreEqual(7, baseState.CurrentCycle,
                "Ночь принадлежит тем же суткам: иначе поселение производит и ест дважды за день");
        }

        [Test]
        public void Cycle_HealsWounds_ThatCrisisLeftBehind()
        {
            var cfg = Cfg();
            var (baseState, roster) = BuildBase(cfg);
            roster.Get("worker").InjuryPoints = 20.0;

            var p = BuildProcessor(cfg, baseState);
            for (int day = 0; day < 10; day++) p.AdvanceFullDay();

            Assert.Less(roster.Get("worker").InjuryPoints, 20.0,
                "Раны обязаны сходить со временем: без этого шага тридцать очков от кризиса оставались навсегда");
        }

        [Test]
        public void Cycle_NeverTouchesTension()
        {
            var cfg = Cfg();
            var (baseState, roster) = BuildBase(cfg);
            roster.Get("worker").InjuryPoints = 20.0;

            var p = BuildProcessor(cfg, baseState);

            // Настоящая гарантия US-1.3 — не номер шага, а то, что дни лечения и
            // производства не растят угрозу. Единственный допустимый источник
            // движения шкалы за спокойные сутки — фоновый тик от тира.
            for (int day = 0; day < 20; day++)
                foreach (var report in p.AdvanceFullDay())
                    foreach (var change in report.TensionChanges)
                        Assert.AreEqual(TensionDriver.CityTierTick, change.Driver,
                            "Цикл поселения не имеет права двигать Напряжение");
        }
    }
}
