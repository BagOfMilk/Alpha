using Game.Core.World;

namespace Game.Core.Loop
{
    /// <summary>
    /// Крок накопичувачів. Стоїть ПІСЛЯ тика Напруги, щоб ставки рахувалися за
    /// сьогоднішньою полосою, і ПЕРЕД інцидентами, щоб спрацьоване одразу розв'язувалося.
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

            // Розряджається тільки джерело, якому є чим спрацювати: вирішує
            // той самий відбір, що слідом застосує крок інцидентів. Без цього
            // накопичувач без свого інциденту скидався мовчки.
            var tick = ctx.Pulse.Advance(pulseCtx, sourceId => IncidentStep.HasIncidentFor(ctx, sourceId));

            // Вночі передвісники дістаються тільки тому, хто не спить.
            // Спати — значить втратити сигнали ночі (Поправка №3.9).
            bool canHear = !ctx.IsNight || ctx.IsPatrolling;
            if (canHear)
            {
                ctx.Forewarnings.AddRange(tick.Forewarnings);

                // Ступінь зараховується тільки почута. Проспане
                // попередження не згоряє — накопичувач запропонує його знову.
                ctx.Pulse.MarkDelivered(tick.Forewarnings, ctx.Day);
            }

            ctx.FiredSourceIds.AddRange(tick.FiredSourceIds);
        }
    }
}
