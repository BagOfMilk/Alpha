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
            // причини минулого матчу — через WhyFor(причина, вид поради): вид — той, яким порада створена в цьому циклі ядра
            // (Kind = AdviceKind.X перед викликом); «Кар'єра», 12.11.2026: атрибут — основа, перк — .perk, прийом — .move
            int reasons = 0;
            foreach (Match m in Regex.Matches(src, @"WhyFor\(([^;]*?), a\.Kind\)"))
            {
                var kinds = Regex.Matches(src.Substring(0, m.Index), @"Kind = AdviceKind\.(\w+)");
                Assert.Greater(kinds.Count, 0, "вид поради перед " + m.Value + " не знайдено");
                var kind = (AdviceKind)System.Enum.Parse(typeof(AdviceKind), kinds[kinds.Count - 1].Groups[1].Value);
                foreach (Match r in Regex.Matches(m.Groups[1].Value, "\"([a-z_]+)\""))
                {
                    keys.Add(DevelopmentAdvice.WhyFor(r.Groups[1].Value, kind));
                    reasons++;
                }
            }
            Assert.GreaterOrEqual(keys.Count, 7, "скан ключів ядра нічого не знайшов");
            Assert.GreaterOrEqual(reasons, 5, "причини минулого матчу (WhyFor) не знайдено");
            keys.Add(DevelopmentAdvice.RoleMoveKey(RingPath.Heel));   // брудний прийом лиходієві — єдиний прийом ролі в ядрі
            for (int i = 0; i < 3; i++) keys.Add(DevelopmentAdvice.RoleKey((RingPath)i));
            for (int i = 0; i < 5; i++) keys.Add("c.dev.attr.why." + i);
            keys.Add("c.dev.move.why");
            foreach (var k in keys) NoMissingKey(T.Get(k), k);
        }

        // ---------------- федерації (П5, запит ПМ 12.11.2026) ----------------

        /// <summary>
        /// «Пропустити титульний матч» — з наслідком (ПМ 12.11.2026, кадр слоту 28: порожня плитка): що буде — завжди; чемпіонові —
        /// ризиком довіра −1, і саме стільки забирає ядро.
        /// </summary>
        [Test]
        public void SkipTitle_Tile_SaysWhatHappens_AndTheChampionLosesTrust()
        {
            Assert.AreEqual(T.Get("c.season.title_skip.effect"), CareerScreenLines.SkipTitleEffect(T));
            var s = AtSeasonEnd(3, 1, 3, CareerRules.OfferTrust);
            s.State.Report.TitleEligible = true; s.State.Report.TitleDone = false;
            Assert.IsNull(CareerScreenLines.SkipTitleRisk(T, s), "не чемпіон — довіри не втрачає");
            int trust = s.Trust;
            s.SkipTitle();
            Assert.AreEqual(trust, s.Trust);

            var c = AtSeasonEnd(4, 1, 3, CareerRules.OfferTrust, true);
            c.State.Report.TitleEligible = true; c.State.Report.TitleDone = false;
            Assert.IsTrue(c.IsChampion);
            Assert.AreEqual(T.Get("c.season.title_skip.champion"), CareerScreenLines.SkipTitleRisk(T, c));
            StringAssert.Contains("−1", CareerScreenLines.SkipTitleRisk(T, c));
            trust = c.Trust;
            c.SkipTitle();
            Assert.AreEqual(trust - 1, c.Trust, "текст ризику — те саме правило, що в ядрі");
            Assert.IsNull(CareerScreenLines.SkipTitleRisk(T, c), "після рішення ризику вже немає");
        }

        /// <summary>«Очок розвитку: N» — лише там, де шапка числа не має (ПМ 12.11.2026, 4:3: «ОЧКІВ 23» і той самий рядок під ним).</summary>
        [Test]
        public void DevPointsLine_OnlyWhereTheHeaderHasNoCount()
        {
            var s = CareerKit.NewHero(5);
            s.Feud.State.DevPoints = 23;
            Assert.IsNull(CareerScreenLines.DevPointsLine(T, s, true));
            Assert.AreEqual(T.Format("dev.points", 23), CareerScreenLines.DevPointsLine(T, s, false));
        }

        /// <summary>Смуга провалу 2d6 читається однаково в описі дії й легенді шансів: «≤6» (ПМ 12.11.2026: опис промо казав «6−»).</summary>
        [Test]
        public void MissBand_ReadsLikeTheLegend_Everywhere()
        {
            StringAssert.StartsWith("≤6 ", T.Get("c.odds.miss"));
            StringAssert.Contains("≤6 — ", T.Get("week.action.2.desc"));
            var other = new Regex(@"(?<![\d≤])6\s?[−–-](?=\s|$)");
            foreach (var file in new[] { "ui_uk.json", "strings_uk.json" })
                foreach (var line in CareerKit.Read(file).Split('\n'))
                    Assert.IsFalse(other.IsMatch(line), file + ": смуга провалу не «≤6» — " + line.Trim());
        }

        /// <summary>Як CareerTransitionTests.CloseSeason: кінець сезону у федерації з популярністю, довірою й поясом; титул розіграно.</summary>
        private static CareerSession AtSeasonEnd(ulong seed, int tier, int pop, int trust, bool champion = false)
        {
            var s = CareerKit.NewHero(seed);
            var run = new CareerRunReport();
            int guard = 0;
            while (!(s.Stage == CareerStage.Show && s.State.Phase == ShowPhase.Result && s.State.Week == CareerRules.WeeksInSeason(s.State.Season)))
            {
                Assert.Less(++guard, 3000);
                CareerBot.Step(s, 0, run);
            }
            if (tier != s.FederationTier) s.EnterFederation(tier, pop, trust);
            s.Feud.State.Audience = pop; s.Feud.State.Trust = trust;
            s.State.BeltHolder = champion ? CareerState.PlayerHolder : s.Federation.BeltOf(s.State.Female);
            s.ContinueFromShow();
            Assert.AreEqual(CareerStage.SeasonEnd, s.Stage);
            s.Feud.State.Audience = pop; s.Feud.State.Trust = trust;
            s.State.Report.TitleDone = true;
            return s;
        }

        private static string Fed(CareerSession s, int tier) { return T.Get(s.Content.Federation(tier).NameKey); }

        /// <summary>
        /// Нижній ряд кінця сезону — одна послідовність рішень: титульний шанс → пропозиція («Прийняти контракт» / «Лишитися») →
        /// «Новий сезон». Без кнопок пропозиції гравець застрягав на місцевій під стелею 4 (ПМ 12.11.2026).
        /// </summary>
        [Test]
        public void SeasonEndActions_TitleThenOffer_ThenNextSeason()
        {
            var s = AtSeasonEnd(3, 1, 3, CareerRules.OfferTrust);
            s.State.Report.TitleEligible = true; s.State.Report.TitleDone = false;
            CollectionAssert.AreEqual(new[] { SeasonEndAction.TitleMatch, SeasonEndAction.SkipTitle }, CareerScreenLines.SeasonEndActions(s),
                "титульний шанс — першим, пропозиція чекає");
            Assert.IsFalse(CareerScreenLines.OfferShown(s, new SeasonOfferModel(s, T)), "до кінця титульного шансу пропозиції на папері немає");
            s.SkipTitle();
            CollectionAssert.AreEqual(new[] { SeasonEndAction.AcceptOffer, SeasonEndAction.DeclineOffer }, CareerScreenLines.SeasonEndActions(s),
                "обидва ключі — рішення, «Новий сезон» лише після нього");
            var o = new SeasonOfferModel(s, T);
            Assert.IsTrue(CareerScreenLines.OfferShown(s, o));
            Assert.IsTrue(o.ShowKeys);

            // «Прийняти контракт» не-чемпіона: перехід одразу, лишається кінець сезону — далі лише «Новий сезон», ключів немає
            s.AcceptOffer();
            Assert.AreEqual(CareerStage.SeasonEnd, s.Stage);
            Assert.AreEqual(2, s.FederationTier);
            CollectionAssert.AreEqual(new[] { SeasonEndAction.NextSeason }, CareerScreenLines.SeasonEndActions(s));
            o = new SeasonOfferModel(s, T);
            Assert.IsTrue(CareerScreenLines.OfferShown(s, o), "після рішення — рядок стану");
            Assert.IsFalse(o.ShowKeys, "ключі нової федерації не показуються одразу після переходу");
            Assert.AreEqual(T.Format("c.offer.moved", Fed(s, 2)), o.StatusText);
            Assert.AreEqual(s.PopularityCeiling, CareerScreenLines.PopularityMax(s), "шкала популярності — до стелі нової федерації");

            // «Лишитися»: перша відмова — «повториться», друга поспіль — ризик, довіра −1
            var d = AtSeasonEnd(4, 1, 3, CareerRules.OfferTrust);
            Assert.IsFalse(CareerScreenLines.DeclineOffends(d));
            Assert.AreEqual(T.Get("c.offer.decline.again"), CareerScreenLines.DeclineEffect(T));
            Assert.IsNull(CareerScreenLines.DeclineRisk(T, d), "перша відмова — без ризику");
            d.DeclineOffer();
            CollectionAssert.AreEqual(new[] { SeasonEndAction.NextSeason }, CareerScreenLines.SeasonEndActions(d));
            Assert.AreEqual(T.Get("c.offer.declined"), new SeasonOfferModel(d, T).StatusText);
            d.State.OfferDecision = 0;   // наступний сезон з тими самими ключами — друга відмова поспіль
            Assert.IsTrue(CareerScreenLines.DeclineOffends(d));
            Assert.AreEqual(T.Get("c.offer.decline.offend"), CareerScreenLines.DeclineRisk(T, d));
            int trust = d.Trust;
            d.DeclineOffer();
            Assert.AreEqual(trust - 1, d.Trust, "ризик плитки — те, що робить ядро");
            // і третя поспіль — теж ризик (серію обриває лише сезон без явної відмови): довіру знову піднято до ключа
            d.State.OfferDecision = 0; d.Feud.State.Trust = CareerRules.OfferTrust;
            Assert.IsTrue(d.OfferAvailable);
            Assert.AreEqual(T.Get("c.offer.decline.offend"), CareerScreenLines.DeclineRisk(T, d));
            d.DeclineOffer();
            Assert.AreEqual(CareerRules.OfferTrust - 1, d.Trust, "третя поспіль теж коштує довіри");

            // без ключів — одразу «Новий сезон»
            var n = AtSeasonEnd(5, 1, 1, 1);
            CollectionAssert.AreEqual(new[] { SeasonEndAction.NextSeason }, CareerScreenLines.SeasonEndActions(n));
            Assert.IsTrue(CareerScreenLines.OfferShown(n, new SeasonOfferModel(n, T)), "ключі видно й без пропозиції: чого бракує");

            foreach (var k in new[] { "c.offer.decline.again", "c.offer.decline.offend", "c.offer.accept", "c.offer.decline" }) NoMissingKey(T.Get(k), k);
        }

        /// <summary>
        /// Прощальний матч чемпіона: «Прийняти контракт» веде на картку шоу (ряду кінця сезону немає), картка — «Прощальний матч» з
        /// рядком про вищу федерацію й довіру <see cref="CareerRules.FarewellTrust"/>; звичайна картка — без нього.
        /// </summary>
        [Test]
        public void Farewell_Card_HasItsHeading_AndTheNote()
        {
            var s = AtSeasonEnd(7, 1, 3, CareerRules.OfferTrust - CareerRules.OfferChampionTrustBonus, champion: true);
            CollectionAssert.AreEqual(new[] { SeasonEndAction.AcceptOffer, SeasonEndAction.DeclineOffer }, CareerScreenLines.SeasonEndActions(s));
            Assert.AreEqual(T.Get("c.offer.farewell"), new SeasonOfferModel(s, T).StatusText, "чемпіона попереджено про прощальний матч");
            s.AcceptOffer();
            Assert.AreEqual(CareerStage.Show, s.Stage);
            Assert.IsEmpty(CareerScreenLines.SeasonEndActions(s), "на картці шоу ряду кінця сезону немає");
            var card = s.GetShowCard();
            Assert.IsTrue(card.IsFarewell);
            Assert.AreEqual(T.Get("c.card.farewell"), CareerScreenLines.CardHeading(T, card));
            string note = CareerScreenLines.FarewellNote(T, s, card);
            NoMissingKey(note, "рядок прощального матчу");
            Assert.AreEqual(T.Format("c.card.farewell.note", Fed(s, 2), CareerRules.FarewellTrust), note);

            var w = CareerKit.NewHero(8);
            int guard = 0;
            while (w.Stage != CareerStage.Show || w.State.Phase != ShowPhase.Card) { Assert.Less(++guard, 500); CareerBot.Step(w, 0, new CareerRunReport()); }
            var weekly = w.GetShowCard();
            Assert.IsFalse(weekly.IsFarewell);
            Assert.IsNull(CareerScreenLines.FarewellNote(T, w, weekly));
            Assert.AreEqual(T.Get(weekly.IsTitle ? "c.card.title_match" : weekly.IsBig ? "c.card.big" : "c.card.weekly"), CareerScreenLines.CardHeading(T, weekly));
        }

        /// <summary>Звільнення: на місцевій — «назад на мінімум», у вищій — пониження «куди» (c.season.demoted через модель); без нього — нічого.</summary>
        [Test]
        public void FiredLine_LocalReturnsToMinimum_HigherSaysWhere()
        {
            var up = AtSeasonEnd(5, 3, 3, 3);
            Assert.IsTrue(up.State.Report.Fired);
            var o = new SeasonOfferModel(up, T);
            Assert.AreEqual(T.Format("c.season.demoted", Fed(up, 3), Fed(up, 2)), CareerScreenLines.FiredLine(T, up, o));
            Assert.IsFalse(CareerScreenLines.OfferShown(up, o), "звільненому — рядок звільнення, а не пропозиція");
            CollectionAssert.AreEqual(new[] { SeasonEndAction.NextSeason }, CareerScreenLines.SeasonEndActions(up));

            var local = AtSeasonEnd(6, 1, 0, 1);
            Assert.IsTrue(local.State.Report.Fired, "на місцевій звільняють на нулі");
            Assert.AreEqual(T.Get("season.fired"), CareerScreenLines.FiredLine(T, local, new SeasonOfferModel(local, T)));

            var ok = AtSeasonEnd(7, 1, 2, 2);
            Assert.IsNull(CareerScreenLines.FiredLine(T, ok, new SeasonOfferModel(ok, T)));
        }

        /// <summary>
        /// Ціни з федерації (ПМ 12.11.2026, вада: на кнопці костюма стояла стала 60, а списувалась ціна федерації): кнопка костюма —
        /// <see cref="CareerSession.CostumePrice"/>, рядок житла й дороги — <see cref="CareerSession.WeeklyCost"/>; шкала популярності
        /// — до стелі федерації, не обрізаючи старої вищої популярності.
        /// </summary>
        [Test]
        public void Prices_AndPopularityScale_FollowTheFederation()
        {
            var s = CareerKit.NewHero(11);
            Assert.AreEqual(CareerStage.Week, s.Stage);
            Assert.AreEqual(T.Format("c.shop.costume", s.CostumePrice), CareerScreenLines.CostumeButton(T, s));
            Assert.AreEqual(T.Format("c.shop.living", s.WeeklyCost), CareerScreenLines.LivingLine(T, s));
            Assert.AreEqual(s.PopularityCeiling, CareerScreenLines.PopularityMax(s));
            Assert.Less(s.PopularityCeiling, CareerRules.MaxPopularity, "місцева — стеля нижче 10");

            s.EnterFederation(2, 3, 2);
            Assert.AreNotEqual(CareerRules.CostumeCost, s.CostumePrice, "регіональний костюм дорожчий за сталу");
            StringAssert.Contains(s.CostumePrice.ToString(), CareerScreenLines.CostumeButton(T, s));
            StringAssert.DoesNotContain(CareerRules.CostumeCost.ToString(), CareerScreenLines.CostumeButton(T, s));
            StringAssert.Contains(s.WeeklyCost.ToString(), CareerScreenLines.LivingLine(T, s));
            Assert.AreEqual(s.Content.Federation(2).Ceiling, CareerScreenLines.PopularityMax(s));

            s.State.Money = s.CostumePrice;
            Assert.IsTrue(s.BuyCostume());
            Assert.AreEqual(T.Get("c.shop.costume.owned"), CareerScreenLines.CostumeButton(T, s));

            s.Feud.State.Audience = s.PopularityCeiling + 2;   // старий сейв: популярність вища за стелю — шкала її не обрізає
            Assert.AreEqual(s.Popularity, CareerScreenLines.PopularityMax(s));
            foreach (var k in new[] { "c.shop.costume", "c.shop.living", "c.shop.costume.owned" }) NoMissingKey(T.Get(k), k);
        }

        /// <summary>Створення: дивізіон зі статі тіла — body_f_* жіночий, решта чоловічий (P2-6, ПМ 12.11.2026).</summary>
        [Test]
        public void SetDivision_FromTheBody()
        {
            int f = 0, m = 0;
            foreach (var body in Game.Gameplay.Ring.CareerLooks.HeroBodies)
            {
                var d = CareerKit.Draft();
                Assert.IsNull(d.Female);
                CareerScreenLines.SetDivision(d, body);
                Assert.AreEqual(body.StartsWith("body_f_"), d.Female.Value, body);
                if (d.Female.Value) f++; else m++;
            }
            Assert.Greater(f, 0, "жіночі тіла є"); Assert.Greater(m, 0, "чоловічі тіла є");
            var s = CareerKit.NewAtCreate(12);
            var dr = CareerKit.Draft();
            CareerScreenLines.SetDivision(dr, "body_f_latin");
            s.CreateHero(dr);
            Assert.IsTrue(s.State.Female); Assert.IsTrue(s.State.FemaleKnown, "екран створення знав стать — без пізнього SetFemale");
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
            // федерації (ПМ 12.11.2026): ряд кінця сезону, звільнення, пропозиція, картка прощання, шкала популярності, ціни, дивізіон
            string week = File.ReadAllText(Path.Combine(ui, "Career", "WeekScreenView.cs"));
            string create = File.ReadAllText(Path.Combine(ui, "Career", "CreateScreenView.cs"));
            // ряд будує саме Refresh (той самий виклик моделі є й в OnConfirm — сам по собі він ряду не доводить; рев'ю 12.11.2026)
            int seasonEnd = show.IndexOf("class SeasonEndScreenView"), refresh = show.IndexOf("public override void Refresh()", seasonEnd < 0 ? 0 : seasonEnd);
            int rowLoop = show.IndexOf("for (int i = 0; i < actions.Count; i++) AddAction(actions[i], i == 0);", refresh < 0 ? 0 : refresh);
            int report = show.IndexOf("private VisualElement Report(SeasonReport rep)", refresh < 0 ? 0 : refresh);
            if (seasonEnd < 0 || refresh < 0 || rowLoop < 0 || report < 0 || rowLoop > report)
                bad.Add("ShowScreens: ряд кінця сезону (Refresh) не з SeasonEndActions");
            if (show.IndexOf("if (C.CanStartNextSeason)", seasonEnd < 0 ? 0 : seasonEnd) >= 0) bad.Add("ShowScreens: «Новий сезон» повз послідовність рішень");
            if (!show.Contains("if (actions.Count > 0) Run(actions[0]);")) bad.Add("ShowScreens: Enter кінця сезону — не перша дія ряду");
            if (!CodeLine(show, "string fired = CareerScreenLines.FiredLine(T, C, _offer);") || show.Contains("T.Get(\"season.fired\")"))
                bad.Add("ShowScreens: звільнення не через FiredLine (пониження губиться)");
            if (!CodeLine(show, "if (CareerScreenLines.OfferShown(C, _offer)) OfferBlock(col, _offer);")) bad.Add("ShowScreens: пропозиції на папері немає");
            if (!show.Contains("CareerScreenLines.CardHeading(T, c)") || !CodeLine(show, "string farewell = CareerScreenLines.FarewellNote(T, C, c);"))
                bad.Add("ShowScreens: картка прощального матчу не з моделі");
            if (!CodeLine(show, "box.Add(MeterLine(t.Get(\"c.hub.pop\"), v.Popularity, popMax, 56f));")) bad.Add("ShowKit.Resources: шкала популярності не до стелі");
            const string res = "Root.Add(ShowKit.Resources(T, v, CareerScreenLines.PopularityMax(C)));";
            if (Regex.Matches(show, Regex.Escape(res)).Count != 2 || !CodeLine(week, res)) bad.Add("ресурси: не всі три екрани беруть стелю популярності");
            if (!week.Contains("CareerScreenLines.CostumeButton(T, C)") || week.Contains("CareerRules.CostumeCost")) bad.Add("WeekScreenView: ціна костюма не з федерації");
            if (!week.Contains("CareerScreenLines.LivingLine(T, C)")) bad.Add("WeekScreenView: немає рядка житла й дороги");
            int div = create.IndexOf("CareerScreenLines.SetDivision(_d, CareerLooks.HeroBodies[Host.HeroBody]);"), hero = create.IndexOf("C.CreateHero(_d);");
            if (div < 0 || hero < 0 || div > hero) bad.Add("CreateScreenView: дивізіон не ставиться перед створенням героя");
            // слот 28 (ПМ 12.11.2026): наслідок пропуску титулу; «Очок розвитку» — лише там, де шапка числа не має
            if (!CodeLine(show, "Tile(a, T.Get(\"c.season.title_skip\"), CareerScreenLines.SkipTitleEffect(T), CareerScreenLines.SkipTitleRisk(T, C)); break;"))
                bad.Add("ShowScreens: плитка пропуску титулу без наслідку з моделі");
            if (!CodeLine(career, "string points = CareerScreenLines.DevPointsLine(T, C, headerShowsPoints);") || career.Contains("T.Format(\"dev.points\""))
                bad.Add("CareerScreens: рядок очок розвитку повз модель");
            int pointsHead = show.IndexOf("T.Get(\"c.hub.points\")");
            int panelAfterHead = pointsHead < 0 ? -1 : show.IndexOf("DevelopmentPanel(", pointsHead);
            if (pointsHead < 0 || panelAfterHead < 0 || !show.Substring(panelAfterHead).StartsWith("DevelopmentPanel(Refresh, 2, headerShowsPoints: true)"))
                bad.Add("ShowScreens: під шапкою «ОЧКІВ N» — те саме число вдруге");
            if (Regex.Matches(show + week, "headerShowsPoints: true").Count != 1) bad.Add("шапка з числом очок — лише на підсумку матчу");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }
    }
}
