using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core.Randomness;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Core.Ring.Match;
using Game.Gameplay.Combat;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Закулісні сцени кар'єри (TRPG_DESIGN §6.1, §7): розмова з букером і події поза рингом — граф у форматі промо,
    /// вибір сцени за станом, наслідки в шкали/гроші/дії/травми, сейв.
    /// </summary>
    public class BackstageTests
    {
        private static readonly string[] EventIds = { "ev_parking", "ev_interview", "ev_contract", "ev_friend" };

        private static CareerContent Shared() { return CareerKit.Content(); }

        private static CareerSession Hero(CareerContent c, ulong seed = 1, int week = 1, int season = 1, bool allEventsSeen = false)
        {
            var s = CareerKit.NewAtCreate(seed, c);
            s.CreateHero(CareerKit.Draft(Gimmick.Showman, CareerRole.Hero, RingAttr.Strength));
            s.State.Week = week; s.State.Season = season;
            if (allEventsSeen) foreach (var id in EventIds) s.State.BackstageSeen.Add(season + ":" + id);
            return s;
        }

        /// <summary>Сесія, де першою відкриється подія id: попередні події цього сезону вже були, тиждень — останній (усе дозріло).</summary>
        private static CareerSession HeroForEvent(CareerContent c, string id, ulong seed = 1)
        {
            var s = Hero(c, seed, 4);
            foreach (var e in EventIds)
            {
                if (e == id) break;
                s.State.BackstageSeen.Add("1:" + e);
            }
            return s;
        }

        /// <summary>Пройти сцену за сценарієм виборів (id варіантів по черзі; далі — перший доступний); кидки — заданий ступінь.</summary>
        private static WeekActionResult Play(CareerSession s, string[] choices, RollTier tier = RollTier.Strong)
        {
            var r = s.CurrentBackstage;
            int ci = 0, guard = 0;
            while (!r.IsFinished)
            {
                Assert.Less(++guard, 200);
                switch (r.Current.Type)
                {
                    case PromoNodeType.Line: r.Advance(); break;
                    case PromoNodeType.Choice:
                        string want = ci < choices.Length ? choices[ci++] : null;
                        PromoOptionView pick = null;
                        foreach (var v in r.AvailableOptions())
                        {
                            if (v.Locked) continue;
                            if (want == null || v.Option.Id == want) { pick = v; break; }
                        }
                        Assert.IsNotNull(pick, "немає доступного варіанта " + want + " у " + r.Current.Id);
                        r.Choose(pick.Option.Id);
                        break;
                    case PromoNodeType.Check:
                        r.ApplyCheckTier(tier);
                        if (r.AwaitingRewards)
                        {
                            var ids = new List<string>();
                            for (int i = 0; i < r.RewardsToPick; i++) ids.Add(r.Current.Rewards[i].Id);
                            r.ChooseRewards(ids);
                        }
                        break;
                }
            }
            return s.FinishBackstage();
        }

        // ---------- дані ----------

        [Test]
        public void Scenes_PassTheValidator_AndEveryTextKeyIsInBackstageFile()
        {
            var c = Shared();
            var own = new Dictionary<string, string>();
            foreach (var kv in MiniJson.ParseObject(CareerKit.Read("backstage_uk.json"))) own[kv.Key] = (string)kv.Value;
            var errors = PromoValidator.ValidateScenes(c.Backstage.Promos(), own);
            Assert.IsEmpty(errors, string.Join("\n", errors.Select(e => e.ToString())));
        }

        [Test]
        public void Content_HasTheBookerTalkAndTheFourEvents()
        {
            var c = Shared();
            Assert.IsNotNull(c.Backstage);
            Assert.AreEqual(BackstageKind.Booker, c.Backstage.Find("booker_talk").Kind);
            foreach (var id in EventIds) Assert.AreEqual(BackstageKind.Event, c.Backstage.Find(id).Kind, id);
        }

        [Test]
        public void BackstageFile_HasNoUnusedKeys_NoRussianIsms_NoRealBrands_AndSharedSpeakersMatchTheFeudFile()
        {
            string scenes = CareerKit.Read("career_backstage.json");
            var own = MiniJson.ParseObject(CareerKit.Read("backstage_uk.json"));
            var shared = MiniJson.ParseObject(CareerKit.Read("strings_uk.json"));
            var banned = new[] { "wwe", "aew", "hulk", "undertaker", "cena", "triple h", "nxt", "wcw" };
            foreach (var kv in own)
            {
                bool service = kv.Key.StartsWith("speaker.") || kv.Key.StartsWith("week.reason.") || kv.Key.StartsWith("bs.kind.") || kv.Key.StartsWith("bs.ui.");
                if (!service) StringAssert.Contains("\"" + kv.Key + "\"", scenes, "невикористаний ключ " + kv.Key);
                string v = (string)kv.Value;
                Assert.IsFalse(string.IsNullOrWhiteSpace(v), kv.Key);
                foreach (var bad in new[] { "ы", "э", "ё", "ъ" }) StringAssert.DoesNotContain(bad, v.ToLowerInvariant(), kv.Key);
                foreach (var b in banned) StringAssert.DoesNotContain(b, v.ToLowerInvariant(), kv.Key);
                if (shared.ContainsKey(kv.Key)) Assert.AreEqual(shared[kv.Key], kv.Value, "ключ " + kv.Key + " розходиться зі strings_uk.json");
            }
        }

        [Test]
        public void Speakers_AreTheBookerThePlayerTheNarratorTheCrowdOrAnNpcFromTheRoster_AndHaveNames()
        {
            var c = Shared();
            var own = MiniJson.ParseObject(CareerKit.Read("backstage_uk.json"));
            var used = new HashSet<string>();
            foreach (var p in c.Backstage.Promos())
                foreach (var n in p.Nodes)
                    if (n.Type == PromoNodeType.Line) used.Add(n.Speaker);
            foreach (var sp in used)
            {
                Assert.IsTrue(c.FindNpc(sp) != null || Array.IndexOf(CareerContent.ExtraSpeakers, sp) >= 0, "мовець " + sp);
                Assert.IsTrue(own.ContainsKey("speaker." + sp), "немає імені мовця " + sp);
            }
            Assert.IsFalse(used.Contains(CareerContent.HammerId), "Молот — у ворожнечі, а не в закулісних сценах кар'єри");
        }

        [Test]
        public void Loader_RejectsUnknownSpeakers_UnknownNpcTargets_AndFeudOnlyEffects()
        {
            string template = "{\"scenes\":[{\"id\":\"booker_talk\",\"kind\":\"booker\",\"start\":\"n1\",\"nodes\":["
                + "{\"id\":\"n1\",\"type\":\"line\",\"speaker\":\"SPEAKER\",\"text\":\"k\",\"next\":\"c\"},"
                + "{\"id\":\"c\",\"type\":\"choice\",\"options\":[{\"id\":\"o\",\"text\":\"k\",\"effects\":[EFFECT],\"next\":\"end\"}]},"
                + "{\"id\":\"end\",\"type\":\"end\"}]}]}";
            Func<string, string, CareerContent> load = (sp, ef) => CareerContent.Load(CareerKit.Read("feud_hammer.json"), CareerKit.Read("career_moves.json"),
                CareerKit.Read("career_gimmicks.json"), CareerKit.Read("career_roster.json"), template.Replace("SPEAKER", sp).Replace("EFFECT", ef));
            Assert.IsNotNull(load("booker", "{\"flag\":\"x\"}").Backstage);
            Assert.Throws<FormatException>(() => load("ghost", "{\"flag\":\"x\"}"));
            Assert.Throws<FormatException>(() => load("booker", "{\"career\":\"npc_feud\",\"target\":\"ghost\",\"add\":1}"));
            Assert.Throws<FormatException>(() => load("booker", "{\"plan\":\"win_any\"}"));
            Assert.Throws<FormatException>(() => load("booker", "{\"career\":\"teleport\",\"add\":1}"));
            Assert.Throws<FormatException>(() => load("booker", "{\"career\":\"injure\",\"target\":\"elbow\",\"add\":1}"));
            Assert.IsNotNull(load("baron", "{\"career\":\"money\",\"add\":5}"));
        }

        [Test]
        public void CareerConditionsAndEffects_AreInertWithoutTheSession_SoTheHammerFeudIsUntouched()
        {
            var st = new FeudState();
            var cond = FeudCondition.FromJson(MiniJson.ParseObject("{\"career\":\"money\",\"min\":1}"), "t");
            Assert.IsFalse(st.Check(cond));
            var eff = FeudEffect.FromJson(MiniJson.ParseObject("{\"career\":\"money\",\"add\":50}"), "t");
            string before = st.CaptureState();
            st.Apply(eff);
            Assert.AreEqual(before, st.CaptureState());
            var neg = FeudCondition.FromJson(MiniJson.ParseObject("{\"var\":\"trust\",\"min\":3,\"not\":true}"), "t");
            Assert.IsTrue(st.Check(neg), "довіра 2 < 3, тож «not» виконано");
            Assert.AreEqual("not:var:trust>=3", neg.ToString());
        }

        // ---------- обхід усіх шляхів ----------

        private static List<List<int>> Combos(int n, int k)
        {
            var res = new List<List<int>>();
            Action<int, List<int>> rec = null;
            rec = (from, cur) =>
            {
                if (cur.Count == k) { res.Add(new List<int>(cur)); return; }
                for (int i = from; i < n; i++) { cur.Add(i); rec(i + 1, cur); cur.RemoveAt(cur.Count - 1); }
            };
            rec(0, new List<int>());
            return res;
        }

        /// <summary>Обійти всі шляхи сцени (вибори, ступені кидків, набори нагород), кожен — у свіжій сесії; повертає кількість шляхів.</summary>
        private static int Explore(Func<CareerSession> make, string scene, HashSet<string> visited)
        {
            var queue = new Queue<List<int>>();
            queue.Enqueue(new List<int>());
            int paths = 0, guard = 0;
            while (queue.Count > 0)
            {
                Assert.Less(++guard, 3000, "забагато шляхів у " + scene);
                var script = queue.Dequeue();
                var s = make();
                var r0 = s.Backstage();
                Assert.IsTrue(r0.NeedsScene, "сцена не відкрилась: " + r0.Reason);
                Assert.AreEqual(scene, r0.SceneId);
                var run = s.CurrentBackstage;
                int di = 0;
                bool fanned = false;
                while (!run.IsFinished && !fanned)
                {
                    visited.Add(run.Current.Id);
                    switch (run.Current.Type)
                    {
                        case PromoNodeType.Line: run.Advance(); break;
                        case PromoNodeType.Choice:
                            var open = run.AvailableOptions().Where(v => !v.Locked).ToList();
                            Assert.Greater(open.Count, 0, "тупик на " + run.Current.Id);
                            if (di < script.Count) run.Choose(open[script[di++]].Option.Id);
                            else
                            {
                                for (int k = 0; k < open.Count; k++) queue.Enqueue(new List<int>(script) { k });
                                fanned = true;
                            }
                            break;
                        case PromoNodeType.Check:
                            var n = run.Current;
                            int p7 = Math.Min(n.Pick7, n.Rewards.Count), p10 = Math.Min(n.Pick10, n.Rewards.Count);
                            var decisions = new List<KeyValuePair<RollTier, List<int>>> { new KeyValuePair<RollTier, List<int>>(RollTier.Miss, null) };
                            foreach (var cb in Combos(n.Rewards.Count, p7)) decisions.Add(new KeyValuePair<RollTier, List<int>>(RollTier.Partial, cb));
                            foreach (var cb in Combos(n.Rewards.Count, p10)) decisions.Add(new KeyValuePair<RollTier, List<int>>(RollTier.Strong, cb));
                            if (di < script.Count)
                            {
                                var d = decisions[script[di++]];
                                run.ApplyCheckTier(d.Key);
                                if (run.AwaitingRewards) run.ChooseRewards(d.Value.Select(i => n.Rewards[i].Id).ToList());
                            }
                            else
                            {
                                for (int k = 0; k < decisions.Count; k++) queue.Enqueue(new List<int>(script) { k });
                                fanned = true;
                            }
                            break;
                    }
                }
                if (fanned) continue;
                visited.Add("end");
                var res = s.FinishBackstage();
                Assert.IsTrue(res.Ok);
                Assert.IsFalse(s.BackstageOpen);
                Assert.GreaterOrEqual(s.Money, 0);
                paths++;
            }
            return paths;
        }

        private static void AssertAllNodesVisited(CareerContent c, string scene, HashSet<string> visited)
        {
            foreach (var n in c.Backstage.Find(scene).Promo.Nodes)
                if (n.Type != PromoNodeType.Branch) Assert.IsTrue(visited.Contains(n.Id), scene + ": вузол " + n.Id + " не відвідано жодним шляхом");
        }

        [Test]
        public void EveryEvent_EveryPath_ReachesTheEnd_AndEveryNodeIsReachable()
        {
            var c = Shared();
            foreach (var id in EventIds)
            {
                var visited = new HashSet<string>();
                // три варіанти стану: звичайний, без грошей і з однією дією (замкнені варіанти «друга»), щоб відвідати й гілки з умовами
                int total = 0;
                total += Explore(() => HeroForEvent(c, id), id, visited);
                total += Explore(() => { var s = HeroForEvent(c, id); s.State.ActionsLeft = 1; return s; }, id, visited);
                total += Explore(() => { var s = HeroForEvent(c, id); s.State.Money = 0; s.Feud.State.Trust = 3; return s; }, id, visited);
                Assert.Greater(total, 3, id);
                AssertAllNodesVisited(c, id, visited);
            }
        }

        [Test]
        public void BookerTalk_EveryPath_ReachesTheEnd_AcrossAllFiveSubTalks()
        {
            var c = Shared();
            var visited = new HashSet<string>();
            var variants = new List<Func<CareerSession>>
            {
                () => { var s = Hero(c, 1, 1, 1, true); s.Feud.State.Trust = 1; return s; },                                         // холодна
                () => { var s = Hero(c, 1, 1, 1, true); s.Feud.State.Trust = 3; s.State.LastOutcome = 2; s.State.LastOpponent = "dzhura"; return s; },   // поразка
                () => { var s = Hero(c, 1, 1, 1, true); s.Feud.State.Trust = 4; return s; },                                         // титульний натяк
                () => { var s = Hero(c, 1, 2, 1, true); s.Feud.State.Trust = 3; s.State.Plan = BookerPlan.WinClean; return s; },      // план
                () => { var s = Hero(c, 1, 1, 1, true); s.Feud.State.Trust = 2; s.State.LastOutcome = 1; return s; },                 // після перемоги
                () => { var s = Hero(c, 1, 1, 1, true); s.Feud.State.Trust = 2; return s; }                                          // звичайна
            };
            foreach (var v in variants) Explore(v, "booker_talk", visited);
            AssertAllNodesVisited(c, "booker_talk", visited);
        }

        [Test]
        public void BookerTalk_PicksTheSubTalkByState()
        {
            var c = Shared();
            Func<CareerSession, string> second = s =>
            {
                s.Backstage();
                var r = s.CurrentBackstage;
                Assert.AreEqual("n1", r.Current.Id);
                r.Advance();
                return r.Current.Id;
            };
            Func<int, int, Action<CareerSession>, string> at = (trust, week, setup) =>
            {
                var s = Hero(c, 1, week, 1, true);
                s.Feud.State.Trust = trust;
                if (setup != null) setup(s);
                return second(s);
            };
            Assert.AreEqual("c1", at(1, 1, null), "довіра < 2");
            Assert.AreEqual("c1", at(0, 1, s => s.State.LastOutcome = 2), "холодна важить більше за поразку");
            Assert.AreEqual("l1", at(2, 1, s => s.State.LastOutcome = 2), "після поразки");
            Assert.AreEqual("t1", at(4, 1, null), "титульний натяк");
            Assert.AreEqual("p1", at(3, 2, s => s.State.Plan = BookerPlan.WinClean), "план на найближче шоу, парний тиждень");
            Assert.AreEqual("d2", at(3, 1, s => s.State.Plan = BookerPlan.WinClean).Replace("dn", "d2").Replace("dw", "d2"), "непарний тиждень — звичайна розмова");
            Assert.AreEqual("dw", at(2, 1, s => s.State.LastOutcome = 1), "після перемоги");
            Assert.AreEqual("dn", at(2, 3, null), "без особливостей");
        }

        // ---------- вибір сцени ----------

        [Test]
        public void ThereIsAlwaysABookerTalkWhenNoEventIsDue()
        {
            var c = Shared();
            var s = Hero(c, 1, 1, 1, true);
            var r = s.Backstage();
            Assert.IsTrue(r.NeedsScene);
            Assert.AreEqual("booker_talk", r.SceneId);
            Assert.AreEqual(BackstageKind.Booker, r.SceneKind);
            Assert.IsFalse(r.HasRoll, "кубика на відкритті немає");
            Assert.AreEqual(3, s.State.ActionsLeft, "дію ще не витрачено");
        }

        [Test]
        public void SceneChoice_IsDeterministic_AndIndependentOfTheDice()
        {
            var c = Shared();
            Func<ulong, List<string>> run = dice =>
            {
                var s = CareerSession.New(c, new SeededDiceRoller(dice), 7);
                s.StartNewGame();
                s.CreateHero(CareerKit.Draft(Gimmick.Showman, CareerRole.Hero, RingAttr.Strength));
                var seen = new List<string>();
                for (int w = 1; w <= 4; w++)
                {
                    s.State.Week = w;
                    var r = s.Backstage();
                    seen.Add(w + ":" + r.SceneId);
                    CareerKit.PlayScene(s, 0);
                    s.State.ActionsLeft = 3;
                }
                return seen;
            };
            CollectionAssert.AreEqual(run(1), run(1));
            CollectionAssert.AreEqual(run(1), run(12345), "вибір сцени не залежить від кубика, лише від стану");
        }

        [Test]
        public void AnEvent_IsNotBeforeItsWeek_AndHappensOncePerSeason()
        {
            var c = Shared();
            var s = Hero(c, 3, 1);
            foreach (var id in EventIds)
            {
                var sc = c.Backstage.Find(id);
                Assert.That(s.BackstageDueWeek(sc), Is.InRange(sc.FromWeek, Math.Min(CareerRules.WeeksInSeason(1), sc.FromWeek + sc.Spread - 1)), id);
            }
            // тиждень 1: події з fromWeek ≥ 2 ще не доступні
            string first = s.Backstage().SceneId;
            Assert.IsTrue(first == "booker_talk" || first == "ev_interview", first);
            CareerKit.PlayScene(s, 0);
            // тиждень 4: усе дозріло, кожна подія — раз, потім лише розмова
            s.State.Week = 4;
            var seen = new List<string>();
            for (int i = 0; i < 6; i++)
            {
                s.State.ActionsLeft = 3;
                var r = s.Backstage();
                seen.Add(r.SceneId);
                CareerKit.PlayScene(s, 0);
            }
            foreach (var id in EventIds) Assert.LessOrEqual(seen.Count(x => x == id) + (first == id ? 1 : 0), 1, id + ": " + string.Join(",", seen));
            Assert.AreEqual("booker_talk", seen.Last());
            // наступний сезон: події знову можливі
            s.State.Season = 2; s.State.Week = 4; s.State.ActionsLeft = 3;
            Assert.AreEqual(BackstageKind.Event, s.Backstage().SceneKind);
        }

        [Test]
        public void AnEvent_WaitsForItsRequirements()
        {
            var c = Shared();
            var s = HeroForEvent(c, "ev_interview");
            s.Feud.State.Audience = 1;   // інтерв'ю потребує популярності ≥ 2
            Assert.AreNotEqual("ev_interview", s.Backstage().SceneId);
        }

        [Test]
        public void EachEvent_HappensWithin_ThreeSeasons_OfABotThatVisitsTheBackstageTwiceAWeek()
        {
            foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5, 6 })
            {
                var c = Shared();
                var s = CareerKit.NewAtCreate(seed, c);
                s.CreateHero(CareerKit.Draft(Gimmick.Showman, CareerRole.Hero, RingAttr.Strength));
                var rep = new CareerRunReport();
                var perSeason = new Dictionary<string, int>();
                var all = new HashSet<string>();
                int guard = 0;
                while (s.State.Season <= 3 && guard++ < 3000)
                {
                    if (s.Stage == CareerStage.Week)
                    {
                        int n = 0;
                        while (s.State.ActionsLeft > 0 && n < 2)
                        {
                            int season = s.State.Season;
                            var r = s.Backstage();
                            if (!r.Ok) break;
                            string k = season + ":" + r.SceneId;
                            perSeason[k] = (perSeason.ContainsKey(k) ? perSeason[k] : 0) + 1;
                            all.Add(r.SceneId);
                            CareerKit.PlayScene(s, n++);
                        }
                        if (s.State.ActionsLeft > 0) s.Train(false);
                        s.EndWeek();
                    }
                    else CareerBot.Step(s, 1, rep);
                }
                Assert.Less(guard, 3000);
                foreach (var id in EventIds) Assert.IsTrue(all.Contains(id), "seed " + seed + ": подія " + id + " не трапилась за три сезони; були: " + string.Join(",", all));
                foreach (var kv in perSeason) if (kv.Key.Contains(":ev_")) Assert.AreEqual(1, kv.Value, "seed " + seed + ": " + kv.Key + " більше ніж раз за сезон");
            }
        }

        // ---------- дія тижня ----------

        [Test]
        public void WhileASceneIsOpen_OtherActionsEndWeekAndDevelopmentAreUnavailable_AndFinishSpendsExactlyOneAction()
        {
            var c = Shared();
            var s = Hero(c, 1, 1, 1, true);
            s.Feud.State.DevPoints = 5;
            Assert.IsFalse(s.GetWeekView().BackstageOpen);
            Assert.IsTrue(s.Backstage().NeedsScene);
            Assert.IsTrue(s.BackstageOpen);
            Assert.IsTrue(s.GetWeekView().BackstageOpen);
            Assert.AreEqual("scene_open", s.Train(false).Reason);
            Assert.AreEqual("scene_open", s.SideJob().Reason);
            Assert.AreEqual("scene_open", s.Promo().Reason);
            Assert.AreEqual("scene_open", s.Backstage().Reason);
            Assert.AreEqual("scene_open", s.Recover(BodyZone.Body, false).Reason);
            Assert.Throws<InvalidOperationException>(() => s.EndWeek());
            string reason;
            Assert.IsFalse(s.CanBuyAttr(RingAttr.Strength, out reason));
            Assert.Throws<InvalidOperationException>(() => s.FinishBackstage(), "сцену ще не закінчено");
            Assert.AreEqual(3, s.State.ActionsLeft);

            var r = CareerKit.PlayScene(s, 0);
            Assert.IsTrue(r.Ok);
            Assert.AreEqual(WeekActionKind.Backstage, r.Kind);
            Assert.IsFalse(s.BackstageOpen);
            Assert.AreEqual(2, s.State.ActionsLeft, "рівно одна дія");
            Assert.AreEqual(1, s.State.ActionsDone.Count);
            Assert.AreEqual(1, s.State.ActionTally[(int)WeekActionKind.Backstage]);
            Assert.IsTrue(s.Train(false).Ok);
            s.EndWeek();
        }

        [Test]
        public void NoActionsLeft_NoSceneOpens()
        {
            var s = Hero(Shared(), 1, 1, 1, true);
            s.State.ActionsLeft = 0;
            var r = s.Backstage();
            Assert.IsFalse(r.Ok);
            Assert.AreEqual("no_actions", r.Reason);
            Assert.IsFalse(s.BackstageOpen);
        }

        [Test]
        public void Rolls_InScenes_GoThroughTheCareerDice_AndAreDeterministicForASeed()
        {
            var c = Shared();
            Func<CareerSession> make = () => { var s = Hero(c, 9, 1, 1, true); s.Feud.State.Trust = 1; return s; };
            var a = make(); var b = make();
            a.Backstage(); b.Backstage();
            // холодна розмова: «pitch» веде до кидка Харизми
            foreach (var s in new[] { a, b })
            {
                var r = s.CurrentBackstage;
                r.Advance();                    // n1 → c1 (branch пройдено)
                r.Advance();                    // c1 → c2
                r.Choose("pitch");
                Assert.AreEqual(PromoNodeType.Check, r.Current.Type);
            }
            var ta = a.BackstageRollCheck(); var tb = b.BackstageRollCheck();
            Assert.AreEqual(ta, tb);
            Assert.AreEqual(a.CurrentBackstage.LastTotal, b.CurrentBackstage.LastTotal);
            Assert.AreEqual(a.Dice.CaptureState(), b.Dice.CaptureState());
            Assert.Throws<InvalidOperationException>(() => Hero(c).BackstageRollCheck());
        }

        // ---------- наслідки ----------

        [Test]
        public void Parking_Fight_InjuresTheHead_RaisesTheFeudWithTheBaron_AndHeatsTheNextShow()
        {
            var c = Shared();
            var s = HeroForEvent(c, "ev_parking");
            int heel = s.Feud.State.GetPath(RingPath.Heel);
            Assert.AreEqual("ev_parking", s.Backstage().SceneId);
            var r = Play(s, new[] { "fight" });
            Assert.AreEqual(2, s.State.ZoneDamage[(int)BodyZone.Head]);
            Assert.AreEqual(1, s.FeudLevel("baron"));
            Assert.AreEqual(1, r.FeudDelta);
            Assert.AreEqual(1, s.State.NextHypeBonus);
            Assert.AreEqual(1, r.HypeBonusDelta);
            Assert.AreEqual(heel + 1, s.Feud.State.GetPath(RingPath.Heel));
            Assert.IsTrue(s.State.BackstageSeen.Contains("1:ev_parking"));
        }

        [Test]
        public void Parking_Guard_KeepsTheBodySafe_AndGainsTrust()
        {
            var s = HeroForEvent(Shared(), "ev_parking");
            s.Backstage();
            var r = Play(s, new[] { "guard" });
            Assert.AreEqual(0, s.State.ZoneDamage[(int)BodyZone.Head]);
            Assert.AreEqual(1, r.TrustDelta);
            Assert.AreEqual(1, s.FeudLevel("baron"));
        }

        [Test]
        public void Parking_Bluff_Miss_CostsABodyDamage_Strong_PaysWithRewards()
        {
            var c = Shared();
            var miss = HeroForEvent(c, "ev_parking"); miss.Backstage();
            Play(miss, new[] { "bluff" }, RollTier.Miss);
            Assert.AreEqual(1, miss.State.ZoneDamage[(int)BodyZone.Body]);
            Assert.AreEqual(1, miss.FeudLevel("baron"));
            var strong = HeroForEvent(c, "ev_parking"); strong.Backstage();
            int aud = strong.Popularity;
            Play(strong, new[] { "bluff" }, RollTier.Strong);   // перші дві нагороди: популярність і довіра
            Assert.AreEqual(aud + 1, strong.Popularity);
            Assert.AreEqual(0, strong.State.ZoneDamage[(int)BodyZone.Body]);
        }

        [Test]
        public void Interview_Humble_GainsPopularity()
        {
            var s = HeroForEvent(Shared(), "ev_interview");
            Assert.AreEqual("ev_interview", s.Backstage().SceneId);
            int aud = s.Popularity;
            var r = Play(s, new[] { "humble" });
            Assert.AreEqual(1, r.PopularityDelta);
            Assert.AreEqual(aud + 1, s.Popularity);
        }

        [Test]
        public void Interview_Story_RollsTheCharismaCheck_AndAFailureCostsPopularity()
        {
            var s = HeroForEvent(Shared(), "ev_interview");
            s.Backstage();
            int aud = s.Popularity;
            var r = Play(s, new[] { "story" }, RollTier.Miss);
            Assert.AreEqual(-1, r.PopularityDelta);
            Assert.AreEqual(aud - 1, s.Popularity);
        }

        [Test]
        public void Contract_SignAtOnce_PaysNowAndCostsTrust_ReadingIsARiskThatCanPay_RefusingGainsTrust()
        {
            var c = Shared();
            var sign = HeroForEvent(c, "ev_contract"); Assert.AreEqual("ev_contract", sign.Backstage().SceneId);
            int money = sign.Money, trust = sign.Trust;
            var r = Play(sign, new[] { "sign" });
            Assert.AreEqual(60, r.MoneyDelta);
            Assert.AreEqual(money + 60, sign.Money);
            Assert.AreEqual(trust - 2, sign.Trust);
            Assert.AreEqual(-2, r.TrustDelta);
            Assert.IsTrue(sign.Feud.State.HasFlag("contract_signed"));

            var read = HeroForEvent(c, "ev_contract"); read.Backstage();
            int t0 = read.Trust;
            Play(read, new[] { "read" }, RollTier.Miss);
            Assert.AreEqual(t0 - 1, read.Trust, "торги провалились — агент розповів букеру");
            var readOk = HeroForEvent(c, "ev_contract"); readOk.Backstage();
            int m0 = readOk.Money;
            Play(readOk, new[] { "read" }, RollTier.Strong);
            Assert.AreEqual(m0 + 30, readOk.Money, "перша нагорода — чесний аванс");

            var refuse = HeroForEvent(c, "ev_contract"); refuse.Backstage();
            int t1 = refuse.Trust;
            Play(refuse, new[] { "refuse" });
            Assert.AreEqual(t1 + 1, refuse.Trust);
        }

        [Test]
        public void Contract_AskTheBooker_NeedsTrustThree()
        {
            var c = Shared();
            var low = HeroForEvent(c, "ev_contract"); low.Backstage();
            while (low.CurrentBackstage.Current.Type != PromoNodeType.Choice) low.CurrentBackstage.Advance();
            var ask = low.CurrentBackstage.AvailableOptions().First(v => v.Option.Id == "ask");
            Assert.IsTrue(ask.Locked, "довіра 2 < 3");
            var high = HeroForEvent(c, "ev_contract"); high.Feud.State.Trust = 3; high.Backstage();
            while (high.CurrentBackstage.Current.Type != PromoNodeType.Choice) high.CurrentBackstage.Advance();
            Assert.IsFalse(high.CurrentBackstage.AvailableOptions().First(v => v.Option.Id == "ask").Locked);
        }

        [Test]
        public void Friend_PayWithMoney_OrSpendADay_OrWalkPast()
        {
            var c = Shared();
            var pay = HeroForEvent(c, "ev_friend"); Assert.AreEqual("ev_friend", pay.Backstage().SceneId);
            int money = pay.Money, locker = pay.Feud.State.Locker;
            var r = Play(pay, new[] { "pay" });
            Assert.AreEqual(-15, r.MoneyDelta);
            Assert.AreEqual(money - 15, pay.Money);
            Assert.AreEqual(locker + 1, pay.Feud.State.Locker);
            Assert.AreEqual(1, r.LockerDelta);
            Assert.AreEqual(2, pay.State.ActionsLeft, "гроші, а не дія");

            var time = HeroForEvent(c, "ev_friend"); time.Backstage();
            var rt = Play(time, new[] { "time" });
            Assert.AreEqual(-1, rt.ActionsDelta);
            Assert.AreEqual(1, time.State.ActionsLeft, "дія на закулісся й ще одна — на друга");
            Assert.AreEqual(2, time.State.ActionsDone.Count);
            Assert.AreEqual(time.Money, money, "гроші не чіпали");

            var pass = HeroForEvent(c, "ev_friend"); pass.Backstage();
            var rp = Play(pass, new[] { "pass" });
            Assert.AreEqual(-1, rp.LockerDelta);
            Assert.AreEqual(money, pass.Money);
        }

        [Test]
        public void Friend_HelpIsLocked_WithoutMoneyAndWithoutAnExtraAction()
        {
            var c = Shared();
            var s = HeroForEvent(c, "ev_friend");
            s.State.Money = 10;
            s.State.ActionsLeft = 1;
            s.Backstage();
            var r = s.CurrentBackstage;
            while (r.Current.Type != PromoNodeType.Choice) r.Advance();
            var views = r.AvailableOptions();
            Assert.IsTrue(views.First(v => v.Option.Id == "pay").Locked);
            Assert.IsTrue(views.First(v => v.Option.Id == "time").Locked);
            Assert.IsFalse(views.First(v => v.Option.Id == "pass").Locked);
            Assert.Throws<InvalidOperationException>(() => r.Choose("pay"));
        }

        [Test]
        public void BookerTalk_Effects_RematchPlanAndTitleHint()
        {
            var c = Shared();
            // реванш: ворожнеча з останнім суперником
            var lost = Hero(c, 1, 1, 1, true);
            lost.Feud.State.Trust = 3; lost.State.LastOutcome = 2; lost.State.LastOpponent = "dzhura";
            lost.Backstage();
            var r = Play(lost, new[] { "rematch" });
            Assert.AreEqual(1, lost.FeudLevel("dzhura"));
            Assert.AreEqual(1, r.FeudDelta);
            Assert.IsNotNull(r.RumorKey, "після розмови з букером — підказка");

            // «звернути план»: план найближчого шоу змінюється
            var plan = Hero(c, 1, 2, 1, true);
            plan.Feud.State.Trust = 3; plan.State.Plan = BookerPlan.WinClean;
            plan.Backstage();
            var rp = Play(plan, new[] { "swerve" });
            Assert.AreEqual(BookerPlan.WinAny, plan.State.Plan);
            Assert.IsTrue(rp.PlanChanged);
            Assert.AreEqual(BookerPlan.None, plan.Feud.State.Plan, "план ворожнечі з Молотом не чіпали");

            // титульний натяк
            var title = Hero(c, 1, 1, 1, true);
            title.Feud.State.Trust = 4;
            title.Backstage();
            var rt = Play(title, new[] { "ready" });
            Assert.AreEqual(1, rt.TrustDelta);
            Assert.AreEqual(5, title.Trust);
            Assert.AreEqual(1, title.State.NextHypeBonus);
        }

        [Test]
        public void ThePlanIsNotChanged_OnTheBigFeudShow()
        {
            var c = Shared();
            var s = Hero(c, 1, 2, 1, true);
            s.Feud.State.Trust = 3; s.State.Plan = BookerPlan.WinClean;
            s.Backstage();
            while (s.CurrentBackstage.Current.Type != PromoNodeType.Choice) s.CurrentBackstage.Advance();
            s.State.IsTitle = true;   // шоу стало титульним: плану немає, змінювати нічого
            var r = Play(s, new[] { "swerve" });
            Assert.IsFalse(r.PlanChanged);
        }

        [Test]
        public void LastShowOutcome_IsRemembered_AcrossTheWeekChange()
        {
            var c = Shared();
            var s = Hero(c, 5, 1, 1, true);
            var rep = new CareerRunReport();
            s.EndWeek();
            int guard = 0;
            while (s.Stage != CareerStage.Week && guard++ < 50) CareerBot.Step(s, 1, rep);
            Assert.AreEqual(CareerStage.Week, s.Stage);
            Assert.IsNull(s.State.Last, "підсумок шоу з тижнем скидається");
            Assert.AreNotEqual(0, s.State.LastOutcome, "а результат останнього шоу пам'ятається");
        }

        // ---------- сейв ----------

        [Test]
        public void Save_AfterAScene_RestoresIntoAFreshInstance_WithTheSpentActionAndTheSeenEvent()
        {
            var c = Shared();
            var s = HeroForEvent(c, "ev_contract", 21);
            s.Backstage();
            Play(s, new[] { "sign" });
            Assert.AreEqual(2, s.State.ActionsLeft);
            var fresh = CareerKit.Fresh(21, c);
            fresh.RestoreState(s.CaptureState());
            Assert.AreEqual(s.CaptureState(), fresh.CaptureState());
            Assert.AreEqual(2, fresh.State.ActionsLeft);
            Assert.AreEqual(s.Money, fresh.Money);
            Assert.AreEqual(s.Trust, fresh.Trust);
            Assert.IsTrue(fresh.State.BackstageSeen.Contains("1:ev_contract"));
            Assert.IsFalse(fresh.BackstageOpen);
            // далі обидві сесії ведуть себе однаково: та сама наступна сцена
            Assert.AreEqual(s.Backstage().SceneId, fresh.Backstage().SceneId);
        }

        [Test]
        public void Save_InTheMiddleOfAScene_DropsTheScene_ActionNotSpent_AndSceneReopensFromTheStart()
        {
            var c = Shared();
            var s = HeroForEvent(c, "ev_parking", 33);
            string before = s.CaptureState();
            Assert.AreEqual("ev_parking", s.Backstage().SceneId);
            var run = s.CurrentBackstage;
            run.Advance(); run.Advance(); run.Advance();      // три репліки
            run.Choose("fight");                              // вибір зроблено, але сцену не закінчено
            Assert.AreEqual(before, s.CaptureState(), "незавершена сцена не потрапляє в зліпок");

            var fresh = CareerKit.Fresh(33, c);
            fresh.RestoreState(s.CaptureState());
            Assert.IsFalse(fresh.BackstageOpen);
            Assert.AreEqual(3, fresh.State.ActionsLeft, "дія не витрачена");
            Assert.AreEqual(0, fresh.State.ZoneDamage[(int)BodyZone.Head], "наслідки не застосовано");
            Assert.AreEqual(0, fresh.FeudLevel("baron"));
            var again = fresh.Backstage();
            Assert.AreEqual("ev_parking", again.SceneId);
            Assert.AreEqual("n1", fresh.CurrentBackstage.Current.Id);
        }

        [Test]
        public void OldRing3Blobs_WithoutBackstageKeys_StillRestore()
        {
            var c = Shared();
            var s = Hero(c, 4);
            string blob = s.CaptureState();
            var parts = blob.Split('|').Where(p => !p.StartsWith("bs=") && !p.StartsWith("lastout=") && !p.StartsWith("lastplan="));
            var fresh = CareerKit.Fresh(4, c);
            fresh.RestoreState(string.Join("|", parts));
            Assert.AreEqual(0, fresh.State.BackstageSeen.Count);
            Assert.AreEqual(0, fresh.State.LastOutcome);
            Assert.AreEqual(s.Money, fresh.Money);
        }

        [Test]
        public void FullBotPlaythrough_WithScenes_RoundTripsAtEveryWeek()
        {
            var c = Shared();
            var s = CareerKit.NewAtCreate(44, c);
            s.CreateHero(CareerKit.Draft(Gimmick.Flyer, CareerRole.Villain, RingAttr.Technique, "Зірка"));
            var rep = new CareerRunReport();
            int guard = 0;
            while (s.State.Season <= 2 && guard++ < 1500)
            {
                if (s.Stage == CareerStage.Week)
                {
                    var fresh = CareerKit.Fresh(44, c);
                    fresh.RestoreState(s.CaptureState());
                    Assert.AreEqual(s.CaptureState(), fresh.CaptureState(), "тиждень " + s.State.Season + "/" + s.State.Week);
                }
                CareerBot.Step(s, 3, rep);
            }
            Assert.Greater(rep.Scenes.Count, 0, "бот-варіант 3 заходить у закулісся");
        }
    }
}
