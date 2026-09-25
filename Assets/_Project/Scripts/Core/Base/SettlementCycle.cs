using System;
using System.Collections.Generic;
using Game.Core.Loop;

namespace Game.Core.Base
{
    /// <summary>
    /// Єдиний спосіб просунути час у поселенні.
    ///
    /// Пов'язує модуль бази і конвеєр дня так, щоб «виробництво йде» і
    /// «день минув» перестали бути двома різними подіями. Раніше їх можна
    /// було викликати окремо — і в проєкті це рівно так і було:
    /// демка бази крутила AdvanceCycle, демка хроніки крутила DayProcessor, і
    /// жодна не бачила половини гри.
    ///
    /// Прапорець голоду переноситься САМЕ ТУТ і тільки тут. Первинний власник —
    /// BaseState.WasHungryLastCycle, конвеєр отримує дзеркало перед днем.
    /// Двох писачів у прапорця немає: ні ProductionStep, ні HungerStep у нього не
    /// пишуть, тільки читають.
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

            // Забута реєстрація кроку дала б найтихішу з можливих несправностей:
            // дні йдуть, інциденти трапляються, а база нічого не виробляє і
            // ніхто не їсть. Тому це відмова на старті, а не сюрприз на 30-й день.
            if (!Contains(processor.Steps, production))
                throw new ArgumentException(
                    "ProductionStep не зарегистрирован в DayProcessor — собери шаги через SettlementCycle.BuildSteps",
                    nameof(processor));
        }

        /// <summary>
        /// Стандартний набір кроків разом із виробництвом. Окрема фабрика
        /// потрібна, щоб порядок і надалі задавався тільки DayStepOrder, а
        /// не порядком додавання у список.
        /// </summary>
        public static IEnumerable<IDayStep> BuildSteps(ProductionStep production)
        {
            var steps = new List<IDayStep>(DayProcessor.DefaultSteps());
            if (production != null) steps.Add(production);
            return steps;
        }

        /// <summary>Одна фаза дня. Дзеркало голоду оновлюється перед нею.</summary>
        public DayReport AdvanceDay(DayPhase phase = DayPhase.Day)
        {
            SyncHunger();
            return Processor.Advance(phase);
        }

        /// <summary>
        /// Повна доба: день, потім ніч. Виробництво йде рівно один раз —
        /// нічна фаза відсіюється самим кроком.
        ///
        /// Голод синхронізується один раз, перед днем: ніч не може змінити
        /// запас їжі, а другий синк усередині доби означав би, що сьогоднішня
        /// нестача тисне двічі.
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
