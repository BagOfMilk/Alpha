using Game.Core.Signals;

namespace Game.Core.Loop
{
    /// <summary>
    /// Последний содержательный шаг дня: превращает финальное состояние
    /// в то, что игрок увидит и услышит.
    /// </summary>
    public sealed class SignalStep : IDayStep
    {
        public int Order => DayStepOrder.Signals;

        public void Execute(DayContext ctx)
        {
            ctx.Signals = SignalComposer.Compose(
                ctx.Tension.Band,
                ctx.Tension.DayLedger,
                ctx.Tier,
                ctx.Balance.Signals);
        }
    }
}
