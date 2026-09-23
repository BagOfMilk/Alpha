using System.Linq;
using Game.Core.Balance;
using Game.Core.Loop;
using Game.Core.Pressure;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Контракт R6: <see cref="DayProcessor.QueueExternal"/> — единственный
    /// узаконенный мостик, которым внешние системы (указы совета B5, выборы в
    /// квестах B6) будут двигать Напряжение вне собственного конвейера дня.
    /// Список драйверов остаётся закрытым (инвариант 5) — заявка лишь
    /// применяется через СУЩЕСТВУЮЩИЙ драйвер на тике следующей фазы.
    ///
    /// Ревью А1 нашло: сам метод работал корректно, но ни разу не был проверен
    /// тестом, хотя это единственный контракт, на который опираются два будущих
    /// пакета. Эти тесты закрывают разрыв.
    /// </summary>
    public class TensionExternalQueueTests
    {
        private static DayProcessor MakeProcessor(BalanceConfig cfg, int startValue = 300)
        {
            var tension = new TensionState(cfg.Tension, startValue);
            return new DayProcessor(tension, cfg, DayProcessor.DefaultSteps())
            {
                Tier = 1,
                OrderLevel = 1 // Присмотр: множитель тика ×1.0 — считать проще.
            };
        }

        [Test]
        public void QueueExternal_AppliesExactAmount_ThroughTheNamedDriver()
        {
            var cfg = new BalanceConfig();
            var p = MakeProcessor(cfg);
            int before = p.Tension.Value;

            p.QueueExternal(TensionDriver.CouncilEdict, -37);
            var report = p.Advance(DayPhase.Day);

            var entry = report.TensionChanges.Single(c => c.Driver == TensionDriver.CouncilEdict);
            Assert.AreEqual(-37, entry.Requested, "Заявленная величина обязана дойти без искажения");
            Assert.AreEqual(-37, entry.Applied,
                "И примениться ровно на столько же — CouncilEdict в белом списке понижающих");
            Assert.IsFalse(entry.Rejected);

            // Тир 1 / Уклад «Присмотр» даёт ровно +1 фонового тика — единственная
            // другая запись журнала за эти сутки. Итог обязан быть суммой обеих.
            var tick = report.TensionChanges.Single(c => c.Driver == TensionDriver.CityTierTick);
            Assert.AreEqual(1, tick.Applied, "Фоновый тик тира 1 за день — ровно единица");
            Assert.AreEqual(before - 37 + 1, p.Tension.Value,
                "Итоговое Напряжение — фон плюс ровно заявленная внешняя величина, не больше и не меньше");
        }

        [Test]
        public void QueueExternal_ZeroAmount_IsNoOp()
        {
            var cfg = new BalanceConfig();
            var p = MakeProcessor(cfg);

            p.QueueExternal(TensionDriver.CouncilEdict, 0);
            var report = p.Advance(DayPhase.Day);

            Assert.IsFalse(report.TensionChanges.Any(c => c.Driver == TensionDriver.CouncilEdict),
                "Нулевая заявка не обязана оставлять след в журнале — это не событие вовсе");
        }

        [Test]
        public void QueueExternal_IsOneShot_NotReappliedOnFollowingPhase()
        {
            var cfg = new BalanceConfig();
            var p = MakeProcessor(cfg);

            p.QueueExternal(TensionDriver.CouncilEdict, -37);
            p.Advance(DayPhase.Day); // сливает заявку на этом тике

            var next = p.Advance(DayPhase.Night);

            Assert.IsFalse(next.TensionChanges.Any(c => c.Driver == TensionDriver.CouncilEdict),
                "Заявка — одноразовая: следующая фаза не обязана снова тронуть Напряжение тем же драйвером");
        }

        [Test]
        public void QueueExternal_QueuedBeforeAnyAdvance_AppliesOnTheVeryNextPhase()
        {
            // R6 буквально: «применяется тиком СЛЕДУЮЩЕЙ фазы», а не обязательно
            // следующих суток — заявка, поданная вечером, не обязана ждать утра.
            var cfg = new BalanceConfig();
            var p = MakeProcessor(cfg);

            p.QueueExternal(TensionDriver.CouncilEdict, -50);
            var report = p.Advance(DayPhase.Night);

            var entry = report.TensionChanges.Single(c => c.Driver == TensionDriver.CouncilEdict);
            Assert.AreEqual(-50, entry.Applied, "Ближайшая фаза — неважно, день или ночь — обязана слить заявку");
        }
    }
}
