using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Game.Core.Ring;
using Game.Gameplay.Ring.Models;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Охоронці Статуту вигляду «Рингу» (docs/ring/DESIGN_SYSTEM.md): токени арт-директора, контраст, дальтонізм і храповик
    /// відхилень у коді. Палітра живе в docs/ring/design/brand_tokens.json; нові літерали кольору, шрифту чи дрібного кеглю
    /// у Ring-коді не можуть зростати понад базову лінію docs/ring/design/guard_baseline.json (борг лише спадає).
    /// Оновити базову лінію після навмисного зниження: RING_DESIGN_WRITE_BASELINE=1 і прогнати цей клас.
    /// </summary>
    public class RingDesignGuardTests
    {
        // ---------- шляхи й токени ----------

        private static string Root() { return Path.GetDirectoryName(Application.dataPath); }
        private static string TokensPath() { return Path.Combine(Root(), "docs", "ring", "design", "brand_tokens.json"); }
        private static string BaselinePath() { return Path.Combine(Root(), "docs", "ring", "design", "guard_baseline.json"); }

        private static Dictionary<string, object> Tokens() { return MiniJson.ParseObject(File.ReadAllText(TokensPath())); }

        private static Dictionary<string, object> Obj(Dictionary<string, object> parent, string key)
        {
            return (Dictionary<string, object>)parent[key];
        }

        private static string Pascal(string name) { return char.ToUpperInvariant(name[0]) + name.Substring(1); }

        private static uint ParseHex(string hex) { return uint.Parse(hex.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture); }

        private static uint ColorOf(Dictionary<string, object> j, string name)
        {
            var colors = Obj(j, "colors");
            Assert.IsTrue(colors.ContainsKey(name), "невідомий колір у токенах: " + name);
            return ParseHex((string)colors[name]);
        }

        private static object ConstOf(string field)
        {
            var f = typeof(BrandTokens).GetField(field, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(f, "BrandTokens немає поля " + field + ": запусти python tools/ring_design/gen_brand.py");
            return f.GetRawConstantValue();
        }

        // ---------- колірна математика ----------

        private static double[] Rgb01(uint rgb)
        {
            return new[] { ((rgb >> 16) & 255u) / 255.0, ((rgb >> 8) & 255u) / 255.0, (rgb & 255u) / 255.0 };
        }

        private static double Lin(double c) { return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }

        private static double Luminance(uint rgb)
        {
            var c = Rgb01(rgb);
            return 0.2126 * Lin(c[0]) + 0.7152 * Lin(c[1]) + 0.0722 * Lin(c[2]);
        }

        /// <summary>Контраст WCAG: (світліший + 0,05) / (темніший + 0,05).</summary>
        private static double Contrast(uint a, uint b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        // Machado, Oliveira, Fernandes 2009, тяжкість 1,0 (лінійний RGB).
        private static readonly double[][,] CvdMatrices =
        {
            null, // норма
            new[,] { { 0.152286, 1.052583, -0.204868 }, { 0.114503, 0.786281, 0.099216 }, { -0.003882, -0.048116, 1.051998 } }, // протанопія
            new[,] { { 0.367322, 0.860646, -0.227968 }, { 0.280085, 0.672501, 0.047413 }, { -0.011820, 0.042940, 0.968881 } }, // дейтеранопія
            new[,] { { 1.255528, -0.076749, -0.178779 }, { -0.078411, 0.930809, 0.147602 }, { 0.004733, 0.691367, 0.303900 } }  // тританопія
        };
        private static readonly string[] CvdNames = { "норма", "протанопія", "дейтеранопія", "тританопія" };

        private static double[] LabOf(uint rgb, double[,] cvd)
        {
            var c = Rgb01(rgb);
            double[] l = { Lin(c[0]), Lin(c[1]), Lin(c[2]) };
            if (cvd != null)
            {
                var o = new double[3];
                for (int i = 0; i < 3; i++)
                    o[i] = Math.Max(0, Math.Min(1, cvd[i, 0] * l[0] + cvd[i, 1] * l[1] + cvd[i, 2] * l[2]));
                l = o;
            }
            double x = 0.4124564 * l[0] + 0.3575761 * l[1] + 0.1804375 * l[2];
            double y = 0.2126729 * l[0] + 0.7151522 * l[1] + 0.0721750 * l[2];
            double z = 0.0193339 * l[0] + 0.1191920 * l[1] + 0.9503041 * l[2];
            Func<double, double> f = t => t > 216.0 / 24389.0 ? Math.Pow(t, 1.0 / 3.0) : (24389.0 / 27.0 * t + 16.0) / 116.0;
            double fx = f(x / 0.95047), fy = f(y / 1.0), fz = f(z / 1.08883);
            return new[] { 116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz) };
        }

        private static double DeltaE(uint a, uint b, double[,] cvd)
        {
            var la = LabOf(a, cvd); var lb = LabOf(b, cvd);
            return Math.Sqrt((la[0] - lb[0]) * (la[0] - lb[0]) + (la[1] - lb[1]) * (la[1] - lb[1]) + (la[2] - lb[2]) * (la[2] - lb[2]));
        }

        // ---------- токени ----------

        [Test]
        public void BrandTokens_MatchJson()
        {
            var j = Tokens();
            foreach (var kv in Obj(j, "colors"))
                Assert.AreEqual(ParseHex((string)kv.Value), (uint)ConstOf(Pascal(kv.Key)), "колір " + kv.Key);
            foreach (var f in typeof(BrandTokens).GetFields(BindingFlags.Public | BindingFlags.Static).Where(x => x.FieldType == typeof(uint)))
                Assert.IsTrue(Obj(j, "colors").Keys.Any(k => Pascal(k) == f.Name), "у BrandTokens зайвий колір " + f.Name + " (немає в JSON)");

            foreach (var kv in Obj(j, "alpha"))
                Assert.AreEqual(Convert.ToDouble(kv.Value), Convert.ToDouble(ConstOf("Alpha" + Pascal(kv.Key))), 1e-6, "alpha " + kv.Key);
            var type = Obj(j, "type");
            Assert.AreEqual(Convert.ToInt32(type["refHeight"]), (int)ConstOf("TypeRefHeight"));
            Assert.AreEqual(Convert.ToInt32(type["minInfo"]), (int)ConstOf("TypeMinInfo"));
            foreach (var kv in Obj(type, "sizes")) Assert.AreEqual(Convert.ToInt32(kv.Value), (int)ConstOf("Type" + Pascal(kv.Key)), "кегль " + kv.Key);
            foreach (var kv in Obj(type, "stinger")) Assert.AreEqual(Convert.ToInt32(kv.Value), (int)ConstOf("Stinger" + Pascal(kv.Key)), "виноска " + kv.Key);
            foreach (var kv in Obj(j, "space")) Assert.AreEqual(Convert.ToInt32(kv.Value), (int)ConstOf("Space" + Pascal(kv.Key)), "простір " + kv.Key);
            foreach (var kv in Obj(j, "motion"))
                Assert.AreEqual(Convert.ToDouble(kv.Value), Convert.ToDouble(ConstOf("Motion" + Pascal(kv.Key))), 1e-6, "рух " + kv.Key);
        }

        [Test]
        public void GeneratedFiles_AreInSyncWithJson()
        {
            var j = Tokens();
            string py = File.ReadAllText(Path.Combine(Root(), "tools", "blender", "ring_brand.py"));
            string css = File.ReadAllText(Path.Combine(Root(), "docs", "ring", "design", "brand_tokens.css"));
            string theme = File.ReadAllText(Path.Combine(Root(), "Assets", "_Project", "Scripts", "Gameplay", "Ring", "UI", "RingTheme.cs"));
            foreach (var kv in Obj(j, "colors"))
            {
                string hex = ((string)kv.Value).ToUpperInvariant();
                StringAssert.Contains("'" + kv.Key + "': '" + hex + "'", py, "ring_brand.py не збігається з JSON: " + kv.Key + " (запусти gen_brand.py)");
                StringAssert.Contains("--" + kv.Key + ": " + hex + ";", css, "brand_tokens.css не збігається з JSON: " + kv.Key);
                StringAssert.Contains("Color " + Pascal(kv.Key) + " = Rgb(BrandTokens." + Pascal(kv.Key) + ");", theme, "RingTheme.cs не збігається з JSON: " + kv.Key);
            }
            foreach (var kv in Obj(j, "roles"))
                StringAssert.Contains("Color " + Pascal(kv.Key) + " = " + Pascal((string)kv.Value) + ";", theme, "роль " + kv.Key + " у RingTheme.cs не збігається з JSON");
        }

        [Test]
        public void Tokens_RolesPointToExistingColors_AndColorsAreUnique()
        {
            var j = Tokens();
            var colors = Obj(j, "colors");
            foreach (var kv in Obj(j, "roles")) Assert.IsTrue(colors.ContainsKey((string)kv.Value), "роль " + kv.Key + " → невідомий колір " + kv.Value);
            var dup = colors.GroupBy(kv => ((string)kv.Value).ToUpperInvariant()).Where(g => g.Count() > 1).Select(g => string.Join("=", g.Select(x => x.Key)));
            Assert.IsEmpty(dup.ToList(), "один колір під двома іменами (другого «золота» не буває, Статут COL-01)");
        }

        [Test]
        public void Tokens_ContrastPairsMeetMinimums()
        {
            var j = Tokens();
            var bad = new List<string>();
            foreach (Dictionary<string, object> p in (List<object>)j["pairs"])
            {
                uint fg = ColorOf(j, (string)p["fg"]), bg = ColorOf(j, (string)p["bg"]);
                double min = Convert.ToDouble(p["min"]), got = Contrast(fg, bg);
                if (got + 1e-9 < min) bad.Add(p["fg"] + " на " + p["bg"] + ": " + got.ToString("0.00", CultureInfo.InvariantCulture) + " < " + min + " (" + p["use"] + ")");
            }
            Assert.IsEmpty(bad, "контраст нижче норми XAG/WCAG:\n" + string.Join("\n", bad));
        }

        [Test]
        public void Tokens_SemanticColorsStayDistinct_ForColorBlindPlayers()
        {
            var j = Tokens();
            var bad = new List<string>();
            foreach (Dictionary<string, object> p in (List<object>)j["distinct"])
            {
                uint a = ColorOf(j, (string)p["a"]), b = ColorOf(j, (string)p["b"]);
                double min = Convert.ToDouble(p["minDeltaE"]);
                for (int k = 0; k < CvdMatrices.Length; k++)
                {
                    double de = DeltaE(a, b, CvdMatrices[k]);
                    if (de < min) bad.Add(p["a"] + " ↔ " + p["b"] + " (" + p["use"] + ") під «" + CvdNames[k] + "»: ΔE " + de.ToString("0", CultureInfo.InvariantCulture) + " < " + min);
                }
            }
            Assert.IsEmpty(bad, "колір не єдиний носій, але пари мають лишатись розрізнюваними:\n" + string.Join("\n", bad));
        }

        [Test]
        public void Tokens_TypeSizes_RespectXagMinimum()
        {
            // XAG 101: ≥18 px на ПК при 1080p. Еталон 900 px: 15 × 1080/900 = 18.
            double at1080 = BrandTokens.TypeMinInfo * 1080.0 / BrandTokens.TypeRefHeight;
            Assert.GreaterOrEqual(at1080, 18.0, "TypeMinInfo=" + BrandTokens.TypeMinInfo + " дає " + at1080 + " px @1080p");
            Assert.GreaterOrEqual(BrandTokens.TypeLabel, BrandTokens.TypeMinInfo, "мітки не менші за мінімум інформації");
            Assert.GreaterOrEqual(BrandTokens.TypeBody, BrandTokens.TypeMinInfo, "основний текст не менший за мінімум інформації");
        }

        // ---------- храповик відхилень у коді ----------

        private static readonly Regex ColorCtor = new Regex(@"\bnew\s+Color(32)?\s*\(", RegexOptions.Compiled);
        private static readonly Regex HexLiteral = new Regex(@"#[0-9A-Fa-f]{6}(?:[0-9A-Fa-f]{2})?\b", RegexOptions.Compiled);
        private static readonly Regex NamedColor = new Regex(@"\bColor\.(red|green|blue|white|black|yellow|cyan|magenta|gray|grey)\b", RegexOptions.Compiled);
        private static readonly Regex FontLoad = new Regex(@"FontDefinition\.FromFont|unityFontDefinition|Resources\.Load<Font>|CreateDynamicFontFromOSFont|\bLoadFont\s*\(", RegexOptions.Compiled);
        private static readonly string[] Allowlist = { "RingTheme.cs", "BrandTokens.cs" };
        private static readonly string[] Categories = { "colorCtor", "hexLiteral", "namedColor", "fontLoad", "smallFont" };

        private sealed class SourceFile { public string Rel; public string Code; }

        private static List<SourceFile> GuardedSources()
        {
            string root = Root();
            var files = new List<string>();
            string ring = Path.Combine(root, "Assets", "_Project", "Scripts", "Gameplay", "Ring");
            if (Directory.Exists(ring)) files.AddRange(Directory.GetFiles(ring, "*.cs", SearchOption.AllDirectories));
            string editor = Path.Combine(root, "Assets", "_Project", "Scripts", "Editor");
            if (Directory.Exists(editor)) files.AddRange(Directory.GetFiles(editor, "Ring*.cs", SearchOption.TopDirectoryOnly));
            var list = new List<SourceFile>();
            foreach (string f in files.OrderBy(x => x, StringComparer.Ordinal))
            {
                if (Allowlist.Contains(Path.GetFileName(f))) continue;
                string rel = f.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
                list.Add(new SourceFile { Rel = rel, Code = StripComments(File.ReadAllText(f)) });
            }
            return list;
        }

        private static string StripComments(string s)
        {
            var sb = new StringBuilder(s.Length);
            int i = 0, n = s.Length;
            while (i < n)
            {
                char c = s[i];
                if (c == '/' && i + 1 < n && s[i + 1] == '/') { while (i < n && s[i] != '\n') i++; continue; }
                if (c == '/' && i + 1 < n && s[i + 1] == '*') { i += 2; while (i + 1 < n && !(s[i] == '*' && s[i + 1] == '/')) i++; i += 2; continue; }
                if (c == '@' && i + 1 < n && s[i + 1] == '"')
                {
                    sb.Append("@\""); i += 2;
                    while (i < n)
                    {
                        if (s[i] == '"') { if (i + 1 < n && s[i + 1] == '"') { sb.Append("\"\""); i += 2; continue; } break; }
                        sb.Append(s[i]); i++;
                    }
                    if (i < n) { sb.Append('"'); i++; }
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    char q = c; sb.Append(c); i++;
                    while (i < n && s[i] != q)
                    {
                        if (s[i] == '\\' && i + 1 < n) { sb.Append(s[i]); sb.Append(s[i + 1]); i += 2; continue; }
                        sb.Append(s[i]); i++;
                    }
                    if (i < n) { sb.Append(q); i++; }
                    continue;
                }
                sb.Append(c); i++;
            }
            return sb.ToString();
        }

        /// <summary>Аргументи виклику верхнього рівня від позиції після «(» до парної «)».</summary>
        private static List<string> Args(string code, int start)
        {
            var args = new List<string>();
            var cur = new StringBuilder();
            int depth = 0;
            for (int i = start; i < code.Length; i++)
            {
                char c = code[i];
                if (c == '"' || c == '\'')
                {
                    char q = c; cur.Append(c); i++;
                    while (i < code.Length && code[i] != q) { if (code[i] == '\\' && i + 1 < code.Length) { cur.Append(code[i]); i++; } cur.Append(code[i]); i++; }
                    if (i < code.Length) cur.Append(q);
                    continue;
                }
                if (c == '(' || c == '[' || c == '{') depth++;
                if (c == ')' || c == ']' || c == '}')
                {
                    if (depth == 0) { args.Add(cur.ToString()); return args; }
                    depth--;
                }
                if (c == ',' && depth == 0) { args.Add(cur.ToString()); cur.Clear(); continue; }
                cur.Append(c);
            }
            return args;
        }

        private static bool IsSmallLiteral(string arg, int min)
        {
            arg = Regex.Replace(arg.Trim(), @"^size\s*:\s*", "");
            var m = Regex.Match(arg, @"^(\d+(?:\.\d+)?)f?$");
            return m.Success && double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) < min;
        }

        private static int CountSmallFonts(string code)
        {
            int min = BrandTokens.TypeMinInfo, count = 0;
            foreach (Match m in Regex.Matches(code, @"\bLbl\s*\("))
            {
                var a = Args(code, m.Index + m.Length);
                if (a.Count >= 2 && IsSmallLiteral(a[1], min)) count++;
            }
            foreach (Match m in Regex.Matches(code, @"\bMakeButton\s*\("))
            {
                var a = Args(code, m.Index + m.Length);
                if (a.Count >= 5 && IsSmallLiteral(a[4], min)) count++;
            }
            foreach (Match m in Regex.Matches(code, @"\bfontSize\s*=\s*(\d+(?:\.\d+)?)f?\s*;"))
                if (double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) < min) count++;
            return count;
        }

        private static int CountOf(string category, string code)
        {
            switch (category)
            {
                case "colorCtor": return ColorCtor.Matches(code).Count;
                case "hexLiteral": return HexLiteral.Matches(code).Count;
                case "namedColor": return NamedColor.Matches(code).Count;
                case "fontLoad": return FontLoad.Matches(code).Count;
                case "smallFont": return CountSmallFonts(code);
                default: throw new ArgumentException(category);
            }
        }

        private static Dictionary<string, Dictionary<string, int>> Measure()
        {
            var sources = GuardedSources();
            var all = new Dictionary<string, Dictionary<string, int>>();
            foreach (string cat in Categories)
            {
                var d = new Dictionary<string, int>();
                foreach (var s in sources) { int n = CountOf(cat, s.Code); if (n > 0) d[s.Rel] = n; }
                all[cat] = d;
            }
            return all;
        }

        private static void WriteBaseline(Dictionary<string, Dictionary<string, int>> all)
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"note\": \"Базова лінія храповика (RingDesignGuardTests): кількість літералів кольору/шрифту/дрібного кеглю на файл. Лише спадає. Оновлювати: RING_DESIGN_WRITE_BASELINE=1 і прогнати RingDesignGuardTests. Статут: docs/ring/DESIGN_SYSTEM.md.\"");
            foreach (string cat in Categories)
            {
                sb.Append(",\n  \"").Append(cat).Append("\": {");
                bool first = true;
                foreach (var kv in all[cat].OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    sb.Append(first ? "\n    " : ",\n    ").Append('"').Append(kv.Key).Append("\": ").Append(kv.Value);
                    first = false;
                }
                sb.Append(first ? "}" : "\n  }");
            }
            sb.Append("\n}\n");
            File.WriteAllText(BaselinePath(), sb.ToString(), new UTF8Encoding(false));
        }

        private static Dictionary<string, Dictionary<string, int>> Baseline()
        {
            var j = MiniJson.ParseObject(File.ReadAllText(BaselinePath()));
            var all = new Dictionary<string, Dictionary<string, int>>();
            foreach (string cat in Categories)
            {
                var d = new Dictionary<string, int>();
                if (j.ContainsKey(cat)) foreach (var kv in Obj(j, cat)) d[kv.Key] = Convert.ToInt32(kv.Value);
                all[cat] = d;
            }
            return all;
        }

        private static void AssertRatchet(string category, string what, string how)
        {
            if (Environment.GetEnvironmentVariable("RING_DESIGN_WRITE_BASELINE") == "1")
            {
                WriteBaseline(Measure());
                Assert.Pass("базову лінію записано: " + BaselinePath());
            }
            Assert.IsTrue(File.Exists(BaselinePath()), "немає guard_baseline.json: RING_DESIGN_WRITE_BASELINE=1");
            var now = Measure()[category];
            var was = Baseline()[category];
            var worse = new List<string>();
            var better = new List<string>();
            foreach (var kv in now)
            {
                int allowed; was.TryGetValue(kv.Key, out allowed);
                if (kv.Value > allowed) worse.Add(kv.Key + ": " + kv.Value + " > " + allowed);
                else if (kv.Value < allowed) better.Add(kv.Key + ": " + kv.Value + " < " + allowed);
            }
            foreach (var kv in was) if (!now.ContainsKey(kv.Key) && kv.Value > 0) better.Add(kv.Key + ": 0 < " + kv.Value);
            if (better.Count > 0) TestContext.WriteLine("Храповик: можна знизити базову лінію (" + category + "): " + string.Join("; ", better));
            Assert.IsEmpty(worse, what + " — нових відхилень більше не можна (Статут вигляду):\n" + string.Join("\n", worse) + "\n" + how);
        }

        [Test]
        public void Ratchet_ColorConstructors_DoNotGrow()
        {
            AssertRatchet("colorCtor", "`new Color(...)` поза RingTheme", "Бери колір з RingTheme (токени); потрібен новий — запит арт-директору (У0).");
        }

        [Test]
        public void Ratchet_HexLiterals_DoNotGrow()
        {
            AssertRatchet("hexLiteral", "hex-літерал кольору (#RRGGBB) у коді", "Кольори лише з токенів; для rich text — RingTheme + string.Format.");
        }

        [Test]
        public void Ratchet_NamedColors_DoNotGrow()
        {
            AssertRatchet("namedColor", "Color.red/white/... замість токена", "Заміни на RingTheme.* (Color.clear дозволено).");
        }

        [Test]
        public void Ratchet_FontLoads_DoNotGrow()
        {
            AssertRatchet("fontLoad", "завантаження/призначення шрифту поза темою", "Шрифти призначає лише тема (ролі display/text/hand/spray), не екрани.");
        }

        [Test]
        public void Ratchet_SmallFonts_DoNotGrow()
        {
            AssertRatchet("smallFont", "кегль менший за TypeMinInfo", "Інформація, за якою гравець діє, ≥ BrandTokens.TypeMinInfo (XAG 18 px @1080p).");
        }
    }
}
