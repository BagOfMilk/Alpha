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
    /// Три пресета из сцены «Хроника». Демо обещает игроку тезис:
    /// правила одни, а судьба разная — её определяют решения, а не кубик.
    /// Здесь это обещание проверяется, а не берётся на веру: если числа
    /// пресетов подобраны плохо, тесты упадут и демо не соврёт владельцу.
    ///
    /// Настройки обязаны совпадать с ChronicleSceneBuilder.AddPreset.
    /// </summary>
    public class ChroniclePresetTests
    {
        private sealed class Run
        {
            public TensionBand FinalBand;
            public int Incidents;
            public int Crises;
            public int Forewarnings;
        }

        private static Run Simulate(int tier, int days, bool patrol, int choiceEvery)
        {
            var cfg = new BalanceConfig();

            var roster = new Roster();
            foreach (var pair in new[] { ("hero", 9), ("guard", 7), ("trader", 6), ("scout", 5) })
            {
                var arch = new CompanionArchetype(pair.Item1, pair.Item1);
                foreach (StatType st in System.Enum.GetValues(typeof(StatType)))
                    arch.BaseStats.Set(st, pair.Item2);
                roster.Add(arch.CreateInstance(pair.Item1));
            }

            var baseState = new BaseState(roster, new Game.Core.Economy.ResourceLedger(), cfg);
            baseState.AddSlot(new AssignmentSlotDefinition("watch", "Дозор", BaseSectionType.Fortifications));
            baseState.AddSlot(new AssignmentSlotDefinition("market", "Рынок", BaseSectionType.Settlement));
            baseState.TryAssign("guard", "watch");
            baseState.TryAssign("trader", "market");

            var adapter = new RosterAdapter(roster, "hero");
            var tension = new TensionState(cfg.Tension);
            var pulse = new WorldPulse(cfg.Pulse);
            foreach (var s in DefaultPressureSources.All()) pulse.AddSource(s);

            var p = new DayProcessor(tension, cfg, DayProcessor.DefaultSteps())
            {
                Tier = tier,
                Roster = adapter,
                Casualties = adapter,
                Population = new PopulationState(),
                Pulse = pulse,
                Incidents = DefaultIncidents.BuildTable(),
                Repeats = new RepeatTracker(),
                IsPatrolling = patrol,
                PostDomains = new[]
                {
                    new PostDomain("watch", "улицы", SkillKeys.Survival, 6),
                    new PostDomain("market", "рынок", SkillKeys.Trade, 6)
                }
            };

            var run = new Run();
            for (int day = 1; day <= days; day++)
            {
                foreach (var phase in new[] { DayPhase.Day, DayPhase.Night })
                {
                    var report = p.Advance(phase);
                    run.Incidents += report.Incidents.Count;
                    run.Crises += report.Incidents.Count(i => i.WasCrisis);
                    run.Forewarnings += report.Forewarnings.Count;
                }

                if (choiceEvery > 0 && day % choiceEvery == 0)
                    TensionDrivers.QuestChoice(tension, TensionDrivers.ChoiceWeight.Major, "q" + day, cfg);
            }

            run.FinalBand = tension.Band;
            return run;
        }

        private static Run QuietHamlet() => Simulate(tier: 1, days: 90, patrol: true, choiceEvery: 0);
        private static Run PressuredVillage() => Simulate(tier: 2, days: 90, patrol: true, choiceEvery: 10);
        private static Run BreakingTown() => Simulate(tier: 3, days: 120, patrol: false, choiceEvery: 6);

        [Test]
        public void Preset1_QuietHamlet_StaysCalm()
        {
            var run = QuietHamlet();
            Assert.LessOrEqual((int)run.FinalBand, (int)TensionBand.Murmur,
                "Хутор, где игрок ничего тяжёлого не решал, не имеет права скатиться в кризис");
            Assert.AreEqual(0, run.Crises, "И кризиса там быть не должно");
        }

        [Test]
        public void Preset3_BreakingTown_ActuallyBreaks()
        {
            var run = BreakingTown();
            Assert.GreaterOrEqual((int)run.FinalBand, (int)TensionBand.Ferment,
                "Слобода под постоянным давлением обязана дойти до края — иначе демо врёт");
            Assert.Greater(run.Incidents, 0, "И происшествия там обязаны случаться");
        }

        [Test]
        public void Preset3_BreakingTown_ReachesCrisis()
        {
            var run = BreakingTown();
            Assert.Greater(run.Crises, 0,
                "Демо обещает игроку кризис, о котором предупреждали. " +
                "Если ни один пресет до кризиса не доводит — обещание пустое");
        }

        [Test]
        public void Presets_DifferInFate_NotInRules()
        {
            var quiet = QuietHamlet();
            var breaking = BreakingTown();

            Assert.Less((int)quiet.FinalBand, (int)breaking.FinalBand,
                "Главный тезис демо: одни правила — разная судьба. " +
                "Если полосы совпали, сравнивать пресеты бессмысленно");
        }

        [Test]
        public void Preset3_SleepingThroughNights_LosesForewarnings()
        {
            // Слобода спит (patrol: false) — и платит за это слепотой.
            var sleeping = Simulate(tier: 3, days: 120, patrol: false, choiceEvery: 6);
            var patrolling = Simulate(tier: 3, days: 120, patrol: true, choiceEvery: 6);

            Assert.Less(sleeping.Forewarnings, patrolling.Forewarnings,
                "Отказ от патруля обязан стоить предвестников — это цена, а не косметика");
        }

        [Test]
        public void Presets_AreDeterministic()
        {
            Assert.AreEqual(PressuredVillage().FinalBand, PressuredVillage().FinalBand);
            Assert.AreEqual(BreakingTown().Crises, BreakingTown().Crises,
                "Два одинаковых прогона обязаны дать одинаковый результат");
        }
    }
}
