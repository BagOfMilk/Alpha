using System;
using System.Collections.Generic;
using System.Text;
using Game.Core.Ring;
using Game.Gameplay.Ring.Models;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Екран виходу борця (ТЗ <c>docs/ring/design/briefs/B07_entrance_screen.md</c>, Статут вигляду FX-09): чисті дані й
    /// формули запеченого ролика — розбір пулу (<c>Assets/Art/Ring/Titantron/pools/*.json</c>), стан шару на біті,
    /// вибір варіанта, слоти кольорів (<c>docs/ring/design/titantron/kits.json</c>) і тексти імені. Формули — дослівно
    /// за еталоном директора (<c>TitantronAssetsGuardTests</c>, Python <c>tools/ring_design/titantron</c>); паритет
    /// з Python на <c>golden.json</c> тримає <c>EntranceScreenTests</c>. Без UnityEngine: показ — <c>EntranceScreenPlayer</c>.
    /// </summary>
    public static class EntranceScreenMath
    {
        /// <summary>Плавність відрізка, що приходить до ключа (B07 «Доріжки»).</summary>
        public static double Ease(string e, double t)
        {
            switch (e)
            {
                case null:
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

        /// <summary>Ступінчастий показ щабля: біт, який видно в мить <paramref name="sec"/> (gym 6, hall 12, arena 30 кадр/с).</summary>
        public static double ShownBeat(double sec, double bpm, double fps)
        {
            if (sec < 0) sec = 0;
            return Math.Floor(sec * fps + 1e-6) / fps * bpm / 60.0;
        }

        /// <summary>FNV-1a 32 біти від UTF-8 рядка (еталон — <c>golden.json → variantFor</c>).</summary>
        public static uint Fnv1a(string s)
        {
            uint h = 0x811C9DC5;
            foreach (byte by in Encoding.UTF8.GetBytes(s ?? "")) { h ^= by; unchecked { h *= 0x01000193; } }
            return h;
        }

        /// <summary>Варіант пулу: FNV-1a(«ім'я|seed») mod variants.</summary>
        public static int VariantFor(string name, int seed, int variants)
        {
            if (variants <= 0) return 0;
            return (int)(Fnv1a((name ?? "") + "|" + seed) % (uint)variants);
        }

        /// <summary>Мить удару, с: <c>hitBeat · 60 / bpm</c>.</summary>
        public static double HitTimeSec(double bpm, double hitBeat) { return bpm > 0 ? hitBeat * 60.0 / bpm : 0; }

        /// <summary>Затримка кадру на завісі (B07 п. 2): <c>max(2,2 с, HitTimeSec + 1,3 с)</c>.</summary>
        public static double CurtainHoldSec(double hitTimeSec) { return Math.Max(2.2, hitTimeSec + 1.3); }

        /// <summary>Ім'я файла пулу: <c>{gimmick}_{role}_{tier}</c> (без розширення).</summary>
        public static string PoolName(string gimmick, string role, string tier) { return gimmick + "_" + role + "_" + tier; }
    }

    /// <summary>Ключ доріжки: біт, значення, плавність відрізка, що приходить.</summary>
    public struct TronKey
    {
        public double B, V;
        public string E;
    }

    /// <summary>Стан шару на біті (одиниця — висота кадру; x вправо, y вниз, початок — центр; rot — градуси за годинниковою).</summary>
    public struct TronState
    {
        public double X, Y, S, Sx, Sy, Rot, A, Clip, W, H, Ox, Oy;
    }

    /// <summary>Текст шару <c>k = text</c>.</summary>
    public sealed class TronText
    {
        public string Slot, Font = "display900", Fill = "$paper", ShadowTint, OutlineTint;
        public double Size, Fit, ShadowDx, ShadowDy, OutlineT, Slant, Tracking;
        public bool Hand;
    }

    /// <summary>Подія ролика: <c>hit</c> (удар) або <c>shake</c> (глобальне тремтіння шарів без <c>fixed</c>).</summary>
    public sealed class TronEvent
    {
        public double B, Amp, Dur;
        public string T;
    }

    /// <summary>Шар ролика (B07 «Шари»): статика, доріжки й модифікатори.</summary>
    public sealed class TronLayer
    {
        // індекси доріжок
        public const int PX = 0, PY = 1, PS = 2, PSx = 3, PSy = 4, PRot = 5, PA = 6, PClip = 7, PW = 8, PH = 9, PCount = 10;
        private static readonly string[] PropNames = { "x", "y", "s", "sx", "sy", "rot", "a", "clip", "w", "h" };

        public string Id, Kind, Tint, Sprite;
        public double X, Y, S = 1, Sx = 1, Sy = 1, Rot, A = 1, Clip = 1, W = 1, H = 1, Slant = 12, T = 0.03, Tile;
        public bool Fixed, FlipX;
        public TronText Text;
        public readonly TronKey[][] Tracks = new TronKey[PCount][];
        public double[] Pulse, Spin, Sway, Rock, Drift, Blink, Flick, Sweep;

        /// <summary>Шар-спалах (рівно один на ролик; у «Менше руху» не малюється).</summary>
        public bool IsFlash { get { return Id != null && Id.StartsWith("flash", StringComparison.Ordinal); } }

        /// <summary>Чи має шар анімацію (доріжки чи модифікатори) — статичні шари програвач не оновлює щокадру.</summary>
        public bool Animated
        {
            get
            {
                for (int i = 0; i < PCount; i++) if (Tracks[i] != null) return true;
                return Pulse != null || Spin != null || Sway != null || Rock != null || Drift != null || Blink != null || Flick != null || Sweep != null;
            }
        }

        /// <summary>
        /// Стан на біті <paramref name="b"/> — дослівно за еталоном (<c>TitantronAssetsGuardTests.State</c>): доріжки, далі
        /// модифікатори в порядку pulse → spin → sway → rock → drift → blink → flick → sweep. <paramref name="reduceMotion"/>
        /// («Менше руху», B07 «Безпека»): spin, rock, blink, flick і sweep ігноруються, pulse — не більше 3 %.
        /// </summary>
        public void Evaluate(double b, bool reduceMotion, ref TronState st)
        {
            st.X = Val(PX, X, b); st.Y = Val(PY, Y, b); st.S = Val(PS, S, b); st.Sx = Val(PSx, Sx, b); st.Sy = Val(PSy, Sy, b);
            st.Rot = Val(PRot, Rot, b); st.A = Val(PA, A, b); st.Clip = Val(PClip, Clip, b); st.W = Val(PW, W, b); st.H = Val(PH, H, b);
            st.Ox = 0; st.Oy = 0;
            if (Pulse != null)
            {
                double every = Pulse[0], amp = Pulse[1], decay = Pulse[2], start = Pulse[3];
                if (reduceMotion && amp > 0.03) amp = 0.03;
                if (b >= start) st.S *= 1 + amp * Math.Exp(-((b - start) % every) / decay);
            }
            if (Spin != null && !reduceMotion && b > Spin[1]) st.Rot += Spin[0] * (b - Spin[1]);
            if (Sway != null)
            {
                double k = Math.Sin(2 * Math.PI * (b / Sway[2] + Sway[3]));
                st.X += Sway[0] * k; st.Y += Sway[1] * k;
            }
            if (Rock != null && !reduceMotion) st.Rot += Rock[0] * Math.Sin(2 * Math.PI * (b / Rock[1] + Rock[2]));
            if (Drift != null && b > Drift[2]) { st.Ox += Drift[0] * (b - Drift[2]); st.Oy += Drift[1] * (b - Drift[2]); }
            if (Blink != null && !reduceMotion && b >= Blink[1] && ((int)Math.Floor(b)) % 2 != (int)Blink[0]) st.A = 0;
            if (Flick != null && !reduceMotion && (b < Flick[2] || ((b - Flick[2]) % Flick[0]) >= Flick[1])) st.A = 0;
            if (Sweep != null && !reduceMotion)
            {
                double per = Sweep[0], start = Sweep[1];
                if (b < start) st.A = 0;
                else st.X = Sweep[2] + (Sweep[3] - Sweep[2]) * (((b - start) % per) / per);
            }
        }

        private double Val(int p, double stat, double b)
        {
            var keys = Tracks[p];
            return keys == null ? stat : Track(keys, b);
        }

        /// <summary>Значення доріжки (еталон — <c>TitantronAssetsGuardTests.Track</c>).</summary>
        public static double Track(TronKey[] keys, double b)
        {
            if (b <= keys[0].B) return keys[0].V;
            for (int i = 1; i < keys.Length; i++)
            {
                TronKey p = keys[i - 1], q = keys[i];
                if (b < q.B)
                {
                    double t = q.B > p.B ? (b - p.B) / (q.B - p.B) : 1.0;
                    return p.V + (q.V - p.V) * EntranceScreenMath.Ease(q.E, t);
                }
            }
            return keys[keys.Length - 1].V;
        }

        internal static TronLayer FromJson(Dictionary<string, object> o)
        {
            var l = new TronLayer();
            l.Id = Str(o, "id"); l.Kind = Str(o, "k"); l.Tint = Str(o, "tint"); l.Sprite = Str(o, "sprite");
            l.X = Num(o, "x", 0); l.Y = Num(o, "y", 0); l.S = Num(o, "s", 1); l.Sx = Num(o, "sx", 1); l.Sy = Num(o, "sy", 1);
            l.Rot = Num(o, "rot", 0); l.A = Num(o, "a", 1); l.Clip = Num(o, "clip", 1); l.W = Num(o, "w", 1); l.H = Num(o, "h", 1);
            l.Slant = Num(o, "slant", 12); l.T = Num(o, "t", 0.03); l.Tile = Num(o, "tile", 0);
            l.Fixed = Bool(o, "fixed"); l.FlipX = Bool(o, "flipx");
            object v;
            if (o.TryGetValue("tr", out v) && v is Dictionary<string, object>)
            {
                foreach (var kv in (Dictionary<string, object>)v)
                {
                    int p = Array.IndexOf(PropNames, kv.Key);
                    if (p < 0) throw new FormatException("доріжка " + kv.Key + " у шарі " + l.Id);
                    var list = (List<object>)kv.Value;
                    var keys = new TronKey[list.Count];
                    for (int i = 0; i < list.Count; i++)
                    {
                        var k = (List<object>)list[i];
                        keys[i] = new TronKey { B = (double)k[0], V = (double)k[1], E = k.Count > 2 ? (string)k[2] : "l" };
                    }
                    if (keys.Length > 0) l.Tracks[p] = keys;
                }
            }
            if (o.TryGetValue("mod", out v) && v is Dictionary<string, object>)
            {
                var m = (Dictionary<string, object>)v;
                l.Pulse = Nums(m, "pulse"); l.Spin = Nums(m, "spin"); l.Sway = Nums(m, "sway"); l.Rock = Nums(m, "rock");
                l.Drift = Nums(m, "drift"); l.Blink = Nums(m, "blink"); l.Flick = Nums(m, "flick"); l.Sweep = Nums(m, "sweep");
            }
            if (o.TryGetValue("text", out v) && v is Dictionary<string, object>)
            {
                var t = (Dictionary<string, object>)v;
                var tx = new TronText
                {
                    Slot = Str(t, "slot"), Font = Str(t, "font") ?? "display900", Fill = Str(t, "fill") ?? "$paper",
                    Size = Num(t, "size", 0.1), Fit = Num(t, "fit", 0), Slant = Num(t, "slant", 0), Tracking = Num(t, "tracking", 0),
                    Hand = Bool(t, "hand"),
                };
                if (t.TryGetValue("shadow", out v) && v is List<object> shl && shl.Count >= 3)
                {
                    tx.ShadowTint = (string)shl[0]; tx.ShadowDx = (double)shl[1]; tx.ShadowDy = (double)shl[2];
                }
                if (t.TryGetValue("outline", out v) && v is List<object> ol && ol.Count >= 2)
                {
                    tx.OutlineTint = (string)ol[0]; tx.OutlineT = (double)ol[1];
                }
                l.Text = tx;
            }
            return l;
        }

        private static string Str(Dictionary<string, object> o, string k) { object v; return o.TryGetValue(k, out v) ? v as string : null; }
        private static double Num(Dictionary<string, object> o, string k, double d) { object v; return o.TryGetValue(k, out v) && v is double ? (double)v : d; }
        private static bool Bool(Dictionary<string, object> o, string k) { object v; return o.TryGetValue(k, out v) && v is bool && (bool)v; }

        private static double[] Nums(Dictionary<string, object> o, string k, bool mixed = false)
        {
            object v;
            if (!o.TryGetValue(k, out v) || !(v is List<object>)) return null;
            var list = (List<object>)v;
            var r = new double[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is double) r[i] = (double)list[i];
                else if (!mixed) throw new FormatException("модифікатор " + k);
            }
            return r;
        }
    }

    /// <summary>Один ролик (варіант пулу): темп, кадри/с щабля, біт удару, вступ, шари (знизу вгору) і події.</summary>
    public sealed class TronTimeline
    {
        public string Gimmick, Role, Tier;
        public int Variant;
        public double Bpm, Fps, HitBeat, IntroBeats;
        public readonly List<TronLayer> Layers = new List<TronLayer>();
        public readonly List<TronEvent> Events = new List<TronEvent>();

        /// <summary>Мить удару від початку ролика, с.</summary>
        public double HitTimeSec { get { return EntranceScreenMath.HitTimeSec(Bpm, HitBeat); } }

        /// <summary>Глобальний зсув тремтіння (події <c>shake</c>; формула — B07 «Події»). У «Менше руху» — нуль.</summary>
        public void Shake(double b, bool reduceMotion, out double dx, out double dy)
        {
            dx = 0; dy = 0;
            if (reduceMotion) return;
            foreach (var e in Events)
            {
                if (e.T != "shake" || b < e.B || b >= e.B + e.Dur || e.Dur <= 0) continue;
                double k = e.Amp * (1 - (b - e.B) / e.Dur);
                dx += k * Math.Sin(83.1 * b) * Math.Cos(41.7 * b);
                dy += k * Math.Cos(67.3 * b) * Math.Sin(29.9 * b);
            }
        }

        internal static TronTimeline FromJson(Dictionary<string, object> o)
        {
            var t = new TronTimeline
            {
                Gimmick = o["gimmick"] as string, Role = o["role"] as string, Tier = o["tier"] as string,
                Variant = (int)(double)o["variant"], Bpm = (double)o["bpm"], Fps = (double)o["fps"],
                HitBeat = (double)o["hitBeat"], IntroBeats = (double)o["introBeats"],
            };
            foreach (var l in (List<object>)o["layers"]) t.Layers.Add(TronLayer.FromJson((Dictionary<string, object>)l));
            object ev;
            if (o.TryGetValue("events", out ev) && ev is List<object>)
                foreach (Dictionary<string, object> e in (List<object>)ev)
                {
                    object a, d;
                    t.Events.Add(new TronEvent
                    {
                        B = (double)e["b"], T = e["t"] as string,
                        Amp = e.TryGetValue("amp", out a) ? (double)a : 0, Dur = e.TryGetValue("dur", out d) ? (double)d : 0,
                    });
                }
            return t;
        }
    }

    /// <summary>Пул <c>{gimmick}_{role}_{tier}.json</c>: варіанти одного архетипу.</summary>
    public sealed class TronPool
    {
        public string Gimmick, Role, Tier;
        public readonly List<TronTimeline> Variants = new List<TronTimeline>();

        public static TronPool Parse(string json)
        {
            var o = MiniJson.ParseObject(json);
            var p = new TronPool { Gimmick = o["gimmick"] as string, Role = o["role"] as string, Tier = o["tier"] as string };
            foreach (var v in (List<object>)o["variants"]) p.Variants.Add(TronTimeline.FromJson((Dictionary<string, object>)v));
            return p;
        }
    }

    /// <summary>Щабель арени (= виробництво екрана): кадри/с показу, біт удару, бюджет шарів, моно-палітра, рукописний шрифт.</summary>
    public struct TronTier
    {
        public string Key;
        public int Fps, HitBeat, MaxLayers;
        public bool Mono, HandFont, Scan;
    }

    /// <summary>
    /// Слоти кольорів і довідники kits.json (ключі B07 «Рецепт → ролик», п. 3). Таблиці — дзеркало
    /// <c>docs/ring/design/titantron/kits.json</c>; збіг тримає <c>EntranceScreenTests.Slots_MatchKitsJson</c>.
    /// Порядок: <c>slots[role]</c> → для гравця поверх <c>playerSlots</c> → для моно-щабля поверх <c>monoSlots</c>.
    /// Золото — лише <c>$accent</c> і <c>$flash</c> гравця (Статут вигляду COL-02, FX-09).
    /// </summary>
    public static class EntranceScreenSlots
    {
        public static readonly string[] Roles = { "hero", "villain", "rebel" };
        public static readonly string[] Tiers = { "gym", "hall", "arena" };
        public static readonly string[] Gimmicks = { "monster", "flyer", "technician", "showman" };
        public static readonly string[] SlotNames = { "$role", "$roleText", "$flash", "$shadow", "$onRole", "$accent", "$cool", "$ground", "$ground2", "$paper" };

        // рядок = слот (порядок SlotNames), стовпці = hero, villain, rebel
        private static readonly string[,] RoleSlots =
        {
            { "cobalt", "crimson", "rose" },
            { "cobaltText", "signal", "rose" },
            { "cobaltText", "signal", "rose" },
            { "ink0", "ink0", "paper" },
            { "paper", "paper", "ink0" },
            { "paper", "paper", "paper" },
            { "frost", "frost", "frost" },
            { "ink0", "ink0", "ink0" },
            { "ink1", "ink1", "ink1" },
            { "paper", "paper", "paper" },
        };

        public static readonly KeyValuePair<string, string>[] PlayerSlots =
        {
            new KeyValuePair<string, string>("$accent", "gold"),
            new KeyValuePair<string, string>("$flash", "gold"),
        };

        public static readonly KeyValuePair<string, string>[] MonoSlots =
        {
            new KeyValuePair<string, string>("$roleText", "paper"),
            new KeyValuePair<string, string>("$cool", "paper"),
            new KeyValuePair<string, string>("$flash", "paper"),
            new KeyValuePair<string, string>("$ground2", "ink0"),
        };

        public static TronTier Tier(string key)
        {
            switch (key)
            {
                case "hall": return new TronTier { Key = "hall", Fps = 12, HitBeat = 2, MaxLayers = 18 };
                case "arena": return new TronTier { Key = "arena", Fps = 30, HitBeat = 4, MaxLayers = 30 };
                default: return new TronTier { Key = "gym", Fps = 6, HitBeat = 2, MaxLayers = 12, Mono = true, HandFont = true, Scan = true };
            }
        }

        /// <summary>Темп ролика за ґіміком (kits.json → gimmicks.bpm; відкрите питання B07 №4 — трек звуку).</summary>
        public static int Bpm(string gimmick)
        {
            switch (gimmick)
            {
                case "monster": return 100;
                case "flyer": return 132;
                case "technician": return 112;
                default: return 120;
            }
        }

        /// <summary>Ім'я токена для слота (<c>$role</c> → «cobalt») або сам токен, якщо тінт — не слот.</summary>
        public static string Token(string tint, string role, bool player, bool mono)
        {
            if (string.IsNullOrEmpty(tint)) return null;
            if (tint[0] != '$') return tint;
            if (mono) foreach (var kv in MonoSlots) if (kv.Key == tint) return kv.Value;
            if (player) foreach (var kv in PlayerSlots) if (kv.Key == tint) return kv.Value;
            int r = Array.IndexOf(Roles, role); if (r < 0) r = 0;
            int s = Array.IndexOf(SlotNames, tint);
            if (s < 0) throw new ArgumentException("невідомий слот " + tint);
            return RoleSlots[s, r];
        }

        /// <summary>RGB токена (<see cref="BrandTokens"/>); для невідомого — <c>false</c>.</summary>
        public static bool TryRgb(string token, out uint rgb)
        {
            switch (token)
            {
                case "ink0": rgb = BrandTokens.Ink0; return true;
                case "ink1": rgb = BrandTokens.Ink1; return true;
                case "ink2": rgb = BrandTokens.Ink2; return true;
                case "ink3": rgb = BrandTokens.Ink3; return true;
                case "paper": rgb = BrandTokens.Paper; return true;
                case "dim": rgb = BrandTokens.Dim; return true;
                case "ash": rgb = BrandTokens.Ash; return true;
                case "gold": rgb = BrandTokens.Gold; return true;
                case "crimson": rgb = BrandTokens.Crimson; return true;
                case "signal": rgb = BrandTokens.Signal; return true;
                case "cobalt": rgb = BrandTokens.Cobalt; return true;
                case "cobaltText": rgb = BrandTokens.CobaltText; return true;
                case "rose": rgb = BrandTokens.Rose; return true;
                case "frost": rgb = BrandTokens.Frost; return true;
                case "slate": rgb = BrandTokens.Slate; return true;
                case "black": rgb = BrandTokens.Black; return true;
                default: rgb = 0; return false;
            }
        }

        /// <summary>Ключ ролі екрана з шляху борця: Face → hero, Heel → villain, Rebel → rebel.</summary>
        public static string RoleOf(RingPath path)
        {
            switch (path)
            {
                case RingPath.Heel: return "villain";
                case RingPath.Rebel: return "rebel";
                default: return "hero";
            }
        }

        /// <summary>
        /// Ключ пулу екрана для ґіміка — за сімейством виходу (<see cref="EntranceProfile.Family"/>): пули є лише в чотирьох
        /// (<see cref="Gimmicks"/>, 36 = 4 × 3 × 3), нових рядкових ключів без арт-директора не додаємо (kits.json і ще 36 пулів).
        /// Бувалий — «technician», Хардкорник і Силач — «monster», Самородок — «showman». Назва ґіміка в тезі — не звідси, а своїм
        /// ключем тексту (<see cref="Tag"/>): Силача оголошують Силачем, лише ролик у нього монстра. Охоронець —
        /// <c>EntranceScreenTests.EveryGimmick_LeadsToAPoolKey_AndOwnTagName</c>.
        /// </summary>
        public static string GimmickKey(Game.Core.Ring.Career.Gimmick g)
        {
            switch (EntranceProfile.Family(g))
            {
                case Game.Core.Ring.Career.Gimmick.Monster: return "monster";
                case Game.Core.Ring.Career.Gimmick.Flyer: return "flyer";
                case Game.Core.Ring.Career.Gimmick.Technician: return "technician";
                case Game.Core.Ring.Career.Gimmick.Showman:
                default: return "showman";
            }
        }

        /// <summary>
        /// Ключ тексту ґіміка («gimmick.&lt;ключ&gt;»): id у <c>career_gimmicks.json</c> — ім'я значення малими літерами
        /// («strongman»). Гра бере його з контенту (<c>GimmickDef.Key</c>); цей — для оглядів без кар'єри (RingStageDemo). Збіг із
        /// контентом на кожному значенні тримає охоронець.
        /// </summary>
        public static string GimmickTextKey(Game.Core.Ring.Career.Gimmick g) { return g.ToString().ToLowerInvariant(); }

        /// <summary>
        /// Тег екрана виходу «ґімік · шлях» у роді борця (рев'ю ПМ 13.11.2026: героїню оголошували «Силач · Герой»): «Силачка ·
        /// Героїня», «Силач · Лиходій». Ключі «.fem» (<see cref="RingLabels.GimmickName"/>, <see cref="RingLabels.PathName(RingText, RingPath, bool)"/>), не «.f»:
        /// на одному шоу виходять обидва борці, і форми героїні (<see cref="HeroForms"/>) не мають підміняти тег суперника.
        /// Стать: герой — <see cref="HeroForms.ShowsHeroine"/>, суперник — <c>NpcDef.Female</c>.
        /// </summary>
        public static string Tag(RingText t, string gimmickTextKey, RingPath path, bool female)
        {
            if (t == null) return "";
            return RingLabels.GimmickName(t, gimmickTextKey, female) + " · " + RingLabels.PathName(t, path, female);
        }
    }

    /// <summary>
    /// Рецепт ролика (B07 «Рецепт → ролик»): ґімік, роль, щабель, повне ім'я, seed, гравець і готовий текст тегу
    /// («{gimmick.*} · {path.*}» з ключів перекладу). Тексти слотів — <see cref="Nick"/>, <see cref="Full"/>, <see cref="Tag"/>.
    /// </summary>
    public sealed class EntranceRecipe
    {
        public string Gimmick = "showman", Role = "hero", Tier = "gym", Name = "", TagText = "";
        public int Seed;
        public bool Player;

        public string PoolName { get { return EntranceScreenMath.PoolName(Gimmick, Role, Tier); } }

        /// <summary>Прізвисько — те, що між «», або повне ім'я, якщо прізвиська немає.</summary>
        public string Nick
        {
            get
            {
                string n = (Name ?? "").Trim();
                int a = n.IndexOf('«'), b = a >= 0 ? n.IndexOf('»', a + 1) : -1;
                return a >= 0 && b > a ? n.Substring(a + 1, b - a - 1).Trim() : n;
            }
        }

        /// <summary>Ім'я без прізвиська («Тарас «Молот» Гребінь» → «Тарас Гребінь»); без прізвиська — порожнє (шар не малюється).</summary>
        public string Full
        {
            get
            {
                string n = (Name ?? "").Trim();
                int a = n.IndexOf('«'), b = a >= 0 ? n.IndexOf('»', a + 1) : -1;
                if (a < 0 || b <= a) return "";
                string s = (n.Substring(0, a) + " " + n.Substring(b + 1)).Trim();
                while (s.Contains("  ")) s = s.Replace("  ", " ");
                return s;
            }
        }

        public string Tag { get { return TagText ?? ""; } }

        /// <summary>Текст слота (<c>nick · full · tag</c>) великими літерами (<c>RingTheme.Caps</c>: Sofia — лише ВЕЛИКІ).</summary>
        public string SlotText(string slot)
        {
            string s = slot == "nick" ? Nick : slot == "full" ? Full : slot == "tag" ? Tag : "";
            return string.IsNullOrEmpty(s) ? "" : s.ToUpperInvariant();
        }

        public int VariantOf(int variants) { return EntranceScreenMath.VariantFor(Name, Seed, variants); }
    }
}
