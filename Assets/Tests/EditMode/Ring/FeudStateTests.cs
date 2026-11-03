using System.Collections.Generic;
using Game.Core.Ring;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>Спільні тестові дані ворожнечі: маленький сценарій із двох промо й рядки до нього.</summary>
    internal static class RingTestData
    {
        // Лапки — одинарні, щоб не екранувати; перед розбором міняються на подвійні.
        public const string FeudJson = @"{
 'id':'feud_t','title':'feud.title',
 'player':{'id':'player','name':'w.player.name','finisher':'strength','attr':{'strength':1,'agility':1,'technique':1,'stamina':1,'charisma':0}},
 'rival':{'id':'hammer','name':'w.rival.name','finisher':'strength','startMomentum':2,'attr':{'strength':2,'agility':0,'technique':1,'stamina':2,'charisma':1}},
 'start':{'audience':3,'heat':0,'momentum':0,'trust':2,'locker':0},
 'promos':[
  {'id':'p1','act':1,'title':'p1.title','place':'p1.place','start':'n1','nodes':[
    {'id':'n1','type':'line','speaker':'rival','text':'k.n1','next':'n2'},
    {'id':'n2','type':'choice','options':[
      {'id':'respect','text':'k.respect','path':'face','effects':[{'path':'face','add':1}],'next':'n3'},
      {'id':'joke','text':'k.joke','requires':[{'attr':'charisma','min':1}],'effects':[{'var':'audience','add':1}],'next':'n3'},
      {'id':'push','text':'k.push','path':'heel','effects':[{'close':'hands'},{'path':'heel','add':1},{'flag':'pushed'}],'next':'n3'},
      {'id':'secret','text':'k.secret','hidden':true,'requires':[{'flag':'secret'}],'effects':[{'flag':'x'}],'next':'n3'}]},
    {'id':'n3','type':'check','attr':'charisma','mod':0,'pick10':2,'pick7':1,
     'rewards':[{'id':'aud','text':'r.aud','effects':[{'var':'audience','add':1}]},
                {'id':'mom','text':'r.mom','effects':[{'var':'momentum','add':2}]},
                {'id':'heat','text':'r.heat','effects':[{'var':'heat','add':1}]}],
     'fail':[{'var':'momentum','add':-1}],'next':'n4','next6':'nf'},
    {'id':'nf','type':'line','speaker':'narrator','text':'k.nf','next':'n4'},
    {'id':'n4','type':'choice','options':[
      {'id':'hands','text':'k.hands','requires':[{'open':'hands'}],'effects':[{'var':'trust','add':1}],'next':'end'},
      {'id':'deal','text':'k.deal','effects':[{'var':'heat','add':1}],'next':'end'}]},
    {'id':'end','type':'end'}]},
  {'id':'p2','act':2,'title':'p2.title','place':'p2.place','start':'m1','nodes':[
    {'id':'m1','type':'choice','options':[
      {'id':'a','text':'k.a','effects':[{'plan':'win_clean'},{'ally':true}],'next':'end'},
      {'id':'b','text':'k.b','effects':[{'plan':'lose_clean'}],'next':'end'},
      {'id':'c','text':'k.c','effects':[{'plan':'win_any'},{'stip':'no_dq'}],'next':'end'}]},
    {'id':'end','type':'end'}]}]}";

        public static FeudScript Script() => FeudScriptLoader.Load(FeudJson.Replace('\'', '"'));

        public static Dictionary<string, string> Strings()
        {
            var d = new Dictionary<string, string>();
            foreach (var k in new[]
            {
                "feud.title", "w.player.name", "w.rival.name", "p1.title", "p1.place", "p2.title", "p2.place",
                "speaker.rival", "speaker.narrator", "k.n1", "k.respect", "k.joke", "k.push", "k.secret", "r.aud", "r.mom",
                "r.heat", "k.nf", "k.hands", "k.deal", "k.a", "k.b", "k.c"
            }) d[k] = "текст " + k;
            return d;
        }

        public static MatchResult Result(MatchWinner w = MatchWinner.Player, MatchEndKind kind = MatchEndKind.Pin, int hype = 5,
                                         int dirty = 0, int sells = 0, int kick = 0)
        {
            return new MatchResult { Winner = w, Kind = kind, FinalHype = hype, PlayerDirtyMoves = dirty, PlayerSells = sells, PlayerKickoutsAtTwo = kick, Rounds = 8 };
        }
    }

    public class FeudStateTests
    {
        private static FeudState Fresh() => RingTestData.Script().NewState();

        [Test]
        public void Script_StartValuesAndPeople_AreLoaded()
        {
            var s = RingTestData.Script();
            var st = s.NewState();
            Assert.AreEqual(3, st.Audience);
            Assert.AreEqual(2, st.Trust);
            Assert.AreEqual(0, st.Heat);
            Assert.AreEqual(1, st.Player.Get(RingAttr.Strength));
            Assert.AreEqual(0, st.Player.Get(RingAttr.Charisma));
            Assert.AreEqual(2, s.Rival.Get(RingAttr.Stamina));
            Assert.AreEqual(2, s.RivalStartMomentum);
        }

        [TestCase(FeudVars.Audience, 0, 10)]
        [TestCase(FeudVars.Heat, 0, 4)]
        [TestCase(FeudVars.Momentum, 0, 5)]
        [TestCase(FeudVars.Trust, 0, 5)]
        [TestCase(FeudVars.Locker, -2, 2)]
        public void Vars_AreClampedToTheirRange(string name, int min, int max)
        {
            var st = Fresh();
            st.AddVar(name, 100);
            Assert.AreEqual(max, st.GetVar(name));
            st.AddVar(name, -100);
            Assert.AreEqual(min, st.GetVar(name));
        }

        [Test]
        public void PathPoints_NeverGoBelowZero()
        {
            var st = Fresh();
            st.AddPath(RingPath.Face, -3);
            Assert.AreEqual(0, st.GetPath(RingPath.Face));
        }

        [Test]
        public void DominantPath_TieBreak_FaceThenHeelThenRebel()
        {
            var st = Fresh();
            Assert.AreEqual(RingPath.Face, st.DominantPath());
            st.AddPath(RingPath.Rebel, 1);
            Assert.AreEqual(RingPath.Rebel, st.DominantPath());
            st.AddPath(RingPath.Heel, 1);
            Assert.AreEqual(RingPath.Heel, st.DominantPath());
            st.AddPath(RingPath.Face, 1);
            Assert.AreEqual(RingPath.Face, st.DominantPath());
            Assert.AreEqual(RingPath.Face, st.DominantPath(), "нічия трьох — Герой");
            st.AddPath(RingPath.Rebel, 1);
            Assert.AreEqual(RingPath.Rebel, st.DominantPath());
        }

        [Test]
        public void Conditions_EachKind_OpenAndCloseOptions()
        {
            var st = Fresh();
            Assert.IsFalse(st.Check(new FeudCondition { Kind = ConditionKind.Flag, Key = "f" }));
            Assert.IsTrue(st.Check(new FeudCondition { Kind = ConditionKind.NotFlag, Key = "f" }));
            st.Apply(new FeudEffect { Kind = EffectKind.Flag, Key = "f" });
            Assert.IsTrue(st.Check(new FeudCondition { Kind = ConditionKind.Flag, Key = "f" }));
            Assert.IsFalse(st.Check(new FeudCondition { Kind = ConditionKind.NotFlag, Key = "f" }));

            Assert.IsTrue(st.Check(new FeudCondition { Kind = ConditionKind.Open, Key = "b" }));
            st.Apply(new FeudEffect { Kind = EffectKind.Close, Key = "b" });
            Assert.IsFalse(st.Check(new FeudCondition { Kind = ConditionKind.Open, Key = "b" }));
            Assert.IsTrue(st.HasFlag("closed.b"));

            Assert.IsFalse(st.Check(new FeudCondition { Kind = ConditionKind.Var, Key = "heat", Min = 2 }));
            st.AddVar("heat", 2);
            Assert.IsTrue(st.Check(new FeudCondition { Kind = ConditionKind.Var, Key = "heat", Min = 2 }));

            Assert.IsFalse(st.Check(new FeudCondition { Kind = ConditionKind.Attr, Key = "charisma", Min = 1 }));
            st.Player.Set(RingAttr.Charisma, 1);
            Assert.IsTrue(st.Check(new FeudCondition { Kind = ConditionKind.Attr, Key = "charisma", Min = 1 }));

            Assert.IsFalse(st.Check(new FeudCondition { Kind = ConditionKind.Path, Key = "heel", Min = 2 }));
            st.AddPath(RingPath.Heel, 2);
            Assert.IsTrue(st.Check(new FeudCondition { Kind = ConditionKind.Path, Key = "heel", Min = 2 }));
        }

        [Test]
        public void Effects_StipPlanAllyAndVars()
        {
            var st = Fresh();
            st.Apply(new FeudEffect { Kind = EffectKind.Stip, Key = "no_dq" });
            st.Apply(new FeudEffect { Kind = EffectKind.Stip, Key = "no_ally" });
            Assert.AreEqual(MatchStipulations.NoDq | MatchStipulations.NoAlly, st.Stipulations);
            st.Apply(new FeudEffect { Kind = EffectKind.Plan, Key = "lose_clean" });
            Assert.AreEqual(BookerPlan.LoseClean, st.Plan);
            st.Apply(new FeudEffect { Kind = EffectKind.Plan, Key = "win_any" });
            Assert.AreEqual(BookerPlan.WinAny, st.Plan, "новий план замінює старий");
            st.Apply(new FeudEffect { Kind = EffectKind.Ally, Value = true });
            Assert.IsTrue(st.HasAlly);
            st.Apply(new FeudEffect { Kind = EffectKind.Var, Key = "momentum", Amount = 9 });
            Assert.AreEqual(5, st.Momentum);
        }

        [Test]
        public void Loader_RejectsUnknownNamesAndTypes()
        {
            Assert.Throws<System.FormatException>(() => FeudScriptLoader.Load(RingTestData.FeudJson.Replace('\'', '"').Replace("\"var\":\"heat\"", "\"var\":\"fame\"")));
            Assert.Throws<System.FormatException>(() => FeudScriptLoader.Load(RingTestData.FeudJson.Replace('\'', '"').Replace("\"type\":\"end\"", "\"type\":\"boom\"")));
            Assert.Throws<MiniJsonException>(() => FeudScriptLoader.Load("{"));
        }

        [Test]
        public void SaveRestore_IntoFreshObject_GivesEqualState()
        {
            var st = Fresh();
            st.AddVar("audience", 4);
            st.AddVar("locker", -2);
            st.AddPath(RingPath.Heel, 3);
            st.Apply(new FeudEffect { Kind = EffectKind.Close, Key = "a b,c|d=e" });
            st.Apply(new FeudEffect { Kind = EffectKind.Flag, Key = "swerve_won" });
            st.Apply(new FeudEffect { Kind = EffectKind.Plan, Key = "win_clean" });
            st.Apply(new FeudEffect { Kind = EffectKind.Stip, Key = "no_dq" });
            st.Apply(new FeudEffect { Kind = EffectKind.Ally, Value = true });
            st.DevPoints = 3;
            st.Perks.Add(PerkCatalog.IronJaw);
            st.Player.Set(RingAttr.Technique, 3);

            string blob = st.CaptureState();
            var fresh = new FeudState();
            fresh.RestoreState(blob);

            Assert.AreEqual(blob, fresh.CaptureState());
            Assert.AreEqual(7, fresh.Audience);
            Assert.AreEqual(-2, fresh.Locker);
            Assert.AreEqual(3, fresh.GetPath(RingPath.Heel));
            Assert.IsTrue(fresh.IsClosed("a b,c|d=e"));
            Assert.IsTrue(fresh.HasFlag("swerve_won"));
            Assert.AreEqual(BookerPlan.WinClean, fresh.Plan);
            Assert.AreEqual(MatchStipulations.NoDq, fresh.Stipulations);
            Assert.IsTrue(fresh.HasAlly);
            Assert.AreEqual(3, fresh.DevPoints);
            Assert.IsTrue(fresh.Perks.Contains(PerkCatalog.IronJaw));
            Assert.AreEqual(3, fresh.Player.Get(RingAttr.Technique));
            Assert.AreEqual("player", fresh.Player.Id);
        }

        [Test]
        public void Restore_RejectsGarbage()
        {
            Assert.Throws<System.ArgumentException>(() => new FeudState().RestoreState(""));
            Assert.Throws<System.FormatException>(() => new FeudState().RestoreState("v9|a=1"));
        }

        // ---------- матч → ворожнеча (gdd §3) ----------

        [Test]
        public void ApplyMatch_Hype7OrMore_AudiencePlusOne_Hype3OrLess_MinusOne_Between_NoChange()
        {
            var st = Fresh();
            st.ApplyMatchResult(RingTestData.Result(hype: 7));
            Assert.AreEqual(4, st.Audience);
            st.ApplyMatchResult(RingTestData.Result(hype: 3));
            Assert.AreEqual(3, st.Audience);
            st.ApplyMatchResult(RingTestData.Result(hype: 5));
            Assert.AreEqual(3, st.Audience);
        }

        [Test]
        public void ApplyMatch_PlanFulfilled_TrustPlusOne()
        {
            var st = Fresh();
            st.Plan = BookerPlan.WinClean;
            Assert.AreEqual(FeudPlanOutcome.Fulfilled, st.ApplyMatchResult(RingTestData.Result()));
            Assert.AreEqual(3, st.Trust);
        }

        [Test]
        public void ApplyMatch_PlanSwerved_HighHype_AudienceAndFlag_LowHype_TrustMinusTwo_AndRebelPlusOne()
        {
            var good = Fresh();
            good.Plan = BookerPlan.LoseClean;
            var o = good.ApplyMatchResult(RingTestData.Result(MatchWinner.Player, hype: 8));
            Assert.AreEqual(FeudPlanOutcome.Swerved, o);
            Assert.AreEqual(5, good.Audience, "+1 за ажіотаж і +1 за вдалий звіт");
            Assert.IsTrue(good.HasFlag("swerve_won"));
            Assert.AreEqual(2, good.Trust);
            Assert.AreEqual(1, good.GetPath(RingPath.Rebel));

            var bad = Fresh();
            bad.Plan = BookerPlan.LoseClean;
            bad.ApplyMatchResult(RingTestData.Result(MatchWinner.Player, hype: 5));
            Assert.AreEqual(0, bad.Trust);
            Assert.IsFalse(bad.HasFlag("swerve_won"));
            Assert.AreEqual(1, bad.GetPath(RingPath.Rebel));
        }

        [Test]
        public void ApplyMatch_DirtyAndSells_MoveHeelAndFacePaths()
        {
            var st = Fresh();
            st.ApplyMatchResult(RingTestData.Result(dirty: 2));
            Assert.AreEqual(1, st.GetPath(RingPath.Heel));
            Assert.AreEqual(0, st.GetPath(RingPath.Face));
            st.ApplyMatchResult(RingTestData.Result(dirty: 1, sells: 2));
            Assert.AreEqual(1, st.GetPath(RingPath.Heel), "один брудний — не рахується");
            Assert.AreEqual(1, st.GetPath(RingPath.Face));
        }

        [Test]
        public void ApplyMatch_AwardsDevelopmentPoints()
        {
            var st = Fresh();
            st.ApplyMatchResult(RingTestData.Result(MatchWinner.Rival, hype: 4));
            Assert.AreEqual(1, st.DevPoints);
        }

        [TestCase(BookerPlan.None, MatchWinner.Player, MatchEndKind.Pin, 0, 0, FeudPlanOutcome.Neither)]
        [TestCase(BookerPlan.WinClean, MatchWinner.Player, MatchEndKind.Pin, 0, 0, FeudPlanOutcome.Fulfilled)]
        [TestCase(BookerPlan.WinClean, MatchWinner.Player, MatchEndKind.Submission, 0, 0, FeudPlanOutcome.Fulfilled)]
        [TestCase(BookerPlan.WinClean, MatchWinner.Player, MatchEndKind.Pin, 1, 0, FeudPlanOutcome.Neither)]
        [TestCase(BookerPlan.WinClean, MatchWinner.Player, MatchEndKind.Disqualification, 0, 0, FeudPlanOutcome.Neither)]
        [TestCase(BookerPlan.WinClean, MatchWinner.Rival, MatchEndKind.Pin, 0, 0, FeudPlanOutcome.Swerved)]
        [TestCase(BookerPlan.WinClean, MatchWinner.Draw, MatchEndKind.TimeLimit, 0, 0, FeudPlanOutcome.Neither)]
        [TestCase(BookerPlan.LoseClean, MatchWinner.Rival, MatchEndKind.Pin, 0, 1, FeudPlanOutcome.Fulfilled)]
        [TestCase(BookerPlan.LoseClean, MatchWinner.Rival, MatchEndKind.Pin, 0, 0, FeudPlanOutcome.Neither)]
        [TestCase(BookerPlan.LoseClean, MatchWinner.Player, MatchEndKind.Pin, 0, 1, FeudPlanOutcome.Swerved)]
        [TestCase(BookerPlan.WinAny, MatchWinner.Player, MatchEndKind.Disqualification, 3, 0, FeudPlanOutcome.Fulfilled)]
        [TestCase(BookerPlan.WinAny, MatchWinner.Rival, MatchEndKind.Pin, 0, 0, FeudPlanOutcome.Swerved)]
        [TestCase(BookerPlan.WinAny, MatchWinner.Draw, MatchEndKind.TimeLimit, 0, 0, FeudPlanOutcome.Neither)]
        [TestCase(BookerPlan.NoCleanLoss, MatchWinner.Rival, MatchEndKind.Pin, 0, 0, FeudPlanOutcome.Swerved)]
        [TestCase(BookerPlan.NoCleanLoss, MatchWinner.Rival, MatchEndKind.Submission, 0, 0, FeudPlanOutcome.Swerved)]
        [TestCase(BookerPlan.NoCleanLoss, MatchWinner.Rival, MatchEndKind.Disqualification, 0, 0, FeudPlanOutcome.Fulfilled)]
        [TestCase(BookerPlan.NoCleanLoss, MatchWinner.Rival, MatchEndKind.CountOut, 0, 0, FeudPlanOutcome.Fulfilled)]
        [TestCase(BookerPlan.NoCleanLoss, MatchWinner.Player, MatchEndKind.Pin, 0, 0, FeudPlanOutcome.Fulfilled)]
        [TestCase(BookerPlan.NoCleanLoss, MatchWinner.Draw, MatchEndKind.TimeLimit, 0, 0, FeudPlanOutcome.Fulfilled)]
        [TestCase(BookerPlan.NoCleanLoss, MatchWinner.Draw, MatchEndKind.DoubleCountOut, 0, 0, FeudPlanOutcome.Fulfilled)]
        public void EvaluatePlan_Table(BookerPlan plan, MatchWinner w, MatchEndKind kind, int dirty, int kick, FeudPlanOutcome expected)
        {
            var st = Fresh();
            st.Plan = plan;
            Assert.AreEqual(expected, st.EvaluatePlan(RingTestData.Result(w, kind, 5, dirty, 0, kick)));
        }

        [Test]
        public void ResetHeatAfterFeud_SetsOne()
        {
            var st = Fresh();
            st.Heat = 4;
            st.ResetHeatAfterFeud();
            Assert.AreEqual(1, st.Heat);
        }

        // ---------- збирання матчу ----------

        [Test]
        public void BuildMatchSetup_UsesFeudStateAndScript()
        {
            var script = RingTestData.Script();
            var st = script.NewState();
            st.Heat = 3;
            st.Momentum = 4;
            st.HasAlly = true;
            st.Plan = BookerPlan.LoseClean;
            st.Stipulations = MatchStipulations.NoDq;
            st.AddPath(RingPath.Heel, 2);

            var setup = st.BuildMatchSetup(script);
            Assert.AreEqual(4, setup.StartHype, "1 + heat");
            Assert.AreEqual(4, setup.PlayerStartMomentum);
            Assert.AreEqual(2, setup.RivalStartMomentum);
            Assert.IsTrue(setup.PlayerHasAlly);
            Assert.AreEqual(BookerPlan.LoseClean, setup.Plan);
            Assert.AreEqual(MatchStipulations.NoDq, setup.Stipulations);
            Assert.AreEqual(RingPath.Heel, setup.PlayerRole);
            Assert.AreEqual("hammer", setup.Rival.Id);
            Assert.AreNotSame(st.Player, setup.Player, "матч отримує копію атрибутів");
        }

        [Test]
        public void BuildMatchSetup_AddsPerkBonusesToStartHypeAndMomentum()
        {
            var script = RingTestData.Script();
            var st = script.NewState();
            st.Heat = 4;
            st.Momentum = 5;
            st.Perks.Add(PerkCatalog.CrowdFavorite);
            st.Perks.Add(PerkCatalog.ColdBlood);
            var setup = st.BuildMatchSetup(script);
            Assert.AreEqual(7, setup.StartHype);
            Assert.AreEqual(7, setup.PlayerStartMomentum);
            st.Perks.Clear();
            Assert.AreEqual(5, st.BuildMatchSetup(script).StartHype);
        }
    }
}
