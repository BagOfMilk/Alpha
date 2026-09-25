using System;
using System.Collections.Generic;
using Game.Core.Characters.Creation;
using Game.Gameplay.Text;

namespace Game.Gameplay.Walk
{
    /// <summary>Точка на землі села (площина XZ, як у сцені: Y — висота, тут не потрібна).</summary>
    public struct WalkPoint
    {
        public float X;
        public float Z;

        public WalkPoint(float x, float z) { X = x; Z = z; }

        public static float Distance(WalkPoint a, WalkPoint b)
        {
            float dx = a.X - b.X, dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }

    /// <summary>
    /// Місце, куди можна підійти й «зайти» (E): пост, ділянка будівництва чи
    /// орієнтир. Заходження відкриває вкладку хаба <see cref="HubTab"/> — та
    /// сама вкладка, що й у панелі, жодної окремої логіки.
    /// </summary>
    public sealed class WalkPlace
    {
        public string Id;
        public string LabelKey;
        /// <summary>Для ділянки — id будівлі: підпис «Ділянка «{будівля}»».</summary>
        public string BuildingId;
        public int HubTab;
        public float X;
        public float Z;
        public float Radius;
    }

    /// <summary>
    /// Прогулянка селом у дусі CRPG (власник, 25.09.2026: «Я хотів шоб я міг
    /// бігати як у CRPG»): сітка прохідності, пошук шляху для кліку мишею і
    /// ковзання вздовж перешкод для WASD. Чистий C#, жодного типу рушія —
    /// компонент сцени (<c>HeroWalker</c>) лише заповнює перешкоди з
    /// габаритів моделей і застосовує результат, тому «герой обходить хату» і
    /// «у стіну не пройти» — твердження тестів, а не враження від кадру.
    /// Детермінований: ті самі перешкоди й ті самі кліки — той самий шлях.
    /// </summary>
    public sealed class WalkGrid
    {
        private readonly float _minX, _minZ, _cell;
        private readonly int _w, _h;
        private readonly bool[] _blocked;

        public WalkGrid(float minX, float minZ, float maxX, float maxZ, float cell)
        {
            if (cell <= 0f) throw new ArgumentOutOfRangeException(nameof(cell));
            _minX = minX;
            _minZ = minZ;
            _cell = cell;
            _w = Math.Max(1, (int)Math.Ceiling((maxX - minX) / cell));
            _h = Math.Max(1, (int)Math.Ceiling((maxZ - minZ) / cell));
            _blocked = new bool[_w * _h];
        }

        public int Width => _w;
        public int Height => _h;

        public void Clear() { Array.Clear(_blocked, 0, _blocked.Length); }

        /// <summary>Закриває прямокутник (уже розширений на радіус героя — це справа того, хто кличе).</summary>
        public void Block(float minX, float minZ, float maxX, float maxZ)
        {
            int x0 = Math.Max(0, (int)Math.Floor((minX - _minX) / _cell));
            int z0 = Math.Max(0, (int)Math.Floor((minZ - _minZ) / _cell));
            int x1 = Math.Min(_w - 1, (int)Math.Floor((maxX - _minX) / _cell));
            int z1 = Math.Min(_h - 1, (int)Math.Floor((maxZ - _minZ) / _cell));
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    _blocked[z * _w + x] = true;
        }

        public bool IsFree(WalkPoint p)
        {
            int x, z;
            return ToCell(p, out x, out z) && !_blocked[z * _w + x];
        }

        /// <summary>
        /// Найближча вільна точка: коли на місці героя щойно виросла будівля
        /// (ділянку добудовано), він виходить до краю, а не застрягає всередині.
        /// </summary>
        public WalkPoint NearestFreePoint(WalkPoint p)
        {
            int x, z;
            if (!ToCell(Clamp(p), out x, out z)) return p;
            if (!_blocked[z * _w + x]) return Clamp(p);
            int fx, fz;
            return NearestFree(x, z, out fx, out fz) ? CellCenter(fx, fz) : p;
        }

