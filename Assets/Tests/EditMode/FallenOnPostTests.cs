using System.Linq;
using Game.Core;
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
    /// Загиблий на посту (аудит розривів, G17). Смерть очищала лише сторону
    /// напарника, слот лишався зайнятим: мертвий виробляв вічно, їв, а пост
    /// не можна було віддати живому — господар вважав його зайнятим.
    /// </summary>
    public class FallenOnPostTests
    {
        private static SettlementCycle Build(BalanceConfig cfg, int food, params (string id, SkillType skill)[] people)
        {
            var roster = new Roster();
            var state = new BaseState(roster, new ResourceLedger(), cfg);
            foreach (var (id, skill) in people)
                roster.Add(new CompanionArchetype(id, id).SetSkill(skill, 6).CreateInstance(id, cfg));

            state.AddSlot(DefaultContent.AllSlots().First(s => s.Id == "settlement_farms"));
            state.Resources.Add(ResourceType.Food, food);

            var production = new ProductionStep(state);
            var processor = new DayProcessor(new TensionState(cfg.Tension), cfg, SettlementCycle.BuildSteps(production));
            return new SettlementCycle(state, processor, production);
        }

        [Test]
        public void Fallen_ProducesNothing_AndThePostIsFreed()
        {
            var cycle = Build(new BalanceConfig { FoodUpkeepPerCompanion = 0 }, 0, ("farmer", SkillType.Survival));
            var state = cycle.State;
            state.TryAssign("farmer", "settlement_farms");

            cycle.AdvanceCalendarDay();
            int afterAliveDay = state.Resources.Get(ResourceType.Food);
            Assert.Greater(afterAliveDay, 0, "живой пахарь кормит");

            state.Roster.Get("farmer").MarkDead();
            cycle.AdvanceCalendarDay();

            Assert.AreEqual(afterAliveDay, state.Resources.Get(ResourceType.Food), "мёртвый не производит");
            Assert.IsFalse(state.GetSlot("settlement_farms").IsOccupied, "пост погибшего свободен");
        }

        [Test]
        public void Fallen_DoesNotEat()
        {
            var cycle = Build(new BalanceConfig { FoodUpkeepPerCompanion = 1 }, 10,
                ("a", SkillType.Trade), ("b", SkillType.Trade));

            cycle.AdvanceCalendarDay();
            Assert.AreEqual(8, cycle.State.Resources.Get(ResourceType.Food), "двое живых едят двое");

            cycle.State.Roster.Get("b").MarkDead();
            cycle.AdvanceCalendarDay();
            Assert.AreEqual(7, cycle.State.Resources.Get(ResourceType.Food), "погибший не ест");
        }

        [Test]
        public void PostOfTheFallen_GoesToTheLiving()
        {
            var cycle = Build(new BalanceConfig(), 50, ("farmer", SkillType.Survival), ("scout", SkillType.Survival));
            var state = cycle.State;
            state.TryAssign("farmer", "settlement_farms");
            state.Roster.Get("farmer").MarkDead();

            // Звірка ще не пройшла — господар все одно бачить пост вільним.
            Assert.AreEqual("staff:scout@settlement_farms", Steward.Staff(state));
        }

        [Test]
        public void Assign_OntoTheFallensPost_Works_ButTheFallenCannotBeAssigned()
        {
            var cycle = Build(new BalanceConfig(), 50, ("farmer", SkillType.Survival), ("scout", SkillType.Survival));
            var state = cycle.State;
            state.TryAssign("farmer", "settlement_farms");
            state.Roster.Get("farmer").MarkDead();

            Assert.AreEqual(AssignmentResult.Success, state.TryAssign("scout", "settlement_farms"),
                "игрок ставит живого на пост погибшего без обходных путей");

            state.Unassign("settlement_farms");
            Assert.AreEqual(AssignmentResult.CompanionUnavailable, state.TryAssign("farmer", "settlement_farms"),
                "мёртвого на пост не ставят");
        }
    }
}
