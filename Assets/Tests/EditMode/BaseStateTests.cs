using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Economy;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public class BaseStateTests
    {
        private static (BaseState state, Companion comp) MakeBaseWithOneSlot(SlotOutputKind kind)
        {
            var roster = new Roster();
            var ledger = new ResourceLedger();
            var cfg = new BalanceConfig { FoodUpkeepPerCompanion = 0 }; // изолируем от расхода еды
            var state = new BaseState(roster, ledger, cfg);

            var arch = new CompanionArchetype("eng", "Инженер");
            arch.BaseStats.Set(StatType.Engineering, 10);
            var comp = arch.CreateInstance("eng_1");
            roster.Add(comp);

            var def = new AssignmentSlotDefinition("bench", "Верстак", BaseSectionType.Workshop)
            {
                OutputKind = kind,
                OutputResource = ResourceType.Materials,
                PrimaryAptitude = StatType.Engineering,
                BaseOutput = 5, OutputPerPrimaryPoint = 1.0, OutputPerSecondaryPoint = 0
            };
            state.AddSlot(def);
            return (state, comp);
        }

        [Test]
        public void Assign_Then_Produce_AddsResources()
        {
            var (state, comp) = MakeBaseWithOneSlot(SlotOutputKind.Resource);
            Assert.AreEqual(AssignmentResult.Success, state.TryAssign(comp.Id, "bench"));

            var report = state.AdvanceCycle();
            // 5 (base) + 10 (engineering) * 1.0 = 15
            Assert.AreEqual(15, state.Resources.Get(ResourceType.Materials));
            Assert.AreEqual(15, report.Produced[ResourceType.Materials]);
        }

        [Test]
        public void Assign_ToLockedSlot_Fails()
        {
            var (state, comp) = MakeBaseWithOneSlot(SlotOutputKind.Resource);
            state.GetSlot("bench").Unlocked = false;
            Assert.AreEqual(AssignmentResult.SlotLocked, state.TryAssign(comp.Id, "bench"));
        }

        [Test]
        public void Assign_OccupiedSlot_Fails()
        {
            var (state, comp) = MakeBaseWithOneSlot(SlotOutputKind.Resource);
            var other = new CompanionArchetype("eng2", "Инженер2").CreateInstance("eng_2");
            state.Roster.Add(other);

            Assert.AreEqual(AssignmentResult.Success, state.TryAssign(comp.Id, "bench"));
            Assert.AreEqual(AssignmentResult.SlotOccupied, state.TryAssign(other.Id, "bench"));
        }

        [Test]
        public void Reassign_MovesCompanion_FreesOldSlot()
        {
            var (state, comp) = MakeBaseWithOneSlot(SlotOutputKind.Resource);
            var def2 = new AssignmentSlotDefinition("bench2", "Верстак2", BaseSectionType.Workshop)
            {
                OutputResource = ResourceType.Materials, PrimaryAptitude = StatType.Engineering
            };
            state.AddSlot(def2);

            state.TryAssign(comp.Id, "bench");
            state.TryAssign(comp.Id, "bench2");

            Assert.IsFalse(state.GetSlot("bench").IsOccupied);
            Assert.AreEqual(comp.Id, state.GetSlot("bench2").AssignedCompanionId);
        }

        [Test]
        public void InjuredCompanion_ProducesLess()
        {
            var (state, comp) = MakeBaseWithOneSlot(SlotOutputKind.Resource);
            state.TryAssign(comp.Id, "bench");
            comp.InjuryPoints = 100; // тяжело ранен, но всё ещё на посту

            state.AdvanceCycle();
            // 15 * 0.5 (InjuredProductionMultiplier по умолчанию) = 7.5 -> 8
            Assert.AreEqual(8, state.Resources.Get(ResourceType.Materials));
        }

        [Test]
        public void FoodUpkeep_CausesShortage_WhenEmpty()
        {
            var roster = new Roster();
            var ledger = new ResourceLedger();
            var cfg = new BalanceConfig { FoodUpkeepPerCompanion = 5 };
            var state = new BaseState(roster, ledger, cfg);
            roster.Add(new CompanionArchetype("x", "X").CreateInstance("x_1"));

            var report = state.AdvanceCycle();
            Assert.IsTrue(report.FoodShortage);
        }
    }
}
