using System.Collections.Generic;
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
    /// Охоронці п'яти несправностей, знайдених аудитом головного лупа.
    ///
    /// Кожен тест тут падав би ДО ремонту. У цьому їхній єдиний сенс:
    /// набір тестів, який проходить і на зламаній системі, нічого не
    /// охороняє — саме так криза прожила в коді, жодного разу не спрацювавши.
    /// </summary>
    public class LoopRepairTests
    {
        private static BalanceConfig Cfg() => new BalanceConfig();

        private static Companion Guard(string id, int skill)
        {
            var arch = new CompanionArchetype(id, id);
            arch.SetSkill(SkillType.Survival, skill);
            return arch.CreateInstance(id);
        }

        private static DayProcessor MakeProcessor(BalanceConfig cfg, int startTension = 0, int tier = 2)
        {
            var roster = new Roster();
            roster.Add(Guard("alpha", 3));
            roster.Add(Guard("beta", 3));
            roster.Add(Guard("gamma", 3));

            var tension = new TensionState(cfg.Tension, startTension);
            var adapter = new RosterAdapter(roster, null);
            var pulse = new WorldPulse(cfg.Pulse);
            foreach (var s in DefaultPressureSources.All()) pulse.AddSource(s);

            return new DayProcessor(tension, cfg, DayProcessor.DefaultSteps())
            {
                Tier = tier,
                Roster = adapter,
                Casualties = adapter,
                Population = new PopulationState(),
                Pulse = pulse,
                Incidents = DefaultIncidents.BuildTable(),
                Repeats = new RepeatTracker()
            };
        }

        /// <summary>
        /// Голий конвеєр: тільки годинник і фоновий тік. Календар міряємо без Пульсу
        /// і інцидентів, інакше в лік потрапить наслідок події, що сталася.
        /// </summary>
        private static DayProcessor MakeBare(BalanceConfig cfg, int tier = 1)
        {
            return new DayProcessor(new TensionState(cfg.Tension), cfg, DayProcessor.DefaultSteps())
            {
                Tier = tier
            };
        }

        // ============ 1. Календар рахує доби, а не фази ============

        [Test]
        public void Calendar_FullDay_AdvancesCounterOnce()
        {
            var p = MakeBare(Cfg());

            for (int i = 0; i < 10; i++) p.AdvanceFullDay();

            Assert.AreEqual(10, p.CurrentDay,
                "Сутки — это день плюс ночь. Счётчик, считающий фазы, делает каждое окно в днях вдвое короче заявленного");
        }

        [Test]
        public void Calendar_TierTick_AppliedOncePerDayNotPerPhase()
        {
            var cfg = Cfg();
            var p = MakeBare(cfg, tier: 1);

            for (int i = 0; i < 10; i++) p.AdvanceFullDay();

            Assert.AreEqual(10, p.Tension.Value,
                "Тир 1 даёт 1.0 за СУТКИ. Две порции за день ломают якорь про хутор ровно вдвое");
        }

        // ============ 2. Криза взагалі трапляється ============

        [Test]
        public void Crisis_OnFractureBand_ActuallyFires()
        {
            var cfg = Cfg();
            var p = MakeProcessor(cfg, startTension: 850);
            p.IsPatrolling = true;

            bool crisisHappened = false;
            for (int day = 1; day <= 90 && !crisisHappened; day++)
                foreach (var report in p.AdvanceFullDay())
                    if (report.Incidents.Any(i => i.WasCrisis)) crisisHappened = true;

            Assert.IsTrue(crisisHappened,
                "На верхней полосе кризис обязан наступить. Накопитель, который копится, объявляет и никогда не бьёт, учит игнорировать предупреждения");
        }

        [Test]
        public void Crisis_IsPrecededByHeardLevel3AndGraceWindow()
        {
            var cfg = Cfg();
            var p = MakeProcessor(cfg, startTension: 850);
            p.IsPatrolling = true;

            int heardLevel3Day = -1;
            int crisisDay = -1;

            for (int day = 1; day <= 90 && crisisDay < 0; day++)
                foreach (var report in p.AdvanceFullDay())
                {
                    if (heardLevel3Day < 0 &&
                        report.Forewarnings.Any(f => f.SourceId == "crisis" && f.Level >= 3))
                        heardLevel3Day = report.Day;

                    if (crisisDay < 0 && report.Incidents.Any(i => i.WasCrisis))
                        crisisDay = report.Day;
                }

            Assert.Greater(crisisDay, 0, "Кризис не наступил — проверять нечего");
            Assert.Greater(heardLevel3Day, 0,
                "Кризис ударил без УСЛЫШАННОГО предвестника третьей ступени");
            Assert.GreaterOrEqual(crisisDay - heardLevel3Day, cfg.Pulse.CrisisGraceDays,
                "Между услышанной третьей ступенью и ударом обязано пройти окно на реакцию");
        }

        // ============ 3. Проспаний передвісник не згорає ============

        [Test]
        public void Forewarn_SleptThrough_IsOfferedAgainLater()
        {
            var cfg = Cfg();
            var p = MakeProcessor(cfg, startTension: 500);
            p.IsPatrolling = false;

            for (int i = 0; i < 12; i++)
            {
                var slept = p.Advance(DayPhase.Night);
                Assert.IsEmpty(slept.Forewarnings, "Спящий не слышит ночных предвестников");
            }

            // Перша ж фаза, в якій гравець не спить, зобов'язана донести
            // накопичену ступінь: вона не згоріла, вона чекала.
            var awake = p.Advance(DayPhase.Day);

            Assert.IsNotEmpty(awake.Forewarnings,
                "Ступень, выданная спящему, обязана быть предложена снова — иначе сон молча и невидимо наказывается потерей предупреждения");
        }

        // ============ 4. Драбина передвісників не вироджується ============

        [Test]
        public void Forewarn_LadderNeverSkipsAStep()
        {
            var cfg = new PulseBalance();
            var pulse = new WorldPulse(cfg);
            pulse.AddSource(new SteadySource());

            // Друга ступінь — єдина, зобов'язана назвати місце. Якщо після
            // удару залишок заряду повертає накопичувач одразу на неї (або на
            // третю), гравець отримує «скоро» без «де», і драбина мертва.
            int expected = 1;
            int fires = 0;

            for (int day = 1; day <= 30; day++)
            {
                var tick = pulse.Advance(new PulseContext(day, false, 1, 0, false));
                pulse.MarkDelivered(tick.Forewarnings, day);

                foreach (var f in tick.Forewarnings)
                {
                    Assert.AreEqual(expected, f.Level,
                        $"День {day}: ступень перепрыгнута. Лестница обязана идти 1 → 2 → 3 без пропусков");
                    expected++;
                }

                if (tick.FiredSourceIds.Count > 0)
                {
                    fires++;
                    expected = 1; // після удару драбина починається заново
                }
            }

            Assert.GreaterOrEqual(fires, 2,
                "Нужно хотя бы два удара: вырождение лестницы видно только на втором круге");
        }

        /// <summary>
        /// Те саме обіцяне через справжній конвеєр: рахує ступені крок
        /// Pulse, і рахує ПІСЛЯ того, як пульс відстрілявся. Тест вище
        /// перевіряє тільки «не перестрибує» — мовчазне коло він пропускає:
        /// без передвісників нема чого й перестрибувати. Тут кожен інцидент
        /// ticker зобов'язаний прийти після своєї першої й другої ступені, і жодна
        /// ступінь не звучить в одному звіті з самим інцидентом.
        /// </summary>
        [Test]
        public void Forewarn_EveryIncident_IsAnnounced_OnEveryCycle_ThroughTheDayPipeline()
        {
            var cfg = Cfg();
            var table = new IncidentTable();
            table.Add(new IncidentDefinition
            {
                Id = "own", TopicId = "incident.own", DomainTag = "своё",
                SourceId = "ticker", MinBand = TensionBand.Calm, MaxBand = TensionBand.Fracture, Weight = 1,
                QuietPathSkill = SkillKeys.Survival, QuietPathThreshold = 5
            });

            var pulse = new WorldPulse(cfg.Pulse);
            pulse.AddSource(new SteadySource("ticker"));

            var p = new DayProcessor(new TensionState(cfg.Tension), cfg, DayProcessor.DefaultSteps())
            {
                Tier = 1,
                Pulse = pulse,
                Incidents = table,
                Population = new PopulationState(),
                Repeats = new RepeatTracker()
            };

            var heardThisCycle = new System.Collections.Generic.List<int>();
            int incidents = 0;
            for (int i = 0; i < 16; i++)
            {
                var report = p.Advance(DayPhase.Day);
                var levels = report.Forewarnings.Where(f => f.SourceId == "ticker").Select(f => f.Level).ToList();
                bool struck = report.Incidents.Any(o => o.IncidentId == "own");

                if (struck)
                    Assert.IsEmpty(levels, $"Сутки {report.Day}: ступень в одном отчёте с самим инцидентом ни о чём не предупреждает");
                heardThisCycle.AddRange(levels);

                if (!struck) continue;
                incidents++;
                CollectionAssert.AreEqual(new[] { 1, 2 }, heardThisCycle,
                    $"Инцидент №{incidents} (сутки {report.Day}) пришёл без своей лестницы");
                heardThisCycle.Clear();
            }

            Assert.GreaterOrEqual(incidents, 3, "Нужно несколько кругов: вырождение видно со второго");
        }

        // ============ 7. BotRunner не губить записи DayLog між фазами ============

        /// <summary>
        /// РЕГРЕСІЯ (знайдено при діагностиці G21 24.09.2026, реальним прогоном
        /// Steward): <c>BotRunner.CollectDelta</c> визначав нову фазу за
        /// розміром логу (<c>log.Count &lt; курсор</c>) — ознака мовчки не
        /// спрацьовує, коли ClearDayLog() почав нову фазу, а вона встигла дати
        /// записів БІЛЬШЕ, ніж курсор мав на кінець попередньої (коротка
        /// Evening-фаза з парою команд, за нею — щільний AdvanceDay з
        /// десятком). Перші count-курсор записів нової фази тоді тихо не
        /// доходили до <c>fullLog</c>/<c>onEvent</c> — хоча <c>session.DayLog</c>
        /// їх мав, гра не постраждала, тільки читач. Так "forewarn.level2"
        /// Тугара (доба 6/День, Steward) зникав з бот-логу, хоч і був у самому
        /// SignalComposer — саме це і виглядало зовні як пропущена ступінь
        /// лестниці, хоча G21 (SignalComposer.Select) тут ні до чого.
        ///
        /// Мутаційна перевірка: прибрати <see cref="GameSession.DayLogVersion"/>-
        /// засновану ознаку в CollectDelta (повернути висновок за розміром) —
        /// і цей тест впаде знову, бо Steward регулярно чергує короткі й щільні
        /// фази саме так.
        /// </summary>
        [Test]
        public void BotRunner_FullLog_NeverLosesEventsAcrossAShortToDenseTransition()
        {
            var log = new List<Game.Core.Session.GameEvent>();
            var session = new Game.Core.Session.GameSession(null);
            session.NewGame(new Game.Core.Session.NewGameOptions());
            Game.Core.Session.Bots.BotRunner.Drive(session, new Game.Core.Session.Bots.StewardPolicy(), 15, fullLog: log);

            var levelsBySubject = new Dictionary<string, List<int>>();
            foreach (var e in log)
            {
                if (e.Key == null || !e.Key.StartsWith("forewarn.level")) continue;
                int level = int.Parse(e.Key.Substring("forewarn.level".Length));
                string subject = e.Args != null && e.Args.TryGetValue("subject", out var s) ? s : "?";
                if (!levelsBySubject.TryGetValue(subject, out var list))
                    levelsBySubject[subject] = list = new List<int>();
                list.Add(level);
            }

            Assert.IsTrue(levelsBySubject.ContainsKey("tuhar"),
                "За 15 діб Steward жодного forewarn.level* від tuhar — прогін зламано, перевіряти нічого");

            foreach (var pair in levelsBySubject)
            {
                int lastLevel = 0;
                foreach (var level in pair.Value)
                {
                    // level == 1 легітимний завжди (перший крик або перезапуск
                    // після Fire()) — те саме правило, що в Row08.
                    Assert.IsTrue(level == 1 || level == lastLevel + 1,
                        $"джерело {pair.Key}: {lastLevel} -> {level} — fullLog загубив проміжний запис (CollectDelta)");
                    lastLevel = level;
                }
            }
        }

        private sealed class SteadySource : IPressureSource
        {
            private readonly string _id;
            public SteadySource(string id = "steady") { _id = id; }

            public string Id => _id;
            public WorldEventKind Kind => WorldEventKind.InternalThreat;
            public string DomainTag => "домен";
            public int Threshold => 100;
            public int CooldownDays => 0;
            public bool IsActive(PulseContext ctx) => true;
            public bool Announces => true;
            // Рівно стільки, щоб драбина проходила по ступені за добу.
            public int InsistencePerDay(PulseContext ctx) => 30;
        }

        // ============ 5. Інцидент приходить із того джерела, що попереджало ============

        [Test]
        public void Incidents_AreDrawnOnlyFromTheirOwnSource()
        {
            var table = DefaultIncidents.BuildTable();

            var streetPool = table.Eligible(TensionBand.Fracture, 4, true, false, "street");
            var nightPool = table.Eligible(TensionBand.Fracture, 4, true, false, "night");

            Assert.IsNotEmpty(streetPool, "У уличного накопителя обязан быть свой пул");
            Assert.IsNotEmpty(nightPool, "У ночного накопителя обязан быть свой пул");

            Assert.IsTrue(streetPool.All(d => d.SourceId == "street"),
                "Улица не имеет права порождать чужие события: предвестник назвал бы не тот домен");
            Assert.IsTrue(nightPool.All(d => d.SourceId == "night"),
                "Ночь не имеет права порождать дневные события");
            Assert.IsEmpty(streetPool.Intersect(nightPool),
                "Пулы источников не должны пересекаться — иначе домен предвестника ничего не значит");
        }

        [Test]
        public void EveryIncident_DeclaresItsSource()
        {
            var table = DefaultIncidents.BuildTable();

            var orphans = table.All.Where(d => string.IsNullOrEmpty(d.SourceId))
                                   .Select(d => d.Id).ToList();

            Assert.IsEmpty(orphans,
                "Инцидент без источника попадёт в пул любого накопителя: " + string.Join(", ", orphans));
        }

        [Test]
        public void IncidentStep_ResolvesIncidentOfTheSourceThatFired()
        {
            var cfg = Cfg();

            // Чужий інцидент навмисно важчий і стоїть раніше за алфавітом: якщо крок
            // дня забуде передати джерело, відбір піде саме до нього.
            var table = new IncidentTable();
            table.Add(new IncidentDefinition
            {
                Id = "aaa_foreign", TopicId = "incident.foreign", DomainTag = "чужое",
                SourceId = "other", MinBand = TensionBand.Calm, Weight = 1000,
                QuietPathSkill = SkillKeys.Survival, QuietPathThreshold = 5
            });
            table.Add(new IncidentDefinition
            {
                Id = "bbb_own", TopicId = "incident.own", DomainTag = "своё",
                SourceId = "ticker", MinBand = TensionBand.Calm, Weight = 1,
                QuietPathSkill = SkillKeys.Survival, QuietPathThreshold = 5
            });

            var pulse = new WorldPulse(cfg.Pulse);
            pulse.AddSource(new SteadySource("ticker"));

            var p = new DayProcessor(new TensionState(cfg.Tension), cfg, DayProcessor.DefaultSteps())
            {
                Tier = 1,
                Pulse = pulse,
                Incidents = table,
                Population = new PopulationState(),
                Repeats = new RepeatTracker()
            };

            string firstIncident = null;
            for (int day = 1; day <= 20 && firstIncident == null; day++)
            {
                var report = p.Advance(DayPhase.Day);
                if (report.Incidents.Count > 0) firstIncident = report.Incidents[0].IncidentId;
            }

            Assert.AreEqual("bbb_own", firstIncident,
                "Сработал накопитель ticker — значит и событие обязано быть из его пула, иначе предвестник называл один домен, а пришёл другой");
        }

        // ============ 6. Накопичувач без свого інциденту не згорає мовчки ============

        /// <summary>
        /// Раніше пульс розряджав будь-який готовий накопичувач, а крок інцидентів,
        /// не знайшовши його пулу, просто йшов далі: заряд і почута драбина
        /// обнулялися без жодного сліду в звіті (так жив «Тугар» у зрізі
        /// першої години). Тепер пульс питає крок інцидентів, чи є
        /// джерелу чим спрацювати. Сирота стоїть раніше за Id: без запитання він
        /// забирав би нічию за слот дня і відбирав його у ticker.
        /// </summary>
        [Test]
        public void Pulse_SourceWithoutIncident_NeverDischargesSilently_AndLeavesTheSlot()
        {
            var cfg = Cfg();
            var table = new IncidentTable();
            table.Add(new IncidentDefinition
            {
                Id = "own", TopicId = "incident.own", DomainTag = "своё",
                SourceId = "ticker", MinBand = TensionBand.Calm, Weight = 1,
                QuietPathSkill = SkillKeys.Survival, QuietPathThreshold = 5
            });

            var pulse = new WorldPulse(cfg.Pulse);
            pulse.AddSource(new SteadySource("aaa_orphan"));
            pulse.AddSource(new SteadySource("ticker"));

            var p = new DayProcessor(new TensionState(cfg.Tension), cfg, DayProcessor.DefaultSteps())
            {
                Tier = 1,
                Pulse = pulse,
                Incidents = table,
                Population = new PopulationState(),
                Repeats = new RepeatTracker()
            };

            int firstIncidentDay = -1;
            double lastFill = 0.0;
            int lastLevel = 0;
            for (int i = 0; i < 8; i++)
            {
                var report = p.Advance(DayPhase.Day);
                if (firstIncidentDay < 0 && report.Incidents.Count > 0) firstIncidentDay = report.Day;

                double fill = pulse.Tracks["aaa_orphan"].Fill;
                int level = pulse.DeliveredLevelOf("aaa_orphan");
                Assert.GreaterOrEqual(fill, lastFill, $"Сутки {report.Day}: заряд сироты сброшен, а события нет");
                Assert.GreaterOrEqual(level, lastLevel, $"Сутки {report.Day}: услышанная ступень сироты откатилась");
                lastFill = fill;
                lastLevel = level;
            }

            Assert.AreEqual(3, lastLevel, "Лестница сироты дошла до конца и стоит, а не начинается заново");
            Assert.AreEqual(4, firstIncidentDay,
                "Ставка 30, порог 100: ticker готов на четвёртые сутки, и слот его — сирота не должна его занять");
        }

        /// <summary>
        /// Пульс вирішує «чи є чим спрацювати» за полосою на момент свого кроку,
        /// а крок інцидентів зобов'язаний вибирати за ТІЄЮ Ж полосою. Наслідок першого
        /// інциденту фази рухає Напругу одразу: без знімка друге джерело
        /// шукало б пул уже за новою полосою, не знаходило нічого і згорало мовчки.
        /// </summary>
        [Test]
        public void IncidentStep_PicksEveryFiredSource_ByTheBandThePulseAskedAbout()
        {
            var cfg = Cfg();
            var table = new IncidentTable();
            table.Add(new IncidentDefinition
            {
                Id = "surge", TopicId = "incident.surge", DomainTag = "площадь",
                SourceId = "s1", MinBand = TensionBand.Calm, MaxBand = TensionBand.Fracture, Weight = 1,
                QuietPathSkill = SkillKeys.Survival, QuietPathThreshold = 5,
                TensionByBand = new[] { 300, 300, 300, 300 }
            });
            table.Add(new IncidentDefinition
            {
                Id = "calm_only", TopicId = "incident.calm_only", DomainTag = "рынок",
                SourceId = "s2", MinBand = TensionBand.Calm, MaxBand = TensionBand.Calm, Weight = 1,
                QuietPathSkill = SkillKeys.Survival, QuietPathThreshold = 5
            });

            var pulse = new WorldPulse(cfg.Pulse);
            pulse.AddSource(new OneNightSource("s1"));
            pulse.AddSource(new OneNightSource("s2"));

            var p = new DayProcessor(new TensionState(cfg.Tension, 150), cfg, DayProcessor.DefaultSteps())
            {
                Tier = 1,
                Pulse = pulse,
                Incidents = table,
                Population = new PopulationState(),
                Repeats = new RepeatTracker()
            };

            var report = p.Advance(DayPhase.Night);

            Assert.Greater(p.Tension.Band, TensionBand.Calm, "предпосылка: первый инцидент фазы сдвинул полосу");
            CollectionAssert.AreEquivalent(new[] { "surge", "calm_only" }, report.Incidents.Select(o => o.IncidentId).ToArray(),
                "Оба сработавших источника разобраны: пульс разрядил s2 при Спокойствии, и пул ищется по Спокойствию");
        }

        /// <summary>Готовий рівно в першу ніч: два таких дають дві розрядки в одній фазі.</summary>
        private sealed class OneNightSource : IPressureSource
        {
            public OneNightSource(string id) { Id = id; }

            public string Id { get; }
            public WorldEventKind Kind => WorldEventKind.InternalThreat;
            public string DomainTag => "домен";
            public int Threshold => 1;
            public int CooldownDays => 999;
            public bool IsActive(PulseContext ctx) => ctx.IsNight;
            public bool Announces => false;
            public int InsistencePerDay(PulseContext ctx) => 1;
        }
    }
}
