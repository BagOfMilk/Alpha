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
    /// Охранители пяти неисправностей, найденных аудитом главного лупа.
    ///
    /// Каждый тест здесь падал бы ДО ремонта. В этом их единственный смысл:
    /// набор тестов, который проходит и на сломанной системе, ничего не
    /// охраняет — именно так кризис прожил в коде, ни разу не сработав.
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
        /// Голый конвейер: только часы и фоновый тик. Календарь меряем без Пульса
        /// и инцидентов, иначе в счёт попадёт исход случившегося события.
        /// </summary>
        private static DayProcessor MakeBare(BalanceConfig cfg, int tier = 1)
        {
            return new DayProcessor(new TensionState(cfg.Tension), cfg, DayProcessor.DefaultSteps())
            {
                Tier = tier
            };
        }

        // ============ 1. Календарь считает сутки, а не фазы ============

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

        // ============ 2. Кризис вообще случается ============

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

        // ============ 3. Проспанный предвестник не сгорает ============

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

            // Первая же фаза, в которой игрок бодрствует, обязана донести
            // накопленную ступень: она не сгорела, она ждала.
            var awake = p.Advance(DayPhase.Day);

            Assert.IsNotEmpty(awake.Forewarnings,
                "Ступень, выданная спящему, обязана быть предложена снова — иначе сон молча и невидимо наказывается потерей предупреждения");
        }

        // ============ 4. Лестница предвестников не вырождается ============

        [Test]
        public void Forewarn_LadderNeverSkipsAStep()
        {
            var cfg = new PulseBalance();
            var pulse = new WorldPulse(cfg);
            pulse.AddSource(new SteadySource());

            // Вторая ступень — единственная, обязанная назвать место. Если после
            // удара остаток заряда возвращает накопитель сразу на неё (или на
            // третью), игрок получает «скоро» без «где», и лестница мертва.
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
                    expected = 1; // после удара лестница начинается заново
                }
            }

            Assert.GreaterOrEqual(fires, 2,
                "Нужно хотя бы два удара: вырождение лестницы видно только на втором круге");
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
            // Ровно столько, чтобы лестница проходилась по ступени в день.
            public int InsistencePerDay(PulseContext ctx) => 30;
        }

        // ============ 5. Инцидент приходит из того источника, что предупреждал ============

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

            // Чужой инцидент нарочно тяжелее и стоит раньше по алфавиту: если шаг
            // дня забудет передать источник, отбор уйдёт именно к нему.
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
    }
}
