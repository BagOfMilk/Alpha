using System;
using System.Collections.Generic;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Core.Ring.Match;
using Game.Gameplay.Ring.Models;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Контракт переходу (§6.4; власник 11.11.2026: «Пастка «виключне право» — так», аванс 40 / 80 / 150 — ПЛЕЙСХОЛДЕР): аванс,
    /// пастка «виключне право» (аванс ×2, гонорар ×0,7, без виїзних шоу й підробітку, строк 2 сезони, вийти раніше — штраф),
    /// дрібний шрифт видно кидком Техніки або з менеджеркою; менеджерка бере 15 % кожного гонорару й виторговує +25 % авансу.
    /// </summary>
    public class ContractTests
    {
        private static CareerContent C { get { return CareerKit.Content(); } }

        // ---------------- помічники ----------------

        private static void PlayToLastShowResult(CareerSession s)
        {
            var rep = new CareerRunReport();
            for (int guard = 0; !(s.Stage == CareerStage.Show && s.State.Phase == ShowPhase.Result && s.State.Week == CareerRules.WeeksInSeason(s.State.Season)); guard++)
            {
                Assert.Less(guard, 3000);
                CareerBot.Step(s, 0, rep);
            }
        }

        /// <summary>Кінець сезону у федерації <paramref name="tier"/> з популярністю й довірою; титул розіграно.</summary>
        private static CareerSession AtSeasonEnd(ulong seed, int tier, int pop, int trust, bool champion = false)
        {
            var s = CareerKit.NewHero(seed);
            PlayToLastShowResult(s);
            if (tier != s.FederationTier) s.EnterFederation(tier, pop, trust);
            s.Feud.State.Audience = pop; s.Feud.State.Trust = trust;
            s.State.BeltHolder = champion ? CareerState.PlayerHolder : s.Federation.BeltOf(s.State.Female);
            s.ContinueFromShow();
            Assert.AreEqual(CareerStage.SeasonEnd, s.Stage);
            s.Feud.State.Audience = pop; s.Feud.State.Trust = trust;
            s.State.Report.TitleDone = true;
            return s;
        }

        /// <summary>Пропозиція з місцевої в регіональну (аванс 40).</summary>
        private static CareerSession Offer(ulong seed, bool? exclusive)
        {
            var s = AtSeasonEnd(seed, 1, 3, CareerRules.OfferTrust);
            s.ForcedOfferExclusive = exclusive;
            Assert.IsTrue(s.OfferAvailable);
            return s;
        }

        /// <summary>Перший шоу нового сезону — до підсумку.</summary>
        private static ShowResult PlayFirstShow(CareerSession s)
        {
            var rep = new CareerRunReport();
            for (int guard = 0; !(s.Stage == CareerStage.Show && s.State.Phase == ShowPhase.Result); guard++)
            {
                Assert.Less(guard, 3000);
                CareerBot.Step(s, 0, rep);
            }
            return s.State.Last;
        }

        /// <summary>Тиждень 4, усі події сезону до ev_manager уже були — наступне «Закулісся» відкриє її, якщо умови виконано.</summary>
        private static CareerSession BeforeManagerScene(ulong seed)
        {
            var s = CareerKit.NewHero(seed);
            s.State.Week = 4;
            foreach (var sc in s.Content.Backstage.Scenes)
            {
                if (sc.Id == "ev_manager") break;
                if (sc.Kind == BackstageKind.Event) s.State.BackstageSeen.Add(s.State.Season + ":" + sc.Id);
            }
            return s;
        }

        private static RingText Text()
        {
            return RingText.FromJson(CareerKit.Read("strings_uk.json"), CareerKit.Read("ui_uk.json"), CareerKit.Read("backstage_uk.json"));
        }

        // ---------------- аванс ----------------

        [Test]
        public void Advance_IsTheFederations_DoubledByTheTrap_PlusAQuarterWithTheManager()
        {
            CollectionAssert.AreEqual(new[] { 0, 40, 80, 150 }, new[] { C.Federation(1).Advance, C.Federation(2).Advance, C.Federation(3).Advance, C.Federation(4).Advance });
            var f = C.Federation(3);
            Assert.AreEqual(80, CareerRules.Advance(f, false, false));
            Assert.AreEqual(160, CareerRules.Advance(f, true, false), "пастка — аванс ×2");
            Assert.AreEqual(100, CareerRules.Advance(f, false, true), "менеджерка — +25 %");
            Assert.AreEqual(200, CareerRules.Advance(f, true, true));
            Assert.AreEqual(160, CareerRules.ExitPenalty(f), "штраф — аванс пастки, без надбавки менеджерки");
        }

        [Test]
        public void HonestOffer_PaysTheAdvanceOnTransfer_FeeAndAwayShowsAsUsual()
        {
            var s = Offer(1, false);
            var c = s.GetOfferContract();
            Assert.AreEqual(2, c.ToTier);
            Assert.AreEqual(40, c.Advance);
            Assert.AreEqual(ContractSight.Unread, c.Sight);
            Assert.IsTrue(c.CanRead);
            Assert.IsFalse(c.ExclusiveSeen || c.HintSomething);
            Assert.AreEqual(0, c.ExitPenalty);
            int money = s.State.Money;
            s.AcceptOffer();
            Assert.AreEqual(2, s.FederationTier);
            Assert.AreEqual(money + 40, s.State.Money, "аванс — при переході");
            Assert.IsFalse(s.State.ContractExclusive);
            Assert.IsNull(s.GetOfferContract(), "рішення ухвалено — контракту на екрані немає");
            s.StartNextSeason();
            Assert.IsFalse(s.ExclusiveActive);
            Assert.AreEqual(ShowBooker.Fee(s.State.Position, s.State.IsBig, s.Federation.FeePercent), s.GetShowCard().Fee);
            Assert.AreNotEqual("exclusive", s.SideJob().Reason);
        }

        // ---------------- пастка ----------------

        [Test]
        public void Trap_DoubleAdvance_ThenFeeSeventyPercent_NoAwayShowsNoSideJob_ForTwoSeasons()
        {
            var s = Offer(2, true);
            Assert.AreEqual(80, s.GetOfferContract().Advance, "приманка — аванс ×2");
            int money = s.State.Money, season = s.State.Season;
            s.AcceptOffer();
            Assert.AreEqual(money + 80, s.State.Money);
            Assert.IsTrue(s.State.ContractExclusive);
            Assert.AreEqual(season + CareerRules.ExclusiveSeasons, s.State.ContractEndSeason, "строк — два сезони нової федерації");
            Assert.AreEqual(80, s.State.ContractPenalty);

            s.StartNextSeason();
            Assert.IsTrue(s.ExclusiveActive);
            s.State.Money = 500;
            s.State.ContractExclusive = false;
            Assert.IsTrue(s.CanGoAway, "без пастки виїзд цього тижня можливий");
            s.State.ContractExclusive = true;
            Assert.IsFalse(s.CanGoAway, "без виїзних шоу");
            Assert.IsFalse(s.GoAway());
            int left = s.State.ActionsLeft;
            var job = s.SideJob();
            Assert.IsFalse(job.Ok);
            Assert.AreEqual("exclusive", job.Reason, "без підробітку");
            Assert.AreEqual(left, s.State.ActionsLeft, "дія не витрачена");
            int baseFee = ShowBooker.Fee(s.State.Position, s.State.IsBig, s.Federation.FeePercent);
            Assert.AreEqual(baseFee * CareerRules.ExclusiveFeePercent / 100, s.GetShowCard().Fee, "гонорар ×0,7");

            var r = PlayFirstShow(s);
            Assert.AreEqual(ShowBooker.Fee(r.Position, r.IsBig, s.Federation.FeePercent) * CareerRules.ExclusiveFeePercent / 100, r.Fee);
            Assert.AreEqual(0, r.ManagerCut);

            // після строку — чесний контракт: гонорар звичайний, виїзд і підробіток знову є
            s.State.Season = s.State.ContractEndSeason + 1;
            Assert.IsFalse(s.ExclusiveActive);
            Assert.AreEqual(0, s.ExitPenaltyDue);
        }

        [Test]
        public void LeavingTheTrapEarly_CostsThePenalty_OrIsLocked_WhenThePurseIsShort_OnTimeIsFree()
        {
            var s = AtSeasonEnd(3, 2, 5, CareerRules.OfferTrust);
            s.ForcedOfferExclusive = false;
            s.State.ContractExclusive = true;
            s.State.ContractEndSeason = s.State.Season + 1;   // строк ще сезон — вихід ранній
            s.State.ContractPenalty = 80;

            s.State.Money = 50;
            Assert.AreEqual(80, s.ExitPenaltyDue);
            Assert.IsTrue(s.ExitLocked);
            Assert.IsFalse(s.OfferAvailable, "пастка тримає: грошей на штраф немає");
            Assert.Throws<InvalidOperationException>(() => s.DeclineOffer(), "і відмови немає — рішення не ухвалюється");
            var c = s.GetOfferContract();
            Assert.IsNotNull(c, "контракт видно й тоді — екран пояснює, що тримає");
            Assert.IsTrue(c.ExitLocked);
            var m = new SeasonOfferModel(s, Text());
            Assert.IsFalse(m.CanDecide);
            StringAssert.Contains("80", m.PenaltyText);
            StringAssert.Contains("50", m.PenaltyText);

            s.State.Money = 200;
            Assert.IsTrue(s.OfferAvailable);
            StringAssert.Contains("80", new SeasonOfferModel(s, Text()).PenaltyText);
            s.AcceptOffer();
            Assert.AreEqual(3, s.FederationTier);
            Assert.AreEqual(200 - 80 + C.Federation(3).Advance, s.State.Money, "штраф знято, аванс нової федерації заплачено");
            Assert.IsFalse(s.State.ContractExclusive, "стара пастка скінчилась із переходом");

            var t = AtSeasonEnd(4, 2, 5, CareerRules.OfferTrust);
            t.State.ContractExclusive = true;
            t.State.ContractEndSeason = t.State.Season;   // останній сезон строку — вихід уже вчасно
            t.State.ContractPenalty = 80;
            t.State.Money = 0;
            Assert.AreEqual(0, t.ExitPenaltyDue);
            Assert.IsTrue(t.OfferAvailable);
        }

        [Test]
        public void Demotion_EndsTheTrap_TheFederationLetYouGo()
        {
            var s = AtSeasonEnd(5, 2, 0, 1);
            s.State.ContractExclusive = true;
            s.State.ContractEndSeason = s.State.Season + 1;
            s.State.ContractPenalty = 80;
            Assert.IsTrue(s.State.Report.Fired);
            s.StartNextSeason();
            Assert.AreEqual(1, s.FederationTier);
            Assert.IsFalse(s.State.ContractExclusive);
            Assert.IsFalse(s.ExclusiveActive);
        }

        [Test]
        public void TheTermRunsOut_TheContractBecomesHonest()
        {
            var s = AtSeasonEnd(14, 2, 5, CareerRules.OfferTrust - 1);
            s.State.ContractExclusive = true;
            s.State.ContractEndSeason = s.State.Season + 1;
            s.State.ContractPenalty = 80;
            Assert.IsFalse(s.State.Report.Fired);
            s.StartNextSeason();
            Assert.IsTrue(s.ExclusiveActive, "останній сезон строку");
            Assert.AreEqual(0, s.ExitPenaltyDue, "на кінці останнього сезону піти вже не рано");

            var t = AtSeasonEnd(15, 2, 5, CareerRules.OfferTrust - 1);
            t.State.ContractExclusive = true;
            t.State.ContractEndSeason = t.State.Season;
            t.State.ContractPenalty = 80;
            t.StartNextSeason();
            Assert.IsFalse(t.State.ContractExclusive, "строк скінчився — контракт чесний");
            Assert.AreEqual(0, t.State.ContractPenalty);
            Assert.AreEqual(0, t.State.ContractEndSeason);
        }

        [Test]
        public void ChampionFarewell_TermsAreSignedAtAccept_AdvanceIsPaidWhenTheMoveHappens()
        {
            var s = AtSeasonEnd(6, 1, 3, CareerRules.OfferTrust - CareerRules.OfferChampionTrustBonus, champion: true);
            s.ForcedOfferExclusive = true;
            Assert.IsTrue(s.OfferAvailable);
            int money = s.State.Money;
            s.AcceptOffer();
            Assert.IsTrue(s.State.Farewell);
            Assert.IsTrue(s.State.PendingExclusive);
            Assert.AreEqual(80, s.State.PendingAdvance);
            Assert.AreEqual(money, s.State.Money, "до прощального матчу аванс не платять");
            // сейв посеред прощання — підписане не губиться
            var t = CareerKit.Fresh(6, C);
            t.RestoreState(s.CaptureState());
            Assert.IsTrue(t.State.PendingExclusive);
            Assert.AreEqual(80, t.State.PendingAdvance);

            s.StartShow();
            FeudBotDriver.PlayMatch(s.Engine, new MatchGoal { Aim = MatchAim.Lose, Style = MatchStyle.Clean });
            s.FinishMatch();
            int afterShow = s.State.Money;
            s.ContinueFromShow();
            Assert.AreEqual(2, s.FederationTier);
            Assert.AreEqual(afterShow + 80, s.State.Money, "аванс — коли перехід стався");
            Assert.IsTrue(s.State.ContractExclusive);
            Assert.IsFalse(s.State.PendingExclusive);
            Assert.AreEqual(0, s.State.PendingAdvance);
        }

        // ---------------- дрібний шрифт ----------------

        [Test]
        public void FinePrint_TechniqueRollOncePerOffer_TenSeesTheClause_SevenSeesSomething_SixNothing()
        {
            var seen = new HashSet<int>();
            foreach (bool trap in new[] { true, false })
                for (ulong seed = 10; seed < 40 && seen.Count < 6; seed++)
                {
                    var s = Offer(seed, trap);
                    var r = s.ReadOfferContract();
                    Assert.IsTrue(r.Ok);
                    Assert.AreEqual(s.Player.Get(RingAttr.Technique), r.Modifier, "кидок Техніки");
                    Assert.AreEqual(r.Roll + r.Modifier, r.Total);
                    var c = s.GetOfferContract();
                    Assert.AreEqual(r.Sight, c.Sight);
                    Assert.IsFalse(c.CanRead, "раз на пропозицію");
                    Assert.AreEqual("already", s.ReadOfferContract().Reason);
                    var m = new SeasonOfferModel(s, Text());
                    switch (r.Tier)
                    {
                        case RollTier.Strong:
                            Assert.AreEqual(ContractSight.Seen, c.Sight);
                            Assert.AreEqual(trap, c.ExclusiveSeen);
                            if (trap)
                            {
                                Assert.AreEqual(CareerRules.ExclusiveFeePercent, c.ExclusiveFeePercent);
                                Assert.AreEqual(CareerRules.ExclusiveSeasons, c.ExclusiveSeasons);
                                Assert.AreEqual(80, c.ExclusivePenalty);
                                StringAssert.Contains("виключне право", m.FinePrintText);
                            }
                            else StringAssert.Contains("чесний", m.FinePrintText);
                            break;
                        case RollTier.Partial:
                            Assert.AreEqual(ContractSight.Hint, c.Sight);
                            Assert.AreEqual(trap, c.HintSomething);
                            Assert.IsFalse(c.ExclusiveSeen, "7–9 — умови пастки не видно");
                            Assert.AreEqual(0, c.ExclusivePenalty);
                            break;
                        default:
                            Assert.AreEqual(ContractSight.Missed, c.Sight);
                            Assert.IsFalse(c.ExclusiveSeen || c.HintSomething);
                            break;
                    }
                    Assert.AreEqual(trap && (r.Tier != RollTier.Miss), m.FinePrintWarns);
                    seen.Add((int)r.Tier + (trap ? 0 : 3));
                    // прочитане переживає сейв
                    var t = CareerKit.Fresh(seed, C);
                    t.RestoreState(s.CaptureState());
                    t.ForcedOfferExclusive = trap;
                    Assert.AreEqual(c.Sight, t.GetOfferContract().Sight);
                }
            Assert.GreaterOrEqual(seen.Count, 4, "кидки дали різні ступені");
        }

        [Test]
        public void FinePrint_BeforeReading_OnlyTheAdvanceShows_AndTheChancesAreGivenBeforeTheRoll()
        {
            var s = Offer(7, true);
            var m = new SeasonOfferModel(s, Text());
            Assert.IsTrue(m.ShowContract);
            StringAssert.Contains("80", m.AdvanceText);
            Assert.AreEqual(Text().Get("c.offer.fine.unread"), m.FinePrintText, "до читання пастки не видно");
            Assert.IsFalse(m.FinePrintWarns);
            Assert.IsTrue(m.CanRead);
            Assert.IsNotNull(m.ReadChanceText);
            StringAssert.DoesNotContain("⟦", m.ReadChanceText + m.AdvanceText + m.FinePrintText + m.ReadText);
            Assert.IsNull(m.ManagerNote);
            Assert.IsNull(m.PenaltyText);
            m.Read();
            Assert.IsFalse(m.CanRead);
            Assert.IsNull(m.ReadChanceText);
            s.AcceptOffer();
            Assert.IsFalse(m.ShowContract);
        }

        [Test]
        public void Declined_TheNextSeasonsPaperIsReadAnew()
        {
            var s = Offer(7, true);
            Assert.IsTrue(s.ReadOfferContract().Ok);
            Assert.AreNotEqual(0, s.State.OfferRead);
            s.DeclineOffer();
            s.StartNextSeason();
            Assert.AreEqual(0, s.State.OfferRead, "новий кінець сезону — новий папір, читати знову");
        }

        [Test]
        public void TheTrapShare_IsDeterministic_AboutAThird_AndNewEachSeason()
        {
            int traps = 0, n = 120, changed = 0;
            for (ulong seed = 1; seed <= (ulong)n; seed++)
            {
                var s = CareerKit.NewHero(seed);
                bool a = s.OfferIsExclusive;
                Assert.AreEqual(a, CareerKit.NewHero(seed).OfferIsExclusive, "те саме зерно — той самий папір");
                if (a) traps++;
                s.State.Season++;
                if (s.OfferIsExclusive != a) changed++;
            }
            Assert.That(traps * 100 / n, Is.InRange(22, 46), "пастка — приблизно кожна третя пропозиція");
            Assert.Greater(changed, 0, "повторна пропозиція — новий папір");
            var f = CareerKit.NewHero(1);
            f.ForcedOfferExclusive = true;
            Assert.IsTrue(f.OfferIsExclusive);
            var g = CareerKit.Fresh(1, C);
            g.RestoreState(f.CaptureState());
            Assert.IsNull(g.ForcedOfferExclusive, "примус огляду — не в сейві");
        }

        // ---------------- менеджерка ----------------

        [Test]
        public void Manager_SeesTheTrapWithoutRolling_AndBargainsAQuarterMoreAdvance()
        {
            var s = Offer(8, true);
            s.State.Manager = true;
            var c = s.GetOfferContract();
            Assert.AreEqual(ContractSight.Seen, c.Sight);
            Assert.IsTrue(c.ExclusiveSeen);
            Assert.IsFalse(c.CanRead);
            Assert.AreEqual("manager", s.ReadOfferContract().Reason);
            Assert.AreEqual(100, c.Advance, "40 × 2 × 1,25");
            var m = new SeasonOfferModel(s, Text());
            Assert.IsNotNull(m.ManagerNote);
            StringAssert.Contains("+25", m.AdvanceText);
            int money = s.State.Money;
            s.AcceptOffer();
            Assert.AreEqual(money + 100, s.State.Money);
            Assert.AreEqual(80, s.State.ContractPenalty, "штраф — без надбавки менеджерки");
        }

        [Test]
        public void Manager_TakesFifteenPercentOfTheWholeFee_BonusesIncluded_TheFeeLineStillAddsUp_FiredAnyTime()
        {
            CareerSession withM = null;
            ShowResult b = null;
            bool bonusCounted = false;
            for (ulong seed = 9; seed < 60 && !bonusCounted; seed++)
            {
                var plain = CareerKit.NewHero(seed);
                var m = CareerKit.NewHero(seed);
                m.State.Manager = true;
                int moneyBefore = m.State.Money;
                var a = PlayFirstShow(plain);
                var r = PlayFirstShow(m);
                Assert.AreEqual(a.Winner, r.Winner, "той самий матч");
                Assert.AreEqual(0, a.ManagerCut);
                Assert.AreEqual(CareerRules.ManagerCut(a.TotalFee), r.ManagerCut, "15 % від усього гонорару");
                Assert.Greater(r.ManagerCut, 0);
                Assert.AreEqual(a.FeeWinBonus, r.FeeWinBonus, "частку знято з бази, бонуси як є");
                Assert.AreEqual(a.FeeHypeBonus, r.FeeHypeBonus);
                Assert.AreEqual(a.TotalFee - r.ManagerCut, r.TotalFee);
                Assert.AreEqual(r.Fee + r.FeeWinBonus + r.FeeHypeBonus, r.TotalFee, "рядок «гонорар (база + бонуси)» сходиться");
                Assert.AreEqual(plain.State.Money - r.ManagerCut, m.State.Money, "у гаманець — гонорар без частки");
                if (CareerRules.ManagerCut(a.TotalFee) != CareerRules.ManagerCut(a.Fee)) bonusCounted = true;
                withM = m; b = r;
            }
            Assert.IsTrue(bonusCounted, "хоч один матч, де бонуси змінили частку менеджерки");
            var t = CareerKit.Fresh(withM.State.Seed, C);
            t.RestoreState(withM.CaptureState());
            Assert.AreEqual(b.ManagerCut, t.State.Last.ManagerCut);
            Assert.IsTrue(t.HasManager);

            Assert.IsTrue(withM.FireManager(), "звільнити — будь-коли");
            Assert.IsFalse(withM.HasManager);
            Assert.IsFalse(withM.FireManager());
        }

        [Test]
        public void ManagerScene_ComesFromPopularityThree_HiresHer_AndDoesNotComeBackWhileSheWorks()
        {
            var scene = C.Backstage.Find("ev_manager");
            Assert.IsNotNull(scene);
            Assert.AreEqual(BackstageKind.Event, scene.Kind);
            Assert.AreEqual(BackstageAuto.None, scene.Auto);
            Assert.IsTrue(Text().Has("speaker.manager"));

            var low = BeforeManagerScene(11);
            low.Feud.State.Audience = CareerRules.ManagerPopularity - 1;
            Assert.AreNotEqual("ev_manager", low.Backstage().SceneId, "популярність 2 — менеджерка ще не цікавиться");

            var hired = BeforeManagerScene(13);
            hired.Feud.State.Audience = CareerRules.ManagerPopularity;
            hired.State.Manager = true;
            Assert.AreNotEqual("ev_manager", hired.Backstage().SceneId, "менеджерка вже працює — сцени немає");

            var s = BeforeManagerScene(12);
            s.Feud.State.Audience = CareerRules.ManagerPopularity;
            var r = s.Backstage();
            Assert.IsTrue(r.NeedsScene);
            Assert.AreEqual("ev_manager", r.SceneId);
            var run = s.CurrentBackstage;
            for (int guard = 0; !run.IsFinished; guard++)
            {
                Assert.Less(guard, 50);
                if (run.Current.Type == PromoNodeType.Choice) run.Choose("hire"); else run.Advance();
            }
            s.FinishBackstage();
            Assert.IsTrue(s.HasManager, "найнято сценою");
            var t = CareerKit.Fresh(12, C);
            t.RestoreState(s.CaptureState());
            Assert.IsTrue(t.HasManager, "менеджерка — у сейві");

        }
    }
}
