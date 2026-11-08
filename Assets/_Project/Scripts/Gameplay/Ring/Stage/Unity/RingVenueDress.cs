using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Наповнення зали: складані стільці (props.fbx, списки з venue_*_props.txt) і натовп (crowd_fans.fbx + CROWD_Anims).
    /// Стільці — склеєні сітки за кольором сидіння (кілька викликів на весь зал; поблизу детальна модель, далі —
    /// спрощена). Фанати — лише LOD1 (~0,9 тис. трикутників), кількість задає відвідуваність (<c>attendance</c>, для
    /// зрізу ~44 — найближчі до рингу спершу, оператори завжди). Кожен грає свою петлю зі зсувом фази.
    /// Рядок «[Ринг] бюджет» (<see cref="RingStage3D"/>) — лише довідка: бюджет Android не обмеження (власник 03.11.2026).
    /// Не лінтується (скінінг, плейбли, склеювання сіток).
    /// </summary>
    public sealed class RingVenueDress : MonoBehaviour
    {
        // Повна зала (власник 03.11.2026: «Ми тут АА робимо» — бюджет Android не обмеження): усі стільці детальні.
        public int attendance = 120;
        public float chairNear = 99f;
        /// <summary>
        /// Дешевий режим (Статут PERF-01, аудит 08.11.2026, В2): на Низькій графіці видно лише стільки найкращих місць.
        /// Рівень стає відомим пізніше за збирання зали (налаштування гравця), тому межа діє щокадру, а не при збиранні.
        /// </summary>
        public int lowTierAttendance = 60;
        private int _shownCap = -1;

        private sealed class Fan
        {
            public GameObject Go;
            public PlayableGraph Graph;
            public AnimationClipPlayable Clip;
            public float Length, Phase, Speed;
            public Animator Anim;
            public AnimationPlayableOutput Output;
            /// <summary>Кліп зі списку зали (спокійний настрій) і той, що грає зараз.</summary>
            public string Base, Current;
            public bool Standing;
            public Renderer[] Rends;
            public bool Hidden;
            /// <summary>Пропускав оновлення (невидимий): щойно стане видимим — оновити в тому ж кадрі, без чекання черги.</summary>
            public bool Stale = true;
            public int Salt;
        }

        private Dictionary<string, AnimationClip> _clipByName = new Dictionary<string, AnimationClip>();
        private int _mood;
        private float _popUntil;

        private readonly List<Fan> _fans = new List<Fan>();
        private readonly List<GameObject> _owned = new List<GameObject>();
        private int _warmUntil;

        public int FanCount { get { return _fans.Count; } }

        public static float F(string s) { return float.Parse(s, CultureInfo.InvariantCulture); }

        // ============================ стільці ============================

        public void BuildChairs(GameObject props, TextAsset list, Material fallback)
        {
            if (props == null || list == null) return;
            Transform node = Find(props.transform, "prop_chair_folding");
            if (node == null) return;
            Transform lod0 = Find(node, "prop_chair_folding_LOD0"), lod1 = Find(node, "prop_chair_folding_LOD1");
            if (lod0 == null || lod1 == null) return;
            Mesh m0 = lod0.GetComponent<MeshFilter>().sharedMesh, m1 = lod1.GetComponent<MeshFilter>().sharedMesh;
            var mats0 = lod0.GetComponent<MeshRenderer>().sharedMaterials;
            var mats1 = lod1.GetComponent<MeshRenderer>().sharedMaterials;
            if (m0 == null || m1 == null) return;
            if (!m0.isReadable || !m1.isReadable)
            {
                Debug.LogWarning("[Ринг] 3D: props.fbx не читається (isReadable) — стільців не буде.");
                return;
            }
            // Вузол моделі в просторі файлу: поворот осей (як у арени) вже в ньому.
            Matrix4x4 nodeToRoot = props.transform.worldToLocalMatrix * lod0.localToWorldMatrix;
            Matrix4x4 nodeToRoot1 = props.transform.worldToLocalMatrix * lod1.localToWorldMatrix;

            var groups = new Dictionary<string, List<ChairInst>>();
            foreach (var raw in list.text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 8 || p[0] != "chair_folding") continue;
                float x = F(p[1]), y = F(p[2]), z = F(p[3]), yaw = F(p[4]);
                Color col = new Color(F(p[5]), F(p[6]), F(p[7]));
                string key = col.r.ToString("F2") + "," + col.g.ToString("F2") + "," + col.b.ToString("F2");
                List<ChairInst> g;
                if (!groups.TryGetValue(key, out g)) { g = new List<ChairInst>(); groups[key] = g; }
                // Простір кімнати: Блендер (x, y, z вгору) → Юніті (x, z, y); курс — проти годинникової з боку Блендера.
                g.Add(new ChairInst { Pos = new Vector3(x, z, y), Yaw = -yaw, Col = col });
            }

            int total0 = 0, total1 = 0;
            foreach (var kv in groups)
            {
                var near = new List<ChairInst>();
                var far = new List<ChairInst>();
                foreach (var c in kv.Value)
                {
                    float d = new Vector2(c.Pos.x, c.Pos.z).magnitude;
                    (d <= chairNear ? near : far).Add(c);
                }
                Color col = kv.Value[0].Col;
                total0 += near.Count; total1 += far.Count;
                Combine("chairs_near_" + kv.Key, m0, mats0, nodeToRoot, near, col);
                Combine("chairs_far_" + kv.Key, m1, mats1, nodeToRoot1, far, col);
            }
            Debug.Log("[Ринг] 3D: стільців " + (total0 + total1) + " (детальних " + total0 + ")");
        }

        private struct ChairInst { public Vector3 Pos; public float Yaw; public Color Col; }

        private void Combine(string name, Mesh mesh, Material[] mats, Matrix4x4 meshToRoot, List<ChairInst> items, Color seat)
        {
            if (items.Count == 0) return;
            int subs = mesh.subMeshCount;
            for (int s = 0; s < subs; s++)
            {
                var ci = new CombineInstance[items.Count];
                for (int i = 0; i < items.Count; i++)
                {
                    ci[i].mesh = mesh;
                    ci[i].subMeshIndex = s;
                    ci[i].transform = Matrix4x4.TRS(items[i].Pos, Quaternion.Euler(0f, items[i].Yaw, 0f), Vector3.one) * meshToRoot;
                }
                var combined = new Mesh { name = name + "_" + s, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                combined.CombineMeshes(ci, true, true);
                var go = new GameObject(name + "_" + s);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = combined;
                var mr = go.AddComponent<MeshRenderer>();
                Material src = mats != null && s < mats.Length ? mats[s] : null;
                if (src == null) continue;
                var m = new Material(src);
                bool isSeat = src.name.IndexOf("Seat", StringComparison.OrdinalIgnoreCase) >= 0 && src.GetTexture("_BaseMap") == null;
                if (isSeat)
                {
                    Color lin = new Color(Mathf.Max(0.03f, seat.r), Mathf.Max(0.03f, seat.g), Mathf.Max(0.03f, seat.b));
                    m.SetColor("_BaseColor", lin.gamma);
                    m.SetFloat("_Smoothness", 0.3f);
                }
                mr.sharedMaterial = m;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _owned.Add(go);
            }
        }

        private static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = Find(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        // ============================ натовп ============================

        public void BuildCrowd(GameObject crowd, TextAsset list, AnimationClip[] clips, Avatar avatar)
        {
            if (crowd == null || list == null || avatar == null) { Debug.LogWarning("[Ринг] 3D: натовпу не буде (немає моделі, списку чи аватара)."); return; }
            var clipByName = _clipByName;
            foreach (var c in clips) if (c != null && !c.name.StartsWith("__preview__")) clipByName[c.name] = c;

            var cand = new List<FanSpec>();
            foreach (var raw in list.text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 7) continue;
                var f = new FanSpec { Pos = new Vector3(F(p[0]), F(p[2]), F(p[1])), Yaw = 180f - F(p[3]), Fan = p[4], Clip = p[5], Phase = F(p[6]) };
                f.Dist = new Vector2(f.Pos.x, f.Pos.z).magnitude;
                f.Operator = f.Clip.StartsWith("Cam_");
                if (f.Dist > 11f) continue;
                // Коридор виходу вільний: борець ішов крізь фанатів, що стояли впритул до доріжки (огляд 05.11.2026).
                if (!f.Operator && NearAisle(f.Pos, 1.0f)) continue;
                cand.Add(f);
            }
            cand.Sort((a, b) =>
            {
                if (a.Operator != b.Operator) return a.Operator ? -1 : 1;
                // Видимість із глядацької камери (з півдня): задній план (північ) і боки цінніші за місця під камерою.
                float sa = a.Dist + (a.Pos.z < -3.5f ? 4f : 0f), sb = b.Dist + (b.Pos.z < -3.5f ? 4f : 0f);
                return sa.CompareTo(sb);
            });
            var chosen = cand.GetRange(0, Mathf.Min(attendance, cand.Count));

            var protos = new Dictionary<string, GameObject>();
            Transform holder = new GameObject("CrowdProtos").transform;
            holder.SetParent(transform, false);
            holder.gameObject.SetActive(false);
            int idx = 0;
            foreach (var f in chosen)
            {
                AnimationClip clip;
                if (!clipByName.TryGetValue(f.Clip, out clip)) continue;
                GameObject proto;
                if (!protos.TryGetValue(f.Fan, out proto))
                {
                    proto = MakeProto(crowd, f.Fan, holder, avatar);
                    protos[f.Fan] = proto;
                }
                if (proto == null) continue;
                var go = Instantiate(proto, transform);
                go.name = "fan_" + idx++;
                go.SetActive(true);
                go.transform.localPosition = f.Pos;
                go.transform.localRotation = Quaternion.Euler(0f, f.Yaw, 0f);
                var fan = new Fan { Go = go, Length = clip.length, Phase = f.Phase * clip.length * 0.5f + (idx * 0.37f), Speed = 0.9f + 0.2f * ((idx * 53) % 10) / 10f };
                fan.Anim = go.GetComponent<Animator>();
                if (fan.Anim.avatar == null || !fan.Anim.avatar.isHuman) fan.Anim.avatar = avatar;   // свій аватар моделі (Humanoid), запасний — CROWD_Anims
                fan.Anim.applyRootMotion = false;
                fan.Anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;   // межі сітки набору — у бінд-позі: з відсіканням фанати зникали
                fan.Graph = PlayableGraph.Create("fan" + idx);
                fan.Graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(fan.Graph, "o", fan.Anim);
                fan.Clip = AnimationClipPlayable.Create(fan.Graph, clip);
                fan.Clip.SetApplyFootIK(false);
                output.SetSourcePlayable(fan.Clip);
                fan.Output = output;
                fan.Base = fan.Current = clip.name;
                fan.Standing = clip.name.StartsWith("Stand_");
                fan.Salt = (int)(unchecked((uint)idx * 2654435761u) % 100u);
                fan.Graph.Play();
                _fans.Add(fan);
                foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    // Межі — сталий куб навколо кореня скелета (2,6 м: сидячий і стоячий з руками вгору), а не перерахунок
                    // щокадру (updateWhenOffscreen): у бінд-позі межі набору хибні, тож задаємо їх самі (аудит 08.11.2026, В2).
                    r.updateWhenOffscreen = false;
                    r.localBounds = new Bounds(r.rootBone != null ? Vector3.zero : new Vector3(0f, 1f, 0f), new Vector3(2.6f, 2.6f, 2.6f));
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                fan.Rends = go.GetComponentsInChildren<Renderer>(true);
            }
            Destroy(holder.gameObject);
            _warmUntil = Time.frameCount + 3;
            Debug.Log("[Ринг] 3D: фанатів " + _fans.Count + " з " + cand.Count + " кандидатів, типів моделей " + protos.Count);
        }

        private static bool NearAisle(Vector3 p, float r)
        {
            var path = RingShow.Path;
            var q = new Vector2(p.x, p.z);
            for (int i = 0; i + 1 < path.Length; i++)
            {
                var a = new Vector2(path[i].x, path[i].z);
                var b = new Vector2(path[i + 1].x, path[i + 1].z);
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                if ((q - (a + ab * t)).magnitude < r) return true;
            }
            return false;
        }

        private struct FanSpec { public Vector3 Pos; public float Yaw; public string Fan, Clip; public float Phase, Dist; public bool Operator; }

        private GameObject MakeProto(GameObject crowd, string fan, Transform holder, Avatar shared)
        {
            var root = Instantiate(crowd, holder);
            root.name = "proto_" + fan;
            // Одразу, не Destroy: той лише в кінці кадру, і копії фанатів успадковували LODGroup, що показувала LOD0 —
            // а сітки LOD0 тут видаляються, тож зблизька фанат не малювався зовсім (порожні стільці, 03.11.2026).
            foreach (var lg in root.GetComponentsInChildren<LODGroup>(true)) DestroyImmediate(lg);
            // У кожного фаната набору свій скелет з його іменем: лишаємо цей скелет і його LOD1, решту прибираємо одразу —
            // інакше аватар знаходив «Hips» чужого скелета, і фанат стояв у бінд-позі.
            string keepMesh = fan + "_LOD1";
            var kill = new List<GameObject>();
            for (int i = 0; i < root.transform.childCount; i++)
            {
                var ch = root.transform.GetChild(i);
                if (ch.name == fan || ch.name == keepMesh) continue;
                kill.Add(ch.gameObject);
            }
            foreach (var k in kill) DestroyImmediate(k);
            // Матеріали натовпу з FBX можуть лишитись на вбудованому шейдері — URP у білді такі не малює (isVisible = false).
            // Переводимо на URP Lit, зберігаючи текстуру й колір; один набір на модель.
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var src = r.sharedMaterials;
                int sub = r.sharedMesh != null ? r.sharedMesh.subMeshCount : 1;
                Debug.Log("[Ринг] 3D: модель натовпу " + r.name + " матеріалів " + src.Length + ", підмешів " + sub);
                if (src.Length < sub)
                {
                    // Без матеріалу скін-сітка не малюється зовсім: дати одяг різних кольорів (детерміновано від імені).
                    var fill = new Material[sub];
                    for (int i = 0; i < sub; i++) fill[i] = i < src.Length ? src[i] : null;
                    src = fill;
                }
                var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    var s = src[i];
                    // Зачіска набору «shaggy green» має зелену текстуру — фанати виходили зеленоволосими (огляд 03.11.2026,
                    // власник 04.11.2026: «Дороби»). Текстуру прибираємо, колір — з природної палітри.
                    if (s != null && s.name.ToLowerInvariant().Contains("green_hair"))
                    {
                        var hm = s.shader != null && s.shader.name.StartsWith("Universal Render Pipeline") ? new Material(s) : new Material(lit);
                        var brown = BrownHairTex(crowd);
                        if (brown != null)
                        {
                            // Пасма й прозорість лишаються (та сама будова текстури), колір — каштанова зачіска того ж автора.
                            if (hm.HasProperty("_BaseMap")) hm.SetTexture("_BaseMap", brown);
                            if (hm.HasProperty("_MainTex")) hm.SetTexture("_MainTex", brown);
                            hm.SetColor("_BaseColor", Color.white);
                        }
                        else
                        {
                            int hh = (r.name.GetHashCode() ^ (i * 7919)) & 0x7fffffff;
                            hm.SetColor("_BaseColor", HairColors[hh % HairColors.Length]);
                        }
                        dst[i] = hm;
                        continue;
                    }
                    if (s != null && s.shader != null && s.shader.name.StartsWith("Universal Render Pipeline"))
                    {
                        // Без текстури — сірий манекен: даємо одягу колір (детерміновано від імені моделі й підмеша).
                        if (s.HasProperty("_BaseMap") && s.GetTexture("_BaseMap") == null)
                        {
                            var mc = new Material(s);
                            int hh = (r.name.GetHashCode() ^ (i * 7919)) & 0x7fffffff;
                            string ln = s.name.ToLowerInvariant();
                            bool skinLike = ln.Contains("skin") || ln.Contains("body");
                            // Волосся — природні кольори (раніше відтінок брався з усього кола, і фанати були зеленоволосі).
                            mc.SetColor("_BaseColor", skinLike ? s.GetColor("_BaseColor")
                                : ln.Contains("hair") || ln.Contains("brow") || ln.Contains("lash") ? HairColors[hh % HairColors.Length]
                                : Color.HSVToRGB((hh % 360) / 360f, 0.45f, 0.30f + (hh % 35) / 100f));
                            dst[i] = mc;
                        }
                        else dst[i] = s;
                        continue;
                    }
                    var m = new Material(lit);
                    if (s != null)
                    {
                        Texture tex = s.HasProperty("_BaseMap") ? s.GetTexture("_BaseMap") : null;
                        if (tex == null && s.HasProperty("_MainTex")) tex = s.GetTexture("_MainTex");
                        if (tex != null) m.SetTexture("_BaseMap", tex);
                        Color c = s.HasProperty("_BaseColor") ? s.GetColor("_BaseColor") : s.HasProperty("_Color") ? s.GetColor("_Color") : Color.white;
                        c.a = 1f;
                        m.SetColor("_BaseColor", c);
                        Debug.Log("[Ринг] 3D: матеріал натовпу " + s.name + " (" + (s.shader != null ? s.shader.name : "null") + ") → URP Lit");
                    }
                    if (s == null)
                    {
                        int h = (r.name.GetHashCode() ^ (i * 7919)) & 0x7fffffff;
                        m.SetColor("_BaseColor", Color.HSVToRGB((h % 360) / 360f, 0.35f, 0.25f + (h % 40) / 100f));
                    }
                    m.SetFloat("_Smoothness", 0.2f);
                    dst[i] = m;
                }
                r.sharedMaterials = dst;
            }
            var anim = root.GetComponent<Animator>();
            if (anim == null) anim = root.AddComponent<Animator>();
            // Аватар FBX один на весь набір і знятий зі скелета першого фаната (fan_f_east_asian_0): у решти інші
            // пропорції й спокій кісток, і м'язи кліпів лягали на чужі довжини — сітки рвались на уламки (03.11.2026).
            // Будуємо аватар під КОЖНУ модель: та сама карта кісток, але скелет спокою — її власний.
            // Карта кісток — з аватара моделі, якщо він людський, інакше з CROWD_Anims (імена кісток ті самі).
            Avatar map = anim.avatar != null && anim.avatar.isHuman ? anim.avatar : shared;
            if (map != null && map.isHuman) { var own = OwnAvatar(root, map.humanDescription, fan); if (own != null) anim.avatar = own; }
            return root;
        }

        private Texture _brownHair;
        private bool _brownHairLooked;

        /// <summary>Текстура каштанової зачіски набору (short_messy) — заміна зеленої «shaggy green».</summary>
        private Texture BrownHairTex(GameObject crowd)
        {
            if (_brownHairLooked) return _brownHair;
            _brownHairLooked = true;
            foreach (var r in crowd.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || !m.name.ToLowerInvariant().Contains("short_messy_hair")) continue;
                    Texture t = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                    if (t == null && m.HasProperty("_MainTex")) t = m.GetTexture("_MainTex");
                    if (t != null) { _brownHair = t; return t; }
                }
            return null;
        }

        private static readonly Color[] HairColors =
        {
            new Color(0.05f, 0.04f, 0.035f), new Color(0.12f, 0.08f, 0.05f), new Color(0.25f, 0.16f, 0.09f),
            new Color(0.45f, 0.33f, 0.18f), new Color(0.62f, 0.52f, 0.33f), new Color(0.35f, 0.14f, 0.07f), new Color(0.5f, 0.5f, 0.5f),
        };

        private static Avatar OwnAvatar(GameObject root, HumanDescription hd, string fan)
        {
            var bones = new List<SkeletonBone>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                bones.Add(new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale });
            hd.skeleton = bones.ToArray();
            var av = AvatarBuilder.BuildHumanAvatar(root, hd);
            av.name = "avatar_" + fan;
            if (!av.isValid || !av.isHuman) Debug.LogWarning("[Ринг] 3D: аватар " + fan + " не зібрався — лишаю спільний.");
            return av.isValid && av.isHuman ? av : null;
        }

        // ============================ настрій натовпу ============================

        /// <summary>
        /// Настрій за ажіотажем матчу (0 — зал холодний … 3 — зал кипить): частка фанатів, що перемикаються з
        /// «сидить, говорить, телефон» на оплески, а далі на крики й руки вгору; лиходієві — «бу» (<paramref name="boo"/>).
        /// Детерміновано: хто реагує першим — з номера фаната, не випадково.
        /// </summary>
        public void SetMood(int mood, bool boo)
        {
            _mood = Mathf.Clamp(mood, 0, 3);
            _boo = boo;
            if (Time.time >= _popUntil) ApplyMood(_mood);
        }

        private bool _boo;

        /// <summary>Великий момент (фінішер, відлік на 2, кікаут): на 2,5 с більшість фанатів скочує на крик.</summary>
        public void Pop()
        {
            _popUntil = Time.time + 2.5f;
            ApplyMood(3);
        }

        private void ApplyMood(int mood)
        {
            // Частки фанатів, що реагують: 0, 35 %, 65 %, 90 %.
            int[] share = { 0, 35, 65, 90 };
            foreach (var f in _fans)
            {
                if (f.Base.StartsWith("Cam_")) continue;
                string want = f.Base;
                if (f.Salt < share[mood])
                {
                    bool hot = mood >= 2 && f.Salt < share[mood] - 20;
                    if (f.Standing) want = _boo ? "Stand_Boo_Loop" : hot ? (f.Salt % 2 == 0 ? "Stand_Cheer_Loop" : "Stand_FistPump_Loop") : "Stand_Clap_Loop";
                    else want = _boo ? "Sit_Boo_Loop" : hot ? (f.Salt % 3 == 0 ? "Sit_Point_Loop" : "Sit_Cheer_Loop") : "Sit_Clap_Loop";
                }
                SetFanClip(f, want);
            }
        }

        /// <summary>
        /// Фанат упритул до камери не закриває кадр (огляд 04.11.2026: на виході пів екрана — голова фаната біля проходу):
        /// ховається, хто ближче 2 м до камери або стоїть на лінії камера → ціль ближче 0,7 м (у межах відрізка).
        /// </summary>
        public void Occlude(Vector3 cam, Vector3 target)
        {
            Vector3 d = target - cam;
            float len = d.magnitude;
            if (len < 1e-3f) return;
            d /= len;
            foreach (var f in _fans)
            {
                if (f.Go == null) continue;
                Vector3 p = f.Go.transform.position + Vector3.up * (f.Standing ? 1.2f : 0.8f);
                float t = Vector3.Dot(p - cam, d);
                bool hide = (p - cam).magnitude < 2.0f
                    || (t > 0f && t < len - 0.6f && (p - (cam + d * t)).magnitude < 0.7f);
                if (hide == f.Hidden) continue;
                if (f.Rends == null) f.Rends = f.Go.GetComponentsInChildren<Renderer>(true);
                foreach (var r in f.Rends) r.enabled = !hide;
                f.Hidden = hide;
            }
        }

        private void SetFanClip(Fan f, string name)
        {
            if (f.Current == name || !f.Graph.IsValid()) return;
            AnimationClip clip;
            if (!_clipByName.TryGetValue(name, out clip)) return;
            if (f.Clip.IsValid()) f.Clip.Destroy();
            f.Clip = AnimationClipPlayable.Create(f.Graph, clip);
            f.Clip.SetApplyFootIK(false);
            f.Output.SetSourcePlayable(f.Clip);
            f.Length = clip.length;
            f.Current = name;
        }

        private void LateUpdate()
        {
            if (_popUntil > 0f && Time.time >= _popUntil) { _popUntil = 0f; ApplyMood(_mood); }
            float t = Time.time;
            // Дешевий натовп (аудит 08.11.2026, В2): невидимих і схованих не рахуємо; далеких — через кадр чи раз на три.
            // Час петлі абсолютний, тож пропущений кадр не зсуває фазу — фанат лише рідше оновлюється.
            var cam = Camera.main;
            Vector3 cp = cam != null ? cam.transform.position : Vector3.zero;
            int frame = Time.frameCount;
            int cap = GraphicsTier.IsLow ? Mathf.Min(lowTierAttendance, _fans.Count) : _fans.Count;
            if (cap != _shownCap)
            {
                // Фанати вже впорядковані «найцінніші місця спершу» (BuildCrowd) — ховаємо хвіст.
                for (int i = 0; i < _fans.Count; i++) if (_fans[i].Go != null) _fans[i].Go.SetActive(i < cap);
                _shownCap = cap;
            }
            for (int i = 0; i < cap; i++)
            {
                var f = _fans[i];
                if (!f.Graph.IsValid()) continue;
                // Перші кадри після збирання isVisible ще не порахований — оновлюємо всіх, щоб ніхто не стояв у бінд-позі.
                if (frame > _warmUntil && (f.Hidden || !AnyVisible(f))) { f.Stale = true; continue; }
                if (cam != null && !f.Stale)
                {
                    float d2 = (f.Go.transform.position - cp).sqrMagnitude;
                    int stride = d2 > 25f * 25f ? 3 : d2 > 12f * 12f ? 2 : 1;
                    if ((frame + i) % stride != 0) continue;
                }
                f.Stale = false;
                float time = ((t * f.Speed) + f.Phase) % f.Length;
                f.Clip.SetTime(time);
                f.Graph.Evaluate(0f);
            }
        }

        private static bool AnyVisible(Fan f)
        {
            if (f.Rends == null) return true;
            for (int i = 0; i < f.Rends.Length; i++) if (f.Rends[i] != null && f.Rends[i].isVisible) return true;
            return false;
        }

        /// <summary>Діагностика натовпу для логу: де перші фанати, чи видні, межі.</summary>
        public void Diagnose()
        {
            for (int i = 0; i < Mathf.Min(4, _fans.Count); i++)
            {
                var go = _fans[i].Go;
                var r = go.GetComponentInChildren<SkinnedMeshRenderer>();
                string b = r != null ? (" vis=" + r.isVisible + " bounds=" + r.bounds.center.ToString("F2") + "/" + r.bounds.size.ToString("F2") + " en=" + r.enabled) : " (немає SMR)";
                Debug.Log("[Ринг] 3D: фанат " + i + " " + go.name + " pos=" + go.transform.position.ToString("F2") + b + " animHuman=" + (_fans[i].Anim != null && _fans[i].Anim.isHuman)
                    + " avatar=" + (_fans[i].Anim != null && _fans[i].Anim.avatar != null ? _fans[i].Anim.avatar.name + " human=" + _fans[i].Anim.avatar.isHuman + " valid=" + _fans[i].Anim.avatar.isValid : "null")
                    + " clip=" + (_fans[i].Clip.IsValid() ? _fans[i].Clip.GetAnimationClip().name + " humanMotion=" + _fans[i].Clip.GetAnimationClip().humanMotion : "invalid")
                    + " hips=" + (_fans[i].Anim != null && _fans[i].Anim.isHuman && _fans[i].Anim.GetBoneTransform(HumanBodyBones.Hips) != null ? _fans[i].Anim.GetBoneTransform(HumanBodyBones.Hips).position.ToString("F2") : "-"));
            }
        }

        /// <summary>Огляд натовпу зблизька (-ring-crowd-debug): камера на п'ятого фаната, кадр у файл, камера назад.</summary>
        public void DebugShot(Camera cam, string path)
        {
            var f = _fans[4].Go.transform;
            Vector3 p0 = cam.transform.position; Quaternion r0 = cam.transform.rotation;
            cam.transform.position = f.position + new Vector3(0f, 1.6f, 0f) - f.forward * 0f + (f.position.z > 0 ? new Vector3(0, 0, -2.5f) : new Vector3(0, 0, 2.5f));
            cam.transform.LookAt(f.position + Vector3.up * 0.8f);
            var rt = new RenderTexture(800, 600, 24);
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
            RenderTexture.active = rt;
            var t = new Texture2D(800, 600, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, 800, 600), 0, 0); t.Apply();
            RenderTexture.active = null;
            System.IO.File.WriteAllBytes(path, t.EncodeToPNG());
            cam.transform.position = p0; cam.transform.rotation = r0;
            Debug.Log("[Ринг] 3D: кадр натовпу " + path + " фанат " + f.name + " pos=" + f.position + " rot=" + f.rotation.eulerAngles + " scale=" + f.lossyScale);
        }

        private void OnDestroy()
        {
            foreach (var f in _fans) if (f.Graph.IsValid()) f.Graph.Destroy();
        }
    }
}
