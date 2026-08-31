using System.Linq;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Checks;
using Game.Core.Loop;
using Game.Core.Pressure;
using Game.Core.Settlement;
using Game.Core.Signals;
using Game.Core.Stats;
using Game.Core.World;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Сквозная проверка петли: 90 дней жизни поселения целиком.
    /// Отвечает на вопрос «а оно вообще живёт?» — тот самый чек-пойнт,
    /// который Поправка №3 сделала первым приоритетом разработки.
    /// </summary>
    public class ChronicleSmokeTests
    {
        private static DayProcessor BuildTown(BalanceConfig cfg, out Roster roster)
        {
            roster = new Roster();
            foreach (var (id, skill) in new[] { ("hero", 9), ("guard", 7), ("trader", 6), ("scout", 5) })
            {
                var arch = new CompanionArchetype(id, id);
                arch.BaseStats.Set(StatType.Charisma, skill);
                arch.BaseStats.Set(StatType.Will, skill);
                arch.BaseStats.Set(StatType.Survival, skill);
                arch.BaseStats.Set(StatType.Aim, skill);
                roster.Add(arch.CreateInstance(id));
            }

            var baseState = new BaseState(roster, new Game.Core.Economy.ResourceLedger(), cfg);
            baseState.AddSlot(new AssignmentSlotDefinition("watch", "Дозор", BaseSectionType.Fortifications));
            baseState.TryAssign("guard", "watch");

            var adapter = new RosterAdapter(roster, "hero");
            var pulse = new WorldPulse(cfg.Pulse);
            foreach (var s in DefaultPressureSources.All()) pulse.AddSource(s);

            return new DayProcessor(new TensionState(cfg.Tension), cfg, DayProcessor.DefaultSteps())
            {
                Tier = 2,
                Roster = adapter,
                Casualties = adapter,
                Population = new PopulationState(),
                Pulse = pulse,
                Incidents = DefaultIncidents.BuildTable(),
                Repeats = new RepeatTracker(),
                IsPatrolling = true,
                PostDomains = new[] { new PostDomain("watch", "улицы", SkillKeys.Survival, 6) }
            };
        }

        [Test]
        public void Chronicle_NinetyDays_TownActuallyLives()
        {
            var cfg = Cfg();
            var town = BuildTown(cfg, out _);

            int incidents = 0, forewarnings = 0, signals = 0;

            for (int day = 1; day <= 90; day++)
            {
                foreach (var phase in new[] { DayPhase.Day, DayPhase.Night })
                {
                    var report = town.Advance(phase);
                    incidents += report.Incidents.Count;
                    forewarnings += report.Forewarnings.Count;
                    signals += report.Signals.Requests.Count;
                }

                if (day % 12 == 0)
                    TensionDrivers.QuestChoice(town.Tension, TensionDrivers.ChoiceWeight.Major, "q" + day, cfg);
            }

            Assert.Greater(forewarnings, 0, "Мир обязан предупреждать");
            Assert.Greater(incidents, 0, "И обязан что-то делать");
            Assert.Greater(signals, 0, "И всё это должно быть слышно игроку");
            Assert.Greater(town.Tension.Band, TensionBand.Calm, "Тяжёлые решения не проходят даром");
        }

        [Test]
        public void Chronicle_QuietTown_StaysQuiet()
        {
            var cfg = Cfg();
            var town = BuildTown(cfg, out _);
            town.Tier = 1;

            // Ни одного решения игрока — только течение времени.
            for (int day = 1; day <= 90; day++)
            {
                town.Advance(DayPhase.Day);
                town.Advance(DayPhase.Night);
            }

            Assert.LessOrEqual((int)town.Tension.Band, (int)TensionBand.Murmur,
                "Хутор, где игрок ничего не решал, не должен скатываться в кризис");
        }

        [Test]
        public void Chronicle_NeverLeaksHiddenNumbers()
        {
            var cfg = Cfg();
            var town = BuildTown(cfg, out _);

            for (int day = 1; day <= 60; day++)
            {
                var report = town.Advance(day % 2 == 0 ? DayPhase.Night : DayPhase.Day);

                foreach (var r in report.Signals.Requests)
                {
                    Assert.IsFalse(r.TopicId.Any(char.IsDigit) && r.TopicId.Contains("tension.value"),
                        "Сигнал не имеет права нести сырое значение шкалы");
                }
            }
        }

        private static BalanceConfig Cfg() => new BalanceConfig();
    }
}
