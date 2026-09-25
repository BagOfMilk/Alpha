using System.Collections.Generic;
using System.Linq;
using Game.Core.Balance;
using Game.Core.Checks;
using Game.Core.Loop;
using Game.Core.Pressure;
using Game.Core.Signals;
using Game.Core.World;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Шар сигналів — єдиний канал знання гравця про приховані шкали.
    /// Ці тести захищають три правила: немає німого переходу, бюджет уваги,
    /// обов'язковий слот під «що змінилося».
    /// </summary>
    public class SignalComposerTests
    {
        private static BalanceConfig Cfg() => new BalanceConfig();

        [Test]
        public void Signals_BandChange_AlwaysEmitsNotableOrLouder()
        {
            var cfg = Cfg();

            // Проходимо по всіх переходах вгору і перевіряємо, що кожен помітний.
            foreach (var target in new[] { TensionBand.Murmur, TensionBand.Ferment, TensionBand.Heat, TensionBand.Fracture })
            {
                var state = new TensionState(cfg.Tension);
                state.BeginDay();
                int need = cfg.Tension.BandThresholds[(int)target - 1];
                state.Apply(TensionDriver.QuestChoice, need, "jump");

                var digest = SignalComposer.Compose(state.Band, state.DayLedger, 1, cfg.Signals);

                Assert.IsTrue(digest.Requests.Any(r => r.IsDelta && r.Urgency >= SignalUrgency.Notable),
                    $"Переход в «{target}» обязан породить заметный сигнал — немых переходов не бывает");
            }
        }

        [Test]
        public void Signals_FractureIsLoudest()
        {
            Assert.AreEqual(SignalUrgency.Imminent, SignalComposer.UrgencyForBand(TensionBand.Fracture),
                "«Излом» нельзя сообщить шёпотом, иначе кризис покажется несправедливым");
            Assert.AreEqual(SignalUrgency.Alarming, SignalComposer.UrgencyForBand(TensionBand.Heat));
        }

        /// <summary>
        /// Бюджет уваги обмежує ЗВИЧАЙНІ сигнали (те, що не мандатне) —
        /// зміна полоси і ступінь передвісника в цей бюджет не входять
        /// (див. наступний тест). Раніше це був єдиний тест на бюджет, і він
        /// будував саме той густий день, у якому зміна полоси могла лишитися
        /// німою (G21) — тільки через кандидатів з dayLedger, які ТЕПЕР
        /// мандатні. Перевіряємо те саме перевантаження через city-events: вони дельти, але
        /// не мандатні, і бюджет зобов'язаний різати їх як і раніше.
        /// </summary>
        [Test]
        public void Signals_NeverExceedBudget()
        {
            var cfg = Cfg();
            cfg.Signals.MaxSignalsPerDay = 2;

            var state = new TensionState(cfg.Tension);
            state.BeginDay();

            var events = new List<CityEvent>
            {
                new CityEvent("city.a", SignalUrgency.Notable, "a"),
                new CityEvent("city.b", SignalUrgency.Notable, "b"),
                new CityEvent("city.c", SignalUrgency.Notable, "c"),
            };

            var digest = SignalComposer.Compose(state.Band, state.DayLedger, 1, cfg.Signals,
                null, null, null, false, null, 0, events);

            Assert.LessOrEqual(digest.Requests.Count, 2,
                "Бюджет внимания превышать нельзя — иначе шум (для немандатных сигналов)");
        }

        /// <summary>
        /// РЕГРЕСІЯ G21 (закрито 24.09.2026). Раніше саме цей сценарій —
        /// кілька переходів полоси за один густий день — доводив дірку
        /// інваріанта 4: <c>ForceSignalOnBandChange</c> тільки ДОДАВАВ
        /// кандидата, слот не резервував, і бюджет/конкурентна дельта могли
        /// витіснити будь-який з переходів мовчки (див. також
        /// <c>CampaignPacingTests.Pacing_BandChangeIsNeverMute</c>, тепер
        /// знятий). Мутаційна перевірка: якщо прибрати резервування
        /// (<c>Mandatory</c> у зміни полоси в <see cref="SignalComposer"/> або
        /// його форсований відбір у <c>Select</c>), цей тест зобов'язаний впасти —
        /// три переходи при бюджеті 2 інакше не пройдуть.
        /// </summary>
        [Test]
        public void Signals_MandatoryBandChanges_AlwaysGetThrough_EvenOverBudget()
        {
            var cfg = Cfg();
            cfg.Signals.MaxSignalsPerDay = 2;

            var state = new TensionState(cfg.Tension);
            state.BeginDay();
            // Три переходи за один день — Спокій -> Ропіт -> Бродіння -> Розпал.
            state.Apply(TensionDriver.QuestChoice, 250, "a");
            state.Apply(TensionDriver.QuestChoice, 250, "b");
            state.Apply(TensionDriver.QuestChoice, 250, "c");

            var digest = SignalComposer.Compose(state.Band, state.DayLedger, 1, cfg.Signals);

            Assert.Greater(digest.Requests.Count, 2,
                "При трёх переходах мандатные сигналы обязаны превысить бюджет 2 — иначе один из них немой");
            foreach (var target in new[] { TensionBand.Murmur, TensionBand.Ferment, TensionBand.Heat })
                Assert.IsTrue(digest.Requests.Any(r => r.TopicId == "tension.band." + target),
                    $"Переход в «{target}» пропал из густого дня — инвариант 4 нарушен молча");
        }

        /// <summary>
        /// РЕГРЕСІЯ G21: та сама дірка, але для драбини передвісників. Ступінь
        /// вже зарахована почутою накопичувачем (WorldPulse.MarkDelivered,
        /// PulseStep) ДО того, як цей крок вирішує бюджет — якби вона не
        /// пройшла в дайджест, гравець цю ступінь не почув би ніколи, а
        /// драбина ззовні виглядала б такою, що перестрибнула її. Конкуренти —
        /// кілька кризових інцидентів (Imminent, найгучніша терміновість) і
        /// бюджет рівно на них.
        /// </summary>
        [Test]
        public void Signals_DenseDay_ForewarningStepIsNeverMute()
        {
            var cfg = Cfg();
            cfg.Signals.MaxSignalsPerDay = 3;

            var forewarnings = new List<Forewarning> { new Forewarning("street", 1, "улицы") };
            var incidents = new List<IncidentOutcome>
            {
                new IncidentOutcome("i1", "incident.i1", "домен1", OutcomeBand.Worst, false, true, null, null, 0),
                new IncidentOutcome("i2", "incident.i2", "домен2", OutcomeBand.Worst, false, true, null, null, 0),
                new IncidentOutcome("i3", "incident.i3", "домен3", OutcomeBand.Worst, false, true, null, null, 0),
            };

            var digest = SignalComposer.Compose(TensionBand.Calm, null, 1, cfg.Signals,
                forewarnings, incidents, null, false);

            Assert.IsTrue(digest.Requests.Any(r => r.TopicId == "forewarn.level1"),
                "Ступень предвестника обязана прозвучать даже когда все слоты бюджета забрали более громкие кризисы");
        }

        [Test]
        public void Signals_AlwaysIncludeDeltaWhenSomethingChanged()
        {
            var cfg = Cfg();
            cfg.Signals.MaxSignalsPerDay = 1; // бюджет рівно на один сигнал

            var state = new TensionState(cfg.Tension);
            state.BeginDay();
            state.Apply(TensionDriver.QuestChoice, 250, "jump");

            var digest = SignalComposer.Compose(state.Band, state.DayLedger, 1, cfg.Signals);

            Assert.AreEqual(1, digest.Requests.Count);
            Assert.IsTrue(digest.Requests[0].IsDelta,
                "Если что-то изменилось, единственный слот обязан уйти под изменение");
        }

        [Test]
        public void Signals_QuietDay_StillSpeaks()
        {
            var cfg = Cfg();
            var state = new TensionState(cfg.Tension);
            state.BeginDay();

            var digest = SignalComposer.Compose(state.Band, state.DayLedger, 1, cfg.Signals);

            Assert.IsNotEmpty(digest.Requests, "Даже в спокойный день город подаёт голос");
            Assert.IsTrue(digest.Requests.All(r => !r.IsDelta));
        }

        [Test]
        public void EveryTensionBand_HasSignalTopic()
        {
            var cfg = Cfg();

            foreach (TensionBand band in System.Enum.GetValues(typeof(TensionBand)))
            {
                var digest = SignalComposer.Compose(band, new List<TensionChange>(), 1, cfg.Signals);
                Assert.IsTrue(digest.Requests.Any(r => r.TopicId == "tension.ambient." + band),
                    $"У полосы «{band}» нет своей реплики — игрок не сможет её прочитать");
            }
        }

        [Test]
        public void Moodboard_ProsperityAndDecayAreIndependent()
        {
            var cfg = Cfg();
            var empty = new List<TensionChange>();

            // Багатий і напружений проти бідного і спокійного.
            var richTense = SignalComposer.Compose(TensionBand.Heat, empty, 4, cfg.Signals).Moodboard;
            var poorCalm = SignalComposer.Compose(TensionBand.Calm, empty, 1, cfg.Signals).Moodboard;

            Assert.Greater(richTense.Prosperity, poorCalm.Prosperity);
            Assert.Greater(richTense.Decay, poorCalm.Decay);
            Assert.Contains("баррикады", richTense.Overlays,
                "На «Накале» город обязан выглядеть иначе");
            Assert.IsEmpty(poorCalm.Overlays);
        }

        [Test]
        public void Signals_SameInputs_ProduceIdenticalOutput()
        {
            var cfg = Cfg();

            string First() => Run(cfg);
            Assert.AreEqual(First(), First(), "Слой сигналов обязан быть детерминированным");
        }

        private static string Run(BalanceConfig cfg)
        {
            var tension = new TensionState(cfg.Tension);
            var p = new DayProcessor(tension, cfg, DayProcessor.DefaultSteps()) { Tier = 2, OrderLevel = 1 };

            var sb = new System.Text.StringBuilder();
            for (int day = 1; day <= 200; day++)
            {
                var report = p.Advance();
                if (day % 20 == 0)
                    TensionDrivers.QuestChoice(tension, TensionDrivers.ChoiceWeight.Major, "q" + day, cfg);

                foreach (var r in report.Signals.Requests)
                    sb.Append(day).Append(':').Append(r.TopicId).Append(';');
            }
            return sb.ToString();
        }
        // ---- Придушення повторів ----

        [Test]
        public void Composer_FreshTopic_PushesOutTheOneJustHeard()
        {
            var cfg = new SignalBalance { TopicCooldownDays = 2 };
            var memory = new SignalMemory();

            // Доба 1: місту є що сказати тільки фонову репліку.
            var first = SignalComposer.Compose(
                TensionBand.Calm, null, 1, cfg, null, null, null, false, memory, 1);
            string ambient = first.Requests[0].TopicId;
            Assert.IsTrue(ambient.StartsWith("tension.ambient."), "Ожидали фоновую реплику полосы");

            // Доба 2: з'явилося щось нове. Вчорашня фонова зобов'язана поступитися.
            var fore = new[] { new Forewarning("street", 1, "улицы") };
            var second = SignalComposer.Compose(
                TensionBand.Calm, null, 1, cfg, fore, null, null, false, memory, 2);

            var topics = second.Requests.Select(r => r.TopicId).ToList();

            Assert.IsFalse(topics.Contains(ambient),
                "Реплика, прозвучавшая вчера, не имеет права занимать слот, когда есть свежая");
            Assert.IsTrue(topics.Any(t => t.StartsWith("forewarn.")),
                "Свежий предвестник обязан прозвучать");
        }

        [Test]
        public void Composer_QuietDay_IsNeverLeftMute()
        {
            // Усвідомлене рішення: коли сказати більше нічого, повтор КРАЩИЙ за тишу.
            // Німий день гравець читає як «гра зламалася», а не як «усе спокійно».
            var cfg = new SignalBalance { TopicCooldownDays = 30 };
            var memory = new SignalMemory();

            for (int day = 1; day <= 10; day++)
            {
                var digest = SignalComposer.Compose(
                    TensionBand.Calm, null, 1, cfg, null, null, null, false, memory, day);

                Assert.IsNotEmpty(digest.Requests,
                    $"Сутки {day}: подавление повторов не имеет права оставить день совсем немым");
            }
        }

    }
}
