using System;
using System.Linq;
using Game.Core.Ring;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>Граф промо: вузли, умови, закриття гілок, кидки з нагородами (gdd §4).</summary>
    public class PromoGraphTests
    {
        // Кубик ScriptedDiceRoller: грань d (1–6) = (d−1)/6 + 0.01.
        private static double Dice(int d) => (d - 1) / 6.0 + 0.01;

        private static PromoRunner Start(out FeudScript script, out FeudState st, int promo = 0)
        {
            script = RingTestData.Script();
            st = script.NewState();
            return new PromoRunner(script, script.Promos[promo], st);
        }

        private static void ToChoice(PromoRunner r)
        {
            Assert.AreEqual(PromoNodeType.Line, r.Current.Type);
            r.Advance();
            Assert.AreEqual("n2", r.Current.Id);
        }

        [Test]
        public void Loader_BuildsGraphShape()
        {
            var s = RingTestData.Script();
            Assert.AreEqual(2, s.Promos.Count);
            var p1 = s.Promos[0];
            Assert.AreEqual("n1", p1.Start);
            Assert.AreEqual(1, p1.Act);
            Assert.AreEqual(6, p1.Nodes.Count);
            var n2 = p1.Find("n2");
            Assert.AreEqual(4, n2.Options.Count);
            Assert.AreEqual(RingPath.Face, n2.Options[0].Path);
            Assert.IsTrue(n2.Options[3].Hidden);
            var n3 = p1.Find("n3");
            Assert.AreEqual(2, n3.Pick10);
            Assert.AreEqual(3, n3.Rewards.Count);
            Assert.AreEqual("nf", n3.Next6);
            Assert.AreEqual(RingAttr.Charisma, n3.Attr);
        }

        [Test]
        public void Options_LockedShowReason_HiddenAndClosedAreNotShown()
        {
            var r = Start(out _, out var st);
            ToChoice(r);
            var v = r.AvailableOptions();
            CollectionAssert.AreEqual(new[] { "respect", "joke", "push" }, v.Select(x => x.Option.Id).ToArray());
            Assert.IsFalse(v[0].Locked);
            Assert.IsTrue(v[1].Locked);
            Assert.AreEqual(ConditionKind.Attr, v[1].Reason.Kind);
            Assert.AreEqual("attr:charisma>=1", v[1].Reason.ToString());

            st.Player.Set(RingAttr.Charisma, 1);
            st.Flags.Add("secret");
            v = r.AvailableOptions();
            Assert.AreEqual(4, v.Count);
            Assert.IsTrue(v.All(x => !x.Locked));
        }

        [Test]
        public void Choose_Locked_OrHidden_OrUnknown_Throws()
        {
            var r = Start(out _, out _);
            ToChoice(r);
            Assert.Throws<InvalidOperationException>(() => r.Choose("joke"));
            Assert.Throws<InvalidOperationException>(() => r.Choose("secret"));
            Assert.Throws<InvalidOperationException>(() => r.Choose("nope"));
            Assert.Throws<InvalidOperationException>(() => r.PreviewChances(), "n2 — не check");
        }

        [Test]
        public void Choose_AppliesEffects_AndMovesOn()
        {
            var r = Start(out _, out var st);
            ToChoice(r);
            r.Choose("respect");
            Assert.AreEqual(1, st.GetPath(RingPath.Face));
            Assert.AreEqual("n3", r.Current.Id);
        }

        [Test]
        public void CloseEffect_HidesBranchForever_AlsoAfterSaveRestore()
        {
            var r = Start(out var script, out var st);
            ToChoice(r);
            r.Choose("push");
            Assert.IsTrue(st.IsClosed("hands"));
            Assert.IsTrue(st.HasFlag("pushed"));

            // у свіжий стан із зліпка, курсор — одразу на n4
            var fresh = new FeudState();
            fresh.RestoreState(st.CaptureState());
            var r2 = new PromoRunner(script, script.Promos[0], fresh, "n4");
            CollectionAssert.AreEqual(new[] { "deal" }, r2.AvailableOptions().Select(x => x.Option.Id).ToArray());

            // без закриття — обидва
            var open = new PromoRunner(script, script.Promos[0], script.NewState(), "n4");
            CollectionAssert.AreEqual(new[] { "hands", "deal" }, open.AvailableOptions().Select(x => x.Option.Id).ToArray());
        }

        [Test]
        public void Check_Strong_PicksExactlyPick10Rewards_ThenNext()
        {
            var r = Start(out _, out var st);
            ToChoice(r); r.Choose("respect");
            var dice = new ScriptedDiceRoller(Dice(6), Dice(6));
            Assert.AreEqual(RollTier.Strong, r.RollCheck(dice));
            Assert.IsTrue(r.AwaitingRewards);
            Assert.AreEqual(2, r.RewardsToPick);
            Assert.AreEqual("n3", r.Current.Id);
            Assert.Throws<ArgumentException>(() => r.ChooseRewards(new[] { "aud" }));
            Assert.Throws<ArgumentException>(() => r.ChooseRewards(new[] { "aud", "aud" }));
            Assert.Throws<ArgumentException>(() => r.ChooseRewards(new[] { "aud", "zzz" }));
            r.ChooseRewards(new[] { "aud", "mom" });
            Assert.AreEqual(4, st.Audience);
            Assert.AreEqual(2, st.Momentum);
            Assert.AreEqual(0, st.Heat);
            Assert.AreEqual("n4", r.Current.Id);
            Assert.IsFalse(r.AwaitingRewards);
        }

        [Test]
        public void Check_Partial_PicksPick7_Rewards()
        {
            var r = Start(out _, out var st);
            ToChoice(r); r.Choose("respect");
            Assert.AreEqual(RollTier.Partial, r.RollCheck(new ScriptedDiceRoller(Dice(4), Dice(4))));
            Assert.AreEqual(1, r.RewardsToPick);
            r.ChooseRewards(new[] { "heat" });
            Assert.AreEqual(1, st.Heat);
            Assert.AreEqual("n4", r.Current.Id);
        }

        [Test]
        public void Check_Miss_AppliesFail_AndGoesToNext6()
        {
            var r = Start(out _, out var st);
            ToChoice(r); r.Choose("respect");
            st.Momentum = 3;
            Assert.AreEqual(RollTier.Miss, r.RollCheck(new ScriptedDiceRoller(Dice(1), Dice(2))));
            Assert.AreEqual(2, st.Momentum);
            Assert.AreEqual("nf", r.Current.Id);
            Assert.AreEqual(RollTier.Miss, r.LastTier);
            Assert.AreEqual(3, r.LastTotal);
            Assert.IsFalse(r.AwaitingRewards);
            r.Advance();
            Assert.AreEqual("n4", r.Current.Id);
        }

        [Test]
        public void Check_Miss_WithoutNext6_GoesToNext()
        {
            var script = RingTestData.Script();
            script.Promos[0].Find("n3").Next6 = null;
            var st = script.NewState();
            var r = new PromoRunner(script, script.Promos[0], st, "n3");
            r.ApplyCheckTier(RollTier.Miss);
            Assert.AreEqual("n4", r.Current.Id);
        }

        [Test]
        public void Check_CannotRollTwice_OrChooseRewardsBeforeRoll()
        {
            var script = RingTestData.Script();
            var r = new PromoRunner(script, script.Promos[0], script.NewState(), "n3");
            Assert.Throws<InvalidOperationException>(() => r.ChooseRewards(new[] { "aud", "mom" }));
            r.ApplyCheckTier(RollTier.Strong);
            Assert.Throws<InvalidOperationException>(() => r.ApplyCheckTier(RollTier.Strong));
        }

        [Test]
        public void Check_PreviewChances_SumTo36_AndFollowAttributeAndPerk()
        {
            var script = RingTestData.Script();
            var st = script.NewState();
            var r = new PromoRunner(script, script.Promos[0], st, "n3");
            var c0 = r.PreviewChances();
            Assert.AreEqual(36, c0.Sum());
            Assert.AreEqual(TwoD6.ChancesOf36(0)[2], c0[2]);

            st.Perks.Add(PerkCatalog.OwnScript);
            Assert.AreEqual(1, r.CheckModifier());
            Assert.AreEqual(TwoD6.ChancesOf36(1)[2], r.PreviewChances()[2]);
            Assert.Greater(r.PreviewChances()[2], c0[2]);

            st.Player.Set(RingAttr.Charisma, 2);
            Assert.AreEqual(3, r.CheckModifier());
        }

        [Test]
        public void Runner_ReachesEnd_AndAdvanceOnWrongNodeThrows()
        {
            var r = Start(out _, out var st);
            Assert.Throws<InvalidOperationException>(() => r.AvailableOptions());
            ToChoice(r); r.Choose("push");
            r.ApplyCheckTier(RollTier.Miss);
            r.Advance();
            Assert.Throws<InvalidOperationException>(() => r.Advance());
            r.Choose("deal");
            Assert.IsTrue(r.IsFinished);
            Assert.AreEqual(1, st.Heat);
        }

        [Test]
        public void SecondPromo_PlanOptionsSetPlanAllyAndStipulation()
        {
            var r = Start(out _, out var st, 1);
            r.Choose("a");
            Assert.AreEqual(BookerPlan.WinClean, st.Plan);
            Assert.IsTrue(st.HasAlly);
            Assert.IsTrue(r.IsFinished);
        }
    }
}
