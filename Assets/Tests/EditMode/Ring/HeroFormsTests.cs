using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Core.Ring;
using Game.Gameplay.Ring;
using Game.Gameplay.Ring.Models;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Жіночі форми звертання (П5, ПМ 12.11.2026): «охоронець: кожен ключ ворожнечі й закулісся, де є чоловічий рід, має `.f`,
    /// або тест-перелік винятків з причиною». Межі — тексти ворожнечі (strings_uk.json), закулісся (backstage_uk.json) і ключі
    /// кінцівок та кар'єри в ui_uk.json. Вибір форми — <see cref="HeroForms"/>: RingText відступу на базовий ключ не знає.
    /// <para>
    /// Сітка мінімальна: ловить «ти + минулий час», «ти + прикметник на -ий», минулий час на початку речення й перелік
    /// слів-назв героя. Прикметник без «ти» поруч («готовий до бою?»), «Ти, мабуть, програв», назви шляхів у непрямих відмінках
    /// («Вінець Героя») вона не бачить — нові тексти перевіряти й очима.
    /// </para>
    /// </summary>
    public class HeroFormsTests
    {
        private const string L = "[А-Яа-яІіЇїЄєҐґ'’]";

        /// <summary>«Ти виграв», «ти вже домовився», «Ти сам просив» — минулий час чоловічого роду після «ти».</summary>
        private static readonly Regex YouPastMasc = new Regex(
            "(?<!" + L + ")[Тт]и\\s+(?:(?:вже|не|ж|теж|так|сам|його|її|їх|це|мене|тоді|знову|навіть)\\s+)*" + L + "+(?:в|вся)(?!" + L + ")");

        /// <summary>«Ти ще не виснажений», «ти готовий» — прикметник чоловічого роду після «ти».</summary>
        private static readonly Regex YouAdjMasc = new Regex(
            "(?<!" + L + ")[Тт]и\\s+(?:(?:вже|не|ще|ж|теж|так|зовсім|справді|дуже|точно|був)\\s+)*" + L + "+ий(?!" + L + ")");

        /// <summary>«Думав, дійдеш…», «Виграв раз…», «Програв — але…» — минулий час на початку речення (звертання без «ти»).</summary>
        private static readonly Regex OpeningPastMasc = new Regex(
            "(?:^|[.!?…:«—]\\s*)" + L + "{2,}(?:ав|ив|ів|ув|яв|ов|ився|ався)(?=[\\s,.!?—])");

        /// <summary>Слова, що називають героя в чоловічому роді.</summary>
        private static readonly Regex HeroWords = new Regex(
            "(?<!" + L + ")(?:хлопче|хлопець|новачку|новачок|сміливий|сміливця|сміливець|сам|такий|цілий|цілим|травмований|зламаним|"
            + "чемпіон|вигнанець|улюбленець|лиходій|лиходієві|лиходія|героєві|бунтар|бунтареві|бунтаря|герой|забутий)(?!" + L + ")",
            RegexOptions.IgnoreCase);

        /// <summary>Ключі з чоловічим родом, яким жіноча форма не потрібна, — з причиною.</summary>
        private static readonly Dictionary<string, string> Exceptions = new Dictionary<string, string>
        {
            { "w.player.name", "ім'я героя в окремій ворожнечі (RingFeud — лише герой); у кар'єрі його заступає ім'я героя чи героїні" },
            { "p3.n5", "«сам Молот» — про Молота" },
            { "p4.n2", "«Молоте, ти перший» — букер до Молота" },
            { "p4.n4.recall", "«Ти сам просив» — героїня каже Молотові, він чоловік" },
            { "bs.booker_talk.t3.price", "«такий шанс» — про шанс" },
            { "bs.ev_friend.n7", "«Я якось сам» — Сталевий про себе" },
            { "bs.ev_mixed.n2", "«Молот сам попросив» — про Молота; сцена лише героїні" },
            { "bs.ev_mixed.n4.crowd", "«зал сам вирішить» — про зал" },
            { "bs.ev_betrayal.n2", "«А ти молодий» — сцена лише героя (умова female not): Сталевий виступає в чоловічому дивізіоні федерації 1" },
            { "c.create.default_name", "ім'я за замовчуванням у полі вводу створення — «Новачок» лишається винятком (ПМ 12.11.2026)" },
            { "path.face", "назва ролі обох борців (CareerScreens: і героя, і суперника) — рід за статтю борця, не через «.f»" },
            { "path.heel", "назва ролі обох борців (CareerScreens: і героя, і суперника) — рід за статтю борця, не через «.f»" },
            { "path.rebel", "назва ролі обох борців (CareerScreens: і героя, і суперника) — рід за статтю борця, не через «.f»" },
        };

        /// <summary>Суперник у чоловічому роді: «суперник(а)», «сильніший», «розлюченим», «він тебе», «його / нього».</summary>
        private static readonly Regex RivalMasc = new Regex(
            "(?<!" + L + ")(?:суперник|суперника|суперникові|суперники|суперників|сильніший|сильний|розлюченим|він|його|нього|бив)(?!" + L + ")",
            RegexOptions.IgnoreCase);

        /// <summary>Тексти про суперника тижня (картка, підсумок дії, чутки, розмова з букером): чоловічий рід — «.rf» з білого переліку чи нейтральний текст.</summary>
        private static readonly string[] RivalPrefixes = { "c.card.", "c.delta.", "rumor.", "bs.booker" };   // bs.booker — розмови всіх букерів

        /// <summary>
        /// Білий перелік «.rf» (ПМ 12.11.2026): лише картка тижня й рядок підсумку дії про того самого суперника — на екрані один
        /// суперник, той, що в картці. Новий «.rf» поза переліком — червоний: спершу перевірити, що екран не показує кількох.
        /// </summary>
        private static readonly string[] RivalWhitelist =
        {
            "c.card.rematch", "c.card.rival_strong", "c.delta.rival",
            "c.card.vs", "c.card.rival_place", "c.card.rival_form",   // та сама картка тижня (CareerScreens: рядок суперника й сили)
        };

        /// <summary>Ключі про суперника в чоловічому роді, яким «.rf» не потрібна, — з причиною.</summary>
        private static readonly Dictionary<string, string> RivalExceptions = new Dictionary<string, string>
        {
            { "bs.booker_talk.l1", "«з нього роблять вигляд» — про програш, не про суперника" },
        };

        private static readonly string[] Files = { "strings_uk.json", "backstage_uk.json", "ui_uk.json" };
        private static readonly string[] UiPrefixes = { "ending.", "c.", "season.", "show.", "role.", "week.", "feud.", "reason.", "unavail.", "perk.", "path." };

        private static Dictionary<string, string> Read(string file)
        {
            return MiniJson.ParseObject(CareerKit.Read(file)).ToDictionary(kv => kv.Key, kv => (string)kv.Value);
        }

        private static bool InScope(string file, string key)
        {
            return file != "ui_uk.json" || UiPrefixes.Any(p => key.StartsWith(p, StringComparison.Ordinal));
        }

        private static bool IsForm(string key) { return key.EndsWith(HeroForms.FemaleSuffix, StringComparison.Ordinal); }

        private static List<string> Masculine(string text, bool withOpening = true)
        {
            var res = new List<string>();
            foreach (Match m in YouPastMasc.Matches(text)) res.Add(m.Value);
            foreach (Match m in YouAdjMasc.Matches(text)) res.Add(m.Value);
            if (withOpening) foreach (Match m in OpeningPastMasc.Matches(text)) res.Add(m.Value.Trim());
            foreach (Match m in HeroWords.Matches(text)) res.Add(m.Value);
            return res;
        }

        private static RingText AllText() { return RingText.FromJson(Files.Select(CareerKit.Read).ToArray()); }

        [Test]
        public void EveryHeroLine_InTheMasculine_HasAFemaleForm_OrAReasonedException()
        {
            var bad = new List<string>();
            var flagged = new HashSet<string>();
            foreach (var f in Files)
            {
                var d = Read(f);
                foreach (var kv in d)
                {
                    if (IsForm(kv.Key) || !InScope(f, kv.Key)) continue;
                    var hits = Masculine(kv.Value);
                    if (hits.Count == 0) continue;
                    flagged.Add(kv.Key);
                    if (d.ContainsKey(HeroForms.FemaleKey(kv.Key)) || Exceptions.ContainsKey(kv.Key)) continue;
                    bad.Add(f + ": " + kv.Key + " [" + string.Join(", ", hits) + "] «" + kv.Value + "»");
                }
            }
            Assert.IsEmpty(bad, "чоловічий рід без «.f» і без винятку:\n" + string.Join("\n", bad));

            // перелік винятків не старіє: ключ є, чоловічий рід у ньому ще є, а жіночої форми немає
            var all = new Dictionary<string, string>();   // мовці спільні для двох файлів — останній виграє, як у RingText
            foreach (var f in Files) foreach (var kv in Read(f)) all[kv.Key] = kv.Value;
            foreach (var e in Exceptions)
            {
                Assert.IsTrue(all.ContainsKey(e.Key), "виняток без ключа: " + e.Key);
                Assert.IsTrue(flagged.Contains(e.Key), "виняток зайвий — чоловічого роду в ключі вже немає: " + e.Key);
                Assert.IsFalse(all.ContainsKey(HeroForms.FemaleKey(e.Key)), "і виняток, і «.f»: " + e.Key);
                Assert.IsFalse(string.IsNullOrWhiteSpace(e.Value), "виняток без причини: " + e.Key);
            }
        }

        [Test]
        public void FemaleForms_HaveTheirBaseKey_DifferFromIt_KeepThePlaceholders_AndHaveNoMasculineHero()
        {
            int forms = 0;
            foreach (var f in Files)
            {
                var d = Read(f);
                foreach (var kv in d.Where(x => IsForm(x.Key)))
                {
                    var bk = kv.Key.Substring(0, kv.Key.Length - HeroForms.FemaleSuffix.Length);
                    if (!d.ContainsKey(bk))
                    {
                        // пара статі («c.look.sex.m / .f») — не форма: базового ключа немає, є чоловічий близнюк
                        Assert.IsTrue(d.ContainsKey(bk + ".m"), f + ": «" + kv.Key + "» без базового ключа");
                        continue;
                    }
                    forms++;
                    Assert.AreNotEqual(d[bk], kv.Value, kv.Key + ": форма збігається з базовим текстом");
                    CollectionAssert.AreEquivalent(Placeholders(d[bk]), Placeholders(kv.Value), kv.Key + ": інші {0}");
                    // «Чув, ти вже домовилася» — «Чув» каже Семен про себе, тож початок речення тут не міряємо
                    var hits = Masculine(kv.Value, withOpening: false);
                    Assert.IsEmpty(hits, kv.Key + " — у жіночій формі лишився чоловічий рід: " + string.Join(", ", hits));
                }
            }
            Assert.GreaterOrEqual(forms, 50, "форм героїні замало — файл не той?");
        }

        private static List<string> Placeholders(string s)
        {
            return Regex.Matches(s, "\\{\\d+\\}").Cast<Match>().Select(m => m.Value).ToList();
        }

        [Test]
        public void Apply_GivesTheHeroineHerForms_GivesTheHeroHisOwnBack_AndLeavesTheRestAlone()
        {
            var t = AllText();
            var before = new Dictionary<string, string>(t.AsDictionary());
            var keys = HeroForms.FormKeys(t.AsDictionary());
            Assert.GreaterOrEqual(keys.Count, 50);
            CollectionAssert.Contains(keys, "p1.n1");
            CollectionAssert.Contains(keys, "bs.ev_parking.n5");
            CollectionAssert.Contains(keys, "ending.crowd_favorite");
            CollectionAssert.DoesNotContain(keys, "c.look.sex", "пара статі — не форма");

            Assert.AreEqual(keys.Union(HeroForms.RivalKeys(t.AsDictionary())).Count(), HeroForms.Apply(t, true), "ключі з будь-якою формою");
            foreach (var k in keys) Assert.AreEqual(before[HeroForms.FemaleKey(k)], t.Get(k), k);
            foreach (var kv in before)
                if (!keys.Contains(kv.Key)) Assert.AreEqual(kv.Value, t.Get(kv.Key), "чужий ключ змінився: " + kv.Key);
            Assert.AreEqual("жінка", t.Get("c.look.sex.f"));
            Assert.AreEqual("чоловік", t.Get("c.look.sex.m"));
            Assert.AreEqual(before.Count, t.Count, "словник не росте");

            // героїня не бачить чоловічого звертання ніде, крім переліку винятків
            foreach (var f in Files)
                foreach (var k in Read(f).Keys)
                {
                    if (IsForm(k) || !InScope(f, k) || Exceptions.ContainsKey(k)) continue;
                    var hits = Masculine(t.Get(k), withOpening: !keys.Contains(k));
                    Assert.IsEmpty(hits, k + " у героїні: " + t.Get(k));
                }

            HeroForms.Apply(t, true);   // повторно — те саме
            foreach (var k in keys) Assert.AreEqual(before[HeroForms.FemaleKey(k)], t.Get(k), k);

            HeroForms.Apply(t, false);  // знову герой — усе своє
            foreach (var kv in before) Assert.AreEqual(kv.Value, t.Get(kv.Key), kv.Key);

            var fresh = AllText();       // новий екземпляр (меню перечитало тексти) — свій чоловічий текст береться з нього
            HeroForms.Apply(fresh, false);
            foreach (var kv in before) Assert.AreEqual(kv.Value, fresh.Get(kv.Key), kv.Key);
            HeroForms.Apply(fresh, true);
            Assert.AreEqual(before["p1.n1.f"], fresh.Get("p1.n1"));
            Assert.AreEqual(0, HeroForms.Apply(null, true));
        }

        /// <summary>Екрани беруть звичайний ключ: героїня бачить свою форму в промо ворожнечі й у сцені закулісся.</summary>
        [Test]
        public void Heroine_SeesHerForms_OnTheFeudPromo_AndOnTheBackstageScene()
        {
            var t = AllText();
            var promo = RingCampaignDriver.NewCampaign(1).StartPromo();
            HeroForms.Apply(t, true);
            var pm = new PromoScreenModel(promo, t);
            Assert.AreEqual(t.Get("p1.n1.f"), pm.LineText);
            StringAssert.Contains("мала", pm.LineText);
            HeroForms.Apply(t, false);
            Assert.AreEqual(t.Get("p1.n1"), new PromoScreenModel(RingCampaignDriver.NewCampaign(1).StartPromo(), t).LineText);
            StringAssert.Contains("хлопче", t.Get("p1.n1"));

            HeroForms.Apply(t, true);
            Assert.AreEqual("Улюблениця публіки", t.Get("ending.crowd_favorite"));
            Assert.AreEqual("Ти лишилася. Пропозиція повториться наступного сезону, поки обидва ключі в тебе.", t.Get("c.offer.declined"));
            Assert.AreEqual("Героїня", t.Get("role.hero"));
        }

        [Test]
        public void IsHeroine_OnCreateFollowsTheChosenBody_OnTitleIsTheBaseText_LaterIsTheCareersSex()
        {
            int fb = Array.FindIndex(CareerLooks.HeroBodies, CareerLooks.IsFemaleBody);
            int mb = Array.FindIndex(CareerLooks.HeroBodies, b => !CareerLooks.IsFemaleBody(b));
            Assert.GreaterOrEqual(fb, 0); Assert.GreaterOrEqual(mb, 0);
            var title = CareerKit.NewAtTitle(1);
            Assert.IsFalse(HeroForms.IsHeroine(title, fb), "титул — базові тексти");
            var s = CareerKit.NewAtCreate(1);
            Assert.IsTrue(HeroForms.IsHeroine(s, fb), "створення: жіноче тіло — «Героїня / Лиходійка»");
            Assert.IsFalse(HeroForms.IsHeroine(s, mb));
            Assert.IsTrue(HeroForms.IsHeroine(s, fb + 10 * CareerLooks.HeroBodies.Length) == CareerLooks.IsFemaleBody(CareerLooks.HeroBodies[CareerLooks.HeroBodies.Length - 1]),
                "індекс поза межами — останнє тіло, як у SyncHeroSex");
            var t = AllText();
            HeroForms.Apply(t, HeroForms.IsHeroine(s, fb));
            Assert.AreEqual("Героїня", t.Get("role.hero"));
            Assert.AreEqual("Лиходійка", t.Get("role.villain"));
            HeroForms.Apply(t, HeroForms.IsHeroine(s, mb));
            Assert.AreEqual("Герой", t.Get("role.hero"));

            s.CreateHero(CareerKit.Draft());
            s.SetFemale(true);
            Assert.IsTrue(HeroForms.IsHeroine(s, mb), "після створення — стать кар'єри, не тіла");
            s.SetFemale(false);
            Assert.IsFalse(HeroForms.IsHeroine(s, fb));
        }

        // ---------- рід суперника (ПМ 12.11.2026, P2-6) ----------

        [Test]
        public void EveryRivalLine_InTheMasculine_HasAHeroineVsWomanForm_OrAReasonedException()
        {
            var bad = new List<string>();
            var flagged = new HashSet<string>();
            foreach (var f in new[] { "ui_uk.json", "backstage_uk.json" })
            {
                var d = Read(f);
                foreach (var kv in d)
                {
                    if (IsForm(kv.Key) || kv.Key.EndsWith(HeroForms.RivalSuffix, StringComparison.Ordinal)) continue;
                    if (!RivalPrefixes.Any(x => kv.Key.StartsWith(x, StringComparison.Ordinal))) continue;
                    var hits = RivalMasc.Matches(kv.Value).Cast<Match>().Select(m => m.Value).ToList();
                    if (hits.Count == 0) continue;
                    flagged.Add(kv.Key);
                    if (d.ContainsKey(HeroForms.RivalKey(kv.Key)) || RivalExceptions.ContainsKey(kv.Key)) continue;
                    bad.Add(f + ": " + kv.Key + " [" + string.Join(", ", hits) + "] «" + kv.Value + "»");
                }
            }
            Assert.IsEmpty(bad, "суперник у чоловічому роді без «.rf» і без винятку (або текст нейтрально):\n" + string.Join("\n", bad));
            foreach (var e in RivalExceptions)
            {
                Assert.IsTrue(flagged.Contains(e.Key), "виняток зайвий: " + e.Key);
                Assert.IsFalse(string.IsNullOrWhiteSpace(e.Value), "виняток без причини: " + e.Key);
            }
            CollectionAssert.IsSupersetOf(flagged, new[] { "c.card.rematch", "c.card.rival_strong", "c.delta.rival" }, "ключі з рішення ПМ — у полі зору охоронця");
            var all = new Dictionary<string, string>();
            foreach (var f in Files) foreach (var kv in Read(f)) all[kv.Key] = kv.Value;
            Assert.IsEmpty(RivalMasc.Matches(all["rumor.next_opponent"]).Cast<Match>().Select(m => m.Value).ToList(), "чутка — нейтрально (суперник може змінитися)");
            Assert.IsFalse(all.ContainsKey("rumor.next_opponent.rf"));
            Assert.IsFalse(all.ContainsKey("bs.booker_talk.p2.rf"));
        }

        [Test]
        public void RivalForms_HaveTheirBaseKey_AreTheHeroineVsAWoman_AndKeepThePlaceholders()
        {
            int forms = 0;
            foreach (var f in Files)
            {
                var d = Read(f);
                foreach (var kv in d.Where(x => x.Key.EndsWith(HeroForms.RivalSuffix, StringComparison.Ordinal)))
                {
                    var bk = kv.Key.Substring(0, kv.Key.Length - HeroForms.RivalSuffix.Length);
                    CollectionAssert.Contains(RivalWhitelist, bk, kv.Key + " — «.rf» поза білим переліком (екран з одним суперником?)");
                    Assert.IsTrue(d.ContainsKey(bk), f + ": «" + kv.Key + "» без базового ключа");
                    forms++;
                    Assert.AreNotEqual(d[bk], kv.Value, kv.Key + ": форма збігається з базовим текстом");
                    CollectionAssert.AreEquivalent(Placeholders(d[bk]), Placeholders(kv.Value), kv.Key + ": інші {0}");
                    var rival = RivalMasc.Matches(kv.Value).Cast<Match>().Select(m => m.Value).ToList();
                    Assert.IsEmpty(rival, kv.Key + " — суперник лишився в чоловічому роді: " + string.Join(", ", rival));
                    var hero = Masculine(kv.Value, withOpening: false);
                    Assert.IsEmpty(hero, kv.Key + " — «.rf» для героїні, а тут чоловічий рід героя: " + string.Join(", ", hero));
                }
            }
            Assert.GreaterOrEqual(forms, 6);
        }

        [Test]
        public void Apply_HeroineVsAWoman_TakesRf_HeroineVsTheHammer_TakesF_TheHeroNeverTakesRf()
        {
            var t = AllText();
            var before = new Dictionary<string, string>(t.AsDictionary());
            HeroForms.Apply(t, true, true);
            Assert.IsTrue(HeroForms.ShowsHeroine(t)); Assert.IsTrue(HeroForms.ShowsRivalWoman(t));
            Assert.AreEqual(before["c.card.rival_strong.rf"], t.Get("c.card.rival_strong"));
            Assert.AreEqual(before["c.card.rematch.rf"], t.Get("c.card.rematch"));
            Assert.AreEqual(before["p1.n1.f"], t.Get("p1.n1"), "ключі без «.rf» — форма героїні");

            HeroForms.Apply(t, true, false);   // героїня проти Молота
            Assert.IsTrue(HeroForms.ShowsHeroine(t)); Assert.IsFalse(HeroForms.ShowsRivalWoman(t));
            Assert.AreEqual(before["bs.booker_talk.p2.f"], t.Get("bs.booker_talk.p2"));
            Assert.AreEqual(before["c.card.rival_strong"], t.Get("c.card.rival_strong"), "без «.f» — свій текст");
            Assert.AreEqual(before["c.card.rematch"], t.Get("c.card.rematch"), "«він тебе вже бив» — Молот");

            HeroForms.Apply(t, false, true);   // героя проти жінки кар'єра не ставить — «.rf» не діє
            Assert.IsFalse(HeroForms.ShowsHeroine(t)); Assert.IsFalse(HeroForms.ShowsRivalWoman(t));
            foreach (var kv in before) Assert.AreEqual(kv.Value, t.Get(kv.Key), kv.Key);

            HeroForms.Apply(t, true, true);
            HeroForms.Apply(t, false);
            foreach (var kv in before) Assert.AreEqual(kv.Value, t.Get(kv.Key), "усе своє після героїні: " + kv.Key);
            Assert.IsFalse(HeroForms.ShowsHeroine(null));
        }

        [Test]
        public void OpponentIsWoman_FollowsTheWeeksCard_TheHammerIsAMan_NoOpponentBeforeTheCareer()
        {
            Assert.IsFalse(HeroForms.OpponentIsWoman(CareerKit.NewAtTitle(1)));
            Assert.IsFalse(HeroForms.OpponentIsWoman(CareerKit.NewAtCreate(1)));
            var hero = CareerKit.NewHero(2);
            Assert.IsFalse(hero.Content.FindNpc(hero.State.Opponent).Female);
            Assert.IsFalse(HeroForms.OpponentIsWoman(hero));
            var s = CareerKit.NewAtCreate(3);
            var d = CareerKit.Draft();
            d.Female = true;
            s.CreateHero(d);
            Assert.IsTrue(s.Content.FindNpc(s.State.Opponent).Female, "героїні букер ставить суперницю");
            Assert.IsTrue(HeroForms.OpponentIsWoman(s));
            Assert.AreEqual(s.GetShowCard().Opponent.Female, HeroForms.OpponentIsWoman(s), "з картки тижня");
            s.State.Opponent = Game.Core.Ring.Career.CareerContent.HammerId;
            Assert.IsFalse(HeroForms.OpponentIsWoman(s), "мішаний матч — Молот");
        }

        [Test]
        public void PathName_ByTheWrestlersSex_TheFemaleNamesAreNotFForms()
        {
            var t = AllText();
            foreach (RingPath p in Enum.GetValues(typeof(RingPath)))
            {
                string m = RingLabels.PathName(t, p), f = RingLabels.PathName(t, p, true);
                Assert.AreEqual(m, RingLabels.PathName(t, p, false));
                Assert.AreNotEqual(m, f, p.ToString());
                StringAssert.DoesNotContain("⟦", f);
                Assert.AreEqual(m.Substring(0, 1), f.Substring(0, 1), "мітка шляху (Г / Л / Б) та сама");
            }
            HeroForms.Apply(t, true, true);
            Assert.AreEqual("Герой", RingLabels.PathName(t, RingPath.Face), "HeroForms назв шляхів не чіпає: їх видно в обох борців");
            Assert.AreEqual("Героїня", RingLabels.PathName(t, RingPath.Face, HeroForms.ShowsHeroine(t)));
        }

        [Test]
        public void HerosPaths_InFeudDevelopmentAndPromoReasons_FollowTheHeroinesText()
        {
            var t = AllText();
            var c = RingCampaignDriver.NewCampaign(1);
            HeroForms.Apply(t, true, true);
            Assert.AreEqual("Героїня", new FeudScreenModel(c, t).Paths().First(r => r.Path == RingPath.Face).Name);
            Assert.AreEqual("Лиходійка", new DevelopmentScreenModel(c, t).Paths().First(r => r.Path == RingPath.Heel).Name);
            var cond = new FeudCondition { Kind = ConditionKind.Path, Key = "rebel", Min = 3 };
            StringAssert.Contains("Бунтарка", PromoScreenModel.ReasonText(t, cond));
            HeroForms.Apply(t, false);
            Assert.AreEqual("Герой", new FeudScreenModel(c, t).Paths().First(r => r.Path == RingPath.Face).Name);
            StringAssert.Contains("Бунтар", PromoScreenModel.ReasonText(t, cond));
            StringAssert.DoesNotContain("Бунтарка", PromoScreenModel.ReasonText(t, cond));
        }
    }
}
