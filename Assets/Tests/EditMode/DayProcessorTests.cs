using System.Linq;
using Game.Core.Balance;
using Game.Core.Loop;
using Game.Core.Pressure;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public class DayProcessorTests
    {
        private static BalanceConfig Cfg() => new BalanceConfig();

        [Test]
        public void Processor_SortsStepsByOrder()
        {
            var cfg = Cfg();
            var tension = new TensionState(cfg.Tension);

            // Подаём шаги в обратном порядке — процессор обязан их упорядочить.
            var p = new DayProcessor(tension, cfg, new IDayStep[] { new SignalStep(), new TensionTickStep() });

            Assert.AreEqual(DayStepOrder.Tension, p.Steps[0].Order);
            Assert.AreEqual(DayStepOrder.Signals, p.Steps[1].Order);
        }

        [Test]
        public void Processor_AdvancesDayCounter()
        {
            var cfg = Cfg();
            var p = new DayProcessor(new TensionState(cfg.Tension), cfg, DayProcessor.DefaultSteps());

            var reports = p.Advance(5);

            Assert.AreEqual(5, p.CurrentDay);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, reports.Select(r => r.Day).ToArray());
        }

        [Test]
        public void Report_CarriesSignalsAndIsIndependentOfNextDay()
        {
            var cfg = Cfg();
            var p = new DayProcessor(new TensionState(cfg.Tension), cfg, DayProcessor.DefaultSteps());

            var first = p.Advance();
            int countAfterFirstDay = first.TensionChanges.Count;
            p.Advance();

            Assert.IsNotNull(first.Signals, "Отчёт обязан нести сигналы — это единственный канал наружу");
            Assert.AreEqual(countAfterFirstDay, first.TensionChanges.Count,
                "Отчёт за прошлый день не должен меняться, когда наступил следующий");
        }

        [Test]
        public void Processor_SameInputs_IdenticalRun()
        {
            string Run()
            {
                var cfg = Cfg();
                var tension = new TensionState(cfg.Tension);
                var p = new DayProcessor(tension, cfg, DayProcessor.DefaultSteps()) { Tier = 3 };

                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < 100; i++)
                {
                    var r = p.Advance();
                    sb.Append(r.Day).Append('=').Append(tension.Band).Append(';');
                }
                return sb.ToString();
            }

            Assert.AreEqual(Run(), Run(), "Кампания обязана быть воспроизводимой день в день");
        }
    }
}
