using System.Collections.Generic;
using System.Linq;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Checks;
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
    /// Тесты ТЕМПА, а не арифметики.
    ///
    /// Обычный тест отвечает «правило соблюдено». Эти отвечают на другой вопрос:
    /// «когда игрок впервые что-то увидит и сколько будет ждать». Раз кампания
    /// детерминирована, её можно прогнать целиком за миллисекунды и померить —
    /// вместо того чтобы спорить о числах или ждать живого плейтеста, которого у
    /// соло-разработчика не будет в нужном объёме.
    ///
    /// Утверждения — ОКНА, а не точки: числа баланса плейсхолдерные и будут
    /// двигаться, а вот «первый инцидент приходит на второй неделе» — это уже
    /// дизайн-решение, и его поломку надо замечать.
    ///
    /// Прогоняется весь набор политик × тиров, тот же, что и в tools/Alpha.Sim.
    /// </summary>
    public class CampaignPacingTests
    {
        private const int Days = 200;

        private static readonly string[] Positions =
        {
            "storehouse_dock", "settlement_market", "settlement_farms",
            "infirmary_bed", "council_seat", "scouting_post", "workshop_bench"
        };

        // Список кампаний строится один раз: 12 прогонов по 400 фаз — это
        // десятки миллисекунд, но незачем платить их в каждом тесте.
        private static List<CampaignMetrics> _cache;

        private static List<CampaignMetrics> All()
        {
            if (_cache != null) return _cache;

            var result = new List<CampaignMetrics>();
            foreach (SimPolicy policy in new[] { SimPolicy.Passive, SimPolicy.PatrolEveryNight, SimPolicy.AggressiveChoices })
                foreach (int tier in new[] { 1, 2, 3, 4 })
                {
                    var cfg = new BalanceConfig();
                    var trace = CampaignSimulator.Run(Build(cfg, tier), policy, Days, cfg);
                    result.Add(CampaignSimulator.Measure(trace));
                }

            _cache = result;
            return _cache;
        }

        private static DayProcessor Build(BalanceConfig cfg, int tier)
        {
            var roster = new Roster();
            roster.Add(Make("guard", SkillType.Trade, 8, Positions[0]));
            roster.Add(Make("trader", SkillType.Trade, 7, Positions[1]));
            roster.Add(Make("farmer", SkillType.Survival, 6, Positions[2]));
            roster.Add(Make("medic", SkillType.Medicine, 7, Positions[3]));
            roster.Add(Make("elder", SkillType.Persuade, 6, Positions[4]));
            roster.Add(Make("scout", SkillType.Survival, 6, Positions[5]));

            var adapter = new RosterAdapter(roster);
            var pulse = new WorldPulse(cfg.Pulse);
            foreach (var s in DefaultPressureSources.All()) pulse.AddSource(s);

            return new DayProcessor(new TensionState(cfg.Tension), cfg, DayProcessor.DefaultSteps())
            {
                Tier = tier,
                Roster = adapter,
                Casualties = adapter,
                Population = new PopulationState(),
                Pulse = pulse,
                Incidents = DefaultIncidents.BuildTable(),
                Repeats = new RepeatTracker(),
                PostDomains = new[]
                {
                    new PostDomain(Positions[0], "склад", SkillKeys.Survival, 5),
                    new PostDomain(Positions[1], "рынок", SkillKeys.Trade, 5),
                    new PostDomain(Positions[3], "лазарет", SkillKeys.Medicine, 5)
                }
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

        private static string Where(CampaignMetrics m)
        {
            return m.Policy + ", тир " + m.Tier;
        }

        // ================= Окна темпа =================

        [Test]
        public void Pacing_FirstForewarning_ArrivesInTheFirstWeek()
        {
            foreach (var m in All())
            {
                Assert.Greater(m.FirstForewarnDay, 0, Where(m) + ": за всю кампанию ни одного предвестника");
                Assert.LessOrEqual(m.FirstForewarnDay, 10,
                    Where(m) + ": первое «что-то зреет» обязано прозвучать на первой неделе, иначе онбординг учит, что город немой");
            }
        }

        [Test]
        public void Pacing_FirstIncident_ArrivesInTheFirstTwoWeeks()
        {
            foreach (var m in All())
            {
                Assert.Greater(m.FirstIncidentDay, 0, Where(m) + ": за 200 суток не случилось ни одного инцидента");
                Assert.That(m.FirstIncidentDay, Is.InRange(4, 20),
                    Where(m) + ": первое событие не должно ни падать в первые же сутки, ни заставлять ждать три недели");
            }
        }

        [Test]
        public void Pacing_FirstDeltaSignal_ArrivesInTheFirstWeek()
        {
            foreach (var m in All())
                Assert.That(m.FirstDeltaDay, Is.InRange(1, 10),
                    Where(m) + ": «что изменилось со вчера» — единственный ответ на вопрос игрока; на первой неделе он обязан прозвучать");
        }

        [Test]
        public void Pacing_SilenceWithoutDelta_NeverExceedsTenDays()
        {
            foreach (var m in All())
                Assert.LessOrEqual(m.LongestStreakWithoutDelta, 20,
                    Where(m) + ": двадцать фаз подряд без единой дельты — это десять суток, в которые игрок жмёт кнопку и читает одно и то же");
        }

        [Test]
        public void Pacing_Ladder_NeverSkipsAStep_InAnyCampaign()
        {
            foreach (var m in All())
                Assert.AreEqual(0, m.SkippedLadderSteps,
                    Where(m) + ": ступень предвестника перепрыгнута. Вторая ступень — единственная, обязанная назвать место");
        }

        [Test]
        public void Pacing_EveryCampaign_ReachesItsCrisis()
        {
            foreach (var m in All())
                Assert.Greater(m.CrisisTotal, 0,
                    Where(m) + ": за 200 суток кризис не наступил ни разу — кульминация, которой нет, не кульминация");
        }

        // ================= Известные разрывы, закреплённые намеренно =================
        //
        // Эти тесты фиксируют то, что СЕЙЧАС не так. Они упадут, когда разрыв
        // закроют, — и это правильный повод обновить ожидание, а не подогнать его.
        // Без них находки аудита живут только в отчёте и тихо забываются.

        [Test]
        public void Pacing_CityAlwaysEndsAtFracture_KnownGap()
        {
            var bands = All().Select(m => m.FinalBand).Distinct().ToList();

            Assert.AreEqual(new[] { 4 }, bands.ToArray(),
                "ИЗВЕСТНЫЙ РАЗРЫВ: все 12 кампаний заканчиваются на «Изломе» независимо от того, " +
                "патрулировал игрок или спал. Напряжение — храповик: понижать его нечем, " +
                "потому что четыре из пяти снижающих драйверов не имеют вызывающего кода");
        }

        [Test]
        public void Pacing_CrisisNeverCostsPopulation_KnownGap()
        {
            foreach (var m in All())
                Assert.AreEqual(200, m.FinalPopulation,
                    Where(m) + " — ИЗВЕСТНЫЙ РАЗРЫВ: за кампанию случилось " + m.CrisisTotal +
                    " кризисов, а население не изменилось ни на человека. Отток людей выписан, но ни одна формула его не читает");
        }

        [Test]
        public void Pacing_BandChangeCanBeMute_KnownGap()
        {
            int mute = All().Sum(m => m.BandChangesWithoutSignal);

            Assert.Greater(mute, 0,
                "ИЗВЕСТНЫЙ РАЗРЫВ: инвариант 4 требует, чтобы смена полосы порождала наблюдаемый сигнал. " +
                "Принудительный сигнал только ДОБАВЛЯЕТ кандидата, слота не резервирует, поэтому в шумный " +
                "день его вытесняет бюджет. Когда это починят, тест упадёт");
        }

        [Test]
        public void Pacing_OneTopicDominatesTheWholeCampaign_KnownGap()
        {
            foreach (var m in All())
            {
                Assert.GreaterOrEqual(m.MaxTopicRepeats, 80,
                    Where(m) + " — ИЗВЕСТНЫЙ РАЗРЫВ (уменьшен, но не закрыт): самый частый ключ " +
                    "показывается " + m.MaxTopicRepeats + " раз за кампанию при " + m.DistinctTopics +
                    " уникальных ключах всего. Подавление повторов срезало это вдвое (было около двухсот), " +
                    "но дальше упирается не в механику, а в объём контента: кандидатов в спокойный день " +
                    "меньше, чем слотов. Лечится только новыми ключами");
                Assert.LessOrEqual(m.DistinctTopics, 40,
                    Where(m) + ": ключей стало больше — пора пересматривать оценку контент-бюджета");
            }
        }
    }
}
