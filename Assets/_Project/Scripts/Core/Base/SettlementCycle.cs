using System;
using System.Collections.Generic;
using Game.Core.Loop;

namespace Game.Core.Base
{
    /// <summary>
    /// Единственный способ продвинуть время в поселении.
    ///
    /// Связывает модуль базы и конвейер дня так, чтобы «производство идёт» и
    /// «день прошёл» перестали быть двумя разными событиями. Раньше их было
    /// можно вызвать по отдельности — и в проекте это ровно так и было:
    /// демка базы крутила AdvanceCycle, демка хроники крутила DayProcessor, и
    /// ни одна не видела половины игры.
    ///
    /// Флаг голода переносится ЗДЕСЬ и только здесь. Первичный владелец —
    /// BaseState.WasHungryLastCycle, конвейер получает зеркало перед днём.
    /// Двух писателей у флага нет: ни ProductionStep, ни HungerStep в него не
    /// пишут, только читают.
    /// </summary>
    public sealed class SettlementCycle
    {
        public BaseState State { get; }
        public DayProcessor Processor { get; }
        public ProductionStep Production { get; }

        public SettlementCycle(BaseState state, DayProcessor processor, ProductionStep production)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            Processor = processor ?? throw new ArgumentNullException(nameof(processor));
            Production = production ?? throw new ArgumentNullException(nameof(production));

            // Забытая регистрация шага дала бы самый тихий из возможных сбоев:
            // дни идут, инциденты случаются, а база ничего не производит и
            // никто не ест. Поэтому это отказ на старте, а не сюрприз на 30-й день.
            if (!Contains(processor.Steps, production))
                throw new ArgumentException(
                    "ProductionStep не зарегистрирован в DayProcessor — собери шаги через SettlementCycle.BuildSteps",
                    nameof(processor));
        }

        /// <summary>
        /// Стандартный набор шагов вместе с производством. Отдельная фабрика
        /// нужна, чтобы порядок по-прежнему задавался только DayStepOrder, а
        /// не порядком добавления в список.
        /// </summary>
        public static IEnumerable<IDayStep> BuildSteps(ProductionStep production)
        {
            var steps = new List<IDayStep>(DayProcessor.DefaultSteps());
            if (production != null) steps.Add(production);
            return steps;
        }

        /// <summary>Одна фаза дня. Зеркало голода обновляется перед ней.</summary>
        public DayReport AdvanceDay(DayPhase phase = DayPhase.Day)
        {
            SyncHunger();
            return Processor.Advance(phase);
        }

        /// <summary>
        /// Полные сутки: день, затем ночь. Производство идёт ровно один раз —
        /// ночная фаза отсеивается самим шагом.
        ///
        /// Голод синхронизируется один раз, перед днём: ночь не может изменить
        /// запас еды, а второй синк внутри суток означал бы, что сегодняшняя
        /// нехватка давит дважды.
        /// </summary>
        public List<DayReport> AdvanceCalendarDay()
        {
            SyncHunger();
            return new List<DayReport>
            {
                Processor.Advance(DayPhase.Day),
                Processor.Advance(DayPhase.Night)
            };
        }

        private void SyncHunger()
        {
            Processor.IsHungry = State.WasHungryLastCycle;
        }

        private static bool Contains(IReadOnlyList<IDayStep> steps, IDayStep target)
        {
            if (steps == null) return false;
            for (int i = 0; i < steps.Count; i++)
                if (ReferenceEquals(steps[i], target)) return true;
            return false;
        }
    }
}
