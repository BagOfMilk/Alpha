using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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

        /// <summary>Регіональна федерація — куди веде пропозиція з місцевої. Числа тестів — з CareerRules, а не вшиті: аванси й відсотки — плейсхолдери.</summary>
        private static FederationDef Regional { get { return C.Federation(2); } }
        private static int HonestAdvance { get { return CareerRules.Advance(Regional, false, false); } }
        private static int TrapAdvance { get { return CareerRules.Advance(Regional, true, false); } }
        private static int Penalty { get { return CareerRules.ExitPenalty(Regional); } }

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

        /// <summary>Пропозиція з місцевої в регіональну (аванс <see cref="HonestAdvance"/>, у пастці — <see cref="TrapAdvance"/>).</summary>
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
            SeenBeforeManager(s);
            return s;
        }

        /// <summary>
        /// Події цього сезону, що стоять у файлі до ev_manager, уже були; сцени дуги («arc») перевіряються першими (рев'ю ПМ 927b86c),
        /// тож і вони — теж, інакше «Друг у лазареті» перехопив би менеджерку.
        /// </summary>
        private static void SeenBeforeManager(CareerSession s)
        {
            bool before = true;
            foreach (var sc in s.Content.Backstage.Scenes)
            {
                if (sc.Id == "ev_manager") before = false;
                if (sc.Kind == BackstageKind.Event && (before || sc.Arc)) s.State.BackstageSeen.Add(s.State.Season + ":" + sc.Id);
            }
        }

        /// <summary>Пройти сцену менеджерки з вибором <paramref name="choice"/> (hire / later / refuse).</summary>
        private static CareerSession PlayManagerScene(ulong seed, string choice)
        {
            var s = BeforeManagerScene(seed);
            s.Feud.State.Audience = CareerRules.ManagerPopularity;
            var r = s.Backstage();
            Assert.IsTrue(r.NeedsScene);
            Assert.AreEqual("ev_manager", r.SceneId);
            var run = s.CurrentBackstage;
            for (int guard = 0; !run.IsFinished; guard++)
            {
                Assert.Less(guard, 50);
                if (run.Current.Type == PromoNodeType.Choice) run.Choose(choice); else run.Advance();
            }
            s.FinishBackstage();
            return s;
        }

        /// <summary>Наступний сезон, тиждень 4: події до ev_manager уже були, популярність — досить для менеджерки.</summary>
        private static void ToNextSeasonBeforeManagerScene(CareerSession s)
        {
            s.State.Season++; s.State.Week = 4; s.State.ActionsLeft = 3;
            SeenBeforeManager(s);
            s.Feud.State.Audience = CareerRules.ManagerPopularity;
        }

        private static RingText Text()
        {
            return RingText.FromJson(CareerKit.Read("strings_uk.json"), CareerKit.Read("ui_uk.json"), CareerKit.Read("backstage_uk.json"));
        }

        // ---------------- аванс ----------------

        [Test]
        public void Advance_IsTheFederations_DoubledByTheTrap_PlusAQuarterWithTheManager()
        {
            Assert.AreEqual(0, C.Federation(1).Advance, "у місцеву не переходять — авансу немає");
            for (int t = 2; t <= C.Federations.Count; t++)
                Assert.Greater(C.Federation(t).Advance, C.Federation(t - 1).Advance, "вища федерація — більший аванс");
            var f = C.Federation(3);
            int honest = CareerRules.Advance(f, false, false), trap = CareerRules.Advance(f, true, false);
            Assert.AreEqual(f.Advance, honest);
            Assert.AreEqual(f.Advance * CareerRules.ExclusiveAdvancePercent / 100, trap, "пастка — аванс ×2");
            Assert.Greater(trap, honest);
            Assert.AreEqual(honest * CareerRules.ManagerAdvancePercent / 100, CareerRules.Advance(f, false, true), "менеджерка — +25 %");
            Assert.Greater(CareerRules.Advance(f, false, true), honest);
            Assert.AreEqual(trap * CareerRules.ManagerAdvancePercent / 100, CareerRules.Advance(f, true, true));
            Assert.AreEqual(trap, CareerRules.ExitPenalty(f), "штраф — аванс пастки, без надбавки менеджерки");
        }

        [Test]
        public void HonestOffer_PaysTheAdvanceOnTransfer_FeeAndAwayShowsAsUsual()
        {
            var s = Offer(1, false);
            var c = s.GetOfferContract();
            Assert.AreEqual(2, c.ToTier);
            Assert.AreEqual(HonestAdvance, c.Advance);
            Assert.AreEqual(ContractSight.Unread, c.Sight);
            Assert.IsTrue(c.CanRead);
            Assert.IsFalse(c.ExclusiveSeen || c.HintSomething);
            Assert.AreEqual(0, c.ExitPenalty);
            int money = s.State.Money;
            s.AcceptOffer();
            Assert.AreEqual(2, s.FederationTier);
            Assert.AreEqual(money + HonestAdvance, s.State.Money, "аванс — при переході");
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
            Assert.AreEqual(TrapAdvance, s.GetOfferContract().Advance, "приманка — аванс ×2");
            int money = s.State.Money, season = s.State.Season;
            s.AcceptOffer();
            Assert.AreEqual(money + TrapAdvance, s.State.Money);
            Assert.IsTrue(s.State.ContractExclusive);
            Assert.AreEqual(season + CareerRules.ExclusiveSeasons, s.State.ContractEndSeason, "строк — два сезони нової федерації");
            Assert.AreEqual(Penalty, s.State.ContractPenalty);

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
            s.State.ContractPenalty = Penalty;

            s.State.Money = Penalty / 2;
            Assert.AreEqual(Penalty, s.ExitPenaltyDue);
            Assert.IsTrue(s.ExitLocked);
            Assert.IsFalse(s.OfferAvailable, "пастка тримає: грошей на штраф немає");
            Assert.Throws<InvalidOperationException>(() => s.DeclineOffer(), "і відмови немає — рішення не ухвалюється");
            var c = s.GetOfferContract();
            Assert.IsNotNull(c, "контракт видно й тоді — екран пояснює, що тримає");
            Assert.IsTrue(c.ExitLocked);
            var m = new SeasonOfferModel(s, Text());
            Assert.IsFalse(m.CanDecide);
            StringAssert.Contains(Penalty.ToString(), m.PenaltyText);
            StringAssert.Contains((Penalty / 2).ToString(), m.PenaltyText);

            s.State.Money = Penalty + 120;
            Assert.IsTrue(s.OfferAvailable);
            StringAssert.Contains(Penalty.ToString(), new SeasonOfferModel(s, Text()).PenaltyText);
            s.AcceptOffer();
            Assert.AreEqual(3, s.FederationTier);
            Assert.AreEqual(120 + CareerRules.Advance(C.Federation(3), false, false), s.State.Money, "штраф знято, аванс нової федерації заплачено");
            Assert.IsFalse(s.State.ContractExclusive, "стара пастка скінчилась із переходом");

            var t = AtSeasonEnd(4, 2, 5, CareerRules.OfferTrust);
            t.State.ContractExclusive = true;
            t.State.ContractEndSeason = t.State.Season;   // останній сезон строку — вихід уже вчасно
            t.State.ContractPenalty = Penalty;
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
            s.State.ContractPenalty = Penalty;
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
            s.State.ContractPenalty = Penalty;
            Assert.IsFalse(s.State.Report.Fired);
            s.StartNextSeason();
            Assert.IsTrue(s.ExclusiveActive, "останній сезон строку");
            Assert.AreEqual(0, s.ExitPenaltyDue, "на кінці останнього сезону піти вже не рано");

            var t = AtSeasonEnd(15, 2, 5, CareerRules.OfferTrust - 1);
            t.State.ContractExclusive = true;
            t.State.ContractEndSeason = t.State.Season;
            t.State.ContractPenalty = Penalty;
            t.StartNextSeason();
            Assert.IsFalse(t.State.ContractExclusive, "строк скінчився — контракт чесний");
            Assert.AreEqual(0, t.State.ContractPenalty);
            Assert.AreEqual(0, t.State.ContractEndSeason);
        }

        [Test]
        public void HubLine_WhileTheTrapHolds_NamesItsLastSeason_GoneOnceTheContractIsHonest()
        {
            var honest = Offer(16, false);
            honest.AcceptOffer();
            honest.StartNextSeason();
            Assert.IsNull(new SeasonOfferModel(honest, Text()).ExclusiveText, "чесний контракт — рядка немає");

            var s = Offer(17, true);
            int signed = s.State.Season;
            s.AcceptOffer();
            s.StartNextSeason();
            int last = signed + CareerRules.ExclusiveSeasons;
            Assert.AreEqual(Text().Format("c.offer.exclusive_until", last), new SeasonOfferModel(s, Text()).ExclusiveText,
                "перший сезон строку — до кінця останнього");
            StringAssert.Contains(last.ToString(), new SeasonOfferModel(s, Text()).ExclusiveText);

            s.State.Season = last;
            Assert.AreEqual(0, s.ExitPenaltyDue, "останній сезон: піти на його кінці вже не рано");
            Assert.IsNotNull(new SeasonOfferModel(s, Text()).ExclusiveText, "але пастка ще тримає — рядок є");

            s.State.Season = last + 1;
            Assert.IsNull(new SeasonOfferModel(s, Text()).ExclusiveText, "строк скінчився — рядка немає");
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
            Assert.AreEqual(TrapAdvance, s.State.PendingAdvance);
            Assert.AreEqual(money, s.State.Money, "до прощального матчу аванс не платять");
            // сейв посеред прощання — підписане не губиться
            var t = CareerKit.Fresh(6, C);
            t.RestoreState(s.CaptureState());
            Assert.IsTrue(t.State.PendingExclusive);
            Assert.AreEqual(TrapAdvance, t.State.PendingAdvance);

            s.StartShow();
            FeudBotDriver.PlayMatch(s.Engine, new MatchGoal { Aim = MatchAim.Lose, Style = MatchStyle.Clean });
            s.FinishMatch();
            int afterShow = s.State.Money;
            s.ContinueFromShow();
            Assert.AreEqual(2, s.FederationTier);
            Assert.AreEqual(afterShow + TrapAdvance, s.State.Money, "аванс — коли перехід стався");
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
                                Assert.AreEqual(Penalty, c.ExclusivePenalty);
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
            StringAssert.Contains(TrapAdvance.ToString(), m.AdvanceText);
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
            Assert.AreEqual(CareerRules.Advance(Regional, true, true), c.Advance, "аванс пастки × 1,25");
            Assert.Greater(c.Advance, TrapAdvance);
            var m = new SeasonOfferModel(s, Text());
            Assert.IsNotNull(m.ManagerNote);
            StringAssert.Contains("+" + (CareerRules.ManagerAdvancePercent - 100) + " %", m.AdvanceText);
            int money = s.State.Money;
            s.AcceptOffer();
            Assert.AreEqual(money + CareerRules.Advance(Regional, true, true), s.State.Money);
            Assert.AreEqual(Penalty, s.State.ContractPenalty, "штраф — без надбавки менеджерки");
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

            var s = PlayManagerScene(12, "hire");
            Assert.IsTrue(s.HasManager, "найнято сценою");
            var t = CareerKit.Fresh(12, C);
            t.RestoreState(s.CaptureState());
            Assert.IsTrue(t.HasManager, "менеджерка — у сейві");
            ToNextSeasonBeforeManagerScene(t);
            Assert.AreNotEqual("ev_manager", t.Backstage().SceneId, "і наступного сезону — працює, сцени немає");
        }

        [Test]
        public void ManagerScene_AfterRefusal_SheNeverComesBack_TheCardBringsHerBackNextSeason()
        {
            var refused = PlayManagerScene(21, "refuse");
            Assert.IsFalse(refused.HasManager);
            var restored = CareerKit.Fresh(21, C);
            restored.RestoreState(refused.CaptureState());
            foreach (var s in new[] { refused, restored })
                for (int season = 0; season < 2; season++)
                {
                    ToNextSeasonBeforeManagerScene(s);
                    Assert.AreNotEqual("ev_manager", s.Backstage().SceneId, "після «Відмовити» Марта не повертається — і після сейву");
                    CareerKit.PlayScene(s, 0);
                }

            var card = PlayManagerScene(22, "later");
            Assert.IsFalse(card.HasManager, "візитка — ще не найнято");
            var first = BeforeManagerScene(23);
            first.Feud.State.Audience = CareerRules.ManagerPopularity;
            Assert.AreEqual("ev_manager", first.Backstage().SceneId);
            Assert.AreEqual("n1", first.CurrentBackstage.Current.Id, "перша зустріч — незнайомка з записником");
            var t = CareerKit.Fresh(22, C);
            t.RestoreState(card.CaptureState());
            ToNextSeasonBeforeManagerScene(t);
            Assert.AreEqual("ev_manager", t.Backstage().SceneId, "з візиткою Марта приходить знову наступного сезону");
            var run = t.CurrentBackstage;
            Assert.AreEqual("n1c", run.Current.Id, "і вітається як знайома — візитка ще в кишені");
            run.Advance();
            Assert.AreEqual("n2c", run.Current.Id);
            run.Advance();
            Assert.AreEqual("n3", run.Current.Id, "умови — ті самі");
            run.Advance();
            run.Choose("hire");
            while (!run.IsFinished) run.Advance();
            t.FinishBackstage();
            Assert.IsTrue(t.HasManager, "з другої зустрічі — найнято");
        }

        [Test]
        public void ManagerScene_AfterFiring_SheComesBackNextSeason_RemembersIt_AndCanBeHiredAgain()
        {
            var s = PlayManagerScene(24, "hire");
            Assert.IsTrue(s.FireManager());
            var t = CareerKit.Fresh(24, C);
            t.RestoreState(s.CaptureState());
            Assert.IsFalse(t.HasManager);
            ToNextSeasonBeforeManagerScene(t);
            Assert.AreEqual("ev_manager", t.Backstage().SceneId, "звільнена — приходить знову наступного сезону");
            var run = t.CurrentBackstage;
            Assert.AreEqual("n1f", run.Current.Id, "і пам'ятає звільнення — навіть після сейву");
            run.Advance();
            Assert.AreEqual("n2f", run.Current.Id);
            run.Advance();
            Assert.AreEqual("n3", run.Current.Id, "умови ті самі");
            run.Advance();
            run.Choose("hire");
            while (!run.IsFinished) run.Advance();
            t.FinishBackstage();
            Assert.IsTrue(t.HasManager, "найняти знову можна");

            // візитка, потім найнято й звільнено — пам'ятає звільнення, а не візитку
            var u = PlayManagerScene(25, "later");
            ToNextSeasonBeforeManagerScene(u);
            Assert.AreEqual("ev_manager", u.Backstage().SceneId);
            CareerKit.PlayScene(u, 0);
            Assert.IsTrue(u.HasManager, "перший варіант — найняти");
            Assert.IsTrue(u.FireManager());
            ToNextSeasonBeforeManagerScene(u);
            Assert.AreEqual("ev_manager", u.Backstage().SceneId);
            Assert.AreEqual("n1f", u.CurrentBackstage.Current.Id);
        }

        [Test]
        public void FireManager_NotWhileABackstageSceneIsOpen_TheFiredFlagWouldBeLost()
        {
            // рев'ю ПМ 12.11.2026: сцена працює з копією стану ворожнечі й після кінця переписує прапори — звільнення посеред сцени губилось
            var s = PlayManagerScene(26, "hire");
            Assert.IsTrue(s.HasManager);
            s.State.ActionsLeft = 3;
            Assert.IsTrue(s.Backstage().Ok);
            Assert.IsTrue(s.BackstageOpen);
            Assert.IsFalse(s.FireManager(), "посеред сцени — ні");
            Assert.IsTrue(s.HasManager);
            CareerKit.PlayScene(s, 0);
            Assert.IsTrue(s.FireManager(), "після сцени — так");
            Assert.IsTrue(s.Feud.State.HasFlag(CareerSession.ManagerFiredFlag));
            ToNextSeasonBeforeManagerScene(s);
            Assert.AreEqual("ev_manager", s.Backstage().SceneId);
            Assert.AreEqual("n1f", s.CurrentBackstage.Current.Id, "пам'ятає звільнення");
        }

        [Test]
        public void FiredBeforeHerSceneOfTheSeason_SheComesBackNextSeason_NotAWeekLater()
        {
            var s = PlayManagerScene(27, "hire");
            ToNextSeasonBeforeManagerScene(s);
            Assert.IsFalse(s.BackstageSeenThisSeason("ev_manager"), "цього сезону її події ще не було");
            Assert.IsTrue(s.FireManager());
            Assert.AreNotEqual("ev_manager", s.Backstage().SceneId, "звільнена — того самого сезону не повертається");
            CareerKit.PlayScene(s, 0);
            ToNextSeasonBeforeManagerScene(s);
            Assert.AreEqual("ev_manager", s.Backstage().SceneId, "наступного — так");
            Assert.AreEqual("n1f", s.CurrentBackstage.Current.Id);
        }

        [Test]
        public void ChampionFarewell_FiringTheManagerAfterSigning_KeepsTheSignedAdvance()
        {
            var s = AtSeasonEnd(6, 1, 3, CareerRules.OfferTrust - CareerRules.OfferChampionTrustBonus, champion: true);
            s.ForcedOfferExclusive = false;
            s.State.Manager = true;
            s.AcceptOffer();
            int signed = CareerRules.Advance(Regional, false, true);
            Assert.Greater(signed, HonestAdvance);
            Assert.AreEqual(signed, s.State.PendingAdvance, "умови з менеджеркою підписано при «Перейти»");
            Assert.IsTrue(s.FireManager());
            s.StartShow();
            FeudBotDriver.PlayMatch(s.Engine, new MatchGoal { Aim = MatchAim.Lose, Style = MatchStyle.Clean });
            s.FinishMatch();
            Assert.AreEqual(0, s.State.Last.ManagerCut, "звільнена до матчу — частки з прощального гонорару не бере");
            int afterShow = s.State.Money;
            s.ContinueFromShow();
            Assert.AreEqual(2, s.FederationTier);
            Assert.AreEqual(afterShow + signed, s.State.Money, "звільнення після підпису підписаного авансу не зменшує");
        }

        // ---------------- сумісність сейву (відновлення у СВІЖУ сесію) ----------------

        [Test]
        public void SaveWithoutContractFields_RestoresHonest_NoManager_NothingPending()
        {
            var s = Offer(31, true);
            s.State.Manager = true;
            s.AcceptOffer();
            s.State.OfferRead = 3; s.State.PendingExclusive = true; s.State.PendingAdvance = 55;
            Assert.IsTrue(s.State.ContractExclusive);
            string blob = s.CaptureState();
            foreach (var k in new[] { "excl", "exend", "expen", "offread", "pendex", "pendadv", "manager" })
            {
                Assert.AreEqual(1, Regex.Matches(blob, "\\|" + k + "=").Count, k + " — поле зліпка");
                blob = Regex.Replace(blob, "\\|" + k + "=[^|]*", "");
            }
            var t = CareerKit.Fresh(31, C);
            t.RestoreState(blob);
            Assert.IsFalse(t.State.Manager);
            Assert.IsFalse(t.HasManager);
            Assert.IsFalse(t.State.ContractExclusive);
            Assert.IsFalse(t.ExclusiveActive);
            Assert.AreEqual(0, t.State.ContractEndSeason);
            Assert.AreEqual(0, t.State.ContractPenalty);
            Assert.AreEqual(0, t.State.OfferRead);
            Assert.IsFalse(t.State.PendingExclusive);
            Assert.AreEqual(0, t.State.PendingAdvance);
            Assert.AreEqual(s.FederationTier, t.FederationTier, "решта сейву — як була");
            Assert.AreEqual(s.State.Money, t.State.Money);
            t.StartNextSeason();
            Assert.AreEqual(ShowBooker.Fee(t.State.Position, t.State.IsBig, t.Federation.FeePercent), t.GetShowCard().Fee, "чесний гонорар");
        }

        [Test]
        public void OldShowResult_With23Or24Fields_HasNoManagerCut_AlsoInAFreshSession()
        {
            var r = new ShowResult { OpponentId = "dzhura", Fee = 30, FeeWinBonus = 5, FeeHypeBonus = 2, LossPopularityBlocked = 1, ManagerCut = 7 };
            var fields = r.Capture().Split(',');
            Assert.AreEqual(25, fields.Length);
            Assert.AreEqual(7, ShowResult.Parse(r.Capture()).ManagerCut);
            var p24 = ShowResult.Parse(string.Join(",", fields.Take(24)));
            Assert.AreEqual(0, p24.ManagerCut);
            Assert.AreEqual(1, p24.LossPopularityBlocked);
            Assert.AreEqual(37, p24.TotalFee);
            var p23 = ShowResult.Parse(string.Join(",", fields.Take(23)));
            Assert.AreEqual(0, p23.ManagerCut);
            Assert.AreEqual(0, p23.LossPopularityBlocked);
            Assert.AreEqual("dzhura", p23.OpponentId);
            Assert.AreEqual(37, p23.TotalFee);
            Assert.Throws<FormatException>(() => ShowResult.Parse(string.Join(",", fields.Take(22))));

            // той самий підсумок у сейві кар'єри: 23 і 24 поля — частки немає, гонорар той самий
            var s = CareerKit.NewHero(33);
            s.State.Manager = true;
            var last = PlayFirstShow(s);
            Assert.Greater(last.ManagerCut, 0);
            string blob = s.CaptureState();
            var m = Regex.Match(blob, "\\|last=([^|]*)");
            Assert.IsTrue(m.Success);
            var lf = Uri.UnescapeDataString(m.Groups[1].Value).Split(',');
            foreach (int n in new[] { 23, 24 })
            {
                string old = blob.Substring(0, m.Groups[1].Index) + Uri.EscapeDataString(string.Join(",", lf.Take(n))) + blob.Substring(m.Groups[1].Index + m.Groups[1].Length);
                var t = CareerKit.Fresh(33, C);
                t.RestoreState(old);
                Assert.AreEqual(0, t.State.Last.ManagerCut, n + " полів");
                Assert.AreEqual(last.Fee, t.State.Last.Fee);
                Assert.AreEqual(last.Winner, t.State.Last.Winner);
            }
        }

        [Test]
        public void FreshRestore_MidTrap_KeepsTheTrap_TheExitPenaltyAndTheFeeShare()
        {
            var s = Offer(32, true);
            s.AcceptOffer();
            s.StartNextSeason();
            Assert.IsTrue(s.ExclusiveActive);
            var t = CareerKit.Fresh(32, C);
            t.RestoreState(s.CaptureState());
            Assert.IsTrue(t.ExclusiveActive);
            Assert.AreEqual(s.State.ContractEndSeason, t.State.ContractEndSeason);
            Assert.AreEqual(Penalty, t.ExitPenaltyDue, "вийти раніше — штраф");
            Assert.AreEqual(s.GetShowCard().Fee, t.GetShowCard().Fee);
            Assert.AreEqual(ShowBooker.Fee(t.State.Position, t.State.IsBig, t.Federation.FeePercent) * CareerRules.ExclusiveFeePercent / 100,
                t.GetShowCard().Fee, "гонорар ×0,7 — і після сейву");
            var u = CareerKit.Fresh(32, C);
            u.RestoreState(s.CaptureState());
            u.State.Money = 500;
            Assert.IsFalse(u.CanGoAway, "без виїзних шоу — і після сейву");
            Assert.AreEqual("exclusive", u.SideJob().Reason, "без підробітку — і після сейву");
            var a = PlayFirstShow(s);
            var b = PlayFirstShow(t);
            Assert.AreEqual(a.Fee, b.Fee, "той самий гонорар у свіжій сесії");
        }
    }
}
