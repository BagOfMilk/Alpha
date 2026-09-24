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

        // ================= G22: квест между закрытыми сутками =================
        //
        // CampaignPacingTests.Pacing_BandChangeIsNeverMute (инвариант 4) нашёл
        // BandChangesWithoutSignal == 2 после G21: политика AggressiveChoices
        // на сутки 20 (тир 1) и 40 (тир 2) меняла полосу без единого сигнала.
        // Причина — не бюджет (его G21 уже закрыл, мандатные кандидаты идут
        // сверх него), а то, что кандидат вообще не строился: CampaignSimulator
        // звал TensionDrivers.QuestChoice(processor.Tension, …) НАПРЯМУЮ, между
        // Advance() — сутки на этой точке цикла уже закрыты (прошлая фаза
        // отдала отчёт, следующая ещё не начата). Apply() честно меняет Band и
        // пишет запись в дневной журнал, но следующий Advance() начинается с
        // TensionState.BeginDay(), который безусловно чистит журнал ДО того,
        // как SignalStep успевает его прочитать. Полоса меняется по-настоящему,
        // а сигнал о ней не строится никогда — ни в этом отчёте (SignalStep ещё
        // не звали), ни в следующем (журнал уже пуст).

        [Test]
        public void DirectQuestChoiceBetweenClosedPhases_LosesTheBandSignal_DocumentedTrap()
        {
            var cfg = new BalanceConfig();
            var p = MakeProcessor(cfg, startValue: 0);
            p.Advance(DayPhase.Day); // сутки закрыты — окно между Advance() открыто

            // 5 крупных выборов = 200 очков = ровно порог «Ропота» (см. также
            // TensionStateTests.Tension_QuestChoice_MovesBandAndFiresEvent).
            for (int i = 0; i < 5; i++)
                TensionDrivers.QuestChoice(p.Tension, TensionDrivers.ChoiceWeight.Major, "q" + i, cfg);

            var report = p.Advance(DayPhase.Night);

            Assert.AreEqual(TensionBand.Murmur, p.Tension.Band,
                "полоса меняется по-настоящему — ловушка не в этом");
            Assert.IsFalse(
                report.Signals.Requests.Any(r => r.TopicId != null && r.TopicId.StartsWith("tension.band.")),
                "ЛОВУШКА (G22): прямой Apply между закрытыми сутками отдаёт смену полосы без " +
                "сигнала — внешние системы обязаны идти мостиком QueueQuestChoice/QueueEventOutcome, " +
                "а не TensionDrivers.QuestChoice(processor.Tension, …) напрямую");
        }

        [Test]
        public void QueueQuestChoice_BandChangeBetweenClosedPhases_IsHeardInTheVeryNextReport()
        {
            var cfg = new BalanceConfig();
            var p = MakeProcessor(cfg, startValue: 0);
            p.Advance(DayPhase.Day); // тот же межсуточный момент, что и в ловушке выше

            for (int i = 0; i < 5; i++)
                p.QueueQuestChoice(TensionDrivers.ChoiceWeight.Major);

            var report = p.Advance(DayPhase.Night);

            Assert.AreEqual(TensionBand.Murmur, p.Tension.Band, "выбор обязан был перевести полосу");
            Assert.IsTrue(
                report.Signals.Requests.Any(r => r.TopicId == "tension.band.Murmur"),
                "инвариант 4: смена полосы обязана прозвучать в ТОМ ЖЕ отчёте, где она случилась, " +
                "даже если запрос пришёл между закрытыми сутками — мостик R6 кладёт заявку на тик " +
                "следующей фазы ДО SignalStep той же фазы");
        }

        [Test]
        public void QueueEventOutcome_UsesTheSameWeightTable_AsDirectEventOutcome()
        {
            var cfg = new BalanceConfig();
            var p = MakeProcessor(cfg, startValue: 300);

            p.QueueEventOutcome(TensionDrivers.ChoiceWeight.Major);
            var report = p.Advance(DayPhase.Day);

            var entry = report.TensionChanges.Single(c => c.Driver == TensionDriver.EventOutcome);
            Assert.AreEqual(-cfg.Tension.ChoiceMajor, entry.Applied,
                "QueueEventOutcome обязан снимать ровно ту же величину, что и прямой EventOutcome");
        }
    }
}
