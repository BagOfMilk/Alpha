using System.Linq;
using Game.Core.Balance;
using Game.Core.Loop;
using Game.Core.Pressure;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Контракт R6: <see cref="DayProcessor.QueueExternal"/> — єдиний
    /// узаконений місток, яким зовнішні системи (укази ради B5, вибори в
    /// квестах B6) рухатимуть Напругу поза власним конвеєром дня.
    /// Список драйверів лишається закритим (інваріант 5) — заявка лише
    /// застосовується через ІСНУЮЧИЙ драйвер на тику наступної фази.
    ///
    /// Ревʼю А1 знайшло: сам метод працював коректно, але жодного разу не був перевірений
    /// тестом, хоча це єдиний контракт, на який спираються два майбутні
    /// пакети. Ці тести закривають розрив.
    /// </summary>
    public class TensionExternalQueueTests
    {
        private static DayProcessor MakeProcessor(BalanceConfig cfg, int startValue = 300)
        {
            var tension = new TensionState(cfg.Tension, startValue);
            return new DayProcessor(tension, cfg, DayProcessor.DefaultSteps())
            {
                Tier = 1,
                OrderLevel = 1 // Присмотр: множник тика ×1.0 — рахувати простіше.
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

            // Тір 1 / Уклад «Присмотр» дає рівно +1 фонового тика — єдиний
            // інший запис журналу за цю добу. Підсумок зобов'язаний бути сумою обох.
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
            p.Advance(DayPhase.Day); // зливає заявку на цьому тику

            var next = p.Advance(DayPhase.Night);

            Assert.IsFalse(next.TensionChanges.Any(c => c.Driver == TensionDriver.CouncilEdict),
                "Заявка — одноразовая: следующая фаза не обязана снова тронуть Напряжение тем же драйвером");
        }

        [Test]
        public void QueueExternal_QueuedBeforeAnyAdvance_AppliesOnTheVeryNextPhase()
        {
            // R6 буквально: «застосовується тиком НАСТУПНОЇ фази», а не обов'язково
            // наступної доби — заявка, подана ввечері, не зобов'язана чекати ранку.
            var cfg = new BalanceConfig();
            var p = MakeProcessor(cfg);

            p.QueueExternal(TensionDriver.CouncilEdict, -50);
            var report = p.Advance(DayPhase.Night);

            var entry = report.TensionChanges.Single(c => c.Driver == TensionDriver.CouncilEdict);
            Assert.AreEqual(-50, entry.Applied, "Ближайшая фаза — неважно, день или ночь — обязана слить заявку");
        }

        // ================= G22: квест між закритими добами =================
        //
        // CampaignPacingTests.Pacing_BandChangeIsNeverMute (інваріант 4) знайшов
        // BandChangesWithoutSignal == 2 після G21: політика AggressiveChoices
        // на добу 20 (тір 1) і 40 (тір 2) міняла полосу без жодного сигналу.
        // Причина — не бюджет (його G21 вже закрив, мандатні кандидати йдуть
        // понад нього), а те, що кандидат узагалі не будувався: CampaignSimulator
        // кликав TensionDrivers.QuestChoice(processor.Tension, …) НАПРЯМУ, між
        // Advance() — доба в цій точці циклу вже закрита (минула фаза
        // віддала звіт, наступна ще не почалась). Apply() чесно міняє Band і
        // пише запис у денний журнал, але наступний Advance() починається з
        // TensionState.BeginDay(), який безумовно чистить журнал ДО того,
        // як SignalStep встигає його прочитати. Полоса міняється по-справжньому,
        // а сигнал про неї не будується ніколи — ні в цьому звіті (SignalStep ще
        // не кликали), ні в наступному (журнал вже порожній).

        [Test]
        public void DirectQuestChoiceBetweenClosedPhases_LosesTheBandSignal_DocumentedTrap()
        {
            var cfg = new BalanceConfig();
            var p = MakeProcessor(cfg, startValue: 0);
            p.Advance(DayPhase.Day); // доба закрита — вікно між Advance() відкрите

            // 5 великих виборів = 200 очок = рівно поріг «Ропоту» (див. також
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
            p.Advance(DayPhase.Day); // той самий міждобовий момент, що і в пастці вище

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
