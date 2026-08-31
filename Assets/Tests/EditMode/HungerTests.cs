using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Economy;
using Game.Core.Loop;
using Game.Core.Pressure;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Голод как стимул выходить наружу (Поправка №4).
    ///
    /// Давит с двух сторон: поднимает Напряжение (гасится Храмом и Укреплениями,
    /// а те строятся за компонент из вылазок) и роняет выработку базы. Смысл в
    /// том, что пересидеть голод дома нельзя.
    /// </summary>
    public class HungerTests
    {
        private static BalanceConfig Cfg() => new BalanceConfig
        {
            FoodUpkeepPerCompanion = 5,
            HungryProductionMultiplier = 0.5,
            HungryRoleXpMultiplier = 0.5
        };

        private static (BaseState state, Companion comp) MakeBase(BalanceConfig cfg)
        {
            var roster = new Roster();
            var state = new BaseState(roster, new ResourceLedger(), cfg);

            var arch = new CompanionArchetype("eng", "Инженер");
            arch.BaseStats.Set(StatType.Engineering, 10);
            var comp = arch.CreateInstance("eng_1");
            roster.Add(comp);

            state.AddSlot(new AssignmentSlotDefinition("bench", "Верстак", BaseSectionType.Workshop)
            {
                OutputKind = SlotOutputKind.Resource,
                OutputResource = ResourceType.Materials,
                PrimaryAptitude = StatType.Engineering,
                BaseOutput = 5,
                OutputPerPrimaryPoint = 1.0,
                OutputPerSecondaryPoint = 0
            });
            state.TryAssign(comp.Id, "bench");
            return (state, comp);
        }

        [Test]
        public void FullBelly_LeavesProductionAlone()
        {
            var cfg = Cfg();
            var (state, _) = MakeBase(cfg);
            state.Resources.Add(ResourceType.Food, 100);

            state.AdvanceCycle();
            Assert.IsFalse(state.WasHungryLastCycle);

            var second = state.AdvanceCycle();
            Assert.AreEqual(15, second.Produced[ResourceType.Materials], "сытый день — полная выработка");
        }

        /// <summary>Просадка приходит следующим циклом: прокорм считается последним шагом дня.</summary>
        [Test]
        public void HungryDay_HalvesNextCycleOutput()
        {
            var cfg = Cfg();
            var (state, _) = MakeBase(cfg);

            var first = state.AdvanceCycle();
            Assert.IsTrue(first.FoodShortage, "еды не было — день голодный");
            Assert.AreEqual(15, first.Produced[ResourceType.Materials], "в сам голодный день выработка ещё полная");
            Assert.IsTrue(state.WasHungryLastCycle);

            var second = state.AdvanceCycle();
            Assert.AreEqual(8, second.Produced[ResourceType.Materials], "15 * 0.5 = 7.5 -> 8");
        }

        [Test]
        public void FeedingAfterHunger_RestoresProduction()
        {
            var cfg = Cfg();
            var (state, _) = MakeBase(cfg);

            state.AdvanceCycle();                                  // голодный
            state.Resources.Add(ResourceType.Food, 100);
            state.AdvanceCycle();                                  // просевший, но накормленный
            Assert.IsFalse(state.WasHungryLastCycle);

            var third = state.AdvanceCycle();
            Assert.AreEqual(15, third.Produced[ResourceType.Materials], "просадка держится ровно один цикл");
        }

        /// <summary>
        /// Темп: первая граница полосы — 200, голодный день даёт 8, значит полоса
        /// меняется на 25-й день голода. Это и есть заявленная цена: голод давит
        /// заметно, но не мгновенно, и якорь шкалы (пассивный дрейф ≈ одна полоса
        /// за кампанию) не ломает — голод пассивным дрейфом не является.
        /// </summary>
        [Test]
        public void HungryDays_ShiftBandOnTwentyFifthDay()
        {
            var cfg = Cfg();
            var tension = new TensionState(cfg.Tension);
            var processor = new DayProcessor(tension, cfg, new IDayStep[] { new HungerStep() })
            {
                IsHungry = true
            };

            for (int i = 0; i < 24; i++) processor.Advance();
            Assert.AreEqual(TensionBand.Calm, tension.Band, "24 голодных дня — ещё в первой полосе");

            processor.Advance();
            Assert.Greater((int)tension.Band, (int)TensionBand.Calm, "25-й голодный день переводит полосу");
        }

        [Test]
        public void WellFedDays_DoNotRaiseTension()
        {
            var cfg = Cfg();
            var tension = new TensionState(cfg.Tension);
            var processor = new DayProcessor(tension, cfg, new IDayStep[] { new HungerStep() })
            {
                IsHungry = false
            };
            for (int i = 0; i < 20; i++) processor.Advance();

            Assert.AreEqual(TensionBand.Calm, tension.Band, "сытая база сама по себе Напряжение не растит");
        }

        /// <summary>Голод стоит перед тиком Напряжения — иначе инциденты взвесятся по вчерашней полосе.</summary>
        [Test]
        public void HungerStep_RunsBeforeTensionTick()
        {
            Assert.Less(DayStepOrder.Hunger, DayStepOrder.Tension);
        }

        /// <summary>Поправка №4 расширила закрытый список ровно на одно значение.</summary>
        [Test]
        public void Hunger_IsAllowedToRaiseTension()
        {
            var cfg = new TensionBalance();
            CollectionAssert.Contains(cfg.AllowedRaising, TensionDriver.Hunger);
            CollectionAssert.DoesNotContain(cfg.AllowedLowering, TensionDriver.Hunger);
        }
    }
}
