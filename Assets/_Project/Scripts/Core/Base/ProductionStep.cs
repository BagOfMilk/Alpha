using System;
using Game.Core.Loop;

namespace Game.Core.Base
{
    /// <summary>
    /// Производство базы как шаг дневного конвейера.
    ///
    /// Живёт в Game.Core.Base, а не в Core/Loop, и это не вкусовщина: конвейер
    /// дня не должен знать ни про слоты, ни про ResourceType — зависимость идёт
    /// только в одну сторону, Base → Loop, через порт IDayStep. Обратное
    /// направление запрещено тестом-забором.
    ///
    /// До этого шага в проекте было ДВА дневных цикла: старый AdvanceCycle,
    /// который звали только тесты и демка, и настоящий конвейер, у которого
    /// место под производство было зарезервировано, но пустовало. Голод при
    /// этом не давил вообще: флаг в конвейер никто не выставлял.
    /// </summary>
    public sealed class ProductionStep : IDayStep
    {
        private readonly BaseState _state;

        public ProductionStep(BaseState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public int Order => DayStepOrder.Production;

        /// <summary>
        /// Сводка последнего цикла. Числа производства идут ЭТИМ каналом, а не
        /// через DayReport: DayReport лежит в Core/Loop и потому не может нести
        /// ResourceType, а кошелёк игрок и так видит — прятать нужно метрики
        /// скрытых шкал, а не заработанное.
        /// </summary>
        public CycleReport LastReport { get; private set; }

        public void Execute(DayContext ctx)
        {
            // Ночью позиции закрыты (US-1.5), а сутки — это две фазы. Без этой
            // строки база производила бы дважды в день, и заметить это можно
            // было бы только по кошельку.
            if (ctx.IsNight) return;

            LastReport = _state.AdvanceCycle();
        }
    }
}
