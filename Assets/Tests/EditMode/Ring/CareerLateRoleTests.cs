using System;
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
    /// Пізні ролі (TRPG_DESIGN §5.3, черга П5 від ПМ 12.11.2026, пункт 2): «Пізні ролі (після першого сезону): Ікона (+1 ажіотажу на
    /// старті матчу), Легенда (додатковий жетон реверсу), Зірка поза рингом (промо дають гроші, але менше часу на тренування)».
    /// </summary>
    public class CareerLateRoleTests
    {
        private static CareerContent C { get { return CareerKit.Content(); } }

        private static RingText Text()
        {
            return RingText.FromJson(CareerKit.Read("strings_uk.json"), CareerKit.Read("ui_uk.json"), CareerKit.Read("backstage_uk.json"));
        }

        private static CareerSession AtSeasonEnd(ulong seed)
        {
            var s = CareerKit.NewHero(seed);
            var rep = new CareerRunReport();
            for (int guard = 0; s.Stage != CareerStage.SeasonEnd; guard++)
            {
                Assert.Less(guard, 3000);
                CareerBot.Step(s, 0, rep);
            }
            return s;
        }

        /// <summary>Звичайне шоу тижня — до виходу на ринг (картка).</summary>
        private static CareerSession AtWeeklyCard(ulong seed)
        {
            var s = CareerKit.NewHero(seed);
            var rep = new CareerRunReport();
            for (int guard = 0; !(s.Stage == CareerStage.Show && s.State.Phase == ShowPhase.Card && !s.State.IsFeudShow); guard++)
            {
                Assert.Less(guard, 3000);
                CareerBot.Step(s, 0, rep);
            }
            return s;
        }

        /// <summary>Сезон 1, велике шоу ворожнечі з Молотом — перед виходом на ринг.</summary>
        private static CareerSession AtFeudMatchPrep(ulong seed)
        {
            var s = CareerKit.NewAtTitle(seed);
            var rep = new CareerRunReport();
            for (int guard = 0; !(s.Stage == CareerStage.Show && s.State.Phase == ShowPhase.Feud && s.Feud.Stage == RingStage.MatchPrep); guard++)
            {
                Assert.Less(guard, 3000, "бот не дійшов до матчу ворожнечі");
                CareerBot.Step(s, 0, rep);
            }
            return s;
        }

        /// <summary>Свіжа сесія з того самого зліпка, але з іншою чинною пізньою роллю (як сейв, де її вже обрано).</summary>
        private static CareerSession RestoredWith(CareerSession s, ulong seed, LateRole role)
        {
            string snap = s.CaptureState();
            string with = Regex.Replace(snap, @"\|late=\d+", "|late=" + (int)role);
            StringAssert.Contains("|late=" + (int)role + "|", with);
            var t = CareerKit.Fresh(seed, C);
            t.RestoreState(with);
            Assert.AreEqual(role, t.State.LateRole);
            return t;
        }

        /// <summary>Крутити бота, поки не почнеться сезон <paramref name="season"/>.</summary>
        private static void ToSeason(CareerSession s, int season)
        {
            var rep = new CareerRunReport();
            for (int guard = 0; !(s.State.Season == season && s.Stage == CareerStage.Week); guard++) { Assert.Less(guard, 3000); CareerBot.Step(s, 0, rep); }
        }

        private static CareerSession Copy(CareerSession s, ulong seed, LateRole role)
        {
            var t = CareerKit.Fresh(seed, C);
            t.RestoreState(s.CaptureState());
            t.State.LateRole = role;
            return t;
        }

        [Test]
        public void ChosenAtTheSeasonEnd_FromTheFirst_OneAtATime_ActsFromTheNextSeason_ChangeableAtEverySeasonEnd()
        {
            var w = CareerKit.NewHero(1);
            Assert.IsFalse(w.CanChooseLateRole, "до кінця першого сезону — ні");
            Assert.IsFalse(w.ChooseLateRole(LateRole.Icon));
            Assert.AreEqual(LateRole.None, w.State.LateRole);
            Assert.AreEqual(LateRole.None, w.State.NextLateRole);

            var s = AtSeasonEnd(2);
            Assert.AreEqual(1, s.State.Season);
            Assert.IsTrue(s.CanChooseLateRole, "кінець першого сезону — так");
            Assert.IsFalse(s.ChooseLateRole((LateRole)99), "невідомої ролі немає");
            Assert.IsTrue(s.ChooseLateRole(LateRole.Icon));
            Assert.IsTrue(s.ChooseLateRole(LateRole.Legend), "передумати — поки екран кінця сезону");
            Assert.AreEqual(LateRole.Legend, s.State.NextLateRole, "одна на раз");
            Assert.AreEqual(LateRole.None, s.State.LateRole, "роль, обрана в кінці сезону, діє з наступного сезону");
            Assert.AreEqual(LateRole.None, s.Feud.LateRole, "і матч ворожнечі цього сезону — ще без неї");
            Assert.AreEqual(LateRole.Legend, s.GetCareerView().NextLateRole);
            Assert.AreEqual(LateRole.None, s.GetCareerView().LateRole);

            var rep = new CareerRunReport();
            for (int guard = 0; s.Stage == CareerStage.SeasonEnd; guard++) { Assert.Less(guard, 3000); CareerBot.Step(s, 0, rep); }
            Assert.AreEqual(2, s.State.Season);
            Assert.AreEqual(LateRole.Legend, s.State.LateRole, "новий сезон — обрана роль діє");
            Assert.AreEqual(LateRole.Legend, s.Feud.LateRole, "ворожнеча знає роль для свого матчу");
            Assert.AreEqual(LateRole.Legend, s.GetCareerView().LateRole);
            Assert.IsFalse(s.CanChooseLateRole);
            Assert.IsFalse(s.ChooseLateRole(LateRole.Icon), "посеред сезону не міняється");
            Assert.AreEqual(LateRole.Legend, s.State.LateRole);
            Assert.AreEqual(LateRole.Legend, s.State.NextLateRole);

            for (int guard = 0; s.Stage != CareerStage.SeasonEnd; guard++) { Assert.Less(guard, 3000); CareerBot.Step(s, 0, rep); }
            Assert.IsTrue(s.ChooseLateRole(LateRole.None), "у кінці наступного — можна й зняти");
            Assert.AreEqual(LateRole.Legend, s.State.LateRole, "до кінця цього сезону — ще Легенда");
            ToSeason(s, 3);
            Assert.AreEqual(LateRole.None, s.State.LateRole);
            Assert.AreEqual(LateRole.None, s.Feud.LateRole);
        }

        [Test]
        public void TheTitleMatch_AtTheSeasonEnd_PlaysTheRoleOfThisSeason_NotTheOneJustChosen()
        {
            var s = AtSeasonEnd(8);
            s.State.LateRole = LateRole.Icon; s.State.NextLateRole = LateRole.Icon;
            s.State.Report.TitleEligible = true; s.State.Report.TitleDone = false;
            Assert.IsTrue(s.CanStartTitleMatch);
            Assert.IsTrue(s.ChooseLateRole(LateRole.Legend), "обрати можна й до титульного матчу");
            s.StartTitleMatch();
            var e = s.StartShow();
            Assert.IsNotNull(e);
            var plain = Development.ToModifiers(s.Feud.State.Perks);
            Assert.AreEqual(plain.StartHypeBonus + LateRoles.IconStartHype, e.Setup.PlayerMods.StartHypeBonus, "Ікона цього сезону — і на титулі");
            Assert.AreEqual(plain.ExtraReversalTokens, e.Setup.PlayerMods.ExtraReversalTokens, "Легенди на титулі ще немає");
            Assert.IsFalse(s.ChooseLateRole(LateRole.OffRingStar), "посеред матчу — ні");

            var rep = new CareerRunReport();
            CareerBot.PlayMatch(s, 0, rep);
            s.FinishMatch();
            ToSeason(s, 2);
            Assert.AreEqual(LateRole.Legend, s.State.LateRole, "Легенда — з наступного сезону");
        }

        [Test]
        public void TheFeudMatch_InTheCareer_TakesTheLateRole_AlsoAfterAFreshRestore()
        {
            var s = AtFeudMatchPrep(77);
            var none = RestoredWith(s, 77, LateRole.None).StartFeudMatch();
            var icon = RestoredWith(s, 77, LateRole.Icon).StartFeudMatch();
            var legend = RestoredWith(s, 77, LateRole.Legend).StartFeudMatch();
            Assert.Less(none.State.Hype, MatchRules.MaxHype, "замір не впирається в стелю ажіотажу");
            Assert.AreEqual(none.State.Hype + LateRoles.IconStartHype, icon.State.Hype, "Ікона — ажіотаж на старті матчу ворожнечі");
            Assert.AreEqual(none.State.Player.ReversalTokens, icon.State.Player.ReversalTokens);
            Assert.AreEqual(none.State.Player.ReversalTokens + LateRoles.LegendReversalTokens, legend.State.Player.ReversalTokens, "Легенда — жетон реверсу");
            Assert.AreEqual(none.State.Hype, legend.State.Hype);
        }

        [Test]
        public void Icon_PlusStartHype_Legend_PlusAReversalToken_InTheWeeklyShow()
        {
            var s = AtWeeklyCard(3);
            var none = Copy(s, 3, LateRole.None).StartShow();
            var icon = Copy(s, 3, LateRole.Icon).StartShow();
            var legend = Copy(s, 3, LateRole.Legend).StartShow();
            var star = Copy(s, 3, LateRole.OffRingStar).StartShow();
            Assert.Less(none.State.Hype, MatchRules.MaxHype, "замір не впирається в стелю ажіотажу");
            Assert.AreEqual(none.State.Hype + LateRoles.IconStartHype, icon.State.Hype);
            Assert.AreEqual(none.State.Player.ReversalTokens, icon.State.Player.ReversalTokens);
            Assert.AreEqual(none.State.Player.ReversalTokens + LateRoles.LegendReversalTokens, legend.State.Player.ReversalTokens);
            Assert.AreEqual(none.State.Hype, legend.State.Hype);
            Assert.AreEqual(none.State.Hype, star.State.Hype, "зірка — поза рингом, у матчі нічого");
            Assert.AreEqual(none.State.Player.ReversalTokens, star.State.Player.ReversalTokens);
        }

        [Test]
        public void TheFeudMatch_TakesTheLateRoleToo()
        {
            var f = new FeudState();
            var plain = f.BuildMatchSetup(C.Feud);
            var icon = f.BuildMatchSetup(C.Feud, LateRole.Icon);
            Assert.AreEqual(Math.Min(10, plain.StartHype + LateRoles.IconStartHype), icon.StartHype);
            var legend = f.BuildMatchSetup(C.Feud, LateRole.Legend);
            Assert.AreEqual(plain.PlayerMods.ExtraReversalTokens + LateRoles.LegendReversalTokens, legend.PlayerMods.ExtraReversalTokens);
            Assert.AreEqual(plain.StartHype, legend.StartHype);
        }

        [Test]
        public void Star_PromoPaysOnTenAndSevenNothingOnSix_TrainingOncePerWeek()
        {
            bool strong = false, partial = false, miss = false;
            for (ulong seed = 1; seed < 80 && !(strong && partial && miss); seed++)
            {
                var s = CareerKit.NewHero(seed);
                s.State.LateRole = LateRole.OffRingStar;
                int money = s.State.Money;
                var r = s.Promo();
                Assert.IsTrue(r.Ok);
                int pay = r.Tier == RollTier.Strong ? LateRoles.StarPromoMoneyStrong : r.Tier == RollTier.Partial ? LateRoles.StarPromoMoneyPartial : 0;
                Assert.AreEqual(pay, r.MoneyDelta, "ступінь " + r.Tier);
                Assert.AreEqual(money + pay, s.State.Money);
                strong |= r.Tier == RollTier.Strong; partial |= r.Tier == RollTier.Partial; miss |= r.Tier == RollTier.Miss;

                var plain = CareerKit.NewHero(seed);
                int m0 = plain.State.Money;
                Assert.AreEqual(0, plain.Promo().MoneyDelta, "без ролі промо не платить");
                Assert.AreEqual(m0, plain.State.Money);
            }
            Assert.IsTrue(strong && partial && miss, "перебрано всі три ступені");

            var t = CareerKit.NewHero(5);
            t.State.LateRole = LateRole.OffRingStar;
            Assert.IsFalse(t.GetWeekView().StarTrainingUsed);
            Assert.IsTrue(t.Train(false).Ok);
            Assert.IsTrue(t.GetWeekView().StarTrainingUsed);
            int left = t.State.ActionsLeft, dev = t.DevPoints;
            var again = t.Train(false);
            Assert.IsFalse(again.Ok);
            Assert.AreEqual("star_training", again.Reason, "менше часу на тренування");
            Assert.AreEqual(left, t.State.ActionsLeft, "дію не витрачено");
            Assert.AreEqual(dev, t.DevPoints);
            Assert.IsFalse(t.Train(true).Ok, "і з тренером — теж");

            var u = CareerKit.NewHero(5);
            Assert.IsTrue(u.Train(false).Ok);
            Assert.IsTrue(u.Train(false).Ok, "без ролі — тренуйся скільки є дій");
            Assert.IsFalse(u.GetWeekView().StarTrainingUsed);
        }

        [Test]
        public void Save_KeepsTheLateRoleAndTheChosenOne_InAFreshSession_OldAndBrokenSavesDoNotThrow()
        {
            var s = AtSeasonEnd(6);
            s.ChooseLateRole(LateRole.OffRingStar);
            var snap = s.CaptureState();
            StringAssert.Contains("|late=" + (int)LateRole.None, snap);
            StringAssert.Contains("|latenext=" + (int)LateRole.OffRingStar, snap);
            var t = CareerKit.Fresh(6, C);
            t.RestoreState(snap);
            Assert.AreEqual(LateRole.None, t.State.LateRole);
            Assert.AreEqual(LateRole.OffRingStar, t.State.NextLateRole, "обрана на наступний сезон — у сейві");
            ToSeason(t, 2);
            Assert.AreEqual(LateRole.OffRingStar, t.State.LateRole);

            var mid = t.CaptureState();
            var u = CareerKit.Fresh(6, C);
            u.RestoreState(mid);
            Assert.AreEqual(LateRole.OffRingStar, u.State.LateRole);
            Assert.AreEqual(LateRole.OffRingStar, u.Feud.LateRole, "і ворожнеча — у свіжій сесії");

            var noNext = Regex.Replace(mid, @"\|latenext=\d+", "");
            Assert.AreNotEqual(mid, noNext);
            var v = CareerKit.Fresh(6, C);
            v.RestoreState(noNext);
            Assert.AreEqual(LateRole.OffRingStar, v.State.NextLateRole, "сейв до 12.11.2026 — на наступний та сама, що діє");

            var old = Regex.Replace(noNext, @"\|late=\d+", "");
            Assert.AreNotEqual(noNext, old);
            var w = CareerKit.Fresh(6, C);
            w.RestoreState(old);
            Assert.AreEqual(LateRole.None, w.State.LateRole, "старий сейв — без пізньої ролі");
            Assert.AreEqual(LateRole.None, w.State.NextLateRole);
            Assert.AreEqual(LateRole.None, w.Feud.LateRole);

            foreach (var bad in new[] { "x", "", "99", "-1", "1.5", "2x" })
            {
                var broken = Regex.Replace(mid, @"\|late=\d+", "|late=" + bad);
                var b = CareerKit.Fresh(6, C);
                Assert.DoesNotThrow(() => b.RestoreState(broken), "late=" + bad);
                Assert.AreEqual(LateRole.None, b.State.LateRole, "late=" + bad + " — без ролі, а не виняток");
                var brokenNext = Regex.Replace(mid, @"\|latenext=\d+", "|latenext=" + bad);
                var c = CareerKit.Fresh(6, C);
                Assert.DoesNotThrow(() => c.RestoreState(brokenNext), "latenext=" + bad);
                Assert.AreEqual(LateRole.OffRingStar, c.State.NextLateRole, "latenext=" + bad + " — та сама, що діє");
            }
        }

        [Test]
        public void Model_ThreeRolesAndNone_TheChosenIsMarked_DescriptionsCarryTheNumbers()
        {
            var t = Text();
            var w = new LateRoleModel(CareerKit.NewHero(7), t);
            Assert.IsFalse(w.Visible, "посеред сезону блоку вибору немає");
            Assert.IsNull(w.CurrentText);

            var s = AtSeasonEnd(7);
            var m = new LateRoleModel(s, t);
            Assert.IsTrue(m.Visible);
            Assert.AreEqual("Пізня роль", m.TitleText);
            var rows = m.Rows;
            CollectionAssert.AreEqual(new[] { LateRole.Icon, LateRole.Legend, LateRole.OffRingStar, LateRole.None }, rows.Select(r => r.Role).ToArray());
            CollectionAssert.AreEqual(new[] { "Ікона", "Легенда", "Зірка поза рингом", "Без пізньої ролі" }, rows.Select(r => r.Name).ToArray());
            Assert.IsTrue(rows[3].Selected, "спершу — без ролі");
            Assert.IsNull(m.NextText);
            StringAssert.Contains("+" + LateRoles.IconStartHype, rows[0].Description);
            Assert.AreEqual(1, LateRoles.LegendReversalTokens, "опис Легенди каже «додатковий жетон» — один; інше число — інший текст");
            Assert.AreEqual("Додатковий жетон реверсу в кожному матчі", rows[1].Description);
            Assert.AreEqual("Без пізньої ролі: жодних поправок", rows[3].Description);
            StringAssert.Contains("з наступного сезону", t.Get("c.late.hint"));
            StringAssert.Contains("10+ — " + LateRoles.StarPromoMoneyStrong, rows[2].Description);
            StringAssert.Contains("7–9 — " + LateRoles.StarPromoMoneyPartial, rows[2].Description);
            foreach (var r in rows) StringAssert.DoesNotContain("{", r.Description);

            Assert.IsTrue(m.Choose(LateRole.Icon));
            Assert.IsTrue(m.Rows[0].Selected, "позначена — обрана на наступний сезон");
            Assert.IsFalse(m.Rows[3].Selected);
            Assert.IsNull(m.CurrentText, "діє досі — без ролі");
            Assert.AreEqual("З наступного сезону: Ікона", m.NextText);
            Assert.IsTrue(m.Choose(LateRole.None));
            Assert.IsNull(m.NextText, "обрана та сама, що діє, — рядка немає");

            m.Choose(LateRole.Icon);
            ToSeason(s, 2);
            Assert.AreEqual("Пізня роль: Ікона", m.CurrentText);
            Assert.IsNull(m.NextText);
            Assert.AreEqual("Зірка поза рингом: тренування — раз на тиждень", t.Get("week.reason.star_training"));
        }
    }
}
