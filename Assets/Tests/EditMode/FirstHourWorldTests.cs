using System.Linq;
using Game.Core.Base;
using Game.Core.Loop;
using Game.Core.Session;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Світ першої години (Foundation/A1): побудова перенесена з
    /// tools/Shared/SettlementWorld у ядро (аудит G8/G9/G15). Тести перевіряють
    /// контракт, а не баланс: хто на якому посту, хто зареєстрований актором,
    /// що два побудови дають тотожний світ.
    /// </summary>
    public class FirstHourWorldTests
    {
        [Test]
        public void Build_NamedCast_IsOnTheRosterWithCards()
        {
            var world = FirstHourWorld.Build();

            var ids = world.Roster.All.Select(c => c.Id).OrderBy(id => id).ToArray();
            CollectionAssert.AreEquivalent(
                new[] { "zakhar", "keeper", "healer", "maksym", "myroslava", FirstHourWorld.ProtagonistId },
                ids, "Ростер обязан быть именным кастом открытия, а не generic-архетипами (аудит G8)");

            foreach (var c in world.Roster.All)
                Assert.IsNotNull(c.Card, c.Id + ": именной персонаж обязан нести карточку (Поправка №5.6 п. 1)");
        }

        [Test]
        public void Build_ProtagonistId_IsRegisteredAsAnActor()
        {
            var world = FirstHourWorld.Build();

            var adapter = (RosterAdapter)world.Processor.Roster;
            Assert.IsNotNull(adapter.Protagonist,
                "Протагонист обязан быть актором в RosterAdapter, а не просто строкой (аудит G9)");
            Assert.AreEqual(FirstHourWorld.ProtagonistId, adapter.Protagonist.Id);
            Assert.IsTrue(adapter.Protagonist.IsProtagonist);
        }

        [Test]
        public void Build_EveningLayout_MatchesFirstHourOpening()
        {
            var world = FirstHourWorld.Build();

            // Поправка №12.7 (старт без будівель): пости ради, складу й
            // лазарету закриті, доки не стане будівля, — тож і Захар, Дід Овсій
            // та Гафія на старті вільні. Раніше тест фіксував стару реальність
            // (усі троє на постах, рада й склад уже стоять, лазарет відкритий
            // руками); на пост першої будівлі тепер ставить вибір після прологу.
            foreach (var closed in new[] { "council_seat", "storehouse_dock", "infirmary_bed", "workshop_bench", "settlement_market" })
            {
                Assert.IsFalse(world.BaseState.GetSlot(closed).Unlocked, closed + ": пост будівлі закритий до її появи");
                Assert.IsNull(world.BaseState.GetSlot(closed).AssignedCompanionId, closed + " обязан пустовать на старте");
            }
            foreach (var free in new[] { "zakhar", "keeper", "healer" })
                Assert.IsFalse(world.Roster.Get(free).IsAssigned, free + ": пост ще не збудований — вільний");

            // Ферми й розвідпост будівлі не потребують — відкриті, але порожні
            // (§3.0 FIRST_HOUR): видима ціна, а не забута розстановка.
            foreach (var empty in new[] { "settlement_farms", "scouting_post" })
            {
                Assert.IsTrue(world.BaseState.GetSlot(empty).Unlocked, empty + ": відкритий без будівлі");
                Assert.IsNull(world.BaseState.GetSlot(empty).AssignedCompanionId, empty + " обязан пустовать на старте");
            }

            Assert.IsFalse(world.Roster.Get("protagonist").IsAssigned, "Протагонист — в полі, не на посту");
            Assert.IsFalse(world.Roster.Get("maksym").IsAssigned, "Максим — в полі, не на посту");
            Assert.IsFalse(world.Roster.Get("myroslava").IsAssigned, "Мирослава — в полі, не на посту");
        }

        [Test]
        public void Build_StartsWithoutAnyBuilding()
        {
            // Поправка №8.4, уточнена №12.7 (рішення власника 29.09.2026):
            // «Так придовити, перша будівля зьявляється як вибор після прологу».
            var world = FirstHourWorld.Build();

            CollectionAssert.IsEmpty(DefaultBuildings.StartingSet);
            CollectionAssert.IsEmpty(world.CityWorks.Built.ToList(), "гра стартує без жодної будівлі");
            foreach (var def in DefaultBuildings.All())
                Assert.IsFalse(world.CityWorks.IsBuilding(def.Id), def.Id + ": і без жодної будови в процесі");
        }

        [Test]
        public void Build_ProductionAndPopulationSteps_AreWiredIn()
        {
            var world = FirstHourWorld.Build();

            bool hasProduction = world.Processor.Steps.Any(s => s is ProductionStep);
            bool hasCityWorks = world.Processor.Steps.Any(s => s is CityWorksStep);
            bool hasPopulation = world.Processor.Steps.Any(s => s is PopulationStep);

            Assert.IsTrue(hasProduction, "Мост производства (Поправка №7.1) обязан быть в конвейере");
            Assert.IsTrue(hasCityWorks, "Городские работы обязаны быть в конвейере");
            Assert.IsTrue(hasPopulation, "Население обязано быть в конвейере");
        }

        [Test]
        public void Build_Economy_Sites_Flags_ArePluggedIntoTheProcessor()
        {
            var world = FirstHourWorld.Build();

            Assert.IsNotNull(world.Processor.Economy, "DayProcessor.Economy обязан быть подключён (закрывает D10)");
            Assert.AreSame(world.Sites, world.Processor.Sites);
            Assert.AreSame(world.Flags, world.Processor.Flags);
        }

        [Test]
        public void Build_IsDeterministic_TwoBuildsProduceIdenticalInitialSave()
        {
            var a = FirstHourWorld.Build(tier: 2);
            var b = FirstHourWorld.Build(tier: 2);

            Assert.AreEqual(a.Processor.SaveState(), b.Processor.SaveState(),
                "Два построения одного мира с одними аргументами обязаны быть тождественны (инвариант 1: без случайности)");
        }

        [Test]
        public void Play_FiveDays_AutoResolves_WithoutExceptions()
        {
            var world = FirstHourWorld.Build(tier: 1, requirePlayerDecision: true);

            for (int day = 0; day < 5; day++)
                foreach (var phase in new[] { DayPhase.Day, DayPhase.Night })
                {
                    var report = world.Cycle.AdvanceDay(phase);
                    while (report.AwaitsDecision)
                        report = world.Processor.ResolvePending(IncidentPath.Quiet);
                }

            Assert.AreEqual(5, world.Processor.CurrentDay);
        }

        [Test]
        public void DayStepOrder_Readiness_SitsBetweenTensionAndObligations()
        {
            Assert.Greater(DayStepOrder.Readiness, DayStepOrder.Tension);
            Assert.Less(DayStepOrder.Readiness, DayStepOrder.Obligations);
        }
    }
}