        /// <summary>
        /// Крок WASD: рух по осях окремо, тож об стіну герой не зупиняється
        /// намертво, а ковзає вздовж неї (як у будь-якій CRPG).
        /// </summary>
        public WalkPoint Slide(WalkPoint from, float dx, float dz)
        {
            var result = from;
            var tryX = new WalkPoint(from.X + dx, from.Z);
            if (IsFree(tryX)) result = tryX;
            var tryZ = new WalkPoint(result.X, result.Z + dz);
            if (IsFree(tryZ)) result = tryZ;
            return result;
        }

        /// <summary>
        /// Шлях кліку мишею: A* по восьми сусідах без зрізання кутів. Якщо ціль
        /// у стіні — іде до найближчої вільної клітинки. Порожній список — стоїмо
        /// на місці (ціль і так тут або шляху немає). Точки — центри клітинок,
        /// остання — сама ціль, коли вона вільна; прямі відрізки стиснені до
        /// поворотів.
        /// </summary>
        public List<WalkPoint> FindPath(WalkPoint from, WalkPoint to)
        {
            var path = new List<WalkPoint>();
            int sx, sz, tx, tz;
            if (!ToCell(from, out sx, out sz)) return path;
            if (!ToCell(Clamp(to), out tx, out tz)) return path;

            bool targetFree = !_blocked[tz * _w + tx];
            if (!targetFree && !NearestFreeTowards(tx, tz, sx, sz, out tx, out tz)) return path;
            if (sx == tx && sz == tz)
            {
                if (targetFree) path.Add(to);
                return path;
            }

            int n = _w * _h;
            var g = new float[n];
            var parent = new int[n];
            var closed = new bool[n];
            for (int i = 0; i < n; i++) { g[i] = float.MaxValue; parent[i] = -1; }

            int start = sz * _w + sx, goal = tz * _w + tx;
            g[start] = 0f;
            var open = new SortedSet<Node>(NodeComparer.Instance);
            open.Add(new Node(start, Heuristic(sx, sz, tx, tz)));

            // Порядок сусідів фіксований — рівні за вартістю шляхи
            // розв'язуються однаково щоразу (детермінізм).
            int[] ox = { 1, -1, 0, 0, 1, 1, -1, -1 };
            int[] oz = { 0, 0, 1, -1, 1, -1, 1, -1 };

            bool found = false;
            while (open.Count > 0)
            {
                var cur = open.Min;
                open.Remove(cur);
                int ci = cur.Index;
                if (closed[ci]) continue;
                closed[ci] = true;
                if (ci == goal) { found = true; break; }

                int cx = ci % _w, cz = ci / _w;
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + ox[k], nz = cz + oz[k];
                    if (nx < 0 || nz < 0 || nx >= _w || nz >= _h) continue;
                    int ni = nz * _w + nx;
                    if (_blocked[ni] || closed[ni]) continue;
                    bool diagonal = ox[k] != 0 && oz[k] != 0;
                    // Без зрізання кутів: по діагоналі — лише коли обидва бокові проходи вільні.
                    if (diagonal && (_blocked[cz * _w + nx] || _blocked[nz * _w + cx])) continue;

                    float cost = g[ci] + (diagonal ? 1.41421356f : 1f);
                    if (cost >= g[ni]) continue;
                    g[ni] = cost;
                    parent[ni] = ci;
                    open.Add(new Node(ni, cost + Heuristic(nx, nz, tx, tz)));
                }
            }

            if (!found) return path;

            var cells = new List<int>();
            for (int i = goal; i != -1 && i != start; i = parent[i]) cells.Add(i);
            cells.Reverse();

            // Стискаємо прямі відрізки: лишаються тільки повороти і кінець.
            int prevDx = int.MinValue, prevDz = int.MinValue;
            int px = sx, pz = sz;
            for (int i = 0; i < cells.Count; i++)
            {
                int cx = cells[i] % _w, cz = cells[i] / _w;
                int dx = cx - px, dz = cz - pz;
                if (i > 0 && (dx != prevDx || dz != prevDz))
                    path.Add(CellCenter(px, pz));
                prevDx = dx; prevDz = dz;
                px = cx; pz = cz;
            }
            path.Add(targetFree ? to : CellCenter(tx, tz));
            return path;
        }

