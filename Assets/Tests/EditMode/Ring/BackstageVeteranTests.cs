using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Власні плани NPC (TRPG_DESIGN §7, черга П5 від ПМ 12.11.2026, пункт 3): «NPC-борці мають ґімік, роль, ворожнечу з гравцем і власні
    /// плани (зрада напарника, повернення ветерана)». Дуга Сталевого: «Друг у лазареті» (раз за кар'єру) → наступного сезону він
    /// повертається ветераном і пам'ятає, чи ти тоді зупинився (ev_veteran) → якщо ви стали напарниками, ще за сезон він забирає твоє
    /// місце в картці (ev_betrayal; лише герой федерації 1 — Сталевий у чоловічому дивізіоні там).
    /// </summary>
    public class BackstageVeteranTests
    {
        private const string Friend = "ev_friend", Veteran = "ev_veteran", Betrayal = "ev_betrayal", Stalevyi = "stalevyi";

        /// <summary>
        /// Новий тиждень сезону <paramref name="season"/>, на якому подія <paramref name="id"/> уже дозріла; події, що стоять у файлі
        /// перед нею, цього сезону вже були (вибір — перша дозріла за порядком файлу). Дії тижня — повні.
        /// </summary>
        private static void Ready(CareerSession s, int season, string id)
        {
            s.State.Season = season;
            var target = s.Content.Backstage.Find(id);
            foreach (var sc in s.Content.Backstage.Scenes)
            {
                if (sc == target) break;
                if (sc.Kind == BackstageKind.Event) s.State.BackstageSeen.Add(season + ":" + sc.Id);
            }
            s.State.Week = s.BackstageDueWeek(target);
            s.State.ActionsLeft = CareerRules.ActionsPerWeek;
            s.State.ActionsDone.Clear();
        }

        /// <summary>Яку сцену відкрило б «Закулісся» зараз — на копії сесії (сам стан не чіпається).</summary>
        private static string WouldOpen(CareerSession s)
        {
            var t = Clone(s);
            var r = t.Backstage();
            Assert.IsTrue(r.Ok, r.Reason);
            return r.SceneId;
        }

        private static CareerSession Clone(CareerSession s)
        {
            var t = CareerKit.Fresh(s.State.Seed, s.Content);
            t.RestoreState(s.CaptureState());
            return t;
        }

        /// <summary>Відкрити «Закулісся» (має відкритись <paramref name="id"/>) і пройти: репліки, варіанти за списком.</summary>
        private static WeekActionResult Play(CareerSession s, string id, List<string> path, params string[] choices)
        {
            var open = s.Backstage();
            Assert.AreEqual(id, open.SceneId);
            var r = s.CurrentBackstage;
            int ci = 0, guard = 0;
            while (!r.IsFinished)
            {
                Assert.Less(++guard, 50);
                if (path != null) path.Add(r.Current.Id);
                if (r.Current.Type == PromoNodeType.Choice)
                {
                    Assert.Less(ci, choices.Length, "варіантів забракло на " + r.Current.Id);
                    r.Choose(choices[ci++]);
                }
                else r.Advance();
            }
            return s.FinishBackstage();
        }

        private static List<string> OpenOptions(CareerSession s)
        {
            var r = s.CurrentBackstage;
            while (r.Current.Type != PromoNodeType.Choice) r.Advance();
            return r.AvailableOptions().Where(v => !v.Locked).Select(v => v.Option.Id).ToList();
        }

        private static CareerSession Heroine(ulong seed)
        {
            var s = CareerKit.NewAtCreate(seed);
            var d = CareerKit.Draft();
            d.Female = true;
            s.CreateHero(d);
            return s;
        }

        /// <summary>Сезон 1 — «Друг у лазареті» з вибором <paramref name="friend"/>; сезон 2 — повернення ветерана з вибором <paramref name="veteran"/>.</summary>
        private static CareerSession Arc(CareerSession s, string friend, string veteran)
        {
            s.State.Money = 100;
            Ready(s, 1, Friend);
            Play(s, Friend, null, friend);
            Ready(s, 2, Veteran);
            Play(s, Veteran, null, veteran);
            return s;
        }

        // ---------------- повернення ветерана ----------------

        [Test]
        public void Veteran_ComesOnlyASeasonAfterTheFriendsInjury_NeverWithout_AndOnce()
        {
            var never = CareerKit.NewHero(41);
            Ready(never, 2, Veteran);
            Assert.AreNotEqual(Veteran, WouldOpen(never), "без «Друга в лазареті» ветерана немає");
            never.State.BackstageSeen.Add("2:" + Friend);
            Assert.AreNotEqual(Veteran, WouldOpen(never), "друг цього сезону — ще не «повернення»: потрібен минулий сезон");

            var s = CareerKit.NewHero(42);
            s.State.Money = 100;
            Ready(s, 1, Friend);
            Play(s, Friend, null, "pay");
            Ready(s, 1, Veteran);
            Assert.AreNotEqual(Veteran, WouldOpen(s), "того самого сезону ветеран не повертається");

            Ready(s, 2, Friend);
            Assert.AreNotEqual(Friend, WouldOpen(s), "травма друга — раз за кар'єру: наступного сезону Сталевий не ламає ногу вдруге");

            Ready(s, 2, Veteran);
            Assert.AreEqual(Veteran, WouldOpen(s), "наступного сезону — повертається");
            Play(s, Veteran, null, "word");
            Ready(s, 3, Veteran);
            Assert.AreNotEqual(Veteran, WouldOpen(s), "повернення — раз за кар'єру");
            Ready(s, 2, Veteran);
            Assert.AreNotEqual(Veteran, WouldOpen(s), "і того самого сезону вдруге — теж ні");
        }

        [Test]
        public void Veteran_RemembersTheHelp_LearnCostsADayAndGivesDevelopment_WordWinsTheLockerRoom()
        {
            var s = CareerKit.NewHero(43);
            s.State.Money = 100;
            Ready(s, 1, Friend);
            Play(s, Friend, null, "time");
            Assert.IsTrue(s.Feud.State.HasFlag("friend_helped"));
            Ready(s, 2, Veteran);

            var learn = Clone(s);
            int dev = learn.Feud.State.DevPoints;
            var path = new List<string>();
            var r = Play(learn, Veteran, path, "learn");
            CollectionAssert.AreEqual(new[] { "h1", "h2", "h3", "hc", "h4" }, path.Where(n => n != "nb").ToArray(), "гілка «допоміг»");
            Assert.AreEqual(dev + 2, learn.Feud.State.DevPoints, "спаринг з ветераном — очки розвитку");
            Assert.AreEqual(-1, r.ActionsDelta, "день на спаринг — ще одна дія");
            Assert.AreEqual(CareerRules.ActionsPerWeek - 2, learn.State.ActionsLeft);
            Assert.IsTrue(learn.Feud.State.HasFlag("veteran_partner"), "ви — напарники");

            var word = Clone(s);
            int locker = word.Feud.State.Locker;
            var rw = Play(word, Veteran, null, "word");
            Assert.AreEqual(locker + 1, word.Feud.State.Locker, "ветеран поручився — роздягальня");
            Assert.AreEqual(1, rw.LockerDelta);
            Assert.AreEqual(dev, word.Feud.State.DevPoints);
            Assert.IsTrue(word.Feud.State.HasFlag("veteran_partner"));

            var tired = Clone(s);
            tired.State.ActionsLeft = 1;
            tired.Backstage();
            Assert.AreEqual(new[] { "word" }, OpenOptions(tired).ToArray(), "без зайвої дії спаринг замкнений");
        }

        [Test]
        public void Veteran_RemembersYouWalkedPast_NoPartnership_AndNoBetrayalEver()
        {
            var s = CareerKit.NewHero(44);
            Ready(s, 1, Friend);
            Play(s, Friend, null, "pass");
            Assert.IsTrue(s.Feud.State.HasFlag("friend_ignored"));
            Ready(s, 2, Veteran);

            var sorry = Clone(s);
            int locker = sorry.Feud.State.Locker;
            var path = new List<string>();
            Play(sorry, Veteran, path, "sorry");
            CollectionAssert.AreEqual(new[] { "i1", "i2", "ic", "i3" }, path.Where(n => n != "nb").ToArray(), "гілка «пройшов повз»");
            Assert.AreEqual(locker + 1, sorry.Feud.State.Locker, "визнав — роздягальня це цінує");

            var shrug = Clone(s);
            shrug.Backstage();
            CollectionAssert.AreEquivalent(new[] { "sorry", "shrug" }, OpenOptions(shrug), "ні спарингу, ні слова за тебе");
            shrug.CurrentBackstage.Choose("shrug");
            while (!shrug.CurrentBackstage.IsFinished) shrug.CurrentBackstage.Advance();
            shrug.FinishBackstage();
            Assert.AreEqual(locker - 1, shrug.Feud.State.Locker);

            foreach (var t in new[] { sorry, shrug })
            {
                Assert.IsFalse(t.Feud.State.HasFlag("veteran_partner"));
                Ready(t, 3, Betrayal);
                Assert.AreNotEqual(Betrayal, WouldOpen(t), "не напарники — нема кому зраджувати");
            }
        }

        // ---------------- зрада напарника ----------------

        [Test]
        public void Betrayal_ASeasonAfterThePartnership_OnlyForTheHeroOfFederation1()
        {
            var s = Arc(CareerKit.NewHero(45), "pay", "word");
            Ready(s, 2, Betrayal);
            Assert.AreNotEqual(Betrayal, WouldOpen(s), "того самого сезону, що й повернення, — ще ні");
            Ready(s, 3, Betrayal);
            Assert.AreEqual(Betrayal, WouldOpen(s), "наступного сезону — зрада");

            var moved = Clone(s);
            moved.EnterFederation(2, s.Content.Federation(2).Floor, 1);
            Ready(moved, 3, Betrayal);
            Assert.AreNotEqual(Betrayal, WouldOpen(moved), "Сталевий виступає у федерації 1 — у старшій федерації його немає");

            var heroine = Arc(Heroine(46), "pay", "word");
            Assert.IsTrue(heroine.Feud.State.HasFlag("veteran_partner"), "ветеран повертається й до героїні");
            Ready(heroine, 3, Betrayal);
            Assert.AreNotEqual(Betrayal, WouldOpen(heroine), "у жіночому дивізіоні Сталевий місця героїні не забере");

            Play(s, Betrayal, null, "let");
            Ready(s, 4, Betrayal);
            Assert.AreNotEqual(Betrayal, WouldOpen(s), "зрада — раз за кар'єру");
        }

        [Test]
        public void Betrayal_FightBrawlsAndHurts_BookerCostsTrust_LetWinsTheLockerRoom()
        {
            var s = Arc(CareerKit.NewHero(47), "pay", "learn");
            Ready(s, 3, Betrayal);

            var fight = Clone(s);
            int feud = fight.FeudLevel(Stalevyi), damage = fight.State.ZoneDamage.Sum();
            var rf = Play(fight, Betrayal, null, "fight");
            Assert.AreEqual(feud + 2, fight.FeudLevel(Stalevyi), "бійка в коридорі — ворожнеча +2");
            Assert.AreEqual(2, rf.FeudDelta);
            Assert.AreEqual(damage + 1, fight.State.ZoneDamage.Sum(), "і синці");

            var booker = Clone(s);
            int trust = booker.Trust;
            var path = new List<string>();
            var rb = Play(booker, Betrayal, path, "booker");
            Assert.AreEqual(feud + 1, booker.FeudLevel(Stalevyi));
            Assert.AreEqual(trust - 1, booker.Trust, "сперечатися з букером про картку — мінус довіра");
            var n5 = booker.Content.Backstage.Find(Betrayal).Promo.Nodes.First(n => n.Id == "n5");
            Assert.AreEqual("booker", n5.Speaker, "відповідає букер федерації 1 — Ліна");
            Assert.AreEqual(booker.Content.Federation(1).Booker, n5.Speaker);
            CollectionAssert.Contains(path, "n5");

            var let = Clone(s);
            int locker = let.Feud.State.Locker;
            Play(let, Betrayal, null, "let");
            Assert.AreEqual(locker + 1, let.Feud.State.Locker, "потиснув руку — роздягальня запам'ятала");
            Assert.AreEqual(feud, let.FeudLevel(Stalevyi), "без ворожнечі");
            Assert.AreEqual(trust, let.Trust);
        }

        [Test]
        public void Arc_SurvivesAFreshRestore_BetweenSeasons()
        {
            var s = Arc(CareerKit.NewHero(48), "time", "word");
            var t = Clone(s);
            Assert.IsTrue(t.Feud.State.HasFlag("friend_helped"));
            Assert.IsTrue(t.Feud.State.HasFlag("veteran_partner"), "напарництво — у сейві");
            Assert.IsTrue(t.BackstageSeenEver(Veteran));
            Ready(t, 3, Betrayal);
            Assert.IsTrue(t.BackstageSeenBefore(Veteran), "минулий сезон — із сейва");
            Assert.AreEqual(Betrayal, WouldOpen(t));
        }

        // ---------------- черга й розплата (рев'ю ПМ 927b86c) ----------------

        /// <summary>Пройти відкриту сцену: у виборі — перший відкритий з <paramref name="prefer"/>, інакше перший відкритий; кидки — як випадуть.</summary>
        private static WeekActionResult PlayPreferring(CareerSession s, params string[] prefer)
        {
            var r = s.CurrentBackstage;
            for (int guard = 0; !r.IsFinished; guard++)
            {
                Assert.Less(guard, 200);
                switch (r.Current.Type)
                {
                    case PromoNodeType.Line: r.Advance(); break;
                    case PromoNodeType.Choice:
                        var open = r.AvailableOptions().Where(v => !v.Locked).Select(v => v.Option.Id).ToList();
                        r.Choose(prefer.FirstOrDefault(open.Contains) ?? open[0]);
                        break;
                    case PromoNodeType.Check:
                        s.BackstageRollCheck();
                        if (r.AwaitingRewards) r.ChooseRewards(r.Current.Rewards.Take(r.RewardsToPick).Select(x => x.Id).ToList());
                        break;
                }
            }
            return s.FinishBackstage();
        }

        /// <summary>
        /// Природна досяжність (рев'ю ПМ 927b86c): жодної підготовки стану — герой федерації 1 щотижня бере «Закулісся» раз, решту
        /// дня тренується, від пропозицій вищих федерацій відмовляється. Щосезонні події (інтерв'ю, парковка, контракт, менеджерка,
        /// поворот) стоять у файлі раніше, але сцени дуги їх випереджають: друг — у сезоні 1, ветеран — у сезоні 2, зрада — у сезоні 3,
        /// і на найближчому звичайному шоу після зради — матч-розплата зі Сталевим.
        /// </summary>
        [Test]
        public void Arc_ComesNaturally_TheBackstageOnceAWeek_VeteranInSeason2_BetrayalInSeason3_ThenThePayback()
        {
            foreach (ulong seed in new ulong[] { 11, 12, 13, 14, 15 })
            {
                var s = CareerKit.NewHero(seed);
                var at = new Dictionary<string, int>();
                var rep = new CareerRunReport();
                bool betrayed = false, checkedPayback = false;
                int shows = 0, guard = 0;
                string lastShow = null;
                while (s.State.Season <= 4 && guard++ < 4000)
                {
                    if (s.Stage == CareerStage.Week)
                    {
                        var r = s.Backstage();
                        if (r.Ok && r.NeedsScene)
                        {
                            if (!at.ContainsKey(r.SceneId)) at[r.SceneId] = s.State.Season;
                            PlayPreferring(s, "time", "word", "booker");
                            if (r.SceneId == Betrayal) betrayed = true;
                        }
                        for (int i = 0; i < 3 && s.State.ActionsLeft > 0; i++) if (!s.Train(false).Ok) break;
                        s.EndWeek();
                    }
                    else if (s.Stage == CareerStage.SeasonEnd)
                    {
                        if (s.CanStartTitleMatch) s.SkipTitle();
                        else if (s.OfferAvailable) s.DeclineOffer();
                        else s.StartNextSeason();
                    }
                    else
                    {
                        string key = s.State.Season + ":" + s.State.Week;
                        if (s.Stage == CareerStage.Show && key != lastShow)
                        {
                            lastShow = key; shows++;
                            bool ordinary = !s.State.IsBig && !s.State.IsTitle && !s.State.IsFeudShow;
                            if (betrayed && !checkedPayback && ordinary)
                            {
                                Assert.AreEqual(Stalevyi, s.State.Opponent, "seed " + seed + ": найближче звичайне шоу після зради — розплата (" + key + ")");
                                checkedPayback = true;
                            }
                        }
                        CareerBot.Step(s, 0, rep);
                    }
                }
                Assert.Less(guard, 4000);
                string seen = string.Join(",", at.Select(kv => kv.Key + "@" + kv.Value));
                Assert.AreEqual(1, s.State.Federation, "seed " + seed + ": герой лишився у федерації 1");
                Assert.AreEqual(1, at.ContainsKey(Friend) ? at[Friend] : -1, "seed " + seed + ": друг у лазареті — сезон 1; " + seen);
                Assert.AreEqual(2, at.ContainsKey(Veteran) ? at[Veteran] : -1, "seed " + seed + ": ветеран — сезон 2; " + seen);
                Assert.AreEqual(3, at.ContainsKey(Betrayal) ? at[Betrayal] : -1, "seed " + seed + ": зрада — сезон 3; " + seen);
                Assert.IsTrue(checkedPayback, "seed " + seed + ": матч-розплата відбувся");
                Assert.Greater(at.Count, 3, "seed " + seed + ": щосезонні події теж трапляються: " + seen);
            }
        }

        [Test]
        public void ArcScenes_GoFirst_SeasonalEventsEarlierInTheFile_DoNotTakeTheirTurn()
        {
            var s = CareerKit.NewHero(51);
            s.State.Money = 100;
            Ready(s, 1, Friend);
            Play(s, Friend, null, "time");
            // сезон 2, тиждень 4: дозріло все, нічого цього сезону ще не було
            s.State.Season = 2; s.State.Week = 4; s.State.ActionsLeft = CareerRules.ActionsPerWeek; s.State.ActionsDone.Clear();
            var earlier = s.Content.Backstage.Scenes.TakeWhile(sc => sc.Id != Veteran).Where(sc => sc.Kind == BackstageKind.Event && !sc.Arc).ToList();
            Assert.Greater(earlier.Count, 3, "у файлі перед ветераном — щосезонні події");
            Assert.AreEqual(Veteran, WouldOpen(s), "дозріла сцена дуги — першою, хоч у файлі вона нижче");
            Play(s, Veteran, null, "word");
            var next = WouldOpen(s);
            Assert.IsTrue(earlier.Any(sc => sc.Id == next), "після сцени дуги — щосезонна подія за порядком файлу, а не " + next);
            foreach (var id in new[] { Friend, Veteran, Betrayal }) Assert.IsTrue(s.Content.Backstage.Find(id).Arc, id + ": сцена дуги");
            Assert.IsFalse(s.Content.Backstage.Scenes.Any(sc => sc.Arc && !new[] { Friend, Veteran, Betrayal }.Contains(sc.Id)), "дуга — лише Сталевого");
        }

        [Test]
        public void Betrayal_FightOrBooker_PutsStalevyiOnThisWeeksCard_LetLeavesTheCard()
        {
            var s = Arc(CareerKit.NewHero(52), "pay", "word");
            Ready(s, 3, Betrayal);
            s.BookShow();
            Assert.IsFalse(s.State.IsBig || s.State.IsTitle || s.State.IsFeudShow, "тиждень " + s.State.Week + " — звичайне шоу");
            string other = s.BookingPool().First(n => n.Id != Stalevyi && n.Id != CareerContent.HammerId).Id;
            Assert.IsTrue(s.ForceOpponent(other));
            var npc = s.Content.FindNpc(Stalevyi);

            foreach (var choice in new[] { "fight", "booker" })
            {
                var t = Clone(s);
                var r = Play(t, Betrayal, null, choice);
                Assert.AreEqual(Stalevyi, t.State.Opponent, choice + ": картку переписано — матч-розплата цього тижня");
                Assert.IsTrue(r.OpponentChanged, choice);
                Assert.AreEqual("", t.State.PendingOpponent, choice + ": обіцянку виконано");
                // план і умови — ті, що склав би букер для Сталевого вже після сцени (довіра −1, ворожнеча +1/+2)
                Assert.AreEqual(ShowBooker.PlanFor(npc, t.Popularity, t.Trust, t.PopularityFloor, t.PopularityCeiling), t.State.Plan, choice + ": план");
                Assert.AreEqual(ShowBooker.StipulationsFor(t.FeudLevel(Stalevyi)), t.State.Stip, choice + ": умови матчу — за ворожнечею");
                Assert.AreEqual(Stalevyi, t.GetShowCard().Opponent.Id, choice + ": картка тижня");
            }
            var let = Clone(s);
            var rl = Play(let, Betrayal, null, "let");
            Assert.AreEqual(other, let.State.Opponent, "відпустив — картка та сама");
            Assert.IsFalse(rl.OpponentChanged);
            Assert.AreEqual("", let.State.PendingOpponent);

            // ворожнеча зі Сталевим уже гаряча: після сцени вона ≥ NoDqFeudLevel, і розплата йде без дискваліфікацій,
            // хоча матч з попереднім суперником (ворожнеча 0) був звичайним — умови перераховано разом із суперником
            var hot = Clone(s);
            hot.State.SetNpcFeud(Stalevyi, CareerRules.NoDqFeudLevel - 1);
            Assert.AreEqual(MatchStipulations.None, hot.State.Stip, "з попереднім суперником — звичайний матч");
            Play(hot, Betrayal, null, "booker");
            Assert.AreEqual(Stalevyi, hot.State.Opponent);
            Assert.AreEqual(MatchStipulations.NoDq, hot.State.Stip, "розплата — без дискваліфікацій");
        }

        [Test]
        public void Betrayal_OnTheBigShowWeek_ThePaybackWaitsForTheNextOrdinaryShow_ThroughTheSave()
        {
            var s = Arc(CareerKit.NewHero(53), "pay", "word");
            Ready(s, 3, Betrayal);
            s.State.Week = CareerRules.BigShowEvery;
            s.BookShow();
            Assert.IsTrue(s.State.IsBig, "тиждень великого шоу");
            string card = s.State.Opponent;
            var r = Play(s, Betrayal, null, "fight");
            Assert.AreEqual(card, s.State.Opponent, "картку великого шоу (і ворожнечі з Молотом) не переписано");
            Assert.IsFalse(r.OpponentChanged);
            Assert.AreEqual(Stalevyi, s.State.PendingOpponent, "розплата чекає на звичайне шоу");

            var t = Clone(s);
            Assert.AreEqual(Stalevyi, t.State.PendingOpponent, "обіцянка — у сейві");
            t.State.Season = 4; t.State.Week = 1;
            t.BookShow();
            Assert.IsFalse(t.State.IsBig);
            Assert.AreEqual(Stalevyi, t.State.Opponent, "наступне звичайне шоу — проти Сталевого");
            Assert.AreEqual("", t.State.PendingOpponent);
            t.State.Week = 2;
            t.BookShow();
            Assert.AreEqual("", t.State.PendingOpponent, "обіцянка — одна: далі картку складає букер як завжди");

            var moved = Clone(s);
            moved.EnterFederation(2, s.Content.Federation(2).Floor, 1);
            moved.State.Season = 4; moved.State.Week = 1;
            moved.BookShow();
            Assert.AreNotEqual(Stalevyi, moved.State.Opponent, "у федерації 2 Сталевого немає");
            Assert.AreEqual("", moved.State.PendingOpponent, "на першому звичайному шоу нової федерації обіцянку знято");

            var old = CareerKit.Fresh(s.State.Seed, s.Content);
            old.RestoreState(System.Text.RegularExpressions.Regex.Replace(s.CaptureState(), @"\|popp=[^|]*", ""));
            Assert.AreEqual("", old.State.PendingOpponent, "старий сейв без обіцянки читається");
        }

        [Test]
        public void FriendAndVeteran_OnlyInFederation1_LikeTheBetrayal_TheArcDoesNotBreakHalfway()
        {
            var high = CareerKit.NewHero(54);
            high.EnterFederation(2, high.Content.Federation(2).Floor, 1);
            Ready(high, 1, Friend);
            Assert.AreNotEqual(Friend, WouldOpen(high), "друга в лазареті у федерації 2 немає: Сталевий — з місцевої");

            var s = CareerKit.NewHero(55);
            s.State.Money = 100;
            Ready(s, 1, Friend);
            Play(s, Friend, null, "time");
            var moved = Clone(s);
            moved.EnterFederation(2, s.Content.Federation(2).Floor, 1);
            Ready(moved, 2, Veteran);
            Assert.AreNotEqual(Veteran, WouldOpen(moved), "герой пішов у федерацію 2 — ветеран не повертається, напарництва без зради не буде");
            Ready(s, 2, Veteran);
            Assert.AreEqual(Veteran, WouldOpen(s), "у федерації 1 — повертається");

            foreach (var sc in s.Content.Backstage.Scenes.Where(x => x.Arc))
                Assert.IsTrue(sc.Requires.Any(c => c.Kind == ConditionKind.Career && c.Key == "tier" && c.Min == 2 && c.Negate),
                    sc.Id + ": сцена дуги — лише федерація 1");
        }

        [Test]
        public void Loader_ArcOnlyForBackstageEvents_OpponentNeedsARosterNpc_WithoutAdd()
        {
            Func<string, Dictionary<string, object>> o = MiniJson.ParseObject;
            Assert.Throws<FormatException>(() => FeudEffect.FromJson(o("{\"career\":\"opponent\"}"), "t"), "без цілі");
            Assert.Throws<FormatException>(() => FeudEffect.FromJson(o("{\"career\":\"opponent\",\"target\":\"stalevyi\",\"add\":1}"), "t"), "з add");
            Assert.AreEqual(Stalevyi, FeudEffect.FromJson(o("{\"career\":\"opponent\",\"target\":\"stalevyi\"}"), "t").Target);

            string bs = CareerKit.Read("career_backstage.json");
            Func<string, CareerContent> load = j => CareerContent.Load(CareerKit.Read("feud_hammer.json"), CareerKit.Read("career_moves.json"),
                CareerKit.Read("career_gimmicks.json"), CareerKit.Read("career_roster.json"), j);
            Assert.IsNotNull(load(bs));
            const string pay = "\"career\": \"opponent\",\r\n         \"target\": \"stalevyi\"";
            string payLf = pay.Replace("\r\n", "\n");
            string real = bs.Contains(pay) ? pay : payLf;
            Assert.AreEqual(2, bs.Split(new[] { real }, StringSplitOptions.None).Length - 1, "розплата — у двох варіантах зради");
            StringAssert.Contains("opponent", Assert.Throws<FormatException>(() => load(bs.Replace(real, real.Replace("stalevyi", "nobody")))).Message);
            StringAssert.Contains("opponent", Assert.Throws<FormatException>(() => load(bs.Replace(real, real.Replace("stalevyi", CareerContent.HammerId)))).Message);

            const string once = "\"id\": \"booker_talk\",";
            Assert.AreEqual(1, bs.Split(new[] { once }, StringSplitOptions.None).Length - 1);
            StringAssert.Contains("arc", Assert.Throws<FormatException>(() => BackstageContent.Load(bs.Replace(once, once + " \"arc\": true,"))).Message,
                "розмова з букером — не сцена дуги");
            const string mixed = "\"id\": \"ev_mixed\",";
            StringAssert.Contains("arc", Assert.Throws<FormatException>(() => BackstageContent.Load(bs.Replace(mixed, mixed + " \"arc\": true,"))).Message,
                "подія, що відкривається сама, — не сцена дуги");
        }

        // ---------------- завантажувач ----------------

        [Test]
        public void Loader_SeenBeforeNeedsAScene_AndAKnownOne()
        {
            Func<string, Dictionary<string, object>> o = MiniJson.ParseObject;
            Assert.Throws<FormatException>(() => FeudCondition.FromJson(o("{\"career\":\"seen_before\"}"), "t"), "без id сцени");
            Assert.AreEqual(Friend, FeudCondition.FromJson(o("{\"career\":\"seen_before\",\"target\":\"ev_friend\"}"), "t").Target);

            string bs = CareerKit.Read("career_backstage.json");
            const string real = "\"target\": \"ev_friend\"";
            Assert.AreEqual(1, bs.Split(new[] { real }, StringSplitOptions.None).Length - 1, "умова ветерана в даних — одна");
            var e = Assert.Throws<FormatException>(() => CareerContent.Load(CareerKit.Read("feud_hammer.json"), CareerKit.Read("career_moves.json"),
                CareerKit.Read("career_gimmicks.json"), CareerKit.Read("career_roster.json"), bs.Replace(real, "\"target\": \"ev_nope\"")));
            StringAssert.Contains("seen_before", e.Message);
        }
    }
}
