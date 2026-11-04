using System.Collections.Generic;
using System.Linq;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Core.Ring.Match;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>Цикл кар'єри цілком: бот проходить два сезони через публічний фасад; усі етапи, дії тижня і ворожнеча з Молотом досяжні.</summary>
    public class CareerLoopTests
    {
        private const int Variants = 8;

        [Test]
        public void TwoSeasons_BotsWithDifferentChoices_FinishWithoutHangs_AndReachEveryStageAndAction()
        {
            var stages = new HashSet<CareerStage>();
            var phases = new HashSet<ShowPhase>();
            var actions = new int[5];
            for (int v = 0; v < Variants; v++)
            {
                var s = v == 0 ? CareerKit.NewAtTitle(100) : CareerKit.NewAtCreate(100 + (ulong)v);
                var rep = CareerBot.Play(s, 2, v);
                stages.UnionWith(rep.Stages); phases.UnionWith(rep.Phases);
                for (int i = 0; i < 5; i++) actions[i] += rep.Actions[i];
                Assert.AreEqual(3, s.State.Season, "варіант " + v);
                Assert.AreEqual(1, s.State.Week, "варіант " + v);
                Assert.GreaterOrEqual(rep.MinMoney, 0);
                Assert.That(rep.MinPopularity, Is.InRange(0, CareerRules.MaxPopularity));
                Assert.That(rep.MaxPopularity, Is.InRange(0, CareerRules.MaxPopularity));
            }
            foreach (CareerStage st in System.Enum.GetValues(typeof(CareerStage))) CollectionAssert.Contains(stages, st, "етап " + st);
            foreach (var ph in new[] { ShowPhase.Card, ShowPhase.Match, ShowPhase.Feud, ShowPhase.Result }) CollectionAssert.Contains(phases, ph, "фаза " + ph);
            for (int i = 0; i < 5; i++) Assert.Greater(actions[i], 0, "дія тижня " + (WeekActionKind)i + " жодного разу не виконана");
        }

        [Test]
        public void HammerFeud_ResolvesInThreeBigShows_ForEveryVariant()
        {
            for (int v = 0; v < Variants; v++)
            {
                var s = CareerKit.NewAtCreate(200 + (ulong)v);
                CareerBot.Play(s, 3, v);   // сезон — 4 тижні з одним великим шоу: три матчі Молота — три сезони
                Assert.IsTrue(s.State.FeudResolved, "варіант " + v);
                Assert.IsTrue(s.Feud.HasEnding);
                Assert.AreEqual(3, s.Feud.Records.Count, "три матчі ворожнечі");
            }
        }

        [Test]
        public void BigShows_AreEveryFourthWeek_FeudShowsFirst()
        {
            var s = CareerKit.NewHero(7);
            var rep = new CareerRunReport();
            var bigWeeks = new List<int>(); var feudFlags = new List<bool>();
            int guard = 0;
            while (s.State.Season <= 4 && guard++ < 2000)
            {
                if (s.Stage == CareerStage.Week && s.State.IsBig && s.State.ActionsLeft == CareerRules.ActionsPerWeek && s.State.ActionsDone.Count == 0)
                { bigWeeks.Add(s.State.Week); feudFlags.Add(s.State.IsFeudShow); }
                if (s.Stage == CareerStage.SeasonEnd && s.CanStartTitleMatch) s.SkipTitle();
                CareerBot.Step(s, 0, rep);
            }
            // Сезон — 4 тижні (власник 04.11.2026), велике шоу — четвертий тиждень кожного сезону.
            CollectionAssert.AreEqual(new[] { 4, 4, 4, 4 }, bigWeeks);
            CollectionAssert.AreEqual(new[] { true, true, true, false }, feudFlags, "три великі шоу — Молот, далі — ростер");
        }

        [Test]
        public void SameSeed_SameChoices_SameCareer()
        {
            var a = CareerKit.NewAtCreate(31); var b = CareerKit.NewAtCreate(31);
            CareerBot.Play(a, 2, 3); CareerBot.Play(b, 2, 3);
            Assert.AreEqual(a.CaptureState(), b.CaptureState());
        }

        [Test]
        public void DifferentChoices_GiveDifferentCareers()
        {
            var a = CareerKit.NewAtCreate(31); var b = CareerKit.NewAtCreate(31);
            CareerBot.Play(a, 1, 2); CareerBot.Play(b, 1, 5);
            Assert.AreNotEqual(a.CaptureState(), b.CaptureState());
        }

        /// <summary>Довести кар'єру до підсумку шоу тижня 8 (останнє шоу сезону) — далі ContinueFromShow веде в кінець сезону.</summary>
        private static CareerSession ToLastShowResult(ulong seed)
        {
            var s = CareerKit.NewHero(seed);
            var rep = new CareerRunReport();
            int guard = 0;
            while (!(s.Stage == CareerStage.Show && s.State.Phase == ShowPhase.Result && s.State.Week == CareerRules.WeeksPerSeason) && guard++ < 1000)
            {
                if (s.Stage == CareerStage.SeasonEnd) Assert.Fail("сезон закінчився раніше");
                CareerBot.Step(s, 0, rep);
            }
            return s;
        }

        [Test]
        public void Season_EndsAfterEightWeeks_WithReport()
        {
            var s = ToLastShowResult(5);
            s.ContinueFromShow();
            Assert.AreEqual(CareerStage.SeasonEnd, s.Stage);
            var r = s.GetSeasonReport();
            Assert.AreEqual(1, r.Season);
            Assert.AreEqual(s.Popularity, r.Popularity);
        }

        [Test]
        public void ZeroPopularityAtSeasonEnd_FiresBackToLocalLevel_AndCareerContinues()
        {
            var s = ToLastShowResult(6);
            s.Feud.State.Audience = 0;
            s.ContinueFromShow();
            var r = s.GetSeasonReport();
            Assert.IsTrue(r.Fired);
            Assert.IsFalse(s.CanStartTitleMatch, "звільненому титул не світить");
            s.StartNextSeason();
            Assert.AreEqual(CareerStage.Week, s.Stage, "не кінець гри");
            Assert.AreEqual(2, s.State.Season);
            Assert.AreEqual(1, s.State.Week);
            Assert.AreEqual(CareerRules.FiredPopularity, s.Popularity);
            Assert.AreEqual(CareerRules.FiredTrust, s.Trust);
            Assert.IsFalse(s.IsChampion);
            Assert.AreEqual(1, s.State.FiredCount);
        }

        [Test]
        public void HighTrust_GivesTitleShot_ThatCanBePlayedAndWon_OrSkipped()
        {
            bool wonSomewhere = false;
            for (ulong seed = 1; seed <= 12; seed++)
            {
                var s = ToLastShowResult(seed);
                s.Feud.State.Trust = CareerRules.MaxTrust;
                s.ContinueFromShow();
                Assert.IsTrue(s.GetSeasonReport().TitleEligible);
                Assert.IsFalse(s.CanStartNextSeason, "спершу титульний шанс або відмова");
                s.StartTitleMatch();
                Assert.AreEqual(CareerStage.Show, s.Stage);
                Assert.IsTrue(s.GetShowCard().IsTitle);
                Assert.AreEqual(s.Content.BeltHolderId, s.GetShowCard().Opponent.Id);
                s.StartShow();
                CareerBot.PlayMatch(s, 1, new CareerRunReport());
                var res = s.FinishMatch();
                s.ContinueFromShow();
                Assert.AreEqual(CareerStage.SeasonEnd, s.Stage);
                Assert.IsTrue(s.GetSeasonReport().TitleDone);
                Assert.AreEqual(res.Winner == MatchWinner.Player, s.IsChampion);
                wonSomewhere |= s.IsChampion;
                Assert.IsTrue(s.CanStartNextSeason);
                s.StartNextSeason();
                Assert.AreEqual(2, s.State.Season);
            }
            Assert.IsTrue(wonSomewhere, "хоч раз титул має вигратись");
            var t = ToLastShowResult(3);
            t.Feud.State.Trust = CareerRules.MaxTrust;
            t.ContinueFromShow();
            t.SkipTitle();
            Assert.IsTrue(t.CanStartNextSeason);
        }

        [Test]
        public void LowTrust_NoNpcFeud_NoTitleShot()
        {
            var s = ToLastShowResult(9);
            s.Feud.State.Trust = 1;
            foreach (var n in s.Content.Roster) s.State.SetNpcFeud(n.Id, 0);
            s.State.FeudResolved = true;
            s.ContinueFromShow();
            Assert.IsFalse(s.GetSeasonReport().TitleEligible);
            Assert.IsTrue(s.CanStartNextSeason);
        }

        [Test]
        public void NpcFeudLevelFour_GivesTitleShot()
        {
            var s = ToLastShowResult(10);
            s.Feud.State.Trust = 1;
            s.State.FeudResolved = true;
            s.State.SetNpcFeud("baron", 4);
            s.ContinueFromShow();
            Assert.IsTrue(s.GetSeasonReport().TitleEligible);
        }

        [Test]
        public void ShowBooker_FeeByPosition_BigShowDoubles()
        {
            Assert.Less(ShowBooker.Fee(CardPosition.Opener, false), ShowBooker.Fee(CardPosition.Midcard, false));
            Assert.Less(ShowBooker.Fee(CardPosition.SemiMain, false), ShowBooker.Fee(CardPosition.Main, false));
            Assert.AreEqual(ShowBooker.Fee(CardPosition.Midcard, false) * 2, ShowBooker.Fee(CardPosition.Midcard, true));
            Assert.AreEqual(CardPosition.Opener, CareerRules.PositionFor(0));
            Assert.AreEqual(CardPosition.Midcard, CareerRules.PositionFor(3));
            Assert.AreEqual(CardPosition.SemiMain, CareerRules.PositionFor(6));
            Assert.AreEqual(CardPosition.Main, CareerRules.PositionFor(10));
        }

        [Test]
        public void WeeklyShow_PaysFeeByPosition_AndLivingIsPaidAtWeekEnd()
        {
            var s = CareerKit.NewHero(12);
            int money = s.Money;
            var card = s.GetShowCard();
            Assert.IsFalse(card.IsBig);
            s.EndWeek();
            s.StartShow();
            CareerBot.PlayMatch(s, 0, new CareerRunReport());
            var r = s.FinishMatch();
            Assert.AreEqual(money + r.TotalFee, s.Money);
            Assert.AreEqual(card.Fee, r.Fee);
            Assert.AreEqual(ShowPhase.Result, s.State.Phase);
            s.ContinueFromShow();
            Assert.AreEqual(2, s.State.Week);
            Assert.AreEqual(money + r.TotalFee - CareerRules.LivingCost, s.Money, "проживання списується наприкінці тижня");
        }

        [Test]
        public void Booker_PlanIsGivenOnlyWithTrust_AndNoDqFromFeudThree()
        {
            var c = CareerKit.Content();
            var komet = c.FindNpc("komet");
            Assert.AreEqual(BookerPlan.None, ShowBooker.PlanFor(komet, 5, 1));
            Assert.AreEqual(BookerPlan.LoseClean, ShowBooker.PlanFor(komet, 2, 2));
            Assert.AreEqual(BookerPlan.NoCleanLoss, ShowBooker.PlanFor(komet, 2, 4));
            Assert.AreEqual(BookerPlan.WinClean, ShowBooker.PlanFor(c.FindNpc("stalevyi"), 8, 3));
            Assert.AreEqual(BookerPlan.WinAny, ShowBooker.PlanFor(c.FindNpc("vdova"), 5, 3));
            Assert.AreEqual(MatchStipulations.None, ShowBooker.StipulationsFor(2));
            Assert.AreEqual(MatchStipulations.NoDq, ShowBooker.StipulationsFor(3));
        }

        [Test]
        public void Money_PoorWeek_ShortensNextWeek()
        {
            var s = CareerKit.NewHero(13);
            s.EndWeek();
            s.StartShow();
            CareerBot.PlayMatch(s, 0, new CareerRunReport());
            s.FinishMatch();
            s.State.Money = 3;   // менше за проживання
            s.ContinueFromShow();
            Assert.IsTrue(s.State.Strained);
            Assert.AreEqual(0, s.Money);
            Assert.AreEqual(CareerRules.StrainedActions, s.State.ActionsLeft);
            Assert.AreEqual(CareerRules.StrainedActions, s.GetWeekView().ActionsTotal);
        }
    }
}
