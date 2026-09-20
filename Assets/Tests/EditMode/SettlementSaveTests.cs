using System;
using System.Linq;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Loop;
using Game.Core.Pressure;
using Game.Core.Settlement;
using Game.Core.Sim;
using Game.Core.Stats;
using Game.Core.World;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Контракт сохранения городского слоя.
    ///
    /// До этого детерминизм был действителен только в пределах одного процесса:
    /// заряды накопителей, значение Напряжения и услышанные ступени нигде не
    /// хранились, поэтому после загрузки мир начинался с нулей, а игрок получал
    /// кризис без предупреждения. Проверяется ровно одно, зато главное:
    /// продолжение из слепка неотличимо от непрерывного прогона.
    /// </summary>
    public class SettlementSaveTests
    {
        // Тир 1 при Укладе 0 даёт фоновый тик 1.25 за сутки — НЕ целое число.
        // Это намеренно: с целым тиком дробный остаток Напряжения всегда ноль,
        // и round-trip прошёл бы, даже если остаток вообще не сохранять.
        private static DayProcessor Build(BalanceConfig cfg, int tier = 1)
        {
            var roster = new Roster();
            roster.Add(Make("guard", SkillType.Trade, 8, "storehouse_dock"));
            roster.Add(Make("trader", SkillType.Trade, 7, "settlement_market"));
            roster.Add(Make("medic", SkillType.Medicine, 7, "infirmary_bed"));

            var adapter = new RosterAdapter(roster);
            var pulse = new WorldPulse(cfg.Pulse);
            foreach (var s in DefaultPressureSources.All()) pulse.AddSource(s);

            return new DayProcessor(new TensionState(cfg.Tension), cfg, DayProcessor.DefaultSteps())
            {
                Tier = tier,
                OrderLevel = 0,
                Roster = adapter,
                Casualties = adapter,
                Population = new PopulationState(),
                Pulse = pulse,
                Incidents = DefaultIncidents.BuildTable(),
                Repeats = new RepeatTracker()
            };
        }

        private static Companion Make(string id, SkillType skill, int value, string position)
        {
            var arch = new CompanionArchetype(id, id);
            arch.SetSkill(skill, value);
            var c = arch.CreateInstance(id);
            c.AssignedSlotId = position;
            return c;
        }

        private static string Fingerprint(DayRow r)
        {
            return string.Join("/", new[]
            {
                r.Day.ToString(), r.Phase.ToString(), r.Band.ToString(),
                r.TensionValue.ToString(), r.Population.ToString(),
                r.Incidents, r.Forewarnings, r.Topics
            });
        }

        [Test]
        public void Save_ResumedCampaign_IsIndistinguishableFromContinuous()
        {
            var cfg = new BalanceConfig();

            // A: сто суток подряд.
            var continuous = CampaignSimulator.Run(Build(cfg), SimPolicy.Passive, 100, cfg);

            // B: пятьдесят суток, слепок.
            var first = Build(cfg);
            CampaignSimulator.Run(first, SimPolicy.Passive, 50, cfg);
            string blob = first.SaveState();

            // C: СВЕЖИЙ конвейер, восстановление, ещё пятьдесят суток.
            var resumed = Build(cfg);
            resumed.RestoreState(blob);
            var tail = CampaignSimulator.Run(resumed, SimPolicy.Passive, 50, cfg);

            Assert.AreEqual(100, tail.Rows.Count, "Пятьдесят суток — это сто фаз");

            for (int i = 0; i < tail.Rows.Count; i++)
            {
                string expected = Fingerprint(continuous.Rows[100 + i]);
                string actual = Fingerprint(tail.Rows[i]);

                Assert.AreEqual(expected, actual,
                    $"Фаза {i} после загрузки разошлась с непрерывным прогоном. " +
                    "Детерминизм, действительный только внутри одного запуска, — это не детерминизм");
            }
        }

        [Test]
        public void Save_CarriesHiddenState_NotJustTheCalendar()
        {
            var cfg = new BalanceConfig();
            var p = Build(cfg);
            CampaignSimulator.Run(p, SimPolicy.Passive, 30, cfg);

            var restored = Build(cfg);
            restored.RestoreState(p.SaveState());

            Assert.AreEqual(p.CurrentDay, restored.CurrentDay, "Календарь");
            Assert.AreEqual(p.Tension.Value, restored.Tension.Value, "Значение Напряжения");
            Assert.AreEqual(p.Tension.Band, restored.Tension.Band, "Полоса");
            Assert.AreEqual(p.Tension.FractionForSave, restored.Tension.FractionForSave, 1e-9,
                "Дробный остаток: без него каждая загрузка незаметно округляет Напряжение вниз");

            foreach (var pair in p.Pulse.Tracks)
            {
                var before = pair.Value;
                var after = restored.Pulse.Tracks[pair.Key];

                Assert.AreEqual(before.Charge, after.Charge, "Заряд накопителя " + pair.Key);
                Assert.AreEqual(before.DeliveredLevel, after.DeliveredLevel,
                    "Услышанная ступень " + pair.Key + ": иначе кризис ударит по тому, кто ничего не слышал");
                Assert.AreEqual(before.LastFiredDay, after.LastFiredDay, "Кулдаун " + pair.Key);
            }
        }

        [Test]
        public void Save_RepeatPenalty_SurvivesReload()
        {
            var cfg = new BalanceConfig();
            var p = Build(cfg);
            CampaignSimulator.Run(p, SimPolicy.Passive, 40, cfg);

            var restored = Build(cfg);
            restored.RestoreState(p.SaveState());

            // Штраф за повторы обязан пережить загрузку, иначе сейв становится
            // способом снять наказание, ничего не отдав взамен.
            var before = (RepeatTracker)p.Repeats;
            var after = (RepeatTracker)restored.Repeats;

            Assert.AreEqual(before.CaptureState(), after.CaptureState(),
                "Окно повторов сброшено загрузкой — это бесплатная отмена штрафа");
            Assert.IsNotEmpty(before.CaptureState(),
                "За сорок суток хоть один топик обязан был зарегистрироваться, иначе тест ничего не проверил");
        }

        [Test]
        public void Save_UnknownVersion_FailsLoudly()
        {
            var p = Build(new BalanceConfig());

            Assert.Throws<InvalidOperationException>(() => p.RestoreState("alpha0;day=5"),
                "Слепок чужой версии обязан падать, а не грузиться наполовину");
        }

        [Test]
        public void Save_Blob_DoesNotExposeTypedNumbersToGameplay()
        {
            var p = Build(new BalanceConfig());
            CampaignSimulator.Run(p, SimPolicy.Passive, 10, new BalanceConfig());

            object blob = p.SaveState();

            // Наружу уходит строка, а не DTO с полями. Это и есть компромисс,
            // описанный в SettlementSave: сохранить состояние можно, а собрать
            // из него дашборд по дороге — нет.
            Assert.IsInstanceOf<string>(blob, "Слепок обязан быть непрозрачным для Game.Gameplay");
        }
    }
}
