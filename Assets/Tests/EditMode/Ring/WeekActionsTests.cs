using System;
using System.Collections.Generic;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Core.Ring.Match;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>П'ять дій тижня (TRPG_DESIGN §6.1): три на тиждень, кидки з трьома ступенями, травми й гроші.</summary>
    public class WeekActionsTests
    {
        /// <summary>Знайти зерно, за якого перша дія тижня дає потрібний ступінь (кубик детермінований).</summary>
        private static CareerSession HeroWhere(Func<CareerSession, WeekActionResult> act, RollTier tier, out WeekActionResult res, Gimmick g = Gimmick.Showman,
            bool legacyBackstage = false)
        {
            for (ulong seed = 1; seed < 600; seed++)
            {
                CareerSession s;
                if (legacyBackstage)
                {
                    s = CareerKit.NewAtCreate(seed, CareerKit.ContentWithoutBackstage());
                    s.CreateHero(CareerKit.Draft(g, CareerRole.Hero, RingAttr.Technique));
                }
                else s = CareerKit.NewHero(seed, g, CareerRole.Hero, RingAttr.Technique);
                var r = act(s);
                if (r.HasRoll && r.Tier == tier) { res = r; return s; }
            }
            throw new InvalidOperationException("не знайшов зерно для " + tier);
        }

        /// <summary>
        /// Огляд QA (<c>-ring-shot-opponent</c>, рев'ю ПМ 12.11.2026): суперник тижня за id — лише на початку звичайного тижня і лише
        /// з пулу букера; план і умови — як склав би букер; підміна живе в сейві. Барон — найдовше ім'я ростеру й чемпіон місцевої.
        /// </summary>
        [Test]
        public void ForceOpponent_ForTheReview_OnlyAtWeekStart_OnlyFromTheBookersPool()
        {
            var s = CareerKit.NewHero(1);
            Assert.AreEqual(1, s.State.Week);
            Assert.IsFalse(s.State.IsBig);
            Assert.IsTrue(s.ForceOpponent("baron"), "Барон — у пулі чоловічого дивізіону місцевої");
            Assert.AreEqual("baron", s.State.Opponent);
            var baron = s.Content.FindNpc("baron");
            Assert.AreEqual(ShowBooker.PlanFor(baron, s.Popularity, s.Trust, s.PopularityFloor, s.PopularityCeiling), s.State.Plan, "план — як букерів");
            Assert.AreEqual(ShowBooker.StipulationsFor(s.FeudLevel("baron")), s.State.Stip);

            NpcDef female = null;
            foreach (var n in s.Federation.Roster) if (n.Female) female = n;
            Assert.IsNotNull(female);
            Assert.IsFalse(s.ForceOpponent(female.Id), "чужий дивізіон — ні");
            Assert.IsFalse(s.ForceOpponent(CareerContent.HammerId), "Молот — лише після розв'язки ворожнечі");
            Assert.IsFalse(s.ForceOpponent("no_such_npc"));
            Assert.AreEqual("baron", s.State.Opponent, "невдала підміна картку не змінює");

            var blob = s.CaptureState();
            var t = CareerKit.Fresh(1, s.Content);
            t.RestoreState(blob);
            Assert.AreEqual("baron", t.State.Opponent, "підміна — у сейві, як звичайна картка");

            Assert.IsTrue(s.Train(false).Ok);
            Assert.IsFalse(s.ForceOpponent("baron"), "після першої дії — ні");
        }

        [Test]
        public void Week_HasThreeActions_FourthIsRefused_AndEndWeekGoesToShow()
        {
            var s = CareerKit.NewHero(1);
            Assert.AreEqual(3, s.State.ActionsLeft);
            Assert.IsTrue(s.Train(false).Ok);
            Assert.IsTrue(s.SideJob().Ok);
            Assert.IsTrue(s.Train(false).Ok);
            var r = s.Train(false);
            Assert.IsFalse(r.Ok);
            Assert.AreEqual("no_actions", r.Reason);
            Assert.AreEqual(3, s.GetWeekView().ActionsDone.Count);
            s.EndWeek();
            Assert.AreEqual(CareerStage.Show, s.Stage);
            Assert.AreEqual(ShowPhase.Card, s.State.Phase);
            Assert.IsFalse(s.Train(false).Ok, "поза тижнем дій немає");
        }

        [Test]
        public void Training_GivesOnePoint_TrainerAddsOneForMoney()
        {
            var s = CareerKit.NewHero(2);
            int pts = s.DevPoints, money = s.Money;
            var r = s.Train(false);
            Assert.AreEqual(pts + 1, s.DevPoints);
            Assert.AreEqual(0, r.MoneyDelta);
            r = s.Train(true);
            Assert.AreEqual(pts + 1 + 2, s.DevPoints);
            Assert.AreEqual(money - CareerRules.TrainerCost, s.Money);
        }

        [Test]
        public void Training_WithTrainer_NeedsMoney()
        {
            var s = CareerKit.NewHero(2);
            s.State.Money = CareerRules.TrainerCost - 1;
            var r = s.Train(true);
            Assert.IsFalse(r.Ok);
            Assert.AreEqual("money", r.Reason);
            Assert.AreEqual(3, s.State.ActionsLeft, "відмова не з'їдає дію");
        }

        [Test]
        public void Recovery_HealsZoneDamageAndInjury_DoctorHealsFaster_NothingToHealIsRefused()
        {
            var s = CareerKit.NewHero(3);
            var none = s.Recover(BodyZone.Legs, false);
            Assert.IsFalse(none.Ok);
            Assert.AreEqual("nothing", none.Reason);
            s.State.ZoneDamage[(int)BodyZone.Legs] = 2;
            s.State.Injury[(int)BodyZone.Body] = 3;
            Assert.IsTrue(s.Recover(BodyZone.Legs, false).Ok);
            Assert.AreEqual(0, s.State.ZoneDamage[(int)BodyZone.Legs]);
            int money = s.Money;
            var r = s.Recover(BodyZone.Body, true);
            Assert.IsTrue(r.Ok);
            Assert.AreEqual(1, s.State.Injury[(int)BodyZone.Body], "лікар скорочує травму на два тижні");
            Assert.AreEqual(money - CareerRules.DoctorCost, s.Money);
            Assert.IsTrue(s.GetWeekView().CanRecover);
        }

        [Test]
        public void Promo_Strong_RaisesFeud_AndGivesHypeBonus()
        {
            WeekActionResult r;
            var s = HeroWhere(x => x.Promo(), RollTier.Strong, out r);
            Assert.IsTrue(r.Ok);
            Assert.GreaterOrEqual(r.Total, 10);
            Assert.AreEqual(CareerRules.PromoStrongHype, s.State.NextHypeBonus);
            Assert.AreEqual(CareerRules.PromoStrongHype, s.GetShowCard().PromoHypeBonus);
            Assert.AreEqual(1, r.FeudDelta, "ворожнеча з суперником тижня +1");
            Assert.IsFalse(r.NeedsChoice);
        }

        [Test]
        public void Promo_Partial_OneOfTwo_AndMustBeChosenBeforeAnythingElse()
        {
            WeekActionResult r;
            var s = HeroWhere(x => x.Promo(), RollTier.Partial, out r);
            Assert.IsTrue(r.NeedsChoice);
            Assert.IsTrue(s.State.PromoChoicePending);
            Assert.AreEqual("choice_pending", s.Train(false).Reason);
            Assert.Throws<InvalidOperationException>(() => s.EndWeek());
            var c = s.ChoosePromoReward(WeekPromoReward.Hype);
            Assert.IsTrue(c.Ok);
            Assert.AreEqual(CareerRules.PromoStrongHype, s.State.NextHypeBonus);
            Assert.IsFalse(s.ChoosePromoReward(WeekPromoReward.Hype).Ok, "нагороду беруть раз");
            s.EndWeek();
        }

        [Test]
        public void Promo_Partial_FeudChoice_RaisesFeud()
        {
            WeekActionResult r;
            var s = HeroWhere(x => x.Promo(), RollTier.Partial, out r);
            string opp = s.State.Opponent;
            int before = s.FeudLevel(opp);
            s.ChoosePromoReward(WeekPromoReward.Feud);
            Assert.AreEqual(before + 1, s.FeudLevel(opp));
            Assert.AreEqual(0, s.State.NextHypeBonus);
        }

        [Test]
        public void Promo_Miss_CoolsCrowd_AndGivesRivalImpulse()
        {
            WeekActionResult r;
            var s = HeroWhere(x => x.Promo(), RollTier.Miss, out r);
            Assert.AreEqual(-1, s.State.NextHypeBonus);
            Assert.AreEqual(1, s.State.RivalBoost);
            Assert.IsTrue(r.RivalBoosted);
            // імпульс і охолодження доходять до матчу
            s.EndWeek();
            var e = s.StartShow();
            Assert.GreaterOrEqual(e.State.Rival.Momentum, 1 + s.Content.FindNpc(s.State.Opponent).StartMomentum);
        }

        [Test]
        public void Promo_HeadInjury_LowersTheRoll()
        {
            var a = CareerKit.NewHero(8, Gimmick.Showman);
            var b = CareerKit.NewHero(8, Gimmick.Showman);
            b.State.Injury[(int)BodyZone.Head] = 2;
            Assert.AreEqual(a.Promo().Modifier - 1, b.Promo().Modifier);
        }

        [Test]
        public void Backstage_WithoutScenesInTheContent_StaysTheOldCharismaRoll_Strong_TrustPlusOne_AndRumor_PartialRumorOnly_MissNothing()
        {
            WeekActionResult r;
            var s = HeroWhere(x => x.Backstage(), RollTier.Strong, out r, Gimmick.Showman, true);
            Assert.AreEqual(1, r.TrustDelta);
            StringAssert.StartsWith("rumor.", r.RumorKey);
            s = HeroWhere(x => x.Backstage(), RollTier.Partial, out r, Gimmick.Showman, true);
            Assert.AreEqual(0, r.TrustDelta);
            Assert.IsNotNull(r.RumorKey);
            s = HeroWhere(x => x.Backstage(), RollTier.Miss, out r, Gimmick.Showman, true);
            Assert.AreEqual(0, r.TrustDelta);
            Assert.IsNull(r.RumorKey);
        }

        [Test]
        public void SideJob_GivesMoney_NoExperience_RiskOfInjury()
        {
            bool sawInjuryRisk = false, sawSafe = false;
            for (ulong seed = 1; seed < 200 && !(sawInjuryRisk && sawSafe); seed++)
            {
                var s = CareerKit.NewHero(seed);
                int money = s.Money, pts = s.DevPoints;
                var r = s.SideJob();
                Assert.Greater(s.Money, money);
                Assert.AreEqual(pts, s.DevPoints, "підробіток досвіду не дає");
                int dmg = 0; foreach (var z in s.State.ZoneDamage) dmg += z;
                foreach (var z in s.State.Injury) dmg += z > 0 ? 3 : 0;
                if (r.Roll <= CareerRules.SideJobInjuryAtMost) { Assert.AreEqual(CareerRules.SideJobZoneDamage, dmg); sawInjuryRisk = true; }
                else { Assert.AreEqual(0, dmg); sawSafe = true; }
            }
            Assert.IsTrue(sawInjuryRisk && sawSafe);
        }

        [Test]
        public void ZoneDamageThree_BecomesInjury_OfOneToFourWeeks()
        {
            var seen = new HashSet<int>();
            for (ulong seed = 1; seed < 80; seed++)
            {
                var s = CareerKit.NewHero(seed);
                s.State.Week = (int)(seed % 7) + 1;
                // два підробітки по 2 ушкодження: 2 + 2 ≥ 3 → травма
                s.State.ZoneDamage[0] = s.State.ZoneDamage[1] = s.State.ZoneDamage[2] = 2;
                for (int i = 0; i < 3; i++)
                {
                    var r = s.SideJob();
                    if (r.InjuredZone >= 0)
                    {
                        int w = s.State.Injury[r.InjuredZone];
                        Assert.That(w, Is.InRange(CareerRules.MinInjuryWeeks, CareerRules.MaxInjuryWeeks));
                        Assert.AreEqual(0, s.State.ZoneDamage[r.InjuredZone], "травма скидає ушкодження зони");
                        seen.Add(w);
                    }
                }
            }
            Assert.GreaterOrEqual(seen.Count, 2, "тривалість травми різна");
        }

        [Test]
        public void Injury_GivesMinusOneToMovesOfThatZone_AndAppearsInChances()
        {
            var s = CareerKit.NewHero(4);
            s.EndWeek();
            s.StartShow();
            var healthy = s.GetMoveOptions();
            // та сама сесія: травмуємо ноги — прийоми з ціллю «ноги» дешевші на кидку
            s.State.Injury[(int)BodyZone.Legs] = 2;
            var hurt = s.GetMoveOptions();
            for (int i = 0; i < healthy.Count; i++)
            {
                var m = healthy[i].Move;
                if (!healthy[i].Option.HasRoll || m.Action == MatchActionId.Pin) continue;
                int expected = m.Zone == BodyZone.Legs ? healthy[i].Option.Modifier - 1 : healthy[i].Option.Modifier;
                Assert.AreEqual(expected, hurt[i].Option.Modifier, m.Id);
                Assert.AreEqual(m.Zone == BodyZone.Legs ? 1 : 0, hurt[i].InjuryPenalty, m.Id);
            }
        }

        [Test]
        public void WeekEnd_HealsZonesAndInjuries_ByOne()
        {
            var s = CareerKit.NewHero(5);
            s.State.ZoneDamage[1] = 2; s.State.Injury[2] = 3;
            s.EndWeek(); s.StartShow();
            CareerBot.PlayMatch(s, 0, new CareerRunReport());
            s.FinishMatch();
            int zd = s.State.ZoneDamage[1], inj = s.State.Injury[2];
            s.ContinueFromShow();
            Assert.LessOrEqual(s.State.ZoneDamage[1], Math.Max(0, zd - 1));
            Assert.AreEqual(inj - 1, s.State.Injury[2]);
        }

        [Test]
        public void Withdraw_OnlyWhenInjuredAndNotBigShow_CostsTrust_NoFee()
        {
            var s = CareerKit.NewHero(6);
            s.EndWeek();
            Assert.Throws<InvalidOperationException>(() => s.WithdrawFromShow(), "здоровий не сходить");
            var t = CareerKit.NewHero(6);
            t.State.Injury[0] = 2;
            t.EndWeek();
            int trust = t.Trust, money = t.Money;
            Assert.IsTrue(t.GetShowCard().CanWithdraw);
            t.WithdrawFromShow();
            Assert.AreEqual(trust - 1, t.Trust);
            Assert.AreEqual(money, t.Money);
            Assert.AreEqual(1, t.State.Injury[0]);
            Assert.AreEqual(ShowPhase.Result, t.State.Phase);
            Assert.IsTrue(t.State.Last.Withdrew);
            t.ContinueFromShow();
            Assert.AreEqual(2, t.State.Week);
        }

        [Test]
        public void InjuredShow_StartsWithMoreHype()
        {
            var a = CareerKit.NewHero(15); var b = CareerKit.NewHero(15);
            b.State.Injury[(int)BodyZone.Body] = 2;
            a.EndWeek(); b.EndWeek();
            Assert.AreEqual(a.StartShow().State.Hype + CareerRules.InjuredShowHype, b.StartShow().State.Hype);
        }

        [Test]
        public void WeeklyShow_HasNoPromo_StartHypeIsFeudOnly()
        {
            // Власник 04.11.2026: «промо тільки в фьюді, інши не треба» — звичайне шоу без кидка промо.
            for (ulong seed = 1; seed < 40; seed++)
            {
                var s = CareerKit.NewHero(seed, Gimmick.Showman);
                s.EndWeek();
                var e = s.StartShow();
                Assert.IsFalse(s.State.ShowPromoDone);
                Assert.AreEqual(1 + s.FeudLevel(s.State.Opponent), e.State.Hype);
            }
        }
    }
}
