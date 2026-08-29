using Game.Core.Balance;
using Game.Core.Loop;
using Game.Core.Pressure;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Тесты-гарантии ограничений GDD. Их ценность не в проверке арифметики,
    /// а в том, что они делают нарушение дизайн-правил невозможным незаметно.
    /// </summary>
    public class TensionStateTests
    {
        private static BalanceConfig Cfg() => new BalanceConfig();

        private static DayProcessor MakeProcessor(BalanceConfig cfg, int tier = 1, int order = 1)
        {
            var tension = new TensionState(cfg.Tension);
            return new DayProcessor(tension, cfg, DayProcessor.DefaultSteps())
            {
                Tier = tier,
                OrderLevel = order
            };
        }

        // ---- Гарантия US-1.3: ожидание и лечение не растят угрозу ----

        [Test]
        public void Tension_WaitThirtyDays_OnlyTierTickEntries()
        {
            var cfg = Cfg();
            var p = MakeProcessor(cfg);

            var reports = p.Advance(30);

            foreach (var report in reports)
                foreach (var change in report.TensionChanges)
                    Assert.AreEqual(TensionDriver.CityTierTick, change.Driver,
                        "За дни ожидания Напряжение может двигать только фоновый тик от тира");
        }

        [Test]
        public void Tension_TenIdleDays_AddExactlyTenTicks()
        {
            var cfg = Cfg();
            var p = MakeProcessor(cfg); // тир 1 = 1.0/день, Присмотр = ×1.0

            p.Advance(10);

            Assert.AreEqual(10, p.Tension.Value,
                "Десять дней лечения дают ровно десять очков — не больше");
            Assert.AreEqual(TensionBand.Calm, p.Tension.Band,
                "И полоса при этом не должна смениться");
        }

        // ---- Гарантия: кризис — это выборы игрока, а не течение времени ----

        [Test]
        public void Tension_PassiveCampaign_NeverReachesFracture()
        {
            foreach (int tier in new[] { 1, 2 })
            {
                var cfg = Cfg();
                var p = MakeProcessor(cfg, tier);

                p.Advance(200); // кампания целиком, без единого действия игрока

                Assert.LessOrEqual((int)p.Tension.Band, (int)TensionBand.Ferment,
                    $"Пассивный дрейф на тире {tier} не должен доводить город до кризиса");
            }
        }

        // ---- Гарантия Поправки №3.5: список драйверов закрыт ----

        [Test]
        public void Tension_Apply_RejectsDriverOutsideWhitelist()
        {
            var cfg = Cfg();
            var state = new TensionState(cfg.Tension);

            // CouncilRaid умеет только понижать — попытка поднять им должна быть отклонена.
            var change = state.Apply(TensionDriver.CouncilRaid, +50, "test");

            Assert.IsTrue(change.Rejected, "Драйвер вне белого списка обязан быть отклонён");
            Assert.AreEqual(0, change.Applied);
            Assert.AreEqual(0, state.Value);
        }

        [Test]
        public void Tension_Apply_RejectsUnknownDriver()
        {
            var cfg = Cfg();
            var state = new TensionState(cfg.Tension);

            var change = state.Apply(TensionDriver.None, +50, "test");

            Assert.IsTrue(change.Rejected);
            Assert.AreEqual(0, state.Value);
        }

        [Test]
        public void EveryTensionDriver_HasConfigEntry()
        {
            var cfg = Cfg().Tension;

            foreach (TensionDriver driver in System.Enum.GetValues(typeof(TensionDriver)))
            {
                if (driver == TensionDriver.None) continue;

                bool raises = System.Array.IndexOf(cfg.AllowedRaising, driver) >= 0;
                bool lowers = System.Array.IndexOf(cfg.AllowedLowering, driver) >= 0;

                Assert.IsTrue(raises || lowers,
                    $"Драйвер {driver} не читается ни одним списком — это стат без потребителя");
            }
        }

        // ---- Поведение шкалы ----

        [Test]
        public void Tension_FractionalTick_AccumulatesWithoutRoundingLoss()
        {
            var cfg = Cfg();
            // Вольница даёт ×1.25: за 20 дней ровно 25, без потерь на округлении
            // каждого дня (множитель выбран точно представимым в double).
            var p = MakeProcessor(cfg, tier: 1, order: 0);

            p.Advance(20);

            Assert.AreEqual(25, p.Tension.Value,
                "Дробный тик обязан копиться, а не округляться каждый день");
        }

        [Test]
        public void Tension_QuestChoice_MovesBandAndFiresEvent()
        {
            var cfg = Cfg();
            var state = new TensionState(cfg.Tension);

            TensionBand? from = null, to = null;
            state.BandChanged += (a, b) => { from = a; to = b; };

            // 5 крупных выборов = 200 очков = ровно порог «Ропота»
            for (int i = 0; i < 5; i++)
                TensionDrivers.QuestChoice(state, TensionDrivers.ChoiceWeight.Major, "q" + i, cfg);

            Assert.AreEqual(TensionBand.Murmur, state.Band);
            Assert.AreEqual(TensionBand.Calm, from);
            Assert.AreEqual(TensionBand.Murmur, to);
        }

        [Test]
        public void Tension_EventOutcome_CanLowerBandBack()
        {
            var cfg = Cfg();
            var state = new TensionState(cfg.Tension);

            for (int i = 0; i < 5; i++)
                TensionDrivers.QuestChoice(state, TensionDrivers.ChoiceWeight.Major, "q" + i, cfg);
            Assert.AreEqual(TensionBand.Murmur, state.Band);

            TensionDrivers.EventOutcome(state, TensionDrivers.ChoiceWeight.Major, "relief", cfg);

            Assert.AreEqual(TensionBand.Calm, state.Band, "Хороший исход обязан уметь разряжать");
        }

        [Test]
        public void Tension_ClampsAtZeroAndMax()
        {
            var cfg = Cfg();
            var state = new TensionState(cfg.Tension);

            TensionDrivers.EventOutcome(state, TensionDrivers.ChoiceWeight.Monstrous, "x", cfg);
            Assert.AreEqual(0, state.Value, "Ниже нуля шкала не уходит");

            for (int i = 0; i < 100; i++)
                TensionDrivers.QuestChoice(state, TensionDrivers.ChoiceWeight.Monstrous, "q", cfg);
            Assert.AreEqual(cfg.Tension.Max, state.Value, "Выше потолка тоже");
        }

        [Test]
        public void Tension_DaysInCurrentBand_ResetsOnTransition()
        {
            var cfg = Cfg();
            var p = MakeProcessor(cfg, tier: 4); // 7/день — быстро дойдём до перехода

            p.Advance(20); // 140 очков — ещё «Спокойно»
            Assert.AreEqual(TensionBand.Calm, p.Tension.Band);
            int before = p.Tension.DaysInCurrentBand;
            Assert.Greater(before, 0);

            p.Advance(20); // 280 — переход в «Ропот»
            Assert.AreEqual(TensionBand.Murmur, p.Tension.Band);
            Assert.Less(p.Tension.DaysInCurrentBand, before + 20,
                "Счётчик дней в полосе обязан обнуляться при переходе");
        }
    }
}
