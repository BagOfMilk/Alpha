using Game.Core.Pressure;

namespace Game.Core.Loop
{
    /// <summary>
    /// Голодный день поднимает Напряжение (Поправка №4).
    ///
    /// Почему отдельным шагом, а не прямо из BaseState: городской слой и модуль
    /// базы намеренно не знают друг о друге. База выставляет факт голода в
    /// контекст дня, а давление наводит уже конвейер — так же, как это сделано
    /// с остальными драйверами.
    ///
    /// Стоит перед тиком Напряжения: сегодняшний голод должен войти в сегодняшнюю
    /// полосу, иначе таблица инцидентов взвесится по вчерашней обстановке.
    /// </summary>
    public sealed class HungerStep : IDayStep
    {
        public int Order => DayStepOrder.Hunger;

        public void Execute(DayContext ctx)
        {
            if (!ctx.IsHungry) return;
            TensionDrivers.Hunger(ctx.Tension, "hunger:day" + ctx.Day, ctx.Balance);
        }
    }
}
