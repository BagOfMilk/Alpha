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
        /// №11: пізня роль — на останньому кроці кінця сезону (лише «Новий сезон»): не поряд із титульним шансом і контрактом. Рядок під
        /// кнопками каже обрану словами й «діє з наступного сезону»; без ролі — як працює вибір.
        /// </summary>
        [Test]
        public void LateRole_Block_OnTheLastStep_SaysTheChoice()
        {
            var s = AtSeasonEnd(3, 1, 3, CareerRules.OfferTrust);
            Assert.IsFalse(CareerScreenLines.LateRoleShown(s, CareerScreenLines.SeasonEndActions(s)), "поки пропозиція — не тут");
            s.DeclineOffer();
            Assert.IsTrue(CareerScreenLines.LateRoleShown(s, CareerScreenLines.SeasonEndActions(s)));
            var m = new LateRoleModel(s, T);
            Assert.AreEqual(m.HintText, CareerScreenLines.LateRoleNote(T, m), "без ролі — як працює вибір");
            Assert.IsTrue(m.Choose(LateRole.Icon));
            m = new LateRoleModel(s, T);
            string note = CareerScreenLines.LateRoleNote(T, m);
            NoMissingKey(note, "рядок пізньої ролі");
            StringAssert.StartsWith(T.Get("c.late.icon") + ": ", note);
            StringAssert.EndsWith(T.Get("c.late.from_next"), note);
            StringAssert.DoesNotContain("..", note);
            Assert.IsTrue(m.Choose(LateRole.OffRingStar));
            StringAssert.EndsWith(T.Get("c.late.from_next"), CareerScreenLines.LateRoleNote(T, new LateRoleModel(s, T)), "опис без крапки — крапку додано");
            Assert.AreEqual(T.Format("c.late.current", T.Get("c.late.star")), new LateRoleModel(s, T).CurrentText, "у хабі — рядок нинішньої ролі");

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
            if (Regex.Matches(career, Regex.Escape("CareerScreenLines.WithReason(")).Count < 2) bad.Add("CareerScreens: «опис. · причина» (правка 6)");
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
