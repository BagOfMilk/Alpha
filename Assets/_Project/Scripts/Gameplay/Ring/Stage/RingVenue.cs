using System;
using System.Collections.Generic;
using System.Globalization;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Геометрія залу з якорів арени (<c>Assets/Art/Ring/venue_*_anchors.txt</c>, генератор tools/blender/ring_venues.py) у
    /// координатах Unity: x — схід, y — угору, z — північ (Blender: x, z, y). Розміри рингу беруться з якорів, не з констант
    /// (CAST_ANIM.md, «Ринг і місця кліпів»): мат — <c>anchor_center</c>, лінія каната — <c>anchor_rope_NE_1</c>, стійка —
    /// <c>anchor_rope_mid_E_1</c>, край платформи — <c>anchor_apron_E</c> + 0,15.
    /// <para>
    /// Місце пари «за рингом» обирається за картою зайнятої підлоги (<see cref="PlanOutside"/>): стільці, натовп і реквізит
    /// з текстів залу, сходи й столи з якорів, а в грі — ще й трикутники сітки залу (<see cref="MarkTriangle"/>: огорожа,
    /// столи, штативи світла), яких у текстах немає. Оператори камер — «м'які»: поки пара за рингом, сцена їх ховає.
    /// </para>
    /// Чистий C# без типів рушія — лінтується й перевіряється тестами (RingVenueTests) на справжніх даних трьох залів.
    /// </summary>
    public sealed class RingVenue
    {
        /// <summary>Клітинка карти підлоги, м.</summary>
        public const float Cell = 0.1f;
        /// <summary>Вище цього над підлогою — перешкода (мати довкола рингу — 3–5 см, на них стоять).</summary>
        public const float ObstacleMin = 0.12f;
        /// <summary>Вище цього — над головою (ферми, світло під стелею): підлоги не займає.</summary>
        public const float Overhead = 3.2f;

        // Коридор пари за рингом (від краю платформи назовні): фартух не зачіпаємо, огорожа й перший ряд — за ним.
        /// <summary>Центр пари від краю платформи назовні, м.</summary>
        public const float PairOut = 1.0f;
        /// <summary>Розлука пари вздовж боку від центру, м (як на рингу: ±0,85).</summary>
        public const float PairApart = 0.85f;
        /// <summary>Смуга пари назовні від краю платформи: [0,15; 1,85] м — кліпи пари з бічним зсувом до 0,9 м уміщаються.</summary>
        public const float BandIn = 0.15f, BandOut = 1.85f;
        /// <summary>Довжина смуги вздовж боку (половина), від найбажанішої: кліпи пари з поступом 2–3,5 м.</summary>
        public static readonly float[] HalfLengths = { 2.0f, 1.7f, 1.4f };
        /// <summary>Найдальший зсув центру пари вздовж боку від середини боку, м.</summary>
        public const float MaxShift = 1.2f;
        /// <summary>Порядок боків: схід, південь (бік жорсткої камери), захід, північ (з неї — вихід).</summary>
        public static readonly string[] SideOrder = { "E", "S", "W", "N" };

        public string Id = "gym";
        public float MatY = 1.0f, Plat = 2.438f, RopeLine = 2.138f, PostLine = 2.218f;
        /// <summary>Край бійцівського квадрата (всередині канатів): лінія каната − 0,10 (як у режисера).</summary>
        public float RingHalf { get { return RopeLine - 0.10f; } }

        private readonly Dictionary<string, float[]> _anchors = new Dictionary<string, float[]>(StringComparer.Ordinal);
        private readonly List<float[]> _soft = new List<float[]>();

        private float _ext = 7.0f;
        private int _n;
        private float[] _top, _ground;

        public RingVenue() { ResetFloor(); }

        /// <summary>Якорі з тексту арени (<c>&lt;ім'я&gt; x y z курс°</c>, Blender, Z угору). Порожній текст — розміри спортзалу.</summary>
        public static RingVenue Parse(string anchors, string id = null)
        {
            var v = new RingVenue();
            if (!string.IsNullOrEmpty(id)) v.Id = id;
            if (string.IsNullOrEmpty(anchors)) return v;
            foreach (var raw in anchors.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                float x, y, z, yaw = 0f;
                if (p.Length < 4 || !F(p[1], out x) || !F(p[2], out y) || !F(p[3], out z)) continue;
                if (p.Length > 4) F(p[4], out yaw);
                v._anchors[p[0]] = new[] { x, z, y, yaw };
            }
            float[] a;
            if (v._anchors.TryGetValue("anchor_center", out a)) v.MatY = a[1];
            if (v._anchors.TryGetValue("anchor_rope_NE_1", out a)) v.RopeLine = a[0];
            if (v._anchors.TryGetValue("anchor_rope_mid_E_1", out a)) v.PostLine = a[0];
            if (v._anchors.TryGetValue("anchor_apron_E", out a)) v.Plat = a[0] + 0.15f;
            v._ext = Math.Max(7.0f, v.Plat + 4.0f);
            v.ResetFloor();
            return v;
        }

        private static bool F(string s, out float f)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out f);
        }

        /// <summary>Якір в Unity-координатах (x, y угору, z); курс — як у файлі.</summary>
        public bool TryAnchor(string name, out float x, out float y, out float z)
        {
            float[] a;
            if (!_anchors.TryGetValue(name, out a)) { x = y = z = 0f; return false; }
            x = a[0]; y = a[1]; z = a[2];
            return true;
        }

        public IEnumerable<string> AnchorNames { get { return _anchors.Keys; } }

        // ============================ карта підлоги ============================

        private void ResetFloor()
        {
            _n = (int)Math.Ceiling(2f * _ext / Cell);
            _top = new float[_n * _n];
            _ground = new float[_n * _n];
            _soft.Clear();
        }

        /// <summary>Половина сторони карти підлоги від центру, м.</summary>
        public float Extent { get { return _ext; } }

        private int Ix(float v) { return (int)Math.Floor((v + _ext) / Cell); }
        private bool In(int i, int j) { return i >= 0 && j >= 0 && i < _n && j < _n; }
        private float C(int i) { return -_ext + (i + 0.5f) * Cell; }

        /// <summary>Чи клітинка — сам ринг (платформа, фартух, спідниця): перешкодою не рахується.</summary>
        private bool Platform(float x, float z) { return Math.Abs(x) <= Plat + 0.05f && Math.Abs(z) <= Plat + 0.05f; }

        private void Put(int i, int j, float top)
        {
            if (!In(i, j) || Platform(C(i), C(j))) return;
            int k = j * _n + i;
            if (top > _top[k]) _top[k] = top;
        }

        /// <summary>Прямокутник перешкоди заввишки <paramref name="top"/>.</summary>
        public void MarkBox(float x0, float z0, float x1, float z1, float top)
        {
            if (top < ObstacleMin) return;
            int i0 = Ix(Math.Min(x0, x1)), i1 = Ix(Math.Max(x0, x1)), j0 = Ix(Math.Min(z0, z1)), j1 = Ix(Math.Max(z0, z1));
            for (int j = Math.Max(0, j0); j <= Math.Min(_n - 1, j1); j++)
                for (int i = Math.Max(0, i0); i <= Math.Min(_n - 1, i1); i++)
                    Put(i, j, top);
        }

        /// <summary>Коло перешкоди.</summary>
        public void MarkDisc(float x, float z, float r, float top) { MarkSegment(x, z, x, z, r, top); }

        /// <summary>Відрізок перешкоди завтовшки 2·<paramref name="r"/> (сходи від кута платформи до якоря).</summary>
        public void MarkSegment(float ax, float az, float bx, float bz, float r, float top)
        {
            if (top < ObstacleMin) return;
            int i0 = Ix(Math.Min(ax, bx) - r), i1 = Ix(Math.Max(ax, bx) + r), j0 = Ix(Math.Min(az, bz) - r), j1 = Ix(Math.Max(az, bz) + r);
            for (int j = Math.Max(0, j0); j <= Math.Min(_n - 1, j1); j++)
                for (int i = Math.Max(0, i0); i <= Math.Min(_n - 1, i1); i++)
                    if (SegDist(C(i), C(j), ax, az, bx, bz) <= r + Cell * 0.5f) Put(i, j, top);
        }

        /// <summary>
        /// Трикутник сітки залу (світові координати Unity). Плоскі низькі (мати, підлога) — висота підлоги; від
        /// <see cref="ObstacleMin"/> до <see cref="Overhead"/> — перешкода по його проєкції на підлогу (не по габариту:
        /// діагональні трикутники трибун не накривають коридор); над головою — пропуск.
        /// </summary>
        public void MarkTriangle(float ax, float ay, float az, float bx, float by, float bz, float cx, float cy, float cz)
        {
            float minY = Math.Min(ay, Math.Min(by, cy)), maxY = Math.Max(ay, Math.Max(by, cy));
            if (minY >= Overhead) return;
            float x0 = Math.Min(ax, Math.Min(bx, cx)), x1 = Math.Max(ax, Math.Max(bx, cx));
            float z0 = Math.Min(az, Math.Min(bz, cz)), z1 = Math.Max(az, Math.Max(bz, cz));
            if (x1 < -_ext || z1 < -_ext || x0 > _ext || z0 > _ext) return;
            // Увесь трикутник у межах платформи — ринг (полотно, стійки, спідниця): не перешкода й не підлога.
            if (Platform(x0, z0) && Platform(x1, z1) && Platform(x0, z1) && Platform(x1, z0)) return;
            bool ground = maxY < ObstacleMin;
            float top = Math.Min(maxY, Overhead);
            int i0 = Math.Max(0, Ix(x0)), i1 = Math.Min(_n - 1, Ix(x1)), j0 = Math.Max(0, Ix(z0)), j1 = Math.Min(_n - 1, Ix(z1));
            float tol = Cell * 0.71f;   // пів діагоналі клітинки: тонка стіна, що лише перетинає клітинку, теж її займає
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    float px = C(i), pz = C(j);
                    if (!InTri(px, pz, ax, az, bx, bz, cx, cz)
                        && SegDist(px, pz, ax, az, bx, bz) > tol && SegDist(px, pz, bx, bz, cx, cz) > tol && SegDist(px, pz, cx, cz, ax, az) > tol)
                        continue;
                    if (ground)
                    {
                        int k = j * _n + i;
                        if (maxY > _ground[k]) _ground[k] = maxY;
                    }
                    else Put(i, j, top);
                }
        }

        /// <summary>«М'яка» перешкода (оператор камери): у вибір місця не входить, сцена її ховає.</summary>
        public void MarkSoft(float x, float z, float r) { _soft.Add(new[] { x, z, r }); }

        public int SoftCount { get { return _soft.Count; } }

        /// <summary>Найвища перешкода в клітинці точки (0 — вільно).</summary>
        public float TopAt(float x, float z)
        {
            int i = Ix(x), j = Ix(z);
            return In(i, j) ? _top[j * _n + i] : 0f;
        }

        /// <summary>Висота підлоги (мати) в точці.</summary>
        public float GroundAt(float x, float z)
        {
            int i = Ix(x), j = Ix(z);
            return In(i, j) ? _ground[j * _n + i] : 0f;
        }

        /// <summary>Найвища перешкода в прямокутнику.</summary>
        public float TopIn(float x0, float z0, float x1, float z1)
        {
            float m = 0f;
            int i0 = Math.Max(0, Ix(Math.Min(x0, x1))), i1 = Math.Min(_n - 1, Ix(Math.Max(x0, x1)));
            int j0 = Math.Max(0, Ix(Math.Min(z0, z1))), j1 = Math.Min(_n - 1, Ix(Math.Max(z0, z1)));
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                    if (_top[j * _n + i] > m) m = _top[j * _n + i];
            return m;
        }

        /// <summary>Найвища перешкода в колі.</summary>
        public float TopDisc(float x, float z, float r)
        {
            float m = 0f;
            int i0 = Math.Max(0, Ix(x - r)), i1 = Math.Min(_n - 1, Ix(x + r)), j0 = Math.Max(0, Ix(z - r)), j1 = Math.Min(_n - 1, Ix(z + r));
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    float dx = C(i) - x, dz = C(j) - z;
                    if (dx * dx + dz * dz <= (r + Cell * 0.5f) * (r + Cell * 0.5f) && _top[j * _n + i] > m) m = _top[j * _n + i];
                }
            return m;
        }

        /// <summary>Найвища перешкода на відрізку підлоги (крок — клітинка).</summary>
        public float TopAlong(float ax, float az, float bx, float bz)
        {
            float len = (float)Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
            int steps = Math.Max(1, (int)Math.Ceiling(len / Cell));
            float m = 0f;
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)steps;
                m = Math.Max(m, TopAt(ax + (bx - ax) * t, az + (bz - az) * t));
            }
            return m;
        }

        /// <summary>Найближча клітинка з перешкодою від точки (у межах <paramref name="max"/>), м.</summary>
        public float Clearance(float x, float z, float max)
        {
            float best = max;
            int i0 = Math.Max(0, Ix(x - max)), i1 = Math.Min(_n - 1, Ix(x + max)), j0 = Math.Max(0, Ix(z - max)), j1 = Math.Min(_n - 1, Ix(z + max));
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    if (_top[j * _n + i] <= 0f) continue;
                    float dx = C(i) - x, dz = C(j) - z, d = (float)Math.Sqrt(dx * dx + dz * dz) - Cell * 0.5f;
                    if (d < best) best = d;
                }
            return Math.Max(0f, best);
        }

        private static float SegDist(float px, float pz, float ax, float az, float bx, float bz)
        {
            float dx = bx - ax, dz = bz - az, l2 = dx * dx + dz * dz;
            float t = l2 > 1e-8f ? ((px - ax) * dx + (pz - az) * dz) / l2 : 0f;
            t = Math.Max(0f, Math.Min(1f, t));
            float qx = ax + dx * t - px, qz = az + dz * t - pz;
            return (float)Math.Sqrt(qx * qx + qz * qz);
        }

        private static bool InTri(float px, float pz, float ax, float az, float bx, float bz, float cx, float cz)
        {
            float d1 = (px - bx) * (az - bz) - (ax - bx) * (pz - bz);
            float d2 = (px - cx) * (bz - cz) - (bx - cx) * (pz - cz);
            float d3 = (px - ax) * (cz - az) - (cx - ax) * (pz - az);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        // ============================ дані залу ============================

        /// <summary>
        /// Перешкоди з якорів: сходи (від кута платформи до якоря, завширшки ~1,1 м), столи хронометриста й коментаторів,
        /// дзвін, зброя біля рингу. Шляхи виходу перешкодою не є — по них ходять.
        /// </summary>
        public void MarkAnchors()
        {
            foreach (var kv in _anchors)
            {
                string n = kv.Key;
                float x = kv.Value[0], y = kv.Value[1], z = kv.Value[2];
                if (n.StartsWith("anchor_steps_", StringComparison.Ordinal))
                    MarkSegment(Math.Sign(x) * Plat, Math.Sign(z) * Plat, x, z, 0.55f, MatY);
                else if (n == "anchor_timekeeper" || n == "anchor_commentary")
                    MarkDisc(x, z, 0.6f, 1.0f);
                else if (n == "anchor_bell" || n == "anchor_weapon_kendo" || n == "anchor_weapon_trash")
                    MarkDisc(x, z, 0.35f, 0.9f);
                else if (n.StartsWith("anchor_weapon_", StringComparison.Ordinal))
                    MarkDisc(x, z, 0.6f, 1.0f);
            }
        }

        /// <summary>Реквізит залу (<c>venue_*_props.txt</c>: модель x y z курс° r g b): стільці, сумки, куртки, сидіння трибун.</summary>
        public void MarkProps(string props)
        {
            if (string.IsNullOrEmpty(props)) return;
            foreach (var raw in props.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                float x, y, z;
                if (p.Length < 4 || !F(p[1], out x) || !F(p[2], out y) || !F(p[3], out z)) continue;
                MarkDisc(x, y, 0.3f, z + 0.9f);
            }
        }

        /// <summary>
        /// Натовп (<c>venue_*_crowd.txt</c>: x y z курс° фанат кліп …): глядачі — перешкода, оператори камер (кліп Cam_*) —
        /// «м'які» (<see cref="MarkSoft"/>).
        /// </summary>
        public void MarkCrowd(string crowd)
        {
            if (string.IsNullOrEmpty(crowd)) return;
            foreach (var raw in crowd.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                float x, y, z;
                if (p.Length < 6 || !F(p[0], out x) || !F(p[1], out y) || !F(p[2], out z)) continue;
                if (p[5].StartsWith("Cam_", StringComparison.Ordinal)) MarkSoft(x, y, 0.45f);
                else MarkDisc(x, y, 0.3f, z + 1.8f);
            }
        }

        // ============================ за рингом ============================

        /// <summary>Місце пари за рингом: бік, вісь, центр і вільна смуга.</summary>
        public struct OutsideSpot
        {
            /// <summary>Смуга повністю вільна (інакше — найменш зайнятий варіант, з попередженням у журналі).</summary>
            public bool Ok;
            public string Side;
            /// <summary>Назовні від рингу (одиничний).</summary>
            public float OutX, OutZ;
            /// <summary>Уздовж боку (одиничний): гравець — у мінус, суперник — у плюс.</summary>
            public float AlongX, AlongZ;
            /// <summary>Зсув центру пари вздовж боку від середини боку, м.</summary>
            public float Shift;
            /// <summary>Відстань центру пари від центру рингу назовні, м.</summary>
            public float Dist;
            /// <summary>Вільна половина довжини смуги вздовж боку, м.</summary>
            public float HalfLength;
            /// <summary>Висота підлоги в центрі пари (мати), м.</summary>
            public float Ground;
            /// <summary>Найвища перешкода в смузі (0 — вільно).</summary>
            public float Blocked;

            public float X { get { return OutX * Dist + AlongX * Shift; } }
            public float Z { get { return OutZ * Dist + AlongZ * Shift; } }

            /// <summary>Прямокутник смуги (x0, z0, x1, z1) з запасом <paramref name="pad"/>.</summary>
            public void Rect(float plat, float pad, out float x0, out float z0, out float x1, out float z1)
            {
                float oIn = plat + BandIn - pad, oOut = plat + BandOut + pad;
                float a0 = Shift - HalfLength - pad, a1 = Shift + HalfLength + pad;
                float px0 = OutX * oIn + AlongX * a0, pz0 = OutZ * oIn + AlongZ * a0;
                float px1 = OutX * oOut + AlongX * a1, pz1 = OutZ * oOut + AlongZ * a1;
                x0 = Math.Min(px0, px1); x1 = Math.Max(px0, px1); z0 = Math.Min(pz0, pz1); z1 = Math.Max(pz0, pz1);
            }
        }

        private static void SideAxes(string side, out float ox, out float oz, out float ax, out float az)
        {
            switch (side)
            {
                case "E": ox = 1f; oz = 0f; ax = 0f; az = 1f; return;
                case "W": ox = -1f; oz = 0f; ax = 0f; az = 1f; return;
                case "N": ox = 0f; oz = 1f; ax = 1f; az = 0f; return;
                default: ox = 0f; oz = -1f; ax = 1f; az = 0f; return;
            }
        }

        /// <summary>
        /// Обирає місце пари за рингом: перший бік у <see cref="SideOrder"/>, де смуга
        /// [<see cref="BandIn"/>; <see cref="BandOut"/>] від краю платформи на всю довжину <see cref="HalfLengths"/> вільна,
        /// зі зсувом центру вздовж боку не далі <see cref="MaxShift"/> (найменший зсув — краще). Довша смуга важливіша за
        /// бажаний бік. Якщо вільної немає — найменш зайнята (Ok = false).
        /// </summary>
        public OutsideSpot PlanOutside()
        {
            var best = new OutsideSpot { Ok = false, Blocked = float.MaxValue };
            foreach (float half in HalfLengths)
                foreach (string side in SideOrder)
                {
                    float ox, oz, ax, az;
                    SideAxes(side, out ox, out oz, out ax, out az);
                    for (int k = 0; k <= (int)Math.Round(MaxShift / Cell); k++)
                        for (int sgn = 0; sgn < (k == 0 ? 1 : 2); sgn++)
                        {
                            var s = new OutsideSpot
                            {
                                Side = side, OutX = ox, OutZ = oz, AlongX = ax, AlongZ = az,
                                Shift = (sgn == 0 ? 1f : -1f) * k * Cell, Dist = Plat + PairOut, HalfLength = half
                            };
                            float x0, z0, x1, z1;
                            s.Rect(Plat, 0f, out x0, out z0, out x1, out z1);
                            s.Blocked = TopIn(x0, z0, x1, z1);
                            s.Ground = GroundAt(s.X, s.Z);
                            if (s.Blocked <= 0f) { s.Ok = true; return s; }
                            if (s.Blocked < best.Blocked) best = s;
                        }
                }
            return best;
        }

        /// <summary>Точка пари за рингом (x, z) для боку матчу: гравець — у мінус уздовж боку, суперник — у плюс.</summary>
        public static void PairPoint(OutsideSpot s, bool player, out float x, out float z)
        {
            float k = player ? -PairApart : PairApart;
            x = s.X + s.AlongX * k;
            z = s.Z + s.AlongZ * k;
        }

        /// <summary>
        /// Чи може тут стояти камера плану «за рингом»: поза рингом (не над платформою), не далі смуги за огорожею,
        /// на висоті 1,2–3,0 м, не в перешкоді (штатив світла, стіл) і по цей бік огорожі від пари (між камерою й парою
        /// немає нічого вище 0,9 м).
        /// </summary>
        public bool OutsideCameraOk(float x, float y, float z, float pairX, float pairZ)
        {
            float r = Math.Max(Math.Abs(x), Math.Abs(z));
            if (r < Plat + 0.15f || r > Plat + 2.2f) return false;
            if (y < 1.2f || y > 3.0f) return false;
            if (TopDisc(x, z, 0.35f) > y - 0.35f) return false;
            return TopAlong(x, z, pairX, pairZ) <= 0.9f;
        }

        /// <summary>«М'які» перешкоди (оператори) в прямокутнику — їх сцена ховає, поки пара за рингом.</summary>
        public int SoftIn(float x0, float z0, float x1, float z1)
        {
            int n = 0;
            foreach (var s in _soft)
                if (s[0] + s[2] >= x0 && s[0] - s[2] <= x1 && s[1] + s[2] >= z0 && s[1] - s[2] <= z1) n++;
            return n;
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}: мат {1:0.000}, канат {2:0.000}, стійка {3:0.000}, платформа {4:0.000}",
                Id, MatY, RopeLine, PostLine, Plat);
        }
    }
}
