using System.Collections.Generic;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Core.Ring.Match;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// P2-2 (плейтест 08.11.2026): «Радимо зараз» — від ролі, ґіміка й того, чого бракувало в останньому матчі; очок багато — 3–4
    /// варіанти. Модель — <see cref="DevelopmentAdvice"/>; екран розвитку лише показує її поради.
    /// </summary>
    public class ProgressionAdviceTests
    {
        private static CareerSession With(Gimmick g, CareerRole role, int points, int face, int heel, int rebel)
        {
            var s = CareerKit.NewHero(310 + (ulong)g, g, role);
            s.Feud.State.DevPoints = points;
            s.Feud.State.AddPath(RingPath.Face, face);
            s.Feud.State.AddPath(RingPath.Heel, heel);
            s.Feud.State.AddPath(RingPath.Rebel, rebel);
            return s;
        }

        private static MoveDef MoveOf(CareerSession s, DevAdvice a) { return a.Kind == AdviceKind.Move ? s.Content.Moves.Get(a.Id) : null; }

        [Test]
        public void Hero_IsNeverAdvisedDirtyMovesOrAnotherPathsPerks_EvenWhenAffordable()
        {
            for (int g = 0; g < CareerKit.GimmickCount; g++)
            {
                // герой із рангом 3 і в лиходія, і в бунтаря — перки чужих шляхів йому по кишені, але не радяться
                var s = With((Gimmick)g, CareerRole.Hero, 40, 8, 6, 6);
                Assert.AreEqual(RingPath.Face, s.CurrentRole);
                var adv = DevelopmentAdvice.For(s, 3);
                Assert.AreEqual(DevelopmentAdvice.MaxAdvice, adv.Count, "40 очок — чотири поради");
                foreach (var a in adv)
                {
                    if (a.Kind == AdviceKind.Perk)
                    {
                        var p = PerkCatalog.Find(a.Id);
                        Assert.IsTrue(!p.Path.HasValue || p.Path.Value == RingPath.Face, "герою — перк " + a.Id);
                    }
                    var m = MoveOf(s, a);
                    if (m != null) Assert.AreNotEqual(MatchActionId.Dirty, m.Action, "герою — брудний прийом " + a.Id);
                }
                var kinds = new HashSet<AdviceKind>();
                foreach (var a in adv) kinds.Add(a.Kind);
                Assert.AreEqual(3, kinds.Count, "серед чотирьох — перк, прийом і атрибут");
            }
        }

        [Test]
        public void Villain_IsAdvisedHisOwnPerkAndADirtyMove()
        {
            var s = With(Gimmick.Showman, CareerRole.Villain, 40, 0, 7, 0);
            Assert.AreEqual(RingPath.Heel, s.CurrentRole);
            var adv = DevelopmentAdvice.For(s, 3);
            bool dirty = false, heelPerk = false;
            foreach (var a in adv)
            {
                var m = MoveOf(s, a);
                if (m != null && m.Action == MatchActionId.Dirty) { dirty = true; Assert.AreEqual(DevelopmentAdvice.RoleMoveKey(RingPath.Heel), a.WhyKey); }
                if (a.Kind == AdviceKind.Perk) { var p = PerkCatalog.Find(a.Id); heelPerk |= p.Path == RingPath.Heel; Assert.AreNotEqual(RingPath.Face, p.Path); }
            }
            Assert.IsTrue(dirty, "лиходієві-шоумену — брудний прийом");
            Assert.IsTrue(heelPerk, "лиходієві — перк лиходія");
        }

        [Test]
        public void GimmickMove_GoesBeforeAGeneralOne()
        {
            var s = With(Gimmick.Flyer, CareerRole.Hero, 40, 0, 0, 0);
            string gimmick = s.Content.GimmickOf(Gimmick.Flyer).Key;
            // ґімік-прийоми зараз даються на старті; невивчений (старий сейв, майбутні ґіміки 5–8) радиться раніше загального
            var own = s.Content.GimmickOf(Gimmick.Flyer).GimmickMoves[0];
            Assert.IsTrue(s.State.Moves.Remove(own));
            DevAdvice firstMove = null;
            foreach (var a in DevelopmentAdvice.For(s, 3)) if (a.Kind == AdviceKind.Move) { firstMove = a; break; }
            Assert.IsNotNull(firstMove);
            Assert.AreEqual(gimmick, s.Content.Moves.Get(firstMove.Id).GimmickId);
            Assert.AreEqual("c.dev.why.gimmick", firstMove.WhyKey);
        }

        [Test]
        public void Advice_FollowsWhatTheLastMatchLacked()
        {
            // перемога з високим ажіотажем — жодної поради «через минулий матч»
            var s = With(Gimmick.Monster, CareerRole.Hero, 4, 2, 0, 0);
            s.State.Last = new ShowResult { Played = true, Winner = MatchWinner.Player, EndKind = MatchEndKind.Pin, Hype = 9 };
            foreach (var a in DevelopmentAdvice.For(s, 3))
                Assert.IsFalse(a.WhyKey != null && (a.WhyKey.StartsWith("c.dev.why.lost") || a.WhyKey == "c.dev.why.low_hype"), a.WhyKey);

            s.State.Last = new ShowResult { Played = true, Winner = MatchWinner.Rival, EndKind = MatchEndKind.Pin, Hype = 9 };
            var pin = DevelopmentAdvice.For(s, 3);
            Assert.AreEqual(DevelopmentAdvice.WhyFor("lost_pin", pin[0].Kind), pin[0].WhyKey, "поразка утриманням — найперша порада про це");
            Assert.IsTrue(pin.Exists(a => a.Kind == AdviceKind.Attr && a.Attr == RingAttr.Stamina), "Витривалість — вирватися з утримання");

            s.State.Last = new ShowResult { Played = true, Winner = MatchWinner.Rival, EndKind = MatchEndKind.Submission, Hype = 9 };
            Assert.IsTrue(DevelopmentAdvice.For(s, 3).Exists(a => a.WhyKey == DevelopmentAdvice.WhyFor("lost_sub", a.Kind)));

            s.State.Last = new ShowResult { Played = true, Winner = MatchWinner.Player, EndKind = MatchEndKind.Pin, Hype = 4 };
            var low = DevelopmentAdvice.For(s, 3);
            Assert.IsTrue(low.Exists(a => a.WhyKey == DevelopmentAdvice.WhyFor("low_hype", a.Kind) && (a.Kind == AdviceKind.Attr ? a.Attr == RingAttr.Charisma : a.Id == PerkCatalog.CrowdFavorite)));

            s.State.Last = null;
            s.State.Injury[1] = 2;
            var hurt = DevelopmentAdvice.For(s, 3);
            Assert.AreEqual(PerkCatalog.IronJaw, hurt[0].Id, "травма — спершу «Залізна щелепа»");
            Assert.AreEqual("c.dev.why.injured", hurt[0].WhyKey);
        }

        /// <summary>
        /// Кадри «Меню» (слот 28, рев'ю ПМ 12.11.2026): «Друге дихання» і «Витривалість 0 → +1» поруч з одним поясненням «вчися
        /// вириватися». У видачі двох порад з однаковим поясненням немає — у жодній ситуації.
        /// </summary>
        [Test]
        public void NoTwoAdvice_ShareAnExplanation()
        {
            // сама скарга: поразка утриманням, герой з очками — і перк, і Витривалість у видачі, пояснення різні
            var s = With(Gimmick.Monster, CareerRole.Hero, 40, 2, 0, 0);
            s.Player.Set(RingAttr.Stamina, 0);
            s.State.Last = new ShowResult { Played = true, Winner = MatchWinner.Rival, EndKind = MatchEndKind.Pin, Hype = 9 };
            var pin = DevelopmentAdvice.For(s, 3);
            var perk = pin.Find(a => a.Kind == AdviceKind.Perk && a.Id == PerkCatalog.SecondWind);
            var sta = pin.Find(a => a.Kind == AdviceKind.Attr && a.Attr == RingAttr.Stamina);
            Assert.IsNotNull(perk, "«Друге дихання» у видачі");
            Assert.IsNotNull(sta, "Витривалість у видачі");
            Assert.AreEqual("c.dev.why.lost_pin.perk", perk.WhyKey);
            Assert.AreEqual("c.dev.why.lost_pin", sta.WhyKey);

            int checkedLists = 0, withWhy = 0;
            var ends = new[] { MatchEndKind.Pin, MatchEndKind.Submission };
            for (int g = 0; g < CareerKit.GimmickCount; g++)
                for (int r = 0; r < 2; r++)
                    foreach (int points in new[] { 2, 4, 8, 40 })
                        for (int last = 0; last < 6; last++)
                            for (int hurt = 0; hurt < 2; hurt++)
                            {
                                var t = With((Gimmick)g, (CareerRole)r, points, r == 0 ? 6 : 0, r == 1 ? 6 : 0, last % 3 == 2 ? 6 : 0);
                                t.State.Last = last == 5 ? null : new ShowResult
                                {
                                    Played = true, Winner = last < 2 ? MatchWinner.Rival : MatchWinner.Player, EndKind = ends[last % 2],
                                    Hype = last >= 3 ? 3 : 9
                                };
                                if (hurt == 1) t.State.Injury[1] = 2;
                                // ґімік-прийом невивчений — два прийоми ґіміка в одній видачі (одне пояснення «прийом твого ґіміка»)
                                if (g % 2 == 0) t.State.Moves.RemoveAll(id => t.Content.Moves.Get(id).GimmickId != null);
                                var said = new HashSet<string>();
                                foreach (var a in DevelopmentAdvice.For(t, 3))
                                {
                                    if (a.WhyKey == null) continue;
                                    withWhy++;
                                    Assert.IsTrue(said.Add(a.WhyKey), "двічі «" + a.WhyKey + "»: ґімік " + g + ", роль " + r + ", очок " + points + ", матч " + last);
                                }
                                checkedLists++;
                            }
            Assert.Greater(withWhy, checkedLists, "пояснень вистачає — перевірка не порожня");
        }

        [Test]
        public void FewPoints_FewerAdvice_AndOnlyAffordable_NoPointsNone()
        {
            var s = With(Gimmick.Technician, CareerRole.Hero, 2, 0, 0, 0);
            var adv = DevelopmentAdvice.For(s, 3);
            Assert.LessOrEqual(adv.Count, 3);
            Assert.Greater(adv.Count, 0);
            foreach (var a in adv) Assert.LessOrEqual(a.Cost, 2, a.Id ?? a.Attr.ToString());
            Assert.AreEqual(2, DevelopmentAdvice.For(s, 2).Count, "кінець сезону — дві");

            s.Feud.State.DevPoints = 0;
            Assert.AreEqual(0, DevelopmentAdvice.For(s, 3).Count);
        }

        [Test]
        public void Advice_IsReadOnly_AndEveryWhyKeyIsTranslated()
        {
            var keys = MiniJson.ParseObject(CareerKit.Read("ui_uk.json"));
            var why = new HashSet<string> { "c.dev.why.gimmick", "c.dev.why.injured" };
            foreach (var r in new[] { "lost_pin", "lost_sub", "low_hype" })
            {
                why.Add(DevelopmentAdvice.WhyFor(r, AdviceKind.Perk));
                why.Add(DevelopmentAdvice.WhyFor(r, AdviceKind.Attr));
            }
            why.Add(DevelopmentAdvice.WhyFor("lost_sub", AdviceKind.Move));
            why.Add(DevelopmentAdvice.RoleMoveKey(RingPath.Heel));
            for (int i = 0; i < 3; i++) why.Add(DevelopmentAdvice.RoleKey((RingPath)i));
            for (int g = 0; g < CareerKit.GimmickCount; g++)
                for (int r = 0; r < 2; r++)
                {
                    var s = With((Gimmick)g, (CareerRole)r, 40, r == 0 ? 6 : 0, r == 1 ? 6 : 0, 0);
                    s.State.Last = new ShowResult { Played = true, Winner = MatchWinner.Rival, EndKind = (MatchEndKind)(g % 2), Hype = 3 };
                    string before = s.CaptureState();
                    foreach (var a in DevelopmentAdvice.For(s, 3)) if (a.WhyKey != null) why.Add(a.WhyKey);
                    Assert.AreEqual(before, s.CaptureState(), "поради нічого не купують");
                }
            foreach (var k in why) Assert.IsTrue(keys.ContainsKey(k), "немає перекладу " + k);
        }
    }
}
