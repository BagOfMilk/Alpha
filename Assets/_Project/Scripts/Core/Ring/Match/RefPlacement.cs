using System;
using System.Collections.Generic;

namespace Game.Core.Ring.Match
{
    /// <summary>
    /// Місце рефері відносно тіл борців — чиста геометрія на мату (x, z), без рушія. Огляд 8f (11.11.2026): на кіноплані фінішера
    /// рефері опинявся перед парою й закривав пів кадру; на відліку утримання він ставав колінами на стегно суперника.
    /// Тіло — відрізок (голова — таз — ступні); вимога одна: рефері не ближче за поріг до осі жодного тіла.
    /// </summary>
    public static class RefPlacement
    {
        /// <summary>Вісь тіла на мату: відрізок A–B (точка — коли A == B).</summary>
        public struct Seg
        {
            public float Ax, Az, Bx, Bz;
            public Seg(float ax, float az, float bx, float bz) { Ax = ax; Az = az; Bx = bx; Bz = bz; }
        }

        /// <summary>Найближча точка відрізка до (px, pz).</summary>
        public static void Closest(float px, float pz, Seg s, out float cx, out float cz)
        {
            float dx = s.Bx - s.Ax, dz = s.Bz - s.Az;
            float l2 = dx * dx + dz * dz;
            float t = l2 > 1e-8f ? ((px - s.Ax) * dx + (pz - s.Az) * dz) / l2 : 0f;
            t = Math.Max(0f, Math.Min(1f, t));
            cx = s.Ax + dx * t;
            cz = s.Az + dz * t;
        }

        /// <summary>Відстань від точки до найближчої осі тіла (м); без тіл — велика.</summary>
        public static float MinDist(float px, float pz, IList<Seg> bodies)
        {
            float best = float.MaxValue;
            for (int i = 0; i < bodies.Count; i++)
            {
                float cx, cz;
                Closest(px, pz, bodies[i], out cx, out cz);
                float d = (float)Math.Sqrt((px - cx) * (px - cx) + (pz - cz) * (pz - cz));
                if (d < best) best = d;
            }
            return best;
        }

        // ПЛЕЙСХОЛДЕР: глибина (м за парою від камери) і бічний зсув (м) кандидатів «за парою», від кращих до гірших.
        private static readonly float[] Depths = { 1.15f, 1.5f, 0.9f, 1.9f };
        private static readonly float[] Laterals = { 0.9f, -0.9f, 0f, 1.5f, -1.5f };

        /// <summary>Найменша глибина «за парою» (м уздовж погляду камери від середини пари), з якої рефері вважається позаду.</summary>
        public const float MinBehind = 0.3f;

        /// <summary>
        /// Місце рефері за парою відносно камери: від середини пари (<paramref name="midX"/>, <paramref name="midZ"/>) далі за
        /// напрямом погляду (<paramref name="viewX"/>, <paramref name="viewZ"/>, одиничний), у межах рингу ±<paramref name="lim"/>
        /// і не ближче за <paramref name="minClear"/> до осі жодного тіла. false — місця немає (виклик лишає як було).
        /// </summary>
        public static bool BehindPair(float midX, float midZ, float viewX, float viewZ, IList<Seg> bodies, float lim, float minClear, out float x, out float z)
        {
            float rx = viewZ, rz = -viewX;                // убік від погляду
            foreach (float d in Depths)
                foreach (float l in Laterals)
                {
                    float cx = midX + viewX * d + rx * l, cz = midZ + viewZ * d + rz * l;
                    if (Math.Abs(cx) > lim || Math.Abs(cz) > lim) continue;
                    if ((cx - midX) * viewX + (cz - midZ) * viewZ < MinBehind) continue;
                    if (MinDist(cx, cz, bodies) < minClear) continue;
                    x = cx; z = cz;
                    return true;
                }
            x = midX; z = midZ;
            return false;
        }

        /// <summary>
        /// Зсув місця рефері від тіл: і сама точка (<paramref name="x"/>, <paramref name="z"/>), і «коліна» — на
        /// <paramref name="kneeFwd"/> уздовж (<paramref name="faceX"/>, <paramref name="faceZ"/>) — мають бути не ближче за
        /// <paramref name="minClear"/> до осі тіла. Зсув — від найближчої точки тіла, у межах рингу ±<paramref name="lim"/>.
        /// true — місце вільне (після зсуву).
        /// </summary>
        public static bool PushClear(ref float x, ref float z, float faceX, float faceZ, float kneeFwd, IList<Seg> bodies, float minClear, float lim)
        {
            for (int it = 0; it < 12; it++)
            {
                bool moved = false;
                for (int k = 0; k < 2; k++)
                {
                    for (int i = 0; i < bodies.Count; i++)
                    {
                        float px = x + faceX * kneeFwd * k, pz = z + faceZ * kneeFwd * k;
                        float cx, cz;
                        Closest(px, pz, bodies[i], out cx, out cz);
                        float dx = px - cx, dz = pz - cz;
                        float d = (float)Math.Sqrt(dx * dx + dz * dz);
                        if (d >= minClear) continue;
                        if (d < 1e-4f) { dx = -faceX; dz = -faceZ; d = 1f; }   // точно на осі — назад від лиця
                        float push = minClear - d + 0.02f;
                        x += dx / d * push;
                        z += dz / d * push;
                        x = Math.Max(-lim, Math.Min(lim, x));
                        z = Math.Max(-lim, Math.Min(lim, z));
                        moved = true;
                    }
                }
                if (!moved) return true;
            }
            return MinDist(x, z, bodies) >= minClear - 1e-3f && MinDist(x + faceX * kneeFwd, z + faceZ * kneeFwd, bodies) >= minClear - 1e-3f;
        }
    }
}
