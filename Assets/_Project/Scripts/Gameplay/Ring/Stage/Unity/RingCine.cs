using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Розкладка кіноплану (фінішер, перемога): камера з заданим курсом і нахилом, а важливі точки (голови, руки, коліна,
    /// контакт) мусять лягти в смугу кадру: нижче зони виносок (Статут FX-05: слово — у верхніх 8–22 % кадру, обличчя
    /// під ним не ховаються) і вище нижньої панелі. Шукається найближча відстань, за якої всі точки в смузі й по ширині,
    /// а камера там, де їй дозволено (<paramref name="allowed"/>). Плани поза центром — <see cref="OutsideShot"/> («за
    /// рингом») і <see cref="PerchShot"/> («на канаті / на куті»). Не лінтується: вектори рушія.
    /// </summary>
    public static class RingCine
    {
        public struct Shot
        {
            public Vector3 Pos, Look;
            public float Fov, Distance;
            public bool Ok;
            /// <summary>
            /// Чим кадр поступився, коли жоден не виконав м'яких вимог (кутова стійка на передньому плані, рефері обрізаний
            /// краєм), — тоді <see cref="Ok"/> і перший такий кадр; null — кадр без поступок.
            /// </summary>
            public string Compromise;
        }

        /// <param name="yawDeg">Курс погляду камери (Unity: 0 — уздовж +Z).</param>
        /// <param name="pitchDeg">Нахил: + — дивиться вниз, − — знизу вгору.</param>
        /// <param name="top">Найвища точка — не вище цієї частки висоти кадру від верху.</param>
        /// <param name="bottom">Найнижча — не нижче цієї частки.</param>
        /// <param name="side">Поле збоку, частка ширини.</param>
        /// <param name="tops">Межа зверху для кожної точки окремо (null — <paramref name="top"/> для всіх): голови — під зоною
        /// слова, піднята рука може зайти вище (Статут FX-05 стереже обличчя, не руки).</param>
        public static Shot Frame(IList<Vector3> pts, float yawDeg, float pitchDeg, float fov, float aspect,
            float top, float bottom, float side, Func<Vector3, bool> allowed, float dMin = 1.2f, float dMax = 12f, IList<float> tops = null)
        {
            var best = new Shot { Ok = false, Fov = fov };
            if (pts == null || pts.Count == 0) return best;
            float yaw = yawDeg * Mathf.Deg2Rad, pitch = pitchDeg * Mathf.Deg2Rad;
            var F = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), -Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));
            var R = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
            var U = Vector3.Cross(F, R);
            Vector3 c0 = Vector3.zero;
            for (int i = 0; i < pts.Count; i++) c0 += pts[i];
            c0 /= pts.Count;
            float tanH = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            float lo = 1f - 2f * bottom, hi = 1f - 2f * top, k = 1f - 2f * side;
            for (float d = dMin; d <= dMax; d += 0.05f)
            {
                float hLo = float.MinValue, hHi = float.MaxValue, wLo = float.MinValue, wHi = float.MaxValue;
                bool front = true;
                for (int i = 0; i < pts.Count; i++)
                {
                    Vector3 v = pts[i] - c0;
                    float z = Vector3.Dot(v, F) + d;
                    if (z < 0.4f) { front = false; break; }
                    float y = Vector3.Dot(v, U), x = Vector3.Dot(v, R);
                    float hiI = tops != null && i < tops.Count ? 1f - 2f * tops[i] : hi;
                    // (y − h) / (z·tanH) ∈ [lo, hi]  ⇔  h ∈ [y − hi·z·tanH, y − lo·z·tanH]
                    hLo = Mathf.Max(hLo, y - hiI * z * tanH);
                    hHi = Mathf.Min(hHi, y - lo * z * tanH);
                    wLo = Mathf.Max(wLo, x - k * z * tanH * aspect);
                    wHi = Mathf.Min(wHi, x + k * z * tanH * aspect);
                }
                if (!front || hLo > hHi || wLo > wHi) continue;
                // Найвища точка — рівно на межі смуги (обличчя одразу під словом), по ширині — посередині.
                float h = hLo, w = (wLo + wHi) * 0.5f;
                Vector3 pos = c0 - F * d + U * h + R * w;
                if (allowed != null && !allowed(pos)) continue;
                best.Pos = pos;
                best.Look = pos + F * d;
                best.Distance = d;
                best.Ok = true;
                return best;
            }
            return best;
        }

        /// <summary>Те саме з кількома кутами огляду: перший вдалий (ширший кут — лише якщо ближче не вміщається).</summary>
        public static Shot FrameAny(IList<Vector3> pts, float yawDeg, float pitchDeg, float[] fovs, float aspect,
            float top, float bottom, float side, Func<Vector3, bool> allowed, float dMin = 1.2f, float dMax = 12f, IList<float> tops = null)
        {
            var s = new Shot();
            for (int i = 0; i < fovs.Length; i++)
            {
                s = Frame(pts, yawDeg, pitchDeg, fovs[i], aspect, top, bottom, side, allowed, dMin, dMax, tops);
                s.Fov = fovs[i];
                if (s.Ok) return s;
            }
            return s;
        }

        // ---------------- плани поза центром рингу ----------------

        // Курси «за рингом» — кут від напрямку назовні до напрямку вздовж боку: спершу трохи назовні (за парою огорожа й
        // перший ряд), далі вздовж боку, далі трохи до рингу (за парою фартух); наостанок — круто назовні з-над фартуха й круто
        // до рингу з-за огорожі (вузький коридор спортзалу). Ширші кути й крутіший нахил — запасні, після звичних.
        private static readonly float[] OutsideAngles = { 70f, -70f, 82f, -82f, 95f, -95f, 58f, -58f, 110f, -110f, 125f, -125f,
                                                          45f, -45f, 140f, -140f, 30f, -30f, 155f, -155f };
        private static readonly float[] OutsidePitch = { 14f, 20f, 26f, 32f, 40f };
        private static readonly float[] OutsideFovs = { 40f, 46f, 52f, 58f, 64f };

        /// <summary>
        /// План «за рингом»: пара на підлозі між фартухом і огорожею. Камера — довкола рингу (не в рингу, не над платформою,
        /// не далі 2,2 м від неї, не в штативі світла; кожне тіло пари видно не крізь ринг і над огорожею — <see cref="RingVenue.OutsideCameraOk"/>,
        /// <see cref="RingVenue.SeesOutside"/> залу
        /// <see cref="RingStage3D.Venue"/>), на висоті 1,2–3 м, з нахилом униз; у кадрі — <paramref name="pts"/> (тіла пари;
        /// додай точку краю фартуха чи реквізиту, щоб і вони лягли в кадр). <paramref name="pairCenter"/> — центр пари на
        /// підлозі, <paramref name="outward"/> — назовні від рингу (<see cref="RingStage3D.OutsideOut"/>).
        /// Перебір: курс (кут від «назовні», спершу з огорожею за парою), нахил, кут огляду; перший кадр, що вміщає все,
        /// без кутової стійки на передньому плані й без рефері на фартуху, обрізаного краєм кадру (<paramref name="referee"/>,
        /// типово <see cref="RingStage3D.OutsideRefPos"/>: або весь у кадрі, або поза ним). Коли таких немає — перший, що вміщає
        /// пару, з поступкою в <see cref="Shot.Compromise"/>.
        /// <paramref name="preferYaw"/> — курс попереднього кадру «за рингом»: курси перебираються від найближчого до нього, і
        /// новий план лишається з того самого боку пари, поки там є чистий кадр (огляд 8f: між подіями камера перескакувала з
        /// одного кінця проходу на інший — gym x −2,2 → +2,4 → −1,5, рефері стрибав з правого переднього плану на лівий).
        /// </summary>
        public static Shot OutsideShot(IList<Vector3> pts, Vector3 pairCenter, Vector3 outward, float aspect,
            Func<Vector3, bool> allowed = null, IList<float> tops = null, Vector3? referee = null, float? preferYaw = null)
        {
            var venue = RingStage3D.Venue;
            Vector3 rf = referee ?? RingStage3D.OutsideRefPos;
            // Камера бачить кожне тіло пари: не крізь ринг (кутова стійка й подушки — огляд п. 8, 09.11.2026) і над перешкодами
            // підлоги. Точки на рингу (край фартуха) — лише в кадрі, лінію до них не перевіряємо.
            if (allowed == null) allowed = p =>
            {
                if (!venue.OutsideCameraOk(p.x, p.y, p.z, pairCenter.x, pairCenter.z)) return false;
                for (int i = 0; i < pts.Count; i++)
                {
                    Vector3 q = pts[i];
                    if (Mathf.Max(Mathf.Abs(q.x), Mathf.Abs(q.z)) <= venue.RingSightHalf) continue;
                    if (!venue.SeesOutside(p.x, p.y, p.z, q.x, q.y, q.z)) return false;
                }
                return true;
            };
            outward.y = 0f;
            if (outward.sqrMagnitude < 1e-6f) outward = Vector3.right;
            outward.Normalize();
            var along = new Vector3(-outward.z, 0f, outward.x);
            var s = new Shot();
            // Запасні кадри з поступкою: обрізаний рефері гірший за чистий, але кращий за стійку перед парою (вона закриває бій).
            var refCut = new Shot();
            var postFront = new Shot();
            foreach (float th in OutsideOrder(outward, along, preferYaw))
            {
                float r = th * Mathf.Deg2Rad;
                // Камера дивиться в цьому напрямку; стоїть — по інший бік пари.
                Vector3 look = outward * Mathf.Cos(r) + along * Mathf.Sin(r);
                float yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
                // Кути огляду — тут, а не FrameAny: м'які вимоги (стійка, рефері) перевіряються для кожного, бо ширший кут з тим
                // самим курсом і нахилом може їх виконати (рев'ю 8c).
                foreach (float pitch in OutsidePitch)
                foreach (float fov in OutsideFovs)
                {
                    s = Frame(pts, yaw, pitch, fov, aspect, 0.2f, 0.86f, 0.08f, allowed, 1.2f, 7f, tops);
                    s.Fov = fov;
                    if (!s.Ok) continue;
                    if (PostInFront(venue, s, pairCenter, aspect))
                    {
                        if (!postFront.Ok) { postFront = s; postFront.Compromise = "кутова стійка на передньому плані"; }
                        continue;
                    }
                    if (RingVenue.FigureCut(s.Pos.x, s.Pos.y, s.Pos.z, s.Look.x, s.Look.y, s.Look.z, s.Fov, aspect,
                            rf.x, rf.y, rf.z, RingVenue.RefHeight, RingVenue.RefBody))
                    {
                        if (!refCut.Ok) { refCut = s; refCut.Compromise = "рефері на фартуху обрізаний краєм кадру"; }
                        continue;
                    }
                    if (RingLaunchArgs.Has("-ring-cine-log")) LogOutside(venue, s, pairCenter, "чистий");
                    return s;
                }
            }
            var best = refCut.Ok ? refCut : postFront;
            if (!best.Ok) return s;
            // Кадр з поступкою — у журнал (режисер свого рядка про це не пише; огляд 8e — стійка перед парою в арені).
            Debug.LogWarning(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[Ринг] план «за рингом» з поступкою: {0}; камера ({1:0.00}; {2:0.00}; {3:0.00}) → ({4:0.00}; {5:0.00}; {6:0.00}), кут {7:0}°",
                best.Compromise, best.Pos.x, best.Pos.y, best.Pos.z, best.Look.x, best.Look.y, best.Look.z, best.Fov));
            return best;
        }

        /// <summary>
        /// Порядок курсів «за рингом»: без <paramref name="preferYaw"/> — звичний (<see cref="OutsideAngles"/>); з ним — від
        /// найближчого до попереднього курсу камери, за рівності — у звичному порядку (сортування стійке).
        /// </summary>
        private static IList<float> OutsideOrder(Vector3 outward, Vector3 along, float? preferYaw)
        {
            if (!preferYaw.HasValue) return OutsideAngles;
            var order = new List<KeyValuePair<float, int>>(OutsideAngles.Length);
            for (int i = 0; i < OutsideAngles.Length; i++) order.Add(new KeyValuePair<float, int>(OutsideAngles[i], i));
            Func<float, float> gap = th =>
            {
                float r = th * Mathf.Deg2Rad;
                Vector3 look = outward * Mathf.Cos(r) + along * Mathf.Sin(r);
                return Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg, preferYaw.Value));
            };
            order.Sort((a, b) =>
            {
                int c = gap(a.Key).CompareTo(gap(b.Key));
                return c != 0 ? c : a.Value.CompareTo(b.Value);
            });
            var res = new float[order.Count];
            for (int i = 0; i < res.Length; i++) res[i] = order[i].Key;
            return res;
        }

        /// <summary>Курс кадру (°, від осі Z за годинниковою зверху) — те, що приймає <see cref="OutsideShot"/> як preferYaw.</summary>
        public static float YawOf(Shot s)
        {
            Vector3 f = s.Look - s.Pos;
            return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Журнал кадру «за рингом» (прапорець запуску <c>-ring-cine-log</c>, для оглядів): камера, ціль, кут і найближча кутова
        /// стійка — відстань від камери й кут від осі кадру проти відстані до пари.
        /// </summary>
        private static void LogOutside(RingVenue venue, Shot s, Vector3 pairCenter, string kind)
        {
            Vector3 f = s.Look - s.Pos;
            f.y = 0f;
            float best = float.MaxValue, ang = 0f;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    var v = new Vector3(sx * venue.PostLine - s.Pos.x, 0f, sz * venue.PostLine - s.Pos.z);
                    if (v.magnitude < best) { best = v.magnitude; ang = Vector3.Angle(f, v); }
                }
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[Ринг] план «за рингом» ({0}): камера ({1:0.00}; {2:0.00}; {3:0.00}) → ({4:0.00}; {5:0.00}; {6:0.00}), кут {7:0}°; найближча стійка {8:0.00} м, {9:0}° від осі; до пари {10:0.00} м",
                kind, s.Pos.x, s.Pos.y, s.Pos.z, s.Look.x, s.Look.y, s.Look.z, s.Fov, best, ang, Flat(pairCenter - s.Pos)));
        }

        /// <summary>
        /// Кутова стійка на передньому плані знятого кадру «за рингом» (ближче за пару, у горизонтальному полі зору з його
        /// кутом) — <see cref="RingVenue.PostInView"/>. Такий кадр відкидаємо: далі інший курс чи нахил.
        /// </summary>
        private static bool PostInFront(RingVenue venue, Shot s, Vector3 pairCenter, float aspect)
        {
            Vector3 f = s.Look - s.Pos;
            float halfH = Mathf.Atan(Mathf.Tan(0.5f * s.Fov * Mathf.Deg2Rad) * aspect) * Mathf.Rad2Deg;
            return venue.PostInView(s.Pos.x, s.Pos.z, f.x, f.z, halfH, Flat(pairCenter - s.Pos));
        }

        private static readonly float[] PerchYaws = { 30f, -30f, 45f, -45f, 15f, -15f, 60f, -60f, 0f };
        private static readonly float[] PerchPitch = { -8f, -14f, -20f, -3f };
        private static readonly float[] PerchFovs = { 44f, 50f, 56f, 62f };

        /// <summary>
        /// План «на канаті / на куті» (склейка, <see cref="StageCam.Low"/>): камера низько в рингу, знизу вгору — нападник
        /// угорі (на канаті, на куті, на верхньому канаті), жертва в кадрі. Камера за жертвою з боку (курс від жертви до
        /// нападника ±15–60°), у рингу (до краю квадрата − 0,3 м), на висоті 0,35–1,2 м над матом, не ближче 0,6 м до
        /// постатей. <paramref name="pts"/> — тіла обох (голова нападника — верхня межа кадру <paramref name="top"/>).
        /// </summary>
        public static Shot PerchShot(IList<Vector3> pts, Vector3 attacker, Vector3 victim, float aspect,
            Func<Vector3, bool> allowed = null, float top = 0.12f, IList<float> tops = null)
        {
            if (allowed == null)
            {
                float lim = RingStage3D.RingHalf - 0.3f, mat = RingStage3D.MatY;
                allowed = p => Mathf.Abs(p.x) <= lim && Mathf.Abs(p.z) <= lim && p.y >= mat + 0.35f && p.y <= mat + 1.2f
                    && Flat(p - attacker) >= 0.6f && Flat(p - victim) >= 0.6f;
            }
            Vector3 d = attacker - victim;
            d.y = 0f;
            float baseYaw = d.sqrMagnitude > 1e-6f ? Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg : 0f;
            var s = new Shot();
            foreach (float dy in PerchYaws)
                foreach (float pitch in PerchPitch)
                {
                    s = FrameAny(pts, baseYaw + dy, pitch, PerchFovs, aspect, top, 0.92f, 0.06f, allowed, 0.8f, 5f, tops);
                    if (s.Ok) return s;
                }
            return s;
        }

        private static float Flat(Vector3 v) { return new Vector2(v.x, v.z).magnitude; }

        /// <summary>Точки постаті для кадру: стопи, коліна, таз, голова й над головою (руки вгору) — з поправкою на зріст.</summary>
        public static void Body(List<Vector3> into, Vector3 groundPos, float scale, bool armsUp = false, bool knees = true)
        {
            float s = Mathf.Max(0.5f, scale);
            if (knees) into.Add(groundPos + Vector3.up * (0.5f * s));
            else into.Add(groundPos);
            into.Add(groundPos + Vector3.up * (1.0f * s));
            into.Add(groundPos + Vector3.up * ((armsUp ? 2.35f : 1.95f) * s));
        }
    }
}
