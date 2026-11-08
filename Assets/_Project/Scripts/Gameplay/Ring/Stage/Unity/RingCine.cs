using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Розкладка кіноплану (фінішер, перемога): камера з заданим курсом і нахилом, а важливі точки (голови, руки, коліна,
    /// контакт) мусять лягти в смугу кадру: нижче зони виносок (Статут FX-05: слово — у верхніх 8–22 % кадру, обличчя
    /// під ним не ховаються) і вище нижньої панелі. Шукається найближча відстань, за якої всі точки в смузі й по ширині,
    /// а камера там, де їй дозволено (<paramref name="allowed"/>). Не лінтується: вектори рушія.
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
        public static Shot Frame(IList<Vector3> pts, float yawDeg, float pitchDeg, float fov, float aspect,
            float top, float bottom, float side, Func<Vector3, bool> allowed, float dMin = 1.2f, float dMax = 12f)
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
                    // (y − h) / (z·tanH) ∈ [lo, hi]  ⇔  h ∈ [y − hi·z·tanH, y − lo·z·tanH]
                    hLo = Mathf.Max(hLo, y - hi * z * tanH);
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
            float top, float bottom, float side, Func<Vector3, bool> allowed, float dMin = 1.2f, float dMax = 12f)
        {
            var s = new Shot();
            for (int i = 0; i < fovs.Length; i++)
            {
                s = Frame(pts, yawDeg, pitchDeg, fovs[i], aspect, top, bottom, side, allowed, dMin, dMax);
                s.Fov = fovs[i];
                if (s.Ok) return s;
            }
            return s;
        }

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
