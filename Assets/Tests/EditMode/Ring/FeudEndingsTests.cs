using System.Linq;
using Game.Core.Ring;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>Кінцівки ворожнечі (gdd §5.3) і перебір їхньої досяжності (gdd §4.2 п.4).</summary>
    public class FeudEndingsTests
    {
        private static FeudEnding Play(BookerPlan plan, RingPath dominant, MatchResult r)
        {
            var st = RingTestData.Script().NewState();
            st.Plan = plan;
            st.AddPath(dominant, 3); // шляхи після матчу майже не зсуваються (±1) — домінанта лишається
            var outcome = st.ApplyMatchResult(r);
            return FeudEndings.Resolve(st, r, outcome);
        }

        [Test]
        public void OwnScript_PlanSwerved_AndWon()
        {
            Assert.AreEqual(FeudEnding.OwnScript, Play(BookerPlan.LoseClean, RingPath.Face, RingTestData.Result(MatchWinner.Player)));
        }

        [Test]
        public void Outcast_PlanSwerved_AndLostOrDrew()
        {
            Assert.AreEqual(FeudEnding.Outcast, Play(BookerPlan.WinClean, RingPath.Face, RingTestData.Result(MatchWinner.Rival)));
            Assert.AreEqual(FeudEnding.Outcast, Play(BookerPlan.WinAny, RingPath.Heel, RingTestData.Result(MatchWinner.Rival, hype: 9)));
        }

        [Test]
        public void CrowdFavorite_Won_DominantFace()
        {
            Assert.AreEqual(FeudEnding.CrowdFavorite, Play(BookerPlan.None, RingPath.Face, RingTestData.Result(MatchWinner.Player)));
        }

        [Test]
        public void HatedHeel_Won_DominantHeelOrRebel()
        {
            Assert.AreEqual(FeudEnding.HatedHeel, Play(BookerPlan.None, RingPath.Heel, RingTestData.Result(MatchWinner.Player)));
            Assert.AreEqual(FeudEnding.HatedHeel, Play(BookerPlan.None, RingPath.Rebel, RingTestData.Result(MatchWinner.Player)));
        }

        [Test]
        public void HonorableLoss_LostOrDrew_WithHype7()
        {
            Assert.AreEqual(FeudEnding.HonorableLoss, Play(BookerPlan.None, RingPath.Face, RingTestData.Result(MatchWinner.Rival, hype: 7)));
            Assert.AreEqual(FeudEnding.HonorableLoss, Play(BookerPlan.None, RingPath.Heel, RingTestData.Result(MatchWinner.Draw, MatchEndKind.TimeLimit, hype: 9)));
        }

        [Test]
        public void Forgotten_Otherwise()
        {
            Assert.AreEqual(FeudEnding.Forgotten, Play(BookerPlan.None, RingPath.Face, RingTestData.Result(MatchWinner.Rival, hype: 6)));
            Assert.AreEqual(FeudEnding.Forgotten, Play(BookerPlan.None, RingPath.Face, RingTestData.Result(MatchWinner.Draw, MatchEndKind.TimeLimit, hype: 2)));
        }

        [Test]
        public void Resolve_Priority_SwerveBeatsHonorableAndFavorite()
        {
            // перемога звернула план, ажіотаж високий і шлях Герой — все одно «Свій сценарій»
            Assert.AreEqual(FeudEnding.OwnScript, Play(BookerPlan.LoseClean, RingPath.Face, RingTestData.Result(MatchWinner.Player, hype: 9)));
            // поразка при високому ажіотажі, але план звернуто — «Вигнанець», не «Гідна поразка»
            Assert.AreEqual(FeudEnding.Outcast, Play(BookerPlan.WinAny, RingPath.Face, RingTestData.Result(MatchWinner.Rival, hype: 9)));
        }

        [Test]
        public void EveryEnding_HasATitleKey()
        {
            Assert.AreEqual(6, FeudEndings.All.Length);
            Assert.AreEqual(6, FeudEndings.All.Select(FeudEndings.TitleKey).Distinct().Count());
        }

        // ---------- досяжність ----------

        [Test]
        public void Reachability_TestGraph_ReachesAllSixEndings_NoDeadEnds()
        {
            var rep = FeudReachability.Analyze(RingTestData.Script());
            Assert.IsFalse(rep.Truncated);
            Assert.IsFalse(rep.HasDeadEnds, string.Join("; ", rep.DeadEnds));
            Assert.IsEmpty(rep.Unreachable().Select(e => e.ToString()).ToArray());
            Assert.AreEqual(6, rep.Reached.Count);
            Assert.Greater(rep.FinalStates, 1);
        }

        private const string TinyJson = @"{
 'id':'tiny','title':'t',
 'player':{'id':'player','name':'p','attr':{}},
 'rival':{'id':'r','name':'r','attr':{}},
 'promos':[{'id':'p','start':'c','nodes':[
   {'id':'c','type':'choice','options':[{'id':'o','text':'o','effects':[{'path':'face','add':1}],'next':'e'}]},
   {'id':'e','type':'end'}]}]}";

        [Test]
        public void Reachability_GraphWithoutPlans_ReportsUnreachableEndings()
        {
            var rep = FeudReachability.Analyze(FeudScriptLoader.Load(TinyJson.Replace('\'', '"')));
            Assert.IsFalse(rep.HasDeadEnds);
            var un = rep.Unreachable();
            CollectionAssert.Contains(un, FeudEnding.OwnScript, "плану немає — звернути нічого");
            CollectionAssert.Contains(un, FeudEnding.Outcast);
            CollectionAssert.Contains(un, FeudEnding.HatedHeel, "один бал Героя не перебити брудними прийомами");
            Assert.IsTrue(rep.Reached.Contains(FeudEnding.CrowdFavorite));
            Assert.IsTrue(rep.Reached.Contains(FeudEnding.HonorableLoss));
            Assert.IsTrue(rep.Reached.Contains(FeudEnding.Forgotten));
        }

        [Test]
        public void Reachability_DeadEnd_IsReported()
        {
            var json = TinyJson.Replace("'options':[{'id':'o'", "'options':[{'id':'o','requires':[{'flag':'never'}]");
            var s = FeudScriptLoader.Load(json.Replace('\'', '"'));
            var rep = FeudReachability.Analyze(s);
            Assert.IsTrue(rep.HasDeadEnds);
            StringAssert.Contains("p/c", rep.DeadEnds[0]);
            Assert.AreEqual(0, rep.Reached.Count);
        }

        [Test]
        public void Reachability_StateLimit_TruncatesInsteadOfBlowingUp()
        {
            var rep = FeudReachability.Analyze(RingTestData.Script(), 5);
            Assert.IsTrue(rep.Truncated);
        }
    }
}
