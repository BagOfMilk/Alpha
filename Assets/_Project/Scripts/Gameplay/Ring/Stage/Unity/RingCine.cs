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
        // перший ряд), далі вздовж боку, далі трохи до рингу (за парою фартух).
        private static readonly float[] OutsideAngles = { 70f, -70f, 82f, -82f, 95f, -95f, 58f, -58f, 110f, -110f, 125f, -125f };
        private static readonly float[] OutsidePitch = { 14f, 20f, 26f, 32f };
        private static readonly float[] OutsideFovs = { 40f, 46f, 52f, 58f };

        /// <summary>
        /// План «за рингом»: пара на підлозі між фартухом і огорожею. Камера — в коридорі довкола рингу (не в рингу, не над
        /// платформою, не за огорожею в трибунах, не в штативі світла: <see cref="RingVenue.OutsideCameraOk"/> залу
        /// <see cref="RingStage3D.Venue"/>), на висоті 1,2–3 м, з нахилом униз; у кадрі — <paramref name="pts"/> (тіла пари;
        /// додай точку краю фартуха чи реквізиту, щоб і вони лягли в кадр). <paramref name="pairCenter"/> — центр пари на
        /// підлозі, <paramref name="outward"/> — назовні від рингу (<see cref="RingStage3D.OutsideOut"/>).
        /// Перебір: курс (кут від «назовні», спершу з огорожею за парою), нахил, кут огляду; перший кадр, що вміщає все.
        /// </summary>
        public static Shot OutsideShot(IList<Vector3> pts, Vector3 pairCenter, Vector3 outward, float aspect,
            Func<Vector3, bool> allowed = null, IList<float> tops = null)
        {
            var venue = RingStage3D.Venue;
            if (allowed == null) allowed = p => venue.OutsideCameraOk(p.x, p.y, p.z, pairCenter.x, pairCenter.z);
            outward.y = 0f;
            if (outward.sqrMagnitude < 1e-6f) outward = Vector3.right;
            outward.Normalize();
            var along = new Vector3(-outward.z, 0f, outward.x);
            var s = new Shot();
            foreach (float th in OutsideAngles)
            {
                float r = th * Mathf.Deg2Rad;
                // Камера дивиться в цьому напрямку; стоїть — по інший бік пари.
                Vector3 look = outward * Mathf.Cos(r) + along * Mathf.Sin(r);
                float yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
                foreach (float pitch in OutsidePitch)
                {
                    s = FrameAny(pts, yaw, pitch, OutsideFovs, aspect, 0.2f, 0.86f, 0.08f, allowed, 1.2f, 7f, tops);
                    if (s.Ok) return s;
                }
            }
            return s;
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
