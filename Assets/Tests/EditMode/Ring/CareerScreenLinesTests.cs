using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Core.Ring.Match;
using Game.Gameplay.Ring.Models;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Показ ядра «Кар'єри» на екранах зони «Меню» (запит ПМ 11.11.2026): чому поразка не дала популярності (P1-6), підсумок
    /// сезону лише остаточний (3-P2-6), «Радимо зараз» — поради ядра з поясненням-ключем (P2-2). Рядки — <see cref="CareerScreenLines"/>;
    /// екрани лише кладуть їх у наявні складники, що й перевіряє скан джерел наприкінці.
    /// </summary>
    public class CareerScreenLinesTests
    {
        private static RingText _t;
        private static RingText T { get { return _t ?? (_t = RingText.FromJson(CareerKit.Read("strings_uk.json"), CareerKit.Read("ui_uk.json"))); } }

        private static void NoMissingKey(string s, string what)
        {
            Assert.IsNotNull(s, what);
            StringAssert.DoesNotContain("⟦", s, what + " — ключа немає в рядках");
        }

        [Test]
        public void LossPopularity_SaysWhy_OrNothing()
        {
            string hype = CareerScreenLines.LossPopularity(T, new ShowResult { LossPopularityBlocked = 1, Hype = 6 });
            NoMissingKey(hype, "ажіотаж нижче порога");
            Assert.AreEqual(T.Format("c.result.loss_pop.hype", CareerRules.LossPopularityHype, 6), hype);
            StringAssert.Contains(CareerRules.LossPopularityHype.ToString(), hype, "поріг ажіотажу в рядку");
            StringAssert.Contains("(був 6)", hype, "ажіотаж цього вечора в рядку");

            string season = CareerScreenLines.LossPopularity(T, new ShowResult { LossPopularityBlocked = 2, Hype = 12 });
            NoMissingKey(season, "вже цього сезону");
            Assert.AreEqual(T.Get("c.result.loss_pop.season"), season);

            Assert.IsNull(CareerScreenLines.LossPopularity(T, new ShowResult { LossPopularityBlocked = 0, Hype = 3 }), "0 — рядка немає");
            Assert.IsNull(CareerScreenLines.LossPopularity(T, null));
        }

        [Test]
        public void SeasonSummary_OnlyAfterTheTitleChance_IsClosed()
        {
            var s = CareerKit.NewHero(4);
            var rep = new CareerRunReport();
            int guard = 0;
            while (!(s.Stage == CareerStage.Show && s.State.Phase == ShowPhase.Result && s.State.Week == CareerRules.WeeksPerSeason) && guard++ < 1000)
            {
                if (s.Stage == CareerStage.SeasonEnd) Assert.Fail("сезон закінчився раніше");
                CareerBot.Step(s, 0, rep);
            }
            s.Feud.State.Trust = CareerRules.MaxTrust;
            s.ContinueFromShow();
            Assert.AreEqual(CareerStage.SeasonEnd, s.Stage);
            Assert.IsTrue(s.CanStartTitleMatch);
            Assert.IsFalse(CareerScreenLines.SeasonSummaryShown(s), "титульний шанс відкритий — на папері лише він");
            var r = s.GetSeasonReport();
            NoMissingKey(T.Get(r.IsDefense ? "season.defense" : "season.title_shot"), "рядок титульного шансу");
            s.SkipTitle();
            Assert.IsTrue(CareerScreenLines.SeasonSummaryShown(s), "після відмови — повний підсумок");
        }

        [Test]
        public void DevRecs_FollowTheCoreAdvice_WhyFromItsKey_ElseTheOldExplanation()
        {
            // який вид поради бачили з ключем «чому» і без нього — кожна гілка пояснення має пройти перевірку
            var sawKey = new HashSet<AdviceKind>();
            var sawFallback = new HashSet<AdviceKind>();
            foreach (var role in new[] { CareerRole.Hero, CareerRole.Villain })
                for (int g = 0; g < 4; g++)
                    foreach (int points in new[] { 3, 8, 40 })
                        foreach (int max in new[] { 2, 3 })
                            foreach (int path in new[] { 0, 7 })
                                for (int last = 0; last < 5; last++)
                        {
                            var s = CareerKit.NewHero(500 + (ulong)g, (Gimmick)g, role);
                            s.Feud.State.DevPoints = points;
                            s.Feud.State.AddPath(role == CareerRole.Hero ? RingPath.Face : RingPath.Heel, path);   // ранг свого шляху — перки шляху
                            // минулий матч: не було / поразка утриманням / поразка больовим зі слабким ажіотажем / перемога без
                            // ажіотажу / травма
                            if (last == 1) s.State.Last = new ShowResult { Played = true, Winner = MatchWinner.Rival, EndKind = MatchEndKind.Pin, Hype = 9 };
                            if (last == 2) s.State.Last = new ShowResult { Played = true, Winner = MatchWinner.Rival, EndKind = MatchEndKind.Submission, Hype = 4 };
                            if (last == 3) s.State.Last = new ShowResult { Played = true, Winner = MatchWinner.Player, EndKind = MatchEndKind.Pin, Hype = 4 };
                            if (last == 4) s.State.Injury[1] = 2;
                            string at = role + "/" + (Gimmick)g + "/" + points + "/" + max + "/шлях " + path + "/матч " + last;
                            var adv = DevelopmentAdvice.For(s, max);
                            var lines = CareerScreenLines.DevRecs(T, s, max);
                            Assert.AreEqual(adv.Count, lines.Count, at + ": стільки ж порад, скільки дає ядро");
                            var keys = new HashSet<string>();
                            for (int i = 0; i < lines.Count; i++)
                            {
                                var a = adv[i];
                                var l = lines[i];
                                Assert.AreEqual(a.Kind, l.Advice.Kind, at + ": порядок ядра");
                                Assert.AreEqual(a.Id, l.Advice.Id, at);
                                NoMissingKey(l.Title, at + " назва");
                                NoMissingKey(l.Why, at + " пояснення");
                                StringAssert.EndsWith(" · " + a.Cost, l.Title, at + ": ціна на кнопці");
                                Assert.IsTrue(l.PadKey.StartsWith("rec:"), at + ": сталий ключ фокуса " + l.PadKey);
                                Assert.IsTrue(keys.Add(l.PadKey), at + ": ключ фокуса повторюється " + l.PadKey);
                                string fallback;
                                switch (a.Kind)
                                {
                                    case AdviceKind.Perk:
                                        fallback = RingLabels.PerkDesc(T, a.Id);
                                        Assert.AreEqual(PerkCatalog.Find(a.Id).Path, l.Path, at + ": шлях перка — колір смужки");
                                        break;
                                    case AdviceKind.Move:
                                        fallback = T.Format("c.dev.move.why", RingLabels.ActionName(T, s.Content.Moves.Get(a.Id).Action));
                                        break;
                                    default:
                                        fallback = T.Get("c.dev.attr.why." + (int)a.Attr);
                                        int v = s.Player.Get(a.Attr);
                                        StringAssert.StartsWith(RingLabels.AttrName(T, a.Attr) + " " + RingChances.Signed(v) + " → " + RingChances.Signed(v + 1), l.Title, at);
                                        break;
                                }
                                if (a.WhyKey != null)
                                {
                                    sawKey.Add(a.Kind);
                                    Assert.AreEqual(T.Get(a.WhyKey), l.Why, at + ": пояснення — ключ поради");
                                    Assert.AreNotEqual(fallback, l.Why, at + ": ключ поради не збігається з описом — інакше гілку не видно");
                                }
                                else { sawFallback.Add(a.Kind); Assert.AreEqual(fallback, l.Why, at + ": без ключа — як раніше"); }
                            }
                        }
            foreach (AdviceKind k in new[] { AdviceKind.Perk, AdviceKind.Move, AdviceKind.Attr })
            {
                Assert.IsTrue(sawKey.Contains(k), k + ": жодної поради з ключем «чому» — гілку не перевірено");
                Assert.IsTrue(sawFallback.Contains(k), k + ": жодної поради без ключа — запасну гілку не перевірено");
            }
        }

        /// <summary>Кожен ключ «чому» ядра (c.dev.why.*, ролі 0–2) і запасні пояснення є в ui_uk.json — на кнопці не буде «⟦…⟧».</summary>
        [Test]
        public void EveryWhyKey_OfTheCore_IsInTheStrings()
        {
            string src = File.ReadAllText(Path.Combine(Application.dataPath, "_Project", "Scripts", "Core", "Ring", "Progression", "DevelopmentAdvice.cs"));
            var keys = new List<string>();
            foreach (Match m in Regex.Matches(src, "\"(c\\.dev\\.why\\.[a-z_]+)\"")) keys.Add(m.Groups[1].Value);
            Assert.GreaterOrEqual(keys.Count, 5, "скан ключів ядра нічого не знайшов");
            for (int i = 0; i < 3; i++) keys.Add(DevelopmentAdvice.RoleKey((RingPath)i));
            for (int i = 0; i < 5; i++) keys.Add("c.dev.attr.why." + i);
            keys.Add("c.dev.move.why");
            foreach (var k in keys) NoMissingKey(T.Get(k), k);
        }

        /// <summary>Рядок коду (не коментар), що починається з <paramref name="code"/>.</summary>
        private static bool CodeLine(string src, string code)
        {
            foreach (var l in src.Split('\n')) if (l.Trim().StartsWith(code)) return true;
            return false;
        }

        /// <summary>Екрани беруть рядки з <see cref="CareerScreenLines"/>, а не рахують по-своєму (старий вибір порад, «Акт 0»).</summary>
        [Test]
        public void Sources_ScreensUseTheLines()
        {
            string ui = Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Ring", "UI");
            string show = File.ReadAllText(Path.Combine(ui, "Career", "ShowScreens.cs"));
            string career = File.ReadAllText(Path.Combine(ui, "CareerScreens.cs"));
            string promo = File.ReadAllText(Path.Combine(ui, "PromoScreenView.cs"));
            var bad = new List<string>();
            if (!show.Contains("CareerScreenLines.LossPopularity(T, r)") || !CodeLine(show, "if (lossPop != null) col.Add(ShowKit.Bullet(lossPop));"))
                bad.Add("ShowScreens: підсумок вечора без рядка поразки");
            int gate = show.IndexOf("if (!CareerScreenLines.SeasonSummaryShown(C))");
            int record = show.IndexOf("\"c.season.record\"");
            if (gate < 0 || record < 0 || gate > record) bad.Add("ShowScreens: підсумок сезону не чекає кінця титульного шансу");
            if (!career.Contains("CareerScreenLines.DevRecs(T, C, maxRecs)")) bad.Add("CareerScreens: «Радимо зараз» не з порад ядра");
            if (career.Contains("bestAttr") || career.Contains("perkCost") || career.Contains("bestMove")) bad.Add("CareerScreens: лишився старий вибір порад");
            if (!promo.Contains("_m.ActText")) bad.Add("PromoScreenView: шапка не з ActText");
            if (promo.Contains("\"promo.act\"")) bad.Add("PromoScreenView: «Акт N» збирається повз модель");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }
    }
}
