using System.IO;
using System.Linq;
using Game.Core.Randomness;
using Game.Core.Ring;
using Game.Core.Ring.Match;
using Game.Gameplay.Combat;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Ворожнеча з трьох матчів (docs/ring/gdd.md §1, §3, §5.2, §6): дані (matches, flow, branch), стан між матчами (Heat, прапори
    /// результату, план і умови на кожен матч, популярність ×2 у розв'язці), фасад кампанії (порядок кроків, розвиток після
    /// кожного матчу, сейв на кожній межі).
    /// </summary>
    public class FeudThreeMatchesTests
    {
        private static MatchDef Def(string id, bool decisive = false, params PlanRule[] rules)
        {
            var d = new MatchDef { Id = id, Decisive = decisive };
            d.Plans.AddRange(rules);
            return d;
        }

        // ---------- дані: схема, завантажувач, валідатор ----------

        private const string ThreeJson = @"{
 'id':'f','title':'feud.title',
 'player':{'id':'player','name':'w.player.name','attr':{}},
 'rival':{'id':'r','name':'w.rival.name','startMomentum':0,'attr':{}},
 'promos':[
  {'id':'a','act':1,'start':'n1','nodes':[
    {'id':'n1','type':'branch','cases':[{'requires':[{'flag':'m1_won'}],'next':'w'}],'next':'x'},
    {'id':'w','type':'line','speaker':'rival','text':'k.w','next':'end'},
    {'id':'x','type':'line','speaker':'rival','text':'k.x','next':'end'},
    {'id':'end','type':'end'}]},
  {'id':'b','act':2,'start':'n1','nodes':[
    {'id':'n1','type':'line','speaker':'rival','text':'k.w','next':'end'},{'id':'end','type':'end'}]}],
 'matches':[
  {'id':'m1','act':1,'plan':[{'requires':[{'var':'trust','min':3}],'plan':'win_any'},{'plan':'lose_clean'}],'stipulations':['no_dq']},
  {'id':'m2','act':2,'decisive':true}],
 'flow':['a','m1','b','m2']}";

        private static FeudScript Three(string json = null) => FeudScriptLoader.Load((json ?? ThreeJson).Replace('\'', '"'));

        private static System.Collections.Generic.Dictionary<string, string> Strings()
        {
            var d = RingTestData.Strings();
            d["k.w"] = "w";
            d["k.x"] = "x";
            return d;
        }

        [Test]
        public void Loader_ReadsMatchesAndFlow_PlansAndStipulations()
        {
            var s = Three();
            Assert.AreEqual(2, s.Matches.Count);
            Assert.AreEqual(4, s.Flow.Count);
            CollectionAssert.AreEqual(new[] { "a", "m1", "b", "m2" }, s.Flow.Select(f => f.Id).ToArray());
            CollectionAssert.AreEqual(new[] { false, true, false, true }, s.Flow.Select(f => f.IsMatch).ToArray());
            Assert.IsFalse(s.Matches[0].Decisive);
            Assert.IsTrue(s.Matches[1].Decisive);
            Assert.AreEqual(MatchStipulations.NoDq, s.Matches[0].Stipulations);
            Assert.AreEqual(2, s.Matches[0].Plans.Count);
            Assert.AreEqual(BookerPlan.WinAny, s.Matches[0].Plans[0].Plan);
            Assert.AreEqual(PromoNodeType.Branch, s.Promos[0].Find("n1").Type);
            Assert.IsEmpty(PromoValidator.Validate(s, Strings()), "валідний сценарій");
        }

        [Test]
        public void Loader_WithoutFlow_KeepsTheOldOrder_OnePromoSeriesThenOneDecisiveMatch()
        {
            var s = RingTestData.Script();
            CollectionAssert.AreEqual(new[] { "p1", "p2", "m1" }, s.Flow.Select(f => f.Id).ToArray());
            Assert.AreEqual(1, s.Matches.Count);
            Assert.IsTrue(s.Matches[0].Decisive);
            Assert.IsEmpty(PromoValidator.Validate(s, RingTestData.Strings()));
        }

        [Test]
        public void Loader_RejectsBrokenFlow()
        {
            Assert.Throws<System.FormatException>(() => Three(ThreeJson.Replace("'flow':['a','m1','b','m2']", "'flow':['a','zzz']")));
            Assert.Throws<System.FormatException>(() => Three(ThreeJson.Replace("'flow':['a','m1','b','m2']", "'flow':'a'")));
            // Кілька матчів без flow — порядок невідомий.
            // Переноси — як у робочій копії: на Windows з autocrlf=true рядок має \r\n, і пошук «,\n» нічого не знаходив.
            var noFlow = ThreeJson.Replace("\r\n", "\n").Replace(",\n 'flow':['a','m1','b','m2']", "");
            Assert.Throws<System.FormatException>(() => Three(noFlow));
            Assert.Throws<System.FormatException>(() => Three(ThreeJson.Replace("'plan':'win_any'", "'plan':'nope'")));
            Assert.Throws<System.FormatException>(() => Three(ThreeJson.Replace("'no_dq'", "'x'")));
        }

        [Test]
        public void Validator_ReportsFlowErrors()
        {
            Assert.AreEqual(0, Errs(s => { }, PromoErrorCode.BadFlow), "валідний порядок");
            Assert.GreaterOrEqual(Errs(s => s.Flow.RemoveAt(2), PromoErrorCode.BadFlow), 1, "промо b поза порядком");
            Assert.GreaterOrEqual(Errs(s => s.Flow.Reverse(), PromoErrorCode.BadFlow), 1, "перший крок — матч");
            Assert.AreEqual(1, Errs(s => s.Matches[0].Decisive = true, PromoErrorCode.BadFlow), "розв'язка не остання");
            Assert.AreEqual(1, Errs(s => s.Matches[1].Decisive = false, PromoErrorCode.BadFlow), "остання не розв'язка");
            Assert.GreaterOrEqual(Errs(s => s.Flow.Clear(), PromoErrorCode.BadFlow), 1, "порожній порядок");
            Assert.GreaterOrEqual(Errs(s => s.Flow.Add(s.Flow[0]), PromoErrorCode.BadFlow), 1, "крок двічі");
        }

        private static int Errs(System.Action<FeudScript> mutate, PromoErrorCode code)
        {
            var s = Three();
            mutate(s);
            return PromoValidator.Validate(s, Strings()).Count(e => e.Code == code);
        }

        [Test]
        public void Branch_GoesByConditions_WithoutAChoice_AndNextIsTheDefault()
        {
            var s = Three();
            var st = s.NewState();
            var r = new PromoRunner(s, s.Promos[0], st);
            Assert.AreEqual("x", r.Current.Id, "старт-branch проходиться сам: умова не виконана — next");
            st.Flags.Add("m1_won");
            r = new PromoRunner(s, s.Promos[0], st);
            Assert.AreEqual("w", r.Current.Id);
            r.Advance();
            Assert.IsTrue(r.IsFinished);
        }

        [Test]
        public void Validator_BranchWithMissingTarget_IsReported()
        {
            var s = Three();
            s.Promos[0].Find("n1").Cases[0].Next = "zzz";
            Assert.AreEqual(1, PromoValidator.Validate(s, Strings()).Count(e => e.Code == PromoErrorCode.MissingNext));
        }

        // ---------- стан: підготовка й підсумок матчу ----------

        [Test]
        public void PrepareMatch_PromoPlanBeatsTheDefault_StipulationsUnite_AndItIsIdempotent()
        {
            var s = Three();
            var st = s.NewState();
            st.Stipulations = MatchStipulations.NoAlly;
            st.PrepareMatch(s.Matches[0]);
            Assert.AreEqual(BookerPlan.LoseClean, st.Plan, "довіра 2 — друге правило");
            Assert.AreEqual(MatchStipulations.NoAlly | MatchStipulations.NoDq, st.Stipulations);
            st.PrepareMatch(s.Matches[0]);
            Assert.AreEqual(BookerPlan.LoseClean, st.Plan);

            var rich = s.NewState();
            rich.Trust = 3;
            rich.PrepareMatch(s.Matches[0]);
            Assert.AreEqual(BookerPlan.WinAny, rich.Plan, "довіра 3 — перше правило");

            var set = s.NewState();
            set.Plan = BookerPlan.WinClean;
            set.PrepareMatch(s.Matches[0]);
            Assert.AreEqual(BookerPlan.WinClean, set.Plan, "план із промо не перекривається");
        }

        [Test]
        public void CompleteMatch_SetsResultFlags_ClearsPlanAndStipulations_KeepsHeat()
        {
            var s = Three();
            var st = s.NewState();
            st.Heat = 3;
            st.Plan = BookerPlan.NoCleanLoss;
            st.Stipulations = MatchStipulations.NoDq;
            var o = st.CompleteMatch(RingTestData.Result(MatchWinner.Rival, MatchEndKind.Pin, 5), s.Matches[0]);
            Assert.AreEqual(FeudPlanOutcome.Swerved, o);
            Assert.IsTrue(st.HasFlag("m1_lost"));
            Assert.IsTrue(st.HasFlag("m1_plan_swerved"));
            Assert.IsFalse(st.HasFlag("m1_won") || st.HasFlag("m1_draw") || st.HasFlag("m1_dirty") || st.HasFlag("m1_plan_ok"));
            Assert.AreEqual(BookerPlan.None, st.Plan);
            Assert.AreEqual(MatchStipulations.None, st.Stipulations);
            Assert.AreEqual(3, st.Heat, "Heat тримається до розв'язки");
        }

        [TestCase(MatchWinner.Player, MatchEndKind.Pin, "m1_won", false)]
        [TestCase(MatchWinner.Player, MatchEndKind.Disqualification, "m1_won", true)]
        [TestCase(MatchWinner.Rival, MatchEndKind.CountOut, "m1_lost", true)]
        [TestCase(MatchWinner.Rival, MatchEndKind.Submission, "m1_lost", false)]
        [TestCase(MatchWinner.Draw, MatchEndKind.TimeLimit, "m1_draw", false)]
        [TestCase(MatchWinner.Draw, MatchEndKind.DoubleCountOut, "m1_draw", true)]
        public void CompleteMatch_FlagsWinnerAndDirtyEnding(MatchWinner w, MatchEndKind kind, string flag, bool dirty)
        {
            var s = Three();
            var st = s.NewState();
            st.CompleteMatch(RingTestData.Result(w, kind), s.Matches[0]);
            Assert.IsTrue(st.HasFlag(flag), flag);
            Assert.AreEqual(dirty, st.HasFlag("m1_dirty"));
        }

        [Test]
        public void CompleteMatch_PlanOk_Flag_AndTrustPlusOne()
        {
            var s = Three();
            var st = s.NewState();
            st.Plan = BookerPlan.NoCleanLoss;
            st.CompleteMatch(RingTestData.Result(MatchWinner.Rival, MatchEndKind.Disqualification), s.Matches[0]);
            Assert.IsTrue(st.HasFlag("m1_plan_ok"));
            Assert.AreEqual(3, st.Trust);
        }

        [Test]
        public void CompleteMatch_AudienceForTheDecisiveCountsTwice_DevelopmentPointsEveryMatch()
        {
            var s = Three();
            var mid = s.NewState();
            mid.Plan = BookerPlan.WinAny;
            mid.CompleteMatch(RingTestData.Result(MatchWinner.Player, MatchEndKind.Pin, 8), s.Matches[0]);
            Assert.AreEqual(4, mid.Audience, "проміжний: +1");
            Assert.AreEqual(4, mid.DevPoints, "1 + перемога + ажіотаж + план виконано");

            var fin = s.NewState();
            fin.CompleteMatch(RingTestData.Result(MatchWinner.Player, MatchEndKind.Pin, 8), s.Matches[1]);
            Assert.AreEqual(5, fin.Audience, "розв'язка: +2");
            var low = s.NewState();
            low.CompleteMatch(RingTestData.Result(MatchWinner.Rival, MatchEndKind.Pin, 2), s.Matches[1]);
            Assert.AreEqual(1, low.Audience, "розв'язка: −2");
        }

        // ---------- фасад: порядок кроків і розвиток після кожного матчу ----------

        [Test]
        public void Campaign_StagesFollowTheFlow_DevelopmentAfterEveryMatch_EndingOnlyAfterTheThird()
        {
            var c = RingCampaignDriver.NewCampaign(2);
            var log = new System.Collections.Generic.List<string>();
            while (true)
            {
                if (c.Stage == RingStage.Overview) log.Add("promo:" + c.CurrentStep.Id);
                else if (c.Stage == RingStage.MatchPrep) log.Add("match:" + c.CurrentStep.Id + "#" + (c.CurrentMatchIndex + 1) + "/" + c.MatchCount);
                else if (c.Stage == RingStage.Development && c.CanContinue) { log.Add("dev"); Assert.IsFalse(c.HasEnding, "кінцівки ще немає"); }
                else if (c.Stage == RingStage.Ending) log.Add("ending");
                if (!RingCampaignDriver.Step(c)) break;
            }
            CollectionAssert.AreEqual(new[]
            {
                "promo:p1_challenge", "match:m1#1/3", "dev", "promo:p2_booker", "promo:p3_parking", "match:m2#2/3", "dev",
                "promo:p4_contract", "match:m3#3/3", "ending"
            }, log.ToArray());
            Assert.IsTrue(c.HasEnding);
            Assert.IsFalse(c.CanContinue);
        }

        [Test]
        public void Campaign_HeatHoldsBetweenMatches_AndResetsOnlyAfterTheDecisive()
        {
            var c = RingCampaignDriver.NewCampaign(3);
            int[] heats = { 3, 4 };
            int k = 0;
            while (!c.HasEnding)
            {
                if (c.Stage == RingStage.MatchPrep && !c.CurrentMatchDef.Decisive)
                {
                    c.State.Heat = heats[k];
                    RingCampaignDriver.Step(c);   // зіграти матч
                    Assert.AreEqual(heats[k], c.State.Heat, "Heat не скидається після проміжного матчу " + (k + 1));
                    Assert.AreEqual(RingStage.Development, c.Stage);
                    k++;
                    continue;
                }
                RingCampaignDriver.Step(c);
            }
            Assert.AreEqual(2, k);
            Assert.AreEqual(1, c.State.Heat, "після розв'язки — 1");
        }

        [Test]
        public void Campaign_NextMatchStartsFromTheHeatOfTheFeud()
        {
            var c = RingCampaignDriver.NewCampaign(5);
            while (!(c.Stage == RingStage.MatchPrep && c.CurrentMatchIndex == 1)) RingCampaignDriver.Step(c);
            c.State.Heat = 4;
            var m = c.StartMatch();
            Assert.AreEqual(5, m.Setup.StartHype, "ажіотаж старту = 1 + Heat");
        }

        [Test]
        public void Campaign_ResultFlags_AfterEveryMatch_PointsGrow_AndPlansAreRecorded()
        {
            for (ulong seed = 1; seed <= 5; seed++)
            {
                var c = RingCampaignDriver.NewCampaign(seed);
                int matchesDone = 0, pointsBefore = 0;
                while (!c.HasEnding)
                {
                    if (c.Stage == RingStage.MatchPrep) pointsBefore = c.State.DevPoints;
                    int recs = c.Records.Count;
                    RingCampaignDriver.Step(c);
                    if (c.Records.Count == recs) continue;
                    matchesDone++;
                    var rec = c.Records[c.Records.Count - 1];
                    string id = "m" + matchesDone;
                    Assert.AreEqual(id, rec.MatchId);
                    int outcomes = new[] { "_won", "_lost", "_draw" }.Count(f => c.State.HasFlag(id + f));
                    Assert.AreEqual(1, outcomes, "рівно один прапор переможця для " + id + ", зерно " + seed);
                    Assert.AreEqual(rec.Result.Kind == MatchEndKind.Disqualification || rec.Result.Kind == MatchEndKind.CountOut
                        || rec.Result.Kind == MatchEndKind.DoubleCountOut, c.State.HasFlag(id + "_dirty"));
                    Assert.AreEqual(rec.PlanOutcome == FeudPlanOutcome.Fulfilled, c.State.HasFlag(id + "_plan_ok"));
                    Assert.AreEqual(rec.PlanOutcome == FeudPlanOutcome.Swerved, c.State.HasFlag(id + "_plan_swerved"));
                    Assert.Greater(c.State.DevPoints, pointsBefore, "очки розвитку нараховано після " + id);
                    Assert.AreEqual(BookerPlan.None, c.State.Plan, "план очищено після матчу");
                    Assert.AreEqual(MatchStipulations.None, c.State.Stipulations);
                }
                Assert.AreEqual(3, matchesDone);
            }
        }

        [Test]
        public void Campaign_PlansPerMatch_FromTheDataAndFromThePromos()
        {
            var c = RingCampaignDriver.NewCampaign(1);
            // Матч 1: промо його план не задають — за довірою з даних (старт 2 → «програй чисто»; 3 → «виграй будь-як»).
            while (c.Stage != RingStage.MatchPrep) RingCampaignDriver.Step(c);
            c.State.Trust = 2;
            Assert.AreEqual(BookerPlan.LoseClean, c.PreviewNextMatchState().Plan);
            c.State.Trust = 3;
            Assert.AreEqual(BookerPlan.WinAny, c.PreviewNextMatchState().Plan);
            Assert.AreEqual(BookerPlan.None, c.State.Plan, "перегляд стан не міняє");
            var m1 = c.StartMatch();
            Assert.AreEqual(BookerPlan.WinAny, m1.Setup.Plan);

            // Матч 2: без плану з промо — «не програй чисто».
            MatchAutoplay.Run(c.CurrentMatch, true);
            c.FinishMatch();
            Assert.AreEqual(RingStage.Development, c.Stage);
            c.ContinueAfterDevelopment();
            while (c.Stage != RingStage.MatchPrep)
            {
                if (c.Stage == RingStage.Promo && c.CurrentPromoRunner.IsFinished) { c.FinishPromo(); continue; }
                RingCampaignDriver.Step(c);
            }
            Assert.AreEqual(1, c.CurrentMatchIndex);
            Assert.IsTrue(c.State.Plan == BookerPlan.NoCleanLoss || c.State.HasFlag("plan_refused") || c.State.Plan != BookerPlan.None,
                "план другого матчу — з промо 2 або за замовчуванням");
            c.State.Plan = BookerPlan.None;
            c.State.Flags.Remove("plan_refused");
            var m2 = c.StartMatch();
            Assert.AreEqual(BookerPlan.NoCleanLoss, m2.Setup.Plan);
        }

        [Test]
        public void Campaign_Rematch_IsOnlyForTheDecisive_AndKeepsItsPlanAndStipulations()
        {
            var c = RingCampaignDriver.NewCampaign(4);
            while (!(c.Stage == RingStage.Development && c.CanContinue)) RingCampaignDriver.Step(c);
            Assert.IsFalse(c.CanRematch, "після проміжного матчу реваншу немає");
            Assert.Throws<System.InvalidOperationException>(() => c.StartRematch());
            RingCampaignDriver.ToFinalMatchPrep(c);
            var first = c.StartMatch();
            var plan = first.Setup.Plan;
            var stips = first.Setup.Stipulations;
            MatchAutoplay.Run(c.CurrentMatch, true);
            c.FinishMatch();
            c.OpenDevelopment();
            var again = c.StartRematch();
            Assert.AreEqual(plan, again.Setup.Plan);
            Assert.AreEqual(stips, again.Setup.Stipulations);
        }

        [Test]
        public void Campaign_ContinueGuards_AndSaveBetweenMatches_ComesBackToTheSameSpot()
        {
            var c = RingCampaignDriver.NewCampaign(6);
            Assert.Throws<System.InvalidOperationException>(() => c.ContinueAfterDevelopment(), "не з розвитку");
            while (!(c.Stage == RingStage.Development && c.CanContinue)) RingCampaignDriver.Step(c);
            int step = c.StepIndex;
            string blob = c.CaptureState();
            var fresh = RingCampaign.New(RingCampaignDriver.RealScript(), new SeededDiceRoller(99));
            fresh.RestoreState(blob);
            Assert.AreEqual(RingStage.Development, fresh.Stage);
            Assert.AreEqual(step, fresh.StepIndex);
            Assert.AreEqual(1, fresh.Records.Count);
            Assert.AreEqual(c.Records[0].Result.Winner, fresh.Records[0].Result.Winner);
            Assert.AreEqual(c.Records[0].PlanOutcome, fresh.Records[0].PlanOutcome);
            Assert.IsTrue(fresh.CanContinue);
            Assert.IsFalse(fresh.HasEnding);
            fresh.ContinueAfterDevelopment();
            Assert.AreEqual(RingStage.Overview, fresh.Stage);
            Assert.AreEqual(1, fresh.CurrentMatchIndex, "наступний матч — другий");
        }

        [Test]
        public void Campaign_OldSaveFormat_IsRejectedWithAClearError()
        {
            var c = RingCampaignDriver.NewCampaign(1);
            Assert.Throws<System.FormatException>(() => c.RestoreState("ring1|stage=0|promo=0"));
        }

        [Test]
        public void Campaign_LegacyScript_StillPlaysAsOnePromoSeriesAndOneMatch()
        {
            var c = RingCampaign.New(RingTestData.Script(), new SeededDiceRoller(1));
            RingCampaignDriver.PlayToDevelopment(c);
            Assert.AreEqual(1, c.Records.Count);
            Assert.IsTrue(c.HasEnding);
            Assert.AreEqual(1, c.MatchCount);
        }

        // ---------- справжні дані ----------

        private static FeudScript Real() => RingCampaignDriver.RealScript();

        [Test]
        public void RealScript_HasThreeMatchesInThreeActs_InTheFlowOrder()
        {
            var s = Real();
            CollectionAssert.AreEqual(new[] { "m1", "m2", "m3" }, s.Matches.Select(m => m.Id).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, s.Matches.Select(m => m.Act).ToArray());
            CollectionAssert.AreEqual(new[] { false, false, true }, s.Matches.Select(m => m.Decisive).ToArray());
            CollectionAssert.AreEqual(new[]
            {
                "p1_challenge", "m1", "p2_booker", "p3_parking", "m2", "p4_contract", "m3"
            }, s.Flow.Select(f => f.Id).ToArray());
        }

        [Test]
        public void RealScript_EveryResultFlagReadByThePromos_IsOneThatCompleteMatchSets()
        {
            var s = Real();
            var suffixes = new[] { "won", "lost", "draw", "dirty", "plan_ok", "plan_swerved" };
            var ids = s.Matches.Select(m => m.Id).ToList();
            var read = new System.Collections.Generic.HashSet<string>();
            foreach (var p in s.Promos)
                foreach (var n in p.Nodes)
                {
                    foreach (var c in n.Cases) foreach (var q in c.Requires) if (q.Kind == ConditionKind.Flag) read.Add(q.Key);
                    foreach (var o in n.Options) foreach (var q in o.Requires) if (q.Kind == ConditionKind.Flag) read.Add(q.Key);
                }
            foreach (var f in read.Where(f => System.Text.RegularExpressions.Regex.IsMatch(f, @"^m\d+_")))
            {
                int us = f.IndexOf('_');
                Assert.Contains(f.Substring(0, us), ids, "прапор " + f + ": немає такого матчу");
                Assert.Contains(f.Substring(us + 1), suffixes, "прапор " + f + ": результат такого прапора не ставить");
            }
            Assert.IsTrue(read.Contains("m1_won") && read.Contains("m2_dirty"), "промо 2 і 4 читають результати матчів 1 і 2");
        }

        [Test]
        public void RealScript_PromoTwoAndFour_ReadThePreviousMatch_EveryBranchIsPlayable()
        {
            var s = Real();
            // Промо 2 після кожного з чотирьох результатів матчу 1 доходить до кінця; промо 4 — після п'яти результатів матчу 2.
            foreach (var flag in new[] { "m1_plan_swerved", "m1_won", "m1_lost", null })
                AssertPromoFinishes(s, "p2_booker", flag);
            foreach (var flag in new[] { "m2_plan_swerved", "m2_dirty", "m2_won", "m2_lost", null })
                AssertPromoFinishes(s, "p4_contract", flag);
        }

        private static void AssertPromoFinishes(FeudScript s, string promoId, string flag)
        {
            var st = s.NewState();
            st.Trust = 3;
            if (flag != null) st.Flags.Add(flag);
            var r = new PromoRunner(s, s.FindPromo(promoId), st);
            var seen = new System.Collections.Generic.List<string>();
            int guard = 0;
            while (!r.IsFinished)
            {
                Assert.Less(++guard, 200);
                seen.Add(r.Current.Id);
                switch (r.Current.Type)
                {
                    case PromoNodeType.Line: r.Advance(); break;
                    case PromoNodeType.Choice: r.Choose(r.AvailableOptions().First(v => !v.Locked).Option.Id); break;
                    case PromoNodeType.Check:
                        r.ApplyCheckTier(RollTier.Miss);
                        break;
                }
            }
            Assert.IsFalse(seen.Contains("nb"), "branch не потрапляє в Current");
        }

        [Test]
        public void RealScript_PromoTwo_ExplainsTheMatchOne_WithADifferentLinePerResult()
        {
            var s = Real();
            string Line(string flag)
            {
                var st = s.NewState();
                if (flag != null) st.Flags.Add(flag);
                var r = new PromoRunner(s, s.FindPromo("p2_booker"), st);
                r.Advance();   // n1 → branch → пояснення
                return r.Current.Id;
            }
            Assert.AreEqual("ns", Line("m1_plan_swerved"));
            Assert.AreEqual("nw", Line("m1_won"));
            Assert.AreEqual("nl", Line("m1_lost"));
            Assert.AreEqual("nd", Line(null));
        }

        [Test]
        public void RealScript_MatchOneDefaultsToTrust_MatchTwoToNoCleanLoss_MatchThreeHasNone()
        {
            var s = Real();
            var low = s.NewState();
            low.PrepareMatch(s.Matches[0]);
            Assert.AreEqual(BookerPlan.LoseClean, low.Plan);
            var high = s.NewState();
            high.Trust = 3;
            high.PrepareMatch(s.Matches[0]);
            Assert.AreEqual(BookerPlan.WinAny, high.Plan);

            var m2 = s.NewState();
            m2.PrepareMatch(s.Matches[1]);
            Assert.AreEqual(BookerPlan.NoCleanLoss, m2.Plan);
            var refused = s.NewState();
            refused.Flags.Add("plan_refused");
            refused.PrepareMatch(s.Matches[1]);
            Assert.AreEqual(BookerPlan.None, refused.Plan, "відмова від плану — вільний матч");

            var m3 = s.NewState();
            m3.PrepareMatch(s.Matches[2]);
            Assert.AreEqual(BookerPlan.None, m3.Plan, "план розв'язки — з промо 4");
        }

        [Test]
        public void RealScript_PromoFour_PlanChoices_SetThePlanForTheDecisive()
        {
            var s = Real();
            foreach (var kv in new[]
            {
                ("win", BookerPlan.WinClean), ("lose", BookerPlan.LoseClean), ("any", BookerPlan.WinAny), ("own", BookerPlan.None)
            })
            {
                var st = s.NewState();
                st.Trust = 3;
                var r = new PromoRunner(s, s.FindPromo("p4_contract"), st);
                int guard = 0;
                while (r.Current.Id != "np") { Assert.Less(++guard, 20); r.Advance(); }
                r.Choose(kv.Item1);
                Assert.AreEqual(kv.Item2, st.Plan, kv.Item1);
            }
        }

        [Test]
        public void RealScript_AllSixEndingsReachable_WithinTheTimeBudget()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var report = FeudReachability.Analyze(Real());
            sw.Stop();
            Assert.IsFalse(report.Truncated, "перебір обрізано: " + report.StatesVisited);
            Assert.IsEmpty(report.DeadEnds);
            Assert.IsEmpty(report.Unreachable(), string.Join(", ", report.Unreachable()));
            Assert.Less(sw.ElapsedMilliseconds, 30000, "перебір із трьома матчами має вкладатися в 30 с");
        }

        [Test]
        public void Reachability_WorksOnTheLegacyScript_AndOnAThreeMatchScript()
        {
            var legacy = FeudReachability.Analyze(RingTestData.Script());
            Assert.IsFalse(legacy.Truncated);
            Assert.IsEmpty(legacy.DeadEnds);
            Assert.IsNotEmpty(legacy.Reached);
            var three = FeudReachability.Analyze(Three());
            Assert.IsFalse(three.Truncated);
            Assert.IsEmpty(three.DeadEnds);
            Assert.IsNotEmpty(three.Reached);
        }

        [Test]
        public void Real_FullRunWithAi_ThreeSeeds_ThreeMatchesEach_AndSaveAtEveryBoundary()
        {
            for (ulong seed = 11; seed <= 13; seed++)
            {
                var straight = RingCampaignDriver.NewCampaign(seed);
                RingCampaignDriver.PlayToDevelopment(straight);
                Assert.AreEqual(3, straight.Records.Count);

                var c = RingCampaignDriver.NewCampaign(seed);
                int guard = 0;
                while (true)
                {
                    string blob = c.CaptureState();
                    var fresh = RingCampaign.New(RingCampaignDriver.RealScript(), new SeededDiceRoller(500 + seed));
                    fresh.RestoreState(blob);
                    Assert.AreEqual(blob, fresh.CaptureState(), "крок " + guard + ", " + c.Stage);
                    c = fresh;
                    if (!RingCampaignDriver.Step(c)) break;
                    Assert.Less(++guard, 4000);
                }
                Assert.AreEqual(straight.CaptureState(), c.CaptureState(), "зерно " + seed);
            }
        }

        [Test]
        public void StringsOfTheNewPromos_ExistAndHaveNoRussianIsms()
        {
            string dir = RingCampaignDriver.Dir();
            var strings = MiniJson.ParseObject(File.ReadAllText(Path.Combine(dir, "strings_uk.json")));
            foreach (var k in new[] { "p2.n2.swerved", "p2.n2.won", "p2.n2.lost", "p2.n2.draw", "p2.n3.accept", "p3.n2.won",
                                      "p4.n3.swerved", "p4.n3.dirty", "p4.n3.won", "p4.n3.lost", "p4.n3.draw", "p4.n3p",
                                      "p4.np.win", "p4.np.lose", "p4.np.any", "p4.np.own" })
            {
                Assert.IsTrue(strings.ContainsKey(k), k);
                string v = (string)strings[k];
                Assert.IsFalse(string.IsNullOrWhiteSpace(v), k);
                foreach (var bad in new[] { "ы", "э", "ё", "ъ" }) StringAssert.DoesNotContain(bad, v.ToLowerInvariant(), k);
            }
        }
    }
}