        private WalkPoint Clamp(WalkPoint p)
        {
            float maxX = _minX + _w * _cell - _cell * 0.5f;
            float maxZ = _minZ + _h * _cell - _cell * 0.5f;
            return new WalkPoint(
                Math.Max(_minX + _cell * 0.5f, Math.Min(maxX, p.X)),
                Math.Max(_minZ + _cell * 0.5f, Math.Min(maxZ, p.Z)));
        }

        private bool ToCell(WalkPoint p, out int x, out int z)
        {
            x = (int)Math.Floor((p.X - _minX) / _cell);
            z = (int)Math.Floor((p.Z - _minZ) / _cell);
            return x >= 0 && z >= 0 && x < _w && z < _h;
        }

        private WalkPoint CellCenter(int x, int z)
        {
            return new WalkPoint(_minX + (x + 0.5f) * _cell, _minZ + (z + 0.5f) * _cell);
        }

        /// <summary>Найближча вільна клітинка кільцями навколо (детермінований обхід).</summary>
        private bool NearestFree(int x, int z, out int fx, out int fz)
        {
            int maxRing = Math.Max(_w, _h);
            for (int r = 1; r < maxRing; r++)
            {
                float best = float.MaxValue;
                int bx = -1, bz = -1;
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Abs(dx) != r && Math.Abs(dz) != r) continue;
                        int nx = x + dx, nz = z + dz;
                        if (nx < 0 || nz < 0 || nx >= _w || nz >= _h || _blocked[nz * _w + nx]) continue;
                        float d = dx * dx + dz * dz;
                        if (d < best) { best = d; bx = nx; bz = nz; }
                    }
                if (bx >= 0) { fx = bx; fz = bz; return true; }
            }
            fx = fz = -1;
            return false;
        }

        /// <summary>
        /// Клік у хату: вільна клітинка біля неї, але з БОКУ героя. Беремо два
        /// перші кільця, де є вільні клітинки, і з них — найближчу до героя;
        /// інакше тонка стіна вела б на дальній бік хати, бо той на клітинку ближчий.
        /// </summary>
        private bool NearestFreeTowards(int x, int z, int sx, int sz, out int fx, out int fz)
        {
            int maxRing = Math.Max(_w, _h);
            int firstRing = -1;
            float best = float.MaxValue, bestToTarget = float.MaxValue;
            fx = fz = -1;
            for (int r = 1; r < maxRing; r++)
            {
                if (firstRing >= 0 && r > firstRing + 1) break;
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Abs(dx) != r && Math.Abs(dz) != r) continue;
                        int nx = x + dx, nz = z + dz;
                        if (nx < 0 || nz < 0 || nx >= _w || nz >= _h || _blocked[nz * _w + nx]) continue;
                        if (firstRing < 0) firstRing = r;
                        float toStart = (nx - sx) * (nx - sx) + (nz - sz) * (nz - sz);
                        float toTarget = dx * dx + dz * dz;
                        if (toStart < best || (toStart == best && toTarget < bestToTarget))
                        {
                            best = toStart;
                            bestToTarget = toTarget;
                            fx = nx;
                            fz = nz;
                        }
                    }
            }
            return fx >= 0;
        }

        private static float Heuristic(int x, int z, int tx, int tz)
        {
            int dx = Math.Abs(x - tx), dz = Math.Abs(z - tz);
            return Math.Max(dx, dz) + 0.41421356f * Math.Min(dx, dz);
        }

        private struct Node
        {
            public readonly int Index;
            public readonly float F;
            public Node(int index, float f) { Index = index; F = f; }
        }

        private sealed class NodeComparer : IComparer<Node>
        {
            public static readonly NodeComparer Instance = new NodeComparer();
            public int Compare(Node a, Node b)
            {
                int c = a.F.CompareTo(b.F);
                return c != 0 ? c : a.Index.CompareTo(b.Index);
            }
        }
    }

    /// <summary>
    /// Що є в селі, куди можна зайти, і яку вкладку хаба відкриває кожне
    /// місце. Індекси вкладок — ті самі, що в <c>HubScreen.DrawTabBar</c>.
    /// </summary>
    public static class VillagePlaces
    {
        public const int TabPosts = 0, TabBuildings = 1, TabCouncil = 2, TabExpedition = 3, TabGear = 4,
            TabPeople = 5, TabQuests = 6, TabFactions = 7, TabReadiness = 8, TabSave = 9, TabJournal = 10;

        public const string NoticeBoardId = "notice_board";
        public const string TrainingGroundId = "training_ground";

        /// <summary>Вкладка для поста за його id; -1 — пост без свого місця в селі.</summary>
        public static int TabForPost(string postId)
        {
            switch (postId)
            {
                case "council_seat": return TabCouncil;        // рада збирається на площі
                case "storehouse_dock": return TabGear;        // склад — речі й спорядження
                case "workshop_bench": return TabGear;         // майстерня — крафт
                case "settlement_market": return TabFactions;  // ринок — чужі люди, торгівля, фракції
                case "infirmary_bed": return TabPeople;        // лазарет — стан людей
                case "settlement_farms": return TabPosts;      // поле — хто на якому посту
                case "scouting_post": return TabExpedition;    // застава біля воріт — вилазка
                default: return -1;
            }
        }

        public static WalkPlace Post(string postId, float x, float z)
        {
            int tab = TabForPost(postId);
            if (tab < 0) return null;
            return new WalkPlace { Id = "post:" + postId, LabelKey = "place." + postId, HubTab = tab, X = x, Z = z, Radius = 1.6f };
        }

        /// <summary>Ділянка будівництва (готова чи ні) — вкладка «Будівлі».</summary>
        public static WalkPlace Plot(string buildingId, float centerX, float centerZ, float halfSize)
        {
            return new WalkPlace
            {
                Id = "plot:" + buildingId, LabelKey = "place.plot", BuildingId = buildingId, HubTab = TabBuildings,
                X = centerX, Z = centerZ, Radius = halfSize + 1.1f
            };
        }

        public static WalkPlace NoticeBoard(float x, float z)
        {
            return new WalkPlace { Id = NoticeBoardId, LabelKey = "place.notice_board", HubTab = TabQuests, X = x, Z = z, Radius = 1.5f };
        }

        /// <summary>Тренувальний майданчик — вкладка «Готовність», де тренувальний бій.</summary>
        public static WalkPlace TrainingGround(float x, float z)
        {
            return new WalkPlace { Id = TrainingGroundId, LabelKey = "place.training_ground", HubTab = TabReadiness, X = x, Z = z, Radius = 1.8f };
        }

        /// <summary>Підпис місця українською — над ним у світі й у підказці «E — зайти».</summary>
        public static string LabelFor(WalkPlace place, Gender gender)
        {
            if (place == null) return string.Empty;
            if (!string.IsNullOrEmpty(place.BuildingId))
                return UkrainianText.Format(place.LabelKey, gender,
                    "building", UkrainianText.Get("building." + place.BuildingId, gender));
            return UkrainianText.Get(place.LabelKey, gender);
        }

        /// <summary>Найближче місце, в радіус якого герой уже зайшов; null — поруч нічого.</summary>
        public static WalkPlace Nearest(IReadOnlyList<WalkPlace> places, WalkPoint at)
        {
            if (places == null) return null;
            WalkPlace best = null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < places.Count; i++)
            {
                var p = places[i];
                if (p == null) continue;
                float d = WalkPoint.Distance(at, new WalkPoint(p.X, p.Z));
                if (d > p.Radius) continue;
                // Відносна відстань: маленьке місце поруч перемагає велику ділянку, в яку ми ледь зайшли.
                float score = d / p.Radius;
                if (score < bestScore) { bestScore = score; best = p; }
            }
            return best;
        }

        public static WalkPlace Find(IReadOnlyList<WalkPlace> places, string id)
        {
            if (places == null) return null;
            for (int i = 0; i < places.Count; i++)
                if (places[i] != null && places[i].Id == id) return places[i];
            return null;
        }
    }
}
