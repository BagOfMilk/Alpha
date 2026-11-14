using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Core.Ring.Match;
using Game.Gameplay.Ring;
using Game.Gameplay.Ring.Models;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// П5 п. 1–2: чотири федерації (власник 11.11.2026, варіант А) і перехід за правилом А2 «два ключі» («А2, як радиш»): пропозиція
    /// наприкінці сезону, коли глядачі (популярність ≥ стеля − 1) і букер (довіра ≥ порога; пояс додає +1) задоволені; вихід чемпіона
    /// через «віддай пояс»; відмова й образа букера; нижче підлоги — рівень нижче; пояс не переїжджає; два дивізіони (P2-6).
    /// Числа — ПЛЕЙСХОЛДЕР у CareerRules: тести беруть їх звідти, а не вписують.
    /// </summary>
    public class CareerTransitionTests
    {
        private static CareerContent C { get { return CareerKit.Content(); } }

        // ---------------- помічники ----------------

        /// <summary>Довести кар'єру до підсумку останнього шоу поточного сезону (далі ContinueFromShow веде в кінець сезону).</summary>
        private static void PlayToLastShowResult(CareerSession s, int variant = 0)
        {
            var rep = new CareerRunReport();
            int guard = 0;
            while (!(s.Stage == CareerStage.Show && s.State.Phase == ShowPhase.Result && s.State.Week == CareerRules.WeeksInSeason(s.State.Season)))
            {
                Assert.Less(++guard, 3000);
                Assert.AreNotEqual(CareerStage.SeasonEnd, s.Stage, "сезон закінчився раніше");
                CareerBot.Step(s, variant, rep);
            }
        }

        /// <summary>
        /// Закрити сезон у федерації <paramref name="tier"/> з популярністю, довірою й поясом; титульний шанс — уже розіграний (тут не
        /// про титул). Кінець сезону рахує звільнення з цією популярністю.
        /// </summary>
        private static CareerSession CloseSeason(CareerSession s, int tier, int pop, int trust, bool champion)
        {
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

        private static CareerSession AtSeasonEnd(ulong seed, int tier, int pop, int trust, bool champion = false, bool female = false)
        {
            var s = CareerKit.NewHero(seed);
            if (female) s.SetFemale(true);
            return CloseSeason(s, tier, pop, trust, champion);
        }

        private static RingText Text()
        {
            return RingText.FromJson(CareerKit.Read("strings_uk.json"), CareerKit.Read("ui_uk.json"), CareerKit.Read("backstage_uk.json"));
        }

        // ---------------- правило А2: одна функція ----------------

        [Test]
        public void OfferRule_TwoKeys_AudienceAndBooker_BeltCountsForTheBooker_NotForTheAudience()
        {
            int need = CareerRules.OfferTrust;
            for (int t = 1; t < CareerContent.MaxTier; t++)
            {
                var f = C.Federation(t);
                int popNeed = f.Ceiling - CareerRules.OfferPopularityGap;
                Assert.AreEqual(new[] { 3, 5, 7 }[t - 1], popNeed, "ключ глядачів: стеля − 1");
                Assert.IsTrue(CareerRules.OfferReady(popNeed, need, false, f, CareerContent.MaxTier).Ready, "обидва ключі, без пояса — пропозиція");
                var noAud = CareerRules.OfferReady(popNeed - 1, need, false, f, CareerContent.MaxTier);
                Assert.IsFalse(noAud.Ready); Assert.IsFalse(noAud.AudienceOk); Assert.IsTrue(noAud.BookerOk);
                var noBook = CareerRules.OfferReady(popNeed, need - 1, false, f, CareerContent.MaxTier);
                Assert.IsFalse(noBook.Ready); Assert.IsTrue(noBook.AudienceOk); Assert.IsFalse(noBook.BookerOk);
                var belt = CareerRules.OfferReady(popNeed, need - CareerRules.OfferChampionTrustBonus, true, f, CareerContent.MaxTier);
                Assert.IsTrue(belt.Ready, "чемпіон: довіра рахується на +1");
                Assert.IsTrue(belt.BeltCounted); Assert.AreEqual(need, belt.TrustCounted);
                var beltNoAud = CareerRules.OfferReady(popNeed - 1, CareerRules.MaxTrust, true, f, CareerContent.MaxTier);
                Assert.IsFalse(beltNoAud.Ready, "пояс не замінює популярності");
            }
            var top = CareerRules.OfferReady(10, CareerRules.MaxTrust, true, C.Federation(CareerContent.MaxTier), CareerContent.MaxTier);
            Assert.IsFalse(top.HasHigher); Assert.IsFalse(top.Ready, "із найвищої — нікуди");
        }

        [Test]
        public void PopularityIsCappedByTheFederationCeiling_EntryIsMidcard()
        {
            var s = CareerKit.NewHero(1);
            Assert.AreEqual(CareerRules.StartPopularity, s.Popularity);
            Assert.AreEqual(4, s.PopularityCeiling);
            s.Feud.State.Audience = 9;
            Assert.AreEqual(4, s.Popularity, "місцева: стеля 4");
            for (int t = 2; t <= CareerContent.MaxTier; t++)
            {
                var f = C.Federation(t);
                Assert.AreEqual(f.Ceiling - CareerRules.EntryCeilingGap, CareerRules.EntryPopularity(f), "статус входу: попередня стеля");
                Assert.AreEqual(CardPosition.Midcard, CareerRules.PositionFor(CareerRules.EntryPopularity(f), f.Ceiling), "вхід — середина картки");
            }
        }

        // ---------------- перехід ----------------

        [Test]
        public void Accept_NonChampion_MovesUp_WithEntryStatus_TrustOne_BeltStaysBehind_NextSeasonBooksTheNewRoster()
        {
            var s = AtSeasonEnd(1, 1, 3, CareerRules.OfferTrust);
            Assert.IsTrue(s.SeasonSummaryFinal);
            Assert.IsTrue(s.OfferAvailable);
            Assert.AreEqual(2, s.OfferFederation.Tier);
            string belt1 = s.State.Belt(1, false), belt2 = s.State.Belt(2, false);
            s.AcceptOffer();
            Assert.AreEqual(2, s.FederationTier);
            Assert.AreEqual(CareerStage.SeasonEnd, s.Stage);
            Assert.AreEqual(CareerRules.EntryPopularity(C.Federation(2)), s.Popularity);
            Assert.AreEqual(CareerRules.TransferTrust, s.Trust);
            Assert.AreEqual(6, s.PopularityCeiling);
            Assert.IsFalse(s.OfferAvailable, "рішення ухвалено");
            Assert.IsFalse(s.IsChampion);
            Assert.AreEqual(belt1, s.State.Belt(1, false), "пояс місцевої не змінився");
            Assert.AreEqual(belt2, s.State.Belt(2, false), "пояс регіональної — у її чемпіона");
            Assert.IsTrue(s.CanStartNextSeason);
            s.StartNextSeason();
            Assert.AreEqual(CareerStage.Week, s.Stage);
            var opp = C.FindNpc(s.State.Opponent);
            Assert.AreEqual(2, opp.Tier, "букер нової федерації ставить її ростер");
            Assert.IsFalse(opp.Female);
            Assert.AreEqual(CardPosition.Midcard, s.State.Position);
            Assert.Greater(s.WeeklyCost, C.Federation(1).LivingCost, "вища федерація — дорожче жити й їздити");
        }

        /// <summary>Прощальний матч чемпіона, зіграний ботом: план «віддай пояс» = програти чисто.</summary>
        private static CareerSession Farewell(ulong seed, MatchAim aim, out NpcDef heir)
        {
            var s = AtSeasonEnd(seed, 1, 3, CareerRules.OfferTrust - CareerRules.OfferChampionTrustBonus, champion: true);
            Assert.IsTrue(s.OfferAvailable, "чемпіону вистачає довіри з поясом");
            s.AcceptOffer();
            Assert.AreEqual(CareerStage.Show, s.Stage);
            var card = s.GetShowCard();
            Assert.IsTrue(card.IsFarewell);
            Assert.AreEqual(BookerPlan.LoseClean, card.Plan, "«віддай пояс» — програй чисто");
            heir = card.Opponent;
            Assert.AreEqual(1, heir.Tier); Assert.IsFalse(heir.Female);
            Assert.AreNotEqual(CareerContent.HammerId, heir.Id);
            Assert.AreEqual(1, s.FederationTier, "до прощального матчу гравець ще в старій федерації");
            s.StartShow();
            FeudBotDriver.PlayMatch(s.Engine, new MatchGoal { Aim = aim, Style = MatchStyle.Clean });
            s.FinishMatch();
            return s;
        }

        [Test]
        public void ChampionExit_GivesTheBelt_FulfilledGoesBeautifully_TrustTwo_FailedLeavesItVacant_TrustOne_ReturnClosed()
        {
            bool ok = false, bad = false;
            for (ulong seed = 1; seed <= 12 && !(ok && bad); seed++)
            {
                NpcDef heir;
                var s = Farewell(seed, ok ? MatchAim.Win : MatchAim.Lose, out heir);
                int res = s.State.FarewellResult;
                Assert.That(res, Is.EqualTo(1).Or.EqualTo(2));
                string belt2 = s.State.Belt(2, false);
                s.ContinueFromShow();
                Assert.AreEqual(CareerStage.SeasonEnd, s.Stage);
                Assert.AreEqual(2, s.FederationTier);
                Assert.IsFalse(s.IsChampion, "пояс із гравцем не переїжджає");
                Assert.AreEqual(belt2, s.State.Belt(2, false));
                if (res == 1)
                {
                    ok = true;
                    Assert.AreEqual(heir.Id, s.State.Belt(1, false), "пояс — у наступника");
                    Assert.AreEqual(CareerRules.FarewellTrust, s.Trust);
                    Assert.IsFalse(s.State.ClosedTiers.Contains(1));
                }
                else
                {
                    bad = true;
                    Assert.AreEqual("", s.State.Belt(1, false), "пояс вакантний");
                    Assert.AreEqual(CareerRules.TransferTrust, s.Trust);
                    Assert.IsTrue(s.State.ClosedTiers.Contains(1), "двері зачинено");
                }
                Assert.IsTrue(s.CanStartNextSeason);
            }
            Assert.IsTrue(ok, "бот, що програє чисто, хоч раз виконує «віддай пояс»");
            Assert.IsTrue(bad, "бот, що перемагає, хоч раз зриває план");
        }

        [Test]
        public void Decline_OfferRepeatsNextSeason_SecondDeclineInARowCostsTrust_ImplicitSkipIsNotADecline()
        {
            var s = AtSeasonEnd(3, 1, 3, CareerRules.OfferTrust);
            s.DeclineOffer();
            Assert.AreEqual(1, s.FederationTier);
            Assert.AreEqual(1, s.State.OfferDeclines);
            Assert.AreEqual(CareerRules.OfferTrust, s.Trust, "перша відмова — без образи");
            Assert.IsFalse(s.OfferAvailable);
            s.StartNextSeason();
            CloseSeason(s, 1, 3, CareerRules.OfferTrust, false);
            Assert.IsTrue(s.OfferAvailable, "пропозиція повторюється, поки ключі виконані");
            s.DeclineOffer();
            Assert.AreEqual(2, s.State.OfferDeclines);
            Assert.AreEqual(CareerRules.OfferTrust - 1, s.Trust, "друга відмова поспіль — довіра −1");

            // «Новий сезон» без рішення (екрана пропозиції ще немає) — не відмова: серія не росте, образи немає
            var q = AtSeasonEnd(4, 1, 3, CareerRules.OfferTrust);
            Assert.IsTrue(q.OfferAvailable);
            q.StartNextSeason();
            Assert.AreEqual(0, q.State.OfferDeclines);
            CloseSeason(q, 1, 3, CareerRules.OfferTrust, false);
            q.DeclineOffer();
            Assert.AreEqual(1, q.State.OfferDeclines);
            Assert.AreEqual(CareerRules.OfferTrust, q.Trust);
        }

        [Test]
        public void BelowTheFloor_ContractNotRenewed_OneTierDown_SkippingAClosedTier_CareerContinues()
        {
            var s = AtSeasonEnd(5, 3, 3, 3);
            Assert.IsTrue(s.State.Report.Fired, "популярність 3 < підлоги 4");
            Assert.AreEqual(2, s.DemotionFederation.Tier);
            s.StartNextSeason();
            Assert.AreEqual(CareerStage.Week, s.Stage);
            Assert.AreEqual(2, s.FederationTier);
            Assert.AreEqual(CareerRules.FiredTrust, s.Trust);
            Assert.AreEqual(1, s.State.FiredCount);
            Assert.AreEqual(2, C.FindNpc(s.State.Opponent).Tier);

            var c = CareerKit.NewHero(6);
            c.State.ClosedTiers.Add(2);
            CloseSeason(c, 3, 3, 3, false);
            Assert.AreEqual(1, c.DemotionFederation.Tier, "регіональна зачинена — на місцеву");
            c.StartNextSeason();
            Assert.AreEqual(1, c.FederationTier);
        }

        [Test]
        public void Restore_AfterTheMove_AndMidFarewell_IntoAFreshSession()
        {
            var s = AtSeasonEnd(7, 1, 3, CareerRules.OfferTrust);
            s.AcceptOffer();
            s.StartNextSeason();
            var fresh = CareerKit.Fresh(7, C);
            fresh.RestoreState(s.CaptureState());
            Assert.AreEqual(s.CaptureState(), fresh.CaptureState());
            Assert.AreEqual(2, fresh.FederationTier);
            Assert.AreEqual(6, fresh.PopularityCeiling);
            fresh.Feud.State.Audience = 9;
            Assert.AreEqual(6, fresh.Popularity, "стеля федерації — і після відновлення");

            NpcDef heir;
            var f = Farewell(8, MatchAim.Win, out heir);
            var mid = CareerKit.Fresh(8, C);
            mid.RestoreState(f.CaptureState());
            Assert.AreEqual(f.CaptureState(), mid.CaptureState());
            Assert.IsTrue(mid.State.Farewell);
            f.ContinueFromShow(); mid.ContinueFromShow();
            Assert.AreEqual(f.CaptureState(), mid.CaptureState(), "прощання завершується однаково");
            var after = CareerKit.Fresh(8, C);
            after.RestoreState(mid.CaptureState());
            Assert.AreEqual(mid.CaptureState(), after.CaptureState());
            Assert.AreEqual(mid.State.Belt(1, false), after.State.Belt(1, false));
            CollectionAssert.AreEqual(mid.State.ClosedTiers, after.State.ClosedTiers);
        }

        // ---------------- дивізіони (P2-6) ----------------

        [Test]
        public void Divisions_WeeklyOpponentsAreOfTheHerosSex_TitleIsTheDivisionsBelt()
        {
            foreach (bool female in new[] { false, true })
            {
                var s = CareerKit.NewHero(11);
                s.SetFemale(female);
                Assert.IsTrue(s.State.FemaleKnown);
                var rep = new CareerRunReport();
                var opps = new HashSet<string>();
                int guard = 0;
                while (s.State.Season == 1 && guard++ < 2000)
                {
                    if (s.Stage == CareerStage.Show && s.State.Phase == ShowPhase.Card && !s.State.IsBig) opps.Add(s.State.Opponent);
                    if (s.Stage == CareerStage.SeasonEnd) break;
                    CareerBot.Step(s, 0, rep);
                }
                Assert.IsNotEmpty(opps);
                foreach (var id in opps) Assert.AreEqual(female, C.FindNpc(id).Female, id);
                Assert.AreEqual(female ? "komet" : "baron", s.Federation.BeltOf(female));
            }
        }

        [Test]
        public void SetFemale_MovesTheChampionsBelt_ToTheirDivision()
        {
            var s = CareerKit.NewHero(12);
            s.State.BeltHolder = CareerState.PlayerHolder;
            s.SetFemale(true);
            Assert.IsTrue(s.IsChampion);
            Assert.AreEqual(CareerState.PlayerHolder, s.State.Belt(1, true));
            Assert.AreEqual("baron", s.State.Belt(1, false), "чоловічий пояс повернувся чемпіону");
        }

        // ---------------- скаут ----------------

        [Test]
        public void Scout_ComesInSeasonTwoWeekTwo_BelowTheWorldTier_WithoutAnOffer()
        {
            var s = CareerKit.NewHero(13);
            s.State.Week = 4;
            var r1 = s.Backstage();
            Assert.AreNotEqual("ev_scout", r1.SceneId, "у першому сезоні скаута немає");
            var t = CareerKit.NewHero(13);
            t.State.Season = 2; t.State.Week = 2;
            Assert.AreEqual("ev_scout", t.Backstage().SceneId);
            CareerKit.PlayScene(t, 0);
            Assert.AreEqual(1, t.FederationTier, "скаут лише дивиться: пропозиції немає");
            var w = CareerKit.NewHero(13);
            w.State.Season = 2; w.State.Week = 2;
            w.EnterFederation(4, 8, 1);
            Assert.AreNotEqual("ev_scout", w.Backstage().SceneId, "зі світової нікуди кликати");
        }

        // ---------------- екран пропозиції ----------------

        [Test]
        public void OfferModel_ShowsBothKeys_WhatIsMissing_TheBeltMark_AndTheOutcome()
        {
            var tx = Text();
            var ready = AtSeasonEnd(1, 1, 3, CareerRules.OfferTrust);
            var m = new SeasonOfferModel(ready, tx);
            Assert.IsTrue(m.Visible); Assert.IsTrue(m.CanDecide); Assert.IsFalse(m.IsTop);
            Assert.AreEqual("Глядачі 3/3", m.AudienceText);
            Assert.AreEqual("Букер " + CareerRules.OfferTrust + "/" + CareerRules.OfferTrust, m.BookerText);
            Assert.IsNull(m.AudienceHint); Assert.IsNull(m.BookerHint); Assert.IsNull(m.BeltMark);
            StringAssert.Contains(tx.Get("fed.2.name"), m.TitleText);
            Assert.AreEqual(tx.Get("c.offer.ready"), m.StatusText);
            StringAssert.Contains(tx.Get("fed.1.name"), m.FederationText);
            Assert.IsTrue(m.ShowKeys);
            ready.AcceptOffer();
            StringAssert.Contains(tx.Get("fed.2.name"), m.StatusText);
            Assert.IsFalse(m.CanDecide);
            // після рішення ключі сховано: інакше вони рахувалися б уже для нової федерації (рев'ю ПМ, 11.11.2026)
            Assert.IsTrue(m.Visible, "рядок стану лишається");
            Assert.IsFalse(m.ShowKeys);
            Assert.AreEqual("", m.AudienceText); Assert.AreEqual("", m.BookerText); Assert.AreEqual("", m.TitleText);
            Assert.IsNull(m.AudienceHint); Assert.IsNull(m.BookerHint); Assert.IsNull(m.BeltMark); Assert.IsFalse(m.IsTop);

            var miss = new SeasonOfferModel(AtSeasonEnd(2, 1, 2, CareerRules.OfferTrust - 2), tx);
            Assert.IsFalse(miss.CanDecide);
            Assert.AreEqual(tx.Format("c.offer.need_audience", 1), miss.AudienceHint);
            Assert.AreEqual(tx.Format("c.offer.need_booker", 2), miss.BookerHint);
            Assert.IsNull(miss.StatusText);

            var champ = AtSeasonEnd(3, 1, 3, CareerRules.OfferTrust - 1, champion: true);
            var cm = new SeasonOfferModel(champ, tx);
            Assert.AreEqual(tx.Get("c.offer.belt"), cm.BeltMark);
            Assert.AreEqual(tx.Get("c.offer.farewell"), cm.StatusText);

            var top = new SeasonOfferModel(AtSeasonEnd(4, 4, 10, 5), tx);
            Assert.IsTrue(top.IsTop); Assert.IsFalse(top.CanDecide);
            StringAssert.Contains(tx.Get("fed.4.name"), top.TitleText);

            var dec = AtSeasonEnd(5, 1, 3, CareerRules.OfferTrust);
            var dm = new SeasonOfferModel(dec, tx);
            dec.DeclineOffer();
            Assert.AreEqual(tx.Get("c.offer.declined"), dm.StatusText);
            Assert.IsFalse(dm.ShowKeys);

            var down = new SeasonOfferModel(AtSeasonEnd(6, 3, 3, 3), tx);
            Assert.AreEqual(tx.Format("c.season.demoted", tx.Get("fed.3.name"), tx.Get("fed.2.name")), down.StatusText);
            Assert.IsEmpty(tx.Missing, "усі ключі тексту є: " + string.Join(", ", tx.Missing));
        }

        // ---------------- рев'ю ПМ (11.11.2026) ----------------

        [Test]
        public void Declines_InARow_AnySeasonWithoutAnExplicitDecline_BreaksTheSeries()
        {
            // відмова → мовчазний пропуск → відмова: не «дві поспіль»
            var s = AtSeasonEnd(9, 1, 3, CareerRules.OfferTrust);
            s.DeclineOffer();
            s.StartNextSeason();
            Assert.AreEqual(1, s.State.OfferDeclines, "після явної відмови серія триває");
            CloseSeason(s, 1, 3, CareerRules.OfferTrust, false);
            Assert.IsTrue(s.OfferAvailable);
            s.StartNextSeason();
            Assert.AreEqual(0, s.State.OfferDeclines, "мовчазний пропуск серію обриває");
            CloseSeason(s, 1, 3, CareerRules.OfferTrust, false);
            s.DeclineOffer();
            Assert.AreEqual(1, s.State.OfferDeclines);
            Assert.AreEqual(CareerRules.OfferTrust, s.Trust, "відмова → пропуск → відмова — без образи");

            // відмова → сезон без пропозиції → відмова: теж без образи
            var n = AtSeasonEnd(10, 1, 3, CareerRules.OfferTrust);
            n.DeclineOffer();
            n.StartNextSeason();
            CloseSeason(n, 1, 1, 1, false);
            Assert.IsFalse(n.OfferAvailable);
            n.StartNextSeason();
            Assert.AreEqual(0, n.State.OfferDeclines, "сезон без пропозиції серію обриває");
            CloseSeason(n, 1, 3, CareerRules.OfferTrust, false);
            n.DeclineOffer();
            Assert.AreEqual(CareerRules.OfferTrust, n.Trust);
        }

        [Test]
        public void Scout_ComesOncePerCareer_AlsoAfterARestore()
        {
            var s = CareerKit.NewHero(16);
            s.State.Season = 2; s.State.Week = 2;
            Assert.AreEqual("ev_scout", s.Backstage().SceneId);
            CareerKit.PlayScene(s, 0);
            var f = CareerKit.Fresh(16, C);
            f.RestoreState(s.CaptureState());
            foreach (var c in new[] { s, f })
            {
                c.State.Season = 3; c.State.Week = 2; c.State.ActionsDone.Clear();
                Assert.AreNotEqual("ev_scout", c.Backstage().SceneId, "скаут — раз на кар'єру (§6.4)");
            }
            Assert.IsTrue(C.Backstage.Find("ev_scout").Once);
        }

        /// <summary>Зліпок у форматі до 11.11.2026: один пояс «belt=…» замість «belts=…» і без дивізіону героя («fem»).</summary>
        private static string OldSave(string blob, string oldBelt)
        {
            var parts = blob.Split('|').Where(p => !p.StartsWith("belts=") && !p.StartsWith("fem=")).ToList();
            if (oldBelt != null) parts.Insert(2, "belt=" + Uri.EscapeDataString(oldBelt));
            return string.Join("|", parts);
        }

        [Test]
        public void Restore_OldSave_WithoutBelts_VacantOrUnknownBelt_StartingChampions_PlayersBeltToTheirDivision()
        {
            foreach (var old in new[] { null, "", "nobody_from_old_build", "baron" })
            {
                var f = CareerKit.Fresh(14, C);
                f.RestoreState(OldSave(CareerKit.NewHero(14).CaptureState(), old));
                Assert.IsFalse(f.State.FemaleKnown);
                foreach (var fed in C.Federations)
                    foreach (bool female in new[] { false, true })
                        Assert.AreEqual(fed.BeltOf(female), f.State.Belt(fed.Tier, female), "«" + old + "»: пояс " + fed.Tier + (female ? "ж" : "ч"));
            }
            // гравець-чемпіон старого сейву: пояс — у його дивізіон, коли хост дізнається стать з тіла
            var p = CareerKit.Fresh(15, C);
            p.RestoreState(OldSave(CareerKit.NewHero(15).CaptureState(), CareerState.PlayerHolder));
            Assert.IsTrue(p.IsChampion);
            p.SetFemale(true);
            Assert.IsTrue(p.IsChampion);
            Assert.AreEqual(CareerState.PlayerHolder, p.State.Belt(1, true));
            Assert.AreEqual(C.Federation(1).BeltOf(false), p.State.Belt(1, false));
            Assert.AreEqual(C.Federation(2).BeltOf(true), p.State.Belt(2, true), "інші пояси — стартові чемпіони");
        }

        [Test]
        public void SetFemale_OnlyBeforeTheFirstStep_OrOnceForAnOldSave()
        {
            var s = CareerKit.NewHero(17);
            s.SetFemale(true);                         // дивізіон ще невідомий — можна
            s.SetFemale(false);                        // перший тиждень, нічого не зроблено — екран створення передумав
            Assert.IsFalse(s.State.Female);
            s.SetFemale(false);                        // те саме — не зміна
            s.State.Week = 2;
            Assert.Throws<InvalidOperationException>(() => s.SetFemale(true), "посеред кар'єри дивізіон не змінюють");
            Assert.IsFalse(s.State.Female);
        }

        /// <summary>Довести кар'єру до початку тижня (етап тижня, жодної дії, сцени немає).</summary>
        private static void PlayToWeekStart(CareerSession s, int season, int week)
        {
            var rep = new CareerRunReport();
            int guard = 0;
            while (!(s.Stage == CareerStage.Week && s.State.Season == season && s.State.Week == week && s.State.ActionsDone.Count == 0 && !s.BackstageOpen))
            {
                Assert.Less(++guard, 3000);
                CareerBot.Step(s, 0, rep);
            }
        }

        [Test]
        public void SetFemale_OldSave_MidCareer_CardStaysPlayable_BeltsStayPut()
        {
            // Старий сейв посеред кар'єри (п'ятий тиждень: сезон 2, тиждень 1), дивізіон невідомий (FemaleKnown = false); хост
            // проставляє «жінка» з тіла (рев'ю ПМ, 12.11.2026: поведінка навмисна, бракувало тесту).
            var src = CareerKit.NewHero(18);
            PlayToWeekStart(src, 2, 1);
            int fed = src.State.Federation;
            var federation = C.Federation(fed);
            string maleChamp = federation.BeltOf(false), femaleChamp = federation.BeltOf(true);
            // пояс старої кар'єри — у чоловіка, що не стартовий чемпіон (виграв його в старій кар'єрі)
            string oldHolder = federation.Roster.First(n => !n.Female && n.Id != maleChamp && n.Id != CareerContent.HammerId).Id;

            foreach (string oldBelt in new[] { oldHolder, CareerState.PlayerHolder })
                foreach (bool midWeek in new[] { false, true })
                {
                    string at = "«" + oldBelt + "», " + (midWeek ? "посеред тижня" : "початок тижня") + ": ";
                    var s = CareerKit.Fresh(18, C);
                    s.RestoreState(OldSave(src.CaptureState(), oldBelt));
                    Assert.IsFalse(s.State.FemaleKnown, at + "дивізіон невідомий");
                    Assert.AreEqual(CareerStage.Week, s.Stage);
                    if (midWeek) { s.Train(false); Assert.AreEqual(1, s.State.ActionsDone.Count, at + "тиждень почато"); }
                    string bookedOpp = s.State.Opponent;
                    Assert.IsFalse(C.FindNpc(bookedOpp).Female, at + "картку склали для чоловічого дивізіону (дефолт старого сейву)");

                    s.SetFemale(true);
                    Assert.IsTrue(s.State.Female && s.State.FemaleKnown);

                    // картка не ламається: до першої дії тижня букер складає її наново для дивізіону героїні, посеред тижня — лишає
                    // оголошену (тиждень уже почато)
                    var card = s.GetShowCard();
                    Assert.IsNotNull(card.Opponent, at + "суперник є");
                    Assert.GreaterOrEqual(card.RivalSteps, 0, at);
                    if (midWeek) Assert.AreEqual(bookedOpp, card.Opponent.Id, at + "оголошену картку не міняють");
                    else Assert.IsTrue(card.Opponent.Female, at + "суперниця свого дивізіону");
                    var week = s.GetWeekView();
                    Assert.IsNotNull(week.Card);

                    // пояси на місці
                    if (oldBelt == CareerState.PlayerHolder)
                    {
                        Assert.IsTrue(s.IsChampion, at + "чемпіонка свого дивізіону");
                        Assert.AreEqual(CareerState.PlayerHolder, s.State.Belt(fed, true));
                        Assert.AreEqual(maleChamp, s.State.Belt(fed, false), at + "чоловічий пояс — стартовому чемпіону");
                    }
                    else
                    {
                        Assert.IsFalse(s.IsChampion);
                        Assert.AreEqual(oldHolder, s.State.Belt(fed, false), at + "чоловічий пояс лишається в того, хто його виграв");
                        Assert.AreEqual(femaleChamp, s.State.Belt(fed, true), at + "жіночий — стартовій чемпіонці");
                    }

                    // другої зміни немає: дивізіон уже відомий, кар'єра почата
                    Assert.Throws<InvalidOperationException>(() => s.SetFemale(false), at + "лише один раз");

                    // тиждень і шоу граються далі
                    s.EndWeek();
                    var e = s.StartShow();
                    Assert.IsNotNull(e, at + "шоу почалося");
                    Assert.AreEqual(s.State.Opponent, card.Opponent.Id);
                }
        }

        // ---------------- вигляд NPC ----------------

        [Test]
        public void EveryRosterNpc_HasALook_OfTheirSex_CulturesVary()
        {
            var looks = new HashSet<string>(CareerLooks.NpcIds);
            var cultures = new HashSet<string>();
            int ukr = 0, fresh = 0;
            foreach (var n in C.Roster)
            {
                Assert.IsTrue(looks.Contains(n.Id), "немає вигляду: " + n.Id);
                var l = CareerLooks.ForNpc(n.Id);
                Assert.AreEqual(n.Female, CareerLooks.IsFemaleBody(l.Body), n.Id);
                Assert.IsTrue(CareerLooks.HeroBodies.Contains(l.Body), n.Id + ": тіло з набору");
                if (n.Tier == 1 && n.Id != "inti") continue;
                fresh++;
                string culture = l.Body.Substring(7);
                cultures.Add(culture);
                if (culture == "ukrainian") ukr++;
            }
            Assert.AreEqual(19, fresh, "19 нових NPC (П5)");
            Assert.GreaterOrEqual(cultures.Count, 7, "культури різні");
            Assert.That(ukr, Is.InRange(3, 5), "українців — близько п'ятої частини");
        }
    }

    /// <summary>
    /// Темп і вікна перемог на кожному рівні (П5; рекомендація ПМ, TRPG_DECISIONS П5: «місцева S1–S2, регіональна S2–S3,
    /// національна S3–S5, світова S5+»; «щотижневі 55–65 %, ворожнеча ~50 %, титул 35–45 %»; піки сили 70–80 % — рішення власника
    /// 06.11.2026, на кожному рівні — рев'ю ПМ 12.11.2026). Бот кар'єри приймає кожну пропозицію; матчі — однаковий ШІ за обидві сторони (як MatchFeelTests); половина героїв —
    /// жінки, вікна тижневих — і окремо за статтю (на місцевій у жінок ранги 3/5/6, у чоловіків 1/2/4). Межі — із запасом на шум.
    /// </summary>
    public class CareerTempoTests
    {
        private static void Ai(MatchEngine e) { int g = 0; while (!e.IsOver && g++ < 4000) MatchAi.Step(e); }

        public sealed class TempoReport
        {
            /// <summary>[рівень, сезон, коли вперше грає в ньому] — кількість кар'єр.</summary>
            public readonly int[,] FirstSeason = new int[CareerContent.MaxTier + 1, 12];
            public readonly SortedDictionary<string, int[]> Wins = new SortedDictionary<string, int[]>();
            public int Careers;

            public double Rate(string key)
            {
                var v = Wins[key];
                return (double)v[0] / (v[0] + v[1] + v[2]);
            }

            /// <summary>Медіана сезону, з якого кар'єри грають на рівні (лише ті, хто дійшов).</summary>
            public double MedianFirstSeason(int tier)
            {
                var all = new List<int>();
                for (int se = 1; se < 12; se++) for (int k = 0; k < FirstSeason[tier, se]; k++) all.Add(se);
                if (all.Count == 0) return double.NaN;
                return all[all.Count / 2];
            }

            public int Reached(int tier) { int n = 0; for (int se = 1; se < 12; se++) n += FirstSeason[tier, se]; return n; }
        }

        public static TempoReport Measure(int careers, int seasons, int seedBase = 9000)
        {
            var rep = new TempoReport { Careers = careers };
            Action<string, MatchWinner> add = (k, w) =>
            {
                int[] v;
                if (!rep.Wins.TryGetValue(k, out v)) rep.Wins[k] = v = new int[3];
                v[w == MatchWinner.Player ? 0 : w == MatchWinner.Rival ? 1 : 2]++;
            };
            for (int i = 0; i < careers; i++)
            {
                var s = CareerKit.NewHero((ulong)(seedBase + i));
                bool female = i / 8 % 2 == 1;   // стать — не за стилем бота (i % 8): кожен стиль грає обома статями
                s.SetFemale(female);
                var bot = new CareerRunReport();
                var seen = new bool[CareerContent.MaxTier + 1];
                string sex = female ? " ж" : " ч", peak = null;
                int guard = 0;
                while (s.State.Season <= seasons && guard++ < 10000)
                {
                    if (s.Stage == CareerStage.Week && !seen[s.FederationTier])
                    {
                        seen[s.FederationTier] = true;
                        rep.FirstSeason[s.FederationTier, s.State.Season]++;
                    }
                    if (s.Stage == CareerStage.SeasonEnd)
                    {
                        if (s.CanStartTitleMatch) s.StartTitleMatch();
                        else if (s.OfferAvailable) s.AcceptOffer();
                        else s.StartNextSeason();
                        continue;
                    }
                    if (s.Stage != CareerStage.Show) { CareerBot.Step(s, i % 8, bot); continue; }
                    string t = "t" + s.FederationTier;
                    switch (s.State.Phase)
                    {
                        case ShowPhase.Card:
                            // Пік — щотижневий матч, де суперник відстає від поточного росту гравця на 2+ щаблі (як MatchFeelTests), і реванш.
                            peak = null;
                            if (!s.State.IsTitle && !s.State.IsFeudShow && !s.State.IsBig && !s.State.Farewell)
                            {
                                if (s.IsRematch) peak = "реванш";
                                else if (CareerRules.RivalSteps(s.State.Position, s.PlayerGrowth(), s.FederationTier) - s.RivalStepsNow() >= 2) peak = "пік 2+";
                            }
                            s.StartShow();
                            break;
                        case ShowPhase.Match:
                        {
                            Ai(s.Engine);
                            bool title = s.State.IsTitle, big = s.State.IsBig, farewell = s.State.Farewell;
                            var r = s.FinishMatch();
                            if (!farewell)
                            {
                                string kind = t + (title ? " титул" : big ? " велике" : " тижневі");
                                add(kind, r.Winner);
                                add(kind + sex, r.Winner);
                            }
                            if (peak != null) { add(peak, r.Winner); add(peak + " " + t, r.Winner); }
                            break;
                        }
                        case ShowPhase.Feud:
                            if (s.Feud.Stage == RingStage.MatchPrep)
                            {
                                Ai(s.StartFeudMatch());
                                add(t + " ворожнеча", s.FinishMatch().Winner);
                            }
                            else CareerBot.Step(s, i % 8, bot);
                            break;
                        case ShowPhase.Result: s.ContinueFromShow(); break;
                    }
                }
                Assert.Less(guard, 10000);
            }
            return rep;
        }

        public static string Report(TempoReport r)
        {
            var sb = new System.Text.StringBuilder();
            for (int t = 1; t <= CareerContent.MaxTier; t++)
            {
                sb.Append("рівень " + t + " — грає з сезону:");
                for (int se = 1; se < 10; se++) sb.Append(" s" + se + "=" + r.FirstSeason[t, se]);
                sb.Append("; ");
            }
            foreach (var kv in r.Wins)
                sb.AppendFormat("{0}: {1:P0} ({2}/{3}); ", kv.Key, r.Rate(kv.Key), kv.Value[0], kv.Value[0] + kv.Value[1] + kv.Value[2]);
            return sb.ToString();
        }

        [Test]
        public void Tempo_AndWinWindows_OnEveryTier()
        {
            var r = Measure(200, 8);
            TestContext.WriteLine(Report(r));
            System.Console.WriteLine("[tempo] " + Report(r));
            // Замір 12.11.2026 на рушії P2-1 (матч 8–15 дій; ключ букера 4, титул {4, 6, 7, 9}, Молот +3 на рівень; піки — CareerRules
            // «Піки сили»), три набори сідів (9000 / 30000 / 55000): регіональна з S2 у 76–79 %, з S3 у 18 %, з S4 у 4–7 %; національна з S3
            // у 21–25 %, з S4 у 65–69 %; світова з S4 у 15–19 %, з S5 у 36–42 %, з S6 у 22–23 %, далі — решта. Перемоги за рівнями 1–4:
            // тижневі 59–60 / 60–62 / 61–64 / 62–65 %, ворожнеча 47–50 / 46–50 % (вище — менше 60 матчів), титул 37–40 / 37–42 / 43–47 /
            // 44–50 %; тижневі за статтю — жінки 56–65 %, чоловіки 59–65 % (піки розвели регіональну: жінки 64–67 % проти 55–59 %; Сирені
            // +1 Витривалості, Гадюці Спритність → Витривалість — 61–65 / 59–60 %, ростер регіональної лишився слабшим за національний).
            // Піки: реванш 70–76 / 73–78 / 72–78 / 76–78 %, «пік 2+» на регіональній і національній 74–75 / 76–81 %; разом — реванш
            // 74–77 %, «пік 2+» 75–76 %. Межі — із запасом на шум 200 кар'єр.
            Assert.GreaterOrEqual(r.Reached(2), r.Careers * 98 / 100, "з місцевої виходять майже всі");
            Assert.GreaterOrEqual(r.FirstSeason[2, 2] + r.FirstSeason[2, 3], r.Careers * 90 / 100, "місцева — S1–S2");
            Assert.That(r.MedianFirstSeason(2), Is.InRange(2, 3), "регіональна — з S2–S3");
            Assert.That(r.MedianFirstSeason(3), Is.InRange(3, 4), "національна — з S3–S4");
            Assert.That(r.MedianFirstSeason(4), Is.InRange(5, 6), "світова — з S5+");
            for (int se = 1; se <= 3; se++) Assert.AreEqual(0, r.FirstSeason[4, se], "у світову раніше S4 не потрапляють");
            // Піки сили (власник 06.11.2026: «Піки 70–80%») — на кожному рівні, де їх досить для заміру (≥ 60 матчів): «пік 2+» на
            // місцевій і світовій трапляється 5–17 разів на 200 кар'єр (на місцевій його тримає MatchFeelTests).
            foreach (var key in new[] { "реванш", "пік 2+" })
            {
                Assert.That(r.Rate(key), Is.InRange(0.70, 0.82), key + ", усі рівні");
                for (int t = 1; t <= CareerContent.MaxTier; t++)
                {
                    string k = key + " t" + t;
                    if (r.Wins.ContainsKey(k) && r.Wins[k][0] + r.Wins[k][1] + r.Wins[k][2] >= 60)
                        Assert.That(r.Rate(k), Is.InRange(0.66, 0.86), key + ", рівень " + t);
                }
            }
            foreach (int t in new[] { 1, 2, 3, 4 })
                Assert.GreaterOrEqual(r.Wins["реванш t" + t][0] + r.Wins["реванш t" + t][1] + r.Wins["реванш t" + t][2], 60, "реваншів досить для заміру, рівень " + t);
            foreach (int t in new[] { 2, 3 })
                Assert.GreaterOrEqual(r.Wins["пік 2+ t" + t][0] + r.Wins["пік 2+ t" + t][1] + r.Wins["пік 2+ t" + t][2], 60, "«пік 2+» досить для заміру, рівень " + t);
            for (int t = 1; t <= CareerContent.MaxTier; t++)
            {
                Assert.That(r.Rate("t" + t + " тижневі"), Is.InRange(0.48, 0.70), "тижневі, рівень " + t);
                foreach (var sx in new[] { " ч", " ж" })
                    Assert.That(r.Rate("t" + t + " тижневі" + sx), Is.InRange(0.45, 0.72), "тижневі, рівень " + t + "," + sx);
                Assert.That(r.Rate("t" + t + " титул"), Is.InRange(0.28, 0.55), "титул, рівень " + t);
                string feud = "t" + t + " ворожнеча";
                if (r.Wins.ContainsKey(feud) && r.Wins[feud][0] + r.Wins[feud][1] + r.Wins[feud][2] >= 60)
                    Assert.That(r.Rate(feud), Is.InRange(0.38, 0.62), "ворожнеча з Молотом, рівень " + t);
            }
        }
    }
}
