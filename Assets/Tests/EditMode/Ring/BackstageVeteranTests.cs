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
