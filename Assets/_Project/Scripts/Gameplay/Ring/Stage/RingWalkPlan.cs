using System;
using System.Collections.Generic;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Крокова хода виходу: план кроків і поворотів без ковзання стоп (умови арт-директора 15.11.2026).
    /// <para>
    /// Кліпи ходи чисті: якщо постать іде власним ходом петлі, опорна стопа стоїть (вимір прямою кінематикою — 0–0,4 см).
    /// Ковзання давала сцена, яка тягла таз по шляху з чужою швидкістю (фаз-тест 14.11.2026: 0,17–0,30 м на крок). Тому
    /// тут постать тримається за стопу, а не за шлях:
    /// </para>
    /// <list type="bullet">
    /// <item>стопа, що стала останньою, — опора: її носок стоїть у точці світу, а слот постаті щокадру ставиться від нього
    /// (після перенесення кліпа на тіло носок в опорі «гуляє» на ≈ 2 см відносно таза — це забирає таз, а не стопа);</item>
    /// <item>відрізок починається й закінчується посеред опори (одна стопа стоїть, друга в повітрі поруч) — на вході й на
    /// виході з петлі сцена тримає той самий носок, друга стопа ставиться, а не під'їжджає;</item>
    /// <item>поворот — довкола опорного носка, лише коли стоїть одна стопа, не більше <see cref="MaxTurn"/>° за опору;
    /// крутіший поворот шляху розкладається на кілька кроків;</item>
    /// <item>кінцева мітка (сходи, фартух) береться точно зсувом першої, невидимої точки — за шторою.</item>
    /// </list>
    /// Чистий шар: без рушія, усе детерміновано. Виконує <c>RingShow</c>; охоронці — <c>RingWalkPlanTests</c>.
    /// </summary>
    public static class RingWalkPlan
    {
        /// <summary>Кросфейд з ходи в нерухомий кліп, с: стільки сцена тримає опорний носок, поки жест набирає вагу.</summary>
        public const float StandFade = 0.3f;

        /// <summary>Найбільший поворот довкола опорного носка за одну опору, ° (умова АД — не більше 15°; запас 3°).</summary>
        public const float MaxTurn = 12f;

        /// <summary>
        /// Дальність прицілу керма, м: на яку точку шляху попереду таза дивиться постать. 0,9 м — повертає запізно, на крутих
        /// поворотах до сходів виносить на 0,54 м від шляху; 2,1 м і більше — зрізає кут у бік оператора з камерою (0,64 м до
        /// нього) і гірше влучає в мітку. 1,3 м: від шляху до 0,2–0,3 м, до оператора 0,75 м (розвідка RingWalkPlanTests, 15.11.2026).
        /// </summary>
        public const float LookAhead = 1.3f;

        /// <summary>Точка на підлозі (X, Z світу або системи слота).</summary>
        public struct V2
        {
            public float X, Z;
            public V2(float x, float z) { X = x; Z = z; }
            public static V2 operator +(V2 a, V2 b) { return new V2(a.X + b.X, a.Z + b.Z); }
            public static V2 operator -(V2 a, V2 b) { return new V2(a.X - b.X, a.Z - b.Z); }
            public static V2 operator *(V2 a, float k) { return new V2(a.X * k, a.Z * k); }
            public float Length { get { return (float)Math.Sqrt(X * X + Z * Z); } }
            public static float Dot(V2 a, V2 b) { return a.X * b.X + a.Z * b.Z; }
            /// <summary>Курс вектора, ° (0 — уздовж +Z, 90 — уздовж +X), як поворот довкола осі Y у рушії.</summary>
            public float Yaw { get { return (float)(Math.Atan2(X, Z) * 180.0 / Math.PI); } }
            public override string ToString() { return string.Format(System.Globalization.CultureInfo.InvariantCulture, "({0:0.000}; {1:0.000})", X, Z); }
        }

        /// <summary>Вектор системи слота у світі за курсом слота (як Quaternion.Euler(0, yaw, 0) * v).</summary>
        public static V2 Rot(float yawDeg, V2 v)
        {
            double a = yawDeg * Math.PI / 180.0;
            float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
            return new V2(v.X * c + v.Z * s, -v.X * s + v.Z * c);
        }

        /// <summary>Різниця курсів коротшим шляхом, ° (−180…180).</summary>
        public static float DeltaAngle(float from, float to)
        {
            float d = (to - from) % 360f;
            if (d > 180f) d -= 360f;
            if (d < -180f) d += 360f;
            return d;
        }

        /// <summary>Місце й курс слота постаті.</summary>
        public struct Pose
        {
            public V2 P;
            public float Yaw;
            public Pose(V2 p, float yaw) { P = p; Yaw = yaw; }
            /// <summary>Точка системи слота у світі.</summary>
            public V2 At(V2 local) { return P + Rot(Yaw, local); }
            /// <summary>Слот, у якому точка <paramref name="local"/> системи слота стоїть у точці світу при цьому курсі.</summary>
            public static Pose ByPoint(V2 world, V2 local, float yaw) { return new Pose(world - Rot(yaw, local), yaw); }
        }

        /// <summary>
        /// Петля ходи на конкретному тілі — вибірка кліпа в системі слота (за оберт петлі слот стоїть, поза йде вперед на
        /// <see cref="Stride"/>; зразок через межу оберту — зі зсувом на цей хід).
        /// </summary>
        public sealed class Gait
        {
            /// <summary>Хід таза за оберт петлі (два кроки).</summary>
            public V2 Stride;
            /// <summary>Носки й таз за оберт: зразок i — на фазі i / кількість зразків.</summary>
            public V2[] ToeL, ToeR, Hip;
            /// <summary>Вікна одноопорної фази, частки оберту: ліва стопа стоїть сама [LA, LB], права — [RA, RB].</summary>
            public float LA = 0.07f, LB = 0.43f, RA = 0.57f, RB = 0.93f;
            /// <summary>Фаза, на якій стопа стає на підлогу (кінець її маху), частка оберту 0…1.</summary>
            public float LandL = 0.97f, LandR = 0.47f;

            public float MidL { get { return (LA + LB) * 0.5f; } }
            public float MidR { get { return (RA + RB) * 0.5f; } }
            /// <summary>Довжина кроку, м.</summary>
            public float Step { get { return Stride.Length * 0.5f; } }
            /// <summary>Курс ходу в системі слота, °.</summary>
            public float TravelYaw { get { return Stride.Yaw; } }
            public float Mid(int foot) { return foot == 0 ? MidL : MidR; }
            public float Half(int foot) { return (foot == 0 ? LB - LA : RB - RA) * 0.5f; }
            public float Land(int foot) { return foot == 0 ? LandL : LandR; }

            private V2 At(V2[] a, float phase)
            {
                int m = a.Length;
                float x = (phase - (float)Math.Floor(phase)) * m;
                int i = (int)Math.Floor(x);
                if (i >= m) i = m - 1;
                float t = x - i;
                V2 p = a[i], q = i + 1 < m ? a[i + 1] : a[0] + Stride;
                return p + (q - p) * t;
            }

            /// <summary>Носок стопи (0 — ліва) на фазі, система слота поточного оберту.</summary>
            public V2 Toe(int foot, float phase) { return At(foot == 0 ? ToeL : ToeR, phase); }
            /// <summary>Таз на фазі, система слота поточного оберту (з погойдуванням убік, як у кліпі).</summary>
            public V2 HipAt(float phase) { return At(Hip, phase); }
            /// <summary>Перша фаза після <paramref name="phase"/> (абсолютної, з обертами), частка оберту якої — <paramref name="frac"/>.</summary>
            public static float Next(float phase, float frac)
            {
                float v = (float)Math.Floor(phase) + frac;
                if (v <= phase + 1e-4f) v += 1f;
                return v;
            }
        }

        /// <summary>Опора з поворотом: вікно фази (абсолютної, з обертами), стопа (0 — ліва), поворот довкола її носка, °.</summary>
        public struct Turn
        {
            public float A, B;
            public int Foot;
            public float Deg;
        }

        /// <summary>План одного відрізка ходи.</summary>
        public sealed class Leg
        {
            /// <summary>Старт: опорна стопа, її носок у світі, курс слота, фаза (середина опори стартової стопи), слот.</summary>
            public int StartFoot;
            public V2 StartToe;
            public float StartYaw, Phase0;
            public Pose Start;
            /// <summary>Скільки кроків (від середини опори до середини наступної).</summary>
            public int Steps;
            /// <summary>Повороти за опорами, у порядку часу (перша й остання опори — без повороту).</summary>
            public readonly List<Turn> Turns = new List<Turn>();
            /// <summary>Фаза зупинки (абсолютна), стопа, що стоїть у мить зупинки, слот і курс ходу в цю мить.</summary>
            public float PhaseStop;
            public int StopFoot;
            public Pose End;
            public float Heading;
            /// <summary>Справжній таз і опорний носок у мить зупинки, світ.</summary>
            public V2 HipEnd, ToeEnd;
            /// <summary>Найбільший поворот за опору (°) і найбільший відхід таза від шляху (м) — для охоронців і логу.</summary>
            public float MaxTurnDeg, MaxOff;
            /// <summary>Слід таза: на старті й посеред кожної опори (для охоронців: відстань до перешкод).</summary>
            public readonly List<V2> Track = new List<V2>();
            /// <summary>Сліди стоп: де ставав носок кожної опори, світ (для охоронців: стопа не за краєм помосту).</summary>
            public readonly List<V2> Prints = new List<V2>();
        }

        // ============================ шлях ============================

        /// <summary>Довжини шляху до кожної вершини.</summary>
        public static float[] Cumulative(IList<V2> path)
        {
            var c = new float[path.Count];
            for (int i = 1; i < path.Count; i++) c[i] = c[i - 1] + (path[i] - path[i - 1]).Length;
            return c;
        }

        /// <summary>Точка шляху на довжині <paramref name="s"/>; за кінцем — продовження останнього відрізка.</summary>
        public static V2 PointAt(IList<V2> path, float[] cum, float s)
        {
            int n = path.Count;
            if (s <= 0f) return path[0];
            float total = cum[n - 1];
            if (s >= total)
            {
                V2 d = path[n - 1] - path[n - 2];
                float l = d.Length;
                return l > 1e-5f ? path[n - 1] + d * ((s - total) / l) : path[n - 1];
            }
            int i = 1;
            while (i < n - 1 && cum[i] < s) i++;
            float seg = cum[i] - cum[i - 1];
            float t = seg > 1e-5f ? (s - cum[i - 1]) / seg : 1f;
            return path[i - 1] + (path[i] - path[i - 1]) * t;
        }

        /// <summary>
        /// Довжина шляху до найближчої до <paramref name="p"/> точки, не раніше <paramref name="from"/> (шлях не йде назад);
        /// <paramref name="off"/> — відстань до шляху.
        /// </summary>
        public static float Project(IList<V2> path, float[] cum, V2 p, float from, out float off)
        {
            float best = float.MaxValue, bestS = from;
            for (int i = 1; i < path.Count; i++)
            {
                if (cum[i] < from) continue;
                V2 a = path[i - 1], d = path[i] - a;
                float l2 = V2.Dot(d, d);
                float t = l2 > 1e-9f ? V2.Dot(p - a, d) / l2 : 0f;
                if (t < 0f) t = 0f;
                if (t > 1f) t = 1f;
                float s = cum[i - 1] + (cum[i] - cum[i - 1]) * t;
                if (s < from) { s = from; t = (from - cum[i - 1]) / Math.Max(1e-5f, cum[i] - cum[i - 1]); }
                float dist = (p - (a + d * t)).Length;
                if (dist < best - 1e-6f) { best = dist; bestS = s; }
            }
            off = best == float.MaxValue ? 0f : best;
            return bestS;
        }

        // ============================ відрізок ============================

        /// <summary>
        /// План відрізка. Старт: стопа <paramref name="startFoot"/> стоїть носком у точці <paramref name="toe"/> (світ), постать
        /// посеред її опори, слот курсом <paramref name="yaw"/>. Далі — кроки вздовж шляху до довжини <paramref name="stopAt"/>
        /// із зупинкою на найближчій середині опори. Кермо — «погоня за точкою» шляху на <paramref name="lookAhead"/> м попереду
        /// таза; поворот — довкола опорного носка, не більше <see cref="MaxTurn"/>° за опору. <paramref name="steps"/> &gt; 0 —
        /// рівно стільки кроків (перший відрізок за шторою).
        /// </summary>
        public static Leg Plan(Gait g, V2 toe, float yaw, int startFoot, IList<V2> path, float stopAt, float lookAhead, int steps = 0)
        {
            var leg = new Leg { StartFoot = startFoot, StartToe = toe, StartYaw = yaw, Phase0 = g.Mid(startFoot) };
            float[] cum = Cumulative(path);
            float step = g.Step;
            float phase = leg.Phase0;
            int foot = startFoot;
            V2 anchor = toe;
            leg.Start = Pose.ByPoint(anchor, g.Toe(foot, phase), yaw);
            leg.Prints.Add(anchor);
            float off;
            float sPrev = Project(path, cum, leg.Start.At(g.HipAt(phase)), 0f, out off);
            leg.MaxOff = off;
            leg.Track.Add(leg.Start.At(g.HipAt(phase)));
            int limit = steps > 0 ? steps : (int)Math.Ceiling((cum[cum.Length - 1] + 2f) / Math.Max(0.05f, step)) + 4;
            for (int i = 1; i <= limit; i++)
            {
                int next = 1 - foot;
                // Друга стопа стає: слот у цю мить — від носка нинішньої опори, носок нової опори — там, де він у позі.
                float land = Gait.Next(phase, g.Land(next));
                Pose atLand = Pose.ByPoint(anchor, g.Toe(foot, land), yaw);
                anchor = atLand.At(g.Toe(next, land));
                foot = next;
                leg.Prints.Add(anchor);
                // Середина її опори — мить можливої зупинки (без повороту).
                float mid = Gait.Next(land, g.Mid(foot));
                float a = mid - g.Half(foot), b = mid + g.Half(foot);
                Pose atMid = Pose.ByPoint(anchor, g.Toe(foot, mid), yaw);
                V2 hipStop = atMid.At(g.HipAt(mid));
                float sStop = Project(path, cum, hipStop, sPrev, out off);
                bool last = steps > 0 ? i == steps : sStop >= stopAt - step * 0.5f;
                if (last || i == limit)
                {
                    leg.Steps = i;
                    leg.PhaseStop = mid;
                    leg.StopFoot = foot;
                    leg.End = atMid;
                    leg.Heading = yaw + g.TravelYaw;
                    leg.HipEnd = hipStop;
                    leg.ToeEnd = anchor;
                    if (off > leg.MaxOff) leg.MaxOff = off;
                    leg.Track.Add(hipStop);
                    return leg;
                }
                // Кермо на початку вікна опори: куди дивитись — точка шляху попереду таза. Поворот — довкола опорного носка.
                V2 hip = Pose.ByPoint(anchor, g.Toe(foot, a), yaw).At(g.HipAt(a));
                float sHip = Project(path, cum, hip, sPrev, out off);
                if (off > leg.MaxOff) leg.MaxOff = off;
                V2 to = PointAt(path, cum, sHip + lookAhead) - hip;
                float turn = to.Length > 0.05f ? DeltaAngle(yaw + g.TravelYaw, to.Yaw) : 0f;
                if (turn > MaxTurn) turn = MaxTurn;
                if (turn < -MaxTurn) turn = -MaxTurn;
                if (Math.Abs(turn) < 0.05f) turn = 0f;
                if (turn != 0f)
                {
                    yaw += turn;
                    leg.Turns.Add(new Turn { A = a, B = b, Foot = foot, Deg = turn });
                    if (Math.Abs(turn) > leg.MaxTurnDeg) leg.MaxTurnDeg = Math.Abs(turn);
                }
                leg.Track.Add(Pose.ByPoint(anchor, g.Toe(foot, mid), yaw).At(g.HipAt(mid)));
                sPrev = sHip;
                phase = mid;
            }
            return leg;
        }

        // ============================ стояння між відрізками ============================

        /// <summary>
        /// Нерухомий кліп між відрізками ходи (жест біля завіси, зупинка біля бар'єра): носки й курс пози «на вході» й у
        /// кінці, система слота (вибірка кліпа на тілі). «На вході» — мить, коли кліп набрав повну вагу (кінець кросфейду,
        /// <see cref="StandFade"/> с від його початку): доти сцена тримає опорний носок ходи, і саме в цю мить відпускає.
        /// </summary>
        public sealed class Stand
        {
            public V2 ToeL0, ToeR0, ToeL1, ToeR1;
            /// <summary>Курс пози (лінія стегон) відносно слота на вході й у кінці, °.</summary>
            public float Yaw0, Yaw1;
            public V2 Toe0(int foot) { return foot == 0 ? ToeL0 : ToeR0; }
        }

        /// <summary>Слот нерухомого кліпа, що стає на опорний носок ходи: носок <paramref name="foot"/> — у точці, поза — за курсом.</summary>
        public static Pose PlaceStand(Stand s, V2 toeWorld, int foot, float poseYaw)
        {
            float yaw = poseYaw - s.Yaw0;
            return Pose.ByPoint(toeWorld, s.Toe0(foot), yaw);
        }

        // ============================ увесь вихід ============================

        /// <summary>Вихід: відрізок за шторою, жести біля завіси, шлях до рингу з необов'язковою зупинкою біля бар'єра.</summary>
        public sealed class Entrance
        {
            /// <summary>Зсув стартової точки вздовж проходу, м (підібраний так, щоб кінцева мітка вийшла точно).</summary>
            public float Shift;
            public Leg Curtain, ToBarrier, ToRing;
            /// <summary>Слоти жестів біля завіси (по одному на жест) і зупинки біля бар'єра.</summary>
            public readonly List<Pose> Gestures = new List<Pose>();
            /// <summary>Курс пози на вході в кожен жест, °: перший — курс ходу, наступний — курс, яким закінчився попередній.</summary>
            public readonly List<float> GestureYaw = new List<float>();
            public Pose Barrier;
            /// <summary>Похибка кінцевої мітки, м (таз у мить зупинки — мітка).</summary>
            public float EndError;
            /// <summary>Дальність прицілу керма, з якою складено план, м.</summary>
            public float LookAhead;
            /// <summary>Де таз стоїть біля завіси (жести) — для камери й допуску склейок.</summary>
            public V2 CurtainHip;
        }

        /// <summary>
        /// План усього виходу. <paramref name="curtainOut"/> — номінальна точка жестів перед аркою (м'яка мітка: зсувається
        /// в межах пів кроку), <paramref name="hiddenX"/> — борець до удару не ближче цієї межі до зали (за шторою);
        /// <paramref name="path"/> — шлях від точки жестів до рингу (перша вершина — <paramref name="curtainOut"/>),
        /// кінець шляху — жорстка мітка. <paramref name="barrierAt"/> ≥ 0 — довжина шляху, біля якої зупинка до зали.
        /// Прохід — уздовж +X (курс 90°).
        /// </summary>
        public static Entrance PlanEntrance(Gait g, V2 curtainOut, float hiddenX, IList<Stand> gestures, IList<V2> path,
            float barrierAt, Stand barrier, float lookAhead)
        {
            // Останні кроки до сходів повертають ступенями по 12°, тож поперечну нев'язку приціл виправляє не завжди. Інша
            // дальність прицілу кладе ці ступені на кути шляху інакше — пробуємо сусідні й беремо найточніший план.
            Entrance best = null;
            foreach (float d in new[] { 0f, -0.15f, 0.15f, -0.3f, 0.3f })
            {
                var e = PlanWith(g, curtainOut, hiddenX, gestures, path, barrierAt, barrier, lookAhead + d);
                e.LookAhead = lookAhead + d;
                if (best == null || e.EndError < best.EndError) best = e;
                if (best.EndError < 0.005f) break;
            }
            return best;
        }

        private static Entrance PlanWith(Gait g, V2 curtainOut, float hiddenX, IList<Stand> gestures, IList<V2> path,
            float barrierAt, Stand barrier, float lookAhead)
        {
            V2 end = path[path.Count - 1];
            float step = g.Step;
            V2 aim = new V2(0f, 0f);
            Entrance best = null;
            for (int round = 0; round < 4; round++)
            {
                // 1) Перебір зсуву старту в межах кроку. Похибка від зсуву — ламана: змінюється кількість кроків, стопа зупинки
                //    й те, як ступені повороту лягають на кути шляху. Тому кожну точку перебору уточнюємо вздовж ходу й
                //    беремо найкращу: поперечна нев'язка в кожної гілки своя.
                const int N = 48;
                Entrance pick = null;
                for (int i = 0; i <= N; i++)
                {
                    var e = Measure(Build(g, curtainOut, hiddenX, gestures, path, barrierAt, barrier, lookAhead, step * ((float)i / N - 0.5f), aim), end);
                    for (int k = 0; k < 4 && e.EndError > 0.002f; k++)
                    {
                        Leg last = e.ToRing;
                        float eAlong = V2.Dot(last.HipEnd - end, Rot(last.Heading, new V2(0f, 1f)));
                        float shift = e.Shift - eAlong;
                        if (Math.Abs(shift) > step * 0.5f + 1e-4f) break;
                        var e2 = Measure(Build(g, curtainOut, hiddenX, gestures, path, barrierAt, barrier, lookAhead, shift, aim), end);
                        if (e2.EndError >= e.EndError) break;
                        e = e2;
                    }
                    if (pick == null || e.EndError < pick.EndError) pick = e;
                }
                // 2) Упоперек ходу — зсувом точки прицілу в кінці шляху: на виміряну похибку й перебором довкола неї (останні
                //    кроки повертають ступенями, тож відповідь на зсув прицілу не лінійна).
                {
                    Leg last = pick.ToRing;
                    V2 side = Rot(last.Heading, new V2(1f, 0f));
                    float eLat = V2.Dot(last.HipEnd - end, side);
                    V2 aim0 = aim;
                    for (int k = -5; k <= 5 && pick.EndError > 0.002f; k++)
                    {
                        V2 a = aim0 - side * (eLat + k * 0.01f);
                        var e = Measure(Build(g, curtainOut, hiddenX, gestures, path, barrierAt, barrier, lookAhead, pick.Shift, a), end);
                        if (e.EndError < pick.EndError) { pick = e; aim = a; }
                    }
                }
                if (best == null || pick.EndError < best.EndError) best = pick;
                if (best.EndError < 0.004f) break;
            }
            return best;
        }

        private static Entrance Measure(Entrance e, V2 end)
        {
            e.EndError = (e.ToRing.HipEnd - end).Length;
            return e;
        }

        private static Entrance Build(Gait g, V2 curtainOut, float hiddenX, IList<Stand> gestures, IList<V2> path,
            float barrierAt, Stand barrier, float lookAhead, float shift, V2 aim)
        {
            var e = new Entrance { Shift = shift };
            float step = g.Step;
            // Точка жестів (таз) зі зсувом; за шторою — на 2 або 3 кроки раніше, щоб до удару борця не було видно.
            V2 gestureHip = new V2(curtainOut.X + shift, curtainOut.Z);
            int n = 2;
            while (gestureHip.X - n * step > hiddenX && n < 4) n++;
            // Відрізок за шторою: прямо вздовж +X, n кроків; таз на старті (посеред лівої опори) — на n кроків раніше.
            var line = new List<V2> { new V2(gestureHip.X - n * step - 1f, gestureHip.Z), new V2(gestureHip.X + 4f, gestureHip.Z) };
            float heading = 90f;
            float slotYaw = heading - g.TravelYaw;
            V2 hipStart = new V2(gestureHip.X - n * step, gestureHip.Z);
            V2 toeStart = Pose.ByPoint(hipStart, g.HipAt(g.MidL), slotYaw).At(g.Toe(0, g.MidL));
            e.Curtain = Plan(g, toeStart, slotYaw, 0, line, 0f, lookAhead, n);
            e.CurtainHip = e.Curtain.HipEnd;

            // Жести біля завіси: перший стає на опорний носок ходи, наступні — на лівий носок попереднього.
            V2 toeL = e.Curtain.ToeEnd;
            int foot = e.Curtain.StopFoot;
            float poseYaw = heading;
            bool haveL = foot == 0;
            V2 toeStop = e.Curtain.ToeEnd;
            if (gestures != null)
            {
                for (int i = 0; i < gestures.Count; i++)
                {
                    var s = gestures[i];
                    Pose p = i == 0 ? PlaceStand(s, toeStop, foot, poseYaw) : PlaceStand(s, toeL, 0, poseYaw);
                    e.Gestures.Add(p);
                    e.GestureYaw.Add(poseYaw);
                    toeL = p.At(s.ToeL1);
                    poseYaw = p.Yaw + s.Yaw1;
                    haveL = true;
                }
            }
            // Без жестів хода до рингу стартує з лівої опори: якщо зупинка була на правій — лівий носок беремо з пози зупинки.
            if (!haveL) toeL = e.Curtain.End.At(g.Toe(0, e.Curtain.PhaseStop));

            // Шлях до рингу (з прицілом у кінці, зсунутим на поправку).
            var route = new List<V2>(path);
            route[route.Count - 1] = route[route.Count - 1] + aim;
            float[] cum = Cumulative(route);
            float total = cum[cum.Length - 1];
            if (barrier != null && barrierAt > 0f && barrierAt < total - step)
            {
                e.ToBarrier = Plan(g, toeL, poseYaw - g.TravelYaw, 0, route, barrierAt, lookAhead);
                e.Barrier = PlaceStand(barrier, e.ToBarrier.ToeEnd, e.ToBarrier.StopFoot, e.ToBarrier.Heading);
                V2 bl = e.Barrier.At(barrier.ToeL1);
                e.ToRing = Plan(g, bl, e.Barrier.Yaw + barrier.Yaw1 - g.TravelYaw, 0, route, total, lookAhead);
            }
            else
            {
                e.ToBarrier = null;
                e.ToRing = Plan(g, toeL, poseYaw - g.TravelYaw, 0, route, total, lookAhead);
            }
            return e;
        }
    }
}
