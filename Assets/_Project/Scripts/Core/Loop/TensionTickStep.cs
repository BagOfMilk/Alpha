using Game.Core.Pressure;

namespace Game.Core.Loop
{
    /// <summary>
    /// Фоновий тик Напруги від тіра міста — ЄДИНЕ пасивне джерело
    /// тиску в грі. Все інше росте тільки від дій гравця.
    ///
    /// Тут же живе пасивний дренаж (Храм, Укріплення) — на Е0 він вимкнений,
    /// поки немає будівель.
    /// </summary>
    public sealed class TensionTickStep : IDayStep
    {
        public int Order => DayStepOrder.Tension;

        public void Execute(DayContext ctx)
        {
            // Зовнішня черга (R6, DayProcessor.QueueExternal) зливається на
            // ПЕРШОМУ тику після заявки — фаза не важлива, інакше заявка, подана
            // ввечері, чекала б наступного дня. Драйвер приходить готовим від
            // того, хто викликає (список драйверів закритий — інваріант 5), тут він
            // просто застосовується тим самим шляхом, що й будь-який інший.
            if (ctx.ExternalTensionQueue != null)
                for (int i = 0; i < ctx.ExternalTensionQueue.Count; i++)
                {
                    var entry = ctx.ExternalTensionQueue[i];
                    ctx.Tension.Apply(entry.Driver, entry.Amount, "external:" + entry.Driver);
                }

            // Фоновий тик — ДОБОВИЙ. Ніч належить тій самій добі, інакше
            // місто отримує дві порції фонового тиску за день, і якір
            // «хутір не доходить до кризи за кампанію» ламається вдвічі.
            if (ctx.IsNight) return;

            var cfg = ctx.Balance.Tension;
            double tick = cfg.TierTick(ctx.Tier, ctx.OrderLevel);
            if (tick != 0.0)
                ctx.Tension.ApplyFractional(TensionDriver.CityTierTick, tick, "tier:" + ctx.Tier);
        }
    }
}
