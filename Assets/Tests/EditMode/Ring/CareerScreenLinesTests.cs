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

        /// <summary>Ширина-заглушка для правила імені: 20 на знак на кеглі 40 (лінійна за кеглем).</summary>
        private static float Px20(string s) { return 20f * (s ?? "").Length; }

        /// <summary>
        /// Довге ім'я на стрічці (ПМ 14.11.2026: «можна зменшувати кегль до мінімуму токенів (не нижче 15 — охоронець), а далі
        /// другий рядок або «…». Мітка ролі й силует мають лишатися вільними»): правило <see cref="CareerTileLayout.FitStripName"/>
        /// крок за кроком — 40; менший з ґіміком до 26; без ґіміка до 18; два рядки 19…15; один рядок до 15; «…» на 15.
        /// </summary>
        [Test]
        public void StripName_FitRule_StepByStep()
        {
            var f = CareerTileLayout.FitStripName("ЛЕВ", Px20, 200f, 300f);
            Assert.AreEqual(40f, f.Size); Assert.IsFalse(f.HideOptional); Assert.AreEqual("ЛЕВ", f.Text); Assert.IsFalse(f.TwoLines || f.Ellipsis);
            // 15 знаків = 300 на 40; до мітки з ґіміком 210 → 28: ґімік лишається
            string n15 = new string('А', 15);
            f = CareerTileLayout.FitStripName(n15, Px20, 210f, 300f);
            Assert.AreEqual(28f, f.Size); Assert.IsFalse(f.HideOptional, "28 ≥ 26 — ґімік на місці");
            // 180 → 24 < 26: ґімік ховається, без нього 250 → 33
            f = CareerTileLayout.FitStripName(n15, Px20, 180f, 250f);
            Assert.AreEqual(33f, f.Size); Assert.IsTrue(f.HideOptional); Assert.IsFalse(f.TwoLines || f.Ellipsis);
            // без ґіміка місця вдосталь — кегль не більший за 40
            f = CareerTileLayout.FitStripName(n15, Px20, 180f, 1000f);
            Assert.AreEqual(40f, f.Size); Assert.IsTrue(f.HideOptional);
            // ім'я з пробілом, що без ґіміка влазить одним рядком (180 → 20 ≥ 18 на кроці 1) — один рядок, а не два
            f = CareerTileLayout.FitStripName("АААА ББББ", Px20, 50f, 90f);
            Assert.AreEqual(20f, f.Size); Assert.IsFalse(f.TwoLines, "один рядок на 20 — раніше за два"); Assert.AreEqual(1, f.Drop);
            f = CareerTileLayout.FitStripName("АААА ББББ", Px20, new[] { 50f, 90f, 200f, 300f });
            Assert.AreEqual(1, f.Drop, "поясу не чіпаємо, коли один рядок ≥ 18 є вже без ґіміка"); Assert.IsFalse(f.TwoLines);
            // 10 + пробіл + 11 = 440; без ґіміка 150 → 13 < 18: два рядки, довший 220 → 27, але не більше StripNameTwoLineMax
            string two = new string('А', 10) + " " + new string('Б', 11);
            f = CareerTileLayout.FitStripName(two, Px20, 100f, 150f);
            Assert.IsTrue(f.TwoLines); Assert.IsTrue(f.HideOptional); Assert.IsFalse(f.Ellipsis);
            Assert.AreEqual(CareerTileLayout.StripNameTwoLineMax, f.Size);
            Assert.AreEqual(new string('А', 10) + "\n" + new string('Б', 11), f.Text);
            // два рядки на 16: довший 220, місця 88
            f = CareerTileLayout.FitStripName(two, Px20, 50f, 88f);
            Assert.IsTrue(f.TwoLines); Assert.AreEqual(16f, f.Size);
            // без пробілу двох рядків немає: один рядок 16 (300 у 120)
            f = CareerTileLayout.FitStripName(n15, Px20, 50f, 120f);
            Assert.IsFalse(f.TwoLines || f.Ellipsis); Assert.AreEqual(16f, f.Size); Assert.AreEqual(n15, f.Text);
            // і на 15 не влазить — «…» на 15: місце 100 на 15 = 266,7 на 40 → 12 літер + «…» (260)
            f = CareerTileLayout.FitStripName(n15, Px20, 50f, 100f);
            Assert.IsTrue(f.Ellipsis); Assert.IsTrue(f.HideOptional); Assert.IsFalse(f.TwoLines);
            Assert.AreEqual(CareerTileLayout.StripNameMinSize, f.Size); Assert.AreEqual(new string('А', 12) + "…", f.Text);
            // перше слово не влазить і на 15 (200 > 160) — «…» одним рядком: 7 літер + «…»
            f = CareerTileLayout.FitStripName(two, Px20, 50f, 60f);
            Assert.IsTrue(f.Ellipsis); Assert.IsFalse(f.TwoLines); Assert.AreEqual(new string('А', 7) + "…", f.Text);
            // перше слово влазить на 15, а рівні рядки — ні (240 > 160): перший рядок цілим, другий — з «…»
            f = CareerTileLayout.FitStripName("АААА " + new string('Б', 12), Px20, 50f, 60f);
            Assert.IsTrue(f.Ellipsis); Assert.IsTrue(f.TwoLines); Assert.AreEqual(CareerTileLayout.StripNameMinSize, f.Size);
            Assert.AreEqual("АААА\n" + new string('Б', 7) + "…", f.Text);
            // кілька слів: першим рядком — найбільше слів, що влазять
            f = CareerTileLayout.FitStripName("АА ББ ВВ " + new string('Г', 12), Px20, 50f, 60f);
            Assert.AreEqual("АА ББ ВВ\n" + new string('Г', 7) + "…", f.Text);
            Assert.AreEqual("…", CareerTileLayout.Clip("ААА", Px20, 10f), "і перша літера не влазить — лише «…»");
            Assert.AreEqual("ААА", CareerTileLayout.Clip("ААА", Px20, 60f), "влазить — як є");
            Assert.AreEqual("А…", CareerTileLayout.Clip("А БВ", Px20, 50f), "пробіл перед «…» знято");
            // розкладки ще немає — ім'я як є
            f = CareerTileLayout.FitStripName(n15, Px20, float.NaN, 100f);
            Assert.AreEqual(40f, f.Size); Assert.IsFalse(f.HideOptional || f.Ellipsis || f.TwoLines);
            f = CareerTileLayout.FitStripName(n15, Px20, 100f, float.NaN);
            Assert.AreEqual(40f, f.Size); Assert.IsFalse(f.HideOptional || f.Ellipsis);
            f = CareerTileLayout.FitStripName(null, Px20, 10f, 10f);
            Assert.AreEqual("", f.Text); Assert.AreEqual(40f, f.Size);

            // кроки мітки (АД 14.11.2026): ґімік → кегль до 18 → два рядки 15–19 → короткий пояс → пояс → лише тоді «…».
            // 10 + пробіл + 12 = 460, довший рядок 240. Повний пояс 80: два рядки на 13 < 15 — короткий пояс 120: два рядки на 19
            string belt = new string('А', 10) + " " + new string('Б', 12);
            f = CareerTileLayout.FitStripName(belt, Px20, new[] { 50f, 80f, 120f, 200f });
            Assert.AreEqual(2, f.Drop, "короткий пояс"); Assert.IsTrue(f.TwoLines); Assert.IsFalse(f.Ellipsis);
            Assert.AreEqual(CareerTileLayout.StripNameTwoLineMax, f.Size);
            // повний пояс лишає два рядки на 15 — поясу не чіпаємо, хоч з коротким було б 19
            f = CareerTileLayout.FitStripName(belt, Px20, new[] { 50f, 90f, 120f, 200f });
            Assert.AreEqual(1, f.Drop); Assert.AreEqual(15f, f.Size); Assert.IsTrue(f.TwoLines);
            // і короткий не рятує — пояс ховається: без нього 200 → 17 одним рядком < 18, два рядки на 19
            f = CareerTileLayout.FitStripName(belt, Px20, new[] { 50f, 60f, 80f, 200f });
            Assert.AreEqual(3, f.Drop, "пояс сховано"); Assert.AreEqual(CareerTileLayout.StripNameTwoLineMax, f.Size); Assert.IsFalse(f.Ellipsis);
            // «…» — лише коли віддано все: на останньому кроці 40 (106,7 на 40) — перше слово 200 не влазить, «АААА…»
            f = CareerTileLayout.FitStripName(belt, Px20, new[] { 10f, 20f, 30f, 40f });
            Assert.IsTrue(f.Ellipsis); Assert.AreEqual(3, f.Drop); Assert.AreEqual("АААА…", f.Text);
            // два кроки — те саме, що стара пара «з ґіміком / без»
            var p2 = CareerTileLayout.FitStripName(belt, Px20, 50f, 88f);
            var l2 = CareerTileLayout.FitStripName(belt, Px20, new[] { 50f, 88f });
            Assert.AreEqual(p2.Size, l2.Size); Assert.AreEqual(p2.Text, l2.Text); Assert.AreEqual(p2.Drop, l2.Drop);
            Assert.AreEqual(40f, CareerTileLayout.FitStripName(belt, Px20, new[] { 50f }).Size, "один крок — ім'я як є");
            Assert.AreEqual(40f, CareerTileLayout.FitStripName(belt, Px20, new[] { 50f, 80f, float.NaN }).Size, "NaN — ім'я як є");
        }

        /// <summary>
        /// Порядок кроків мітки (АД 14.11.2026: «обрізаємо його останнім, після всіх приміток»): хоч яким буде місце на чотирьох
        /// кроках, крок k віддано лише тоді, коли на кроці k − 1 ім'я цілим не вміщалось; «…» — лише на останньому кроці; кожен рядок —
        /// у місці свого кроку.
        /// </summary>
        [Test]
        public void StripName_Steps_GiveTheNotesBeforeTheName()
        {
            string[] names = { "МАКСИМІЛІАН ШЕРШЕНЬКОВИЙ", new string('Ю', 24), new string('А', 10) + " " + new string('Б', 12), "ЛЕВ" };
            int dropped2 = 0, dropped3 = 0, ell = 0;
            foreach (var n in names)
                for (float a0 = 0f; a0 <= 300f; a0 += 37f)
                    for (float d1 = 0f; d1 <= 120f; d1 += 23f)
                        for (float d2 = 0f; d2 <= 80f; d2 += 19f)
                            for (float d3 = 0f; d3 <= 60f; d3 += 29f)
                            {
                                var av = new[] { a0, a0 + d1, a0 + d1 + d2, a0 + d1 + d2 + d3 };
                                var f = CareerTileLayout.FitStripName(n, Px20, av);
                                string where = string.Format("«{0}», місце {1}/{2}/{3}/{4}", n, av[0], av[1], av[2], av[3]);
                                for (int k = 1; k < f.Drop; k++)
                                {
                                    var g = CareerTileLayout.FitStripName(n, Px20, new[] { av[0], av[k] });
                                    if (!g.Ellipsis) Assert.Fail(where + ": віддано крок " + f.Drop + ", а на кроці " + k + " ім'я вміщалось цілим");
                                }
                                if (f.Ellipsis) { Assert.AreEqual(3, f.Drop, where + ": «…» до останнього кроку"); ell++; }
                                foreach (var line in f.Text.Split('\n'))
                                    if (line != "…") Assert.LessOrEqual(Px20(line) * f.Size / CareerTileLayout.StripNameSize, av[f.Drop] + 0.01f, where + ": рядок «" + line + "»");
                                if (f.Drop == 2) dropped2++;
                                if (f.Drop == 3) dropped3++;
                            }
            Assert.Greater(dropped2, 0, "короткий пояс справді трапляється");
            Assert.Greater(dropped3, 0, "і пояс ховається");
            Assert.Greater(ell, 0, "і «…» на останньому кроці");
        }

        /// <summary>
        /// Хоч яким буде місце, ім'я не лягає на мітку ролі й ляльку: кожен рядок у місці (з ґіміком чи без нього — як вирішено),
        /// кегль 15…40, ґімік на місці лише від 26, два рядки — не більше StripNameTwoLineMax, «…» — лише на 15.
        /// </summary>
        [Test]
        public void StripName_Fit_NeverCoversTheTagOrTheDoll()
        {
            string[] names = { "ЛЕВ", "МАКСИМІЛІАН ШЕРШЕНЬКОВИЙ", new string('Ю', 24), "А Б В Г Д Е Є Ж З И І Ї", "ДУЖЕ  ДОВГЕ  ІМ'Я" };
            int fits = 0, ell = 0;
            foreach (var n in names)
                for (float without = 20f; without <= 420f; without += 7f)
                    for (float with = 0f; with <= without; with += 11f)
                    {
                        var f = CareerTileLayout.FitStripName(n, Px20, with, without);
                        string where = string.Format("«{0}», з ґіміком {1}, без {2}", n, with, without);
                        Assert.GreaterOrEqual(f.Size, CareerTileLayout.StripNameMinSize, where);
                        Assert.LessOrEqual(f.Size, CareerTileLayout.StripNameSize, where);
                        if (!f.HideOptional) Assert.GreaterOrEqual(f.Size, CareerTileLayout.StripNameKeepTagMin, where + ": ґімік лишився на дрібному імені");
                        if (f.TwoLines) Assert.LessOrEqual(f.Size, CareerTileLayout.StripNameTwoLineMax, where);
                        float room = f.HideOptional ? without : with;
                        string[] lines = f.Text.Split('\n');
                        foreach (var line in lines)
                            if (line != "…") Assert.LessOrEqual(Px20(line) * f.Size / CareerTileLayout.StripNameSize, room + 0.01f, where + ": рядок «" + line + "»");
                        Assert.AreEqual(f.TwoLines, f.Text.Contains("\n"), where);
                        Assert.LessOrEqual(lines.Length, 2, where);
                        if (f.Ellipsis)
                        {
                            Assert.AreEqual(CareerTileLayout.StripNameMinSize, f.Size, where);
                            Assert.IsTrue(f.Text.EndsWith("…"), where + ": урізане — з «…» в кінці");
                            string seen = f.Text.Replace("\n", " ").TrimEnd('…').TrimEnd();
                            Assert.IsTrue(n.Replace(" ", "").StartsWith(seen.Replace(" ", "")), where + ": видно початок імені, а не щось інше («" + seen + "»)");
                            ell++;
                            continue;
                        }
                        Assert.AreEqual(n.Replace(" ", ""), f.Text.Replace("\n", "").Replace(" ", ""), where + ": ціле ім'я — без утрат");
                        fits++;
                    }
            Assert.Greater(fits, 0); Assert.Greater(ell, 0, "і «…» справді трапляється (24 знаки без пробілу у 20)");
        }

        /// <summary>Поділ на два рядки — за пробілом, де довший рядок найкоротший; без пробілу всередині — ні.</summary>
        [Test]
        public void StripName_TwoLines_SplitWhereTheLongerLineIsShortest()
        {
            string a, b;
            Assert.IsTrue(CareerTileLayout.SplitTwoLines("А ББББ ВВВВ", Px20, out a, out b));
            Assert.AreEqual("А ББББ", a); Assert.AreEqual("ВВВВ", b);
            Assert.IsTrue(CareerTileLayout.SplitTwoLines("МАКСИМІЛІАН ШЕРШЕНЬКОВИЙ", Px20, out a, out b));
            Assert.AreEqual("МАКСИМІЛІАН", a); Assert.AreEqual("ШЕРШЕНЬКОВИЙ", b);
            Assert.IsTrue(CareerTileLayout.SplitTwoLines("А  Б", Px20, out a, out b), "подвійний пробіл");
            Assert.AreEqual("А", a); Assert.AreEqual("Б", b);
            Assert.IsFalse(CareerTileLayout.SplitTwoLines("БЕЗПРОБІЛУ", Px20, out a, out b));
            Assert.IsFalse(CareerTileLayout.SplitTwoLines(" КРАЙ ", Px20, out a, out b), "пробіл лише з країв — не поділ");
            Assert.IsFalse(CareerTileLayout.SplitTwoLines(null, Px20, out a, out b));
        }

        /// <summary>
        /// Числа стрічки й поля імені: два рядки на найбільшому кеглі — над рядками зон (і він найбільший такий); пороги правила
        /// впорядковані; поле — ширина плити без полів і запасу під курсор, кегль 30 → не нижче 15.
        /// </summary>
        [Test]
        public void StripName_AndNameField_Numbers()
        {
            float max = CareerTileLayout.StripNameTwoLineMax;
            // чорнило двох рядків (АД 14.11.2026): висхідна + крок 1,25 + хвіст великих — щонайменше за 2 до зон
            float ink = CareerTileLayout.DisplayAscentEm + CareerTileLayout.StripNameLinePitchEm + CareerTileLayout.DisplayCapsTailEm;
            float floorY = CareerTileLayout.StripZonesTop - CareerTileLayout.StripNameInkClear;
            Assert.LessOrEqual(CareerTileLayout.StripNameTop + ink * max, floorY, "хвіст другого рядка — за 2 до зон");
            Assert.Greater(CareerTileLayout.StripNameTop + ink * (max + 1f), floorY, "найбільший можливий");
            Assert.AreEqual(1.25f, CareerTileLayout.StripNameLinePitchEm, "крок двох рядків — 1,25 (АД)");
            Assert.AreEqual(2f, CareerTileLayout.StripNameInkClear);
            Assert.AreEqual(18f, max, "на кроці 1,25 — 18: на 19 хвіст абетки ліг би на 52,8");
            Assert.AreEqual(22.5f, CareerTileLayout.StripNameLine2Offset(18f), 0.001f);
            Assert.GreaterOrEqual(max, CareerTileLayout.StripNameMinSize);
            Assert.GreaterOrEqual(CareerTileLayout.StripNameMinSize, 15f, "мінімум токенів, не нижче 15");
            Assert.Less(CareerTileLayout.StripNameOneLineMin, CareerTileLayout.StripNameKeepTagMin);
            Assert.Less(CareerTileLayout.StripNameKeepTagMin, CareerTileLayout.StripNameSize);
            Assert.AreEqual(CareerTileLayout.StripW - CareerTileLayout.NameWellLeft - CareerTileLayout.NameWellRight - 2f * CareerTileLayout.NameFieldInset
                - CareerTileLayout.NameInputPadLeft - CareerTileLayout.NameFieldCaretRoom, CareerTileLayout.NameFieldTextWidth);
            Assert.AreEqual(30f, CareerTileLayout.FitNameField(100f));
            Assert.AreEqual(30f, CareerTileLayout.FitNameField(CareerTileLayout.NameFieldTextWidth));
            Assert.AreEqual(15f, CareerTileLayout.FitNameField(10000f), "не нижче 15");
            Assert.AreEqual((float)System.Math.Floor(30f * CareerTileLayout.NameFieldTextWidth / 500f), CareerTileLayout.FitNameField(500f));
            Assert.IsNaN(CareerTileLayout.FitNameField(float.NaN));
        }

        /// <summary>
        /// Довге ім'я вбудовано в екрани (ПМ 14.11.2026): стрічки героя й суперника вміщають ім'я правилом FitStripName з мір кожної
        /// розкладки (лялька — перешкода героя), числа стрічки — з моделі; поле створення меншає за FitNameField із межею довжини.
        /// </summary>
        [Test]
        public void Sources_LongName_AreWired()
        {
            string ui = Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Ring", "UI");
            string show = File.ReadAllText(Path.Combine(ui, "Career", "ShowScreens.cs"));
            string create = File.ReadAllText(Path.Combine(ui, "Career", "CreateScreenView.cs"));
            string kit = File.ReadAllText(Path.Combine(ui, "Career", "ArenaKit.cs"));
            var bad = new List<string>();
            foreach (var l in new[] {
                "var name = ArenaKit.Big(v.RingName, CareerTileLayout.StripNameSize, RingTheme.Text);",
                "ArenaKit.Place(name, CareerTileLayout.StripNameLeft, CareerTileLayout.StripNameTop, null, null);",
                "ArenaKit.Place(tag, null, CareerTileLayout.StripTagTop, CareerTileLayout.StripTagRight, null);",
                "ArenaKit.FitNameAndTag(box, name, tag, gimmick, true, doll, belt, belt != null ? \" · \" + t.Get(\"c.champion.short\") : null);",
                "if (v.IsChampion) { belt = ArenaKit.Tag(\" · \" + t.Get(\"c.champion\"), RingTheme.Gold); tag.Add(belt); }",
                "ArenaKit.Place(zones, CareerTileLayout.StripNameLeft, CareerTileLayout.StripZonesTop, null, null);",
                "ArenaKit.Place(doll, null, CareerTileLayout.StripDollTop, CareerTileLayout.StripDollRight, null);",
                "var nl = ArenaKit.Big(nick, CareerTileLayout.StripNameSize, RingTheme.Text);",
                "ArenaKit.Place(nl, null, CareerTileLayout.StripNameTop, CareerTileLayout.StripNameLeft, null);",
                "ArenaKit.FitNameAndTag(box, nl, tag, rg, false);",
                "ArenaKit.Place(real, null, CareerTileLayout.StripZonesTop, CareerTileLayout.StripNameLeft, null);" })
                if (!CodeLine(show, l)) bad.Add("ShowScreens: немає «" + l + "»");
            int dollAt = show.IndexOf("var doll = new InjuryDoll();"), fitAt = show.IndexOf("ArenaKit.FitNameAndTag(box, name, tag, gimmick, true, doll, belt,");
            if (dollAt < 0 || fitAt < 0 || dollAt > fitAt) bad.Add("ShowScreens: лялька має бути створена до FitNameAndTag (перешкода імені)");
            foreach (var l in new[] {
                "public static void FitNameAndTag(VisualElement box, Label name, VisualElement tag, VisualElement optional, bool nameLeft, VisualElement obstacle = null,",
                "Label belt = null, string beltShort = null)",
                "var probe = Big(text, CareerTileLayout.StripNameSize, RingTheme.Text);",
                "probe.style.visibility = Visibility.Hidden;",
                "var f = CareerTileLayout.FitStripName(text, measure, avail);",
                "if (beltCut != null && beltCut != beltFull) steps.Add(2);",
                "if (belt != null) steps.Add(3);",
                "string beltCut = belt != null && !string.IsNullOrEmpty(beltShort) ? RingTheme.Caps(beltShort) : null;",
                "if (belt != null && state < 3 && !float.IsNaN(belt.layout.width)) beltW = belt.layout.width + (state == 2 ? cut : 0f);",
                "float tagAll = t.width + Given(state, opt, cut, bw);",
                "float tagK = tagAll - Given(steps[k], opt, cut, bw);",
                "avail[k] = nameLeft ? Math.Min(t.xMax - tagK, ob) - CareerTileLayout.StripNameGap - n.x",
                ": n.xMax - (t.x + tagK) - CareerTileLayout.StripNameGap;",
                "int next = steps[f.Drop];",
                "belt.style.display = state >= 3 ? DisplayStyle.None : DisplayStyle.Flex;",
                "belt.text = state == 2 ? beltCut : beltFull;",
                "return (step >= 1 ? gimmick : 0f) + (step == 2 ? beltCut : step >= 3 ? belt : 0f);",
                "name.style.fontSize = f.Size;",
                "name.style.letterSpacing = new Length(f.Size * CareerTileLayout.StripNameTrack);",
                "name.style.whiteSpace = WhiteSpace.NoWrap;",
                "name.style.width = StyleKeyword.Auto;",
                "string key = f.Size + \"|\" + next + \"|\" + f.Text;",
                "float ob = obstacle != null && !float.IsNaN(obstacle.layout.x) ? obstacle.layout.x : float.MaxValue;",
                "if (optional != null) optional.style.display = state >= 1 ? DisplayStyle.None : DisplayStyle.Flex;",
                "string[] lines = f.Text.Split('\\n');",
                "name.text = lines[0];",
                "var line2 = Big(\"\", CareerTileLayout.StripNameSize, RingTheme.Text);",
                "line2.style.display = lines.Length > 1 ? DisplayStyle.Flex : DisplayStyle.None;",
                "line2.text = lines[1];",
                "line2.style.fontSize = f.Size;",
                "line2.style.top = n.y + CareerTileLayout.StripNameLine2Offset(f.Size);",
                "if (nameLeft) { line2.style.left = n.x; line2.style.right = StyleKeyword.Auto; }",
                "else { line2.style.right = box.layout.width - n.xMax; line2.style.left = StyleKeyword.Auto; }",
                "box.RegisterCallback(fit);", "tag.RegisterCallback(fit);", "probe.RegisterCallback(fit);",
                "style.width = CareerTileLayout.StripDollW; style.height = CareerTileLayout.StripDollH;" })
                if (!CodeLine(kit, l)) bad.Add("ArenaKit: немає «" + l + "»");
            if (Regex.IsMatch(kit, @"FitNameAndTag\([^)]*float size")) bad.Add("ArenaKit: старий ланцюг кроків FitNameAndTag(…, size, minSize) повернувся");
            foreach (var l in new[] {
                "var well = ArenaKit.Abs(CareerTileLayout.NameWellLeft, 36f, CareerTileLayout.NameWellRight, null, null, 58f);",
                "var field = new TextField { value = _d.RingName, maxLength = _v.MaxNameLength };",
                "field.style.left = CareerTileLayout.NameFieldInset; field.style.right = CareerTileLayout.NameFieldInset;",
                "var probe = RingUi.Lbl(\"\", CareerTileLayout.NameFieldSize, RingTheme.Text);",
                "RingFonts.Apply(probe, RingFontRole.TextBold);",
                "field.RegisterValueChangedCallback(e => { _d.RingName = e.newValue; FitNameText(field, probe); });",
                "well.RegisterCallback<GeometryChangedEvent>(e => FitNameText(field, probe));",
                "input.style.fontSize = CareerTileLayout.NameFieldSize;",
                "input.style.paddingLeft = CareerTileLayout.NameInputPadLeft;",
                "float size = nat <= 0f ? CareerTileLayout.NameFieldSize : CareerTileLayout.FitNameField(nat);",
                "if (!float.IsNaN(size)) input.style.fontSize = size;" })
                if (!CodeLine(create, l)) bad.Add("CreateScreenView: немає «" + l + "»");
            // варіант афіші «роль під назвою» — лише кадри огляду: вмикає й вимикає тільки RingMenuShots
            foreach (var f in Directory.GetFiles(Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Ring"), "*.cs", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(f) == "RingMenuShots.cs") continue;
                foreach (var l in File.ReadAllText(f).Split('\n'))
                    if (l.Contains("PosterRoleBelowForShots = true")) bad.Add(Path.GetFileName(f) + ": вмикає варіант огляду «роль під назвою» поза RingMenuShots");
            }
            // і сама RingMenuShots вимикає його навіть після винятку в кадрі (рев'ю 14.11.2026)
            string shots = File.ReadAllText(Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Ring", "RingMenuShots.cs"));
            if (!shots.Contains("finally { UI.CreateScreenView.PosterRoleBelowForShots = false; }"))
                bad.Add("RingMenuShots.cs: варіант «роль під назвою» не вимикається в finally");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        /// <summary>
        /// Базова лінія назви афіші (У0 арт-директора 14.11.2026): назва кегля s, прибита до верху рядка зі зсувом
        /// <see cref="CareerTileLayout.PosterNameTopShift"/>, має базову лінію там само, де назва 52 (висхідна × кегль від верху тексту).
        /// </summary>
        [Test]
        public void PosterName_SmallerSize_KeepsTheBaselineOf52()
        {
            float b52 = CareerTileLayout.PosterNameAscent * CareerTileLayout.PosterNameSize;
            for (float s = CareerTileLayout.PosterNameMinSize; s <= CareerTileLayout.PosterNameSize; s += 1f)
                Assert.AreEqual(b52, CareerTileLayout.PosterNameTopShift(s) + CareerTileLayout.PosterNameAscent * s, 0.01f, "кегль " + s);
            Assert.AreEqual(0f, CareerTileLayout.PosterNameTopShift(CareerTileLayout.PosterNameSize), "на 52 — без зсуву");
            Assert.AreEqual(0f, CareerTileLayout.PosterNameTopShift(float.NaN), "до розкладки — без зсуву");
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
                    string expect = T.Format("dev.attr.buy", s.AttrCost(a)) + " — " + T.Get("c.dev.attr.why." + i);
                    // Б5: з сезону 4 у витривалості — чому дорожча
                    if (a == RingAttr.Stamina && s.State.Season >= Ageing.FromSeason) expect = CareerScreenLines.WithReason(expect, T.Format("c.age.attr", 1));
                    Assert.AreEqual(expect, hint, "сезон " + s.State.Season + ", " + a);
                }
            StringAssert.StartsWith(T.Format("dev.attr.buy", 3), CareerScreenLines.AttrBuyHint(T, s4, RingAttr.Stamina, null), "сезон 4: 3 оч.");
            StringAssert.StartsWith(T.Format("dev.attr.buy", 2), CareerScreenLines.AttrBuyHint(T, s4, RingAttr.Strength, null), "сила — як і була");
            StringAssert.StartsWith(T.Format("dev.attr.buy", 2), CareerScreenLines.AttrBuyHint(T, s1, RingAttr.Stamina, null), "сезон 1 — стара ціна");
            string no = CareerScreenLines.AttrBuyHint(T, s4, RingAttr.Stamina, "points");
            StringAssert.EndsWith(" · " + T.Get("dev.reason.points"), no, "купити не можна — чому");
            StringAssert.StartsWith(T.Format("dev.attr.buy", 3), no);
            StringAssert.EndsWith(" · тіло старіє: +1 оч.", CareerScreenLines.AttrBuyHint(T, s4, RingAttr.Stamina, null), "Б5: хвіст — текст У0");
            StringAssert.Contains("тіло старіє: +1 оч. · " + T.Get("dev.reason.points"), no, "хвіст старіння — перед причиною, крапка «оч.» на місці");
            StringAssert.DoesNotContain("тіло старіє", CareerScreenLines.AttrBuyHint(T, s1, RingAttr.Stamina, null), "сезон 1 — без хвоста");
            StringAssert.DoesNotContain("тіло старіє", CareerScreenLines.AttrBuyHint(T, s4, RingAttr.Strength, null), "сила не дорожчає — без хвоста");

            // магазин тренерів: найнятий — прогрес із кроком цього тренера, ні — плата; підказка — крок цього тренера
            string sName = RingLabels.AttrName(T, RingAttr.Stamina), oName = RingLabels.AttrName(T, CareerRules.Coaches[other]);
            s4.State.Coach = stamina; s4.State.CoachProgress = 2;
            Assert.AreEqual(T.Format("c.shop.coach.hired", sName, 2, CareerRules.CoachProgressStep + 1), CareerScreenLines.CoachButton(T, s4, stamina));
            StringAssert.Contains("2/4", CareerScreenLines.CoachButton(T, s4, stamina), "сезон 4: до +1 витривалості — 4 тренування");
            Assert.AreEqual(CareerScreenLines.WithReason(T.Format("c.shop.coach.desc", sName, CareerRules.CoachProgressStep + 1), T.Format("c.age.coach", 1)),
                CareerScreenLines.CoachHint(T, s4, stamina));
            StringAssert.EndsWith(" · тіло старіє: +1 тренування", CareerScreenLines.CoachHint(T, s4, stamina), "Б5: хвіст — текст У0");
            Assert.AreEqual(T.Format("c.shop.coach", oName, CareerRules.CoachWeeklyFee), CareerScreenLines.CoachButton(T, s4, other), "не найнятий — плата");
            Assert.AreEqual(T.Format("c.shop.coach.desc", oName, CareerRules.CoachProgressStep), CareerScreenLines.CoachHint(T, s4, other), "інший тренер — як і був");
            s1.State.Coach = stamina; s1.State.CoachProgress = 2;
            StringAssert.Contains("2/3", CareerScreenLines.CoachButton(T, s1, stamina), "сезон 1 — стара ціна");
            NoMissingKey(CareerScreenLines.CoachHint(T, s1, stamina), "підказка тренера");
            StringAssert.DoesNotContain("тіло старіє", CareerScreenLines.CoachHint(T, s1, stamina), "сезон 1 — без хвоста");
            // сезон 7: крок тренера витривалості — 5, «за 5 тренувань» (не «тренування»), надбавка +2
            var s7 = InSeason(s1, 7);
            Assert.AreEqual(CareerRules.CoachProgressStep + 2, s7.CoachStepFor(stamina), "передумова: сезон 7 — +2");
            string h7 = CareerScreenLines.CoachHint(T, s7, stamina);
            NoMissingKey(h7, "підказка тренера, сезон 7");
            StringAssert.Contains("За 5 тренувань", h7);
            StringAssert.EndsWith(" · тіло старіє: +2 тренування", h7);
            StringAssert.Contains("За 4 тренування", CareerScreenLines.CoachHint(T, s4, stamina), "4 — «тренування»");

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
            int report = show.IndexOf("private VisualElement Report(SeasonReport rep, bool column)", refresh < 0 ? 0 : refresh);
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
            foreach (Match m in Regex.Matches(show, @"private VisualElement DevPlate\((bool column)?\)\s*\{"))
            {
                n++;
                int end = show.IndexOf("return box;", m.Index);
                string body = end < 0 ? "" : show.Substring(m.Index, end - m.Index);
                int foot = body.IndexOf("= DevFooter("), panel = body.IndexOf("moreTo: foot)"), scroll = body.IndexOf(".Add(scroll);");
                int hint = body.IndexOf("DevFooterHint(foot);"), footAdd = body.IndexOf(".Add(foot);");
                if (foot < 0 || panel < foot || scroll < panel || hint < scroll || footAdd < hint)
                    bad.Add("DevPlate №" + n + ": не «підвал → панель із moreTo → прокрутка → підказка в підвал → підвал після прокрутки»");
                if (!body.Contains("scroll.style.flexShrink = 1f;")) bad.Add("DevPlate №" + n + ": прокрутка не стискається");
                // кінець сезону — через DevMaxH: 16:9 — DevPlateMaxH від верху плити, 4:3 — місце під вкладками колонки
                if (!body.Contains("CareerTileLayout.DevPlateMaxH(") && !body.Contains("box.style.maxHeight = DevMaxH(Root.layout.height);"))
                    bad.Add("DevPlate №" + n + ": плита без межі до канатів");
                if (body.Contains("scroll.style.maxHeight = 370f")) bad.Add("DevPlate №" + n + ": стара стала висота прокрутки кінця сезону");
                if (!body.Contains("scroll.style.minHeight = CareerTileLayout.DevScrollMinH;")) bad.Add("DevPlate №" + n + ": найменша прокрутка не з моделі");
                // щойно розгорнутий «Усе» — до початку списку (ScrollToDevAll), інакше — та сама прокрутка після перебудови (огляд слоту 31)
                int to = body.IndexOf("KeepDevScroll(scroll, ");
                if (to < footAdd) bad.Add("DevPlate №" + n + ": після підвалу немає прокрутки до щойно розгорнутого списку чи до колишнього місця");
            }
            if (n != 2) bad.Add("DevPlate: плит розвитку на екранах показу — дві, знайдено " + n);
            // межа від справжньої висоти панелі — і при зміні розміру (кінець сезону), і при зміні верху на вузькій панелі (підсумок)
            if (!CodeLine(show, "float w = Root.layout.width, h = Root.layout.height;")
                || !CodeLine(show, "if (_devBox != null) _devBox.style.maxHeight = DevMaxH(h);")
                || !CodeLine(show, "return _single ? CareerTileLayout.SeasonColumnRoom(panelH) : CareerTileLayout.DevPlateMaxH(CareerTileLayout.SeasonDevTop, panelH);"))
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
            // Б4-1: два ряди ґіміків («tall») — і на вузькій панелі, і з п'ятим ґіміком на будь-якій
            if (!Regex.IsMatch(create, @"bool tall = narrow \|\| eight;") || !Regex.IsMatch(create, @"bool paper = tall && _paperMoves != null;"))
                bad.Add("CreateScreenView: прийоми на папері не залежать від двох рядів (вузька панель чи Б4-1)");
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

        // ---------------- стрічка дії тижня (АД 15.11.2026, варіант (А)) ----------------

        /// <summary>Результат з усіма дельтами разом — синтетичний найгірший випадок стрічки (у грі одночасно їх не буває).</summary>
        internal static WeekActionResult WeekStripWorst(WeekActionKind kind = WeekActionKind.Backstage)
        {
            return new WeekActionResult
            {
                Kind = kind, Ok = true, HasRoll = true, Roll = 12, Modifier = -9, Total = 3, Tier = RollTier.Miss,
                MoneyDelta = -9999, DevPointsDelta = 99, TrustDelta = -99, FeudDelta = 99, HypeBonusDelta = -99, RivalBoosted = true,
                InjuredZone = 1, PopularityDelta = -99, LockerDelta = -99, ActionsDelta = -9, PlanChanged = true, OpponentChanged = true,
                CoachRaised = true, TurnedTo = RingPath.Rebel, NeedsChoice = true, RumorKey = "rumor.next_opponent",
            };
        }

        /// <summary>
        /// Порядок дельт — одна стала таблиця (АД 15.11.2026): що важливіше, те лівіше, однаково для всіх дій. Зміна порядку — рішення
        /// директора, а не побічна правка: таблиця тут закріплена поіменно, кожна дельта в ній рівно раз, а рядок 2 стрічки йде саме нею.
        /// </summary>
        [Test]
        public void WeekStrip_DeltaOrder_IsOneStableTable_ForEveryAction()
        {
            var pinned = new[]
            {
                CareerScreenLines.WeekDelta.Injury, CareerScreenLines.WeekDelta.Turn, CareerScreenLines.WeekDelta.Money,
                CareerScreenLines.WeekDelta.DevPoints, CareerScreenLines.WeekDelta.Trust, CareerScreenLines.WeekDelta.Popularity,
                CareerScreenLines.WeekDelta.Feud, CareerScreenLines.WeekDelta.Hype, CareerScreenLines.WeekDelta.Rival,
                CareerScreenLines.WeekDelta.Opponent, CareerScreenLines.WeekDelta.Plan, CareerScreenLines.WeekDelta.Locker,
                CareerScreenLines.WeekDelta.Actions, CareerScreenLines.WeekDelta.Coach, CareerScreenLines.WeekDelta.PromoChoice,
            };
            CollectionAssert.AreEqual(pinned, CareerScreenLines.WeekDeltaOrder, "порядок важливості змінено — спершу У0 директора");
            var all = (CareerScreenLines.WeekDelta[])System.Enum.GetValues(typeof(CareerScreenLines.WeekDelta));
            Assert.AreEqual(all.Length, CareerScreenLines.WeekDeltaOrder.Length, "кожна дельта — у таблиці");
            CollectionAssert.AllItemsAreUnique(CareerScreenLines.WeekDeltaOrder);
            foreach (WeekActionKind kind in System.Enum.GetValues(typeof(WeekActionKind)))
            {
                var r = WeekStripWorst(kind);
                var expect = new List<string>();
                foreach (var d in pinned) expect.Add(CareerScreenLines.WeekDeltaText(T, r, d, "Сталевий", false));
                var got = CareerScreenLines.WeekResultDeltas(T, r, "Сталевий", false);
                CollectionAssert.AreEqual(expect, got, kind + ": дельти не в порядку таблиці");
                foreach (var s in got) NoMissingKey(s, kind + ": дельта");
            }
            // дельти — ті самі рядки, що й у колишньому відгуку: поворот у роді героя, суперник Б6, травма — зоною
            var w = WeekStripWorst();
            Assert.AreEqual(CareerScreenLines.TurnLine(T, w, true), CareerScreenLines.WeekDeltaText(T, w, CareerScreenLines.WeekDelta.Turn, "Сталевий", true));
            Assert.AreEqual(CareerScreenLines.OpponentLine(T, w, "Сталевий"), CareerScreenLines.WeekDeltaText(T, w, CareerScreenLines.WeekDelta.Opponent, "Сталевий", false));
            Assert.AreEqual(T.Format("c.delta.injury", T.Get("bodyzone.1")), CareerScreenLines.WeekDeltaText(T, w, CareerScreenLines.WeekDelta.Injury, null, false));
            Assert.AreEqual(T.Format("c.delta.money", "−9999"), CareerScreenLines.WeekDeltaText(T, w, CareerScreenLines.WeekDelta.Money, null, false));
            Assert.IsEmpty(CareerScreenLines.WeekResultDeltas(T, new WeekActionResult { Ok = true }, null, false), "дельт немає — рядка 2 немає");
        }

        /// <summary>
        /// Рядок 1 — «Дія · Результат» словом (АД 15.11.2026): рівень кидка з порогом у слові, без арифметики (вона — у підказці);
        /// без кидка — «Зроблено»; відмова — лише дія, а причина ядра — рядком 2.
        /// </summary>
        [Test]
        public void WeekStrip_Head_IsActionAndResultInWords_TheRollOnlyInTheHover()
        {
            var r = new WeekActionResult { Kind = WeekActionKind.Promo, Ok = true, HasRoll = true, Roll = 5, Modifier = 1, Total = 6, Tier = RollTier.Miss };
            Assert.AreEqual(T.Get("week.action.2") + " · " + T.Get("tier.miss"), CareerScreenLines.WeekResultHead(T, r));
            StringAssert.Contains("(до 6)", CareerScreenLines.WeekResultHead(T, r), "поріг лишається в слові");
            StringAssert.DoesNotContain("=", CareerScreenLines.WeekResultHead(T, r), "арифметика кидка — лише в підказці");
            Assert.AreEqual(T.Format("c.roll", 5, RingChances.Term(1), 6, T.Get("tier.miss")), CareerScreenLines.WeekResultRoll(T, r));
            var done = new WeekActionResult { Kind = WeekActionKind.Training, Ok = true, DevPointsDelta = 2 };
            Assert.AreEqual(T.Get("week.action.0") + " · " + T.Get("c.week.result.done"), CareerScreenLines.WeekResultHead(T, done));
            Assert.IsNull(CareerScreenLines.WeekResultRoll(T, done), "без кидка арифметики немає");
            var no = new WeekActionResult { Kind = WeekActionKind.SideJob, Ok = false, Reason = "exclusive" };
            Assert.AreEqual(T.Get("week.action.4"), CareerScreenLines.WeekResultHead(T, no));
            CollectionAssert.AreEqual(new[] { T.Get("week.reason.exclusive") }, CareerScreenLines.WeekResultDeltas(T, no, null, false));
            Assert.IsNull(CareerScreenLines.WeekResultRoll(T, no));
            NoMissingKey(CareerScreenLines.WeekResultHead(T, done), "c.week.result.done");
            // кидок підробітку — лише перевірка травми: результат «Зроблено», не «Провал»
            var job = new WeekActionResult { Kind = WeekActionKind.SideJob, Ok = true, HasRoll = true, Roll = 5, Total = 5, Tier = RollTier.Miss, MoneyDelta = 40 };
            Assert.AreEqual(T.Get("week.action.4") + " · " + T.Get("c.week.result.done"), CareerScreenLines.WeekResultHead(T, job));
            Assert.IsNotNull(CareerScreenLines.WeekResultRoll(T, job), "кидок підробітку — у підказці");
        }

        /// <summary>
        /// Смуга стрічки — Gold, коли дія вдалась, Signal — коли ні (АД 15.11.2026). «Ok» ядра — лише «дію дозволено»: провал кидка, що
        /// вирішує результат (промо, закулісся), — не вдалась; частковий успіх і дії без кидка — вдались; кидок підробітку (травма) колір не
        /// міняє; відмова — ні.
        /// </summary>
        [Test]
        public void WeekStrip_Stripe_GoldOnlyWhenTheActionSucceeded()
        {
            foreach (var kind in new[] { WeekActionKind.Promo, WeekActionKind.Backstage })
            {
                Assert.IsFalse(CareerScreenLines.WeekResultSucceeded(new WeekActionResult { Kind = kind, Ok = true, HasRoll = true, Tier = RollTier.Miss }), kind + ": провал");
                Assert.IsTrue(CareerScreenLines.WeekResultSucceeded(new WeekActionResult { Kind = kind, Ok = true, HasRoll = true, Tier = RollTier.Partial }), kind + ": частково");
                Assert.IsTrue(CareerScreenLines.WeekResultSucceeded(new WeekActionResult { Kind = kind, Ok = true, HasRoll = true, Tier = RollTier.Strong }), kind + ": успіх");
                Assert.IsTrue(CareerScreenLines.WeekResultSucceeded(new WeekActionResult { Kind = kind, Ok = true }), kind + ": без кидка (вибір нагороди, сцена)");
            }
            Assert.IsTrue(CareerScreenLines.WeekResultSucceeded(new WeekActionResult { Kind = WeekActionKind.SideJob, Ok = true, HasRoll = true, Tier = RollTier.Miss, InjuredZone = 2 }),
                "підробіток заробив — вдався; травма — словом у дельті");
            Assert.IsTrue(CareerScreenLines.WeekResultSucceeded(new WeekActionResult { Kind = WeekActionKind.Recovery, Ok = true }));
            Assert.IsFalse(CareerScreenLines.WeekResultSucceeded(new WeekActionResult { Kind = WeekActionKind.Training, Ok = false, Reason = "money" }), "відмова");
            Assert.IsFalse(CareerScreenLines.WeekResultSucceeded(null));
        }

        /// <summary>
        /// «· ще N» ховає найменш важливі (кінець таблиці), а не будь-які: показано завжди найдовший префікс, що влазить разом із хвостом;
        /// влазить усе — хвоста немає; не влазить нічого — лише «ще N». Підказка — сховане: спершу арифметика кидка, далі сховані дельти
        /// тим самим порядком, і те саме «ще N», коли й підказці тісно.
        /// </summary>
        [Test]
        public void WeekStrip_More_HidesTheLeastImportant_AndTheHoverShowsOnlyTheHidden()
        {
            var parts = new List<string> { "аа", "бб", "вв", "гг" };
            string line;
            Assert.AreEqual(4, CareerScreenLines.FitParts(T, parts, s => true, out line));
            Assert.AreEqual("аа · бб · вв · гг", line);
            Assert.AreEqual(2, CareerScreenLines.FitParts(T, parts, s => s.Length <= 16, out line));
            Assert.AreEqual("аа · бб · " + T.Format("c.week.more", 2), line, "сховано два останні — найменш важливі");
            Assert.AreEqual(0, CareerScreenLines.FitParts(T, parts, s => false, out line));
            Assert.AreEqual(T.Format("c.week.more", 4), line);
            NoMissingKey(line, "c.week.more");
            // стрічка: найважливіша за «ще N» не ховається — не влазить і з хвостом, то її кінець обрізано «…», хвіст лишається, а сховане — усе
            bool clipped;
            Assert.AreEqual(2, CareerScreenLines.StripParts(T, parts, s => s.Length <= 16, out line, out clipped));
            Assert.IsFalse(clipped);
            Assert.AreEqual("аа · бб · " + T.Format("c.week.more", 2), line);
            var longFirst = new List<string> { "абвгде", "бб" };
            Assert.AreEqual(0, CareerScreenLines.StripParts(T, longFirst, s => s.Length <= 10, out line, out clipped));
            Assert.IsTrue(clipped);
            Assert.AreEqual("аб… · " + T.Format("c.week.more", 1), line, "кінець найважливішої — «…», хвіст «ще N» — на місці");
            Assert.AreEqual(0, CareerScreenLines.StripParts(T, new List<string> { "абвгде" }, s => s.Length <= 3, out line, out clipped));
            Assert.AreEqual("аб…", line, "одна дельта — без хвоста");
            Assert.AreEqual(0, CareerScreenLines.StripParts(T, parts, s => false, out line, out clipped));
            Assert.AreEqual("… · " + T.Format("c.week.more", 3), line);
            Assert.AreEqual(0, CareerScreenLines.StripParts(T, new List<string>(), s => false, out line, out clipped));
            Assert.IsFalse(clipped, "дельт немає — нічого обрізати");
            // підказка: кидок першим, далі сховане в порядку стрічки
            var hidden = new List<string> { "вввввв", "гггггг" };
            Assert.AreEqual("кидок · вввввв · гггггг", CareerScreenLines.WeekResultHover(T, "кидок", hidden, s => true));
            Assert.AreEqual("кидок · вввввв · " + T.Format("c.week.more", 1), CareerScreenLines.WeekResultHover(T, "кидок", hidden, s => s.Length <= 21));
            Assert.AreEqual("вввввв · гггггг", CareerScreenLines.WeekResultHover(T, null, hidden, s => true), "без кидка — лише сховані дельти");
            Assert.IsNull(CareerScreenLines.WeekResultHover(T, null, new List<string>(), s => true), "нічого не сховано — підказки немає");
        }

        /// <summary>
        /// Чутка (голос зали, не букера — «Чутка: …», АД 15.11.2026) стоїть у рядку-підказці шапки до наступної дії; вибір нагороди промо
        /// й перевтома — інструкція й попередження — важливіші й лишаються першими; чутки немає — рядок порожній.
        /// </summary>
        [Test]
        public void WeekHint_Default_TheRumorUnlessPromoChoiceOrStrain()
        {
            var r = new WeekActionResult { Ok = true, RumorKey = "rumor.defense" };
            string rumor = CareerScreenLines.WeekRumor(T, r);
            Assert.AreEqual(T.Format("c.week.rumor", T.Get("rumor.defense")), rumor);
            StringAssert.StartsWith("Чутка: ", rumor);
            NoMissingKey(rumor, "c.week.rumor");
            Assert.IsNull(CareerScreenLines.WeekRumor(T, new WeekActionResult { Ok = true }));
            Assert.IsNull(CareerScreenLines.WeekRumor(T, new WeekActionResult { Ok = false, RumorKey = "rumor.defense" }), "відмова чуток не приносить");
            Assert.IsNull(CareerScreenLines.WeekRumor(T, null));
            Assert.AreEqual(rumor, CareerScreenLines.WeekHintDefault(null, null, rumor));
            Assert.AreEqual("п", CareerScreenLines.WeekHintDefault("п", null, rumor));
            Assert.AreEqual("с", CareerScreenLines.WeekHintDefault(null, "с", rumor));
            Assert.AreEqual("п с", CareerScreenLines.WeekHintDefault("п", "с", rumor));
            Assert.AreEqual("", CareerScreenLines.WeekHintDefault(null, null, null));
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
            if (!CodeLine(create, "tile.SelectAsEdge = true;")) bad.Add("CreateScreenView: роль не обирається краєм (№4: «ЛИХОДІЙ» — як «ГЕРОЙ», край свого кольору)");
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
            // поворот ролі й суперник Б6 — серед дельт стрічки (CareerScreenLines.WeekDeltaText; тест WeekStrip_DeltaOrder_…)
            if (!week.Contains("CareerScreenLines.WeekResultDeltas(T, r, OpponentName(), HeroForms.ShowsHeroine(T))")) bad.Add("WeekScreenView: відгук без повороту ролі");
            if (!show.Contains("if (CareerScreenLines.LateRoleShown(C, CareerScreenLines.SeasonEndActions(C))) LateRoleBlock(col);")) bad.Add("ShowScreens: немає вибору пізньої ролі");
            // правки слоту 31
            // наліпку рахує CareerTileLayout.DrawerOverPoster (через DrawerRoomUnderPoster; тест WeekDrawer_FoldDecision_…) — вид лише передає її
            if (!week.Contains("_posterSticker, _dev, out fold);")) bad.Add("WeekScreenView: шухляда не рахує наліпку (правка 1)");
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

        // ---------------- огляд слоту 33 (ПМ 13.11.2026) ----------------

        /// <summary>
        /// П. 1 (ВИСОКА): шухляда тижня на 4:3 лізла на картку шоу з трьома рядками умов — текст крізь текст (s33_w_p11_star_4x3 006–011).
        /// Рішення ПМ: «верх шухляди не вище за низ картки (із зазором), решта — прокруткою»; коротку шухляду в М1 прийнято. Числа — з
        /// кадрів 4:3 (1024×768, ×1,172): низ шухляди 539, низ афіші p7 ≈ 352, p11 ≈ 380. Колишній відкат «менше 150 — до стрічки
        /// суперника» давав p11 шухляду 376 і верх на 163 — над карткою.
        /// </summary>
        [Test]
        public void WeekDrawer_NeverClimbsOverThePoster()
        {
            const float bottomY = 539f;
            foreach (bool dev in new[] { true, false })
            foreach (float posterBottom in new[] { 250f, 300f, 352f, 380f, 400f, 430f, 470f, 520f })
            foreach (bool sticker in new[] { false, true })
            {
                string at = (dev ? "розвиток" : "витрати") + ", низ афіші " + posterBottom + (sticker ? " + наліпка" : "");
                float room = CareerTileLayout.DrawerRoomUnderPoster(bottomY, posterBottom, sticker);
                bool fold;
                float h = CareerTileLayout.DrawerMaxHeight(room, dev, out fold);
                Assert.LessOrEqual(h, CareerTileLayout.DrawerMaxH, at);
                if (fold) { Assert.Less(room, CareerTileLayout.DrawerNeed(dev), at + ": згортання — лише коли не влазить мінімум"); continue; }
                float top = bottomY - h, posterLow = posterBottom + (sticker ? CareerTileLayout.PosterStickerHang : 0f);
                Assert.GreaterOrEqual(top + 0.01f, posterLow + CareerTileLayout.DrawerPosterGap, at + ": верх шухляди не вище за низ картки з просвітом");
                Assert.AreEqual(System.Math.Min(room, CareerTileLayout.DrawerMaxH), h, 0.01f, at + ": решта — прокруткою, місце під карткою не губиться");
            }
            bool f;
            float p11 = CareerTileLayout.DrawerMaxHeight(CareerTileLayout.DrawerRoomUnderPoster(bottomY, 380f, false), true, out f);
            Assert.IsFalse(f, "p11 4:3: коротка шухляда, картка ціла (ПМ: «коротку шухляду на 4:3 в М1 приймаю»)");
            Assert.Less(p11, 160f, "p11 4:3: шухляда коротка, а не до стрічки суперника (було 376)");
            Assert.GreaterOrEqual(p11, CareerTileLayout.DrawerNeed(true), "мінімум — шапка, рядок прокрутки, «Усе»");
            float tall = CareerTileLayout.DrawerMaxHeight(60f, true, out f);
            Assert.IsTrue(f, "картка з усіма рядками умов: мінімум не влазить — рядки умов згортаються");
            Assert.AreEqual(CareerTileLayout.DrawerNeed(true), tall, 0.01f, "до згортання — мінімум, наступний прохід рахує від згорнутої");
            Assert.AreEqual(CareerTileLayout.DrawerMaxH, CareerTileLayout.DrawerMaxHeight(1000f, true, out f), 0.01f, "широка панель — як було");
            Assert.IsFalse(f);
            float before = CareerTileLayout.DrawerMaxHeight(float.NaN, false, out f);
            Assert.IsFalse(f, "до розкладки — без згортання");
            Assert.AreEqual(CareerTileLayout.DrawerNeed(false), before, 0.01f, "до розкладки — мінімум, не NaN у стилі");
            Assert.GreaterOrEqual(CareerTileLayout.DrawerPosterGap, 20f, "«Витрати» впритул до рваного краю (s33 p11 012–015, p7 006–015)");
        }

        /// <summary>
        /// П. 1, кадр слоту 34 (a_s1 4:3): картка згортала рядок умов на проміжному проході розкладки (ряд дій ще не на місці) і так
        /// лишалась, хоч під повною карткою було ~198 од. Рішення — за повною висотою афіші, тож воно оборотне й не коливається.
        /// </summary>
        [Test]
        public void WeekDrawer_FoldDecision_IsStableAndReversible()
        {
            const float bottomY = 539f, details = 26f;
            bool f;
            // проміжний прохід: шухляда «нижче» на два ряди плиток — тісно, згортає
            CareerTileLayout.DrawerOverPoster(bottomY - 200f, 321f, false, details, false, true, out f);
            Assert.IsTrue(f, "тісно — згортає");
            // наступний прохід: розкладка на місці, афіша згорнута (низ 295) — повна (321) влазить: розгортає
            float h = CareerTileLayout.DrawerOverPoster(bottomY, 321f - details, true, details, false, true, out f);
            Assert.IsFalse(f, "місця під повною карткою досить — картка розгортається");
            Assert.AreEqual(bottomY - 321f - CareerTileLayout.DrawerPosterGap, h, 0.01f, "висота — від низу розгорнутої картки");
            // стійкість: рішення, застосоване до розкладки, на наступному проході те саме (обидва стани, усі розміри)
            foreach (bool dev in new[] { true, false })
            foreach (bool sticker in new[] { false, true })
            for (float full = 250f; full <= 520f; full += 5f)
            for (float d = 0f; d <= 120f; d += 20f)
            {
                bool f1, f2;
                CareerTileLayout.DrawerOverPoster(bottomY, full, false, d, sticker, dev, out f1);
                float h2 = CareerTileLayout.DrawerOverPoster(bottomY, f1 ? full - d : full, f1, d, sticker, dev, out f2);
                Assert.AreEqual(f1, f2, "коливання: повна " + full + ", рядки " + d);
                if (!f2) Assert.GreaterOrEqual(bottomY - h2 + 0.01f, full + (sticker ? CareerTileLayout.PosterStickerHang : 0f) + CareerTileLayout.DrawerPosterGap, "не на картці");
                else if (bottomY - (full - d) - (sticker ? CareerTileLayout.PosterStickerHang : 0f) - CareerTileLayout.DrawerPosterGap >= CareerTileLayout.DrawerNeed(dev))
                    Assert.GreaterOrEqual(bottomY - h2 + 0.01f, full - d + (sticker ? CareerTileLayout.PosterStickerHang : 0f) + CareerTileLayout.DrawerPosterGap, "згорнута — не на картці");
            }
            // згорнута — шухляда бере звільнене рядками місце: повна 390 (під нею 129 < 132), згорнута 364 — під нею 155
            h = CareerTileLayout.DrawerOverPoster(bottomY, 390f, false, details, false, true, out f);
            Assert.IsTrue(f, "під повною карткою не влазить мінімум — згортає");
            Assert.AreEqual(bottomY - (390f - details) - CareerTileLayout.DrawerPosterGap, h, 0.01f, "висота — від низу згорнутої картки");
            // висота рядків невідома (NaN) чи від'ємна — рахується нулем, а не ламає висоту до мінімуму
            foreach (float bad in new[] { float.NaN, -5f })
            {
                h = CareerTileLayout.DrawerOverPoster(bottomY, 300f, true, bad, false, true, out f);
                Assert.IsFalse(f, "рядки " + bad + ": місця досить — розгорнута");
                Assert.AreEqual(bottomY - 300f - CareerTileLayout.DrawerPosterGap, h, 0.01f, "рядки " + bad + " — нуль");
            }
            CareerTileLayout.DrawerOverPoster(bottomY, 321f, false, float.NaN, false, true, out f);
            Assert.IsFalse(f, "висота рядків ще невідома — без згортання за неї");
        }

        /// <summary>
        /// П. 1: шапка з дворядковою підказкою (при наведенні) не виштовхує підвал «Усе» на канати — прокрутка шухляди стискається до
        /// рядка; поки місця досить — звична <see cref="CareerTileLayout.DevScrollMinH"/>.
        /// </summary>
        [Test]
        public void WeekDrawer_ScrollMin_GivesWayToTheHeadAndFoot()
        {
            Assert.AreEqual(CareerTileLayout.DevScrollMinH, CareerTileLayout.DrawerScrollMin(139f, 40f, 36f), 0.01f, "місця досить");
            Assert.AreEqual(139f - 56f - 36f, CareerTileLayout.DrawerScrollMin(139f, 56f, 36f), 0.01f, "дворядкова підказка — менша прокрутка");
            Assert.AreEqual(CareerTileLayout.DrawerScrollMinTight, CareerTileLayout.DrawerScrollMin(90f, 56f, 36f), 0.01f, "не менше за рядок кнопок");
            Assert.AreEqual(CareerTileLayout.DevScrollMinH, CareerTileLayout.DrawerScrollMin(430f, float.NaN, 0f), 0.01f, "до розкладки — шапка за сталою");
            foreach (float max in new[] { 132f, 139f, 160f })
                Assert.LessOrEqual(CareerTileLayout.DrawerHeadH + CareerTileLayout.DrawerScrollMin(max, CareerTileLayout.DrawerHeadH, CareerTileLayout.DrawerFootH)
                    + CareerTileLayout.DrawerFootH, max + 0.01f, "мінімум шухляди вміщається в " + max);
        }

        /// <summary>
        /// П. 2: порожнє значення — без мітки-тавтології скрізь: «План букера: Плану немає», «Умови: Умов немає», «Умови матчу: Умов
        /// немає», «Напарник: Напарника немає» (картка шоу, рядок тижня, огляд ворожнечі) → «Плану букера немає», «Умов немає», «Без
        /// напарника». П. 4: «Очки розвитку». П. 5: «Тест — Тарас «Молот» Гребінь» (імена не відмінюються).
        /// </summary>
        [Test]
        public void EmptyValues_HaveNoTautologicalLabel()
        {
            Assert.AreEqual("Плану букера немає", CareerScreenLines.PlanLine(T, BookerPlan.None));
            foreach (BookerPlan p in System.Enum.GetValues(typeof(BookerPlan)))
            {
                string line = CareerScreenLines.PlanLine(T, p);
                NoMissingKey(line, p.ToString());
                if (p != BookerPlan.None) Assert.AreEqual(T.Get("c.card.plan") + ": " + RingLabels.PlanName(T, p), line);
                StringAssert.DoesNotContain(T.Get("plan.none"), line, p + ": «Плану немає» після мітки");
            }
            string none = T.Get("stip.none");
            Assert.AreEqual("Умов немає", none);
            Assert.AreEqual(none, CareerScreenLines.StipLine(T, T.Get("c.card.stip"), new[] { none }), "картка шоу");
            Assert.AreEqual(none, CareerScreenLines.StipLine(T, T.Get("feud.section.stip"), new List<string> { none }), "огляд ворожнечі");
            Assert.AreEqual(none, CareerScreenLines.StipLine(T, T.Get("c.card.stip"), new string[0]));
            Assert.AreEqual(none, CareerScreenLines.StipLine(T, T.Get("c.card.stip"), null));
            Assert.AreEqual(T.Get("feud.section.stip") + ": " + T.Get("stip.no_dq") + ", " + T.Get("stip.no_ally"),
                CareerScreenLines.StipLine(T, T.Get("feud.section.stip"), new[] { T.Get("stip.no_dq"), T.Get("stip.no_ally") }), "умови — з міткою");
            Assert.AreEqual("Без напарника", T.Get("feud.ally.no"));
            Assert.AreEqual("Очки розвитку: 5", T.Format("dev.points", 5));
            Assert.AreEqual("Тест — Тарас «Молот» Гребінь", T.Format("feud.versus", "Тест", "Тарас «Молот» Гребінь"));
            Assert.AreEqual("Тест — Тарас «Молот» Гребінь", T.Format("c.open.vs", "Тест", "Тарас «Молот» Гребінь"), "заголовок відкриття — так само");

            // тавтологія як правило: «мітка: значення», де значення — «X немає» з тим самим коренем, що й мітка
            var bad = new List<string>();
            foreach (var pair in new[] { new[] { "c.card.plan", "plan.none" }, new[] { "c.card.stip", "stip.none" }, new[] { "feud.section.stip", "stip.none" },
                new[] { "feud.section.ally", "feud.ally.no" } })
            {
                string label = T.Get(pair[0]), value = T.Get(pair[1]);
                if (value.EndsWith("немає") && value.Substring(0, 4).ToLowerInvariant() == label.Substring(0, 4).ToLowerInvariant()) bad.Add(pair[0] + " → " + pair[1]);
            }
            Assert.AreEqual(new List<string> { "c.card.plan → plan.none", "c.card.stip → stip.none", "feud.section.stip → stip.none" }, bad,
                "пари, які разом дають тавтологію (тому значення ставиться без мітки); напарник — «Без напарника»");

            string ui = Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Ring", "UI");
            string week = File.ReadAllText(Path.Combine(ui, "Career", "WeekScreenView.cs"));
            string show = File.ReadAllText(Path.Combine(ui, "Career", "ShowScreens.cs"));
            string feud = File.ReadAllText(Path.Combine(ui, "FeudScreenView.cs"));
            var src = new List<string>();
            if (!week.Contains("CareerScreenLines.PlanLine(T, c.Plan) + \" · \"") || week.Contains("T.Get(\"c.card.plan\") + \": \"")) src.Add("WeekScreenView: рядок тижня «План букера: Плану немає»");
            if (!CodeLine(show, "if (!c.IsFeud) col.Add(ShowKit.Bullet(CareerScreenLines.PlanLine(T, c.Plan)));") || show.Contains("T.Get(\"c.card.plan\") + \": \""))
                src.Add("ShowScreens: картка шоу «План букера: Плану немає»");
            if (!show.Contains("CareerScreenLines.StipLine(T, T.Get(\"c.card.stip\"), ") || show.Contains("T.Get(\"c.card.stip\") + \": \"")) src.Add("ShowScreens: картка шоу «Умови: Умов немає»");
            if (!feud.Contains("CareerScreenLines.StipLine(t, t.Get(\"feud.section.stip\"), _m.StipulationTexts())") || feud.Contains("t.Get(\"feud.section.ally\") + \": \"")
                || feud.Contains("t.Get(\"feud.section.stip\") + \": \""))
                src.Add("FeudScreenView: огляд ворожнечі «Умови матчу: Умов немає» / «Напарник: …»");
            Assert.IsEmpty(src, string.Join("\n", src));
        }

        /// <summary>
        /// П. 6: на афіші створення героїні — «Новачка» (s33_p1080 014–021 і ті самі кадри 16:10, 21:9, 4:3). Ім'я за замовчуванням іде
        /// за тілом в обидва боки; своє ім'я гравця не чіпаємо; огляд з -ring-hero-name ставить своє ім'я обом статям.
        /// </summary>
        [Test]
        public void Create_DefaultName_FollowsTheBody()
        {
            Assert.AreEqual("Новачок", T.Get("c.create.default_name"));
            Assert.AreEqual("Новачка", T.Get("c.create.default_name.fem"));
            Assert.AreEqual("Новачка", CareerScreenLines.FollowDefaultName(T, "Новачок", true));
            Assert.AreEqual("Новачок", CareerScreenLines.FollowDefaultName(T, "Новачка", false), "назад до героя");
            Assert.AreEqual("Новачка", CareerScreenLines.FollowDefaultName(T, "Новачка", true));
            Assert.AreEqual("Новачок", CareerScreenLines.FollowDefaultName(T, null, false));
            Assert.AreEqual("Буря", CareerScreenLines.FollowDefaultName(T, "Буря", true), "своє ім'я — як є");
            Assert.AreEqual("Буря", CareerScreenLines.FollowDefaultName(T, "Буря", false));
            var shot = RingText.FromJson(CareerKit.Read("strings_uk.json"), CareerKit.Read("ui_uk.json"));
            shot.Add("c.create.default_name", "Тест");
            shot.Add("c.create.default_name.fem", "Тест");
            Assert.AreEqual("Тест", CareerScreenLines.FollowDefaultName(shot, "Тест", true), "огляд з -ring-hero-name: ім'я огляду й героїні");

            string root = Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Ring");
            string create = File.ReadAllText(Path.Combine(root, "UI", "Career", "CreateScreenView.cs"));
            string menus = File.ReadAllText(Path.Combine(root, "RingGameBootstrap.Menus.cs"));
            var src = new List<string>();
            if (!CodeLine(create, "_d.RingName = CareerScreenLines.FollowDefaultName(T, _d.RingName, HeroForms.ShowsHeroine(T));")) src.Add("CreateScreenView: ім'я не йде за тілом");
            int refresh = create.IndexOf("public override void Refresh()", System.StringComparison.Ordinal);
            int follow = create.IndexOf("CareerScreenLines.FollowDefaultName(", System.StringComparison.Ordinal);
            int field = create.IndexOf("new TextField { value = _d.RingName", System.StringComparison.Ordinal);
            if (refresh < 0 || follow < refresh || field < follow) src.Add("CreateScreenView: ім'я за тілом — на початку Refresh, до поля імені");
            if (!menus.Contains("_text.Add(\"c.create.default_name.fem\", _shotHeroName)") || System.Text.RegularExpressions.Regex.Matches(menus, @"\bMirrorShotHeroName\(\);").Count < 2)
                src.Add("RingGameBootstrap.Menus: -ring-hero-name не доходить до героїні");
            Assert.IsEmpty(src, string.Join("\n", src));
        }

        /// <summary>
        /// П. 1, 3 і 7 — джерела екранів: шухляда тижня без відкату до стрічки й зі згортанням рядків картки; заголовок «АТРИБУТИ» у
        /// розгорнутому «Усе» кінцівки ворожнечі (k_ending_1080 155); клин титулу з темною смугою зі сталих моделі, підпис двома рядками.
        /// </summary>
        [Test]
        public void Sources_Slot33Fixes()
        {
            string ui = Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Ring", "UI");
            string week = File.ReadAllText(Path.Combine(ui, "Career", "WeekScreenView.cs"));
            string ending = File.ReadAllText(Path.Combine(ui, "EndingScreenView.cs"));
            string title = File.ReadAllText(Path.Combine(ui, "Menus", "TitleMenuView.cs"));
            string shapes = File.ReadAllText(Path.Combine(ui, "Menus", "MenuShapes.cs"));
            var bad = new List<string>();
            if (week.Contains("ArenaKit.StripH + 12f") || week.Contains("DrawerMinRoom") || week.Contains("if (room > 120f)")) bad.Add("WeekScreenView: шухляда знову відкочується до стрічки");
            if (!week.Contains("float max = CareerTileLayout.DrawerOverPoster(bottomY, _poster.layout.y + _poster.layout.height, _posterFolded, _detailsH,")
                || !CodeLine(week, "_drawer.style.maxHeight = max;")) bad.Add("WeekScreenView: висота шухляди не з моделі");
            if (!CodeLine(week, "if (fold != _posterFolded) FoldPoster(fold);")) bad.Add("WeekScreenView: картка не згортає (чи не розгортає) рядки умов");
            if (!CodeLine(week, "if (!_posterFolded && _posterDetails != null && !float.IsNaN(_posterDetails.layout.height)) _detailsH = _posterDetails.layout.height;"))
                bad.Add("WeekScreenView: висота схованих рядків не з розгорнутої афіші");
            if (!week.Contains("_posterDetails.style.display = fold ? DisplayStyle.None : DisplayStyle.Flex;")) bad.Add("WeekScreenView: згортання нічого не ховає");
            if (Regex.Matches(week, Regex.Escape("details.Add(Bullet(")).Count < 6 || Regex.Matches(week, Regex.Escape("col.Add(Bullet(")).Count > 0)
                bad.Add("WeekScreenView: рядки умов афіші — не в блоці, що згортається");
            if (!CodeLine(week, "_posterFolded = false;")) bad.Add("WeekScreenView: згортання не скидається з перебудовою");
            if (!CodeLine(week, "_detailsH = 0f;")) bad.Add("WeekScreenView: висота рядків старої афіші переживає перебудову");
            if (!week.Contains("CareerTileLayout.DrawerScrollMin(max, ")) bad.Add("WeekScreenView: прокрутка шухляди не поступається шапці");
            int all = ending.IndexOf("if (_all)", System.StringComparison.Ordinal);
            int attrs = all < 0 ? -1 : ending.IndexOf("dev.Add(RingUi.Section(t.Get(\"dev.section.attrs\")));", all, System.StringComparison.Ordinal);
            int perks = all < 0 ? -1 : ending.IndexOf("dev.Add(RingUi.Section(t.Get(\"dev.section.perks\")));", all, System.StringComparison.Ordinal);
            if (attrs < 0 || perks < 0 || attrs > perks) bad.Add("EndingScreenView: у «Усе» атрибути без заголовка «АТРИБУТИ»");
            if (!title.Contains("CareerTileLayout.TitleTagLines(T.Get(\"m.title.sub\"), rootW)")) bad.Add("TitleMenuView: підпис на вузькій панелі одним рядком");
            if (!shapes.Contains("ww * CareerTileLayout.WedgeInnerTop") || !shapes.Contains("ww * CareerTileLayout.WedgeInnerBottom")) bad.Add("MenuShapes: темна смуга клина не зі сталих моделі");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }
        // ---------------- Б4: вісім ґіміків, клавіші, «Вільні бали (+2)» (власник 13.11.2026; ПМ і арт-директор 14.11.2026) ----------------

        /// <summary>
        /// Б4: клавіші — ґіміки 1…кількість за місцем плитки; ролі 5 і 6, поки ґіміків не більше чотирьох (М1 без змін), з п'ятим —
        /// 9 і 0. Решта цифр нічого не робить; «0» на плитці — «0».
        /// </summary>
        [Test]
        public void CreateKeys_GimmicksByPlace_RolesFiveSix_OrNineZero()
        {
            int hero, villain;
            CareerTileLayout.CreateRoleKeys(4, out hero, out villain);
            Assert.AreEqual(5, hero); Assert.AreEqual(6, villain);
            CareerTileLayout.CreateRoleKeys(8, out hero, out villain);
            Assert.AreEqual(9, hero); Assert.AreEqual(CareerTileLayout.KeyZero, villain);
            Assert.AreEqual("0", CareerTileLayout.KeyLabel(CareerTileLayout.KeyZero));
            Assert.AreEqual("7", CareerTileLayout.KeyLabel(7));
            Assert.AreEqual("", CareerTileLayout.KeyLabel(0), "без клавіші — порожній квадрат не підписується");
            foreach (int n in new[] { 4, 5, 8 })
            {
                CareerTileLayout.CreateRoleKeys(n, out hero, out villain);
                for (int k = 1; k <= CareerTileLayout.KeyZero; k++)
                {
                    int d = CareerTileLayout.CreateDigit(k, n);
                    if (k == hero) Assert.AreEqual(CareerTileLayout.CreateDigitHero, d, n + " ґіміків, клавіша " + k);
                    else if (k == villain) Assert.AreEqual(CareerTileLayout.CreateDigitVillain, d, n + " ґіміків, клавіша " + k);
                    else if (k <= n) Assert.AreEqual(k - 1, d, n + " ґіміків, клавіша " + k + " — ґімік за місцем");
                    else Assert.AreEqual(CareerTileLayout.CreateDigitNone, d, n + " ґіміків, клавіша " + k + " — нічого");
                }
            }
            Assert.IsFalse(CareerTileLayout.CreateEight(4), "чотири ґіміки — розкладка М1");
            Assert.IsTrue(CareerTileLayout.CreateEight(8), "вісім — Б4-1");
        }

        /// <summary>
        /// Зміна ґіміка не лишає вільного балу за стелею (ПМ 14.11.2026): кожна пара ґімік → ґімік із будь-яким дозволеним розміщенням
        /// вільних балів першого — після <see cref="CareerScreenLines.NormalizeFreePoints(CharacterDraft, int, int[], int, out RingAttr, out RingAttr)"/>
        /// CanCreate без відмови; переставляється рівно тоді, коли ядро відмовило б attr_max, а бал, що влазить, не рухається (заготовка
        /// та сама); «звідки» — атрибут, що вийшов за стелю, «куди» — під стелею. Сценарій ПМ: Монстр, бал у Силу (3), потім Силач — бал
        /// переходить, Сила 3, і рядок афіші каже, куди й звідки.
        /// </summary>
        [Test]
        public void GimmickChange_NeverLeavesAFreePointOverTheCap()
        {
            var s = CareerKit.NewAtCreate(7);
            var v = s.GetCreationView();
            // усі ґіміки даних, не лише ті, що зараз на екрані створення (5–8 виходять за onCreationScreen)
            var all = new List<GimmickView>();
            foreach (var g in s.Content.Gimmicks) all.Add(new GimmickView { Def = g, BaseAttr = (int[])g.Attr.Clone(), FreePoints = g.FreePoints });
            var bad = new List<string>();
            int pairs = 0, moved = 0;
            foreach (var from in all)
                foreach (var start in FreePlacements(from, v.MaxAttr))
                    foreach (var to in all)
                    {
                        var d = CareerKit.Draft(from.Def.Id, CareerRole.Hero, start[0]);
                        d.FreePoint2 = start.Length > 1 ? start[1] : (RingAttr?)null;
                        string why;
                        string where = from.Def.Key + " " + start[0] + (start.Length > 1 ? "+" + start[1] : "") + " → " + to.Def.Key;
                        if (!s.CanCreate(d, out why)) { bad.Add(where + ": початкове розміщення не дозволене — " + why); continue; }
                        d.Gimmick = to.Def.Id;
                        bool over = !s.CanCreate(d, out why) && why == "attr_max";
                        var fp = d.FreePoint; var fp2 = d.FreePoint2;
                        RingAttr was, now;
                        bool m = CareerScreenLines.NormalizeFreePoints(d, to.FreePoints, to.BaseAttr, v.MaxAttr, out was, out now);
                        pairs++; if (m) moved++;
                        if (m != over) bad.Add(where + ": переставлено — " + m + ", за стелею було — " + over);
                        if (!m && (d.FreePoint != fp || d.FreePoint2 != fp2)) bad.Add(where + ": заготовка змінилась без перестановки");
                        if (m && to.FreePoints < 2 && (was != fp || to.BaseAttr[(int)was] + 1 <= v.MaxAttr)) bad.Add(where + ": «звідки» " + was + " — не той бал, що вийшов за стелю");
                        if (m && to.BaseAttr[(int)now] + CareerScreenLines.FreePointsIn(d, to.FreePoints, now) > v.MaxAttr) bad.Add(where + ": «куди» " + now + " за стелею");
                        if (!s.CanCreate(d, out why)) bad.Add(where + ": після зміни ґіміка CanCreate — " + why);
                        int sum = 0;
                        for (int i = 0; i < to.BaseAttr.Length; i++) sum += CareerScreenLines.FreePointsIn(d, to.FreePoints, (RingAttr)i);
                        if (sum != to.FreePoints) bad.Add(where + ": вільних балів " + sum + ", а має бути " + to.FreePoints);
                    }
            Assert.Greater(pairs, all.Count * all.Count, "пари справді перебрано");
            Assert.Greater(moved, 0, "перестановка справді трапляється — гілка міряється");
            Assert.IsEmpty(bad, string.Join("\n", bad));

            // сценарій ПМ
            GimmickView strong = null;
            foreach (var g in all) if (g.Def.Id == Gimmick.Strongman) strong = g;
            Assert.IsNotNull(strong, "передумова: Силач у даних ґіміків");
            var pm = CareerKit.Draft(Gimmick.Monster, CareerRole.Hero, RingAttr.Strength);
            string r;
            Assert.IsTrue(s.CanCreate(pm, out r), "Монстр + Сила: " + r);
            pm.Gimmick = Gimmick.Strongman;
            Assert.IsFalse(s.CanCreate(pm, out r), "передумова: без перестановки Силач + Сила за стелею");
            Assert.AreEqual("attr_max", r);
            RingAttr f0, t0;
            Assert.IsTrue(CareerScreenLines.NormalizeFreePoints(pm, strong.FreePoints, strong.BaseAttr, v.MaxAttr, out f0, out t0));
            Assert.AreEqual(RingAttr.Strength, f0, "звідки — Сила");
            Assert.AreEqual(pm.FreePoint, t0, "куди — туди, де бал тепер");
            Assert.AreNotEqual(RingAttr.Strength, pm.FreePoint, "бал пішов із Сили");
            Assert.AreEqual(0, CareerScreenLines.FreePointsIn(pm, strong.FreePoints, RingAttr.Strength), "Сила — основа Силача, 3");
            Assert.IsTrue(s.CanCreate(pm, out r), r);
            string line = CareerScreenLines.FreeMovedLine(T, f0, t0);
            NoMissingKey(line, "рядок афіші");
            StringAssert.Contains("у Силі", line, "звідки — місцевим відмінком");
            StringAssert.Contains("у " + T.Get("attr." + FeudVars.AttrName(t0) + ".acc"), line, "куди — знахідним");
            Assert.IsFalse(CareerScreenLines.NormalizeFreePoints(null, 1, strong.BaseAttr, v.MaxAttr));
            Assert.IsFalse(CareerScreenLines.NormalizeFreePoints(pm, 0, strong.BaseAttr, v.MaxAttr), "без вільних балів — нічого");

            // два бали, обидва за стелею (у даних такого ґіміка немає — Самородок з основами 0): кожен шукає місце з урахуванням
            // уже поставленого — Спритність 2 + 1, далі Техніка 2 + 1, а не обидва в Спритність (2 + 2 > 3)
            var syn = new[] { 3, 2, 2, 3, 3 };
            var two = CareerKit.Draft(Gimmick.Rookie, CareerRole.Hero, RingAttr.Strength);
            two.FreePoint2 = null;   // обидва — у Силу
            Assert.IsTrue(CareerScreenLines.NormalizeFreePoints(two, 2, syn, v.MaxAttr, out f0, out t0));
            Assert.AreEqual(RingAttr.Strength, f0); Assert.AreEqual(RingAttr.Agility, t0);
            for (int i = 0; i < syn.Length; i++)
                Assert.LessOrEqual(syn[i] + CareerScreenLines.FreePointsIn(two, 2, (RingAttr)i), v.MaxAttr, "атрибут " + (RingAttr)i + " за стелею");
            Assert.AreEqual(1, CareerScreenLines.FreePointsIn(two, 2, RingAttr.Agility));
            Assert.AreEqual(1, CareerScreenLines.FreePointsIn(two, 2, RingAttr.Technique));
            // місця немає ніде — заготовка та сама, false
            var full = CareerKit.Draft(Gimmick.Monster, CareerRole.Hero, RingAttr.Charisma);
            Assert.IsFalse(CareerScreenLines.NormalizeFreePoints(full, 1, new[] { 3, 3, 3, 3, 3 }, v.MaxAttr));
            Assert.AreEqual(RingAttr.Charisma, full.FreePoint, "без місця бал не рухається");
        }

        /// <summary>Усі дозволені розміщення вільних балів ґіміка: один — кожен атрибут під стелею; два — кожна пара (давніший, новіший).</summary>
        private static IEnumerable<RingAttr[]> FreePlacements(GimmickView g, int max)
        {
            int n = g.BaseAttr.Length;
            if (g.FreePoints < 2)
            {
                for (int a = 0; a < n; a++) if (g.BaseAttr[a] + 1 <= max) yield return new[] { (RingAttr)a };
                yield break;
            }
            for (int a = 0; a < n; a++)
                for (int b = 0; b < n; b++)
                    if (a == b ? g.BaseAttr[a] + 2 <= max : g.BaseAttr[a] + 1 <= max && g.BaseAttr[b] + 1 <= max)
                        yield return new[] { (RingAttr)a, (RingAttr)b };
        }

        /// <summary>
        /// Рядок підказки над рядом створення (АД 15.11.2026, Б1): на 4:3 (колонка) — «бал перейшов» &gt; С-1 &gt; опис ґіміка; на
        /// широкій — завжди опис ґіміка (рядок і С-1 там на папері).
        /// </summary>
        [Test]
        public void CreateRowHint_FourThree_FreeMoved_ThenC1_ThenGimmickDesc()
        {
            var t = RingText.FromJson(CareerKit.Read("strings_uk.json"), CareerKit.Read("ui_uk.json"));
            string key = CareerKit.NewAtCreate(1).GetCreationView().Gimmicks[0].Def.NameKey;
            string moved = CareerScreenLines.FreeMovedLine(t, RingAttr.Agility, RingAttr.Stamina), c1 = t.Get("c.create.freepoints.hint"), desc = t.Get(key + ".desc");
            foreach (var s in new[] { moved, c1, desc }) Assert.IsFalse(s.Contains("⟦") || s.Contains("{"), "немає тексту: «" + s + "»");
            Assert.AreNotEqual(moved, c1);
            Assert.AreNotEqual(c1, desc);
            Assert.AreEqual(moved, CareerScreenLines.CreateRowHint(t, true, true, RingAttr.Agility, RingAttr.Stamina, true, key), "4:3: «бал перейшов» — першим");
            Assert.AreEqual(moved, CareerScreenLines.CreateRowHint(t, true, true, RingAttr.Agility, RingAttr.Stamina, false, key));
            Assert.AreEqual(c1, CareerScreenLines.CreateRowHint(t, true, false, RingAttr.Agility, RingAttr.Stamina, true, key), "4:3: без перестановки — С-1");
            Assert.AreEqual(desc, CareerScreenLines.CreateRowHint(t, true, false, RingAttr.Agility, RingAttr.Stamina, false, key), "4:3: інакше — опис ґіміка");
            foreach (bool m in new[] { false, true })
                foreach (bool two in new[] { false, true })
                    Assert.AreEqual(desc, CareerScreenLines.CreateRowHint(t, false, m, RingAttr.Agility, RingAttr.Stamina, two, key), "широка: опис ґіміка");
            Assert.AreEqual("", CareerScreenLines.CreateRowHint(null, true, true, RingAttr.Agility, RingAttr.Stamina, true, key));
        }

        /// <summary>
        /// Кінець сезону 4:3 (АД 15.11.2026): рядок шкал — лише на 16:9 (на 4:3 дублює плиту ресурсів); підсумок згорнутий — лише на 4:3 і
        /// лише на кроках контракту чи пізньої ролі; на 16:9 — повний на будь-якому кроці.
        /// </summary>
        [Test]
        public void SeasonEnd_Column_ScalesLine_AndFoldedSummary()
        {
            Assert.IsTrue(CareerScreenLines.SeasonScalesLineShown(false));
            Assert.IsFalse(CareerScreenLines.SeasonScalesLineShown(true));
            foreach (bool contract in new[] { false, true })
                foreach (bool late in new[] { false, true })
                {
                    Assert.IsFalse(CareerScreenLines.SeasonSummaryCollapsed(false, contract, late), "16:9 — підсумок повний");
                    Assert.AreEqual(contract || late, CareerScreenLines.SeasonSummaryCollapsed(true, contract, late),
                        "4:3: контракт " + contract + ", пізня роль " + late);
                }
        }

        /// <summary>
        /// Проводка кінця сезону 4:3 (АД 15.11.2026, варіант А): розкладка — та сама межа, що на створенні (до розкладки — за екраном),
        /// перехід межі перебудовує раз; права колонка — вкладки «Кінець сезону» без числа й «Розвиток N» (N — очки з шапки плити,
        /// золото — як на тижні), компактні TabPlate, перша впритул; папір без рядка шкал, згорнутий на кроках контракту й пізньої ролі
        /// (рядок звільнення лишається); плита розвитку — місце під вкладками.
        /// </summary>
        [Test]
        public void Sources_SeasonEndColumn_AreWired()
        {
            string dir = Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Ring");
            string show = File.ReadAllText(Path.Combine(dir, "UI", "Career", "ShowScreens.cs"));
            string kit = File.ReadAllText(Path.Combine(dir, "UI", "Career", "ArenaKit.cs"));
            int start = show.IndexOf("public sealed class SeasonEndScreenView");
            Assert.Greater(start, 0, "немає SeasonEndScreenView");
            string season = show.Substring(start);
            var bad = new List<string>();
            foreach (var l in new[] {
                "float pw = Root.layout.width, ph = Root.layout.height;",
                "if (float.IsNaN(pw) || float.IsNaN(ph) || pw <= 0f || ph <= 0f) { pw = Screen.width; ph = Screen.height; }",
                "_single = CareerTileLayout.CreateSingleColumn(pw, ph);",
                "_relayout = false;",
                "if (!float.IsNaN(w) && !float.IsNaN(h) && w > 0f && h > 0f && CareerTileLayout.CreateSingleColumn(w, h) != _single)",
                "if (!_relayout) { _relayout = true; Root.schedule.Execute(Refresh); }",
                "if (_devBox != null) _devBox.style.maxHeight = DevMaxH(h);",
                "return _single ? CareerTileLayout.SeasonColumnRoom(panelH) : CareerTileLayout.DevPlateMaxH(CareerTileLayout.SeasonDevTop, panelH);",
                "if (_single) Root.Add(SeasonColumn(rep));",
                "Root.Add(Report(rep, false));",
                "Root.Add(DevPlate(false));",
                "var col = ArenaKit.Abs(null, ArenaKit.Top, ArenaKit.Edge, null, CareerTileLayout.SeasonColumnW, null);",
                "var report = new TabPlate(T.Get(\"c.season.tab\"), -1, _tab == TabReport, () => { if (_tab != TabReport) { _tab = TabReport; Refresh(); } }, false, true);",
                "report.style.marginLeft = 0f;",
                "var dev = new TabPlate(T.Get(\"dev.title\"), C.DevPoints, _tab == TabDev, () => { if (_tab != TabDev) { _tab = TabDev; Refresh(); } }, C.DevPoints > 0, true);",
                "dev.style.marginLeft = CareerTileLayout.ColumnTabGap;",
                "var plate = _tab == TabDev ? DevPlate(true) : Report(rep, true);",
                "plate.style.marginTop = CareerTileLayout.ColumnPlateGap;",
                "var box = column ? new VisualElement() : ArenaKit.Abs(null, ArenaKit.Top, ArenaKit.Edge, null, 520f, null);",
                "bool folded = CareerScreenLines.SeasonSummaryCollapsed(column, CareerScreenLines.OfferShown(C, _offer) && _offer.ShowContract,",
                "CareerScreenLines.LateRoleShown(C, actions));",
                "if (C.State.SeasonPopStart >= 0 && !folded)",
                "if (CareerScreenLines.SeasonScalesLineShown(column))",
                "var hammer = folded ? null : C.Content.FindNpc(CareerContent.HammerId);",
                "if (!folded && rep.TitleDone)",
                "else if (!folded && rep.TitleEligible)",
                "else if (!folded && !rep.Fired)",
                "string fired = CareerScreenLines.FiredLine(T, C, _offer);",
                "var box = column ? new VisualElement() : ArenaKit.Abs(ArenaKit.Edge, CareerTileLayout.SeasonDevTop, null, null, ArenaKit.StripW, null);",
                "box.style.maxHeight = DevMaxH(Root.layout.height);",
                "internal static readonly bool DevTabFirstForShots = Array.IndexOf(Environment.GetCommandLineArgs(), \"-ring-shot-season-dev\") >= 0;" })
                if (!CodeLine(season, l)) bad.Add("SeasonEndScreenView: немає «" + l + "»");
            if (!CodeLine(kit, "public const float Top = CareerTileLayout.Top;")) bad.Add("ArenaKit: верх екранів — не з моделі");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        /// <summary>
        /// Підказка плитки під курсором після перебудови (АД 15.11.2026, п. 3): позиція й пристрій вказівника — з руху й натискання
        /// (обидва — TrickleDown на корені), після перебудови — один раз, із запізненням на розкладку; лише миша й не після геймпада;
        /// плитка під курсором — та сама дорога наведення (OnHover: підсвітка й рядок підказки).
        /// </summary>
        [Test]
        public void Sources_CreateHoverAfterRebuild_IsWired()
        {
            string dir = Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Ring");
            string create = File.ReadAllText(Path.Combine(dir, "UI", "Career", "CreateScreenView.cs"));
            var bad = new List<string>();
            foreach (var l in new[] {
                "Root.RegisterCallback<PointerMoveEvent>(e => { _ptr = new Vector2(e.position.x, e.position.y); _ptrMouse = e.pointerType == PointerType.mouse; }, TrickleDown.TrickleDown);",
                "Root.RegisterCallback<PointerDownEvent>(e => { _ptr = new Vector2(e.position.x, e.position.y); _ptrMouse = e.pointerType == PointerType.mouse; }, TrickleDown.TrickleDown);",
                "if (!_ptrMouse || CareerPadFeature.PadHints(Menus.RingMenuInput.PadLast) || _hint == null) return;",
                "if (t.panel == null || t.OnHover == null) continue;",
                "var r = t.worldBound;",
                "if (_ptr.x >= r.xMin && _ptr.x <= r.xMax && _ptr.y >= r.yMin && _ptr.y <= r.yMax) { t.OnHover(); return; }",
                "if (_ptrMouse) Root.schedule.Execute(RestoreHover).ExecuteLater(30);" })
                if (!CodeLine(create, l)) bad.Add("CreateScreenView: немає «" + l + "»");
            // після перебудови: виклик — у Refresh, після очищення рядів (нові плитки вже в _tiles до розкладки)
            int refresh = create.IndexOf("public override void Refresh()");
            int clear = refresh < 0 ? -1 : create.IndexOf("_attrRows.Clear();", refresh);
            int later = refresh < 0 ? -1 : create.IndexOf("Root.schedule.Execute(RestoreHover)", refresh);
            if (refresh < 0 || clear < 0 || later < clear) bad.Add("CreateScreenView: відновлення підказки не в Refresh");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        /// <summary>
        /// Проводка створення 4:3 (АД 14–15.11.2026): розкладка — за співвідношенням панелі (до розкладки — за екраном), перехід межі
        /// 3:2 перебудовує раз; колонка — стрічка імені, три вкладки (ті самі TabPlate, компактні, «Прийоми» без числа, перша впритул,
        /// відкрита з обводкою) і плита відкритої вкладки; афіша колонки — без заголовка й опису ролі (Б1), рядок підказки —
        /// CreateRowHint; опис ролі — підказкою плитки ролі; Esc на «Прийомах» — назад на афішу; вертикаль афіші й «Вигляду» — з
        /// констант моделі, які міряє охоронець CareerTextFitTests.
        /// </summary>
        [Test]
        public void Sources_CreateColumn_AreWired()
        {
            string dir = Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Ring");
            string create = File.ReadAllText(Path.Combine(dir, "UI", "Career", "CreateScreenView.cs"));
            string kit = File.ReadAllText(Path.Combine(dir, "UI", "Career", "ArenaKit.cs"));
            string shots = File.ReadAllText(Path.Combine(dir, "RingMenuShots.cs"));
            var bad = new List<string>();
            foreach (var l in new[] {
                "float pw = Root.layout.width, ph = Root.layout.height;",
                "if (float.IsNaN(pw) || float.IsNaN(ph) || pw <= 0f || ph <= 0f) { pw = Screen.width; ph = Screen.height; }",
                "_single = CareerTileLayout.CreateSingleColumn(pw, ph);",
                "if (!float.IsNaN(height) && height > 0f && CareerTileLayout.CreateSingleColumn(width, height) != _single)",
                "if (!_relayout) { _relayout = true; Root.schedule.Execute(Refresh); }",
                "if (_single) Root.Add(TabColumn(path, roleColor));",
                "var col = ArenaKit.Abs(ArenaKit.Edge, ArenaKit.SecondY, null, null, ArenaKit.StripW, null);",
                "string[] words = { T.Get(\"c.hub.create.poster\"), T.Get(\"c.create.look\"), T.Get(\"c.hub.create.moves\") };",
                "var tab = new TabPlate(words[i], -1, _tab == i, () => { if (_tab != k) { _tab = k; Refresh(); } }, false, true);",
                "tab.style.flexShrink = 0f;",
                "tab.style.marginLeft = i == 0 ? 0f : CareerTileLayout.ColumnTabGap;",
                "else if (_tab == TabMoves) plate = MovesCard();",
                "else plate = Poster(path, roleColor, true);",
                "plate.style.marginTop = CareerTileLayout.ColumnPlateGap;",
                "if (!column) col.Add(MenuKit.Display(T.Get(\"c.hub.create.poster\"), 24f, RingTheme.Bg, RingFontRole.Display800, 0.04f));",
                "_hintDefault = CareerScreenLines.CreateRowHint(T, _single, FreeMovedNote, _freeMovedFrom, _freeMovedTo, Current.FreePoints >= 2, Current.Def.NameKey);",
                "_hint.style.marginLeft = CareerTileLayout.CreateHintGap;",
                "var tile = AddTile(r == CareerRole.Hero ? heroKey : villainKey, T.Get(key), \"\", () => { _d.Role = rr; Refresh(); }, T.Get(key + \".desc\"));",
                "col.style.paddingTop = CareerTileLayout.CreatePosterPadTop; col.style.paddingBottom = CareerTileLayout.CreatePaperPadBottom;",
                "head.style.marginTop = CareerTileLayout.CreatePosterHeadTop;",
                "line.style.height = CareerTileLayout.CreatePosterRuleH; line.style.backgroundColor = RingTheme.Bg;",
                "line.style.marginTop = CareerTileLayout.CreatePosterRuleTop; line.style.marginBottom = CareerTileLayout.CreatePosterRuleBottom;",
                "hr.style.marginBottom = CareerTileLayout.CreatePosterPickGap;",
                "var box = ArenaKit.Abs(ArenaKit.Edge, ArenaKit.SecondY, null, null, ArenaKit.StripW, CareerTileLayout.CreateLookH);",
                "foreach (var r in _attrRows) r.style.height = tall ? CareerTileLayout.CreateAttrRowNarrowH : CareerTileLayout.CreateAttrRowH;",
                "if (_tab != TabMoves) return false;",
                "plate.style.position = Position.Relative;",
                "plate.style.left = StyleKeyword.Auto; plate.style.top = StyleKeyword.Auto;",
                "_movesBox = null; _rightCol = null; _desc = null; _paperMoves = null; _movesCard = null; _posterCol = null; _posterName = null;" })
                if (!CodeLine(create, l)) bad.Add("CreateScreenView: немає «" + l + "»");
            // Esc у колонці: лише з «Прийомів» — на афішу, і до гілки широкої розкладки
            int back = create.IndexOf("public override bool OnBack()");
            int movesOnly = back < 0 ? -1 : create.IndexOf("if (_tab != TabMoves) return false;", back);
            int toPoster = back < 0 ? -1 : create.IndexOf("_tab = TabPoster;", back);
            int wide = back < 0 ? -1 : create.IndexOf("if (!_moves) return false;", back);
            if (movesOnly < 0 || toPoster < movesOnly || wide < toPoster) bad.Add("CreateScreenView: Esc на «Прийомах» колонки не повертає на афішу");
            // колонка розрахована на панель 900: екрани кар'єри — у масштабі меню, «розмір інтерфейсу» їх не міняє (RingGameBootstrap)
            string boot = File.ReadAllText(Path.Combine(dir, "RingGameBootstrap.cs"));
            if (!CodeLine(boot, "bool menu = _view is IMenuNavigable || _view is UI.CareerScreenView"))
                bad.Add("RingGameBootstrap: екрани кар'єри більше не в масштабі меню — колонку 4:3 міряли на панелі 900");
            foreach (var l in new[] {
                "public TabPlate(string text, int number, bool open, Action onClick, bool goldStripe) : this(text, number, open, onClick, goldStripe, false) { }",
                "style.height = CareerTileLayout.TabPlateH;",
                "style.paddingLeft = compact ? CareerTileLayout.ColumnTabPadLeft : 18f; style.paddingRight = compact ? CareerTileLayout.ColumnTabPadRight : 26f;",
                "var plate = new ArenaPlate { Fill = RingTheme.Panel, Stripe = goldStripe ? (Color?)RingTheme.Gold : null, StripeW = 5f, Outline = open ? (Color?)RingTheme.Text : null, OutlineW = 2f };",
                "if (number >= 0)" })
                if (!CodeLine(kit, l)) bad.Add("ArenaKit: немає «" + l + "»");
            foreach (var l in new[] {
                "string pair = create.FreeMovedForShots(CareerRole.Hero);",
                "foreach (int tab in new[] { UI.CreateScreenView.TabLook, UI.CreateScreenView.TabMoves })",
                "if (float.IsNaN(size) || size >= PosterBelowUnder || create.SingleColumnForShots) continue;" })
                if (!CodeLine(shots, l)) bad.Add("RingMenuShots: немає «" + l + "»");
            // опис ролі на афіші колонки не будується: блок опису — під if (!column) у Poster
            int poster = create.IndexOf("private VisualElement Poster(RingPath path, Color roleColor, bool column = false)");
            int notCol = poster < 0 ? -1 : create.IndexOf("if (!column)", create.IndexOf("_paperMoves = null;", poster));
            int descAt = poster < 0 ? -1 : create.IndexOf("var desc = FreeMovedNote ?", poster);
            if (notCol < 0 || descAt < notCol) bad.Add("CreateScreenView: опис ролі на афіші колонки не під if (!column)");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        /// <summary>
        /// Проводка зміни ґіміка на створенні (ПМ 14.11.2026): плитка, клавіша й огляд ідуть через PickGimmick (перестановка вільного
        /// балу), інших присвоєнь ґіміка заготовці немає; рядок — на папері на місці опису ролі чи С-1 (АД, варіант (1)), чорнилом
        /// паперу, і лише доки заготовка та сама.
        /// </summary>
        [Test]
        public void Sources_GimmickChange_NormalizesTheFreePoint()
        {
            string create = File.ReadAllText(Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Ring", "UI", "Career", "CreateScreenView.cs"));
            var bad = new List<string>();
            foreach (var l in new[] {
                "() => { PickGimmick(gid); Refresh(); }, T.Get(g.Def.NameKey + \".desc\"));",
                "else if (d >= 0) { PickGimmick(_v.Gimmicks[d].Def.Id); Refresh(); }",
                "_freeMoved = CareerScreenLines.NormalizeFreePoints(_d, cur.FreePoints, cur.BaseAttr, _v.MaxAttr, out _freeMovedFrom, out _freeMovedTo);",
                "_freeMovedGimmick = _d.Gimmick; _freeMovedFp = _d.FreePoint; _freeMovedFp2 = _d.FreePoint2;",
                "get { return _freeMoved && _d.Gimmick == _freeMovedGimmick && _d.FreePoint == _freeMovedFp && _d.FreePoint2 == _freeMovedFp2; }",
                "CareerScreenLines.NormalizeFreePoints(_d, Current.FreePoints, Current.BaseAttr, _v.MaxAttr);",
                "var desc = FreeMovedNote ? MenuKit.Body(CareerScreenLines.FreeMovedLine(T, _freeMovedFrom, _freeMovedTo), BrandTokens.TypeMinInfo, RingTheme.Bg)",
                ": two ? MenuKit.Body(T.Get(\"c.create.freepoints.hint\"), BrandTokens.TypeMinInfo, RingTheme.Bg)" })
                if (!CodeLine(create, l)) bad.Add("CreateScreenView: немає «" + l + "»");
            int shotsAt = create.IndexOf("internal void SetForShots(Gimmick g, CareerRole r)");
            int pick = shotsAt < 0 ? -1 : create.IndexOf("PickGimmick(g);", shotsAt);
            if (pick < 0 || pick > create.IndexOf("Refresh();", shotsAt)) bad.Add("CreateScreenView: огляд ставить ґімік повз PickGimmick");
            int assigns = Regex.Matches(create, @"_d\.Gimmick\s*=[^=]").Count;
            if (assigns != 2) bad.Add("CreateScreenView: присвоєнь _d.Gimmick " + assigns + " — лише стартове і в PickGimmick");
            if (create.Contains("create.note.free_moved")) bad.Add("CreateScreenView: рядок — лише через CareerScreenLines.FreeMovedLine");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        /// <summary>
        /// Б4: два вільні бали Самородка — клік ставить бал сюди, найдавніший переходить; двічі по тому самому — обидва там; вище стелі
        /// бал не ставиться. Один бал — просто переходить. Ядро рахує заготовку так само (CreateHero дає основу + бали).
        /// </summary>
        [Test]
        public void FreePoints_TwoPoints_OldestMoves_TwiceStacks_CapHolds_AndTheCoreAgrees()
        {
            const int max = WrestlerStats.MaxAttr;
            var content = CareerKit.NewAtCreate(1).Content;
            Assert.AreEqual(2, content.GimmickOf(Gimmick.Rookie).FreePoints, "передумова: у Самородка два вільні бали");
            foreach (int v in content.GimmickOf(Gimmick.Rookie).Attr) Assert.AreEqual(0, v, "передумова: основи Самородка — 0");

            var d = CareerKit.Draft(Gimmick.Rookie, CareerRole.Hero, RingAttr.Strength);
            Assert.AreEqual(2, CareerScreenLines.FreePointsIn(d, 2, RingAttr.Strength), "другого ще не ставили — обидва там, де перший (як у ядрі)");
            Assert.IsTrue(CareerScreenLines.PlaceFreePoint(d, 2, 0, max, RingAttr.Agility));
            Assert.AreEqual(1, CareerScreenLines.FreePointsIn(d, 2, RingAttr.Strength));
            Assert.AreEqual(1, CareerScreenLines.FreePointsIn(d, 2, RingAttr.Agility));
            Assert.IsTrue(CareerScreenLines.PlaceFreePoint(d, 2, 0, max, RingAttr.Technique));
            Assert.AreEqual(0, CareerScreenLines.FreePointsIn(d, 2, RingAttr.Strength), "найдавніший (сила) перейшов");
            Assert.AreEqual(1, CareerScreenLines.FreePointsIn(d, 2, RingAttr.Agility));
            Assert.AreEqual(1, CareerScreenLines.FreePointsIn(d, 2, RingAttr.Technique));
            Assert.IsTrue(CareerScreenLines.PlaceFreePoint(d, 2, 0, max, RingAttr.Technique));
            Assert.AreEqual(2, CareerScreenLines.FreePointsIn(d, 2, RingAttr.Technique), "двічі — обидва тут");
            Assert.AreEqual(0, CareerScreenLines.FreePointsIn(d, 2, RingAttr.Agility));
            Assert.IsTrue(CareerScreenLines.PlaceFreePoint(d, 2, 0, max, RingAttr.Technique));
            Assert.AreEqual(2, CareerScreenLines.FreePointsIn(d, 2, RingAttr.Technique), "утретє — так само обидва");
            int sum = 0;
            for (int i = 0; i < 5; i++) sum += CareerScreenLines.FreePointsIn(d, 2, (RingAttr)i);
            Assert.AreEqual(2, sum, "балів завжди два");

            // стеля: основа 2 — другий бал туди ж дав би 4
            var c = CareerKit.Draft(Gimmick.Rookie, CareerRole.Hero, RingAttr.Strength);
            Assert.IsTrue(CareerScreenLines.PlaceFreePoint(c, 2, 2, max, RingAttr.Agility), "2 + 1 = 3 — можна");
            Assert.IsFalse(CareerScreenLines.PlaceFreePoint(c, 2, 2, max, RingAttr.Agility), "2 + 2 > 3 — ні");
            Assert.AreEqual(1, CareerScreenLines.FreePointsIn(c, 2, RingAttr.Agility), "відмова заготовки не змінює");
            Assert.AreEqual(1, CareerScreenLines.FreePointsIn(c, 2, RingAttr.Strength));

            // один вільний бал — переходить, другий бал заготовки не рахується
            var m = CareerKit.Draft(Gimmick.Monster, CareerRole.Hero, RingAttr.Strength);
            Assert.IsFalse(CareerScreenLines.PlaceFreePoint(m, 1, max, max, RingAttr.Technique), "на стелі — ні");
            Assert.IsTrue(CareerScreenLines.PlaceFreePoint(m, 1, 0, max, RingAttr.Charisma));
            Assert.AreEqual(RingAttr.Charisma, m.FreePoint);
            Assert.IsNull(m.FreePoint2);
            m.FreePoint2 = RingAttr.Agility;   // лишився від Самородка
            Assert.AreEqual(0, CareerScreenLines.FreePointsIn(m, 1, RingAttr.Agility), "у ґіміка з одним балом другий не рахується");
            Assert.AreEqual(0, CareerScreenLines.FreePointsIn(null, 2, RingAttr.Agility));
            Assert.IsFalse(CareerScreenLines.PlaceFreePoint(null, 2, 0, max, RingAttr.Agility));
            Assert.IsFalse(CareerScreenLines.PlaceFreePoint(m, 0, 0, max, RingAttr.Agility), "без вільних балів — нічого");

            // ядро: обидва в техніку — техніка +2; два різні — по +1
            var s = CareerKit.NewAtCreate(31);
            var r = CareerKit.Draft(Gimmick.Rookie, CareerRole.Hero, RingAttr.Strength);
            CareerScreenLines.PlaceFreePoint(r, 2, 0, max, RingAttr.Technique);
            CareerScreenLines.PlaceFreePoint(r, 2, 0, max, RingAttr.Technique);
            string reason;
            Assert.IsTrue(s.CanCreate(r, out reason), reason);
            s.CreateHero(r);
            Assert.AreEqual(2, s.Player.Get(RingAttr.Technique));
            Assert.AreEqual(0, s.Player.Get(RingAttr.Strength), "перший бал пішов із сили");
            var s2 = CareerKit.NewAtCreate(32);
            var r2 = CareerKit.Draft(Gimmick.Rookie, CareerRole.Hero, RingAttr.Strength);
            CareerScreenLines.PlaceFreePoint(r2, 2, 0, max, RingAttr.Agility);
            s2.CreateHero(r2);
            Assert.AreEqual(1, s2.Player.Get(RingAttr.Strength));
            Assert.AreEqual(1, s2.Player.Get(RingAttr.Agility));
        }

        /// <summary>Шкала характеристики афіші: мінусовий блок, заповнені, вільні (золоті) й порожні клітинки — з моделі.</summary>
        [Test]
        public void AttrCells_FreePointsGoldOverTheBase_NegativeBlockFirst()
        {
            Assert.AreEqual("·|□□□", Cells(0, 0));
            Assert.AreEqual("·|■■□", Cells(2, 0));
            Assert.AreEqual("·|■■+", Cells(2, 1));
            Assert.AreEqual("·|■■■", Cells(3, 0));
            Assert.AreEqual("·|++□", Cells(0, 2), "Самородок: обидва в одну");
            Assert.AreEqual("·|■++", Cells(1, 2));
            Assert.AreEqual("−|□□□", Cells(-1, 0), "мінус — сигнальний блок");
            Assert.AreEqual("+|□□□", Cells(-1, 1), "бал при мінусовій основі закриває мінус");
            Assert.AreEqual("+|+□□", Cells(-1, 2));
        }

        private static string Cells(int b, int free)
        {
            var c = CareerTileLayout.CreateAttrCells(b, free, WrestlerStats.MaxAttr);
            Assert.AreEqual(WrestlerStats.MaxAttr + 1, c.Length);
            var sb = new System.Text.StringBuilder();
            sb.Append(c[0] == CareerTileLayout.AttrCell.Negative ? "−" : c[0] == CareerTileLayout.AttrCell.Free ? "+" : "·").Append('|');
            for (int k = 1; k < c.Length; k++)
                sb.Append(c[k] == CareerTileLayout.AttrCell.Filled ? "■" : c[k] == CareerTileLayout.AttrCell.Free ? "+" : "□");
            return sb.ToString();
        }

        /// <summary>
        /// Ш-1 (арт-директор 14.11.2026): частини плитки ґіміка — ті самі, що з <see cref="RingLabels.AttrBonusParts"/>; переставлено
        /// лише Шоумена — «Харизма +3 · Сила −1» / «Витривалість +1» (найдовша — остання, підписна — перша). У Самородка — «+2 вільні
        /// бали». Модель порядку без значень лише ставить найдовшу в кінець; менше трьох частин чи найдовша вже остання — без змін.
        /// </summary>
        [Test]
        public void CreateTileParts_OnlyShowmanReordered_SignatureFirst_RookieSaysTwoFreePoints()
        {
            var content = CareerKit.NewAtCreate(1).Content;
            Assert.AreEqual(CareerKit.GimmickCount, content.Gimmicks.Count, "усі вісім ґіміків");
            int changed = 0;
            foreach (var g in content.Gimmicks)
            {
                var plain = new List<string>(RingLabels.AttrBonusParts(T, g.Attr));
                var parts = CareerScreenLines.CreateTileParts(T, g.Attr, g.FreePoints);
                var expect = new List<string>(plain);
                if (g.FreePoints >= 2) expect.Add(T.Get("c.create.tile.freepoints"));
                CollectionAssert.AreEquivalent(expect, parts, g.Key + ": ті самі частини");
                foreach (var p in parts) NoMissingKey(p, g.Key);
                bool same = true;
                for (int i = 0; i < plain.Count; i++) if (parts[i] != plain[i]) same = false;
                if (!same) { changed++; Assert.AreEqual(Gimmick.Showman, g.Id, g.Key + ": порядок мав лишитися"); }
            }
            Assert.AreEqual(1, changed, "переставлено лише Шоумена");
            var showman = content.GimmickOf(Gimmick.Showman);
            string cha = RingLabels.AttrName(T, RingAttr.Charisma) + " " + RingChances.Signed(3);
            string str = RingLabels.AttrName(T, RingAttr.Strength) + " " + RingChances.Signed(-1);
            string sta = RingLabels.AttrName(T, RingAttr.Stamina) + " " + RingChances.Signed(1);
            CollectionAssert.AreEqual(new[] { cha, str, sta }, CareerScreenLines.CreateTileParts(T, showman.Attr, showman.FreePoints), "Шоумен — за Харизмою");
            var rookie = content.GimmickOf(Gimmick.Rookie);
            CollectionAssert.AreEqual(new[] { "+2 вільні бали" }, CareerScreenLines.CreateTileParts(T, rookie.Attr, rookie.FreePoints), "текст У0");

            CollectionAssert.AreEqual(new[] { "a", "ccc", "bbbbb" }, CareerTileLayout.CreateEffectOrder(new[] { "a", "bbbbb", "ccc" }), "без значень — лише найдовша в кінець");
            CollectionAssert.AreEqual(new[] { "ccc", "a", "bbbbb" }, CareerTileLayout.CreateEffectOrder(new[] { "a", "bbbbb", "ccc" }, new[] { -1, 1, 3 }), "підписна — перша");
            CollectionAssert.AreEqual(new[] { "a", "ccc", "bbbbb" }, CareerTileLayout.CreateEffectOrder(new[] { "a", "bbbbb", "ccc" }, new[] { 2, 1, 2 }), "рівні бонуси — перша з них");
            CollectionAssert.AreEqual(new[] { "aa", "bb", "cc" }, CareerTileLayout.CreateEffectOrder(new[] { "aa", "bb", "cc" }, new[] { 1, 2, 3 }), "рівна довжина — остання вже найдовша");
            CollectionAssert.AreEqual(new[] { "x", "yyyy" }, CareerTileLayout.CreateEffectOrder(new[] { "x", "yyyy" }, new[] { 3, 1 }), "дві частини — без змін");
            CollectionAssert.IsEmpty(CareerTileLayout.CreateEffectOrder(null));
            CollectionAssert.IsEmpty(CareerScreenLines.CreateTileParts(T, null, 1));
        }

        // ---------------- Б5: тіло старіє (власник 13.11.2026, «Так, усі 7») ----------------

        /// <summary>
        /// Б5: хвіст «тіло старіє» — з сезону 4 (+1), з сезону 7 (+2); рядок кінця сезону — лише на останньому кроці («Новий сезон»)
        /// кінців сезонів 3 і 6, коли ціна наступного сезону росте; ціни — з сесії того сезону; 5 — «тренувань».
        /// </summary>
        [Test]
        public void Ageing_Tail_FromSeason4_AndTheNextSeasonLine_OnlyWhenThePriceGrows()
        {
            for (int season = 1; season < Ageing.FromSeason; season++)
            {
                Assert.IsNull(CareerScreenLines.AgeingTail(T, season, false), "сезон " + season);
                Assert.IsNull(CareerScreenLines.AgeingTail(T, season, true), "сезон " + season);
            }
            Assert.AreEqual("тіло старіє: +1 оч.", CareerScreenLines.AgeingTail(T, 4, false));
            Assert.AreEqual("тіло старіє: +1 тренування", CareerScreenLines.AgeingTail(T, 4, true));
            Assert.AreEqual("тіло старіє: +2 оч.", CareerScreenLines.AgeingTail(T, 7, false));
            Assert.AreEqual("тіло старіє: +2 тренування", CareerScreenLines.AgeingTail(T, 7, true));

            var last = new List<SeasonEndAction> { SeasonEndAction.NextSeason };
            var s1 = CareerKit.NewHero(8);
            var s3 = InSeason(s1, 3);
            Assert.AreEqual("Тіло старіє: витривалість — 3 оч., тренер — 4 тренування.",
                CareerScreenLines.AgeingNextLine(T, s3, last), "кінець сезону 3 — однорядковий текст (ПМ 14.11.2026)");
            var s6 = InSeason(s1, 6);
            Assert.AreEqual("Тіло старіє: витривалість — 4 оч., тренер — 5 тренувань.",
                CareerScreenLines.AgeingNextLine(T, s6, last), "кінець сезону 6: 5 — «тренувань»");
            // ціни — ті, що спише покупка наступного сезону (сесія того сезону)
            foreach (var n in new[] { 3, 6 })
            {
                var cur = n == 3 ? s3 : s6;
                var nxt = InSeason(s1, n + 1);
                int stamina = System.Array.IndexOf(CareerRules.Coaches, RingAttr.Stamina);
                StringAssert.Contains("— " + nxt.AttrCost(RingAttr.Stamina) + " оч.", CareerScreenLines.AgeingNextLine(T, cur, last), "сезон " + n + ": очки");
                StringAssert.Contains("— " + nxt.CoachStepFor(stamina) + " трен", CareerScreenLines.AgeingNextLine(T, cur, last), "сезон " + n + ": тренування");
            }
            for (int season = 1; season <= 12; season++)
                if (season != 3 && season != 6) Assert.IsNull(CareerScreenLines.AgeingNextLine(T, InSeason(s1, season), last), "сезон " + season + ": ціна не росте");
            Assert.IsNull(CareerScreenLines.AgeingNextLine(T, s3, new List<SeasonEndAction> { SeasonEndAction.TitleMatch, SeasonEndAction.SkipTitle }), "не останній крок");
            Assert.IsNull(CareerScreenLines.AgeingNextLine(T, s3, new List<SeasonEndAction> { SeasonEndAction.AcceptOffer, SeasonEndAction.DeclineOffer }));
            Assert.IsNull(CareerScreenLines.AgeingNextLine(T, s3, null));
            Assert.IsNull(CareerScreenLines.AgeingNextLine(T, s3, new List<SeasonEndAction>()));
            Assert.IsNull(CareerScreenLines.AgeingNextLine(T, null, last));

            foreach (int n in new[] { 0, 5, 6, 9, 10, 11, 12, 13, 14, 15, 20, 25, 111, 112 }) Assert.IsTrue(CareerScreenLines.PluralMany(n), n + " — «тренувань»");
            foreach (int n in new[] { 1, 2, 3, 4, 21, 22, 23, 24, 101, 102, 104 }) Assert.IsFalse(CareerScreenLines.PluralMany(n), n + " — «тренування»");
        }

        /// <summary>
        /// Б5: слід травми (з сезону 4) — хвіст «(слід: +1)» у травмованої й ушкодженої зони, рядок «слід — травма на 1 тиж. довша» у
        /// здорової зони зі слідом. Вид — справжній (сезон 4, сліди в стані), рядки — тексти У0.
        /// </summary>
        [Test]
        public void Injury_ScarTail_AndTheScarOnlyLine_FromTheRealView()
        {
            var t = InSeason(CareerKit.NewHero(6), 4);
            t.State.Scars[(int)BodyZone.Legs] = 2; t.State.Injury[(int)BodyZone.Legs] = 3;   // травма зі слідом раніших
            t.State.Scars[(int)BodyZone.Head] = 1; t.State.Injury[(int)BodyZone.Head] = 0; t.State.ZoneDamage[(int)BodyZone.Head] = 0;
            t.State.Scars[(int)BodyZone.Body] = 1; t.State.Injury[(int)BodyZone.Body] = 0; t.State.ZoneDamage[(int)BodyZone.Body] = 1;
            var v = t.GetWeekView().Injuries;
            string legs = T.Get("bodyzone." + (int)BodyZone.Legs), head = T.Get("bodyzone." + (int)BodyZone.Head), body = T.Get("bodyzone." + (int)BodyZone.Body);
            string line = CareerScreenLines.InjuryText(T, v);
            NoMissingKey(line, "рядок тіла зі слідами");
            StringAssert.Contains("Ноги: травма, ще 3 тиж. (слід: +1)", line, "текст У0");
            StringAssert.Contains(head + ": слід — травма на 1 тиж. довша", line, "здорова зона зі слідом");
            StringAssert.Contains(T.Format("c.injury.dmg", body, 1, t.InjuryAtZoneDamage) + " (слід: +1)", line, "ушкоджена зона зі слідом");
            Assert.AreEqual(legs, "Ноги", "передумова тексту У0");

            var early = InSeason(CareerKit.NewHero(6), 3);
            early.State.Scars[(int)BodyZone.Legs] = 2; early.State.Injury[(int)BodyZone.Legs] = 3;
            StringAssert.DoesNotContain("слід", CareerScreenLines.InjuryText(T, early.GetWeekView().Injuries), "до сезону 4 сліду не видно");
            Assert.AreEqual("x", CareerScreenLines.ScarTail(T, "x", 0));
            Assert.AreEqual("x (слід: +1)", CareerScreenLines.ScarTail(T, "x", 1));
        }

        // ---------------- Б6: суперник на шоу (власник 13.11.2026, варіант А) ----------------

        /// <summary>
        /// Б6: рядок результату «суперник на шоу — {0}» — лише коли сцена переписала картку (<see cref="WeekActionResult.OpponentChanged"/>);
        /// без прапорця, без імені чи без результату — рядка немає. Суперниця — «.rf» того самого ключа (білий перелік «Кар'єри»,
        /// 4cbeac7f).
        /// </summary>
        [Test]
        public void OpponentLine_OnlyWhenTheSceneRewroteTheCard_RivalWomanForm()
        {
            var yes = new WeekActionResult { OpponentChanged = true };
            Assert.AreEqual("суперник на шоу — Сталевий", CareerScreenLines.OpponentLine(T, yes, "Сталевий"));
            Assert.IsNull(CareerScreenLines.OpponentLine(T, new WeekActionResult(), "Сталевий"), "картку не переписано — рядка немає");
            Assert.IsNull(CareerScreenLines.OpponentLine(T, yes, null));
            Assert.IsNull(CareerScreenLines.OpponentLine(T, yes, ""));
            Assert.IsNull(CareerScreenLines.OpponentLine(T, null, "Сталевий"));
            var rt = RingText.FromJson(CareerKit.Read("strings_uk.json"), CareerKit.Read("ui_uk.json"));
            HeroForms.Apply(rt, true, true);   // «.rf» — героїня проти суперниці (суперниці без героїні кар'єра не ставить)
            Assert.AreEqual("суперниця на шоу — Мара", CareerScreenLines.OpponentLine(rt, yes, "Мара"));
            HeroForms.Apply(rt, true, false);
            Assert.AreEqual("суперник на шоу — Сталевий", CareerScreenLines.OpponentLine(rt, yes, "Сталевий"), "героїня проти суперника");
        }

        /// <summary>Б4, Б4-1, С-1, Ш-1, Б5, Б6 — джерела екранів кладуть рядки з моделей у свої складники.</summary>
        [Test]
        public void Sources_B4_B5_B6_AreWired()
        {
            string ui = Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Ring", "UI");
            string create = File.ReadAllText(Path.Combine(ui, "Career", "CreateScreenView.cs"));
            string week = File.ReadAllText(Path.Combine(ui, "Career", "WeekScreenView.cs"));
            string show = File.ReadAllText(Path.Combine(ui, "Career", "ShowScreens.cs"));
            string kit = File.ReadAllText(Path.Combine(ui, "Career", "ArenaKit.cs"));
            var bad = new List<string>();
            if (!CodeLine(create, "tile.SetEffectParts(CareerScreenLines.CreateTileParts(T, g.BaseAttr, g.FreePoints));"))
                bad.Add("CreateScreenView: плитка ґіміка не з CreateTileParts (Ш-1, «+2 вільні бали»)");
            if (!CodeLine(create, "CareerTileLayout.CreateRoleKeys(_v.Gimmicks.Count, out heroKey, out villainKey);")) bad.Add("CreateScreenView: клавіші ролей не з моделі");
            if (!CodeLine(create, "int d = CareerTileLayout.CreateDigit(n, _v.Gimmicks.Count);")) bad.Add("CreateScreenView: цифри не з моделі");
            if (!CodeLine(create, "if (Input.GetKeyDown(KeyCode.Alpha0) && Host != null && Host.KeysFree && !Typing()) OnDigit(CareerTileLayout.KeyZero);"))
                bad.Add("CreateScreenView: «0» не ловиться або обходить ворота клавіш хоста (завантаження, розріз, пауза, ввід меню)");
            string menus = File.ReadAllText(Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Ring", "RingGameBootstrap.Menus.cs"));
            if (!menus.Contains("public bool KeysFree { get { return !MenuOverlayBusy && !Paused && _keysTakenFrame != Time.frameCount; } }")
                || !menus.Contains("return Took(true);") || !menus.Contains("return Took(PollTyping());") || !menus.Contains("return Took(PollCareerPad());"))
                bad.Add("RingGameBootstrap.Menus: KeysFree не за воротами HandleKeys (оверлей, пауза, ввід меню кадру)");
            if (!create.Contains("CareerScreenLines.PlaceFreePoint(_d, freePoints, baseValue, _v.MaxAttr, a)")
                || !create.Contains("CareerScreenLines.FreePointsIn(_d, Current.FreePoints, a)")) bad.Add("CreateScreenView: вільні бали не з моделі");
            if (!create.Contains("CareerTileLayout.CreateAttrCells(baseValue, free, _v.MaxAttr)")) bad.Add("CreateScreenView: клітинки шкали не з моделі");
            if (!create.Contains("T.Get(two ? \"c.create.freepoints\" : \"c.create.freepoint\")")) bad.Add("CreateScreenView: заголовок «Вільні бали (+2)»");
            // С-1 — на місці опису ролі; тимчасово його заступає лише рядок «вільний бал перейшов» (АД 14.11.2026, варіант (1))
            if (!create.Contains(": two ? MenuKit.Body(T.Get(\"c.create.freepoints.hint\")")) bad.Add("CreateScreenView: С-1 — підказка Самородка не на місці опису ролі");
            if (!CodeLine(create, "bool eight = CareerTileLayout.CreateEight(_v.Gimmicks.Count);")) bad.Add("CreateScreenView: Б4-1 не за моделлю");
            if (!CodeLine(create, "tile.SetLow();") || !create.Contains("_roleCol.Add(tile);")) bad.Add("CreateScreenView: Б4-1 — ролі не низькими плитами в колонці");
            if (!create.Contains("_reasonBox.style.height = eight ? CareerTileLayout.CreateRoleReasonH : 45f;")) bad.Add("CreateScreenView: Б4-1 — причина не в просвіті");
            if (!kit.Contains("CareerTileLayout.KeyLabel(key)")) bad.Add("ArenaKit: квадрат клавіші без «0»");
            if (!week.Contains("CareerScreenLines.ScarTail(T, T.Format(\"c.injury.tile\"") || !CodeLine(week, "i.ScarWeeks);   // Б5: слід"))
                bad.Add("WeekScreenView: ризик травми без сліду");
            if (!CodeLine(week, "var deltas = CareerScreenLines.WeekResultDeltas(T, r, OpponentName(), HeroForms.ShowsHeroine(T));")) bad.Add("WeekScreenView: Б6 — без рядка суперника");
            // стрічка дії (АД 15.11.2026, (А)): лівий низ на базовій лінії шухляди, колір лише в смузі, два рядки за моделлю, «ще N» за
            // виміром шрифту, сховане — у рядку-підказці, поява лише для нового результату; шухляда 472 на 4:3 і не відкривається сама
            if (!CodeLine(week, "if (_last != null) _last.style.bottom = CareerTileLayout.DrawerBottom + lift;") || week.Contains("_last.style.top"))
                bad.Add("WeekScreenView: стрічка дії не на базовій лінії шухляди");
            if (!CodeLine(week, "box.style.maxWidth = ArenaKit.StripW;")
                || !CodeLine(week, "box.Add(new ArenaPlate { Fill = RingTheme.WithAlpha(RingTheme.Panel, BrandTokens.AlphaPanel), Stripe = CareerScreenLines.WeekResultSucceeded(r) ? RingTheme.Gold : RingTheme.Signal });")
                || !CodeLine(week, "head.style.paddingBottom = deltas.Count == 0 ? 8f : 0f;")
                || !CodeLine(week, "var head = MenuKit.Body(CareerScreenLines.WeekResultHead(T, r), BrandTokens.TypeMinInfo, RingTheme.Text);")
                || !CodeLine(week, "var line2 = MenuKit.Body(_stripDeltas ?? \"\", BrandTokens.TypeMinInfo, RingTheme.Text);"))
                bad.Add("WeekScreenView: стрічка дії не стилем панелі грошей або колір не лише в смузі");
            if (!CodeLine(week, "int k = CareerScreenLines.StripParts(T, deltas, fits, out line, out clipped);") || !week.Contains("<= CareerTileLayout.WeekStripTextW;")
                || !CodeLine(week, "line2.style.whiteSpace = WhiteSpace.NoWrap; line2.style.overflow = Overflow.Hidden; line2.style.textOverflow = TextOverflow.Ellipsis;   // рівно один рядок")
                || !CodeLine(week, "_stripHidden = deltas.GetRange(k, deltas.Count - k);"))
                bad.Add("WeekScreenView: рядок 2 стрічки не за виміром «ще N»");
            if (!CodeLine(week, "return CareerScreenLines.WeekResultHover(T, CareerScreenLines.WeekResultRoll(T, r), _stripHidden, fits);")
                || !week.Contains("CareerTileLayout.WeekHintNarrowest, VisualElement.MeasureMode.Exactly")
                || !week.Contains("internal sealed class WeekResultStrip : VisualElement, IPadTarget") || !CodeLine(week, "PadFocus.Hover(this, _show, _hide);")
                || !week.Contains("e.pointerType == PointerType.touch") || !CodeLine(week, "if (_frame == null) { _frame = PadFocus.Frame(); Add(_frame); }"))
                bad.Add("WeekScreenView: сховане стрічки не показується наведенням, фокусом чи дотиком");
            if (!CodeLine(week, "if (_stripDeltas != null) { if (fresh) Appear(box); return box; }") || !CodeLine(week, "_anim.Tick(unscaledDeltaTime * 1000f);")
                || !week.Contains("BrandTokens.MotionMenuPlateInMs, Ease.OutCubic"))
                bad.Add("WeekScreenView: стрічка дії блимає (поява не раз на результат) або рух не з токенів");
            if (!CodeLine(week, "_hintDefault = CareerScreenLines.WeekHintDefault(promo, strained, CareerScreenLines.WeekRumor(T, Host.LastWeekResult));"))
                bad.Add("WeekScreenView: чутки немає в рядку-підказці");
            if (!CodeLine(week, "_dev = CareerTileLayout.WeekDrawerAutoOpen(pw, ph, w.DevPoints);")) bad.Add("WeekScreenView: шухляда на 4:3 відкривається сама");
            if (!CodeLine(show, "string age = CareerScreenLines.AgeingNextLine(T, C, CareerScreenLines.SeasonEndActions(C));")
                || !CodeLine(show, "if (age != null) { var a = MenuKit.Body(age, BrandTokens.TypeMinInfo, RingTheme.Bg, true); a.style.whiteSpace = WhiteSpace.Normal; a.style.marginTop = 8f; col.Add(a); }"))
                bad.Add("ShowScreens: Б5 — рядок «Тіло старіє…» не на папері кінця сезону так, як прийняв директор (Body 15 напівжирний, відступ 8)");
            if (!CodeLine(create, "gname.style.alignSelf = Align.FlexStart;") || !CodeLine(create, "gname.style.marginTop = CareerTileLayout.PosterNameTopShift(s);"))
                bad.Add("CreateScreenView: менша назва афіші не на базовій лінії 52 (У0 директора: не пливе від плашки ролі)");
            // шапка шухляди — ті розміри, за якими CareerTextFitTests.DrawerHead_Hint_… міряє підказки атрибутів і тренерів
            string rui = File.ReadAllText(Path.Combine(ui, "RingUi.cs"));
            if (!week.Contains("ArenaKit.Abs(null, null, ArenaKit.Edge, CareerTileLayout.DrawerBottom, CareerTileLayout.WeekDrawerW, null)")
                || !CodeLine(week, "_drawer.style.width = CareerTileLayout.WeekDrawerWidth(width, Root.layout.height);")
                || !CodeLine(week, "top.style.paddingLeft = 18f; top.style.paddingRight = 24f;")
                || !CodeLine(week, "var head = MenuKit.Display(_dev ? T.Get(\"dev.title\") : T.Get(\"c.hub.shop\"), 25f, RingTheme.Text, RingFontRole.Display900, 0.02f);")
                || !CodeLine(week, "hint.style.marginTop = 0f; hint.style.marginLeft = 16f;")
                || !CodeLine(week, "hint.style.flexGrow = 1f; hint.style.flexShrink = 1f;") || !CodeLine(week, "head.style.flexShrink = 0f;")
                || !CodeLine(week, "var hint = MakeHint();") || week.Contains("DevFooterHint(")
                || !CodeLine(rui, "var h = Lbl(\"\", 12f, Dim);"))
                bad.Add("WeekScreenView: шапка шухляди змінилась — онови вимір підказок (CareerTextFitTests.DrawerHead_Hint_…)");
            if (!CodeLine(create, "t.ReserveCheck = true;   // назва не заходить під галочку вибору (Б4-1: «ХАРДКОРНИЦЯ» на 4:3)")
                || !kit.Contains("CareerTileLayout.TitleWidthBeforeCheck(faceW, left, checkR)") || !CodeLine(kit, "if (ReserveCheck && !float.IsNaN(faceW) && faceW > 0f)")
                || !CodeLine(kit, "float checkR = _low ? CareerTileLayout.LowCheckRight : CareerTileLayout.CheckRight(_compact);")) bad.Add("CreateScreenView / ArenaKit: назва плитки створення — не до галочки");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }
    }
}
