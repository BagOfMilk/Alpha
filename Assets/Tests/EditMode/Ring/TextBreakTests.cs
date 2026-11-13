using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Core.Ring.Career;
using Game.Gameplay.Ring.Models;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Правило переносу «тире рядка не починає» (ПМ 12.11.2026, кадр слоту 28: у рядку поразки «— удруге зал не підніме» тире стало
    /// на початок другого рядка; нерозривного пробілу U+00A0 у Fixel немає). <see cref="TextBreak.GlueDashes"/> тримає слово перед
    /// тире разом із ним (<c>&lt;nobr&gt;</c> UI Toolkit); <c>MenuKit.Body</c> і рядки-підказки екранів кличуть її. Модель переносу —
    /// <see cref="TtfMetrics.Wrap"/> (жадібно за пробілами, як рушій; <c>&lt;nobr&gt;</c> — одне слово).
    /// </summary>
    public class TextBreakTests
    {
        private static TtfMetrics _regular, _semi;
        private static TtfMetrics Regular { get { return _regular ?? (_regular = TtfMetrics.Load("ThirdParty", "Fonts", "Fixel", "FixelText-Regular.ttf")); } }
        private static TtfMetrics Semi { get { return _semi ?? (_semi = TtfMetrics.Load("ThirdParty", "Fonts", "Fixel", "FixelText-SemiBold.ttf")); } }

        /// <summary>Пункт на папері підсумку (ShowKit.Bullet): папір 410 − відступи 26 і 24 − квадрат 11 і просвіт 8.</summary>
        private const float ResultBulletW = 410f - 26f - 24f - 11f - 8f;

        /// <summary>Рядок сповіщення тижня (WeekScreenView.LastResult): смуга 410 − відступи 20 і 26.</summary>
        private const float WeekNoteW = CareerTileLayout.StripW - 20f - 26f;

        private static bool StartsWithDash(string line)
        {
            return line.StartsWith("—", StringComparison.Ordinal) || line.StartsWith("–", StringComparison.Ordinal);
        }

        private static bool StartsWithSeparator(string line) { return line.StartsWith("·", StringComparison.Ordinal); }

        [Test]
        public void GlueDashes_KeepsTheWordBeforeTheDash_WithIt()
        {
            Assert.AreEqual("Поразка дала <nobr>популярність —</nobr> удруге не підніме",
                TextBreak.GlueDashes("Поразка дала популярність — удруге не підніме"));
            Assert.AreEqual("<nobr>10+ —</nobr> <nobr>ворожнеча +1;</nobr> <nobr>7–9 —</nobr> одне з двох; <nobr>≤6 —</nobr> зірване промо.",
                TextBreak.GlueDashes("10+ — ворожнеча +1; 7–9 — одне з двох; ≤6 — зірване промо."), "кожне окреме тире; діапазон «7–9» не чіпається");
            Assert.AreEqual("<nobr>Програв –</nobr> але", TextBreak.GlueDashes("Програв – але"), "коротке тире, що стоїть окремо, — так само");
            Assert.AreEqual("Діапазон 7–9 і 1-2", TextBreak.GlueDashes("Діапазон 7–9 і 1-2"), "без пробілів довкола — не тире-слово");
            Assert.AreEqual("— так", TextBreak.GlueDashes("— так"), "тире на самому початку тексту нема до чого клеїти");
            Assert.IsNull(TextBreak.GlueDashes(null));
            Assert.AreEqual("", TextBreak.GlueDashes(""));

            string s = "Пояс — у тебе, а зал — твій.";
            string g = TextBreak.GlueDashes(s);
            Assert.AreEqual(g, TextBreak.GlueDashes(g), "повторний виклик нічого не міняє");
            Assert.AreEqual(s, TextBreak.Strip(g), "без тегів — той самий текст, що бачить гравець");
            Assert.AreEqual(s.Split(' ').Length - 2, TtfMetrics.Units(g).Count, "слово з тире — одна одиниця переносу (два тире — дві склейки)");
        }

        /// <summary>Роздільник частин « · » тримається зі словом перед ним (огляд слоту 30, ПМ: «·» на початку рядка сповіщення).</summary>
        [Test]
        public void GlueDashes_KeepsTheWordBeforeTheSeparator_WithIt()
        {
            Assert.AreEqual("<nobr>+3 ·</nobr> <nobr>ажіотаж +2</nobr>", TextBreak.GlueDashes("+3 · ажіотаж +2"));
            Assert.AreEqual("<nobr>Промо ·</nobr> кидок 11, Успіх <nobr>(10+) ·</nobr> <nobr>ворожнеча +1 ·</nobr> ажіотаж",
                TextBreak.GlueDashes("Промо · кидок 11, Успіх (10+) · ворожнеча +1 · ажіотаж"), "кожен роздільник");
            Assert.AreEqual("<nobr>Рух ·</nobr> <nobr>Удар —</nobr> так", TextBreak.GlueDashes("Рух · Удар — так"), "тире й роздільник в одному рядку");
            Assert.AreEqual("а·б і 7·9", TextBreak.GlueDashes("а·б і 7·9"), "без пробілів довкола — не роздільник-слово");
            Assert.AreEqual("· так", TextBreak.GlueDashes("· так"), "роздільник на самому початку тексту нема до чого клеїти");
            Assert.IsTrue(TextBreak.IsSeparator("·"));
            Assert.IsFalse(TextBreak.IsSeparator("а·б"));

            string s = "Сезон 2 · тиждень 3 з 10 · ворожнеча +1";
            string g = TextBreak.GlueDashes(s);
            Assert.AreEqual(g, TextBreak.GlueDashes(g), "повторний виклик нічого не міняє");
            Assert.AreEqual(s, TextBreak.Strip(g), "без тегів — той самий текст");
            Assert.AreEqual(s.Split(' ').Length - 3, TtfMetrics.Units(g).Count, "слово з роздільником (і «ворожнеча +1») — одна одиниця переносу");
        }

        /// <summary>
        /// Число зі знаком тримається зі словом перед ним (огляд слоту 31, ПМ: «ворожнеча» в кінці рядка, «+1» на початку наступного).
        /// Слово, число й роздільник за ним — один шматок; знак без цифри й дефіс у слові — ні.
        /// </summary>
        [Test]
        public void GlueDashes_KeepsTheSignedNumber_WithTheWordBeforeIt()
        {
            Assert.AreEqual("Промо: <nobr>ворожнеча +1</nobr>", TextBreak.GlueDashes("Промо: ворожнеча +1"));
            Assert.AreEqual("<nobr>довіра −1 ·</nobr> <nobr>гроші +₴40</nobr>", TextBreak.GlueDashes("довіра −1 · гроші +₴40"), "мінус U+2212 і гроші");
            Assert.AreEqual("<nobr>Сила +2,</nobr> <nobr>Спритність -1.</nobr>", TextBreak.GlueDashes("Сила +2, Спритність -1."), "розділовий знак у кінці числа");
            Assert.AreEqual("плюс + мінус − і 1-2", TextBreak.GlueDashes("плюс + мінус − і 1-2"), "знак без цифри й дефіс — не число зі знаком");
            Assert.IsTrue(TextBreak.IsSignedNumber("+1")); Assert.IsTrue(TextBreak.IsSignedNumber("−12")); Assert.IsTrue(TextBreak.IsSignedNumber("+₴5"));
            Assert.IsFalse(TextBreak.IsSignedNumber("+")); Assert.IsFalse(TextBreak.IsSignedNumber("+₴")); Assert.IsFalse(TextBreak.IsSignedNumber("1+"));
            Assert.IsFalse(TextBreak.IsSignedNumber(null));

            // рядок відгуку тижня з ключів WeekScreenView.ResultText: без правила «+1» хоч на одній ширині стає на початок рядка
            var t = RingText.FromJson(CareerKit.Read("ui_uk.json"));
            string s = string.Join(" · ", new[]
            {
                t.Get("week.action.3"), t.Format("c.roll", 9, "+ 1", 10, "Успіх (10+)"), t.Format("c.delta.trust", "+1"), t.Format("c.delta.feud", "+1"),
            });
            float widest;
            bool reproduced = false;
            var bad = new List<string>();
            foreach (var font in new[] { Regular, Semi })
                for (float w = WeekNoteW - 80f; w <= WeekNoteW + 80f; w += 2f)
                {
                    if (font.Wrap(s, BrandTokens.TypeMinInfo, w, 0f, out widest).Skip(1).Any(StartsWithSignedNumber)) reproduced = true;
                    var glued = font.Wrap(TextBreak.GlueDashes(s), BrandTokens.TypeMinInfo, w, 0f, out widest);
                    if (glued.Skip(1).Any(StartsWithSignedNumber) || glued.Skip(1).Any(StartsWithSeparator)) bad.Add(w + ": " + string.Join(" / ", glued));
                }
            Assert.IsTrue(reproduced, "без правила модель має відтворити ваду хоч на одній ширині: " + s);
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        private static bool StartsWithSignedNumber(string line)
        {
            int sp = line.IndexOf(' ');
            return TextBreak.IsSignedNumber(sp < 0 ? line : line.Substring(0, sp));
        }

        /// <summary>
        /// Рядок сповіщення тижня з кадру слоту 30 (w_t2_a1080\016: «· ажіотаж на старті +2» з нового рядка), зібраний із тих самих
        /// ключів, що WeekScreenView.ResultText: без правила модель відтворює ваду, з правилом — ні.
        /// </summary>
        [Test]
        public void WeekNote_FromTheSlot30Frame_NeverStartsALineWithTheSeparator()
        {
            var t = RingText.FromJson(CareerKit.Read("ui_uk.json"));
            string s = string.Join(" · ", new[]
            {
                t.Get("week.action.2"), t.Format("c.roll", 11, "+ 0", 11, "Успіх (10+)"),
                t.Format("c.delta.feud", "+1"), t.Format("c.delta.hype", "+2"),
            });
            float widest;
            bool reproduced = false;
            var bad = new List<string>();
            foreach (var font in new[] { Regular, Semi })
                for (float w = WeekNoteW - 60f; w <= WeekNoteW + 60f; w += 2f)
                {
                    if (font.Wrap(s, BrandTokens.TypeMinInfo, w, 0f, out widest).Skip(1).Any(StartsWithSeparator)) reproduced = true;
                    var glued = font.Wrap(TextBreak.GlueDashes(s), BrandTokens.TypeMinInfo, w, 0f, out widest);
                    if (glued.Skip(1).Any(StartsWithSeparator)) bad.Add(w + ": " + string.Join(" / ", glued));
                }
            Assert.IsTrue(reproduced, "без правила модель має відтворити ваду кадру хоч на одній ширині: " + s);
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        /// <summary>
        /// Рядок поразки з кадру слоту 28 на папері підсумку: без правила тире стає на початок рядка (вада відтворюється моделлю),
        /// з правилом — ні.
        /// </summary>
        [Test]
        public void LossLine_OnTheResultPaper_KeepsTheDashOnTheFirstLine()
        {
            string s = RingText.FromJson(CareerKit.Read("ui_uk.json")).Get("c.result.loss_pop.season");
            StringAssert.Contains(" — ", s);
            float widest;
            var raw = Semi.Wrap(s, BrandTokens.TypeMinInfo, ResultBulletW, 0f, out widest);
            Assert.IsTrue(raw.Exists(StartsWithDash), "без правила модель має відтворити ваду кадру: " + string.Join(" / ", raw));
            var glued = Semi.Wrap(TextBreak.GlueDashes(s), BrandTokens.TypeMinInfo, ResultBulletW, 0f, out widest);
            Assert.IsFalse(glued.Exists(StartsWithDash), string.Join(" / ", glued));
            Assert.LessOrEqual(widest, ResultBulletW, "склеєне слово з тире вміщається в рядок");
            Assert.AreEqual(s, string.Join(" ", glued), "той самий текст, лише інакше розбитий");
        }

        /// <summary>
        /// Усі рядки текстів екранів із тире-словом (ui_uk.json, strings_uk.json): за будь-якої ширини від 120 до 720 і обох накреслень
        /// тексту (15 і 17) жоден рядок, крім першого, не починається з тире.
        /// </summary>
        [Test]
        public void EveryText_WithASpacedDash_NeverStartsALineWithIt()
        {
            var bad = new List<string>();
            int checkedTexts = 0;
            foreach (var file in new[] { "ui_uk.json", "strings_uk.json" })
                foreach (var v in Values(CareerKit.Read(file)))
                {
                    if (v.IndexOf(" — ", StringComparison.Ordinal) < 0 && v.IndexOf(" – ", StringComparison.Ordinal) < 0) continue;
                    checkedTexts++;
                    string s = Regex.Replace(v, @"\{\d+(:[^}]*)?\}", "10");
                    string g = TextBreak.GlueDashes(s);
                    foreach (var font in new[] { Regular, Semi })
                        foreach (var size in new[] { BrandTokens.TypeMinInfo, 17f })
                            for (float w = 120f; w <= 720f; w += 3f)
                            {
                                float widest;
                                var lines = font.Wrap(g, size, w, 0f, out widest);
                                for (int i = 1; i < lines.Count; i++)
                                    if (StartsWithDash(lines[i]) && bad.Count < 20)
                                        bad.Add(string.Format(CultureInfo.InvariantCulture, "{0}: «{1}» — рядок {2} з тире ({3}, {4:0})", file, v, i + 1, size, w));
                            }
                }
            Assert.Greater(checkedTexts, 20, "текстів із тире-словом — десятки; менше — розбір JSON зламався");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        /// <summary>
        /// Усі рядки текстів із роздільником « · » (ui_uk.json, strings_uk.json) — так само: жоден рядок, крім першого, не починається з «·».
        /// </summary>
        [Test]
        public void EveryText_WithASpacedSeparator_NeverStartsALineWithIt()
        {
            var bad = new List<string>();
            int checkedTexts = 0;
            foreach (var file in new[] { "ui_uk.json", "strings_uk.json" })
                foreach (var v in Values(CareerKit.Read(file)))
                {
                    if (v.IndexOf(" · ", StringComparison.Ordinal) < 0) continue;
                    checkedTexts++;
                    string s = Regex.Replace(v, @"\{\d+(:[^}]*)?\}", "10");
                    string g = TextBreak.GlueDashes(s);
                    foreach (var font in new[] { Regular, Semi })
                        foreach (var size in new[] { BrandTokens.TypeMinInfo, 17f })
                            for (float w = 120f; w <= 720f; w += 3f)
                            {
                                float widest;
                                var lines = font.Wrap(g, size, w, 0f, out widest);
                                for (int i = 1; i < lines.Count; i++)
                                    if (StartsWithSeparator(lines[i]) && bad.Count < 20)
                                        bad.Add(string.Format(CultureInfo.InvariantCulture, "{0}: «{1}» — рядок {2} з «·» ({3}, {4:0})", file, v, i + 1, size, w));
                            }
                }
            Assert.Greater(checkedTexts, 20, "текстів із роздільником — десятки; менше — розбір JSON зламався");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        /// <summary>Екрани ставлять текст через правило: мітка тексту (MenuKit.Body) і кожен рядок-підказка, що отримує опис.</summary>
        [Test]
        public void Sources_LabelsAndHints_GlueDashes()
        {
            string ui = Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Ring", "UI");
            string kit = File.ReadAllText(Path.Combine(ui, "Menus", "MenuKit.cs"));
            StringAssert.Contains("new Label(TextBreak.GlueDashes(text))", kit, "MenuKit.Body");
            // пояснення поради під кнопкою («Радимо зараз») — багаторядкове й з тире («…на три — цей перк…»), мітка не через MenuKit.Body
            // (кадри слоту 30: правило оминало його)
            StringAssert.Contains("RingUi.Lbl(TextBreak.GlueDashes(why),", File.ReadAllText(Path.Combine(ui, "CareerScreens.cs")), "пояснення поради (Rec)");
            var hintSet = new Regex(@"\b(_hint|HintLine|hint)\.text\s*=\s*([^;]*);");
            int setters = 0;
            foreach (var rel in new[] { Path.Combine("Career", "WeekScreenView.cs"), Path.Combine("Career", "CreateScreenView.cs"), "CareerScreens.cs", "RingUi.cs" })
            {
                string src = File.ReadAllText(Path.Combine(ui, rel));
                foreach (Match m in hintSet.Matches(src))
                {
                    string rhs = m.Groups[2].Value.Trim();
                    if (rhs.TrimEnd(')', ' ') == "\"\"") continue;   // прибрати підказку
                    setters++;
                    StringAssert.Contains("TextBreak.GlueDashes(", rhs, rel + ": " + m.Value);
                }
            }
            Assert.GreaterOrEqual(setters, 7, "рядків-підказок з описом — сім (тиждень, створення ×2, кнопки екранів ×2, RingUi ×2)");
        }

        /// <summary>Значення рядків JSON-словника («ключ»: «значення»), з розекрануванням лапок і косих.</summary>
        private static IEnumerable<string> Values(string json)
        {
            foreach (Match m in Regex.Matches(json, "\"(?:[^\"\\\\]|\\\\.)*\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\""))
                yield return m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\\\", "\\");
        }
    }
}
