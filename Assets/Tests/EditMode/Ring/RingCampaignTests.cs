using System.IO;
using Game.Core.Randomness;
using Game.Core.Ring;
using Game.Core.Ring.Match;
using Game.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>Спільний водій кампанії для тестів: перший доступний варіант, нагороди — перші N, ШІ за обидві сторони в матчі.</summary>
    internal static class RingCampaignDriver
    {
        public static string Dir() => Path.Combine(Application.dataPath, "Resources", "Ring");

        public static FeudScript RealScript() => FeudScriptLoader.Load(File.ReadAllText(Path.Combine(Dir(), "feud_hammer.json")));

        public static RingCampaign NewCampaign(ulong seed) => RingCampaign.New(RealScript(), new SeededDiceRoller(seed));

        /// <summary>Один вузол промо: репліка, перший доступний варіант або кидок із першими N нагородами.</summary>
        public static void StepNode(RingCampaign c)
        {
            var r = c.CurrentPromoRunner;
            switch (r.Current.Type)
            {
                case PromoNodeType.Line:
                    r.Advance();
                    break;
                case PromoNodeType.Choice:
                    foreach (var v in r.AvailableOptions())
                        if (!v.Locked) { r.Choose(v.Option.Id); break; }
                    break;
                case PromoNodeType.Check:
                    r.RollCheck(c.Dice);
                    if (r.AwaitingRewards)
                    {
                        var ids = new System.Collections.Generic.List<string>();
                        for (int i = 0; i < r.RewardsToPick; i++) ids.Add(r.Current.Rewards[i].Id);
                        r.ChooseRewards(ids);
                    }
                    break;
            }
        }

        /// <summary>Один крок кампанії (для побудови прогону з перервами на сейв). false — дійшли до розвитку.</summary>
        public static bool Step(RingCampaign c)
        {
            switch (c.Stage)
            {
                case RingStage.Overview: c.StartPromo(); return true;
                case RingStage.Promo:
                    if (c.CurrentPromoRunner.IsFinished) c.FinishPromo(); else StepNode(c);
                    return true;
                case RingStage.MatchPrep:
                    c.StartMatch();
                    MatchAutoplay.Run(c.CurrentMatch, true);
                    c.FinishMatch();
                    return true;
                case RingStage.Ending: c.OpenDevelopment(); return true;
                default: return false;
            }
        }

        public static void PlayToDevelopment(RingCampaign c)
        {
            int guard = 0;
            while (Step(c)) Assert.Less(++guard, 2000, "кампанія не доходить до розвитку");
        }
    }

    /// <summary>Фасад кампанії (docs/ring/gdd.md §1): повний прохід на справжніх даних, сейв на кожній межі, реванш із перком.</summary>
    public class RingCampaignTests
    {
        [Test]
        public void FullPlaythrough_ReachesDevelopment_ForSeveralSeeds()
        {
            for (ulong seed = 1; seed <= 6; seed++)
            {
                var c = RingCampaignDriver.NewCampaign(seed);
                RingCampaignDriver.PlayToDevelopment(c);
                Assert.AreEqual(RingStage.Development, c.Stage, "зерно " + seed);
                Assert.IsTrue(c.HasEnding, "зерно " + seed);
                Assert.AreEqual(c.PromoCount, c.PromoIndex);
                Assert.IsNotNull(c.Result);
                Assert.IsTrue(c.CanRematch);
                Assert.Greater(c.State.DevPoints, 0, "очки розвитку нараховано за матч");
                Assert.AreEqual(1, c.State.Heat, "ворожнеча скинута до 1 після кінцівки");
            }
        }

        [Test]
        public void StageGuards_RejectWrongOrder()
        {
            var c = RingCampaignDriver.NewCampaign(1);
            Assert.Throws<System.InvalidOperationException>(() => c.StartMatch());
            Assert.Throws<System.InvalidOperationException>(() => c.FinishPromo());
            c.StartPromo();
            Assert.Throws<System.InvalidOperationException>(() => c.StartPromo());
            Assert.Throws<System.InvalidOperationException>(() => c.FinishPromo(), "промо ще не закінчилось");
            Assert.IsFalse(c.CanRematch);
        }

        /// <summary>Кінець прогону з сейвом-відновленням у СВІЖИЙ об'єкт перед кожним кроком кампанії.</summary>
        private static string PlayWithRoundtrips(ulong seed, ulong freshSeed)
        {
            var script = RingCampaignDriver.RealScript();
            var c = RingCampaign.New(script, new SeededDiceRoller(seed));
            int guard = 0;
            while (true)
            {
                string blob = c.CaptureState();
                var fresh = RingCampaign.New(script, new SeededDiceRoller(freshSeed));
                fresh.RestoreState(blob);
                Assert.AreEqual(blob, fresh.CaptureState(), "відновлений зліпок збігається (крок " + guard + ", " + c.Stage + ")");
                c = fresh;
                if (!RingCampaignDriver.Step(c)) break;
                Assert.Less(++guard, 2000);
            }
            return c.CaptureState();
        }

        [Test]
        public void SaveRestore_AtEveryBoundary_GivesTheSameOutcome()
        {
            for (ulong seed = 1; seed <= 4; seed++)
            {
                var straight = RingCampaignDriver.NewCampaign(seed);
                RingCampaignDriver.PlayToDevelopment(straight);
                // Свіжий кубик має інше зерно: якщо відновлення не продовжує потік, підсумок розійдеться.
                Assert.AreEqual(straight.CaptureState(), PlayWithRoundtrips(seed, 777 + seed), "зерно " + seed);
            }
        }

        [Test]
        public void SaveInsideMatch_ComesBackAsMatchPrep_AndRematchAsDevelopment()
        {
            var c = RingCampaignDriver.NewCampaign(2);
            while (c.Stage != RingStage.MatchPrep) RingCampaignDriver.Step(c);
            c.StartMatch();
            string blob = c.CaptureState();
            var fresh = RingCampaign.New(RingCampaignDriver.RealScript(), new SeededDiceRoller(5));
            fresh.RestoreState(blob);
            Assert.AreEqual(RingStage.MatchPrep, fresh.Stage);
            Assert.IsNull(fresh.CurrentMatch);

            MatchAutoplay.Run(c.CurrentMatch, true);
            c.FinishMatch();
            c.OpenDevelopment();
            c.StartRematch();
            var f2 = RingCampaign.New(RingCampaignDriver.RealScript(), new SeededDiceRoller(5));
            f2.RestoreState(c.CaptureState());
            Assert.AreEqual(RingStage.Development, f2.Stage);
            Assert.IsTrue(f2.CanRematch, "знімок до матчу доїхав у сейв");
            Assert.AreEqual(c.Ending, f2.Ending);
        }

        [Test]
        public void Rematch_WithBoughtSecondWind_GivesKickOutBonus_AndKeepsTheEnding()
        {
            var c = RingCampaignDriver.NewCampaign(3);
            RingCampaignDriver.PlayToDevelopment(c);
            Assert.AreEqual(0, c.State.Perks.Count);
            var ending = c.Ending;
            var firstResult = c.Result;

            // Шлях Героя на ранг 1 і очки — напряму (які шляхи набрав типовий прохід, залежить від зерна).
            c.State.AddPath(RingPath.Face, 2);
            c.State.DevPoints = 3;
            Assert.IsTrue(c.BuyPerk(PerkCatalog.SecondWind));
            Assert.IsFalse(c.BuyPerk(PerkCatalog.SecondWind), "удруге не купити");
            Assert.IsTrue(c.BuyAttr(RingAttr.Stamina));
            int stamina = c.State.Player.Get(RingAttr.Stamina);

            var m = c.StartRematch();
            Assert.AreEqual(RingStage.Match, c.Stage);
            Assert.IsTrue(c.IsRematch);
            Assert.AreEqual(1, m.Setup.PlayerMods.KickOutBonus);
            Assert.AreEqual(stamina, m.Setup.Player.Get(RingAttr.Stamina), "куплений атрибут доїхав у матч");

            string before = c.State.CaptureState();
            MatchAutoplay.Run(m, true);
            c.FinishMatch();
            Assert.AreEqual(RingStage.Development, c.Stage);
            Assert.IsNotNull(c.RematchResult);
            Assert.AreEqual(ending, c.Ending, "кінцівка ворожнечі — за першим матчем");
            Assert.AreSame(firstResult, c.Result);
            Assert.AreEqual(before, c.State.CaptureState(), "реванш не змінює стану ворожнечі");
            Assert.IsTrue(c.CanRematch, "реванш можна повторити");
        }

        [Test]
        public void Rematch_StartsFromThePreMatchState_NotFromTheEndingOne()
        {
            var c = RingCampaignDriver.NewCampaign(4);
            while (c.Stage != RingStage.MatchPrep) RingCampaignDriver.Step(c);
            var first = c.State.BuildMatchSetup(c.Script);
            RingCampaignDriver.Step(c);
            c.OpenDevelopment();
            var m = c.StartRematch();
            Assert.AreEqual(first.StartHype, m.Setup.StartHype);
            Assert.AreEqual(first.PlayerStartMomentum, m.Setup.PlayerStartMomentum);
            Assert.AreEqual(first.Plan, m.Setup.Plan);
            Assert.AreEqual(first.Stipulations, m.Setup.Stipulations);
        }
    }
}
