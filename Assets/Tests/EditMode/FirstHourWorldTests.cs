using System.Linq;
using Game.Core.Base;
using Game.Core.Loop;
using Game.Core.Session;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Мир первого часа (Foundation/A1): постройка перенесена из
    /// tools/Shared/SettlementWorld в ядро (аудит G8/G9/G15). Тесты проверяют
    /// контракт, а не баланс: кто на каком посту, кто зарегистрирован актором,
    /// что два построения дают тождественный мир.
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

            Assert.AreEqual("zakhar", world.BaseState.GetSlot("council_seat").AssignedCompanionId);
            Assert.AreEqual("keeper", world.BaseState.GetSlot("storehouse_dock").AssignedCompanionId);
            Assert.AreEqual("healer", world.BaseState.GetSlot("infirmary_bed").AssignedCompanionId);

            // Четыре из семи постов пустуют на старте (§3.0 FIRST_HOUR) — это
            // видимая цена, а не забытая расстановка.
            foreach (var empty in new[] { "settlement_market", "settlement_farms", "scouting_post", "workshop_bench" })
                Assert.IsNull(world.BaseState.GetSlot(empty).AssignedCompanionId, empty + " обязан пустовать на старте");

            Assert.IsFalse(world.Roster.Get("protagonist").IsAssigned, "Протагонист — в полі, не на посту");
            Assert.IsFalse(world.Roster.Get("maksym").IsAssigned, "Максим — в полі, не на посту");
            Assert.IsFalse(world.Roster.Get("myroslava").IsAssigned, "Мирослава — в полі, не на посту");
        }

        [Test]
        public void Build_StartingSet_IsAlreadyBuilt()
        {
            var world = FirstHourWorld.Build();

            Assert.IsTrue(world.CityWorks.Has("council_hall"), "Зал совета уже стоит (Поправка №6.1)");
            Assert.IsTrue(world.CityWorks.Has("storehouse"), "Склад уже стоит (Поправка №6.1)");
            Assert.IsFalse(world.CityWorks.Has("infirmary"), "Лазарет НЕ достроен — пост открыт вручную под §3.0, не зданием");
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
