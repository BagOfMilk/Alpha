using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Core.Ring.Match;
using Game.Gameplay.Ring.Models;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Ґіміки 5–8 (TRPG_DESIGN §5.2, черга П5, пункт 4; умови ПМ 12.11.2026): «Ветеран — Техніка +2, Харизма +1 — Більше жетонів
    /// реверсу, читає суперника», «Хардкорник — Витривалість +2, Сила +1 — Підлога, предмети, терпіти біль», «Новачок — Рівно, +2
    /// вільні — Швидко росте, мало прийомів», «Силач — Сила +3 — Підняття, кидки, повільний». На екрані: «Бувалий» (ключ veteran — «Ветеран»
    /// уже перк, NPC і сцена) і «Самородок» (ключ rookie — «Новачок» уже типове рингове ім'я). На екран створення — після У0 власника;
    /// ядро й дані — уже. Кліпи прийомів (RingClipTable) — зона Бою.
    /// </summary>
    public class GimmickRulesTests
    {
        private static readonly Gimmick[] New = { Gimmick.Veteran, Gimmick.Hardcore, Gimmick.Rookie, Gimmick.Strongman };

        private static CareerSession Hero(ulong seed, Gimmick g, Action<CharacterDraft> tweak = null)
        {
            var s = CareerKit.NewAtCreate(seed);
            var d = CareerKit.Draft(g, CareerRole.Hero, CareerKit.FreeFor(s.Content, g, RingAttr.Strength));
            if (tweak != null) tweak(d);
            s.CreateHero(d);
            return s;
        }

        /// <summary>
        /// Та сама сесія у свіжій копії через сейв, але з іншим ґіміком у зліпку (атрибути, прийоми й перки ті самі) — щоб поміряти лише
        /// правило ґіміка. Вбудованого перка Ветерана така копія не має: його кладе створення.
        /// </summary>
        private static CareerSession AsGimmick(CareerSession s, ulong seed, Gimmick g)
        {
            string blob = s.CaptureState(), from = "|gimmick=" + (int)s.State.Gimmick + "|";
            Assert.AreEqual(1, (blob.Length - blob.Replace(from, "").Length) / from.Length, "у зліпку один ґімік");
            var t = CareerKit.Fresh(seed, s.Content);
            t.RestoreState(blob.Replace(from, "|gimmick=" + (int)g + "|"));
            Assert.AreEqual(g, t.State.Gimmick);
            return t;
        }

        private static string Gimmicks() { return CareerKit.Read("career_gimmicks.json"); }

        private static CareerContent Load(string gimmicksJson)
        {
            return CareerContent.Load(CareerKit.Read("feud_hammer.json"), CareerKit.Read("career_moves.json"), gimmicksJson, CareerKit.Read("career_roster.json"));
        }

        /// <summary>Довести бота до матчу ворожнечі з Молотом (етап підготовки).</summary>
        private static CareerSession AtFeudMatch(ulong seed, Gimmick g)
        {
            var s = CareerKit.NewHero(seed, g, CareerRole.Hero);
            var rep = new CareerRunReport();
            for (int guard = 0; !(s.Stage == CareerStage.Show && s.State.Phase == ShowPhase.Feud && s.Feud.Stage == RingStage.MatchPrep); guard++)
            {
                Assert.Less(guard, 3000, "бот не дійшов до матчу ворожнечі");
                CareerBot.Step(s, 0, rep);
            }
            return s;
        }

        // ---------------- дані ----------------

        [Test]
        public void TheFourNewGimmicks_FollowTheDesignTable_AndWaitForTheOwnersU0BeforeTheCreationScreen()
        {
            var c = CareerKit.Content();
            Assert.AreEqual(CareerKit.GimmickCount, c.Gimmicks.Count);
            CollectionAssert.AreEqual(Enum.GetValues(typeof(Gimmick)).Cast<Gimmick>().ToArray(), c.Gimmicks.Select(g => g.Id).ToArray(), "порядок — як у переліку");
            CollectionAssert.AreEqual(new[] { 4, 5, 6, 7 }, New.Select(g => (int)g).ToArray(), "номери 4–7 — у сейвах, не міняти");
            Func<Gimmick, int[]> a = g => c.GimmickOf(g).Attr;
            CollectionAssert.AreEqual(new[] { 0, 0, 2, 0, 1 }, a(Gimmick.Veteran), "Ветеран: Техніка +2, Харизма +1");
            CollectionAssert.AreEqual(new[] { 1, 0, 0, 2, 0 }, a(Gimmick.Hardcore), "Хардкорник: Витривалість +2, Сила +1");
            CollectionAssert.AreEqual(new[] { 0, 0, 0, 0, 0 }, a(Gimmick.Rookie), "Новачок: рівно");
            CollectionAssert.AreEqual(new[] { 3, -1, 0, 0, 0 }, a(Gimmick.Strongman), "Силач: Сила +3, повільний — спритність −1");
            Assert.AreEqual(RingAttr.Technique, c.GimmickOf(Gimmick.Veteran).FinisherAttr);
            Assert.AreEqual(2, c.GimmickOf(Gimmick.Rookie).FreePoints, "+2 вільні");
            Assert.AreEqual(0, c.GimmickOf(Gimmick.Rookie).StartGimmickMoves, "мало прийомів");
            foreach (var g in c.Gimmicks)
            {
                bool mvp = !New.Contains(g.Id);
                Assert.AreEqual(mvp, g.OnCreationScreen, g.Key + ": на екрані створення — лише MVP, решта після У0");
                if (g.Id != Gimmick.Rookie) { Assert.AreEqual(1, g.FreePoints, g.Key); Assert.AreEqual(-1, g.StartGimmickMoves, g.Key); }
                Assert.IsTrue(c.Roster.Any(n => n.Gimmick == g.Id), "у ростері є борець ґіміка " + g.Key);
            }
            foreach (var g in New)
                Assert.IsTrue(c.Roster.Where(n => n.Gimmick == g).All(n => n.Tier >= 2), g + ": NPC — у федераціях 2–4, М1 федерації 1 той самий");
            var v = CareerKit.NewAtCreate(1).GetCreationView();
            CollectionAssert.AreEqual(new[] { Gimmick.Monster, Gimmick.Flyer, Gimmick.Technician, Gimmick.Showman }, v.Gimmicks.Select(x => x.Def.Id).ToArray());

            // після У0 власник (через ПМ) перемикає прапор у даних — і ґімік з'являється з усім своїм
            var open = Load(Gimmicks().Replace("\"onCreationScreen\": false", "\"onCreationScreen\": true"));
            var s = CareerSession.New(open, new Game.Gameplay.Combat.SeededDiceRoller(1), 1);
            s.StartNewGame();
            var all = s.GetCreationView();
            Assert.AreEqual(CareerKit.GimmickCount, all.Gimmicks.Count);
            var rookie = all.Gimmicks.Single(x => x.Def.Id == Gimmick.Rookie);
            Assert.AreEqual(2, rookie.FreePoints);
            Assert.AreEqual(8, rookie.StartMoves.Count, "6 базових + коронний + фінішер");
            Assert.AreEqual(1, all.Gimmicks.Single(x => x.Def.Id == Gimmick.Veteran).FreePoints);
        }

        [Test]
        public void TheNewMoves_AreTheGimmicksOwn_OnExistingActions_HardcoreWithoutDirty()
        {
            var c = CareerKit.Content();
            var ok = new HashSet<MatchActionId> { MatchActionId.Strike, MatchActionId.Throw, MatchActionId.Submission, MatchActionId.TopRope,
                MatchActionId.Taunt, MatchActionId.Dirty, MatchActionId.Finisher };
            var keys = new List<string>();
            foreach (var gi in New)
            {
                var g = c.GimmickOf(gi);
                var ids = new List<string>(g.GimmickMoves) { g.SignatureMove, g.FinisherMove };
                foreach (var id in ids)
                {
                    var m = c.Moves.Get(id);
                    keys.Add(id);
                    Assert.AreEqual(g.Key, m.GimmickId, id);
                    Assert.IsTrue(ok.Contains(m.Action), id + ": лише наявні дії (без weapon — він легальний лише без DQ)");
                    // у звичайному матчі dirty — попередження й мінус ажіотажу, три — DQ: ґімік карав би сам себе (умова ПМ)
                    if (gi == Gimmick.Hardcore)
                        Assert.That(new[] { MatchActionId.Strike, MatchActionId.Throw, MatchActionId.TopRope, MatchActionId.Finisher }, Has.Member(m.Action), id);   // Has.Member: у Unity NUnit 3.5 (новіші обмеження не компілюються)
                }
                Assert.AreEqual(MoveKind.Gimmick, c.Moves.Get(g.GimmickMoves[0]).Kind);
                Assert.Greater(c.Moves.Get(g.GimmickMoves[0]).Cost, 0, "ґімік-прийом вивчається за очки");
            }
            Assert.AreEqual(16, keys.Distinct().Count());
            Assert.IsNull(c.Moves.Find("stomp"), "удари по лежачому рушій не вміє — у М1 їх не обіцяємо");
            TestContext.WriteLine("ключі прийомів ґіміків 5–8: " + string.Join(", ", keys));
        }

        [Test]
        public void TheNewNames_DoNotClash_WithThePerkTheNpcTheSceneOrTheDefaultRingName()
        {
            var ui = MiniJson.ParseObject(CareerKit.Read("ui_uk.json"));
            var strings = MiniJson.ParseObject(CareerKit.Read("strings_uk.json"));
            Func<string, string> t = k => { object o; Assert.IsTrue(ui.TryGetValue(k, out o) || strings.TryGetValue(k, out o), "немає ключа " + k); return (string)o; };
            var names = new HashSet<string>();
            foreach (var g in CareerKit.Content().Gimmicks)
            {
                Assert.IsTrue(names.Add(t("gimmick." + g.Key)), "назва ґіміка повторюється: " + g.Key);
                Assert.IsNotEmpty(t("gimmick." + g.Key + ".desc"));
                Assert.AreEqual(g.Id.ToString().ToLowerInvariant(), g.Key, "id — назва enum у нижньому регістрі");
            }
            Assert.AreEqual("Бувалий", t("gimmick.veteran"));
            Assert.AreEqual("Самородок", t("gimmick.rookie"));
            Assert.IsFalse(names.Contains(t("perk.veteran")), "«Ветеран» — уже перк");
            Assert.IsFalse(names.Contains(t("c.create.default_name")), "типове рингове ім'я — не назва ґіміка");
            Assert.IsFalse(names.Contains(t("w.player.name")));
        }

        /// <summary>
        /// Назва ґіміка за статтю борця (рев'ю ПМ 13.11.2026: героїня бачила себе «Силачем»). Назву показують і героєві, і суперникові
        /// на одному екрані, тож це «.fem», як у шляхів (path.&lt;шлях&gt;.fem, рід — за статтю борця), а не «.f»: «.f» HeroForms
        /// підставляє глобально, і суперник героїні теж став би «Силачкою». Монстр і Самородок — спільного роду.
        /// </summary>
        [Test]
        public void EveryGimmickName_HasAFemForm_NotAGlobalHeroForm_AndTheFemNamesDoNotClash()
        {
            var ui = MiniJson.ParseObject(CareerKit.Read("ui_uk.json"));
            var strings = MiniJson.ParseObject(CareerKit.Read("strings_uk.json"));
            Func<string, string> t = k => { object o; Assert.IsTrue(ui.TryGetValue(k, out o) || strings.TryGetValue(k, out o), "немає ключа " + k); return (string)o; };
            var fem = new HashSet<string>();
            foreach (var g in CareerKit.Content().Gimmicks)
            {
                string key = "gimmick." + g.Key;
                Assert.IsTrue(fem.Add(t(key + ".fem")), "жіноча назва повторюється: " + g.Key);
                Assert.IsFalse(ui.ContainsKey(key + HeroForms.FemaleSuffix) || strings.ContainsKey(key + HeroForms.FemaleSuffix),
                    key + ": «.f» підмінив би назву й суперникові — лише «.fem»");
                Assert.IsFalse(ui.ContainsKey(key + ".desc" + HeroForms.FemaleSuffix), key + ".desc — безособовий, без «.f»");
            }
            CollectionAssert.AreEquivalent(new[] { "Монстр", "Летунка", "Технікиня", "Шоувумен", "Бувала", "Хардкорниця", "Самородок", "Силачка" }, fem);
            foreach (var k in new[] { "perk.veteran", "perk.crowd_favorite.f", "perk.hero_of_crowd.f", "c.create.default_name", "w.player.name" })
                Assert.IsFalse(fem.Contains(t(k)), k);
        }

        [Test]
        public void Loader_RejectsBrokenNewFields()
        {
            Assert.DoesNotThrow(() => Load(Gimmicks()));
            Assert.Throws<FormatException>(() => Load(Gimmicks().Replace("\"freePoints\": 2", "\"freePoints\": 3")), "вільних — 1–2");
            Assert.Throws<FormatException>(() => Load(Gimmicks().Replace("\"freePoints\": 2", "\"freePoints\": 0")));
            Assert.Throws<FormatException>(() => Load(Gimmicks().Replace("\"startMoves\": 0", "\"startMoves\": 3")), "стартових більше, ніж ґімік-прийомів");
            Assert.Throws<FormatException>(() => Load(Gimmicks().Replace("\"id\": \"strongman\"", "\"id\": \"rookie\"")), "ґімік двічі");
            string json = Gimmicks();
            int at = json.IndexOf("\"id\": \"strongman\"", StringComparison.Ordinal);
            int str = json.IndexOf("\"strength\": 3", at, StringComparison.Ordinal);
            Assert.Greater(str, at);
            Assert.Throws<FormatException>(() => Load(json.Substring(0, str) + "\"strength\": 4" + json.Substring(str + "\"strength\": 3".Length)), "основа понад межу");
        }

        // ---------------- створення ----------------

        [Test]
        public void Rookie_TwoFreePoints_IntoOneOrTwoAttributes_StartsWithoutGimmickMoves_LearnsThemForPoints()
        {
            var two = Hero(1, Gimmick.Rookie, d => d.FreePoint2 = RingAttr.Agility);
            CollectionAssert.AreEqual(new[] { 1, 1, 0, 0, 0 }, Enumerable.Range(0, 5).Select(i => two.Player.Get((RingAttr)i)).ToArray());
            var one = Hero(1, Gimmick.Rookie);
            Assert.AreEqual(2, one.Player.Get(RingAttr.Strength), "без другого атрибута — обидва бали в перший");
            Assert.AreEqual(2, Enumerable.Range(0, 5).Sum(i => one.Player.Get((RingAttr)i)));
            Assert.AreEqual(0, one.PlayerGrowth(), "вільні бали ґіміка — не ріст (суперники не сильнішають)");
            // куплені атрибути понад основу й два вільні бали — ріст за тією самою формулою, що в інших (лише вільних два, а не один)
            var grow = Hero(1, Gimmick.Rookie, d => d.FreePoint2 = RingAttr.Agility);
            for (int step = 0; step < 15; step++)
            {
                var attr = (RingAttr)(step % 5);
                grow.Feud.State.Player.Set(attr, Math.Max(grow.Player.Get(attr), step / 5 + 1));
                int bought = Enumerable.Range(0, 5).Sum(i => grow.Player.Get((RingAttr)i));   // основа Новачка — нулі
                int units = Math.Max(0, bought - 2);
                Assert.AreEqual(Math.Max(0, (CareerRules.RivalGrowthNum * units - CareerRules.RivalGrowthOffset) / CareerRules.RivalGrowthDen),
                    grow.PlayerGrowth(), "куплено " + bought);
            }

            var v = Hero(1, Gimmick.Rookie).GetCareerView();
            Assert.AreEqual(8, v.Moves.Count, "6 базових + коронний + фінішер");
            Assert.AreEqual(0, v.Moves.Count(m => m.Kind == MoveKind.Gimmick));
            Assert.AreEqual(1, v.Moves.Count(m => m.Kind == MoveKind.Signature));
            var learn = Hero(1, Gimmick.Rookie);
            string reason;
            Assert.IsFalse(learn.CanLearnMove("chop", out reason));
            Assert.AreEqual("points", reason);
            learn.Feud.State.DevPoints = 2;
            Assert.IsTrue(learn.LearnMove("chop"));
            Assert.IsTrue(learn.KnowsMove("chop"));

            // другий бал у ґіміку з одним — ігнорується
            var mono = Hero(1, Gimmick.Technician, d => d.FreePoint2 = RingAttr.Agility);
            Assert.AreEqual(0, mono.Player.Get(RingAttr.Agility));
            Assert.AreEqual(4, Enumerable.Range(0, 5).Sum(i => mono.Player.Get((RingAttr)i)));
        }

        [Test]
        public void Strongman_StrengthIsAlreadyAtTheTop_TheFreePointCannotGoThere_AndNorCanARookiesTwo()
        {
            var s = CareerKit.NewAtCreate(1);
            string reason;
            Assert.IsFalse(s.CanCreate(CareerKit.Draft(Gimmick.Strongman, CareerRole.Hero, RingAttr.Strength), out reason));
            Assert.AreEqual("attr_max", reason);
            Assert.IsTrue(s.CanCreate(CareerKit.Draft(Gimmick.Strongman, CareerRole.Hero, RingAttr.Agility), out reason), "спритність −1 → 0");
            var bad = CareerKit.Draft(Gimmick.Rookie, CareerRole.Hero, RingAttr.Strength);
            bad.Gimmick = (Gimmick)99;
            Assert.IsFalse(s.CanCreate(bad, out reason), "невідомий ґімік");
            Assert.AreEqual("gimmick", reason);

            var d = CareerKit.Draft(Gimmick.Rookie, CareerRole.Hero, RingAttr.Strength);
            Assert.IsTrue(s.CanCreate(d, out reason));
            d.Gimmick = Gimmick.Technician; d.FreePoint = RingAttr.Technique;
            Assert.IsFalse(s.CanCreate(d, out reason), "технік: 3 + 1");
            // основа Новачка: сила 2 (замір межі для двох балів)
            string j = Gimmicks();
            int ra = j.IndexOf("\"id\": \"rookie\"", StringComparison.Ordinal);
            int rs = j.IndexOf("\"strength\": 0", ra, StringComparison.Ordinal);
            Assert.Greater(rs, ra);
            var r = Load(j.Substring(0, rs) + "\"strength\": 2" + j.Substring(rs + "\"strength\": 0".Length));
            Assert.AreEqual(2, r.GimmickOf(Gimmick.Rookie).Attr[0], "замір: основа Новачка змінена");
            var t = CareerSession.New(r, new Game.Gameplay.Combat.SeededDiceRoller(1), 1);
            t.StartNewGame();
            Assert.IsFalse(t.CanCreate(CareerKit.Draft(Gimmick.Rookie, CareerRole.Hero, RingAttr.Strength), out reason), "2 + два бали — понад межу");
            var split = CareerKit.Draft(Gimmick.Rookie, CareerRole.Hero, RingAttr.Strength); split.FreePoint2 = RingAttr.Agility;
            Assert.IsTrue(t.CanCreate(split, out reason), "2 + 1 і другий бал в інший атрибут — можна");
        }

        // ---------------- Ветеран: вбудований перк ----------------

        [Test]
        public void Veteran_HasTheVeteranPerkFromTheStart_CannotBuyItAgain_IsNotAdvisedIt_AndItIsNotGrowth()
        {
            var vet = Hero(2, Gimmick.Veteran);
            var tech = Hero(2, Gimmick.Technician);
            Assert.IsTrue(vet.Feud.State.Perks.Contains(PerkCatalog.Veteran), "перк «Ветеран» — від старту");
            Assert.AreEqual(0, vet.DevPoints, "без очок");
            Assert.IsFalse(tech.Feud.State.Perks.Contains(PerkCatalog.Veteran));
            Assert.AreEqual(0, vet.PlayerGrowth(), "вбудований перк — не ріст: суперники не сильнішають");
            CollectionAssert.AreEquivalent(new[] { PerkCatalog.Veteran }, vet.Feud.State.Clone().Perks, "перк — у стані ворожнечі (сейв)");

            foreach (var x in new[] { vet, tech }) x.Feud.State.DevPoints = 40;
            string reason;
            Assert.IsFalse(vet.CanBuyPerk(PerkCatalog.Veteran, out reason), "купити вдруге не можна");
            Assert.AreEqual("owned", reason);
            Assert.IsFalse(vet.BuyPerk(PerkCatalog.Veteran));
            Assert.IsTrue(tech.CanBuyPerk(PerkCatalog.Veteran, out reason), "замір: іншим ґімікам перк продається");

            // програне утримання: порада радить «Ветерана» тому, у кого його нема (перший перк уже куплено, щоб «Друге дихання» не
            // стояло попереду), і ніколи — Ветеранові
            foreach (var x in new[] { vet, tech })
            {
                x.Feud.State.Perks.Add(PerkCatalog.SecondWind);
                x.State.Last = new ShowResult { Played = true, Winner = MatchWinner.Rival, EndKind = MatchEndKind.Pin, Hype = 3 };
            }
            Assert.IsTrue(DevelopmentAdvice.For(tech, 3).Any(a => a.Kind == AdviceKind.Perk && a.Id == PerkCatalog.Veteran), "замір: порада бачить перк");
            for (int pts = 2; pts <= 40; pts += 2)
            {
                vet.Feud.State.DevPoints = pts;
                foreach (var end in new[] { MatchEndKind.Pin, MatchEndKind.Submission })
                {
                    vet.State.Last.EndKind = end;
                    Assert.IsFalse(DevelopmentAdvice.For(vet, 3).Any(a => a.Kind == AdviceKind.Perk && a.Id == PerkCatalog.Veteran), pts + " " + end);
                }
            }

            // купив перк — ріст, як і раніше; у Ветерана того самого перка немає в рості
            tech.Feud.State.Perks.Remove(PerkCatalog.SecondWind); vet.Feud.State.Perks.Remove(PerkCatalog.SecondWind);
            Assert.IsTrue(tech.BuyPerk(PerkCatalog.Veteran));
            int techGrowth = tech.PlayerGrowth();
            tech.Feud.State.Perks.Remove(PerkCatalog.Veteran);
            Assert.GreaterOrEqual(techGrowth, tech.PlayerGrowth(), "куплений перк — ріст (чи не менше)");

            // ріст Ветерана на кожному кроці купівель — та сама формула, що в інших, а перків у ній нуль: вбудований не рахується
            var g = vet.Content.GimmickOf(Gimmick.Veteran);
            var buy = Hero(2, Gimmick.Veteran);
            for (int step = 0; step < 15; step++)
            {
                var attr = (RingAttr)(step % 5);
                buy.Feud.State.Player.Set(attr, Math.Min(WrestlerStats.MaxAttr, buy.Player.Get(attr) + 1));
                int bought = Enumerable.Range(0, 5).Sum(i => Math.Max(0, buy.Player.Get((RingAttr)i) - g.Attr[i]));
                int units = Math.Max(0, bought - g.FreePoints);
                Assert.AreEqual(Math.Max(0, (CareerRules.RivalGrowthNum * units - CareerRules.RivalGrowthOffset) / CareerRules.RivalGrowthDen),
                    buy.PlayerGrowth(), "куплено " + bought);
            }
        }

        [Test]
        public void Veteran_TokensDoNotStack_GimmickPerkAndLegend_GiveNoMoreThanAnyoneCanBuy()
        {
            var vet = Hero(3, Gimmick.Veteran);
            var tech = Hero(3, Gimmick.Technician);
            tech.Feud.State.Perks.Add(PerkCatalog.Veteran);
            foreach (var x in new[] { vet, tech }) { x.State.LateRole = LateRole.Legend; x.EndWeek(); }
            var ev = vet.StartShow();
            var et = tech.StartShow();
            Assert.AreEqual(MatchRules.BaseReversalTokens + 1 + LateRoles.LegendReversalTokens, ev.State.Player.ReversalTokens, "ґімік (= перк) + Легенда");
            Assert.AreEqual(et.State.Player.ReversalTokens, ev.State.Player.ReversalTokens, "стільки ж, скільки в будь-кого з купленим перком і Легендою");
            Assert.AreEqual(et.Setup.PlayerMods.ExtraReversalTokens, ev.Setup.PlayerMods.ExtraReversalTokens);
        }

        // ---------------- матч: тиждень ----------------

        [Test]
        public void WeeklyShow_Veteran_ReadsTheRival_Hardcore_DamageShift_Strongman_StartMomentum_OthersNothing()
        {
            var s = Hero(4, Gimmick.Technician);
            s.EndWeek();
            var plain = AsGimmick(s, 4, Gimmick.Technician).StartShow();
            Assert.IsNotNull(plain, "тиждень 1 — звичайне шоу");
            Assert.IsFalse(plain.Setup.PlayerMods.ReadsRival);
            var vet = AsGimmick(s, 4, Gimmick.Veteran).StartShow();
            var hard = AsGimmick(s, 4, Gimmick.Hardcore).StartShow();
            var strong = AsGimmick(s, 4, Gimmick.Strongman).StartShow();

            Assert.IsTrue(vet.Setup.PlayerMods.ReadsRival, "Ветеран читає суперника (показ наміру — Бій)");
            Assert.AreEqual(plain.State.Player.ReversalTokens, vet.State.Player.ReversalTokens, "жетон Ветерана — лише через вбудований перк, не понад нього");
            Assert.AreEqual(plain.Setup.PlayerMods.DamageThresholdShift + GimmickRules.HardcoreDamageShift, hard.Setup.PlayerMods.DamageThresholdShift, "терпіти біль");
            Assert.AreEqual(plain.Setup.PlayerStartMomentum + GimmickRules.StrongmanStartMomentum, strong.Setup.PlayerStartMomentum, "важкий з першого кидка");
            Assert.AreEqual(plain.State.Player.Momentum + GimmickRules.StrongmanStartMomentum, strong.State.Player.Momentum);
            foreach (var e in new[] { vet, hard, strong })
            {
                Assert.AreEqual(plain.Setup.RivalStartMomentum, e.Setup.RivalStartMomentum, "суперник той самий");
                Assert.AreEqual(plain.State.Hype, e.State.Hype);
            }
            foreach (var g in new[] { Gimmick.Rookie, Gimmick.Monster, Gimmick.Flyer, Gimmick.Showman })
            {
                var e = AsGimmick(s, 4, g).StartShow();
                Assert.AreEqual(plain.State.Player.ReversalTokens, e.State.Player.ReversalTokens, g.ToString());
                Assert.AreEqual(plain.Setup.PlayerMods.DamageThresholdShift, e.Setup.PlayerMods.DamageThresholdShift, g.ToString());
                Assert.AreEqual(plain.Setup.PlayerStartMomentum, e.Setup.PlayerStartMomentum, g.ToString());
                Assert.IsFalse(e.Setup.PlayerMods.ReadsRival, g.ToString());
            }
            var real = Hero(4, Gimmick.Veteran);
            real.EndWeek();
            Assert.AreEqual(MatchRules.BaseReversalTokens + 1, real.StartShow().State.Player.ReversalTokens, "справжній Ветеран — з перком від створення");
        }

        [Test]
        public void TheRivalsGimmick_ChangesTheRivalsMatchToo()
        {
            var a = Hero(5, Gimmick.Technician);
            a.EndWeek();
            string opp = a.State.Opponent;
            string blob = a.CaptureState();
            var ea = a.StartShow();
            Assert.IsFalse(ea.Setup.RivalMods.ReadsRival);
            Func<Gimmick, MatchEngine> withRival = g =>
            {
                var c = CareerKit.Content();
                c.FindNpc(opp).Gimmick = g;
                var t = CareerKit.Fresh(5, c);
                t.RestoreState(blob);
                return t.StartShow();
            };
            var ev = withRival(Gimmick.Veteran);
            Assert.AreEqual(ea.State.Rival.ReversalTokens + 1, ev.State.Rival.ReversalTokens, "суперник-ветеран — з перком «Ветеран»");
            Assert.IsTrue(ev.Setup.RivalMods.ReadsRival, "і читає гравця (моди NPC)");
            Assert.AreEqual(ea.State.Player.ReversalTokens, ev.State.Player.ReversalTokens);
            var eh = withRival(Gimmick.Hardcore);
            Assert.AreEqual(ea.Setup.RivalMods.DamageThresholdShift + GimmickRules.HardcoreDamageShift, eh.Setup.RivalMods.DamageThresholdShift);
            var es = withRival(Gimmick.Strongman);
            Assert.AreEqual(Math.Min(10, ea.Setup.RivalStartMomentum + GimmickRules.StrongmanStartMomentum), es.Setup.RivalStartMomentum);
            var er = withRival(Gimmick.Rookie);
            Assert.AreEqual(ea.State.Rival.ReversalTokens, er.State.Rival.ReversalTokens, "Новачок — суть у кар'єрі, не в матчі");
            Assert.AreEqual(ea.Setup.RivalMods.DamageThresholdShift, er.Setup.RivalMods.DamageThresholdShift);
            Assert.AreEqual(ea.Setup.RivalStartMomentum, er.Setup.RivalStartMomentum);
        }

        [Test]
        public void ForRival_IsTheBuiltInPerkPlusTheRest_ApplyToLeavesThePerkToThePerks()
        {
            var v = GimmickRules.ForRival(Gimmick.Veteran);
            Assert.AreEqual(Development.ToModifiers(new[] { PerkCatalog.Veteran }).ExtraReversalTokens, v.ExtraReversalTokens);
            Assert.IsTrue(v.ReadsRival);
            var m = new MatchModifiers();
            GimmickRules.ApplyTo(m, Gimmick.Veteran);
            Assert.AreEqual(0, m.ExtraReversalTokens, "гравцеві жетон дає перк серед перків, не ґімік удруге");
            Assert.IsTrue(m.ReadsRival);
            foreach (Gimmick g in Enum.GetValues(typeof(Gimmick)))
            {
                bool hasPerk = GimmickRules.BuiltInPerks(g).Count > 0;
                Assert.AreEqual(g == Gimmick.Veteran, hasPerk, g.ToString());
                Assert.AreEqual(hasPerk, GimmickRules.IsBuiltInPerk(g, PerkCatalog.Veteran), g.ToString());
                Assert.IsFalse(GimmickRules.IsBuiltInPerk(g, PerkCatalog.SecondWind), g.ToString());
            }
        }

        // ---------------- матч ворожнечі ----------------

        [Test]
        public void TheFeudMatch_TakesThePlayersGimmick_ThroughTheCampaign_LikeTheLateRole()
        {
            var s = AtFeudMatch(77, Gimmick.Technician);
            var plain = AsGimmick(s, 77, Gimmick.Technician).StartFeudMatch();
            var vet = AsGimmick(s, 77, Gimmick.Veteran).StartFeudMatch();
            var hard = AsGimmick(s, 77, Gimmick.Hardcore).StartFeudMatch();
            var strong = AsGimmick(s, 77, Gimmick.Strongman).StartFeudMatch();
            Assert.IsFalse(plain.Setup.PlayerMods.ReadsRival);
            Assert.IsTrue(vet.Setup.PlayerMods.ReadsRival, "Ветеран читає Молота");
            Assert.AreEqual(plain.State.Player.ReversalTokens, vet.State.Player.ReversalTokens, "жетон — від перка, не від ґіміка");
            Assert.AreEqual(plain.Setup.PlayerMods.DamageThresholdShift + GimmickRules.HardcoreDamageShift, hard.Setup.PlayerMods.DamageThresholdShift);
            Assert.AreEqual(Math.Min(10, plain.Setup.PlayerStartMomentum + GimmickRules.StrongmanStartMomentum), strong.Setup.PlayerStartMomentum);
            Assert.AreEqual(plain.State.Player.Momentum + GimmickRules.StrongmanStartMomentum, strong.State.Player.Momentum, "у рушії — те саме, що в MatchSetup");
            Assert.AreEqual(plain.State.Player.ReversalTokens, hard.State.Player.ReversalTokens);

            var real = AtFeudMatch(78, Gimmick.Veteran);
            Assert.AreEqual(Gimmick.Veteran, real.Feud.PlayerGimmick);
            Assert.IsTrue(real.Feud.State.Perks.Contains(PerkCatalog.Veteran));
            var e = real.StartFeudMatch();
            Assert.AreEqual(Development.ToModifiers(real.Feud.State.Perks).ExtraReversalTokens, e.Setup.PlayerMods.ExtraReversalTokens,
                "справжній Ветеран: жетон перка — серед поправок матчу ворожнечі, ґімік його не подвоює");
            Assert.GreaterOrEqual(e.Setup.PlayerMods.ExtraReversalTokens, 1);
            Assert.IsTrue(e.Setup.PlayerMods.ReadsRival);
        }

        [Test]
        public void TheHammersGimmick_IsAppliedLikeAnyRivals_AndAStandaloneFeudHasNoGimmick()
        {
            var s = AtFeudMatch(79, Gimmick.Technician);
            string blob = s.CaptureState();
            var plain = s.StartFeudMatch();
            var c = CareerKit.Content();
            c.FindNpc(CareerContent.HammerId).Gimmick = Gimmick.Hardcore;
            var t = CareerKit.Fresh(79, c);
            t.RestoreState(blob);
            var hard = t.StartFeudMatch();
            Assert.AreEqual(plain.Setup.RivalMods.DamageThresholdShift + GimmickRules.HardcoreDamageShift, hard.Setup.RivalMods.DamageThresholdShift);
            var c2 = CareerKit.Content();
            c2.FindNpc(CareerContent.HammerId).Gimmick = Gimmick.Veteran;
            var u = CareerKit.Fresh(79, c2);
            u.RestoreState(blob);
            var vet = u.StartFeudMatch();
            Assert.AreEqual(plain.State.Rival.ReversalTokens + 1, vet.State.Rival.ReversalTokens);
            Assert.IsTrue(vet.Setup.RivalMods.ReadsRival);

            // окрема ворожнеча (без кар'єри) ґіміка не знає — як і раніше
            var camp = RingCampaign.New(c.Feud, new Game.Gameplay.Combat.SeededDiceRoller(3));
            Assert.IsNull(camp.PlayerGimmick);
            var st = camp.State;
            var none = st.BuildMatchSetup(c.Feud);
            var withHard = st.BuildMatchSetup(c.Feud, LateRole.None, Gimmick.Hardcore);
            Assert.AreEqual(none.PlayerMods.DamageThresholdShift + GimmickRules.HardcoreDamageShift, withHard.PlayerMods.DamageThresholdShift);
            Assert.AreEqual(none.PlayerStartMomentum, st.BuildMatchSetup(c.Feud, LateRole.None, Gimmick.Monster).PlayerStartMomentum);
        }

        [Test]
        public void TheGimmick_GoesIntoTheFeudMatch_AndIntoTheRematch_ButNotIntoTheFeudSave()
        {
            var c = RingCampaignDriver.NewCampaign(4);
            RingCampaignDriver.ToFinalMatchPrep(c);
            var plain = c.State.BuildMatchSetup(c.Script);
            string saved = c.State.CaptureState();
            c.PlayerGimmick = Gimmick.Hardcore;
            Assert.AreEqual(saved, c.State.CaptureState(), "ґімік — стан кар'єри, не ворожнечі (ставить кар'єра, як пізню роль)");
            var m = c.StartMatch();
            Assert.AreEqual(plain.PlayerMods.DamageThresholdShift + GimmickRules.HardcoreDamageShift, m.Setup.PlayerMods.DamageThresholdShift);
            MatchAutoplay.Run(m, true);
            c.FinishMatch();
            c.OpenDevelopment();
            var again = c.StartRematch();
            Assert.AreEqual(plain.PlayerMods.DamageThresholdShift + GimmickRules.HardcoreDamageShift, again.Setup.PlayerMods.DamageThresholdShift,
                "реванш — з тим самим ґіміком");
        }

        // ---------------- кар'єра ----------------

        [Test]
        public void Hardcore_InjuryComesOneZoneDamageLater()
        {
            var plain = Hero(6, Gimmick.Technician);
            var hard = Hero(6, Gimmick.Hardcore);
            Assert.AreEqual(CareerRules.InjuryAtZoneDamage, plain.InjuryAtZoneDamage);
            Assert.AreEqual(CareerRules.InjuryAtZoneDamage + GimmickRules.HardcoreInjuryExtra, hard.InjuryAtZoneDamage);
            Assert.IsTrue(plain.GetWeekView().Card.Injuries.All(i => i.Threshold == plain.InjuryAtZoneDamage), "поріг — у виді для екрана");
            Assert.IsTrue(hard.GetWeekView().Card.Injuries.All(i => i.Threshold == hard.InjuryAtZoneDamage));

            Assert.IsTrue(plain.AddZoneDamage(0, CareerRules.InjuryAtZoneDamage), "звичайний — травма на трьох");
            Assert.Greater(plain.State.Injury[0], 0);
            Assert.IsFalse(hard.AddZoneDamage(0, CareerRules.InjuryAtZoneDamage), "хардкорник — ще терпить");
            Assert.AreEqual(0, hard.State.Injury[0]);
            Assert.IsTrue(hard.AddZoneDamage(0, 1));
            Assert.Greater(hard.State.Injury[0], 0);
        }

        [Test]
        public void Rookie_GrowsFaster_OneMoreDevPointForEveryMatch()
        {
            var s = Hero(7, Gimmick.Rookie);
            s.EndWeek();
            var rookie = AsGimmick(s, 7, Gimmick.Rookie);
            var other = AsGimmick(s, 7, Gimmick.Monster);
            foreach (var x in new[] { rookie, other })
            {
                x.StartShow();
                CareerBot.PlayMatch(x, 0, new CareerRunReport());
            }
            var a = rookie.FinishMatch();
            var b = other.FinishMatch();
            Assert.AreEqual(b.Winner, a.Winner, "замір: той самий матч");
            Assert.AreEqual(b.DevPointsGained + GimmickRules.RookieDevPerMatch, a.DevPointsGained);
            Assert.AreEqual(other.DevPoints + GimmickRules.RookieDevPerMatch, rookie.DevPoints);
        }

        [Test]
        public void Save_KeepsGimmicks4To7_AndRejectsAnUnknownOne()
        {
            foreach (var g in New)
            {
                var s = Hero((ulong)(20 + (int)g), g);
                var t = CareerKit.Fresh(1, s.Content);
                t.RestoreState(s.CaptureState());
                Assert.AreEqual(g, t.State.Gimmick);
                Assert.AreEqual(g, t.Feud.PlayerGimmick, "і в кампанії ворожнечі — після відновлення");
                Assert.AreEqual(s.CaptureState(), t.CaptureState());
            }
            var v = Hero(30, Gimmick.Veteran);
            string blob = v.CaptureState();
            var bad = CareerKit.Fresh(1, v.Content);
            var ex = Assert.Throws<FormatException>(() => bad.RestoreState(blob.Replace("|gimmick=4|", "|gimmick=8|")));
            StringAssert.Contains("ґімік", ex.Message);
        }

        [Test]
        public void EveryNewGimmick_PlaysASeason()
        {
            foreach (var g in New)
                for (int variant = 0; variant < 2; variant++)
                {
                    var s = Hero((ulong)(90 + (int)g * 2 + variant), g);
                    var rep = CareerBot.Play(s, 2, variant); // два сезони по 4 тижні — без зависань, гроші не в мінусі
                    Assert.AreEqual(3, s.State.Season, g + " / " + variant);
                    Assert.AreEqual(1, s.State.Week, g + " / " + variant);
                    Assert.GreaterOrEqual(rep.Shows, 4, g + " / " + variant);
                    Assert.GreaterOrEqual(rep.MinMoney, 0, g + " / " + variant);
                    Assert.AreEqual(g, s.State.Gimmick);
                }
        }
    }
}
