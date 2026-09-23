using Game.Core.Pressure;

namespace Game.Core.Loop
{
    /// <summary>
    /// Фоновый тик Напряжения от тира города — ЕДИНСТВЕННЫЙ пассивный источник
    /// давления в игре. Всё остальное растёт только от действий игрока.
    ///
    /// Здесь же живёт пассивный дренаж (Храм, Укрепления) — на Э0 он выключен,
    /// пока нет зданий.
    /// </summary>
    public sealed class TensionTickStep : IDayStep
    {
        public int Order => DayStepOrder.Tension;

        public void Execute(DayContext ctx)
        {
            // Внешняя очередь (R6, DayProcessor.QueueExternal) сливается на
            // ПЕРВОМ тике после заявки — фаза не важна, иначе заявка, поданная
            // вечером, дожидалась бы следующего дня. Драйвер приходит готовым от
            // вызывающего (список драйверов закрыт — инвариант 5), здесь он
            // просто применяется тем же путём, что и любой другой.
            if (ctx.ExternalTensionQueue != null)
                for (int i = 0; i < ctx.ExternalTensionQueue.Count; i++)
                {
                    var entry = ctx.ExternalTensionQueue[i];
                    ctx.Tension.Apply(entry.Driver, entry.Amount, "external:" + entry.Driver);
                }

            // Фоновый тик — СУТОЧНЫЙ. Ночь принадлежит тем же суткам, иначе
            // город получает две порции фонового давления за день, и якорь
            // «хутор не доходит до кризиса за кампанию» ломается вдвое.
            if (ctx.IsNight) return;

            var cfg = ctx.Balance.Tension;
            double tick = cfg.TierTick(ctx.Tier, ctx.OrderLevel);
            if (tick != 0.0)
                ctx.Tension.ApplyFractional(TensionDriver.CityTierTick, tick, "tier:" + ctx.Tier);
        }
    }
}
