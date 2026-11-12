using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Core.Ring;
using Game.Gameplay.Ring.Models;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Жіночі форми звертання (П5, ПМ 12.11.2026): «охоронець: кожен ключ ворожнечі й закулісся, де є чоловічий рід, має `.f`,
    /// або тест-перелік винятків з причиною». Межі — тексти ворожнечі (strings_uk.json), закулісся (backstage_uk.json) і ключі
    /// кінцівок та кар'єри в ui_uk.json. Вибір форми — <see cref="HeroForms"/>: RingText відступу на базовий ключ не знає.
    /// </summary>
    public class HeroFormsTests
    {
        private const string L = "[А-Яа-яІіЇїЄєҐґ'’]";

        /// <summary>«Ти виграв», «ти вже домовився», «Ти сам просив» — минулий час чоловічого роду після «ти».</summary>
        private static readonly Regex YouPastMasc = new Regex(
            "(?<!" + L + ")[Тт]и\\s+(?:(?:вже|не|ж|теж|так|сам|його|її|їх|це|мене|тоді|знову|навіть)\\s+)*" + L + "+(?:в|вся)(?!" + L + ")");

        /// <summary>«Думав, дійдеш…», «Виграв раз…», «Програв — але…» — минулий час на початку речення (звертання без «ти»).</summary>
        private static readonly Regex OpeningPastMasc = new Regex(
            "(?:^|[.!?…:«—]\\s*)" + L + "{2,}(?:ав|ив|ів|ув|яв|ов|ився|ався)(?=[\\s,.!?—])");

        /// <summary>Слова, що називають героя в чоловічому роді.</summary>
        private static readonly Regex HeroWords = new Regex(
            "(?<!" + L + ")(?:хлопче|хлопець|новачку|новачок|сміливий|сміливця|сміливець|сам|такий|цілий|цілим|травмований|зламаним|"
            + "чемпіон|вигнанець|улюбленець|лиходій|лиходієві|лиходія|героєві|бунтареві|герой|забутий)(?!" + L + ")",
            RegexOptions.IgnoreCase);

        /// <summary>Ключі з чоловічим родом, яким жіноча форма не потрібна, — з причиною.</summary>
        private static readonly Dictionary<string, string> Exceptions = new Dictionary<string, string>
        {
            { "w.player.name", "ім'я героя в окремій ворожнечі (RingFeud — лише герой); у кар'єрі його заступає ім'я героя чи героїні" },
            { "p3.n5", "«сам Молот» — про Молота" },
            { "p4.n4.recall", "«Ти сам просив» — героїня каже Молотові, він чоловік" },
            { "bs.booker_talk.t3.price", "«такий шанс» — про шанс" },
            { "bs.ev_friend.n7", "«Я якось сам» — Сталевий про себе" },
            { "bs.ev_mixed.n2", "«Молот сам попросив» — про Молота; сцена лише героїні" },
            { "bs.ev_mixed.n4.crowd", "«зал сам вирішить» — про зал" },
            { "c.create.default_name", "екран створення: стать там ще не вибрано (SyncHeroSex — після CreateHero), поле — «Меню»" },
        };

        private static readonly string[] Files = { "strings_uk.json", "backstage_uk.json", "ui_uk.json" };
        private static readonly string[] UiPrefixes = { "ending.", "c.", "season.", "show.", "role.", "week.", "feud." };

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

            Assert.AreEqual(keys.Count, HeroForms.Apply(t, true));
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
    }
}
