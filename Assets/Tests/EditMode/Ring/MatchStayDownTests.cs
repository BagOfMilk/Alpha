using Game.Core.Ring;
using Game.Core.Ring.Match;
using Game.Gameplay.Ring.Stage;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Пози без телепортів (звіт 8f, кадри 05–06): невдалий підйом лежачого не ставить його в стійку (раніше план давав
    /// «Idle_Tired_Loop» — стійка, а за нею продаж падіння), і ланцюг кліпів ліпить розрив тазу.
    /// </summary>
    public sealed class MatchStayDownTests
    {
        private static MatchEvent GetUp(RollTier t)
        {
            return new MatchEvent { Kind = MatchEventKind.Action, Actor = MatchSide.Rival, Action = MatchActionId.GetUp, Tier = t };
        }

        [TestCase("lying_back")]
        [TestCase("lying_front")]
        [TestCase("kneel_up")]
        [TestCase("pinned_B")]
        public void FailedGetUp_FromDownNode_PlaysNoStandingClip(string node)
        {
            var steps = RingClipTable.Plan(GetUp(RollTier.Miss), new PlanContext { ActorNode = node });
            Assert.AreEqual(0, steps.Count, "лежачий лишається лежати: стійка без підйому — телепорт");
        }

        [Test]
        public void FailedGetUp_WhenStanding_StillBreathes()
        {
            var steps = RingClipTable.Plan(GetUp(RollTier.Miss), new PlanContext { ActorNode = "stance" });
            Assert.AreEqual(1, steps.Count);
            Assert.AreEqual("Idle_Tired_Loop", steps[0].Clip);
            steps = RingClipTable.Plan(GetUp(RollTier.Miss));
            Assert.AreEqual(1, steps.Count, "вузол невідомий — стійка");
        }

        [Test]
        public void SuccessfulGetUp_StillStandsUpByClip()
        {
            var steps = RingClipTable.Plan(GetUp(RollTier.Partial), new PlanContext { ActorNode = "lying_back" });
            Assert.AreEqual(1, steps.Count);
            Assert.AreEqual("GetUp_Back", steps[0].Clip, "у стійку — лише кліпом підйому");
        }

        [Test]
        public void StaysDown_Nodes()
        {
            Assert.IsTrue(RingClipTable.StaysDown("lying_back"));
            Assert.IsTrue(RingClipTable.StaysDown("kneel_up"));
            Assert.IsTrue(RingClipTable.StaysDown("pinned_A"));
            Assert.IsFalse(RingClipTable.StaysDown("stance"));
            Assert.IsFalse(RingClipTable.StaysDown(null));
        }

        [Test]
        public void ChainShift_OnlyForRealJumps()
        {
            Assert.IsFalse(RingClipTable.ChainNeedsShift(0.03f), "дихання петлі не лікуємо");
            Assert.IsTrue(RingClipTable.ChainNeedsShift(0.83f), "стрибок Cover_Release_A");
            Assert.IsFalse(RingClipTable.ChainNeedsShift(3f), "це вже інша розстановка");
        }
    }
}
