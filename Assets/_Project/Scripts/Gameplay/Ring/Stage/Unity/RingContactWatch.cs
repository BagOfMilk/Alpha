using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Вимірювач контакту тіл (QA «Скилы», критерій М1 №4: «взаємопроникнення &lt; 3 см», §11.2; зона — ZONES.md, «План
    /// кадрів приймання М1»). Щокадру, після анімації (поруч із <see cref="RingStageWatch"/>), будує з кісток Humanoid кожної
    /// постаті капсули (тулуб, шия, голова, плечі, передпліччя, стегна, гомілки) і рахує:
    /// <list type="bullet">
    /// <item>проникнення — капсули двох різних постатей входять одна в одну глибше за 3 см (борець–борець, борець–рефері);
    /// у звіт — пара постатей, пара кісток, кліп і мить кліпа;</item>
    /// <item>зазор контакту — на кліпах накриття, больового й захвату найменша відстань від кисті атакувального (роль A) до
    /// поверхні тіла суперника за весь кліп; понад 5 см — «без контакту».</item>
    /// </list>
    /// Радіуси — для тіла ~1,8 м, множаться на масштаб тіла (довжина таз→голова на першому кадрі проти 0,60 м рига). Геометрія —
    /// <see cref="RingCapsules"/> (тести без Unity). Суто перевірка: на гру не впливає. Не лінтується (вектори рушія).
    /// </summary>
    public sealed class RingContactWatch
    {
        private const double PenLimit = 0.03, GapLimit = 0.05;
        private const float RigTorso = 0.60f;

        private struct Seg
        {
            public HumanBodyBones From, To;
            public float Radius;
            public string Name;
            public Seg(HumanBodyBones f, HumanBodyBones t, float r, string n) { From = f; To = t; Radius = r; Name = n; }
        }

        private static readonly Seg[] Body =
        {
            new Seg(HumanBodyBones.Hips, HumanBodyBones.Chest, 0.14f, "тулуб"),
            new Seg(HumanBodyBones.Chest, HumanBodyBones.Neck, 0.15f, "груди"),
            new Seg(HumanBodyBones.Neck, HumanBodyBones.Head, 0.06f, "шия"),
            new Seg(HumanBodyBones.Head, HumanBodyBones.Head, 0.11f, "голова"),
            new Seg(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, 0.055f, "ліве плече"),
            new Seg(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 0.045f, "ліве передпліччя"),
            new Seg(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, 0.055f, "праве плече"),
            new Seg(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 0.045f, "праве передпліччя"),
            new Seg(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, 0.08f, "ліве стегно"),
            new Seg(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 0.055f, "ліва гомілка"),
            new Seg(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 0.08f, "праве стегно"),
            new Seg(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 0.055f, "права гомілка"),
        };

        /// <summary>Кліпи, де кисть атакувального мусить лежати на тілі: накриття, больові, захвати.</summary>
        private static readonly string[] ContactClips = { "Cover", "Pin", "Submission", "Hold", "Lock", "Clamp", "Headlock", "Lockup", "Armbar", "Choke" };

        private sealed class Body3
        {
            public float Scale;
            public readonly Vector3[] A = new Vector3[Body.Length], B = new Vector3[Body.Length];
            public readonly float[] R = new float[Body.Length];
            public readonly bool[] Ok = new bool[Body.Length];
            public Vector3 HandL, HandR;
            public bool HasHands;
            // зазор поточного контактного кліпа
            public string GapClip;
            public double GapBest = double.MaxValue;
        }

        private readonly Dictionary<RingActor, Body3> _bodies = new Dictionary<RingActor, Body3>();
        private readonly List<KeyValuePair<double, string>> _worst = new List<KeyValuePair<double, string>>();
        private readonly Dictionary<string, int> _penByClip = new Dictionary<string, int>();
        public int Frames, PenFrames, ContactClipsSeen, NoContact;
        public double WorstPen;
        private int _logged;

        public void Reset()
        {
            _bodies.Clear(); _worst.Clear(); _penByClip.Clear();
            Frames = PenFrames = ContactClipsSeen = NoContact = 0;
            WorstPen = 0; _logged = 0;
        }

        public void Tick(IList<RingActor> actors)
        {
            Frames++;
            for (int i = 0; i < actors.Count; i++) if (actors[i] != null) Build(actors[i]);
            for (int i = 0; i < actors.Count; i++)
            for (int j = i + 1; j < actors.Count; j++)
            {
                var a = actors[i]; var b = actors[j];
                if (a == null || b == null) continue;
                Body3 ba, bb;
                if (!_bodies.TryGetValue(a, out ba) || !_bodies.TryGetValue(b, out bb)) continue;
                Penetration(a, ba, b, bb);
            }
            for (int i = 0; i < actors.Count; i++)
            {
                var a = actors[i];
                if (a == null) continue;
                RingActor victim = null;
                for (int j = 0; j < actors.Count; j++)
                    if (j != i && actors[j] != null && IsWrestlerPartner(a, actors[j])) { victim = actors[j]; break; }
                Gap(a, victim);
            }
        }

        private void Build(RingActor a)
        {
            Body3 b;
            if (!_bodies.TryGetValue(a, out b)) { b = new Body3(); _bodies[a] = b; }
            if (b.Scale <= 0f)
            {
                var h = a.GetBone(HumanBodyBones.Hips); var hd = a.GetBone(HumanBodyBones.Head);
                b.Scale = h != null && hd != null ? Mathf.Clamp(Vector3.Distance(h.position, hd.position) / RigTorso, 0.8f, 1.25f) : 1f;
            }
            for (int k = 0; k < Body.Length; k++)
            {
                var f = a.GetBone(Body[k].From); var t = a.GetBone(Body[k].To);
                b.Ok[k] = f != null && t != null;
                if (!b.Ok[k]) continue;
                b.A[k] = f.position; b.B[k] = t.position;
                if (Body[k].From == HumanBodyBones.Head) b.A[k] = b.B[k] = f.position + f.up * 0.09f * b.Scale; // центр черепа
                b.R[k] = Body[k].Radius * b.Scale;
            }
            var hl = a.GetBone(HumanBodyBones.LeftHand); var hr = a.GetBone(HumanBodyBones.RightHand);
            b.HasHands = hl != null && hr != null;
            if (b.HasHands) { b.HandL = hl.position; b.HandR = hr.position; }
        }

        private void Penetration(RingActor a, Body3 ba, RingActor b, Body3 bb)
        {
            double worst = 0; int wi = -1, wj = -1;
            for (int i = 0; i < Body.Length; i++)
            {
                if (!ba.Ok[i]) continue;
                for (int j = 0; j < Body.Length; j++)
                {
                    if (!bb.Ok[j]) continue;
                    double p = RingCapsules.Penetration(P(ba.A[i]), P(ba.B[i]), ba.R[i], P(bb.A[j]), P(bb.B[j]), bb.R[j]);
                    if (p > worst) { worst = p; wi = i; wj = j; }
                }
            }
            if (worst <= PenLimit) return;
            PenFrames++;
            if (worst > WorstPen) WorstPen = worst;
            string key = Clip(a) + " / " + Clip(b);
            int n;
            _penByClip.TryGetValue(key, out n);
            _penByClip[key] = n + 1;
            if (n == 0) Note(worst, a.Name + " (" + Body[wi].Name + ") у " + b.Name + " (" + Body[wj].Name + ") на " + Cm(worst) + " — " + ClipAt(a) + " / " + ClipAt(b));
        }

        private void Gap(RingActor a, RingActor victim)
        {
            Body3 ba;
            if (!_bodies.TryGetValue(a, out ba)) return;
            var c = a.CurrentClip;
            string clip = c != null && IsContactClip(c.name) && c.name.EndsWith("_A", System.StringComparison.Ordinal) ? c.name : null;
            if (clip != ba.GapClip)
            {
                Finish(a, ba);
                ba.GapClip = clip;
                ba.GapBest = double.MaxValue;
                if (clip != null) ContactClipsSeen++;
            }
            Body3 bv;
            if (clip == null || victim == null || !ba.HasHands || !_bodies.TryGetValue(victim, out bv)) return;
            for (int k = 0; k < Body.Length; k++)
            {
                if (!bv.Ok[k]) continue;
                double dl = RingCapsules.PointSegmentDistance(P(ba.HandL), P(bv.A[k]), P(bv.B[k])) - bv.R[k];
                double dr = RingCapsules.PointSegmentDistance(P(ba.HandR), P(bv.A[k]), P(bv.B[k])) - bv.R[k];
                double d = dl < dr ? dl : dr;
                if (d < ba.GapBest) ba.GapBest = d;
            }
        }

        private void Finish(RingActor a, Body3 b)
        {
            if (b.GapClip == null || b.GapBest == double.MaxValue) return;
            if (b.GapBest > GapLimit)
            {
                NoContact++;
                Note(b.GapBest, a.Name + ": без контакту — кисть найближче " + Cm(b.GapBest) + " від суперника за весь " + b.GapClip);
            }
        }

        private static bool IsWrestlerPartner(RingActor a, RingActor other)
        {
            // рефері — не жертва прийому; ім'я постаті рефері містить «Рефері» (RingStage3D)
            return other.Name == null || other.Name.IndexOf("Рефері", System.StringComparison.Ordinal) < 0;
        }

        private static bool IsContactClip(string name)
        {
            for (int i = 0; i < ContactClips.Length; i++)
                if (name.IndexOf(ContactClips[i], System.StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private static CapPoint P(Vector3 v) { return new CapPoint(v.x, v.y, v.z); }
        private static string Cm(double m) { return (m * 100).ToString("0.0", CultureInfo.InvariantCulture) + " см"; }
        private static string Clip(RingActor a) { var c = a.CurrentClip; return c != null ? c.name : "—"; }
        private static string ClipAt(RingActor a)
        {
            var c = a.CurrentClip;
            return c != null ? c.name + " @" + a.ClipTime.ToString("0.00", CultureInfo.InvariantCulture) : "—";
        }

        private void Note(double weight, string line)
        {
            if (_logged++ < 60) Debug.Log("[Ринг] контакт: " + line);
            _worst.Add(new KeyValuePair<double, string>(weight, line));
        }

        public string Report()
        {
            foreach (var kv in _bodies) { Finish(kv.Key, kv.Value); kv.Value.GapClip = null; }
            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "[Ринг] контакт: {0} кадрів; проникнення понад {1:0} см — {2} кадрів у {3} парах кліпів (найгірше {4:0.0} см); " +
                "контактних кліпів {5}, без контакту (кисть далі {6:0} см) — {7}",
                Frames, PenLimit * 100, PenFrames, _penByClip.Count, WorstPen * 100, ContactClipsSeen, GapLimit * 100, NoContact);
            _worst.Sort((x, y) => y.Key.CompareTo(x.Key));
            for (int i = 0; i < _worst.Count && i < 12; i++) sb.Append("\n    ").Append(_worst[i].Value);
            var top = new List<KeyValuePair<string, int>>(_penByClip);
            top.Sort((x, y) => y.Value.CompareTo(x.Value));
            for (int i = 0; i < top.Count && i < 8; i++)
                sb.Append("\n    кадрів проникнення ").Append(top[i].Value).Append(": ").Append(top[i].Key);
            return sb.ToString();
        }
    }
}
