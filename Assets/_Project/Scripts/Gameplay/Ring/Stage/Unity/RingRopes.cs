using System.Collections.Generic;
using System.Globalization;
using Game.Core.Ring;
using UnityEngine;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Канати рингу: три на кожному боці між кутовими стійками (точки — з якорів арени anchor_rope_&lt;кут&gt;_&lt;1..3&gt;, генератор
    /// канатів не малює: їх будує гра). Кожен проліт — верле-канат <see cref="RopeSim"/> (ядро, без рушія): капсули тіл борців
    /// і рефері гнуть його, відскок від канатів видно. Малюється однією трубкою-сіткою (один виклик на весь ринг), форма
    /// оновлюється щокадру. Суто вигляд: на ядро гри не впливає. Не лінтується (сітка й рантайм-текстура).
    /// </summary>
    public sealed class RingRopes : MonoBehaviour
    {
        private const int Sides = 6;
        private const float RopeRadius = 0.026f;
        private const float TileLen = 0.18f;

        private sealed class Span
        {
            public RopeSim Sim;
            public Vector3 Out;     // з рингу назовні (Блендер: x, y)
            public float Height;    // м над підлогою
        }

        private readonly List<Span> _spans = new List<Span>();
        private readonly List<RopeCapsule> _caps = new List<RopeCapsule>(32);
        private RingActor[] _actors;
        private Mesh _mesh;
        private Vector3[] _verts;
        private Vector3[] _norms;
        private float _acc;
        private const float Dt = 1f / 60f;

        private struct Rig
        {
            public Transform LUL, RUL, LLL, RLL, LF, RF, LUA, RUA, LLA, RLA, LH, RH;
        }
        private Rig[] _rigs;

        /// <summary>Будує канати з якорів арени (текст venue_*_anchors.txt) і вішає сітку на цей об'єкт.</summary>
        public bool Build(TextAsset anchors, Material baseMaterial, RingActor[] actors, int points = 10)
        {
            _actors = actors;
            var a = new Dictionary<string, Vector3>();
            if (anchors != null)
            {
                foreach (var raw in anchors.text.Split('\n'))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    var p = line.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length < 4 || !p[0].StartsWith("anchor_rope_")) continue;
                    a[p[0]] = new Vector3(F(p[1]), F(p[2]), F(p[3]));   // Блендер: x, y, z вгору
                }
            }
            // Запасний варіант, якщо якорів немає: геометрія ринга 16 футів.
            string[] corners = { "NE", "NW", "SW", "SE" };
            Vector3[] outs = { new Vector3(0, 1, 0), new Vector3(-1, 0, 0), new Vector3(0, -1, 0), new Vector3(1, 0, 0) };
            float[] hs = { 1.48f, 1.86f, 2.24f };
            Vector3[] fallback = { new Vector3(2.138f, 2.138f, 0), new Vector3(-2.138f, 2.138f, 0), new Vector3(-2.138f, -2.138f, 0), new Vector3(2.138f, -2.138f, 0) };

            var settings = new RopeSettings { Points = points, Iterations = 3 };
            for (int rope = 0; rope < 3; rope++)
            {
                for (int side = 0; side < 4; side++)
                {
                    Vector3 p0, p1;
                    string k0 = "anchor_rope_" + corners[side] + "_" + (rope + 1);
                    string k1 = "anchor_rope_" + corners[(side + 1) % 4] + "_" + (rope + 1);
                    if (!a.TryGetValue(k0, out p0)) p0 = new Vector3(fallback[side].x, fallback[side].y, hs[rope]);
                    if (!a.TryGetValue(k1, out p1)) p1 = new Vector3(fallback[(side + 1) % 4].x, fallback[(side + 1) % 4].y, hs[rope]);
                    var sim = new RopeSim(new RopeVec(p0.x, p0.y, p0.z), new RopeVec(p1.x, p1.y, p1.z), new RopeVec(outs[side].x, outs[side].y, 0f), settings);
                    _spans.Add(new Span { Sim = sim, Out = outs[side], Height = p0.z });
                }
            }

            BuildMesh(points, baseMaterial);
            CacheRigs();
            Refresh();
            return true;
        }

        private static float F(string s) { return float.Parse(s, CultureInfo.InvariantCulture); }

        private void BuildMesh(int points, Material baseMaterial)
        {
            int n = _spans.Count;
            _verts = new Vector3[n * points * Sides];
            _norms = new Vector3[_verts.Length];
            var uv = new Vector2[_verts.Length];
            var tris = new List<int>(n * (points - 1) * Sides * 6);
            for (int s = 0; s < n; s++)
            {
                int b = s * points * Sides;
                for (int i = 0; i < points; i++)
                    for (int j = 0; j < Sides; j++)
                        uv[b + i * Sides + j] = new Vector2(i / (float)(points - 1) * (4.28f / TileLen), j / (float)Sides);
                for (int i = 0; i < points - 1; i++)
                    for (int j = 0; j < Sides; j++)
                    {
                        int j2 = (j + 1) % Sides;
                        int a0 = b + i * Sides + j, a1 = b + i * Sides + j2, c0 = b + (i + 1) * Sides + j, c1 = b + (i + 1) * Sides + j2;
                        tris.Add(a0); tris.Add(c0); tris.Add(a1);
                        tris.Add(a1); tris.Add(c0); tris.Add(c1);
                    }
            }
            _mesh = new Mesh { name = "RingRopes" };
            _mesh.MarkDynamic();
            _mesh.vertices = _verts;
            _mesh.uv = uv;
            _mesh.SetTriangles(tris, 0);
            _mesh.bounds = new Bounds(new Vector3(0f, 1.9f, 0f), new Vector3(5.4f, 1.4f, 5.4f));
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MakeMaterial(baseMaterial);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        private static Material MakeMaterial(Material template)
        {
            var m = template != null ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.name = "ring_rope";
            var tex = RopeTexture();
            m.SetTexture("_BaseMap", tex);
            m.SetTexture("_MainTex", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BumpMap", null);
            m.DisableKeyword("_NORMALMAP");
            m.SetFloat("_Smoothness", 0.35f);
            m.SetFloat("_Metallic", 0f);
            return m;
        }

        /// <summary>Скручений канат: три пасма по діагоналі, волокна й тінь між пасмами. Вздовж каната — u, навколо — v.</summary>
        private static Texture2D RopeTexture()
        {
            const int W = 128, H = 96;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, true, false) { name = "rope_twist", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    float u = x / (float)W, v = y / (float)H;
                    float ph = (v * 3f + u * 2f) % 1f;                          // три пасма, нахилені
                    float strand = Mathf.Sin(ph * Mathf.PI);                    // 0 у стику, 1 по центру
                    float fib = Hash(x * 7 + y * 131) * 0.10f + Mathf.Sin((u * 41f + v * 9f) * Mathf.PI * 2f) * 0.035f;
                    float c = 0.52f + 0.40f * Mathf.Pow(strand, 0.7f) + fib;
                    float warm = 0.97f;
                    px[y * W + x] = new Color(Mathf.Clamp01(c), Mathf.Clamp01(c * warm), Mathf.Clamp01(c * 0.93f), 1f);
                }
            }
            t.SetPixels32(px);
            t.Apply(true, true);
            return t;
        }

        private static float Hash(int i)
        {
            unchecked { uint x = (uint)i * 2654435761u; x ^= x >> 15; x *= 2246822519u; x ^= x >> 13; return (x & 0xFFFF) / 65535f; }
        }

        // ---------------- тіла ----------------

        private void CacheRigs()
        {
            _rigs = new Rig[_actors.Length];
            for (int i = 0; i < _actors.Length; i++)
            {
                var a = _actors[i];
                if (a == null) continue;
                _rigs[i] = new Rig
                {
                    LUL = a.GetBone(HumanBodyBones.LeftUpperLeg), RUL = a.GetBone(HumanBodyBones.RightUpperLeg),
                    LLL = a.GetBone(HumanBodyBones.LeftLowerLeg), RLL = a.GetBone(HumanBodyBones.RightLowerLeg),
                    LF = a.GetBone(HumanBodyBones.LeftFoot), RF = a.GetBone(HumanBodyBones.RightFoot),
                    LUA = a.GetBone(HumanBodyBones.LeftUpperArm), RUA = a.GetBone(HumanBodyBones.RightUpperArm),
                    LLA = a.GetBone(HumanBodyBones.LeftLowerArm), RLA = a.GetBone(HumanBodyBones.RightLowerArm),
                    LH = a.GetBone(HumanBodyBones.LeftHand), RH = a.GetBone(HumanBodyBones.RightHand)
                };
            }
        }

        private static RopeVec R(Transform t)
        {
            Vector3 p = t.position;           // Юніті → простір каната (Блендер): x, y = z_юніті, z = y_юніті
            return new RopeVec(p.x, p.z, p.y);
        }

        private static void Add(List<RopeCapsule> l, Transform a, Transform b, float r)
        {
            if (a == null || b == null) return;
            l.Add(new RopeCapsule(R(a), R(b), r));
        }

        private readonly List<RopeCapsule> _all = new List<RopeCapsule>(48);

        private void GatherBodies()
        {
            _all.Clear();
            for (int i = 0; i < _rigs.Length; i++)
            {
                if (_actors[i] == null || !_actors[i].Model.activeInHierarchy) continue;
                var g = _rigs[i];
                Add(_all, g.LUL, g.RUL, 0.17f);                 // таз
                Add(_all, g.LUA, g.RUA, 0.13f);                 // плечі
                Add(_all, g.LUL, g.LLL, 0.09f); Add(_all, g.LLL, g.LF, 0.07f);
                Add(_all, g.RUL, g.RLL, 0.09f); Add(_all, g.RLL, g.RF, 0.07f);
                Add(_all, g.LUA, g.LLA, 0.055f); Add(_all, g.LLA, g.LH, 0.05f);
                Add(_all, g.RUA, g.RLA, 0.055f); Add(_all, g.RLA, g.RH, 0.05f);
            }
        }

        private void Update()
        {
            if (_spans.Count == 0 || _rigs == null) return;
            _acc += Mathf.Min(Time.deltaTime, 0.1f);
            if (_acc < Dt) return;
            GatherBodies();
            int steps = 0;
            while (_acc >= Dt && steps < 4)
            {
                _acc -= Dt;
                steps++;
                for (int s = 0; s < _spans.Count; s++)
                {
                    var sp = _spans[s];
                    _caps.Clear();
                    for (int c = 0; c < _all.Count; c++)
                    {
                        var cap = _all[c];
                        float da = cap.A.X * sp.Out.x + cap.A.Y * sp.Out.y;
                        float db = cap.B.X * sp.Out.x + cap.B.Y * sp.Out.y;
                        if (da < 1.2f && db < 1.2f) continue;                       // далеко всередині рингу
                        if (da > 3.4f && db > 3.4f) continue;                       // далеко ззовні
                        if ((cap.A.Z < sp.Height - 0.5f && cap.B.Z < sp.Height - 0.5f) || (cap.A.Z > sp.Height + 0.5f && cap.B.Z > sp.Height + 0.5f)) continue;
                        _caps.Add(cap);
                    }
                    sp.Sim.Step(_caps);
                }
            }
            if (_acc > Dt) _acc = 0f;
            Refresh();
        }

        /// <summary>Поштовх канату в точці (0..1 вздовж прольоту) — для відскоку від канатів.</summary>
        public void Hit(int side, int rope, float along, Vector3 unityVelocity)
        {
            int idx = rope * 4 + side;
            if (idx < 0 || idx >= _spans.Count) return;
            _spans[idx].Sim.Hit(along, new RopeVec(unityVelocity.x, unityVelocity.z, unityVelocity.y));
        }

        private void Refresh()
        {
            int points = _spans[0].Sim.Count;
            for (int s = 0; s < _spans.Count; s++)
            {
                var sim = _spans[s].Sim;
                int b = s * points * Sides;
                float dist = 0f;
                Vector3 prevP = Vector3.zero;
                for (int i = 0; i < points; i++)
                {
                    RopeVec rv = sim[i];
                    Vector3 p = new Vector3(rv.X, rv.Z, rv.Y);
                    Vector3 pn = i + 1 < points ? new Vector3(sim[i + 1].X, sim[i + 1].Z, sim[i + 1].Y) : p;
                    Vector3 pp = i > 0 ? prevP : p;
                    Vector3 tan = (pn - pp);
                    if (tan.sqrMagnitude < 1e-8f) tan = Vector3.right;
                    tan.Normalize();
                    Vector3 n1 = Vector3.Cross(tan, Vector3.up);
                    if (n1.sqrMagnitude < 1e-6f) n1 = Vector3.Cross(tan, Vector3.forward);
                    n1.Normalize();
                    Vector3 n2 = Vector3.Cross(tan, n1);
                    for (int j = 0; j < Sides; j++)
                    {
                        float ang = j * (Mathf.PI * 2f / Sides);
                        Vector3 nrm = n1 * Mathf.Cos(ang) + n2 * Mathf.Sin(ang);
                        _verts[b + i * Sides + j] = p + nrm * RopeRadius;
                        _norms[b + i * Sides + j] = nrm;
                    }
                    prevP = p;
                    if (i > 0) dist += (p - pp).magnitude;
                }
            }
            _mesh.vertices = _verts;
            _mesh.normals = _norms;
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
