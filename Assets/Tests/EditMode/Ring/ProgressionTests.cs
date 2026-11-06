using Game.Core.Ring;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>Розвиток (gdd §6): очки, ранги шляху, перки, атрибути, вплив на матч.</summary>
    public class ProgressionTests
    {
        private static FeudState Fresh() => RingTestData.Script().NewState();

        [Test]
        public void Points_Formula()
        {
            // 1 + перемога + ажіотаж ≥ 7 + (виконано або вдалий звіт)
            Assert.AreEqual(1, Development.PointsFor(RingTestData.Result(MatchWinner.Rival, hype: 5), FeudPlanOutcome.Neither));
            Assert.AreEqual(2, Development.PointsFor(RingTestData.Result(MatchWinner.Player, hype: 5), FeudPlanOutcome.Neither));
            Assert.AreEqual(3, Development.PointsFor(RingTestData.Result(MatchWinner.Player, hype: 7), FeudPlanOutcome.Neither));
            Assert.AreEqual(4, Development.PointsFor(RingTestData.Result(MatchWinner.Player, hype: 7), FeudPlanOutcome.Fulfilled));
            Assert.AreEqual(2, Development.PointsFor(RingTestData.Result(MatchWinner.Rival, hype: 4), FeudPlanOutcome.Fulfilled));
            Assert.AreEqual(2, Development.PointsFor(RingTestData.Result(MatchWinner.Player, hype: 5), FeudPlanOutcome.Swerved), "невдалий звіт не дає очка");
            Assert.AreEqual(4, Development.PointsFor(RingTestData.Result(MatchWinner.Player, hype: 8), FeudPlanOutcome.Swerved));
        }

        [Test]
        public void PathRanks_AtTwoAndFourPoints()
        {
            Assert.AreEqual(0, Development.PathRank(0));
            Assert.AreEqual(0, Development.PathRank(1));
            Assert.AreEqual(1, Development.PathRank(2));
            Assert.AreEqual(1, Development.PathRank(3));
            Assert.AreEqual(2, Development.PathRank(4));
            Assert.AreEqual(2, Development.PathRank(5));
            Assert.AreEqual(3, Development.PathRank(6), "ранг 3 — вінець шляху");
            Assert.AreEqual(3, Development.PathRank(9), "ранг не вищий за 3");
        }

        [Test]
        public void Catalog_HasFourteenPerks_FourPerPath_CrownAtRankThree()
        {
            // 06.11.2026 (пункт 2 плану лупу): 2 загальні + по 4 на шлях; ціна 1–3, ранг шляху 1–3, ранг 3 — вінець.
            Assert.AreEqual(14, PerkCatalog.All.Count);
            foreach (var p in PerkCatalog.All)
            {
                if (p.IsGeneral) { Assert.AreEqual(2, p.Cost, p.Id); continue; }
                Assert.That(p.Cost, Is.InRange(1, 3), p.Id);
                Assert.That(p.Rank, Is.InRange(1, 3), p.Id);
            }
            foreach (RingPath path in new[] { RingPath.Face, RingPath.Heel, RingPath.Rebel })
            {
                int n = 0, crowns = 0;
                foreach (var p in PerkCatalog.All) if (p.Path == path) { n++; if (p.Rank == 3) crowns++; }
                Assert.AreEqual(4, n, path.ToString());
                Assert.AreEqual(1, crowns, "вінець " + path);
            }
            Assert.AreEqual(RingPath.Face, PerkCatalog.Find(PerkCatalog.SecondWind).Path);
            Assert.AreEqual(2, PerkCatalog.Find(PerkCatalog.CrowdFavorite).Rank);
            Assert.AreEqual(RingPath.Heel, PerkCatalog.Find(PerkCatalog.ColdBlood).Path);
            Assert.AreEqual(RingPath.Rebel, PerkCatalog.Find(PerkCatalog.NoNet).Path);
            Assert.IsTrue(PerkCatalog.Find(PerkCatalog.Veteran).IsGeneral);
            Assert.IsTrue(PerkCatalog.Find(PerkCatalog.IronJaw).IsGeneral);
        }

        [Test]
        public void BuyPerk_RequiresRankAndPoints_ThenSpendsAndIsOwned()
        {
            var st = Fresh();
            string why;
            st.DevPoints = 5;
            Assert.IsFalse(Development.CanBuyPerk(st, PerkCatalog.SecondWind, out why));
            Assert.AreEqual("rank", why);

            st.AddPath(RingPath.Face, 2);
            Assert.IsTrue(Development.BuyPerk(st, PerkCatalog.SecondWind));
            Assert.AreEqual(4, st.DevPoints);
            Assert.IsTrue(st.Perks.Contains(PerkCatalog.SecondWind));
            Assert.IsFalse(Development.CanBuyPerk(st, PerkCatalog.SecondWind, out why));
            Assert.AreEqual("owned", why);

            Assert.IsFalse(Development.CanBuyPerk(st, PerkCatalog.CrowdFavorite, out why), "ранг 2 потребує 4 очків шляху");
            st.AddPath(RingPath.Face, 2);
            Assert.IsTrue(Development.BuyPerk(st, PerkCatalog.CrowdFavorite));
            Assert.AreEqual(3, st.DevPoints);

            Assert.IsFalse(Development.CanBuyPerk(st, "no_such", out why));
        }

        [Test]
        public void BuyPerk_NotEnoughPoints_Fails_AndGeneralCostsTwo()
        {
            var st = Fresh();
            string why;
            st.DevPoints = 1;
            Assert.IsFalse(Development.CanBuyPerk(st, PerkCatalog.Veteran, out why));
            Assert.AreEqual("points", why);
            st.DevPoints = 2;
            Assert.IsTrue(Development.BuyPerk(st, PerkCatalog.Veteran));
            Assert.AreEqual(0, st.DevPoints);
            Assert.IsFalse(Development.BuyPerk(st, PerkCatalog.IronJaw));
        }

        [Test]
        public void BuyAttr_CostsTwo_UpToPlusThree()
        {
            var st = Fresh();
            string why;
            st.DevPoints = 10;
            Assert.AreEqual(0, st.Player.Get(RingAttr.Charisma));
            for (int i = 1; i <= 3; i++)
            {
                Assert.IsTrue(Development.BuyAttr(st, RingAttr.Charisma));
                Assert.AreEqual(i, st.Player.Get(RingAttr.Charisma));
            }
            Assert.AreEqual(4, st.DevPoints);
            Assert.IsFalse(Development.CanBuyAttr(st, RingAttr.Charisma, out why));
            Assert.AreEqual("max", why);
            Assert.IsFalse(Development.BuyAttr(st, RingAttr.Charisma));
            Assert.AreEqual(4, st.DevPoints, "відмова не списує очок");

            st.DevPoints = 1;
            Assert.IsFalse(Development.CanBuyAttr(st, RingAttr.Strength, out why));
            Assert.AreEqual("points", why);
        }

        [Test]
        public void ToModifiers_EachPerk_ChangesItsField()
        {
            Assert.AreEqual(1, Development.ToModifiers(new[] { PerkCatalog.SecondWind }).KickOutBonus);
            Assert.AreEqual(2, Development.ToModifiers(new[] { PerkCatalog.CrowdFavorite }).StartHypeBonus);
            Assert.IsTrue(Development.ToModifiers(new[] { PerkCatalog.EyesBehind }).DirtyNoWarningOnPartial);
            Assert.AreEqual(2, Development.ToModifiers(new[] { PerkCatalog.ColdBlood }).StartMomentumBonus);
            Assert.AreEqual(1, Development.ToModifiers(new[] { PerkCatalog.NoNet }).RiskyMoveBonus);
            Assert.AreEqual(1, Development.ToModifiers(new[] { PerkCatalog.Veteran }).ExtraReversalTokens);
            Assert.AreEqual(2, Development.ToModifiers(new[] { PerkCatalog.IronJaw }).DamageThresholdShift);
            var none = Development.ToModifiers(new string[0]);
            Assert.AreEqual(0, none.KickOutBonus + none.StartHypeBonus + none.StartMomentumBonus + none.RiskyMoveBonus + none.ExtraReversalTokens + none.DamageThresholdShift);
            Assert.IsFalse(none.DirtyNoWarningOnPartial);
        }

        [Test]
        public void OwnScript_AddsPromoRollBonus_Only()
        {
            Assert.AreEqual(1, Development.PromoRollBonus(new[] { PerkCatalog.OwnScript }));
            Assert.AreEqual(0, Development.PromoRollBonus(new[] { PerkCatalog.SecondWind }));
            var m = Development.ToModifiers(new[] { PerkCatalog.OwnScript });
            Assert.AreEqual(0, m.KickOutBonus + m.StartHypeBonus + m.StartMomentumBonus);
        }

        [Test]
        public void Perks_ChangeMatchSetup_ThroughBuildMatchSetup()
        {
            var script = RingTestData.Script();
            var st = script.NewState();
            st.Heat = 2;
            st.Momentum = 1;
            var before = st.BuildMatchSetup(script);
            Assert.AreEqual(3, before.StartHype);
            Assert.AreEqual(1, before.PlayerStartMomentum);
            Assert.AreEqual(0, before.PlayerMods.KickOutBonus);

            st.Perks.Add(PerkCatalog.CrowdFavorite);
            st.Perks.Add(PerkCatalog.ColdBlood);
            st.Perks.Add(PerkCatalog.SecondWind);
            st.Perks.Add(PerkCatalog.IronJaw);
            var after = st.BuildMatchSetup(script);
            Assert.AreEqual(5, after.StartHype, "ажіотаж старту +2");
            Assert.AreEqual(3, after.PlayerStartMomentum, "імпульс старту +2");
            Assert.AreEqual(1, after.PlayerMods.KickOutBonus);
            Assert.AreEqual(2, after.PlayerMods.DamageThresholdShift);
        }

        [Test]
        public void Development_FullLoop_MatchToPerkToRematchSetup()
        {
            var script = RingTestData.Script();
            var st = script.NewState();
            st.Plan = BookerPlan.WinAny;
            st.AddPath(RingPath.Face, 2);
            var snapshot = st.CaptureState();

            var outcome = st.ApplyMatchResult(RingTestData.Result(MatchWinner.Player, hype: 8, sells: 2));
            Assert.AreEqual(FeudPlanOutcome.Fulfilled, outcome);
            Assert.AreEqual(4, st.DevPoints);
            Assert.AreEqual(3, st.GetPath(RingPath.Face));
            Assert.IsTrue(Development.BuyPerk(st, PerkCatalog.SecondWind));
            Assert.IsTrue(Development.BuyAttr(st, RingAttr.Technique));
            Assert.AreEqual(1, st.DevPoints);
            Assert.AreEqual(2, st.Player.Get(RingAttr.Technique));

            // реванш: стан до матчу + куплене
            var rematch = new FeudState();
            rematch.RestoreState(snapshot);
            rematch.Perks.UnionWith(st.Perks);
            Assert.AreEqual(1, rematch.BuildMatchSetup(script).PlayerMods.KickOutBonus);
        }
    }
}
