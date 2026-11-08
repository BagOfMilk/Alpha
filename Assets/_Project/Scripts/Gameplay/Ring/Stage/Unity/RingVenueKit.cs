using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Матеріали <c>Kit_*</c> арени в Unity. У Blender (tools/blender/ring_venues.py, <c>build_textured</c>) колір такої поверхні —
    /// яскравість фото набору × колір вершин <c>tint</c> × 4, шорсткість — карта × альфа <c>tint</c> × 1,7. URP Lit кольорів вершин
    /// не читає, а фото-наборів Poly Haven у репозиторії немає, тож кожна така поверхня була світло-сірою 0,91 без текстури:
    /// під прожектором — білою (плейтест 08.11.2026, P3: білий «!» біля рингу в промо — шафа за канатом), роздягальня — пласка.
    /// Тут сітка ділиться за кольором вершин на підсітки; кожна отримує свій матеріал: колір = tint × 4 × середня яскравість
    /// фото-набору (так у Blender виходить колір матеріалу), сіра карта процедурного набору тієї ж назви з репозиторію
    /// (<c>Assets/Art/Ring/Textures</c>) у масштабі плитки, його нормаль і гладкість з альфи. Кольори — з даних арени, не нові.
    /// Потрібне читання сіток арени (isReadable — вмикає збирач сцени). Не лінтується (сітки, текстури рушія).
    /// </summary>
    public static class RingVenueKit
    {
        /// <summary>Набір Kit → (процедурний набір у репозиторії, плитка фото-набору, м; середня яскравість фото-набору).</summary>
        private static readonly Dictionary<string, KitSet> Sets = new Dictionary<string, KitSet>(StringComparer.Ordinal)
        {
            // плитка й середній колір — із _cc0(...) у ring_venues.py; яскравість — lum(avg)
            { "concrete_block_wall", new KitSet("block_wall", 2.0f, Lum(0.27f, 0.24f, 0.2f)) },
            { "concrete_wall_006", new KitSet("concrete", 2.0f, Lum(0.36f, 0.32f, 0.27f)) },
            { "plastered_wall_04", new KitSet("plaster", 3.2f, Lum(0.55f, 0.54f, 0.53f)) },
            { "velour_velvet", new KitSet("cloth", 0.6f, Lum(0.68f, 0.18f, 0.28f)) },
            { "fabric_leather_02", new KitSet("vinyl", 0.5f, Lum(0.51f, 0.3f, 0.1f)) },
            { "fabric_pattern_07", new KitSet("canvas", 0.4f, 1f) },
            { "rubber_tiles", new KitSet("vinyl", 2.0f, Lum(0.13f, 0.13f, 0.14f)) },
            { "dirty_carpet", new KitSet("carpet", 0.6f, Lum(0.21f, 0.18f, 0.14f)) },
            { "metal_plate_02", new KitSet("metal_brushed", 2.0f, Lum(0.33f, 0.28f, 0.24f)) },
            { "painted_metal_shutter", new KitSet("metal_painted", 2.0f, Lum(0.5f, 0.51f, 0.52f)) },
            { "worn_tile_floor", new KitSet("tile_floor", 2.0f, Lum(0.29f, 0.27f, 0.23f)) },
            // процедурні набори самі (у Blender їхня яскравість — 0,9)
            { "plastic", new KitSet("plastic", 0.5f, 0.9f) },
            { "sport_floor", new KitSet("sport_floor", 3.0f, 0.9f) },
            { "wood_board", new KitSet("wood_board", 2.0f, 0.9f) },
        };

        /// <summary>Металевість за набором (KIT_METAL у ring_venues.py).</summary>
        private static readonly Dictionary<string, float> Metal = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            { "metal_plate_02", 0.9f }, { "painted_metal_shutter", 0.2f },
        };

        private const float FactorScale = 4f;       // FACTOR_SCALE: tint зберігається поділеним на 4
        private const float DetailMean = 0.75f;     // середнє сірої карти (лінійне): колір матеріалу ділиться на нього

        private struct KitSet
        {
            public readonly string Proc;
            public readonly float Tile, Lum;
            public KitSet(string proc, float tile, float lum) { Proc = proc; Tile = tile; Lum = lum; }
        }

        private static float Lum(float r, float g, float b) { return 0.2126f * r + 0.7152f * g + 0.0722f * b; }

        /// <summary>
        /// Перефарбувати арену. <paramref name="procSets"/>/<paramref name="albedo"/>/<paramref name="normal"/>/<paramref name="procTile"/>
        /// — процедурні набори з посилань сцени (щоб потрапили в білд). Повертає кількість перефарбованих підсіток.
        /// </summary>
        public static int Apply(GameObject venue, Material template, string[] procSets, Texture2D[] albedo, Texture2D[] normal, float[] procTile)
        {
            if (venue == null) return 0;
            var detail = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
            var cache = new Dictionary<string, Material>(StringComparer.Ordinal);
            int parts = 0, meshes = 0, unreadable = 0;
            foreach (var mf in venue.GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                var mesh = mf.sharedMesh;
                if (mr == null || mesh == null) continue;
                var mats = mr.sharedMaterials;
                bool any = false;
                for (int s = 0; s < mats.Length && !any; s++) any = KitOf(mats[s]) != null;
                if (!any) continue;
                if (!mesh.isReadable) { unreadable++; continue; }
                var colors = mesh.colors;
                if (colors == null || colors.Length != mesh.vertexCount) continue;

                var subs = new List<int[]>();
                var outMats = new List<Material>();
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var tris = mesh.GetTriangles(s);
                    var src = s < mats.Length ? mats[s] : null;
                    string kit = KitOf(src);
                    if (kit == null) { subs.Add(tris); outMats.Add(src); continue; }
                    // Колір вершин однаковий у межах речі: трикутники групуються за кольором першої вершини (крок 1/128).
                    var groups = new Dictionary<int, List<int>>();
                    var keyColor = new Dictionary<int, Color>();
                    for (int t = 0; t + 2 < tris.Length; t += 3)
                    {
                        Color c = colors[tris[t]];
                        int key = Q(c.r) | (Q(c.g) << 8) | (Q(c.b) << 16) | (Q(c.a) << 24);
                        List<int> g;
                        if (!groups.TryGetValue(key, out g)) { g = new List<int>(); groups[key] = g; keyColor[key] = c; }
                        g.Add(tris[t]); g.Add(tris[t + 1]); g.Add(tris[t + 2]);
                    }
                    foreach (var kv in groups)
                    {
                        subs.Add(kv.Value.ToArray());
                        outMats.Add(KitMaterial(kit, keyColor[kv.Key], src, template, procSets, albedo, normal, procTile, detail, cache));
                        parts++;
                    }
                }
                var m2 = UnityEngine.Object.Instantiate(mesh);
                m2.name = mesh.name + "_kit";
                m2.subMeshCount = subs.Count;
                for (int i = 0; i < subs.Count; i++) m2.SetTriangles(subs[i], i, false);
                mf.sharedMesh = m2;
                mr.sharedMaterials = outMats.ToArray();
                meshes++;
            }
            Debug.Log("[Ринг] 3D: матеріали Kit — сіток " + meshes + ", підсіток " + parts + ", матеріалів " + cache.Count
                + (unreadable > 0 ? ", НЕ читаються " + unreadable + " (isReadable арени вимкнено — лишились сірими)" : ""));
            return parts;
        }

        /// <summary>
        /// Прибрати підсітку з матеріалом <paramref name="material"/> (сітка має читатися). Мікрофон промо — бездротовий: кабель
        /// був частиною сітки і ходив за кистю прямим жорстким стрижнем до підлоги (плейтест 08.11.2026, P3).
        /// </summary>
        public static int DropSubmesh(GameObject root, string material)
        {
            int n = 0;
            if (root == null) return 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                var mesh = mf.sharedMesh;
                if (mr == null || mesh == null) continue;
                var mats = mr.sharedMaterials;
                int idx = -1;
                for (int s = 0; s < mats.Length && s < mesh.subMeshCount; s++)
                    if (mats[s] != null && mats[s].name.IndexOf(material, StringComparison.Ordinal) >= 0) idx = s;
                if (idx < 0) continue;
                if (!mesh.isReadable) { Debug.LogWarning("[Ринг] 3D: " + root.name + " не читається — " + material + " лишився"); continue; }
                var subs = new List<int[]>();
                var keep = new List<Material>();
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    if (s == idx) continue;
                    subs.Add(mesh.GetTriangles(s));
                    keep.Add(s < mats.Length ? mats[s] : null);
                }
                var m2 = UnityEngine.Object.Instantiate(mesh);
                m2.name = mesh.name + "_no_" + material;
                m2.subMeshCount = subs.Count;
                for (int i = 0; i < subs.Count; i++) m2.SetTriangles(subs[i], i, false);
                mf.sharedMesh = m2;
                mr.sharedMaterials = keep.ToArray();
                n++;
            }
            // Тиха нездача ховала ваду (рецензія 08.11.2026: шукали «Cable», а матеріал — «Mic_Cable»).
            Debug.Log("[Ринг] 3D: " + root.name + " — прибрано підсіток «" + material + "»: " + n);
            return n;
        }

        /// <summary>
        /// Приглушити емісію матеріалу <paramref name="material"/> (усі сітки арени) у <paramref name="mul"/> разів — її ж колір.
        /// Нутро холодильника з напоями (ring_gym.py, «Fridge_Inner», емісія 2,0) у темній залі Unity малювалось суцільною білою
        /// смугою 0,5 × 1,6 м — найяскравішим у кадрі; перерізана канатом, вона читалась як білий «!» за рингом (плейтест 08.11.2026,
        /// P3; знайдено зондом -ring-probe).
        /// </summary>
        public static int TameEmission(GameObject venue, string material, float mul)
        {
            if (venue == null) return 0;
            var done = new HashSet<Material>();
            foreach (var r in venue.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || done.Contains(m) || m.name.IndexOf(material, StringComparison.Ordinal) < 0) continue;
                    if (!m.HasProperty("_EmissionColor")) continue;
                    m.SetColor("_EmissionColor", m.GetColor("_EmissionColor") * mul);
                    done.Add(m);
                }
            Debug.Log("[Ринг] 3D: емісію «" + material + "» приглушено ×" + mul + " — матеріалів " + done.Count);
            return done.Count;
        }

        private static int Q(float v) { return Mathf.Clamp(Mathf.RoundToInt(v * 127f), 0, 255); }

        /// <summary>«Kit_painted_metal_shutter» / «Kit_painted_metal_shutter_m» (+ « (Instance)») → «painted_metal_shutter».</summary>
        private static string KitOf(Material m)
        {
            if (m == null) return null;
            string n = m.name;
            int sp = n.IndexOf(' ');
            if (sp > 0) n = n.Substring(0, sp);
            if (!n.StartsWith("Kit_", StringComparison.Ordinal)) return null;
            n = n.Substring(4);
            if (n.EndsWith("_m", StringComparison.Ordinal)) n = n.Substring(0, n.Length - 2);
            return n;
        }

        private static Material KitMaterial(string kit, Color tint, Material src, Material template, string[] procSets, Texture2D[] albedo,
            Texture2D[] normal, float[] procTile, Dictionary<string, Texture2D> detail, Dictionary<string, Material> cache)
        {
            string key = kit + ":" + Q(tint.r) + "," + Q(tint.g) + "," + Q(tint.b) + "," + Q(tint.a);
            Material m;
            if (cache.TryGetValue(key, out m)) return m;
            KitSet set;
            if (!Sets.TryGetValue(kit, out set)) set = new KitSet(kit, 1f, 0.9f);
            int pi = procSets != null ? Array.IndexOf(procSets, set.Proc) : -1;
            Texture2D gray = pi >= 0 && albedo != null && pi < albedo.Length ? Gray(set.Proc, albedo[pi], detail) : null;

            m = new Material(template != null ? template : src);
            m.name = "Kit_" + kit + "_tint";
            // Колір у лінійному просторі (як у Blender): tint × 4 × яскравість фото-набору = колір матеріалу генератора.
            float k = FactorScale * set.Lum / (gray != null ? DetailMean : 1f);
            Color lin = tint * k;
            lin.a = 1f;
            m.SetColor("_BaseColor", lin.gamma);
            m.SetTexture("_BaseMap", gray);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", gray);
            float scale = pi >= 0 && procTile != null && pi < procTile.Length && procTile[pi] > 0f ? set.Tile / procTile[pi] : 1f;
            m.SetTextureScale("_BaseMap", new Vector2(scale, scale));
            Texture2D nor = pi >= 0 && normal != null && pi < normal.Length ? normal[pi] : null;
            if (nor != null)
            {
                m.SetTexture("_BumpMap", nor);
                m.SetTextureScale("_BumpMap", new Vector2(scale, scale));
                m.EnableKeyword("_NORMALMAP");
            }
            else { m.SetTexture("_BumpMap", null); m.DisableKeyword("_NORMALMAP"); }
            // Шорсткість = карта (≈0,6) × альфа × 1,7; гладкість — доповнення.
            float rough = Mathf.Clamp01(0.6f * tint.a * 1.7f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", Mathf.Clamp(1f - rough, 0.05f, 0.6f));
            float metal;
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", Metal.TryGetValue(kit, out metal) ? metal : 0f);
            m.DisableKeyword("_EMISSION");
            cache[key] = m;
            return m;
        }

        /// <summary>
        /// Сіра карта набору: лише яскравість фото (як <c>ShaderNodeRGBToBW</c> у Blender), лінійна, середнє — <see cref="DetailMean"/>.
        /// Читання — через RenderTexture (налаштувань імпорту не міняємо).
        /// </summary>
        private static Texture2D Gray(string name, Texture2D src, Dictionary<string, Texture2D> cache)
        {
            Texture2D t;
            if (cache.TryGetValue(name, out t)) return t;
            t = null;
            if (src != null)
            {
                try
                {
                    // Лінійна ціль: карта (імпорт sRGB) при вибірці вже розкодована в лінійні — так і записується.
                    var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    Graphics.Blit(src, rt);
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    var read = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, true);
                    read.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
                    read.Apply(false, false);
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(rt);
                    var px = read.GetPixels32();
                    UnityEngine.Object.Destroy(read);
                    var lum = new float[px.Length];
                    double sum = 0;
                    for (int i = 0; i < px.Length; i++)
                    {
                        // Байти вже лінійні (див. вище): повторне GammaToLinear підносило яскравість до квадрата — контраст
                        // карти виходив надмірним (рецензія 08.11.2026).
                        float l = (px[i].r * 0.2126f + px[i].g * 0.7152f + px[i].b * 0.0722f) / 255f;
                        lum[i] = l;
                        sum += l;
                    }
                    float mean = (float)(sum / Math.Max(1, px.Length));
                    float mul = mean > 1e-4f ? DetailMean / mean : 1f;
                    var raw = new byte[px.Length * 4];
                    for (int i = 0; i < px.Length; i++)
                    {
                        byte v = (byte)Mathf.Clamp(Mathf.RoundToInt(lum[i] * mul * 255f), 0, 255);
                        raw[i * 4] = v; raw[i * 4 + 1] = v; raw[i * 4 + 2] = v; raw[i * 4 + 3] = 255;
                    }
                    t = new Texture2D(src.width, src.height, TextureFormat.RGBA32, true, true) { name = name + "_gray", wrapMode = TextureWrapMode.Repeat };
                    t.SetPixelData(raw, 0);
                    t.Apply(true, true);
                    t.anisoLevel = 4;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[Ринг] 3D: сіра карта набору " + name + " не вдалась: " + ex.Message);
                    t = null;
                }
            }
            cache[name] = t;
            return t;
        }
    }
}
