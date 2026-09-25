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
    /// Хід гравця всередині доби.
    ///
    /// У конвеєрі було дванадцять кроків обчислення і нуль кроків уводу: подія
    /// вибиралася, перевірка резолвилася і жертва призначалася всередині одного тіку,
    /// а гравець дізнавався про все з протоколу. Тут конвеєр вперше
    /// ЗУПИНЯЄТЬСЯ і питає.
    ///
    /// Заразом це єдине місце, де оживає кривавий шлях: він був виписаний
    /// у семи інцидентах і не читався жодним рядком коду, бо вибирати
    /// було нема де.
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

        /// <summary>Крутить доби, поки конвеєр не зупиниться і не спитає.</summary>
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

            // Шукаємо подію, у якої виписані обидва шляхи.
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
        /// Аудит П10: коли в одній фазі спрацьовує більше одного інциденту,
        /// КОЖЕН стає своїм рішенням по черзі — AwaitsDecision не
        /// відпускає добу, поки черга фази не спорожніє. Раніше друге
        /// спрацювання фази тихо резолвилося саме, і вибір без наслідку
        /// був саме тим дефектом, який виправляє ця поправка.
        ///
        /// Два авторських джерела, обидва вистрелили рівно на добу 1 (той самий
        /// прийом, що й у OpeningContent.ScriptedSource) — детерміновано, а
        /// не «прожени довше і сподівайся на збіг накопичувачів».
        /// </summary>
        [Test]
        public void Decision_TwoIncidentsInOnePhase_BothBecomeSeparateDecisions()
        {
            var cfg = new BalanceConfig();
            cfg.Pulse.MaxFiresPerDay = 2; // обидві заявки доби 1 зобов'язані вміститися в один день

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

        /// <summary>
        /// Рев'ю А1 (major): оффери фази раніше будувалися ОДНИМ проходом, весь
        /// одразу, до першого ходу гравця (IncidentStep.Execute). Якщо перший
        /// інцидент фази розбирався кривавим шляхом — страх громади озброюється на
        /// ПОТОЧНУ добу, — а другий інцидент фази йшов тихим СОЦІАЛЬНИМ підходом
        /// (Persuade/Trade), його показаний поріг рахувався за станом Fear ДО
        /// розв'язання першого, а застосовувався — ПІСЛЯ. Показане і застосоване
        /// розходилися: пряме порушення інваріанту 8 («поріг, який бачить
        /// гравець, зобов'язаний бути тим самим, що застосується»). Відтворювано в
        /// звичайній грі — MaxFiresPerNight/Day=2 за замовчуванням.
        ///
        /// Лагодиться тим, що PendingDecision будується ЛІНИВО, в момент вилучення
        /// наступного елемента черги (DayProcessor.TryDequeueNextPending), а
        /// не заздалегідь в IncidentStep.Execute — див. IncidentStep.BuildOffer.
        /// </summary>
        [Test]
        public void Decision_SecondQueuedIncident_ShownThresholdMatchesWhatResolves_AfterFirstArmsFear()
        {
            var cfg = new BalanceConfig();
            cfg.Pulse.MaxFiresPerDay = 2;

            var bloody = new IncidentDefinition
            {
                Id = "test_bloody", TopicId = "test.bloody", SourceId = "test.bloody", DomainTag = "тест",
                MinBand = TensionBand.Calm, MaxBand = TensionBand.Fracture, MinTier = 1, Weight = 10,
                QuietPathSkill = SkillKeys.Survival, QuietPathThreshold = 5,
                BloodyPathSkill = SkillKeys.Tactics, BloodyPathThreshold = 4,
                RelevantPositionId = Positions[0]
            };
            // Тихий шлях — СОЦІАЛЬНИЙ підхід: саме такі пороги ростуть від
            // страху громади (IncidentResolver.IsSocial: Persuade/Trade).
            var social = new IncidentDefinition
            {
                Id = "test_social", TopicId = "test.social", SourceId = "test.social", DomainTag = "тест",
                MinBand = TensionBand.Calm, MaxBand = TensionBand.Fracture, MinTier = 1, Weight = 10,
                QuietPathSkill = SkillKeys.Persuade, QuietPathThreshold = 5,
                QuietPathApproach = ApproachForm.Persuade,
                RelevantPositionId = Positions[1]
            };

            var table = new IncidentTable();
            table.Add(bloody);
            table.Add(social);

            var roster = new Roster();
            for (int i = 0; i < Positions.Length; i++)
                roster.Add(Make("actor" + i, 7, Positions[i]));
            var adapter = new RosterAdapter(roster);

            var pulse = new WorldPulse(cfg.Pulse);
            // Порядок відбору при рівному заповненні вирішує Id за зростанням
            // (WorldPulse.Advance) — "test.bloody" < "test.social", кривавий
            // інцидент гарантовано стає в чергу першим.
            pulse.AddSource(new OpeningContent.ScriptedSource("test.bloody", "тест", 1));
            pulse.AddSource(new OpeningContent.ScriptedSource("test.social", "тест", 1));

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

            var report1 = p.Advance(DayPhase.Day);
            Assert.IsTrue(report1.AwaitsDecision);
            Assert.AreEqual("test_bloody", report1.Pending.IncidentId,
                "Кровавый инцидент обязан встать в очередь первым (см. комментарий выше про порядок отбора)");

            var report2 = p.ResolvePending(IncidentPath.Bloody);
            Assert.IsTrue(report2.AwaitsDecision, "Второй инцидент фазы обязан стать отдельным решением (П10)");
            Assert.AreEqual("test_social", report2.Pending.IncidentId);

            Assert.IsTrue(p.Fear.IsAfraid(report2.Day),
                "Кровавый путь первого инцидента обязан заармить страх общины на текущие сутки — " +
                "иначе сценарий не воспроизводит регрессию из ревью");

            var shown = report2.Pending.Options.First(o => o.Path == IncidentPath.Quiet);

            // Перераховуємо НЕЗАЛЕЖНО, тим самим шляхом, яким резолвер застосує
            // поріг при фактичному розборі, — прямо зараз, поки Fear в тому самому
            // стані, що побачить ResolvePending(Quiet) наступним викликом.
            var request = IncidentResolver.BuildRequest(social, IncidentPath.Quiet, p.Fear, report2.Day, cfg);
            var preview = CheckResolver.Preview(request, p.Roster, p.Repeats, report2.Day, cfg);

            Assert.AreEqual(preview.EffectiveThreshold, shown.Threshold,
                "Инвариант 8: порог, показанный игроку, обязан совпадать с тем, что реально применится");

            var done = p.ResolvePending(IncidentPath.Quiet);
            Assert.IsFalse(done.AwaitsDecision, "Оба инцидента фазы разобраны — очередь обязана опустеть");
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
