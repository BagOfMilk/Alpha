using System;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Core.Ring.Match;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>Зліпок кар'єри «ring3»: відновлення у СВІЖИЙ об'єкт дає той самий стан і ту саму подальшу гру (кубик у зліпку).</summary>
    public class CareerSaveTests
    {
        private static CareerSession Restored(CareerSession s, ulong seed)
        {
            var fresh = CareerKit.Fresh(seed, s.Content);
            fresh.RestoreState(s.CaptureState());
            return fresh;
        }

        /// <summary>Сходинки сили (06.11.2026): знімок щабля й образи переживають зліпок; старий зліпок без них — «знімка ще не було».</summary>
        [Test]
        public void PowerSteps_TierSnapshotAndGrudges_SurviveSave_AndOldSavesStillLoad()
        {
            var s = CareerKit.NewHero(41);
            s.State.TierPos = 2; s.State.TierGrowth = 3;
            s.State.Grudges.Add("baron"); s.State.Grudges.Add("vdova");
            var f = Restored(s, 41);
            Assert.AreEqual(2, f.State.TierPos);
            Assert.AreEqual(3, f.State.TierGrowth);
            CollectionAssert.AreEquivalent(new[] { "baron", "vdova" }, f.State.Grudges);

            string old = s.CaptureState();
            foreach (var key in new[] { "|tierpos=", "|tiergrowth=", "|grudges=" })
            {
                int i = old.IndexOf(key, StringComparison.Ordinal);
                int j = old.IndexOf('|', i + 1);
                old = old.Remove(i, (j < 0 ? old.Length : j) - i);
            }
            var g = CareerKit.Fresh(41, s.Content);
            g.RestoreState(old);
            Assert.AreEqual(-1, g.State.TierPos);
            Assert.AreEqual(0, g.State.Grudges.Count);
        }

        private static void AssertRestoresEqual(CareerSession s, ulong seed, string where)
        {
            var fresh = Restored(s, seed);
            Assert.AreEqual(s.CaptureState(), fresh.CaptureState(), where);
            Assert.AreEqual(s.Stage, fresh.Stage, where);
            Assert.AreEqual(s.Money, fresh.Money, where);
            Assert.AreEqual(s.Popularity, fresh.Popularity, where);
            Assert.AreEqual(s.Trust, fresh.Trust, where);
            for (int i = 0; i < 5; i++) Assert.AreEqual(s.Player.Get((RingAttr)i), fresh.Player.Get((RingAttr)i), where);
        }

        [Test]
        public void EveryStage_RoundTripsIntoAFreshInstance()
        {
            const ulong seed = 41;
            var s = CareerKit.NewAtTitle(seed);
            AssertRestoresEqual(s, seed, "титул");
            s.StartNewGame();
            AssertRestoresEqual(s, seed, "створення");
            s.CreateHero(CareerKit.Draft(Gimmick.Flyer, CareerRole.Villain, RingAttr.Technique, "Зірка №1"));
            AssertRestoresEqual(s, seed, "тиждень 1");
            s.Train(true); s.Promo();
            if (s.State.PromoChoicePending) s.ChoosePromoReward(WeekPromoReward.Feud);
            s.State.ZoneDamage[1] = 2; s.State.Injury[2] = 2;
            AssertRestoresEqual(s, seed, "посеред тижня з травмами");
            s.EndWeek();
            AssertRestoresEqual(s, seed, "картка вечора");

            var rep = new CareerRunReport();
            int guard = 0;
            var seen = new System.Collections.Generic.HashSet<string>();
            while (s.State.Season <= 2 && guard++ < 1500)
            {
                string key = s.Stage + "/" + s.State.Phase + (s.Stage == CareerStage.Show && s.State.Phase == ShowPhase.Feud ? "/" + s.Feud.Stage : "");
                if (seen.Add(key)) AssertRestoresEqual(s, seed, key);
                if (s.Stage == CareerStage.SeasonEnd && s.CanStartTitleMatch) { s.StartTitleMatch(); continue; }
                CareerBot.Step(s, 1, rep);
            }
            foreach (var expect in new[] { "Week/None", "Show/Card", "Show/Result", "Show/Feud/Overview", "Show/Feud/Promo", "Show/Feud/MatchPrep", "SeasonEnd/None" })
                Assert.IsTrue(seen.Contains(expect), "не зустрілось " + expect + "; були: " + string.Join(", ", seen));
        }

        [Test]
        public void RestoredCareer_PlaysOnExactlyLikeTheOriginal()
        {
            for (int cut = 3; cut < 60; cut += 9)
            {
                const ulong seed = 52;
                var a = CareerKit.NewAtCreate(seed);
                var rep = new CareerRunReport();
                for (int i = 0; i < cut; i++) CareerBot.Step(a, 2, rep);
                while (a.State.Phase == ShowPhase.Match) CareerBot.Step(a, 2, rep);   // матч посеред гри не зберігається (див. окремий тест)
                var b = Restored(a, seed);
                for (int i = 0; i < 40; i++)
                {
                    string before = a.Stage + "/" + a.State.Phase + "/" + a.Feud.Stage;
                    CareerBot.Step(a, 2, rep); CareerBot.Step(b, 2, rep);
                    Assert.AreEqual(a.CaptureState(), b.CaptureState(), "розріз " + cut + ", крок " + i + " зі стану " + before);
                }
            }
        }

        [Test]
        public void SavedMidMatch_ComesBackAsTheShowCard_FeudMatchAsPrep()
        {
            const ulong seed = 63;
            var s = CareerKit.NewHero(seed);
            s.EndWeek();
            s.StartShow();
            Assert.AreEqual(ShowPhase.Match, s.State.Phase);
            var f = Restored(s, seed);
            Assert.AreEqual(CareerStage.Show, f.Stage);
            Assert.AreEqual(ShowPhase.Card, f.State.Phase);
            Assert.IsNull(f.Engine);
            Assert.AreEqual(s.State.Opponent, f.State.Opponent);
            Assert.IsNotNull(f.StartShow(), "після відновлення матч можна почати знову");
            // у самій сесії фази не змінено
            Assert.AreEqual(ShowPhase.Match, s.State.Phase);
        }

        [Test]
        public void NpcFeud_Belt_Report_Result_SurviveTheRoundTrip()
        {
            const ulong seed = 74;
            var s = CareerKit.NewHero(seed);
            s.State.SetNpcFeud("baron", 3); s.State.SetNpcFeud("vdova", 1);
            s.State.BeltHolder = CareerState.PlayerHolder;
            s.State.TitlesWon = 2; s.State.FiredCount = 1;
            var f = Restored(s, seed);
            Assert.AreEqual(3, f.State.GetNpcFeud("baron"));
            Assert.AreEqual(1, f.State.GetNpcFeud("vdova"));
            Assert.IsTrue(f.IsChampion);
            Assert.AreEqual(2, f.State.TitlesWon);
            Assert.AreEqual(1, f.State.FiredCount);
            Assert.AreEqual("Тест", f.State.RingName);
            CollectionAssert.AreEqual(s.State.Moves, f.State.Moves);
        }

        [Test]
        public void ShowResult_AndSeasonReport_RoundTrip()
        {
            const ulong seed = 85;
            var s = CareerKit.NewHero(seed);
            s.EndWeek(); s.StartShow();
            CareerBot.PlayMatch(s, 0, new CareerRunReport());
            var r = s.FinishMatch();
            var f = Restored(s, seed);
            Assert.AreEqual(ShowPhase.Result, f.State.Phase);
            Assert.AreEqual(r.TotalFee, f.State.Last.TotalFee);
            Assert.AreEqual(r.Winner, f.State.Last.Winner);
            Assert.AreEqual(r.Hype, f.State.Last.Hype);
            Assert.AreEqual(s.CaptureState(), f.CaptureState());
        }

        [Test]
        public void BadBlobs_AreRefused()
        {
            var s = CareerKit.NewHero(1);
            var f = CareerKit.Fresh(1, s.Content);
            Assert.Throws<ArgumentException>(() => f.RestoreState(""));
            Assert.Throws<FormatException>(() => f.RestoreState("ring2|x=1"));
            Assert.Throws<FormatException>(() => f.RestoreState("ring3|stage=2"));
            string blob = s.CaptureState();
            Assert.Throws<FormatException>(() => f.RestoreState(blob.Replace("|money=", "|moneyx=")));
            f.RestoreState(blob);   // справний зліпок після відмов усе ще читається
            Assert.AreEqual(blob, f.CaptureState());
        }

        [Test]
        public void Blob_IsCanonical_SameStateSameString()
        {
            var a = CareerKit.NewHero(9); var b = CareerKit.NewHero(9);
            Assert.AreEqual(a.CaptureState(), b.CaptureState());
            a.Train(false);
            Assert.AreNotEqual(a.CaptureState(), b.CaptureState());
            StringAssert.StartsWith("ring3|", a.CaptureState());
        }
    }
}
