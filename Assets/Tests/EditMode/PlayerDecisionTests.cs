using System;
using System.Linq;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Checks;
using Game.Core.Loop;
using Game.Core.Pressure;
using Game.Core.Settlement;
using Game.Core.Stats;
using Game.Core.World;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Ход игрока внутри суток.
    ///
    /// В конвейере было двенадцать шагов вычисления и ноль шагов ввода: событие
    /// выбиралось, проверка резолвилась и жертва назначалась внутри одного тика,
    /// а игрок узнавал обо всём из протокола. Здесь конвейер впервые
    /// ОСТАНАВЛИВАЕТСЯ и спрашивает.
    ///
    /// Заодно это единственное место, где оживает кровавый путь: он был выписан
    /// в семи инцидентах и не читался ни одной строкой кода, потому что выбирать
    /// было негде.
    /// </summary>
    public class PlayerDecisionTests
    {
        private static readonly string[] Positions =
        {
            "storehouse_dock", "settlement_market", "settlement_farms",
            "infirmary_bed", "council_seat", "scouting_post", "workshop_bench"
        };

        private static Companion Make(string id, int skill, string position)
        {
            var arch = new CompanionArchetype(id, id);
            foreach (var sk in new[] { SkillType.Survival, SkillType.Trade, SkillType.Persuade, SkillType.Medicine })
                arch.SetSkill(sk, skill);
            var c = arch.CreateInstance(id);
            c.AssignedSlotId = position;
            return c;
        }

        private static DayProcessor Build(BalanceConfig cfg, bool askPlayer)
        {
            var roster = new Roster();
            for (int i = 0; i < Positions.Length; i++)
                roster.Add(Make("actor" + i, 7, Positions[i]));

            var adapter = new RosterAdapter(roster);
            var pulse = new WorldPulse(cfg.Pulse);
            foreach (var s in DefaultPressureSources.All()) pulse.AddSource(s);

            return new DayProcessor(new TensionState(cfg.Tension, 300), cfg, DayProcessor.DefaultSteps())
            {
                Tier = 2,
                Roster = adapter,
                Casualties = adapter,
                Population = new PopulationState(),
                Pulse = pulse,
                Incidents = DefaultIncidents.BuildTable(),
                Repeats = new RepeatTracker(),
                RequirePlayerDecision = askPlayer
            };
        }

        /// <summary>Крутит сутки, пока конвейер не остановится и не спросит.</summary>
        private static DayReport RunUntilAsked(DayProcessor p, int maxDays = 60)
        {
            for (int day = 1; day <= maxDays; day++)
                foreach (var phase in new[] { DayPhase.Day, DayPhase.Night })
                {
                    var report = p.Advance(phase);
                    if (report.AwaitsDecision) return report;
                }
            return null;
        }

        [Test]
        public void Decision_WhenEnabled_DayStopsAndAsks()
        {
            var p = Build(new BalanceConfig(), askPlayer: true);

            var report = RunUntilAsked(p);

            Assert.IsNotNull(report, "За шестьдесят суток конвейер ни разу не спросил игрока");
            Assert.IsTrue(p.AwaitsDecision, "Процессор обязан помнить, что сутки не закончены");
            Assert.IsNotNull(report.Pending, "Отчёт обязан нести само предложение");
            Assert.IsNull(report.Signals,
                "Сигналы описывают финальное состояние дня, а день ещё не случился");
        }

        [Test]
        public void Decision_Offer_ShowsThresholdBeforeConfirming()
        {
            var p = Build(new BalanceConfig(), askPlayer: true);
            var report = RunUntilAsked(p);
            Assert.IsNotNull(report);

            Assert.IsNotEmpty(report.Pending.Options, "Предложение без вариантов — это не выбор");

            foreach (var option in report.Pending.Options)
            {
                Assert.Greater(option.Threshold, 0,
                    "Порог обязан быть показан ДО подтверждения (US-2.6): провал должен быть следствием подготовки");
                Assert.IsFalse(option.Skill.IsNone, "У варианта обязан быть named навык");
            }
        }

        [Test]
        public void Decision_BloodyPath_IsFinallyReachable()
        {
            var cfg = new BalanceConfig();
            var p = Build(cfg, askPlayer: true);

            // Ищем событие, у которого выписаны оба пути.
            DayReport report = null;
            for (int i = 0; i < 40 && report == null; i++)
            {
                var candidate = RunUntilAsked(p, 60);
                if (candidate == null) break;
                if (candidate.Pending.Options.Count > 1) report = candidate;
                else p.ResolvePending(IncidentPath.Quiet);
            }

            Assert.IsNotNull(report,
                "Ни одно предложение не дало выбора пути — кровавый путь так и остался мёртвыми данными");

            var quiet = report.Pending.Options.First(o => o.Path == IncidentPath.Quiet);
            var bloody = report.Pending.Options.First(o => o.Path == IncidentPath.Bloody);

            Assert.AreNotEqual(quiet.Skill, bloody.Skill,
                "Пути обязаны требовать разного: иначе выбор косметический");

            var done = p.ResolvePending(IncidentPath.Bloody);

            Assert.IsFalse(done.AwaitsDecision, "После хода сутки обязаны закончиться");
            Assert.IsTrue(done.Incidents.Any(o => o.IncidentId == report.Pending.IncidentId),
                "Разобранное событие обязано попасть в отчёт");
        }

        [Test]
        public void Decision_ResolvePending_CompletesTheDay()
        {
            var p = Build(new BalanceConfig(), askPlayer: true);
            var asked = RunUntilAsked(p);
            Assert.IsNotNull(asked);

            var done = p.ResolvePending(IncidentPath.Quiet);

            Assert.IsFalse(p.AwaitsDecision, "Конвейер обязан отпустить сутки");
            Assert.IsNotNull(done.Signals, "Теперь сигналы собраны — по финальному состоянию дня");
            Assert.AreEqual(asked.Day, done.Day, "Это те же сутки, а не следующие");
        }

        [Test]
        public void Decision_AdvanceWhileAwaiting_Throws()
        {
            var p = Build(new BalanceConfig(), askPlayer: true);
            Assert.IsNotNull(RunUntilAsked(p));

            Assert.Throws<InvalidOperationException>(() => p.Advance(DayPhase.Day),
                "Промотать сутки в обход собственного решения нельзя");
        }

        [Test]
        public void Decision_ResolveWithoutOffer_Throws()
        {
            var p = Build(new BalanceConfig(), askPlayer: true);

            Assert.Throws<InvalidOperationException>(() => p.ResolvePending(IncidentPath.Quiet),
                "Решать нечего, пока конвейер не остановлен");
        }

        /// <summary>
        /// Аудит П10: когда в одной фазе срабатывает больше одного инцидента,
        /// КАЖДЫЙ становится своим решением по очереди — AwaitsDecision не
        /// отпускает сутки, пока очередь фазы не опустеет. Раньше второе
        /// срабатывание фазы тихо резолвилось само, и выбор без последствия
        /// был именно тем дефектом, который правит эта поправка.
        ///
        /// Два авторских источника, оба выстрелившие ровно на сутки 1 (тот же
        /// приём, что и у OpeningContent.ScriptedSource) — детерминированно, а
        /// не «прогони подольше и надейся на совпадение накопителей».
        /// </summary>
        [Test]
        public void Decision_TwoIncidentsInOnePhase_BothBecomeSeparateDecisions()
        {
            var cfg = new BalanceConfig();
            cfg.Pulse.MaxFiresPerDay = 2; // обе заявки суток 1 обязаны поместиться в один день

            var table = new IncidentTable();
            table.Add(new IncidentDefinition
            {
                Id = "test_a", TopicId = "test.a", SourceId = "test.a", DomainTag = "тест",
                MinBand = TensionBand.Calm, MaxBand = TensionBand.Fracture, MinTier = 1, Weight = 10,
                QuietPathSkill = SkillKeys.Persuade, QuietPathThreshold = 5,
                RelevantPositionId = Positions[0]
            });
            table.Add(new IncidentDefinition
            {
                Id = "test_b", TopicId = "test.b", SourceId = "test.b", DomainTag = "тест",
                MinBand = TensionBand.Calm, MaxBand = TensionBand.Fracture, MinTier = 1, Weight = 10,
                QuietPathSkill = SkillKeys.Survival, QuietPathThreshold = 5,
                RelevantPositionId = Positions[1]
            });

            var roster = new Roster();
            for (int i = 0; i < Positions.Length; i++)
                roster.Add(Make("actor" + i, 7, Positions[i]));
            var adapter = new RosterAdapter(roster);

            var pulse = new WorldPulse(cfg.Pulse);
            pulse.AddSource(new OpeningContent.ScriptedSource("test.a", "тест", 1));
            pulse.AddSource(new OpeningContent.ScriptedSource("test.b", "тест", 1));

            var p = new DayProcessor(new TensionState(cfg.Tension), cfg, DayProcessor.DefaultSteps())
            {
                Tier = 1,
                Roster = adapter,
                Casualties = adapter,
                Population = new PopulationState(),
                Pulse = pulse,
                Incidents = table,
                Repeats = new RepeatTracker(),
                RequirePlayerDecision = true
            };

            var report = p.Advance(DayPhase.Day);
            Assert.IsTrue(report.AwaitsDecision, "Первое из двух сработавших событий обязано остановить сутки");

            report = p.ResolvePending(IncidentPath.Quiet);
            Assert.IsTrue(report.AwaitsDecision,
                "Второй инцидент фазы обязан стать ОТДЕЛЬНЫМ решением — очередь фазы не должна " +
                "опустеть после первого (П10)");
            Assert.AreEqual(1, report.Day, "Второе решение — те же сутки, а не следующие");
            Assert.AreEqual(DayPhase.Day, report.Phase, "Второе решение — та же фаза");

            report = p.ResolvePending(IncidentPath.Quiet);
            Assert.IsFalse(report.AwaitsDecision, "После второго решения очередь фазы обязана быть исчерпана");
            Assert.AreEqual(2, report.Incidents.Count(o => o.TopicId == "test.a" || o.TopicId == "test.b"),
                "Оба инцидента фазы обязаны попасть в итоговый отчёт");
        }

        [Test]
        public void Decision_Disabled_KeepsTheOldContract()
        {
            var p = Build(new BalanceConfig(), askPlayer: false);

            for (int day = 1; day <= 30; day++)
                foreach (var phase in new[] { DayPhase.Day, DayPhase.Night })
                {
                    var report = p.Advance(phase);
                    Assert.IsFalse(report.AwaitsDecision,
                        "С выключенным режимом сутки обязаны заканчиваться за один вызов");
                    Assert.IsNotNull(report.Signals, "И нести сигналы");
                }
        }
    }
}
