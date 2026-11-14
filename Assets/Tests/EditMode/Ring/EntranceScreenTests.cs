using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core.Ring;
using Game.Gameplay.Ring.Models;
using Game.Gameplay.Ring.Stage;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Програвач екрана виходу (ТЗ B07): рантайм-розбір пулів і формули шарів (<see cref="TronLayer"/>, <see cref="TronTimeline"/>)
    /// відтворюють Python-еталон директора (<c>Assets/Art/Ring/Titantron/golden.json</c>, допуск 2e-3), слоти кольорів —
    /// дзеркало <c>docs/ring/design/titantron/kits.json</c>, «Менше руху» прибирає спалах, тремтіння й мерехтіння.
    /// </summary>
    public class EntranceScreenTests
    {
        private static string Root() { return Path.GetDirectoryName(Application.dataPath); }
        private static string ArtDir() { return Path.Combine(Root(), "Assets", "Art", "Ring", "Titantron"); }
        private static Dictionary<string, object> Load(string path) { return MiniJson.ParseObject(File.ReadAllText(path)); }
        private static Dictionary<string, object> O(object o) { return (Dictionary<string, object>)o; }
        private static List<object> A(object o) { return (List<object>)o; }
        private static double N(object o) { return (double)o; }

        private static readonly Dictionary<string, TronPool> Pools = new Dictionary<string, TronPool>();

        private static TronPool Pool(string file)
        {
            TronPool p;
            if (!Pools.TryGetValue(file, out p)) Pools[file] = p = TronPool.Parse(File.ReadAllText(Path.Combine(ArtDir(), "pools", file)));
            return p;
        }

        private static double Get(TronState st, string key)
        {
            switch (key)
            {
                case "x": return st.X; case "y": return st.Y; case "s": return st.S; case "sx": return st.Sx; case "sy": return st.Sy;
                case "rot": return st.Rot; case "a": return st.A; case "clip": return st.Clip; case "w": return st.W; case "h": return st.H;
                case "ox": return st.Ox; case "oy": return st.Oy;
                default: throw new ArgumentException(key);
            }
        }

        [Test]
        public void Golden_LayerStatesMatchPython()
        {
            var golden = Load(Path.Combine(ArtDir(), "golden.json"));
            int checkedValues = 0;
            foreach (var raw in A(golden["states"]))
            {
                var s = O(raw);
                string file = (string)s["pool"];
                if (!file.EndsWith(".json")) file += ".json";
                var tl = Pool(file).Variants[(int)N(s["variant"])];
                double beat = N(s["beat"]);
                foreach (var kv in O(s["layers"]))
                {
                    var layer = tl.Layers.FirstOrDefault(l => l.Id == kv.Key);
                    Assert.IsNotNull(layer, file + ": немає шару " + kv.Key);
                    var st = new TronState();
                    layer.Evaluate(beat, false, ref st);
                    foreach (var e in O(kv.Value))
                    {
                        Assert.AreEqual(N(e.Value), Get(st, e.Key), 2e-3, file + " v" + tl.Variant + " b" + beat + " " + kv.Key + "." + e.Key);
                        checkedValues++;
                    }
                }
            }
            Assert.Greater(checkedValues, 100, "замало перевірених станів");
        }

        [Test]
        public void Golden_EasingMatchesPython()
        {
            var golden = Load(Path.Combine(ArtDir(), "golden.json"));
            foreach (var kv in O(golden["ease"]))
                foreach (var pt in A(kv.Value))
                {
                    var p = A(pt);
                    Assert.AreEqual(N(p[1]), EntranceScreenMath.Ease(kv.Key, N(p[0])), 1e-4, kv.Key + " t=" + N(p[0]));
                }
        }

        [Test]
        public void Golden_VariantPickMatchesPython()
        {
            var golden = Load(Path.Combine(ArtDir(), "golden.json"));
            foreach (var raw in A(golden["variantFor"]))
            {
                var v = O(raw);
                string name = (string)v["name"]; int seed = (int)N(v["seed"]);
                Assert.AreEqual((uint)N(v["hash"]), EntranceScreenMath.Fnv1a(name + "|" + seed), name);
                Assert.AreEqual((int)N(v["variant"]), EntranceScreenMath.VariantFor(name, seed, 4), name);
            }
        }

        [Test]
        public void Slots_MatchKitsJson()
        {
            var kits = Load(Path.Combine(Root(), "docs", "ring", "design", "titantron", "kits.json"));
            CollectionAssert.AreEquivalent(O(O(kits["slots"])["hero"]).Keys.ToArray(), EntranceScreenSlots.SlotNames, "набір слотів");
            CollectionAssert.AreEqual(A(kits["roles"]).Cast<string>().ToArray(), EntranceScreenSlots.Roles);
            CollectionAssert.AreEqual(A(kits["tiersOrder"]).Cast<string>().ToArray(), EntranceScreenSlots.Tiers);
            CollectionAssert.AreEquivalent(O(kits["gimmicks"]).Keys.ToArray(), EntranceScreenSlots.Gimmicks);
            foreach (var g in EntranceScreenSlots.Gimmicks)
                Assert.AreEqual((int)N(O(O(kits["gimmicks"])[g])["bpm"]), EntranceScreenSlots.Bpm(g), "bpm " + g);
            foreach (var tier in EntranceScreenSlots.Tiers)
            {
                var kt = O(O(kits["tiers"])[tier]);
                var t = EntranceScreenSlots.Tier(tier);
                Assert.AreEqual((int)N(kt["fps"]), t.Fps, tier + " fps");
                Assert.AreEqual((int)N(kt["hitBeat"]), t.HitBeat, tier + " hitBeat");
                Assert.AreEqual((int)N(kt["maxLayers"]), t.MaxLayers, tier + " maxLayers");
                Assert.AreEqual((bool)kt["mono"], t.Mono, tier + " mono");
                Assert.AreEqual((bool)kt["handFont"], t.HandFont, tier + " handFont");
                foreach (var role in EntranceScreenSlots.Roles)
                    foreach (bool player in new[] { false, true })
                    {
                        var m = new Dictionary<string, string>();
                        foreach (var kv in O(O(kits["slots"])[role])) m[kv.Key] = (string)kv.Value;
                        if (player) foreach (var kv in O(kits["playerSlots"])) m[kv.Key] = (string)kv.Value;
                        if (t.Mono) foreach (var kv in O(kits["monoSlots"])) m[kv.Key] = (string)kv.Value;
                        foreach (var kv in m)
                            Assert.AreEqual(kv.Value, EntranceScreenSlots.Token(kv.Key, role, player, t.Mono), role + "/" + tier + "/" + player + " " + kv.Key);
                    }
            }
        }

        [Test]
        public void Slots_GoldOnlyForThePlayer()
        {
            foreach (var tier in EntranceScreenSlots.Tiers)
                foreach (var role in EntranceScreenSlots.Roles)
                    foreach (var slot in EntranceScreenSlots.SlotNames)
                        Assert.AreNotEqual("gold", EntranceScreenSlots.Token(slot, role, false, EntranceScreenSlots.Tier(tier).Mono),
                            "золото в NPC: " + role + "/" + tier + " " + slot + " (Статут вигляду COL-02, FX-09)");
        }

        [Test]
        public void Tokens_EveryBrandColourResolves()
        {
            var tokens = Load(Path.Combine(Root(), "docs", "ring", "design", "brand_tokens.json"));
            foreach (var kv in O(tokens["colors"]))
            {
                uint rgb;
                Assert.IsTrue(EntranceScreenSlots.TryRgb(kv.Key, out rgb), "токен " + kv.Key);
                Assert.AreEqual(Convert.ToUInt32(((string)kv.Value).Substring(1), 16), rgb, "токен " + kv.Key);
            }
        }

        [Test]
        public void Pools_ParseWithinBudget_AndHitOnTheBeatGrid()
        {
            int timelines = 0;
            foreach (var g in EntranceScreenSlots.Gimmicks)
                foreach (var role in EntranceScreenSlots.Roles)
                    foreach (var tierKey in EntranceScreenSlots.Tiers)
                    {
                        var pool = Pool(EntranceScreenMath.PoolName(g, role, tierKey) + ".json");
                        var tier = EntranceScreenSlots.Tier(tierKey);
                        Assert.AreEqual(4, pool.Variants.Count, pool.Gimmick + pool.Role + pool.Tier);
                        foreach (var tl in pool.Variants)
                        {
                            timelines++;
                            string id = g + "_" + role + "_" + tierKey + " v" + tl.Variant;
                            Assert.LessOrEqual(tl.Layers.Count, tier.MaxLayers, id + ": шарів більше за бюджет щабля (Статут Д6)");
                            Assert.AreEqual(tier.Fps, tl.Fps, 1e-9, id + " fps");
                            Assert.AreEqual(tier.HitBeat, tl.HitBeat, 1e-9, id + " hitBeat");
                            Assert.AreEqual(1, tl.Events.Count(e => e.T == "hit" && Math.Abs(e.B - tl.HitBeat) < 1e-9), id + ": удар не на hitBeat");
                            Assert.AreEqual(1, tl.Layers.Count(l => l.IsFlash), id + ": шар-спалах має бути рівно один");
                            foreach (var l in tl.Layers)
                            {
                                Assert.IsTrue(new[] { "rect", "plate", "disc", "ring", "sprite", "text" }.Contains(l.Kind), id + " " + l.Id + ": k=" + l.Kind);
                                if (l.Kind == "text") Assert.IsNotNull(l.Text, id + " " + l.Id);
                                if (l.Kind == "sprite") Assert.IsFalse(string.IsNullOrEmpty(l.Sprite), id + " " + l.Id);
                                if (l.Tint != null)
                                {
                                    uint rgb;
                                    Assert.IsTrue(EntranceScreenSlots.TryRgb(EntranceScreenSlots.Token(l.Tint, role, false, tier.Mono), out rgb), id + " " + l.Id + " tint " + l.Tint);
                                }
                            }
                        }
                    }
            Assert.AreEqual(144, timelines);
        }

        [Test]
        public void ReduceMotion_DropsSpinRockBlinkFlickSweepAndShake_CapsPulse()
        {
            var layer = new TronLayer
            {
                Id = "x", Kind = "rect",
                Pulse = new double[] { 1, 0.2, 0.3, 0 }, Spin = new double[] { 30, 0 }, Rock = new double[] { 10, 2, 0 },
                Blink = new double[] { 0, 0 }, Flick = new double[] { 1, 0.2, 0 }, Sweep = new double[] { 2, 0, -1, 1 },
            };
            var st = new TronState();
            layer.Evaluate(1.5, true, ref st);
            Assert.AreEqual(0, st.Rot, 1e-9, "spin і rock");
            Assert.AreEqual(1, st.A, 1e-9, "blink/flick/sweep не гасять шар");
            Assert.AreEqual(0, st.X, 1e-9, "sweep не рухає шар");
            Assert.LessOrEqual(st.S, 1.03 + 1e-9, "pulse ≤ 3 %");
            layer.Evaluate(1.5, false, ref st);
            Assert.AreNotEqual(0, st.Rot, "без «Менше руху» модифікатори діють");

            var tl = new TronTimeline();
            tl.Events.Add(new TronEvent { T = "shake", B = 1, Amp = 0.02, Dur = 1 });
            double dx, dy;
            tl.Shake(1.3, true, out dx, out dy);
            Assert.AreEqual(0, dx, 1e-12); Assert.AreEqual(0, dy, 1e-12);
            tl.Shake(1.3, false, out dx, out dy);
            Assert.AreNotEqual(0, dx + dy);
            Assert.IsTrue(new TronLayer { Id = "flash" }.IsFlash);
        }

        [Test]
        public void Recipe_TextSlots_NickFullTag()
        {
            var r = new EntranceRecipe { Name = "Тарас «Молот» Гребінь", TagText = "Монстр · Лиходій" };
            Assert.AreEqual("Молот", r.Nick);
            Assert.AreEqual("Тарас Гребінь", r.Full);
            Assert.AreEqual("МОЛОТ", r.SlotText("nick"));
            Assert.AreEqual("ТАРАС ГРЕБІНЬ", r.SlotText("full"));
            Assert.AreEqual("МОНСТР · ЛИХОДІЙ", r.SlotText("tag"));
            var p = new EntranceRecipe { Name = "Ґанна Єрмак" };
            Assert.AreEqual("Ґанна Єрмак", p.Nick, "без прізвиська — повне ім'я");
            Assert.AreEqual("", p.Full, "без прізвиська шар full не малюється");
            Assert.AreEqual("ҐАННА ЄРМАК", p.SlotText("nick"));
            Assert.AreEqual("", new EntranceRecipe().SlotText("nick"));
        }

        [Test]
        public void Timing_HitCurtainHoldAndSteppedBeat()
        {
            Assert.AreEqual(1.2, EntranceScreenMath.HitTimeSec(100, 2), 1e-9);
            Assert.AreEqual(2.5, EntranceScreenMath.CurtainHoldSec(1.2), 1e-9);
            Assert.AreEqual(2.2, EntranceScreenMath.CurtainHoldSec(0.5), 1e-9, "не менше 2,2 с");
            Assert.AreEqual(4 * 60.0 / 132 + 1.3, EntranceScreenMath.CurtainHoldSec(EntranceScreenMath.HitTimeSec(132, 4)), 1e-9);
            // ступінчастий показ: 6 кадр/с, 120 BPM — 0,25 с показує кадр 1/6 с
            Assert.AreEqual(1.0 / 6 * 2, EntranceScreenMath.ShownBeat(0.25, 120, 6), 1e-9);
            Assert.AreEqual(0, EntranceScreenMath.ShownBeat(0.1, 120, 6), 1e-9);
        }

        /// <summary>
        /// Ґіміки 5–8 на екрані виходу (рев'ю ПМ 12–13.11.2026): кожне значення <see cref="Game.Core.Ring.Career.Gimmick"/> веде до
        /// ключа пулу з <see cref="EntranceScreenSlots.Gimmicks"/> — за сімейством виходу (Силач — «monster»), нових пулів немає; а назва в
        /// тезі — своя, ключем ґіміка з контенту, і в роді борця. Героїня-Силачка й суперник-Силач на одному шоу: «Силачка · Героїня» і
        /// «Силач · Лиходій» — форми героїні тег суперника не підміняють. Гра й огляд кличуть той самий <see cref="EntranceScreenSlots.Tag"/>.
        /// </summary>
        [Test]
        public void EveryGimmick_LeadsToAPoolKey_AndOwnTagName()
        {
            var content = CareerKit.Content();
            var t = RingText.FromJson(CareerKit.Read("strings_uk.json"), CareerKit.Read("ui_uk.json"));   // свій текст: Apply міняє його
            foreach (Game.Core.Ring.Career.Gimmick g in Enum.GetValues(typeof(Game.Core.Ring.Career.Gimmick)))
            {
                string pool = EntranceScreenSlots.GimmickKey(g);
                CollectionAssert.Contains(EntranceScreenSlots.Gimmicks, pool, g + ": ключ пулу «" + pool + "» — пулів такого ґіміка немає");
                Assert.AreEqual(EntranceScreenSlots.GimmickKey(EntranceProfile.Family(g)), pool, g + ": пул не свого сімейства виходу");
                string key = EntranceScreenSlots.GimmickTextKey(g);
                Assert.AreEqual(content.GimmickOf(g).Key, key, g + ": ключ тексту огляду не той, що в контенті");
                Assert.IsTrue(t.Has("gimmick." + key), g + ": немає назви gimmick." + key);
                Assert.IsTrue(t.Has("gimmick." + key + ".fem"), g + ": немає жіночої назви gimmick." + key + ".fem");
            }
            Assert.AreEqual("monster", EntranceScreenSlots.GimmickKey(Game.Core.Ring.Career.Gimmick.Strongman));
            Assert.AreEqual("technician", EntranceScreenSlots.GimmickKey(Game.Core.Ring.Career.Gimmick.Veteran));

            // героїня-Силачка проти суперника-Силача на одному шоу
            HeroForms.Apply(t, true, false);
            string strong = content.GimmickOf(Game.Core.Ring.Career.Gimmick.Strongman).Key;
            string heroine = EntranceScreenSlots.Tag(t, strong, RingPath.Face, HeroForms.ShowsHeroine(t));
            string rival = EntranceScreenSlots.Tag(t, strong, RingPath.Heel, false);
            Assert.AreEqual(t.Get("gimmick.strongman.fem") + " · " + t.Get("path.face.fem"), heroine);
            Assert.AreEqual("Силачка · " + t.Get("path.face.fem"), heroine, "героїню не оголошують «Силач · Герой»");
            Assert.AreEqual(t.Get("gimmick.strongman") + " · " + t.Get("path.heel"), rival, "суперник — у своєму, чоловічому роді");
            StringAssert.StartsWith("Силач · ", rival);
            Assert.AreNotEqual(heroine.Split('·')[0], rival.Split('·')[0]);

            // гра бере той самий тег і рід борця: герой — за формами героїні, суперник — за NpcDef.Female; огляд — без масиву слів
            string boot = File.ReadAllText(Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Ring", "RingGameBootstrap.cs"));
            StringAssert.Contains("EntranceScreenSlots.Tag(_text, _content.GimmickOf(input.Gimmick).Key, input.Path, female)", boot);
            StringAssert.Contains("player ? HeroForms.ShowsHeroine(_text) : npc != null ? npc.Female : female", boot);
            string demo = File.ReadAllText(Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Ring", "Stage", "Unity", "RingStageDemo.cs"));
            StringAssert.Contains("EntranceScreenSlots.Tag(text, EntranceScreenSlots.GimmickTextKey(c.Gimmick)", demo);
            StringAssert.DoesNotContain("UaGimmick", demo, "огляд виходів знову тримає слова ґіміків масивом — на ґіміках 5–8 він падав");
        }
    }
}
