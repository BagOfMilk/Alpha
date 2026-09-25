using System;
using Game.Core.Loop;

namespace Game.Core.Base
{
    /// <summary>
    /// Вироблення бази як крок денного конвеєра.
    ///
    /// Живе в Game.Core.Base, а не в Core/Loop, і це не смакова примха: конвеєр
    /// дня не повинен знати ні про слоти, ні про ResourceType — залежність іде
    /// лише в один бік, Base → Loop, через порт IDayStep. Зворотний
    /// напрямок заборонений тестом-парканом.
    ///
    /// До цього кроку в проєкті було ДВА денних цикли: старий AdvanceCycle,
    /// якого кликали тільки тести й демка, і справжній конвеєр, у якого
    /// місце під вироблення було зарезервоване, але порожнювало. Голод при
    /// цьому не тиснув зовсім: прапорець у конвеєр ніхто не виставляв.
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
        /// Зведення останнього циклу. Числа вироблення йдуть ЦИМ каналом, а не
        /// через DayReport: DayReport лежить у Core/Loop і тому не може нести
        /// ResourceType, а гаманець гравець і так бачить — ховати треба метрики
        /// прихованих шкал, а не зароблене.
        /// </summary>
        public CycleReport LastReport { get; private set; }

        public void Execute(DayContext ctx)
        {
            // Вночі позиції закриті (US-1.5), а доба — це дві фази. Без цього
            // рядка база виробляла б двічі на день, і помітити це можна
            // було б тільки по гаманцю.
            if (ctx.IsNight) return;

            LastReport = _state.AdvanceCycle();
        }
    }
}
