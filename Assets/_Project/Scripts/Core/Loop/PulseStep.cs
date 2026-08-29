using Game.Core.World;

namespace Game.Core.Loop
{
    /// <summary>
    /// Шаг накопителей. Стоит ПОСЛЕ тика Напряжения, чтобы ставки считались по
    /// сегодняшней полосе, и ПЕРЕД инцидентами, чтобы сработавшее сразу разрешалось.
    /// </summary>
    public sealed class PulseStep : IDayStep
    {
        public int Order => DayStepOrder.Pulse;

        public void Execute(DayContext ctx)
        {
            if (ctx.Pulse == null) return;

            var pulseCtx = new PulseContext(
                ctx.Day,
                ctx.IsNight,
                ctx.Tier,
                (int)ctx.Tension.Band,
                ctx.IsPatrolling);

            var tick = ctx.Pulse.Advance(pulseCtx);

            // Ночью предвестники достаются только тому, кто не спит.
            // Спать — значит потерять сигналы ночи (Поправка №3.9).
            bool canHear = !ctx.IsNight || ctx.IsPatrolling;
            if (canHear)
                ctx.Forewarnings.AddRange(tick.Forewarnings);

            ctx.FiredSourceIds.AddRange(tick.FiredSourceIds);
        }
    }
}
