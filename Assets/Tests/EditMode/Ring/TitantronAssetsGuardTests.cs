using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Game.Core.Ring;
using Game.Gameplay.Ring.Models;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Охоронці генератора екранів виходу (docs/ring/design/briefs/B07_entrance_screen.md): kits.json і запечені пули
    /// (Assets/Art/Ring/Titantron/pools) живуть лише на токенах, золото — лише гравцю, прозорість — лише з кроків
    /// (BrandTokens.AlphaTron*), бюджет шарів, удар на сітці бітів; а еталонні значення плавності, станів шарів і
    /// вибору варіанта (golden.json, Python) відтворюються еталонною реалізацією нижче — її й бере програвач.
    /// Python-бік: cd tools &amp;&amp; python -m ring_design.titantron.selfcheck.
    /// </summary>
    public class TitantronAssetsGuardTests
    {
        // ---------- шляхи й JSON ----------

        private static string Root() { return Path.GetDirectoryName(Application.dataPath); }
        private static string KitsPath() { return Path.Combine(Root(), "docs", "ring", "design", "titantron", "kits.json"); }
        private static string TokensPath() { return Path.Combine(Root(), "docs", "ring", "design", "brand_tokens.json"); }
        private static string ArtDir() { return Path.Combine(Root(), "Assets", "Art", "Ring", "Titantron"); }

        private static Dictionary<string, object> Load(string path) { return MiniJson.ParseObject(File.ReadAllText(path)); }
        private static Dictionary<string, object> Kits() { return Load(KitsPath()); }
        private static Dictionary<string, object> O(object o) { return (Dictionary<string, object>)o; }
        private static List<object> A(object o) { return (List<object>)o; }
        private static double N(object o) { return (double)o; }
        private static IEnumerable<string> Strs(object o) { return A(o).Cast<string>(); }

        private static IEnumerable<KeyValuePair<string, Dictionary<string, object>>> AllPools()
        {
            var kits = Kits();
            foreach (var g in O(kits["gimmicks"]).Keys)
                foreach (var role in Strs(kits["roles"]))
                    foreach (var tier in Strs(kits["tiersOrder"]))
                    {
                        string name = g + "_" + role + "_" + tier + ".json";
                        string path = Path.Combine(ArtDir(), "pools", name);
                        Assert.IsTrue(File.Exists(path), "немає пулу " + name + ": cd tools && python -m ring_design.titantron.bake");
                        yield return new KeyValuePair<string, Dictionary<string, object>>(name, Load(path));
                    }
        }

        // ---------- слоти кольорів ----------

        private static Dictionary<string, string> Slots(Dictionary<string, object> kits, string role, string tier, bool player)
        {
            var m = new Dictionary<string, string>();
            foreach (var kv in O(O(kits["slots"])[role])) m[kv.Key] = (string)kv.Value;
            if (player) foreach (var kv in O(kits["playerSlots"])) m[kv.Key] = (string)kv.Value;
            if ((bool)O(O(kits["tiers"])[tier])["mono"]) foreach (var kv in O(kits["monoSlots"])) m[kv.Key] = (string)kv.Value;
            return m;
        }

        private static string TokenOf(string tint, Dictionary<string, string> slots)
        {
            if (!tint.StartsWith("$")) return tint;
            string tok;
            Assert.IsTrue(slots.TryGetValue(tint, out tok), "слот без значення: " + tint);
            return tok;
        }

        private static IEnumerable<string> Tints(Dictionary<string, object> layer)
        {
            if (layer.ContainsKey("tint")) yield return (string)layer["tint"];
            if (layer.ContainsKey("text"))
            {
                var t = O(layer["text"]);
                yield return t.ContainsKey("fill") ? (string)t["fill"] : "$paper";
                if (t.ContainsKey("shadow")) yield return (string)A(t["shadow"])[0];
                if (t.ContainsKey("outline")) yield return (string)A(t["outline"])[0];
            }
        }

        private static IEnumerable<Dictionary<string, object>> Timelines(Dictionary<string, object> pool)
        {
            foreach (var v in A(pool["variants"])) yield return O(v);
        }

        // ---------- kits.json ----------

        [Test]
        public void Kits_SlotsAndAlphaStepsResolveToTokens()
        {
            var kits = Kits();
            var tokens = Load(TokensPath());
            var colors = O(tokens["colors"]);
            foreach (var role in Strs(kits["roles"]))
                foreach (var kv in O(O(kits["slots"])[role]))
                    Assert.IsTrue(colors.ContainsKey((string)kv.Value), role + " " + kv.Key + " → невідомий токен " + kv.Value);
            foreach (var kv in O(kits["playerSlots"])) Assert.IsTrue(colors.ContainsKey((string)kv.Value), "playerSlots " + kv.Key);
            foreach (var kv in O(kits["monoSlots"])) Assert.IsTrue(colors.ContainsKey((string)kv.Value), "monoSlots " + kv.Key);
            var alpha = O(tokens["alpha"]);
            foreach (var kv in O(kits["alpha"])) Assert.IsTrue(alpha.ContainsKey((string)kv.Value), "крок прозорості без токена: " + kv.Value);
            Assert.AreEqual(N(alpha["tronGhost"]), BrandTokens.AlphaTronGhost, 1e-6);
            Assert.AreEqual(N(alpha["tronDim"]), BrandTokens.AlphaTronDim, 1e-6);
            Assert.AreEqual(N(alpha["tronMid"]), BrandTokens.AlphaTronMid, 1e-6);
        }

        [Test]
        public void Kits_GoldBelongsToThePlayerOnly()
        {
            var kits = Kits();
            foreach (var role in Strs(kits["roles"]))
                foreach (var tier in Strs(kits["tiersOrder"]))
                {
                    foreach (var kv in Slots(kits, role, tier, false))
                        Assert.AreNotEqual("gold", kv.Value, "золото в NPC: " + role + "/" + tier + " " + kv.Key);
                    foreach (var kv in Slots(kits, role, tier, true))
                        if (kv.Value == "gold")
                            Assert.IsTrue(kv.Key == "$accent" || kv.Key == "$flash", "золото гравця лише в $accent/$flash, а не в " + kv.Key);
                }
        }

        // ---------- запечені пули ----------

        [Test]
        public void Pools_ExistForEveryCombination_AndMatchTheKits()
        {
            var kits = Kits();
            int variants = (int)N(kits["variants"]);
            int count = 0;
            foreach (var kv in AllPools())
            {
                var pool = kv.Value;
                string tier = (string)pool["tier"];
                var tcfg = O(O(kits["tiers"])[tier]);
                Assert.AreEqual(variants, A(pool["variants"]).Count, kv.Key + ": кількість варіантів");
                foreach (var tl in Timelines(pool))
                {
                    string tag = kv.Key + " v" + N(tl["variant"]);
                    Assert.LessOrEqual(A(tl["layers"]).Count, (int)N(tcfg["maxLayers"]), tag + ": шарів більше за бюджет щабля");
                    Assert.AreEqual(N(tcfg["hitBeat"]), N(tl["hitBeat"]), tag + ": hitBeat");
                    Assert.AreEqual(N(tcfg["fps"]), N(tl["fps"]), tag + ": fps");
                    Assert.AreEqual(N(O(O(kits["gimmicks"])[(string)tl["gimmick"]])["bpm"]), N(tl["bpm"]), tag + ": bpm");
                    Assert.AreEqual(0.0, (N(tl["hitBeat"]) * 2) % 1.0, tag + ": удар не на сітці півбітів");
                    Assert.IsTrue(A(tl["events"]).Select(O).Any(e => (string)e["t"] == "hit" && N(e["b"]) == N(tl["hitBeat"])), tag + ": немає події hit");
                    Assert.LessOrEqual(N(tl["introBeats"]) * 60.0 / N(tl["bpm"]), 5.0, tag + ": вступ довший за 5 с");
                }
                count++;
            }
            Assert.AreEqual(O(kits["gimmicks"]).Count * A(kits["roles"]).Count * A(kits["tiersOrder"]).Count, count);
        }

        [Test]
        public void Pools_ColoursAreSlots_AndGoldIsNeverInNpcRolls()
        {
            var kits = Kits();
            var colors = O(Load(TokensPath())["colors"]);
            foreach (var kv in AllPools())
                foreach (var tl in Timelines(kv.Value))
                    foreach (var layer in A(tl["layers"]).Select(O))
                        foreach (var tint in Tints(layer))
                            foreach (var role in Strs(kits["roles"]))
                                foreach (var tier in Strs(kits["tiersOrder"]))
                                {
                                    string tok = TokenOf(tint, Slots(kits, role, tier, false));
                                    string where = kv.Key + " " + (string)layer["id"] + " " + tint;
                                    Assert.IsTrue(colors.ContainsKey(tok), where + " → невідомий токен " + tok);
                                    Assert.AreNotEqual("gold", tok, where + ": золото в ролику NPC");
                                }
        }

        [Test]
        public void Pools_OpacityComesFromTokenSteps()
        {
            var allowed = new[] { 0.0, 1.0, BrandTokens.AlphaTronGhost, BrandTokens.AlphaTronDim, BrandTokens.AlphaTronMid };
            Func<double, bool> ok = v => allowed.Any(a => Math.Abs(a - v) < 1e-3);
            foreach (var kv in AllPools())
                foreach (var tl in Timelines(kv.Value))
                    foreach (var layer in A(tl["layers"]).Select(O))
                    {
                        string where = kv.Key + " " + (string)layer["id"];
                        if (layer.ContainsKey("a")) Assert.IsTrue(ok(N(layer["a"])), where + ": статична прозорість " + N(layer["a"]));
                        if (layer.ContainsKey("tr") && O(layer["tr"]).ContainsKey("a"))
                            foreach (var key in A(O(layer["tr"])["a"]))
                                Assert.IsTrue(ok(N(A(key)[1])), where + ": ключ прозорості " + N(A(key)[1]));
                    }
        }

        [Test]
        public void Pools_FlashesStayBelowSafetyLimits()
        {
            // WCAG 2.3.1: не більше трьох спалахів за секунду; тут — один шар-спалах не яскравіший за ghost (різниця яскравості < 10 %)
            foreach (var kv in AllPools())
                foreach (var tl in Timelines(kv.Value))
                {
                    string tag = kv.Key + " v" + N(tl["variant"]);
                    var layers = A(tl["layers"]).Select(O).ToList();
                    var flashes = layers.Where(l => ((string)l["id"]).StartsWith("flash")).ToList();
                    Assert.AreEqual(1, flashes.Count, tag + ": шарів-спалахів");
                    foreach (var key in A(O(flashes[0]["tr"])["a"]))
                        Assert.LessOrEqual(N(A(key)[1]), BrandTokens.AlphaTronGhost + 1e-4, tag + ": спалах яскравіший за ghost");
                    double perSec = N(tl["bpm"]) / 60.0;
                    foreach (var l in layers.Where(l => l.ContainsKey("mod")))
                    {
                        var m = O(l["mod"]);
                        if (m.ContainsKey("blink")) Assert.LessOrEqual(perSec, 3.0, tag + " " + l["id"] + ": блимає частіше 3/с");
                        if (m.ContainsKey("flick")) Assert.LessOrEqual(perSec / N(A(m["flick"])[0]), 3.0, tag + " " + l["id"] + ": спалахує частіше 3/с");
                    }
                }
        }

        [Test]
        public void Pools_SpritesFontsAndEasingAreKnown()
        {
            var kits = Kits();
            var easing = new HashSet<string>(Strs(kits["easing"]));
            var fonts = O(kits["fonts"]);
            foreach (var kv in AllPools())
                foreach (var tl in Timelines(kv.Value))
                    foreach (var layer in A(tl["layers"]).Select(O))
                    {
                        string where = kv.Key + " " + (string)layer["id"];
                        if ((string)layer["k"] == "sprite")
                        {
                            string sid = (string)layer["sprite"];
                            bool ok = sid.StartsWith("brand_")
                                ? File.Exists(Path.Combine(Root(), "Assets", "Art", "Ring", "Brand", sid == "brand_seal" ? "emblem_seal_1024.png" : "rope_frame_1040.png"))
                                : File.Exists(Path.Combine(ArtDir(), "sprites", sid + ".png"));
                            Assert.IsTrue(ok, where + ": немає спрайта " + sid);
                        }
                        if ((string)layer["k"] == "text")
                        {
                            var t = O(layer["text"]);
                            Assert.IsTrue(new[] { "nick", "full", "tag" }.Contains((string)t["slot"]), where + ": текстовий слот " + t["slot"]);
                            Assert.IsTrue(fonts.ContainsKey((string)t["font"]), where + ": шрифт " + t["font"]);
                            Assert.GreaterOrEqual(N(t["size"]) * 512, 30.0, where + ": кегль надто малий");
                        }
                        if (layer.ContainsKey("tr"))
                            foreach (var prop in O(layer["tr"]))
                            {
                                double last = -1;
                                foreach (var key in A(prop.Value).Select(A))
                                {
                                    Assert.GreaterOrEqual(N(key[0]), last, where + "." + prop.Key + ": ключі не за зростанням");
                                    last = N(key[0]);
                                    if (key.Count > 2) Assert.IsTrue(easing.Contains((string)key[2]), where + "." + prop.Key + ": плавність " + key[2]);
                                }
                            }
                    }
        }

        // ---------- паритет Python ↔ C# (golden.json) ----------

        private static double Ease(string e, double t)
        {
            switch (e)
            {
                case "l": return t;
                case "o2": return 1 - Math.Pow(1 - t, 2);
                case "o3": return 1 - Math.Pow(1 - t, 3);
                case "i2": return t * t;
                case "i3": return t * t * t;
                case "io2": return t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
                case "ob": { const double c1 = 1.70158; double c3 = c1 + 1; return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2); }
                case "s": return 0;
                default: throw new ArgumentException("плавність " + e);
            }
        }

        private static double Track(List<object> keys, double b)
        {
            var k0 = A(keys[0]);
            if (b <= N(k0[0])) return N(k0[1]);
            for (int i = 1; i < keys.Count; i++)
            {
                var p = A(keys[i - 1]);
                var q = A(keys[i]);
                double b0 = N(p[0]), v0 = N(p[1]), b1 = N(q[0]), v1 = N(q[1]);
                if (b < b1)
                {
                    string e = q.Count > 2 ? (string)q[2] : "l";
                    double t = b1 > b0 ? (b - b0) / (b1 - b0) : 1.0;
                    return v0 + (v1 - v0) * Ease(e, t);
                }
            }
            return N(A(keys[keys.Count - 1])[1]);
        }

        /// <summary>Еталонна реалізація стану шару в біті (формули — B07); програвач UI Toolkit відтворює саме їх.</summary>
        private static Dictionary<string, double> State(Dictionary<string, object> layer, double b)
        {
            Func<string, double, double> g = (k, d) => layer.ContainsKey(k) ? N(layer[k]) : d;
            var st = new Dictionary<string, double>
            {
                { "x", g("x", 0) }, { "y", g("y", 0) }, { "s", g("s", 1) }, { "sx", g("sx", 1) }, { "sy", g("sy", 1) },
                { "rot", g("rot", 0) }, { "a", g("a", 1) }, { "clip", g("clip", 1) }, { "w", g("w", 1) }, { "h", g("h", 1) },
                { "ox", 0 }, { "oy", 0 },
            };
            if (layer.ContainsKey("tr"))
                foreach (var kv in O(layer["tr"])) st[kv.Key] = Track(A(kv.Value), b);
            if (!layer.ContainsKey("mod")) return st;
            var m = O(layer["mod"]);
            if (m.ContainsKey("pulse"))
            {
                var v = A(m["pulse"]);
                double every = N(v[0]), amp = N(v[1]), decay = N(v[2]), start = N(v[3]);
                if (b >= start) st["s"] *= 1 + amp * Math.Exp(-((b - start) % every) / decay);
            }
            if (m.ContainsKey("spin")) { var v = A(m["spin"]); if (b > N(v[1])) st["rot"] += N(v[0]) * (b - N(v[1])); }
            if (m.ContainsKey("sway"))
            {
                var v = A(m["sway"]);
                double k = Math.Sin(2 * Math.PI * (b / N(v[2]) + N(v[3])));
                st["x"] += N(v[0]) * k;
                st["y"] += N(v[1]) * k;
            }
            if (m.ContainsKey("rock")) { var v = A(m["rock"]); st["rot"] += N(v[0]) * Math.Sin(2 * Math.PI * (b / N(v[1]) + N(v[2]))); }
            if (m.ContainsKey("drift"))
            {
                var v = A(m["drift"]);
                if (b > N(v[2])) { st["ox"] += N(v[0]) * (b - N(v[2])); st["oy"] += N(v[1]) * (b - N(v[2])); }
            }
            if (m.ContainsKey("blink"))
            {
                var v = A(m["blink"]);
                if (b >= N(v[1]) && ((int)Math.Floor(b)) % 2 != (int)N(v[0])) st["a"] = 0;
            }
            if (m.ContainsKey("flick"))
            {
                var v = A(m["flick"]);
                if (b < N(v[2]) || ((b - N(v[2])) % N(v[0])) >= N(v[1])) st["a"] = 0;
            }
            if (m.ContainsKey("sweep"))
            {
                var v = A(m["sweep"]);
                double per = N(v[0]), start = N(v[1]);
                if (b < start) st["a"] = 0;
                else st["x"] = N(v[2]) + (N(v[3]) - N(v[2])) * (((b - start) % per) / per);
            }
            return st;
        }

        private static uint Fnv1a(string s)
        {
            uint h = 0x811C9DC5;
            foreach (byte by in Encoding.UTF8.GetBytes(s)) { h ^= by; h *= 0x01000193; }
            return h;
        }

        [Test]
        public void Golden_EasingFormulasMatchPython()
        {
            var golden = Load(Path.Combine(ArtDir(), "golden.json"));
            foreach (var kv in O(golden["ease"]))
                foreach (var pt in A(kv.Value).Select(A))
                    Assert.AreEqual(N(pt[1]), Ease(kv.Key, N(pt[0])), 1e-4, "плавність " + kv.Key + " при t=" + N(pt[0]));
        }

        [Test]
        public void Golden_LayerStatesMatchPython()
        {
            var golden = Load(Path.Combine(ArtDir(), "golden.json"));
            int checkedStates = 0;
            foreach (var sample in A(golden["states"]).Select(O))
            {
                var pool = Load(Path.Combine(ArtDir(), "pools", (string)sample["pool"]));
                var tl = O(A(pool["variants"])[(int)N(sample["variant"])]);
                double beat = N(sample["beat"]);
                var expected = O(sample["layers"]);
                foreach (var layer in A(tl["layers"]).Select(O))
                {
                    var exp = O(expected[(string)layer["id"]]);
                    var got = State(layer, beat);
                    foreach (var kv in exp)
                        Assert.AreEqual(N(kv.Value), got[kv.Key], 2e-3,
                            sample["pool"] + " v" + sample["variant"] + " біт " + beat + " шар " + layer["id"] + "." + kv.Key);
                    checkedStates++;
                }
            }
            Assert.Greater(checkedStates, 100);
        }

        [Test]
        public void Golden_VariantPickMatchesPython()
        {
            var golden = Load(Path.Combine(ArtDir(), "golden.json"));
            int variants = (int)N(Kits()["variants"]);
            foreach (var e in A(golden["variantFor"]).Select(O))
            {
                uint h = Fnv1a((string)e["name"] + "|" + (int)N(e["seed"]));
                Assert.AreEqual(N(e["hash"]), (double)h, "хеш для «" + e["name"] + "»");
                Assert.AreEqual(N(e["variant"]), (double)(h % (uint)variants));
            }
        }
    }
}
