using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Core.Ring.Match;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Поворот ролі герой ↔ лиходій (TRPG_DESIGN §5.3, черга П5 від ПМ 12.11.2026, пункт 2): «Поворот (герой ↔ лиходій) — подія кар'єри:
    /// потрібна ворожнеча 3+ і план букера або звернення плану». Подія ev_turn: план приносить букер федерації (своїм голосом), звернений
    /// план — зал. «Погодитися» міняє місцями очки героя й лиходія; «лишитися» — +1 до нинішньої ролі.
    /// </summary>
    public class CareerTurnTests
    {
        private static CareerContent C { get { return CareerKit.Content(); } }

        private static void Paths(CareerSession s, int face, int heel, int rebel)
        {
            var f = s.Feud.State;
            f.AddPath(RingPath.Face, face - f.GetPath(RingPath.Face));
            f.AddPath(RingPath.Heel, heel - f.GetPath(RingPath.Heel));
            f.AddPath(RingPath.Rebel, rebel - f.GetPath(RingPath.Rebel));
        }

        /// <summary>
        /// Герой у федерації <paramref name="tier"/> на тижні, коли ev_turn уже дозріла; решта подій сезону вже були. Ворожнеча з першим
        /// суперником картки — <paramref name="feud"/>.
        /// </summary>
        private static CareerSession HeroBeforeTurn(int tier, int feud, ulong seed = 1, CareerRole role = CareerRole.Hero)
        {
            var s = CareerKit.NewHero(seed, Gimmick.Showman, role);
            if (tier > 1) s.EnterFederation(tier, C.Federation(tier).Floor, 1);
            var turn = s.Content.Backstage.Find("ev_turn");
            foreach (var sc in s.Content.Backstage.Scenes)
                if (sc.Kind == BackstageKind.Event && sc.Id != "ev_turn") s.State.BackstageSeen.Add(s.State.Season + ":" + sc.Id);
            s.State.Week = s.BackstageDueWeek(turn);
            foreach (var n in s.BookingPool()) s.State.SetNpcFeud(n.Id, 0);
            var rival = s.BookingPool().First(n => n.Id != CareerContent.HammerId);
            s.State.SetNpcFeud(rival.Id, feud);
            return s;
        }

        private static List<string> Options(PromoRunner r)
        {
            return r.AvailableOptions().Where(v => !v.Locked).Select(v => v.Option.Id).ToList();
        }

        /// <summary>Від відкритої сцени — до вибору; повертає шлях вузлів.</summary>
        private static List<string> ToChoice(PromoRunner r)
        {
            var path = new List<string>();
            int guard = 0;
            while (r.Current.Type != PromoNodeType.Choice)
            {
                Assert.Less(++guard, 20);
                path.Add(r.Current.Id);
                r.Advance();
            }
            path.Add(r.Current.Id);
            return path;
        }

        /// <summary>Від тижня — до старту наступного матчу (звичайне шоу чи велике шоу ворожнечі): рушій на старті.</summary>
        private static MatchEngine NextMatch(CareerSession s)
        {
            if (s.Stage == CareerStage.Week) s.EndWeek();
            Assert.AreEqual(CareerStage.Show, s.Stage);
            if (!s.State.IsFeudShow) return s.StartShow();
            var rep = new CareerRunReport();
            for (int guard = 0; !(s.State.Phase == ShowPhase.Feud && s.Feud.Stage == RingStage.MatchPrep); guard++)
            {
                Assert.Less(guard, 400);
                CareerBot.Step(s, 0, rep);
            }
            return s.StartFeudMatch();
        }

        private static WeekActionResult ChooseAndFinish(CareerSession s, string option)
        {
            var r = s.CurrentBackstage;
            r.Choose(option);
            while (!r.IsFinished) r.Advance();
            return s.FinishBackstage();
        }

        // ---------------- ядро ----------------

        [Test]
        public void Turn_SwapsHeroAndVillainPoints_PerksStay_AndOnlyFromTheOppositeRole()
        {
            var s = CareerKit.NewHero(3);
            Paths(s, 6, 1, 2);
            s.Feud.State.Perks.Add(PerkCatalog.SecondWind);
            Assert.AreEqual(RingPath.Face, s.CurrentRole);

            Assert.IsTrue(s.Turn(RingPath.Heel));
            Assert.AreEqual(6, s.Feud.State.GetPath(RingPath.Heel), "вага образу переходить у нову роль");
            Assert.AreEqual(1, s.Feud.State.GetPath(RingPath.Face));
            Assert.AreEqual(2, s.Feud.State.GetPath(RingPath.Rebel), "бунтар не чіпається");
            Assert.AreEqual(RingPath.Heel, s.CurrentRole);
            Assert.AreEqual(CareerRole.Villain, s.State.Role, "заявлена роль — теж");
            Assert.IsTrue(s.Feud.State.Perks.Contains(PerkCatalog.SecondWind), "куплені перки лишаються");

            Assert.IsFalse(s.Turn(RingPath.Heel), "у ту саму роль — нічого");
            Assert.IsFalse(s.Turn(RingPath.Rebel), "бунтар — не роль повороту");
            Assert.AreEqual(6, s.Feud.State.GetPath(RingPath.Heel));

            Assert.IsTrue(s.Turn(RingPath.Face), "і назад");
            Assert.AreEqual(6, s.Feud.State.GetPath(RingPath.Face));
            Assert.AreEqual(CareerRole.Hero, s.State.Role);

            Paths(s, 1, 1, 5);
            Assert.AreEqual(RingPath.Rebel, s.CurrentRole);
            Assert.IsFalse(s.Turn(RingPath.Heel), "бунтар (без ролі) не повертається");
            Assert.IsFalse(s.Turn(RingPath.Face));
        }

        [Test]
        public void Turn_OnEqualPoints_TheNewRoleStillLeads_EvenAgainstTheRebel()
        {
            var s = CareerKit.NewHero(4);
            Paths(s, 3, 3, 0);
            Assert.AreEqual(RingPath.Face, s.CurrentRole, "нічия — герой");
            Assert.IsTrue(s.Turn(RingPath.Heel));
            Assert.AreEqual(RingPath.Heel, s.CurrentRole, "обмін рівних нічого б не дав — новій ролі +1");
            Assert.AreEqual(4, s.Feud.State.GetPath(RingPath.Heel));

            Paths(s, 3, 0, 3);
            Assert.AreEqual(RingPath.Face, s.CurrentRole);
            Assert.IsTrue(s.Turn(RingPath.Heel));
            Assert.AreEqual(RingPath.Heel, s.CurrentRole);

            Paths(s, 0, 3, 3);
            Assert.AreEqual(RingPath.Heel, s.CurrentRole);
            Assert.IsTrue(s.Turn(RingPath.Face));
            Assert.AreEqual(RingPath.Face, s.CurrentRole, "бунтар із тими самими очками не перехоплює роль");
        }

        // ---------------- подія ----------------

        [Test]
        public void TurnScene_NeedsAFeudOfThree_AndARole_TheRebelNeverGetsIt()
        {
            var s = HeroBeforeTurn(1, 2);
            Assert.AreEqual(2, s.MaxFeudLevel);
            Assert.AreNotEqual("ev_turn", s.Backstage().SceneId, "ворожнеча 2 — ще ні");

            s = HeroBeforeTurn(1, 3);
            Assert.AreEqual(3, s.MaxFeudLevel);
            Assert.AreEqual("ev_turn", s.Backstage().SceneId, "ворожнеча 3 — так");

            s = HeroBeforeTurn(1, 3);
            Paths(s, 1, 1, 4);
            Assert.AreNotEqual("ev_turn", s.Backstage().SceneId, "бунтар без ролі — повороту немає");

            s = HeroBeforeTurn(1, 3);
            s.State.Week = s.BackstageDueWeek(s.Content.Backstage.Find("ev_turn")) - 1;
            if (s.State.Week >= 1) Assert.AreNotEqual("ev_turn", s.Backstage().SceneId, "до свого тижня — ні");
        }

        [Test]
        public void TurnScene_OldFeudsOfAnotherFederation_DoNotCount()
        {
            var s = HeroBeforeTurn(1, 3);
            string local = s.BookingPool().First(n => n.Id != CareerContent.HammerId).Id;
            s.EnterFederation(2, C.Federation(2).Floor, 1);
            Assert.IsFalse(s.BookingPool().Any(n => n.Id == local));
            foreach (var n in s.BookingPool()) s.State.SetNpcFeud(n.Id, 0);
            Assert.AreEqual(3, s.State.GetNpcFeud(local), "стара ворожнеча лишилась у стані");
            Assert.AreEqual(0, s.MaxFeudLevel, "але рахуються лише суперники картки");
        }

        [Test]
        public void TurnScene_TheBookersPlan_ComesFromEachFederationsOwnBooker_TurnEarnsTrust()
        {
            for (int tier = 1; tier <= C.Federations.Count; tier++)
            {
                var s = HeroBeforeTurn(tier, 3, (ulong)(10 + tier));
                Assert.AreEqual("ev_turn", s.Backstage().SceneId, "федерація " + tier);
                var r = s.CurrentBackstage;
                var path = ToChoice(r);
                var proposal = s.CurrentBackstageScene.Promo.Find(path[0]);
                Assert.AreEqual(s.Federation.Booker, proposal.Speaker, "план повороту приносить букер цієї федерації: " + string.Join(" → ", path));
                Assert.AreEqual("fc", r.Current.Id, "герой → лиходій");
                CollectionAssert.AreEquivalent(new[] { "turn", "stay" }, Options(r), "план букера: «звернений» варіант схований");

                int trust = s.Trust;
                var res = ChooseAndFinish(s, "turn");
                Assert.AreEqual(RingPath.Heel, s.CurrentRole);
                Assert.AreEqual(RingPath.Heel, res.TurnedTo);
                Assert.AreEqual(Math.Min(trust + 1, CareerRules.MaxTrust), s.Trust, "план букера виконано — довіра");
                Assert.AreEqual(s.Trust - trust, res.TrustDelta);
            }
        }

        [Test]
        public void TurnScene_Stay_DeepensTheRole_NoTurnNoTrust()
        {
            var s = HeroBeforeTurn(1, 3, 21);
            s.Backstage();
            ToChoice(s.CurrentBackstage);
            int face = s.Feud.State.GetPath(RingPath.Face), trust = s.Trust;
            var res = ChooseAndFinish(s, "stay");
            Assert.AreEqual(RingPath.Face, s.CurrentRole);
            Assert.IsNull(res.TurnedTo);
            Assert.AreEqual(face + 1, s.Feud.State.GetPath(RingPath.Face), "лишитися собою — +1 до нинішньої ролі");
            Assert.AreEqual(trust, s.Trust, "відмова від повороту довіри не коштує: подія може прийти й наступного сезону");
            Assert.AreNotEqual("ev_turn", s.Backstage().SceneId, "раз за сезон");
        }

        [Test]
        public void TurnScene_AfterASwervedPlan_TheCrowdTurnsYou_HypeInsteadOfTrust()
        {
            var s = HeroBeforeTurn(2, 3, 22);
            s.State.LastPlanOutcome = FeudPlanOutcome.Swerved;
            Assert.AreEqual("ev_turn", s.Backstage().SceneId);
            var r = s.CurrentBackstage;
            var path = ToChoice(r);
            Assert.AreEqual("sw", path[0], "звернений план — говорить не букер, а зал: " + string.Join(" → ", path));
            CollectionAssert.AreEquivalent(new[] { "turn_sw", "stay" }, Options(r));
            int trust = s.Trust, hype = s.State.NextHypeBonus;
            var res = ChooseAndFinish(s, "turn_sw");
            Assert.AreEqual(RingPath.Heel, s.CurrentRole);
            Assert.AreEqual(RingPath.Heel, res.TurnedTo);
            Assert.AreEqual(trust, s.Trust, "букер такого не планував — довіри немає");
            Assert.AreEqual(hype + 1, s.State.NextHypeBonus, "зал чекає нового тебе — ажіотаж на старті наступного матчу");

            // і справді на старті матчу: та сама сесія без цього +1 — на один ажіотаж менше
            s.EndWeek();
            var without = CareerKit.Fresh(22, C);
            without.RestoreState(s.CaptureState());
            without.State.NextHypeBonus -= 1;
            var e = NextMatch(s);
            var e0 = NextMatch(without);
            Assert.Less(e0.State.Hype, MatchRules.MaxHype, "замір не впирається в стелю");
            Assert.AreEqual(e0.State.Hype + 1, e.State.Hype, "ажіотаж повороту — на старті наступного матчу");
            CareerBot.PlayMatch(s, 0, new CareerRunReport());
            s.FinishMatch();
            Assert.AreEqual(0, s.State.NextHypeBonus, "один матч — і бонус витрачено");
        }

        [Test]
        public void TurnScene_TheHammerFeud_CountsAsAFeud_ForTheHeroine_AndInTheUpperFederations()
        {
            // героїня: Молот — у чоловічому дивізіоні, його немає в пулі суперників, але ворожнеча з ним — є (§5.3, рев'ю ПМ 12.11.2026)
            var h = CareerKit.NewAtCreate(31);
            var d = CareerKit.Draft(Gimmick.Showman); d.Female = true;
            h.CreateHero(d);
            Assert.IsTrue(h.State.Female);
            Assert.IsFalse(h.BookingPool().Any(n => n.Id == CareerContent.HammerId), "Молота в пулі героїні немає");
            foreach (var n in h.BookingPool()) h.State.SetNpcFeud(n.Id, 0);
            h.Feud.State.Heat = 3;
            Assert.AreEqual(3, h.MaxFeudLevel, "ворожнеча з Молотом — теж ворожнеча");
            h.State.FeudResolved = true;
            Assert.AreEqual(0, h.MaxFeudLevel, "розв'язана — вже ні");

            // герой у федерації 2: ворожнеча з Молотом перейшла разом із ним, і поворот приходить
            for (int tier = 2; tier <= C.Federations.Count; tier++)
            {
                var s = HeroBeforeTurn(tier, 0, (ulong)(40 + tier));
                Assert.AreEqual(0, s.MaxFeudLevel);
                Assert.AreNotEqual("ev_turn", s.Backstage().SceneId, "федерація " + tier + ": без ворожнечі — ні");
                s = HeroBeforeTurn(tier, 0, (ulong)(40 + tier));
                s.Feud.State.Heat = 3;
                Assert.AreEqual(3, s.MaxFeudLevel, "федерація " + tier);
                Assert.AreEqual("ev_turn", s.Backstage().SceneId, "федерація " + tier + ": жар Молота 3 — поворот");
            }
        }

        [Test]
        public void TurnScene_TheVillain_TurnsHero_AndTheTurnSurvivesAFreshRestore()
        {
            var s = HeroBeforeTurn(3, 4, 23, CareerRole.Villain);
            Paths(s, 2, 5, 1);
            Assert.AreEqual(RingPath.Heel, s.CurrentRole);
            s.Backstage();
            var r = s.CurrentBackstage;
            ToChoice(r);
            Assert.AreEqual("hc", r.Current.Id, "лиходій → герой");
            ChooseAndFinish(s, "turn");
            Assert.AreEqual(RingPath.Face, s.CurrentRole);
            Assert.AreEqual(5, s.Feud.State.GetPath(RingPath.Face));

            var t = CareerKit.Fresh(23, C);
            t.RestoreState(s.CaptureState());
            Assert.AreEqual(RingPath.Face, t.CurrentRole, "поворот у сейві");
            Assert.AreEqual(CareerRole.Hero, t.State.Role);
            Assert.IsTrue(t.BackstageSeenThisSeason("ev_turn"));
        }

        [Test]
        public void TurnScene_InTheMatch_TheNewRoleIsTheOneThatPlays()
        {
            var s = HeroBeforeTurn(1, 3, 24);
            s.Backstage();
            ToChoice(s.CurrentBackstage);
            ChooseAndFinish(s, "turn");
            Assert.AreEqual(RingPath.Heel, s.GetCareerView().Role, "картка героя показує нову роль");
            var e = NextMatch(s);
            Assert.AreEqual(RingPath.Heel, e.Setup.PlayerRole, "і в матчі грає нова роль");
        }

        // ---------------- завантажувач ----------------

        [Test]
        public void Loader_RoleNeedsAPath_TurnNeedsFaceOrHeel_WithoutAdd()
        {
            Func<string, Dictionary<string, object>> o = MiniJson.ParseObject;
            Assert.Throws<FormatException>(() => FeudCondition.FromJson(o("{\"career\":\"role\"}"), "t"));
            Assert.Throws<FormatException>(() => FeudCondition.FromJson(o("{\"career\":\"role\",\"target\":\"hero\"}"), "t"));
            Assert.AreEqual("heel", FeudCondition.FromJson(o("{\"career\":\"role\",\"target\":\"heel\"}"), "t").Target);
            Assert.AreEqual(3, FeudCondition.FromJson(o("{\"career\":\"max_feud\",\"min\":3}"), "t").Min);

            Assert.Throws<FormatException>(() => FeudEffect.FromJson(o("{\"career\":\"turn\"}"), "t"));
            Assert.Throws<FormatException>(() => FeudEffect.FromJson(o("{\"career\":\"turn\",\"target\":\"rebel\"}"), "t"), "бунтар — не поворот");
            Assert.Throws<FormatException>(() => FeudEffect.FromJson(o("{\"career\":\"turn\",\"target\":\"heel\",\"add\":1}"), "t"));
            Assert.AreEqual("face", FeudEffect.FromJson(o("{\"career\":\"turn\",\"target\":\"face\"}"), "t").Target);
        }
    }
}
