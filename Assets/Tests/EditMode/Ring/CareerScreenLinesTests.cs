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
            CollectionAssert.AreEqual(new[] { SeasonEndAction.AcceptOffer, SeasonEndAction.DeclineOffer, SeasonEndAction.ReadContract },
                CareerScreenLines.SeasonEndActions(s), "обидва ключі — рішення (і дрібний шрифт до нього), «Новий сезон» лише після нього");
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
            CollectionAssert.AreEqual(new[] { SeasonEndAction.AcceptOffer, SeasonEndAction.DeclineOffer, SeasonEndAction.ReadContract },
                CareerScreenLines.SeasonEndActions(s));
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

        /// <summary>
        /// Та сама сесія у свіжій копії через сейв, але в сезоні <paramref name="season"/>: надбавку старіння ставить Sync відновлення —
        /// як у сейві гравця.
        /// </summary>
        private static CareerSession InSeason(CareerSession s, int season)
        {
            string blob = s.CaptureState(), from = "|season=" + s.State.Season + "|";
            Assert.AreEqual(1, (blob.Length - blob.Replace(from, "").Length) / from.Length, "у зліпку один сезон");
            var t = CareerKit.Fresh(s.State.Seed, s.Content);
            t.RestoreState(blob.Replace(from, "|season=" + season + "|"));
            Assert.AreEqual(season, t.State.Season);
            return t;
        }

        /// <summary>
        /// Злиття «Кар'єри» (74a7d4b1, тіло старіє, §5.5; ПМ 12.11.2026, п. А1): з сезону 4 витривалість дорожча — і в очках розвитку, і в
        /// тренувань тренера. Екран показує ту ціну, що спише покупка, і той крок, що рахує тренування: стала <c>Development.AttrCost</c> /
        /// <c>CareerRules.CoachProgressStep</c> казала «+1 (2 оч.)» і «2/3» там, де покупка списувала 3, а до +1 треба було 4 тренування.
        /// </summary>
        [Test]
        public void Ageing_AttrAndCoachLines_ShowWhatThePurchaseCharges()
        {
            var s1 = CareerKit.NewHero(7);
            var s4 = InSeason(s1, 4);
            Assert.AreEqual(Development.AttrCost + 1, s4.AttrCost(RingAttr.Stamina), "передумова: сезон 4 — витривалість дорожча");
            int stamina = System.Array.IndexOf(CareerRules.Coaches, RingAttr.Stamina), other = stamina == 0 ? 1 : 0;
            Assert.GreaterOrEqual(stamina, 0, "тренер витривалості є");
            Assert.AreEqual(CareerRules.CoachProgressStep + 1, s4.CoachStepFor(stamina), "передумова: тренер витривалості довший");
            // жоден атрибут не на стелі: там ціни немає зовсім (п. 6, AttrAtMax_TileSaysMax_HintHasNoPrice), а тут перевіряється ціна
            foreach (var s in new[] { s1, s4 })
                for (int i = 0; i < 5; i++) s.Player.Set((RingAttr)i, 0);

            foreach (var s in new[] { s1, s4 })
                for (int i = 0; i < 5; i++)
                {
                    var a = (RingAttr)i;
                    string hint = CareerScreenLines.AttrBuyHint(T, s, a, null);
                    NoMissingKey(hint, "підказка атрибута");
                    Assert.AreEqual(T.Format("dev.attr.buy", s.AttrCost(a)) + " — " + T.Get("c.dev.attr.why." + i), hint, "сезон " + s.State.Season + ", " + a);
                }
            StringAssert.StartsWith(T.Format("dev.attr.buy", 3), CareerScreenLines.AttrBuyHint(T, s4, RingAttr.Stamina, null), "сезон 4: 3 оч.");
            StringAssert.StartsWith(T.Format("dev.attr.buy", 2), CareerScreenLines.AttrBuyHint(T, s4, RingAttr.Strength, null), "сила — як і була");
            StringAssert.StartsWith(T.Format("dev.attr.buy", 2), CareerScreenLines.AttrBuyHint(T, s1, RingAttr.Stamina, null), "сезон 1 — стара ціна");
            string no = CareerScreenLines.AttrBuyHint(T, s4, RingAttr.Stamina, "points");
            StringAssert.EndsWith(" · " + T.Get("dev.reason.points"), no, "купити не можна — чому");
            StringAssert.StartsWith(T.Format("dev.attr.buy", 3), no);

            // магазин тренерів: найнятий — прогрес із кроком цього тренера, ні — плата; підказка — крок цього тренера
            string sName = RingLabels.AttrName(T, RingAttr.Stamina), oName = RingLabels.AttrName(T, CareerRules.Coaches[other]);
            s4.State.Coach = stamina; s4.State.CoachProgress = 2;
            Assert.AreEqual(T.Format("c.shop.coach.hired", sName, 2, CareerRules.CoachProgressStep + 1), CareerScreenLines.CoachButton(T, s4, stamina));
            StringAssert.Contains("2/4", CareerScreenLines.CoachButton(T, s4, stamina), "сезон 4: до +1 витривалості — 4 тренування");
            Assert.AreEqual(T.Format("c.shop.coach.desc", sName, CareerRules.CoachProgressStep + 1), CareerScreenLines.CoachHint(T, s4, stamina));
            Assert.AreEqual(T.Format("c.shop.coach", oName, CareerRules.CoachWeeklyFee), CareerScreenLines.CoachButton(T, s4, other), "не найнятий — плата");
            Assert.AreEqual(T.Format("c.shop.coach.desc", oName, CareerRules.CoachProgressStep), CareerScreenLines.CoachHint(T, s4, other), "інший тренер — як і був");
            s1.State.Coach = stamina; s1.State.CoachProgress = 2;
            StringAssert.Contains("2/3", CareerScreenLines.CoachButton(T, s1, stamina), "сезон 1 — стара ціна");
            NoMissingKey(CareerScreenLines.CoachHint(T, s1, stamina), "підказка тренера");

            // «Радимо зараз»: ціна атрибута в назві — з поради ядра, тобто та сама, що спише покупка
            s4.Feud.State.DevPoints = 20;
            foreach (var l in CareerScreenLines.DevRecs(T, s4, 9))
                if (l.Advice.Kind == AdviceKind.Attr) StringAssert.EndsWith(" · " + s4.AttrCost(l.Advice.Attr), l.Title, l.Advice.Attr.ToString());
        }

        /// <summary>
        /// Поріг травми зони (злиття «Кар'єри» a3fd3fd5, §5.2; ПМ 12.11.2026, п. А2): у Хардкорника травма приходить на 4-му ушкодженні,
        /// тож рядок тіла пише «2/4», а не «2/3» — поріг береться з виду (<see cref="InjuryView.Threshold"/>), не зі сталої в рядку.
        /// </summary>
        [Test]
        public void Injury_Line_CountsDamageAgainstTheZoneThreshold()
        {
            var plain = CareerKit.NewHero(6, Gimmick.Technician);
            var hard = CareerKit.NewHero(6, Gimmick.Hardcore);
            plain.State.ZoneDamage[(int)BodyZone.Head] = 2;
            hard.State.ZoneDamage[(int)BodyZone.Head] = 2;
            string head = T.Get("bodyzone." + (int)BodyZone.Head);
            string p = CareerScreenLines.InjuryText(T, plain.GetWeekView().Card.Injuries);
            string h = CareerScreenLines.InjuryText(T, hard.GetWeekView().Card.Injuries);
            NoMissingKey(p, "рядок тіла");
            NoMissingKey(h, "рядок тіла Хардкорника");
            Assert.AreEqual(T.Format("c.injury.dmg", head, 2, CareerRules.InjuryAtZoneDamage), p);
            Assert.AreEqual(T.Format("c.injury.dmg", head, 2, hard.InjuryAtZoneDamage), h);
            StringAssert.Contains("2/3", p);
            StringAssert.Contains("2/4", h, "Хардкорник терпить до 4");

            Assert.AreEqual(T.Get("c.injury.none"), CareerScreenLines.InjuryText(T, new List<InjuryView>()), "ціле тіло");
            var mixed = new List<InjuryView>
            {
                new InjuryView { Zone = BodyZone.Legs, WeeksLeft = 2, ZoneDamage = 0 },
                new InjuryView { Zone = BodyZone.Body, ZoneDamage = 1, Threshold = 4 },
                new InjuryView { Zone = BodyZone.Head },
            };
            Assert.AreEqual(T.Format("c.injury.weeks", T.Get("bodyzone." + (int)BodyZone.Legs), 2) + " · "
                + T.Format("c.injury.dmg", T.Get("bodyzone." + (int)BodyZone.Body), 1, 4), CareerScreenLines.InjuryText(T, mixed), "ціла зона — без рядка");

            // смужка зони на стрічці героя (ShowKit.PlayerStrip; рев'ю 13.11.2026): сегментів — поріг, підпис — «ушкодження/поріг»
            var pm = CareerScreenLines.ZoneMeterFor(plain.GetWeekView().Card.Injuries, BodyZone.Head);
            var hm = CareerScreenLines.ZoneMeterFor(hard.GetWeekView().Card.Injuries, BodyZone.Head);
            Assert.AreEqual(CareerRules.InjuryAtZoneDamage, pm.Segments);
            Assert.AreEqual(hard.InjuryAtZoneDamage, hm.Segments, "Хардкорник — чотири сегменти");
            Assert.AreEqual(2, pm.Filled);
            Assert.AreEqual(2, hm.Filled);
            Assert.AreEqual("2/3", CareerScreenLines.ZoneMeterText(T, pm));
            Assert.AreEqual("2/4", CareerScreenLines.ZoneMeterText(T, hm));
            hard.State.ZoneDamage[(int)BodyZone.Head] = 3;
            hm = CareerScreenLines.ZoneMeterFor(hard.GetWeekView().Card.Injuries, BodyZone.Head);
            Assert.AreEqual("3/4", CareerScreenLines.ZoneMeterText(T, hm), "три ушкодження Хардкорника — ще не травма й не повна смужка");
            Assert.Less(hm.Filled, hm.Segments);
            var legs = CareerScreenLines.ZoneMeterFor(mixed, BodyZone.Legs);
            Assert.AreEqual(legs.Segments, legs.Filled, "травма — смужка повна");
            Assert.AreEqual(T.Format("c.hub.weeks", 2), CareerScreenLines.ZoneMeterText(T, legs));
            var body = CareerScreenLines.ZoneMeterFor(mixed, BodyZone.Body);
            Assert.AreEqual(4, body.Segments);
            Assert.AreEqual("1/4", CareerScreenLines.ZoneMeterText(T, body));
            var none = CareerScreenLines.ZoneMeterFor(new List<InjuryView>(), BodyZone.Head);
            Assert.AreEqual(CareerRules.InjuryAtZoneDamage, none.Segments);
            Assert.AreEqual("0/3", CareerScreenLines.ZoneMeterText(T, none), "зони немає у виді — порожня смужка");
        }

        /// <summary>
        /// Ціни старіння й поріг травми в екранах — з сесії й виду (ПМ 12.11.2026, п. А1–А2): у коді екранів кар'єри (не в коментарях)
        /// немає сталих <c>Development.AttrCost</c> і <c>CareerRules.CoachProgressStep</c>, рядок тіла збирає лише <see cref="CareerScreenLines"/>.
        /// Рев'ю 13.11.2026: скан не бачив смужки зони з порогом 3 у ShowScreens — тепер ловить і сталий поріг травми
        /// (<c>CareerRules.InjuryAtZoneDamage</c>, <c>"/3"</c>, <c>Segments(3,</c>, <c>Math.Min(3,</c>) в екранах, а ціни — і в моделях.
        /// </summary>
        [Test]
        public void Sources_AgeingPricesAndInjuryThreshold_ComeFromTheSession()
        {
            string ui = Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Ring", "UI");
            var files = new List<string>(Directory.GetFiles(Path.Combine(ui, "Career"), "*.cs")) { Path.Combine(ui, "CareerScreens.cs") };
            var bad = new List<string>();
            var stale = new Regex(@"Development\.AttrCost\b|CareerRules\.CoachProgressStep\b|CareerRules\.InjuryAtZoneDamage\b|""c\.injury\.dmg""|""/3""|Segments\(\s*3\s*,|Math\.Min\(\s*3\s*,");
            // моделі збирають рядки й самі беруть сталий поріг за замовчуванням і ключ рядка тіла; ціну й крок — лише з сесії
            var staleModels = new Regex(@"Development\.AttrCost\b|CareerRules\.CoachProgressStep\b");
            var models = Directory.GetFiles(Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Ring", "Models"), "*.cs");
            Assert.Greater(models.Length, 0);
            foreach (var pair in new[] { new KeyValuePair<Regex, IEnumerable<string>>(stale, files), new KeyValuePair<Regex, IEnumerable<string>>(staleModels, models) })
                foreach (var f in pair.Value)
                    foreach (var line in File.ReadAllText(f).Split('\n'))
                    {
                        string code = line.Trim();
                        if (code.StartsWith("//")) continue;
                        int cut = code.IndexOf("//", System.StringComparison.Ordinal);
                        if (cut >= 0) code = code.Substring(0, cut);
                        if (pair.Key.IsMatch(code)) bad.Add(Path.GetFileName(f) + ": " + code);
                    }
            string week = File.ReadAllText(Path.Combine(ui, "Career", "WeekScreenView.cs"));
            string career = File.ReadAllText(Path.Combine(ui, "CareerScreens.cs"));
            if (!week.Contains("CareerScreenLines.CoachButton(T, C, k)") || !week.Contains("CareerScreenLines.CoachHint(T, C, k)"))
                bad.Add("WeekScreenView: тренер не з CoachButton / CoachHint");
            if (!career.Contains("CareerScreenLines.AttrBuyHint(T, C, a, can ? null : reason)")) bad.Add("CareerScreens: підказка атрибута не з AttrBuyHint");
            if (!career.Contains("Small(CareerScreenLines.AttrButton(T, C, a),") || career.Contains("\" → \" + Signed(v + 1)"))
                bad.Add("CareerScreens: плитка атрибута не з AttrButton (на стелі — «· макс.», п. 6)");
            if (!career.Contains("return CareerScreenLines.InjuryText(T, injuries);")) bad.Add("CareerScreens: рядок тіла не з InjuryText");
            string show = File.ReadAllText(Path.Combine(ui, "Career", "ShowScreens.cs"));
            if (!show.Contains("CareerScreenLines.ZoneMeterFor(v.Injuries, (BodyZone)z)") || !show.Contains("ArenaKit.Segments(meter.Segments, filled,")
                || !show.Contains("CareerScreenLines.ZoneMeterText(t, meter)"))
                bad.Add("ShowScreens: смужка зони героя не з ZoneMeterFor / ZoneMeterText");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        /// <summary>
        /// Атрибут на стелі (ПМ 13.11.2026, п. 6, огляд слоту 32): плитка «Сила +3 · макс.» замість тьмяної «Сила +3 → +4», підказка —
        /// навіщо атрибут і «Досягнуто максимуму», без ціни: купувати нічого. Хоч би яку причину дав виклик («max», «stage» — на етапі
        /// без розвитку ядро каже її раніше, «points», null) — підказка та сама. Нижче стелі — «було → стане» і ціна, як і були.
        /// </summary>
        [Test]
        public void AttrAtMax_TileSaysMax_HintHasNoPrice()
        {
            var s = CareerKit.NewHero(7);
            s.Feud.State.DevPoints = 9;
            for (int i = 0; i < 5; i++)
            {
                var a = (RingAttr)i;
                string name = RingLabels.AttrName(T, a), why = T.Get("c.dev.attr.why." + i);
                NoMissingKey(name, "назва атрибута " + i);

                s.Player.Set(a, WrestlerStats.MaxAttr);
                Assert.IsTrue(CareerScreenLines.AttrAtMax(s, a), a + ": на стелі");
                string reason;
                Assert.IsFalse(s.CanBuyAttr(a, out reason), a + ": на стелі купити не можна");
                string tile = CareerScreenLines.AttrButton(T, s, a);
                NoMissingKey(tile, "плитка атрибута на стелі");
                Assert.AreEqual(name + " +" + WrestlerStats.MaxAttr + " · " + T.Get("c.dev.attr.max"), tile, a + ": плитка на стелі");
                StringAssert.DoesNotContain("→", tile, a + ": на стелі не обіцяє +1");
                string expect = CareerScreenLines.WithReason(why, T.Get("dev.reason.max"));
                foreach (var r in new[] { reason, "max", "stage", "points", null })
                {
                    string hint = CareerScreenLines.AttrBuyHint(T, s, a, r);
                    NoMissingKey(hint, "підказка атрибута на стелі");
                    Assert.AreEqual(expect, hint, a + ", причина " + (r ?? "null") + ": підказка на стелі — без ціни");
                    StringAssert.DoesNotContain(T.Format("dev.attr.buy", s.AttrCost(a)), hint, a + ": ціни немає");
                    StringAssert.DoesNotContain("оч.", hint, a + ": очок немає");
                }

                s.Player.Set(a, WrestlerStats.MaxAttr - 1);
                Assert.IsFalse(CareerScreenLines.AttrAtMax(s, a));
                Assert.AreEqual(name + " " + RingChances.Signed(WrestlerStats.MaxAttr - 1) + " → " + RingChances.Signed(WrestlerStats.MaxAttr),
                    CareerScreenLines.AttrButton(T, s, a), a + ": нижче стелі — «було → стане»");
                StringAssert.StartsWith(T.Format("dev.attr.buy", s.AttrCost(a)), CareerScreenLines.AttrBuyHint(T, s, a, null), a + ": нижче стелі — ціна");

                s.Player.Set(a, WrestlerStats.MinAttr);
                Assert.AreEqual(name + " \u22121 → 0", CareerScreenLines.AttrButton(T, s, a), a + ": від’ємний — знак мінуса, як у порадах");
            }
            Assert.AreEqual("Сила +3 · макс.", CareerScreenLines.AttrButton(T, WithAttr(s, RingAttr.Strength, 3), RingAttr.Strength), "як просив ПМ");
            // екран кінцівки ворожнечі — своя модель рядка (AttrRow), той самий напис (рев'ю 13.11.2026: там теж було «+3 → +4»)
            Assert.AreEqual("Сила +3 · макс.", CareerScreenLines.AttrTile(T, "Сила", WrestlerStats.MaxAttr));
            Assert.AreEqual("Сила +2 → +3", CareerScreenLines.AttrTile(T, "Сила", 2));
            string ending = File.ReadAllText(Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Ring", "UI", "EndingScreenView.cs"));
            StringAssert.Contains("RingUi.SmallButton(CareerScreenLines.AttrTile(t, a.Name, a.Value),", ending, "кінцівка: плитка атрибута з AttrTile");
            StringAssert.DoesNotContain("\" → \" + Signed(a.Value + 1)", ending, "кінцівка: старе «було → стане» повз AttrTile");
            StringAssert.Contains("private bool _all = CareerScreenView.DevAllForShots;", ending, "огляд знімає кінцівку з повним списком");
            Assert.AreEqual("Сила: удари, кидки, викинути за канати · Досягнуто максимуму",
                CareerScreenLines.AttrBuyHint(T, s, RingAttr.Strength, "max"), "крапка опису перед « · » знята");
        }

        private static CareerSession WithAttr(CareerSession s, RingAttr a, int v) { s.Player.Set(a, v); return s; }

        /// <summary>
        /// Заголовок афіші створення (ПМ 13.11.2026, п. 1, огляд слоту 32): у героїні — «Твоя борчиня», у героя — «Твій борець». Жіноча
        /// форма — ключем <c>c.hub.create.poster.f</c>, яку <see cref="HeroForms.Apply(RingText, bool, bool)"/> ставить за тілом на
        /// екрані; екран бере саме цей ключ (скан коду нижче).
        /// </summary>
        [Test]
        public void Create_PosterHeader_TakesTheHeroSex()
        {
            var t = RingText.FromJson(CareerKit.Read("strings_uk.json"), CareerKit.Read("ui_uk.json"));   // свій текст: Apply міняє його
            HeroForms.Apply(t, true, false);
            Assert.AreEqual("Твоя борчиня", t.Get("c.hub.create.poster"), "героїня");
            HeroForms.Apply(t, false, false);
            Assert.AreEqual("Твій борець", t.Get("c.hub.create.poster"), "герой");
            string create = File.ReadAllText(Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Ring", "UI", "Career", "CreateScreenView.cs"));
            StringAssert.Contains("T.Get(\"c.hub.create.poster\")", create, "афіша бере ключ, який міняють форми героїні");
        }

        /// <summary>
        /// Назва ґіміка за статтю борця на одному екрані (ПМ 13.11.2026, <see cref="RingLabels.GimmickName"/> 7d1510f4): героїня-Силачка
        /// проти суперника-Силача — стрічка героя бере рід героя (<see cref="HeroForms.ShowsHeroine"/>), стрічка суперника — свій
        /// (<c>NpcDef.Female</c>); форми героїні (<see cref="HeroForms.Apply(RingText, bool, bool)"/>) назви суперника не підміняють.
        /// І навпаки: герой проти суперниці. Вирази — ті самі, що в екранах (їх тримає скан нижче).
        /// </summary>
        [Test]
        public void GimmickName_HeroineAndMaleRival_OnOneScreen_EachInTheirOwnSex()
        {
            var t = RingText.FromJson(CareerKit.Read("strings_uk.json"), CareerKit.Read("ui_uk.json"));   // свій текст: Apply міняє його
            var content = CareerKit.Content();
            NpcDef man = null, woman = null;
            foreach (var n in content.Roster)
            {
                if (!n.Female && man == null && RingLabels.GimmickName(t, content.GimmickOf(n.Gimmick).Key, true) != RingLabels.GimmickName(t, content.GimmickOf(n.Gimmick).Key, false)) man = n;
                if (n.Female && woman == null && RingLabels.GimmickName(t, content.GimmickOf(n.Gimmick).Key, true) != RingLabels.GimmickName(t, content.GimmickOf(n.Gimmick).Key, false)) woman = n;
            }
            Assert.IsNotNull(man, "у ростері є суперник-чоловік із ґіміком двох родів");

            // героїня (Силачка) проти суперника-чоловіка
            HeroForms.Apply(t, true, false);
            Assert.IsTrue(HeroForms.ShowsHeroine(t));
            Assert.AreEqual("Силачка", RingLabels.GimmickName(t, content.GimmickOf(Gimmick.Strongman).Key, HeroForms.ShowsHeroine(t)), "стрічка героїні");
            Assert.AreEqual(t.Get("path.face.fem"), RingLabels.PathName(t, RingPath.Face, HeroForms.ShowsHeroine(t)));
            string rival = RingLabels.GimmickName(t, content.GimmickOf(man.Gimmick).Key, man.Female);
            Assert.AreEqual(t.Get("gimmick." + content.GimmickOf(man.Gimmick).Key), rival, "суперник героїні — у своєму, чоловічому роді");
            Assert.AreEqual(t.Get("path." + (man.Path == RingPath.Face ? "face" : "heel")), RingLabels.PathName(t, man.Path, man.Female));
            Assert.AreNotEqual(RingLabels.GimmickName(t, content.GimmickOf(man.Gimmick).Key, true), rival);

            // герой проти суперниці
            HeroForms.Apply(t, false, true);
            Assert.IsFalse(HeroForms.ShowsHeroine(t));
            Assert.AreEqual("Силач", RingLabels.GimmickName(t, content.GimmickOf(Gimmick.Strongman).Key, HeroForms.ShowsHeroine(t)), "стрічка героя");
            Assert.IsNotNull(woman, "у ростері є суперниця з ґіміком двох родів — інакше половина тесту мовчки не перевіряється");
            Assert.AreEqual(t.Get("gimmick." + content.GimmickOf(woman.Gimmick).Key + ".fem"),
                    RingLabels.GimmickName(t, content.GimmickOf(woman.Gimmick).Key, woman.Female), "суперниця — у жіночому роді");
            foreach (var g in content.Gimmicks)
            {
                NoMissingKey(RingLabels.GimmickName(t, g.Key, true), "жіноча назва " + g.Key);
                NoMissingKey(RingLabels.GimmickName(t, g.Key, false), "назва " + g.Key);
            }
        }

        /// <summary>
        /// Назву ґіміка на екранах кар'єри (ПМ 13.11.2026) дає лише <see cref="RingLabels.GimmickName"/> зі статтю борця: у коді (не в
        /// коментарях) усіх екранів <c>UI/**</c> і рядків <c>CareerScreenLines</c> немає ключа <c>"gimmick.…"</c> (зі склеюванням, сталого
        /// чи інтерпольованого) і <c>Get(… Def.NameKey)</c> / <c>GimmickOf(…).NameKey</c> повз неї (опис <c>NameKey + ".desc"</c> — без
        /// роду, його можна). Герой — <c>HeroForms.ShowsHeroine</c> (на створенні — за тілом на екрані), суперник — <c>npc.Female</c>.
        /// </summary>
        [Test]
        public void Sources_CareerScreens_GimmickNameTakesTheWrestlersSex()
        {
            string ui = Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Ring", "UI");
            // рев'ю 13.11.2026: скан — усі екрани UI/** (а не лише Career/ і CareerScreens.cs) і рядки моделі; ловить і сталий ключ,
            // і інтерпольований, і склеювання в змінну
            var files = new List<string>(Directory.GetFiles(ui, "*.cs", SearchOption.AllDirectories))
                { Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Ring", "Models", "CareerScreenLines.cs") };
            Assert.That(files.Exists(f => Path.GetFileName(f) == "CareerScreens.cs") && files.Exists(f => Path.GetFileName(f) == "ShowScreens.cs"),
                "скан не бачить екранів кар'єри");
            var bypass = new Regex(@"""gimmick\.|\.Get\(\s*[\w.()]*Def\.NameKey\s*\)|GimmickOf\([^)]*\)\.NameKey");
            var bad = new List<string>();
            foreach (var f in files)
                foreach (var line in File.ReadAllText(f).Split('\n'))
                {
                    string code = line.Trim();
                    if (code.StartsWith("//")) continue;
                    int cut = code.IndexOf("//", System.StringComparison.Ordinal);
                    if (cut >= 0) code = code.Substring(0, cut);
                    if (bypass.IsMatch(code)) bad.Add(Path.GetFileName(f) + ": " + code + " — назва ґіміка без статі борця");
                }
            string show = File.ReadAllText(Path.Combine(ui, "Career", "ShowScreens.cs"));
            string create = File.ReadAllText(Path.Combine(ui, "Career", "CreateScreenView.cs"));
            if (!show.Contains("RingLabels.GimmickName(t, c.Content.GimmickOf(v.Gimmick).Key, HeroForms.ShowsHeroine(t))")) bad.Add("ShowScreens: ґімік героя не за статтю героя");
            if (!show.Contains("RingLabels.GimmickName(t, c.Content.GimmickOf(npc.Gimmick).Key, npc.Female)")) bad.Add("ShowScreens: ґімік суперника не за статтю суперника");
            if (!create.Contains("RingLabels.GimmickName(T, g.Def.Key, HeroForms.ShowsHeroine(T))")) bad.Add("CreateScreenView: плитки ґіміків не за тілом на екрані");
            if (Regex.Matches(create, Regex.Escape("RingLabels.GimmickName(T, C.Content.GimmickOf(_d.Gimmick).Key, HeroForms.ShowsHeroine(T))")).Count != 2)
                bad.Add("CreateScreenView: стрічка й афіша — не обидві за тілом на екрані");
            Assert.IsEmpty(bad, string.Join("\n", bad));
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
            // плити розвитку показу (підсумок матчу, кінець сезону; слот 30): у кожній шапка «ОЧКІВ N», тож картка числа вдруге не пише;
            // тиждень шапки з числом не має
            var plates = Regex.Matches(show, @"DevelopmentPanel\(Refresh, 2[^;]*;");
            if (plates.Count != 2) bad.Add("ShowScreens: плит розвитку — дві (підсумок і кінець сезону), знайдено " + plates.Count);
            foreach (Match p in plates)
            {
                int pointsHead = show.LastIndexOf("T.Get(\"c.hub.points\")", p.Index);
                int method = show.LastIndexOf("private VisualElement DevPlate()", p.Index);
                if (!p.Value.Contains("headerShowsPoints: true") || pointsHead < 0 || method < 0 || pointsHead < method)
                    bad.Add("ShowScreens: плита розвитку без числа очок у своїй шапці або з тим самим числом удруге — " + p.Value);
            }
            if (week.Contains("headerShowsPoints: true")) bad.Add("WeekScreenView: шапки з числом очок у шухляді немає");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        /// <summary>
        /// Плити розвитку показу (кадри слоту 30, 12.11.2026): «▸ Усе (19)» на кінці сезону різав навпіл край прокрутки, підказка повного
        /// списку лежала в прокрутці під ним, а на підсумку 4:3 плита лягала на канати. Перемикач і підказка — у підвалі під прокруткою;
        /// плита не вища за місце до канатів (від висоти панелі: канати — відлік знизу); стискається лише прокрутка.
        /// </summary>
        [Test]
        public void Sources_DevPlates_KeepAllAndHintBelowTheScroll_AboveTheRopes()
        {
            string ui = Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Ring", "UI");
            string show = File.ReadAllText(Path.Combine(ui, "Career", "ShowScreens.cs"));
            string career = File.ReadAllText(Path.Combine(ui, "CareerScreens.cs"));
            var bad = new List<string>();
            if (!CodeLine(career, "if (moreTo != null) { more.style.marginBottom = 0f; more.style.flexShrink = 0f; moreTo.Add(more); }"))
                bad.Add("CareerScreens: перемикач «Усе (N)» не йде в підвал");
            if (show.Contains("scroll.Add(MakeHint())")) bad.Add("ShowScreens: рядок-підказка знову в прокрутці");
            // рев'ю слоту 30: підказка — лише в розгорнутому списку й сталої висоти (підвал не росте при наведенні); щойно розгорнутий
            // список — прокрутка до його початку
            if (!CodeLine(career, "if (!_devAll) { HintLine = null; return; }")) bad.Add("CareerScreens: згорнутий підвал з порожньою підказкою");
            if (!CodeLine(career, "hint.style.minHeight = h; hint.style.height = h; hint.style.maxHeight = h;")
                || !career.Contains("float h = CareerTileLayout.DevFootHintH;"))
                bad.Add("CareerScreens: висота підказки підвалу не стала");
            if (!career.Contains("_devAll = !_devAll; _devAllOpened = _devAll;")) bad.Add("CareerScreens: перемикач «Усе» не позначає щойно розгорнутий список");
            if (!CodeLine(career, "if (opened) DevAllStart = attrsHead;")) bad.Add("CareerScreens: не позначено початок повного списку");
            int n = 0;
            foreach (Match m in Regex.Matches(show, @"private VisualElement DevPlate\(\)\s*\{"))
            {
                n++;
                int end = show.IndexOf("return box;", m.Index);
                string body = end < 0 ? "" : show.Substring(m.Index, end - m.Index);
                int foot = body.IndexOf("= DevFooter("), panel = body.IndexOf("moreTo: foot)"), scroll = body.IndexOf(".Add(scroll);");
                int hint = body.IndexOf("DevFooterHint(foot);"), footAdd = body.IndexOf(".Add(foot);");
                if (foot < 0 || panel < foot || scroll < panel || hint < scroll || footAdd < hint)
                    bad.Add("DevPlate №" + n + ": не «підвал → панель із moreTo → прокрутка → підказка в підвал → підвал після прокрутки»");
                if (!body.Contains("scroll.style.flexShrink = 1f;")) bad.Add("DevPlate №" + n + ": прокрутка не стискається");
                if (!body.Contains("CareerTileLayout.DevPlateMaxH(")) bad.Add("DevPlate №" + n + ": плита без межі до канатів");
                if (body.Contains("scroll.style.maxHeight = 370f")) bad.Add("DevPlate №" + n + ": стара стала висота прокрутки кінця сезону");
                if (!body.Contains("scroll.style.minHeight = CareerTileLayout.DevScrollMinH;")) bad.Add("DevPlate №" + n + ": найменша прокрутка не з моделі");
                // щойно розгорнутий «Усе» — до початку списку (ScrollToDevAll), інакше — та сама прокрутка після перебудови (огляд слоту 31)
                int to = body.IndexOf("KeepDevScroll(scroll, ");
                if (to < footAdd) bad.Add("DevPlate №" + n + ": після підвалу немає прокрутки до щойно розгорнутого списку чи до колишнього місця");
            }
            if (n != 2) bad.Add("DevPlate: плит розвитку на екранах показу — дві, знайдено " + n);
            // межа від справжньої висоти панелі — і при зміні розміру (кінець сезону), і при зміні верху на вузькій панелі (підсумок)
            if (!CodeLine(show, "if (_devBox != null) _devBox.style.maxHeight = CareerTileLayout.DevPlateMaxH(CareerTileLayout.SeasonDevTop, Root.layout.height);"))
                bad.Add("SeasonEnd: межа плити не оновлюється з висотою панелі");
            if (!CodeLine(show, "if (_devCol != null) _devCol.style.maxHeight = CareerTileLayout.DevPlateMaxH(top, Root.layout.height);"))
                bad.Add("ShowResult.Fit: межа плити не йде за її верхом");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        private static ScrollRow Row(float top, float bottom, bool head = false) { return new ScrollRow(top, bottom, head); }

        /// <summary>
        /// Прокрутка на межу рядка (ПМ 13.11.2026, п. 2 і 5, кадри слоту 32), чиста частина — <see cref="CareerTileLayout.SnapRows"/>.
        /// п. 2 (с4, 1080): «Усе» щойно розгорнуто, а початок повного списку нижче межі прокрутки — вікно стояло на межі, і над
        /// «АТРИБУТИ» висів зріз поради. Тепер верх — сам «АТРИБУТИ», вікно до кінця, решта — запас знизу.
        /// п. 5 (4:3): нижній край різав заголовок «ПЕРКИ» — тепер вікно кінчається на останньому цілому рядку, заголовок без свого
        /// рядка ховається разом із ним.
        /// </summary>
        [Test]
        public void SnapRows_TopOnARow_BottomAfterTheLastWholeRow_HeadKeptWithItsRow()
        {
            const RowSnapMode Mark = RowSnapMode.Mark, Restore = RowSnapMode.Restore, Keep = RowSnapMode.Keep;
            float extra;
            // п. 2: дві поради, далі «АТРИБУТИ» (100) і решта; вміст 282, вікно 200 — межа прокрутки 82, нижче за 100
            var s4 = new List<ScrollRow> { Row(0, 50), Row(54, 96), Row(100, 118, true), Row(121, 145), Row(148, 172), Row(175, 193, true),
                Row(196, 220), Row(223, 247), Row(250, 274) };
            Assert.AreEqual(100f, CareerTileLayout.SnapRows(s4, 282f, 200f, 100f, Mark, out extra), "п. 2: верх — сам заголовок списку");
            Assert.AreEqual(18f, extra, 0.01f, "п. 2: вікно від «АТРИБУТИ» до кінця (182), решта 18 — запас знизу");

            // п. 5: заголовок, два ряди атрибутів, «НОВІ ПРИЙОМИ», ряд, «ПЕРКИ» (123–141), перки; вікно 135 різало «ПЕРКИ»
            var narrow = new List<ScrollRow> { Row(0, 18, true), Row(21, 45), Row(48, 72), Row(75, 93, true), Row(96, 120), Row(123, 141, true),
                Row(144, 168), Row(171, 195) };
            Assert.AreEqual(0f, CareerTileLayout.SnapRows(narrow, 200f, 135f, 0f, Restore, out extra));
            Assert.AreEqual(12f, extra, 0.01f, "п. 5: вікно кінчається там, де починається «ПЕРКИ» (123), — край його не ріже");
            // вікно 145: «ПЕРКИ» вміщається цілим, а його рядок — ні: заголовок без рядка над краєм не лишається
            Assert.AreEqual(0f, CareerTileLayout.SnapRows(narrow, 200f, 145f, 0f, Restore, out extra));
            Assert.AreEqual(22f, extra, 0.01f, "заголовок — разом зі своїм рядком");
            // вікно 170: «ПЕРКИ» і перший ряд перків цілі; наступний ряд (171) починається нижче краю — різати нічого
            Assert.AreEqual(0f, CareerTileLayout.SnapRows(narrow, 200f, 170f, 0f, Restore, out extra));
            Assert.AreEqual(0f, extra, 0.01f, "низ 170 — у проміжку між рядами");
            Assert.AreEqual(0f, CareerTileLayout.SnapRows(narrow, 200f, 160f, 0f, Restore, out extra));
            Assert.AreEqual(160f - 123f, extra, 0.01f, "край посеред ряду перків — «ПЕРКИ» ховається з ним: вікно до 123");
            // межа допуску: низ ряду на 0,4 нижче краю — ряд цілий; на 2 нижче — ні
            Assert.AreEqual(0f, CareerTileLayout.SnapRows(narrow, 200f, 167.6f, 0f, Restore, out extra));
            Assert.AreEqual(0f, extra, 0.01f, "168 при краї 167,6 — у допуску пів пікселя");
            Assert.AreEqual(0f, CareerTileLayout.SnapRows(narrow, 200f, 166f, 0f, Restore, out extra));
            Assert.AreEqual(166f - 123f, extra, 0.01f);

            // верх — на початок рядка, що містить бажаний зсув (не нижче): 50 — у ряду 48–72, 47 — між рядами, на попередній
            Assert.AreEqual(48f, CareerTileLayout.SnapRows(narrow, 200f, 100f, 50f, Restore, out extra));
            Assert.AreEqual(21f, CareerTileLayout.SnapRows(narrow, 200f, 100f, 47f, Mark, out extra));
            Assert.AreEqual(0f, CareerTileLayout.SnapRows(narrow, 200f, 100f, -5f, Restore, out extra), "від'ємний — з верху");
            Assert.AreEqual(0f, CareerTileLayout.SnapRows(narrow, 200f, 100f, float.NaN, Mark, out extra), "NaN — з верху");
            Assert.AreEqual(0f, CareerTileLayout.SnapRows(narrow, 200f, 100f, float.NaN, Keep, out extra), "NaN рукою — з верху, не NaN");
            // гравець прокрутив сам — верх не рухається, лише чистий низ
            Assert.AreEqual(30f, CareerTileLayout.SnapRows(narrow, 200f, 100f, 30f, Keep, out extra));
            Assert.AreEqual(130f - 123f, extra, 0.01f, "рукою на 30: низ 130 різав «ПЕРКИ» (123) — вікно до 123");

            // рев'ю 13.11.2026: гравець був у кінці довгого списку, купив — список коротшає; відновлений зсув (190) — далі нової межі
            // (200 − 100 = 100): упор у межу, а не майже порожнє вікно
            Assert.AreEqual(96f, CareerTileLayout.SnapRows(narrow, 200f, 100f, 190f, Restore, out extra), "до межі 100, на початок рядка 96");
            Assert.AreEqual(0f, extra, 0.01f, "від 96 вікно до 196 — останній ряд (171–195) цілий, різати нічого");
            // мітка ж нижче межі — дозволено (п. 2): від неї до кінця, решта порожня
            Assert.AreEqual(144f, CareerTileLayout.SnapRows(narrow, 200f, 100f, 150f, Mark, out extra));
            Assert.AreEqual(100f - (200f - 144f), extra, 0.01f, "мітка 144: вікно 56 вмісту, 44 — запас");
            // рукою за межею (запас від мітки ще стоїть) — верх не рухається, запас тримає зсув
            Assert.AreEqual(120f, CareerTileLayout.SnapRows(narrow, 200f, 100f, 120f, Keep, out extra));
            Assert.AreEqual(20f, extra, 0.01f);

            // рядки не з нуля (відступ картки 8): верх — 0, низ на межі рядка
            var padded = new List<ScrollRow> { Row(8, 26, true), Row(29, 53), Row(56, 80), Row(83, 107) };
            Assert.AreEqual(0f, CareerTileLayout.SnapRows(padded, 115f, 70f, 0f, Restore, out extra));
            Assert.AreEqual(70f - 56f, extra, 0.01f);
            // два заголовки поспіль над краєм — ховаються обидва
            var twoHeads = new List<ScrollRow> { Row(0, 24), Row(27, 51), Row(54, 72, true), Row(75, 93, true), Row(96, 120) };
            Assert.AreEqual(0f, CareerTileLayout.SnapRows(twoHeads, 130f, 100f, 0f, Restore, out extra));
            Assert.AreEqual(100f - 54f, extra, 0.01f, "обидва заголовки — разом зі своїм рядком");
            // лише заголовок вміщається — лишається (порожнє вікно гірше)
            Assert.AreEqual(54f, CareerTileLayout.SnapRows(twoHeads, 130f, 20f, 54f, Restore, out extra));
            Assert.AreEqual(0f, extra, 0.01f, "перший заголовок — єдиний рядок вікна, лишається; наступний (75) нижче краю 74");

            // уміщається — гортати нічого
            Assert.AreEqual(0f, CareerTileLayout.SnapRows(narrow, 200f, 200f, 0f, Restore, out extra));
            Assert.AreEqual(0f, extra);
            Assert.AreEqual(0f, CareerTileLayout.SnapRows(new List<ScrollRow>(), 300f, 100f, 0f, Restore, out extra));
            Assert.AreEqual(0f, extra);
            // видно до кінця — запасу немає
            Assert.AreEqual(96f, CareerTileLayout.SnapRows(narrow, 200f, 104f, 96f, Restore, out extra));
            Assert.AreEqual(0f, extra, 0.01f, "від 96 до кінця (200) — 104: видно все до кінця");
            // рядок, вищий за вікно, цілим не стане — запасу немає (частина краща за порожнечу)
            var tall = new List<ScrollRow> { Row(0, 18, true), Row(21, 300) };
            Assert.AreEqual(0f, CareerTileLayout.SnapRows(tall, 300f, 100f, 0f, Restore, out extra));
            Assert.AreEqual(0f, extra);
        }

        /// <summary>
        /// Атоми — у рядки (<see cref="CareerTileLayout.MergeRows"/>): кнопки одного ряду (різної висоти) — один рядок; заголовок лише
        /// тоді, коли весь рядок — заголовок; порожні й NaN відкидаються; порядок — за верхом, хоч би як прийшли.
        /// </summary>
        [Test]
        public void MergeRows_ButtonsOfOneLine_OneRow_HeadOnlyIfAllHeads()
        {
            var rows = CareerTileLayout.MergeRows(new[] { Row(48, 72), Row(0, 18, true), Row(21, 45), Row(21, 46), Row(200, 200),
                Row(float.NaN, 10f), Row(75, 93, true), Row(80, 90), Row(96, 120, true), Row(97, 119, true) });
            Assert.AreEqual(5, rows.Count, string.Join(" | ", rows) + " — порожній атом (200–200) рядком не стає");
            Assert.AreEqual(0f, rows[0].Top); Assert.IsTrue(rows[0].Head);
            Assert.AreEqual(21f, rows[1].Top); Assert.AreEqual(46f, rows[1].Bottom, "ряд кнопок — до найнижчої"); Assert.IsFalse(rows[1].Head);
            Assert.AreEqual(48f, rows[2].Top);
            Assert.AreEqual(75f, rows[3].Top); Assert.IsFalse(rows[3].Head, "заголовок із кнопкою поруч — уже не заголовок");
            Assert.IsTrue(rows[4].Head, "два заголовки на одній висоті — заголовок");
            Assert.AreEqual(0, CareerTileLayout.MergeRows(null).Count);
            // дотик рядків (низ одного = верх іншого) — два рядки
            Assert.AreEqual(2, CareerTileLayout.MergeRows(new[] { Row(0, 20), Row(20, 40) }).Count);
        }

        /// <summary>
        /// Прокрутки розвитку йдуть на межу рядка (п. 2 і 5): шухляда тижня (і «Витрати»), плита підсумку, плита кінця сезону — через
        /// <c>RowSnap</c>; заголовки розділів і «Рекомендовано» — заголовки рядків, порада — неподільна група. Прив'язка тримається, доки
        /// гравець не прокрутить сам; базовий відступ — з розрахованого стилю першої розкладки.
        /// </summary>
        [Test]
        public void Sources_DevScrolls_SnapToRows()
        {
            string ui = Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Ring", "UI");
            string week = File.ReadAllText(Path.Combine(ui, "Career", "WeekScreenView.cs"));
            string career = File.ReadAllText(Path.Combine(ui, "CareerScreens.cs"));
            var bad = new List<string>();
            if (!CodeLine(week, "RowSnap(scroll, () => mark != null ? MarkTop(mark, scroll) : restore.y, mark != null);")) bad.Add("WeekScreenView: шухляда не на межу рядка");
            if (week.Contains("scroll.scrollOffset = restore;")) bad.Add("WeekScreenView: старе відновлення зсуву повз RowSnap");
            if (!CodeLine(career, "RowSnap(scroll, () => MarkTop(mark, scroll), true);")) bad.Add("CareerScreens: ScrollToDevAll повз RowSnap (мітка)");
            if (!CodeLine(career, "RowSnap(scroll, () => restore.y, false);")) bad.Add("CareerScreens: KeepDevScroll повз RowSnap (відновлення)");
            if (Regex.Matches(career, Regex.Escape("scroll.scrollOffset = ")).Count != 1) bad.Add("CareerScreens: зсув прокрутки ставиться не лише в RowSnap");
            if (!CodeLine(career, "l.AddToClassList(RowHeadClass);") || !CodeLine(career, "recHead.AddToClassList(RowHeadClass);"))
                bad.Add("CareerScreens: заголовки розділів не позначені");
            if (!career.Contains("box.AddToClassList(RowUnitClass);")) bad.Add("CareerScreens: порада не неподільна");
            if (!career.Contains("CareerTileLayout.SnapRows(RowsOf(scroll.contentContainer), contentH, view, anchored ? want() : scroll.scrollOffset.y, mode, out add)"))
                bad.Add("CareerScreens: RowSnap не з SnapRows або прив'язка не до want");
            if (!CodeLine(career, "if (float.IsNaN(basePad)) basePad = scroll.resolvedStyle.paddingBottom;")) bad.Add("CareerScreens: базовий відступ не з першої розкладки");
            if (career.Contains("++waits")) bad.Add("CareerScreens: прив'язку знову знято лічильником подій (рев'ю 13.11.2026)");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        /// <summary>
        /// Рід назви шляху (запит «Кар'єри» через ПМ, 12.11.2026, P2-6): героїня бачить «Героїня / Лиходійка / Бунтарка», суперниця на
        /// картці — у своєму роді. HeroForms назв шляхів не підміняє (їх видно в обох борців), тож кожен виклик <c>PathName</c> в
        /// екранах кар'єри передає стать борця третім аргументом: героя — <c>HeroForms.ShowsHeroine</c> (на створенні — за тілом на
        /// екрані), суперника — <c>npc.Female</c>. Виклик із двома аргументами — назва завжди в чоловічому роді.
        /// </summary>
        [Test]
        public void Sources_CareerScreens_PathNameTakesTheWrestlersSex()
        {
            string ui = Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Ring", "UI");
            var files = new List<string>(Directory.GetFiles(Path.Combine(ui, "Career"), "*.cs")) { Path.Combine(ui, "CareerScreens.cs") };
            var bad = new List<string>();
            int calls = 0;
            var two = new Regex(@"RingLabels\.PathName\(\s*\w+\s*,\s*[\w.]+\s*\)");
            foreach (var f in files)
            {
                string src = File.ReadAllText(f);
                calls += Regex.Matches(src, @"RingLabels\.PathName\(").Count;
                foreach (System.Text.RegularExpressions.Match m in two.Matches(src)) bad.Add(Path.GetFileName(f) + ": " + m.Value + " — без статі борця");
            }
            string show = File.ReadAllText(Path.Combine(ui, "Career", "ShowScreens.cs"));
            string create = File.ReadAllText(Path.Combine(ui, "Career", "CreateScreenView.cs"));
            if (!show.Contains("RingLabels.PathName(t, v.Role, HeroForms.ShowsHeroine(t))")) bad.Add("ShowScreens: шлях героя не за статтю героя");
            if (!show.Contains("RingLabels.PathName(t, npc.Path, npc.Female)")) bad.Add("ShowScreens: шлях суперника не за статтю суперника");
            if (Regex.Matches(create, Regex.Escape("RingLabels.PathName(T, path, HeroForms.ShowsHeroine(T))")).Count != 2)
                bad.Add("CreateScreenView: стрічка й плашка ролі — не обидві за тілом на екрані");
            Assert.GreaterOrEqual(calls, 4, "викликів PathName в екранах кар'єри менше, ніж стрічки героя й суперника та дві на створенні");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        /// <summary>
        /// Правки огляду слоту 30 (ПМ, 12.11.2026) стоять в екранах: високі плити — щільні й з фаскою лише в куті, шухляда — над
        /// канатами з «Усе (N)» у підвалі, «Прийоми» створення на папері на вузькій панелі, тост налаштувань гасне при зміні
        /// вкладки, клин титулу підганяється під підпис.
        /// </summary>
        [Test]
        public void Sources_Slot30Review_FixesAreWiredIntoTheScreens()
        {
            string ui = Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Ring", "UI");
            string show = File.ReadAllText(Path.Combine(ui, "Career", "ShowScreens.cs"));
            string week = File.ReadAllText(Path.Combine(ui, "Career", "WeekScreenView.cs"));
            string create = File.ReadAllText(Path.Combine(ui, "Career", "CreateScreenView.cs"));
            string settings = File.ReadAllText(Path.Combine(ui, "Menus", "SettingsPanel.cs"));
            string title = File.ReadAllText(Path.Combine(ui, "Menus", "TitleMenuView.cs"));
            string shapes = File.ReadAllText(Path.Combine(ui, "Menus", "MenuShapes.cs"));
            var bad = new List<string>();
            const string cap = "SlantCapH = CareerTileLayout.PlateSlantCapH";
            if (Regex.Matches(show, Regex.Escape(cap)).Count != 2) bad.Add("ShowScreens: плити розвитку підсумку й кінця сезону — не обидві з фаскою в куті");
            if (Regex.Matches(week, Regex.Escape(cap)).Count != 1) bad.Add("WeekScreenView: шухляда без фаски в куті");
            foreach (var plate in Regex.Matches(show + week, @"new ArenaPlate \{[^}]*" + Regex.Escape(cap)))
                if (!((System.Text.RegularExpressions.Match)plate).Value.Contains("BrandTokens.AlphaPlate")) bad.Add("висока плита не щільна (AlphaPlate): " + ((System.Text.RegularExpressions.Match)plate).Value);
            if (week.Contains("HeaderBottom + 50f")) bad.Add("WeekScreenView: шухляда знову на старому низу (214, у канатах)");
            if (Regex.Matches(week, Regex.Escape("CareerTileLayout.DrawerBottom")).Count < 3) bad.Add("WeekScreenView: низ шухляди (створення й Fit) — не DrawerBottom");
            if (!week.Contains("DevelopmentPanel(Refresh, 2, moreTo: foot)")) bad.Add("WeekScreenView: «Усе (N)» шухляди не в підвалі");
            if (!Regex.IsMatch(create, @"bool paper = narrow && _paperMoves != null;")) bad.Add("CreateScreenView: прийоми на папері не залежать від вузької панелі");
            if (!create.Contains("_desc.style.display = paper ? DisplayStyle.None : DisplayStyle.Flex")) bad.Add("CreateScreenView: опис ролі не ховається під прийоми");
            if (!create.Contains("_movesCard.style.display = paper ? DisplayStyle.None : DisplayStyle.Flex")) bad.Add("CreateScreenView: темна картка прийомів лишається на вузькій панелі");
            var swap = Regex.Match(settings, @"void SwitchTab\([^)]*\)\s*\{(?<body>[\s\S]*?)\n        \}");
            if (!swap.Success || !swap.Groups["body"].Value.Contains("ClearToast();")) bad.Add("SettingsPanel: SwitchTab не гасить тост попередньої вкладки");
            if (!title.Contains("CareerTileLayout.FitTitleWedge(")) bad.Add("TitleMenuView: клин не підганяється під підпис");
            if (Regex.Matches(title, @"RegisterCallback<GeometryChangedEvent>\(e => FitWedge\(\)\)").Count < 2) bad.Add("TitleMenuView: FitWedge не на зміні геометрії підпису й панелі");
            if (!shapes.Contains("CareerTileLayout.WedgeTaper") || shapes.Contains("ww * 0.62f")) bad.Add("MenuBackdrop: звуження клина — не з CareerTileLayout.WedgeTaper");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        // ---------------- пакет «Так, усі 11» (власник, 12.11.2026) і правки огляду слоту 31 ----------------

        /// <summary>
        /// №4: вибір плитки — заливкою (лиходій, ґіміки) чи краєм (герой: темна плитка з кобальтовим краєм і смужкою, а не кобальтова
        /// заливка). Фокус завжди золотий, вимкнена — без країв.
        /// </summary>
        [Test]
        public void TileLook_SelectAsEdge_KeepsTheTileDark()
        {
            var edge = CareerTileLayout.Look(true, false, true, true, false);
            Assert.AreEqual(TileFill.Panel, edge.Fill, "герой обраний — плитка темна");
            Assert.AreEqual(TileEdge.Select, edge.Stripe, "смужка — кольору вибору");
            Assert.AreEqual(TileEdge.Select, edge.Outline, "край — кольору вибору");
            Assert.IsFalse(edge.DarkText, "на темній плитці текст світлий");

            var fill = CareerTileLayout.Look(true, false, true, false, true);
            Assert.AreEqual(TileFill.Select, fill.Fill, "лиходій обраний — заливка, як було");
            Assert.AreEqual(TileEdge.None, fill.Stripe);
            Assert.AreEqual(TileEdge.None, fill.Outline);
            Assert.IsTrue(fill.DarkText, "темний текст на світлій заливці вибору — як просила плитка");

            var focus = CareerTileLayout.Look(true, true, true, true, false);
            Assert.AreEqual(TileFill.Gold, focus.Fill, "фокус — золото й поверх вибору краєм");
            Assert.AreEqual(TileEdge.Focus, focus.Outline);
            Assert.IsTrue(focus.DarkText);

            var plain = CareerTileLayout.Look(true, false, false, true, false);
            Assert.AreEqual(TileFill.Panel, plain.Fill);
            Assert.AreEqual(TileEdge.Accent, plain.Stripe, "не обрана — смужка акценту");
            Assert.AreEqual(TileEdge.None, plain.Outline);

            var off = CareerTileLayout.Look(false, true, true, true, true);
            Assert.AreEqual(TileFill.Disabled, off.Fill);
            Assert.AreEqual(TileEdge.None, off.Outline, "вимкнена — без рамки фокуса й вибору");
            Assert.IsFalse(off.DarkText);
        }

        /// <summary>Правка 1 слоту 31: шухляда тижня рахує місце від низу наліпки «У ФОРМІ», а не від афіші (наліпка звисає на 26).</summary>
        [Test]
        public void DrawerRoom_CountsTheStickerHang()
        {
            float plain = CareerTileLayout.DrawerRoomUnderPoster(800f, 400f, false);
            float sticker = CareerTileLayout.DrawerRoomUnderPoster(800f, 400f, true);
            Assert.AreEqual(800f - 400f - CareerTileLayout.DrawerPosterGap, plain, 1e-4f);
            Assert.AreEqual(plain - CareerTileLayout.PosterStickerHang, sticker, 1e-4f, "наліпка забирає своє місце");
        }

        /// <summary>Правка 6 слоту 31: «опис · причина» — без крапки перед роздільником; без причини — опис як є.</summary>
        [Test]
        public void WithReason_DropsThePeriodBeforeTheSeparator()
        {
            Assert.AreEqual("Ажіотаж на старті +1 · Ранг шляху замалий", CareerScreenLines.WithReason("Ажіотаж на старті +1.", "Ранг шляху замалий"));
            Assert.AreEqual("Без крапки · причина", CareerScreenLines.WithReason("Без крапки", "причина"));
            Assert.AreEqual("Трикрапка... · причина", CareerScreenLines.WithReason("Трикрапка...", "причина"), "трикрапку не ріжемо");
            Assert.AreEqual("Опис.", CareerScreenLines.WithReason("Опис.", null));
            Assert.AreEqual("причина", CareerScreenLines.WithReason("", "причина"));
            string rank = T.Get("dev.reason.rank");
            NoMissingKey(rank, "dev.reason.rank");
            var bad = new List<string>();
            foreach (var p in PerkCatalog.All)
            {
                string line = CareerScreenLines.WithReason(RingLabels.PerkDesc(T, p.Id), rank);
                if (line.Contains(". ·")) bad.Add(p.Id + ": " + line);
            }
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        /// <summary>№7: «переріс щабель» — лише коли ядро так каже; ключ має жіночу форму (героїня «переросла»).</summary>
        [Test]
        public void Outgrown_Line_FromTheCard()
        {
            Assert.IsNull(CareerScreenLines.OutgrownLine(T, new ShowCardView()));
            Assert.IsNull(CareerScreenLines.OutgrownLine(T, null));
            string line = CareerScreenLines.OutgrownLine(T, new ShowCardView { IsOutgrown = true });
            NoMissingKey(line, "c.hub.outgrown");
            Assert.AreEqual(T.Get("c.hub.outgrown"), line);
            var t = MiniJson.ParseObject(CareerKit.Read("ui_uk.json"));
            Assert.IsTrue(t.ContainsKey("c.hub.outgrown.f"), "героїня — «переросла»");
            StringAssert.Contains("ереросла", (string)t["c.hub.outgrown.f"]);
        }

        /// <summary>
        /// №11 і №8: плитки тижня вимкнені з причиною — зірка поза рингом уже тренувалась (обидва тренування), виключне право (підробіток);
        /// дій немає — «дії вичерпано» першим. Причини — ті самі ключі, що повертає ядро (week.reason.star_training / .exclusive).
        /// </summary>
        [Test]
        public void WeekTiles_SayWhyTheyAreOff()
        {
            var w = new WeekView { ActionsLeft = 2, Money = 999 };
            Assert.IsNull(CareerScreenLines.TrainReason(T, w, false));
            Assert.IsNull(CareerScreenLines.TrainReason(T, w, true));
            Assert.IsNull(CareerScreenLines.SideJobReason(T, w, false));
            w.StarTrainingUsed = true;
            Assert.AreEqual(T.Get("c.week.reason.star"), CareerScreenLines.TrainReason(T, w, false));
            Assert.AreEqual(T.Get("c.week.reason.star"), CareerScreenLines.TrainReason(T, w, true), "з тренером — те саме тренування");
            w.StarTrainingUsed = false; w.Money = CareerRules.TrainerCost - 3;
            Assert.AreEqual(T.Format("c.hub.need_money", 3), CareerScreenLines.TrainReason(T, w, true));
            Assert.IsNull(CareerScreenLines.TrainReason(T, w, false), "без тренера гроші не потрібні");
            Assert.AreEqual(T.Get("c.week.reason.exclusive"), CareerScreenLines.SideJobReason(T, w, true));
            w.ActionsLeft = 0; w.StarTrainingUsed = true;
            Assert.AreEqual(T.Get("c.hub.no_actions"), CareerScreenLines.TrainReason(T, w, false), "дій немає — це головне");
            Assert.AreEqual(T.Get("c.hub.no_actions"), CareerScreenLines.SideJobReason(T, w, true));
            // повні причини ядра — у відгуку дії (ResultText: week.reason.{Reason}); на плитці — короткі
            foreach (var k in new[] { "week.reason.star_training", "week.reason.exclusive", "c.week.reason.star", "c.week.reason.exclusive", "c.hub.no_actions" })
                NoMissingKey(T.Get(k), k);
        }

        /// <summary>
        /// №8: частка менеджерки в підсумку вечора — ставка до частки, частка окремо, і сума рядка дорівнює числу праворуч (TotalFee);
        /// без менеджерки — як було. Кнопка звільнення й підказка «Виїзного шоу» під виключним правом.
        /// </summary>
        [Test]
        public void Manager_FeeLine_AndShop()
        {
            var r = new ShowResult { Fee = 51, ManagerCut = 9, FeeWinBonus = 10, FeeHypeBonus = 5 };
            Assert.AreEqual(T.Format("c.hub.fee_split", 60, 15) + " · " + T.Format("c.result.manager", 9), CareerScreenLines.FeeSplitLine(T, r));
            Assert.AreEqual(66, r.TotalFee, "ставка 60 + бонуси 15 − частка 9 = 66 — число праворуч");
            var plain = new ShowResult { Fee = 60, FeeWinBonus = 15 };
            Assert.AreEqual(T.Format("c.hub.fee_split", 60, 15), CareerScreenLines.FeeSplitLine(T, plain));

            var s = CareerKit.NewHero(11);
            Assert.IsFalse(CareerScreenLines.FireManagerShown(s));
            s.State.Manager = true;
            Assert.IsTrue(CareerScreenLines.FireManagerShown(s));
            string hint = CareerScreenLines.FireManagerHint(T);
            NoMissingKey(hint, "c.shop.manager.fire.desc");
            StringAssert.Contains(CareerRules.ManagerFeePercent + " %", hint);
            NoMissingKey(T.Get("c.shop.manager.fire"), "c.shop.manager.fire");
            Assert.AreEqual(T.Get("c.shop.away.desc"), CareerScreenLines.AwayHint(T, s));
            s.State.ContractExclusive = true; s.State.ContractEndSeason = s.State.Season;
            Assert.IsTrue(s.ExclusiveActive);
            Assert.AreEqual(CareerScreenLines.WithReason(T.Get("c.shop.away.desc"), T.Get("week.reason.exclusive")), CareerScreenLines.AwayHint(T, s));
            Assert.IsFalse(s.CanGoAway, "підказка каже те саме, що ядро");
        }

        /// <summary>№11: поворот ролі в рядку відгуку — у роді героя (героїня — «Лиходійка»); без повороту — нічого.</summary>
        [Test]
        public void Turn_Line_InTheHerosForm()
        {
            Assert.IsNull(CareerScreenLines.TurnLine(T, new WeekActionResult(), false));
            Assert.IsNull(CareerScreenLines.TurnLine(T, null, false));
            var r = new WeekActionResult { TurnedTo = RingPath.Heel };
            Assert.AreEqual(T.Format("c.delta.turn", T.Get("path.heel")), CareerScreenLines.TurnLine(T, r, false));
            Assert.AreEqual(T.Format("c.delta.turn", T.Get("path.heel.fem")), CareerScreenLines.TurnLine(T, r, true));
            NoMissingKey(CareerScreenLines.TurnLine(T, r, true), "c.delta.turn");
        }

        /// <summary>
        /// №8: «Прочитати дрібний шрифт» — у ряду кінця сезону поряд із рішенням, раз на пропозицію: після читання зникає, з менеджеркою
        /// його немає (вона читає сама). Рядки контракту на папері — аванс, дрібний шрифт — з ключами.
        /// </summary>
        [Test]
        public void ReadContract_Action_OncePerOffer_NotWithTheManager()
        {
            var s = AtSeasonEnd(3, 1, 3, CareerRules.OfferTrust);
            CollectionAssert.AreEqual(new[] { SeasonEndAction.AcceptOffer, SeasonEndAction.DeclineOffer, SeasonEndAction.ReadContract },
                CareerScreenLines.SeasonEndActions(s));
            var o = new SeasonOfferModel(s, T);
            Assert.IsTrue(o.ShowContract);
            NoMissingKey(o.AdvanceText, "аванс");
            NoMissingKey(o.FinePrintText, "дрібний шрифт");
            NoMissingKey(o.ReadText, "c.offer.read");
            NoMissingKey(o.ReadChanceText, "c.offer.read.chance");
            Assert.AreEqual(T.Get("c.offer.fine.unread"), o.FinePrintText, "до читання — «ще не прочитано»");
            o.Read();
            CollectionAssert.AreEqual(new[] { SeasonEndAction.AcceptOffer, SeasonEndAction.DeclineOffer }, CareerScreenLines.SeasonEndActions(s),
                "прочитано — плитки більше немає");
            Assert.AreNotEqual(T.Get("c.offer.fine.unread"), new SeasonOfferModel(s, T).FinePrintText, "на папері — що видно");

            var m = AtSeasonEnd(4, 1, 3, CareerRules.OfferTrust);
            m.State.Manager = true;
            CollectionAssert.AreEqual(new[] { SeasonEndAction.AcceptOffer, SeasonEndAction.DeclineOffer }, CareerScreenLines.SeasonEndActions(m),
                "з менеджеркою читати не треба");
            NoMissingKey(new SeasonOfferModel(m, T).ManagerNote, "c.offer.fine.manager");
        }

        /// <summary>
        /// №11: пізня роль — на останньому кроці кінця сезону (лише «Новий сезон»): не поряд із титульним шансом і контрактом.
        /// 043f2706: позначка обраної — роль на НАСТУПНИЙ сезон. Заголовок блоку — чинна роль (без ролі — «Пізня роль»), рядок під
        /// кнопками — «З наступного сезону: …», лише коли обрана інша, ніж чинна; та сама — що вона дає; без ролі — як працює вибір.
        /// Обрана на наступний ніде не видає себе за чинну: ні в заголовку, ні в рядку хабу.
        /// </summary>
        [Test]
        public void LateRole_Block_OnTheLastStep_SaysTheChoice()
        {
            var s = AtSeasonEnd(3, 1, 3, CareerRules.OfferTrust);
            Assert.IsFalse(CareerScreenLines.LateRoleShown(s, CareerScreenLines.SeasonEndActions(s)), "поки пропозиція — не тут");
            s.DeclineOffer();
            Assert.IsTrue(CareerScreenLines.LateRoleShown(s, CareerScreenLines.SeasonEndActions(s)));
            var m = new LateRoleModel(s, T);
            Assert.AreEqual(T.Get("c.late.title"), CareerScreenLines.LateRoleTitle(m), "чинної ролі немає — просто заголовок");
            Assert.AreEqual(m.HintText, CareerScreenLines.LateRoleNote(T, m), "без ролі — як працює вибір");

            // перший сезон: обрала Ікону на наступний — заголовок не каже «Пізня роль: Ікона», рядок каже «з наступного сезону»
            Assert.IsTrue(m.Choose(LateRole.Icon));
            m = new LateRoleModel(s, T);
            Assert.AreEqual(T.Get("c.late.title"), CareerScreenLines.LateRoleTitle(m), "обрана на наступний — не чинна");
            Assert.IsNull(m.CurrentText, "у хабі ролі ще немає");
            string next = T.Format("c.late.next", T.Get("c.late.icon"));
            string note = CareerScreenLines.LateRoleNote(T, m);
            NoMissingKey(note, "рядок пізньої ролі");
            StringAssert.StartsWith(next + ". ", note);
            StringAssert.Contains(T.Format("c.late.icon.desc", LateRoles.IconStartHype), note);
            StringAssert.EndsWith(".", note);
            StringAssert.DoesNotContain("..", note);

            // новий сезон Іконою: у кінці наступного сезону Ікона — чинна (заголовок), та сама обрана — що вона дає, без «з наступного»
            s.State.LateRole = LateRole.Icon;
            m = new LateRoleModel(s, T);
            string current = T.Format("c.late.current", T.Get("c.late.icon"));
            Assert.AreEqual(current, CareerScreenLines.LateRoleTitle(m));
            Assert.IsNull(m.NextText);
            note = CareerScreenLines.LateRoleNote(T, m);
            StringAssert.DoesNotContain(T.Format("c.late.next", ""), note);
            Assert.AreEqual(T.Format("c.late.icon.desc", LateRoles.IconStartHype) + ".", note, "та сама — що вона дає, з крапкою");

            // передумала на «Зірку»: чинна лишається Іконою — і в заголовку, і в хабі; рядок — про наступний сезон
            Assert.IsTrue(m.Choose(LateRole.OffRingStar));
            m = new LateRoleModel(s, T);
            Assert.AreEqual(current, CareerScreenLines.LateRoleTitle(m), "золота смужка «Зірки» — не чинна роль");
            Assert.AreEqual(current, m.CurrentText, "у хабі — рядок нинішньої ролі, не обраної");
            note = CareerScreenLines.LateRoleNote(T, m);
            StringAssert.StartsWith(T.Format("c.late.next", T.Get("c.late.star")) + ". ", note);
            StringAssert.EndsWith(".", note, "опис без крапки — крапку додано");
            StringAssert.DoesNotContain("..", note);

            // відмовилась від ролі: «З наступного сезону: Без пізньої ролі.» — без опису, що повторює назву
            Assert.IsTrue(m.Choose(LateRole.None));
            m = new LateRoleModel(s, T);
            Assert.AreEqual(T.Format("c.late.next", T.Get("c.late.none")) + ".", CareerScreenLines.LateRoleNote(T, m));
            Assert.AreEqual(current, CareerScreenLines.LateRoleTitle(m));

            var t = AtSeasonEnd(5, 1, 3, CareerRules.OfferTrust);
            t.State.Report.TitleEligible = true; t.State.Report.TitleDone = false;
            Assert.IsFalse(CareerScreenLines.LateRoleShown(t, CareerScreenLines.SeasonEndActions(t)), "поки титульний шанс — не тут");
        }

        /// <summary>Пакет №4–№11 і правки огляду слоту 31 вбудовано в екрани (скан джерел).</summary>
        [Test]
        public void Sources_Package11_AndSlot31_AreWired()
        {
            string ui = Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Ring", "UI");
            string show = File.ReadAllText(Path.Combine(ui, "Career", "ShowScreens.cs"));
            string week = File.ReadAllText(Path.Combine(ui, "Career", "WeekScreenView.cs"));
            string create = File.ReadAllText(Path.Combine(ui, "Career", "CreateScreenView.cs"));
            string kit = File.ReadAllText(Path.Combine(ui, "Career", "ArenaKit.cs"));
            string career = File.ReadAllText(Path.Combine(ui, "CareerScreens.cs"));
            string dialog = File.ReadAllText(Path.Combine(ui, "Menus", "MenuDialog.cs"));
            string pause = File.ReadAllText(Path.Combine(ui, "Menus", "PauseOverlay.cs"));
            var bad = new List<string>();
            // №4
            if (!kit.Contains("CareerTileLayout.Look(")) bad.Add("ArenaKit: плитка не бере вигляд із CareerTileLayout.Look");
            if (!CodeLine(create, "tile.SelectAsEdge = r == CareerRole.Hero;")) bad.Add("CreateScreenView: герой не обирається краєм");
            // №7
            if (!CodeLine(week, "string outgrown = CareerScreenLines.OutgrownLine(T, c);")) bad.Add("WeekScreenView: афіша без «переріс щабель»");
            if (!CodeLine(show, "string outgrown = CareerScreenLines.OutgrownLine(T, c);")) bad.Add("ShowScreens: картка шоу без «переріс щабель»");
            // №8
            if (!week.Contains("new SeasonOfferModel(C, T).ExclusiveText")) bad.Add("WeekScreenView: хаб без рядка виключного права");
            if (Regex.Matches(week, Regex.Escape("CareerScreenLines.SideJobReason(")).Count < 2) bad.Add("WeekScreenView: підробіток (плитка й клавіша 6) без причини");
            if (!week.Contains("CareerScreenLines.FireManagerShown(C)") || !week.Contains("C.FireManager()")) bad.Add("WeekScreenView: немає «Звільнити менеджерку»");
            if (!week.Contains("CareerScreenLines.AwayHint(T, C)")) bad.Add("WeekScreenView: «Виїзне шоу» без причини виключного права");
            if (!show.Contains("CareerScreenLines.FeeSplitLine(T, r)")) bad.Add("ShowScreens: гонорар без частки менеджерки");
            if (!show.Contains("case SeasonEndAction.ReadContract: _offer.Read(); break;")) bad.Add("ShowScreens: «Прочитати дрібний шрифт» нічого не робить");
            foreach (var part in new[] { "o.AdvanceText", "o.FinePrintText, o.FinePrintWarns", "o.ManagerNote", "o.PenaltyText" })
                if (!show.Contains(part)) bad.Add("ShowScreens: на папері немає " + part);
            // №11
            if (Regex.Matches(week, Regex.Escape("CareerScreenLines.TrainReason(")).Count < 4) bad.Add("WeekScreenView: тренування (плитки й клавіші 1–2) без причини зірки");
            if (!week.Contains("new LateRoleModel(C, T).CurrentText")) bad.Add("WeekScreenView: хаб без рядка пізньої ролі");
            if (!week.Contains("CareerScreenLines.TurnLine(T, r, HeroForms.ShowsHeroine(T))")) bad.Add("WeekScreenView: відгук без повороту ролі");
            if (!show.Contains("if (CareerScreenLines.LateRoleShown(C, CareerScreenLines.SeasonEndActions(C))) LateRoleBlock(col);")) bad.Add("ShowScreens: немає вибору пізньої ролі");
            // правки слоту 31
            if (!week.Contains("CareerTileLayout.DrawerRoomUnderPoster(")) bad.Add("WeekScreenView: шухляда не рахує наліпку (правка 1)");
            if (Regex.Matches(week, @"Tab\.style\.flexShrink = 0f").Count < 2) bad.Add("WeekScreenView: вкладки «Розвиток / Витрати» стискаються (правка 2)");
            if (!dialog.Contains("MenuShapes.Trapezoid(") || !dialog.Contains("card.Add(col);")) bad.Add("MenuDialog: діалог без підкладки (правка 3)");
            if (pause.Contains("MenuKit.Footer(") || !pause.Contains("_col.Add(_footer);")) bad.Add("PauseOverlay: підказка клавіш знову внизу, на плитках тижня (правка 4)");
            if (!create.Contains("CareerTileLayout.CreateAttrRowNarrowH") || !create.Contains("CareerTileLayout.CreatePaperPadBottomNarrow")) bad.Add("CreateScreenView: афіша на вузькій панелі не компактна (правка 5)");
            // правка 6: підказка атрибута — через AttrBuyHint (усередині WithReason; ціна старіння, ПМ 12.11.2026, п. А1)
            if (Regex.Matches(career, Regex.Escape("CareerScreenLines.WithReason(")).Count + Regex.Matches(career, Regex.Escape("CareerScreenLines.AttrBuyHint(")).Count < 2)
                bad.Add("CareerScreens: «опис. · причина» (правка 6)");
            if (Regex.Matches(show, Regex.Escape("KeepDevScroll(scroll, ")).Count != 2 || Regex.Matches(show, Regex.Escape("RememberDevScroll();")).Count != 2)
                bad.Add("ShowScreens: плити розвитку не тримають прокрутку при перебудові (правка 8)");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        /// <summary>Титри — без шляхів до файлів репозиторію (огляд слоту 30, ПМ: «Assets/ThirdParty/…», «tools/ring_audio» у титрах).</summary>
        [Test]
        public void Credits_ShowNoRepositoryPaths()
        {
            var t = MiniJson.ParseObject(CareerKit.Read("ui_uk.json"));
            var bad = new List<string>();
            int n = 0;
            foreach (var kv in t)
            {
                if (!kv.Key.StartsWith("m.credits.", System.StringComparison.Ordinal)) continue;
                n++;
                string v = kv.Value as string ?? "";
                if (Regex.IsMatch(v, @"[\w.-]+/[\w./-]+|\\|\.(md|cs|json|txt)\b")) bad.Add(kv.Key + ": " + v);
            }
            Assert.Greater(n, 6, "ключів титрів — понад шість; менше — розбір зламався");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        /// <summary>
        /// Тексти кар'єри й меню (c.*, week.*, m.*) звертаються на «ти»: «обери рядок», не «оберіть» (огляд слоту 30). Мітки бою
        /// («unavail.*: Ви в різних зонах») — зона бою, тут не перевіряються.
        /// </summary>
        [Test]
        public void CareerAndMenuTexts_SpeakInTheSecondPersonSingular()
        {
            StringAssert.StartsWith("обери", T.Get("c.hub.create.pick"));
            var t = MiniJson.ParseObject(CareerKit.Read("ui_uk.json"));
            var plural = new Regex(@"(?i)\b(оберіть|виберіть|натисніть|спробуйте|ви|вас|вам|ваш\w*)\b");
            var bad = new List<string>();
            foreach (var kv in t)
            {
                if (!(kv.Key.StartsWith("c.", System.StringComparison.Ordinal) || kv.Key.StartsWith("week.", System.StringComparison.Ordinal)
                      || kv.Key.StartsWith("m.", System.StringComparison.Ordinal))) continue;
                string v = kv.Value as string ?? "";
                if (plural.IsMatch(v)) bad.Add(kv.Key + ": " + v);
            }
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }
    }
}
