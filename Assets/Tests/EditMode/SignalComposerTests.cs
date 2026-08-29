using System.Collections.Generic;
using System.Linq;
using Game.Core.Balance;
using Game.Core.Loop;
using Game.Core.Pressure;
using Game.Core.Signals;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Слой сигналов — единственный канал знания игрока о скрытых шкалах.
    /// Эти тесты защищают три правила: нет немого перехода, бюджет внимания,
    /// обязательный слот под «что изменилось».
    /// </summary>
    public class SignalComposerTests
    {
        private static BalanceConfig Cfg() => new BalanceConfig();

        [Test]
        public void Signals_BandChange_AlwaysEmitsNotableOrLouder()
        {
            var cfg = Cfg();

            // Проходим по всем переходам вверх и проверяем, что каждый заметен.
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

        [Test]
        public void Signals_NeverExceedBudget()
        {
            var cfg = Cfg();
            cfg.Signals.MaxSignalsPerDay = 2;

            var state = new TensionState(cfg.Tension);
            state.BeginDay();
            // Несколько переходов за один день — кандидатов больше бюджета.
            state.Apply(TensionDriver.QuestChoice, 250, "a");
            state.Apply(TensionDriver.QuestChoice, 250, "b");
            state.Apply(TensionDriver.QuestChoice, 250, "c");

            var digest = SignalComposer.Compose(state.Band, state.DayLedger, 1, cfg.Signals);

            Assert.LessOrEqual(digest.Requests.Count, 2, "Бюджет внимания превышать нельзя — иначе шум");
        }

        [Test]
        public void Signals_AlwaysIncludeDeltaWhenSomethingChanged()
        {
            var cfg = Cfg();
            cfg.Signals.MaxSignalsPerDay = 1; // бюджет ровно на один сигнал

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

            // Богатый и напряжённый против бедного и спокойного.
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
    }
}
