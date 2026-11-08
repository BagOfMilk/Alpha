using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Фаз-тест сцени матчу (пакет «Відео огляд» 08.11.2026, п. 8: «Немає проходу крізь канати, ковзання стоп і
    /// телепортів (фаз-тест + кадри очима)»). Щокадру — після анімації й вирівнювання слотів, тобто те, що піде на екран, —
    /// дивиться на кожну постать і рахує:
    /// <list type="bullet">
    /// <item>телепорт — таз за кадр зрушив більше ніж на 0,15 м зі швидкістю понад 9 м/с ігрового часу (найшвидший біг
    /// кліпів — ~6 м/с); окремо — ті, що сталися в кадр склейки камери (їх не видно);</item>
    /// <item>перетин канатів — таз перейшов лінію канатів (з запасом 0,45 м на прогин) нижче верхнього каната;</item>
    /// <item>ковзання стопи — носок стоїть на маті (у межах 3 см від бінд-висоти) і повзе (повільніше за 1 м/с), а за час стояння зрушив більше ніж на
    /// 8 см.</item>
    /// </list>
    /// Кожне порушення — з кліпом і миттю кліпа; підсумок — у звіті огляду (<see cref="RingStage3D.PlayedReport"/>). Суто
    /// перевірка: на гру не впливає. Не лінтується (вектори рушія).
    /// </summary>
    public sealed class RingStageWatch
    {
        private const float TeleportStep = 0.15f, TeleportSpeed = 9f;
        private const float RopeSlack = 0.45f, TopRope = 1.31f;
        private const float PlantLift = 0.03f, SlideLimit = 0.08f;

        private sealed class Track
        {
            public Vector3 Hip;
            public bool Has;
            public readonly Vector3[] Foot = new Vector3[2];
            public readonly bool[] Planted = new bool[2];
            public readonly float[] Drift = new float[2];
            public readonly string[] DriftClip = new string[2];
        }

        private readonly Dictionary<RingActor, Track> _tracks = new Dictionary<RingActor, Track>();
        private readonly List<KeyValuePair<float, string>> _worst = new List<KeyValuePair<float, string>>();
        public int Frames, Teleports, TeleportsUnderCut, RopeCrossings, FootSlides;
        public float WorstSlide;
        private int _logged;

        /// <summary>Лінія канатів від центру, м (як у режисера: RingHalf + 0,10).</summary>
        private static float RopeLine { get { return RingStage3D.RingHalf + 0.10f; } }

        public void Reset()
        {
            _tracks.Clear();
            _worst.Clear();
            Frames = Teleports = TeleportsUnderCut = RopeCrossings = FootSlides = 0;
            WorstSlide = 0f;
            _logged = 0;
        }

        /// <summary>Після руху постатей між подіями (ковзання на місце) — не телепорт: нова точка відліку.</summary>
        public void Forget(RingActor a)
        {
            Track t;
            if (a != null && _tracks.TryGetValue(a, out t)) t.Has = false;
        }

        public void Tick(IList<RingActor> actors, float dt, bool cut)
        {
            if (dt <= 0f) return;
            Frames++;
            for (int i = 0; i < actors.Count; i++)
            {
                var a = actors[i];
                if (a == null || a.Hips == null) continue;
                Track t;
                if (!_tracks.TryGetValue(a, out t)) { t = new Track(); _tracks[a] = t; }
                Vector3 hip = a.PosePos;
                float hy = a.Hips.position.y - RingStage3D.MatY;
                if (t.Has)
                {
                    float step = new Vector2(hip.x - t.Hip.x, hip.z - t.Hip.z).magnitude;
                    if (step > TeleportStep && step / dt > TeleportSpeed)
                    {
                        if (cut) TeleportsUnderCut++;
                        else { Teleports++; Note(step, a, "телепорт " + F(step) + " м з (" + F(t.Hip.x) + "," + F(t.Hip.z) + ") у (" + F(hip.x) + "," + F(hip.z) + ")", true); }
                    }
                    bool wasIn = Inside(t.Hip), isIn = Inside(hip);
                    if (wasIn != isIn && hy < TopRope)
                    {
                        RopeCrossings++;
                        Note(0f, a, (isIn ? "у ринг" : "з рингу") + " крізь канати, таз " + F(hy) + " м над матом", true);
                    }
                }
                t.Hip = hip;
                t.Has = true;
                for (int f = 0; f < 2; f++)
                {
                    Vector3 p;
                    float lift = a.FootLift(f, out p);
                    bool planted = lift < PlantLift;
                    if (planted && t.Planted[f])
                    {
                        // Ковзання — повільний рух стопи на маті (до 1 м/с): швидкий низький виніс — це крок кліпа (стопа
                        // рефері в ході піднімається лише на 2–3 см), а не ковзання.
                        float st = new Vector2(p.x - t.Foot[f].x, p.z - t.Foot[f].z).magnitude;
                        if (st / dt < 1f) t.Drift[f] += st;
                        if (t.DriftClip[f] == null) t.DriftClip[f] = Clip(a);
                    }
                    else if (!planted && t.Planted[f]) EndPlant(a, t, f);
                    if (planted && !t.Planted[f]) { t.Drift[f] = 0f; t.DriftClip[f] = Clip(a); }
                    t.Planted[f] = planted;
                    t.Foot[f] = p;
                }
            }
        }

        private void EndPlant(RingActor a, Track t, int f)
        {
            float d = t.Drift[f];
            t.Drift[f] = 0f;
            if (d <= SlideLimit) return;
            FootSlides++;
            WorstSlide = Mathf.Max(WorstSlide, d);
            Note(d, a, (f == 0 ? "ліва" : "права") + " стопа ковзнула " + F(d) + " м (стояла в " + t.DriftClip[f] + ")");
        }

        private static bool Inside(Vector3 p)
        {
            float lim = RopeLine + RopeSlack;
            return Mathf.Abs(p.x) < lim && Mathf.Abs(p.z) < lim;
        }

        private static string Clip(RingActor a)
        {
            var c = a.CurrentClip;
            return c != null ? c.name + " @" + a.ClipTime.ToString("0.00", CultureInfo.InvariantCulture) : "—";
        }

        private static string F(float v) { return v.ToString("0.00", CultureInfo.InvariantCulture); }

        private void Note(float weight, RingActor a, string what, bool always = false)
        {
            string line = a.Name + ": " + what + " — " + Clip(a);
            if (always || _logged++ < 60) Debug.Log("[Ринг] фаз-тест: " + line);
            _worst.Add(new KeyValuePair<float, string>(weight, line));
        }

        public string Report()
        {
            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "[Ринг] фаз-тест: {0} кадрів; телепорти на очах {1} (під склейкою {2}); перетини канатів нижче верхнього {3}; " +
                "ковзання стоп понад {4:0.00} м — {5} (найгірше {6:0.00} м)",
                Frames, Teleports, TeleportsUnderCut, RopeCrossings, SlideLimit, FootSlides, WorstSlide);
            _worst.Sort((x, y) => y.Key.CompareTo(x.Key));
            for (int i = 0; i < _worst.Count && i < 12; i++) sb.Append("\n    ").Append(_worst[i].Value);
            return sb.ToString();
        }
    }
}
