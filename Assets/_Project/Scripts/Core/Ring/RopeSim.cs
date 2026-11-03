using System;
using System.Collections.Generic;

namespace Game.Core.Ring
{
    /// <summary>Тривимірний вектор канату (метри; Z — угору, як у генераторі анімацій). Свій, бо ядро без рушія.</summary>
    public struct RopeVec
    {
        public float X, Y, Z;

        public RopeVec(float x, float y, float z) { X = x; Y = y; Z = z; }

        public static RopeVec operator +(RopeVec a, RopeVec b) => new RopeVec(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static RopeVec operator -(RopeVec a, RopeVec b) => new RopeVec(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static RopeVec operator *(RopeVec a, float k) => new RopeVec(a.X * k, a.Y * k, a.Z * k);
        public static float Dot(RopeVec a, RopeVec b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public float Length => (float)Math.Sqrt(Dot(this, this));
        public static RopeVec Lerp(RopeVec a, RopeVec b, float k) => a + (b - a) * k;
    }

    /// <summary>
    /// Капсула тіла борця, що штовхає канат. Тулуб задається ПОПЕРЕЧНИМИ капсулами (лінія таза, лінія плечей між
    /// кістками стегон і плечей), а не кістками хребта: вузьку вертикальну капсулу канат просто обтікає збоку (тест).
    /// Руки й ноги — капсули своїх кісток.
    /// </summary>
    public struct RopeCapsule
    {
        public RopeVec A, B;
        public float Radius;

        public RopeCapsule(RopeVec a, RopeVec b, float radius) { A = a; B = b; Radius = radius; }
    }

    /// <summary>
    /// Параметри канату — ПЛЕЙСХОЛДЕР (власник, 29.10.2026: «Канати мають мати фізику»). Дешевий режим (Статут PERF-01):
    /// <see cref="Low"/> — менше точок і проходів.
    /// </summary>
    public sealed class RopeSettings
    {
        public int Points = 16;                 // точок на проліт між стійками, разом з кінцями
        public int Iterations = 6;              // проходів обмежень за крок
        public float FixedDt = 1f / 60f;        // с — крок фіксований: однаковий вхід дає однаковий канат
        public float Damping = 0.97f;           // частка швидкості, що лишається за крок (заспокоюється за ~1,5 с)
        public float Gravity = 9.81f;
        public float Home = 600f;               // 1/с² — натяг: пружина до прямої між стійками (провис g/Home ≈ 1,6 см)
        public float Stretch = 0.12f;           // найбільше видовження прольоту (частка): далі канат не піддається
        public float Contact = 0.004f;          // м — запас між канатом і тілом

        public static RopeSettings Low() => new RopeSettings { Points = 8, Iterations = 3 };
    }

    /// <summary>
    /// Канат рингу між двома стійками: ланцюжок точок Верле, кінці закріплені, натяг тягне точки до прямої, тіла
    /// борців (капсули кісток) виштовхують точки назовні. Суто вигляд: ядро гри канат не читає, детермінізм бою не
    /// залежить від нього. Удар у канат з кліпу (мітка rope_hit у ring_anims_events.txt) — <see cref="Hit"/>.
    /// </summary>
    public sealed class RopeSim
    {
        private readonly RopeSettings _s;
        private readonly RopeVec[] _p, _prev, _home;
        private readonly float _segment;        // найбільша довжина відрізка (з урахуванням видовження)
        private readonly RopeVec _out;          // одиничний напрямок «з рингу назовні», поперек каната

        /// <param name="outward">Напрямок з рингу назовні (поперек каната, горизонтально). Тіло гне канат уздовж
        /// нього: радіальне виштовхування давало канату прослизати над або під капсулою (тест).</param>
        public RopeSim(RopeVec start, RopeVec end, RopeVec outward, RopeSettings settings = null)
        {
            _s = settings ?? new RopeSettings();
            var ol = outward.Length;
            if (ol < 1e-6f) throw new ArgumentException("Потрібен напрямок назовні.");
            _out = outward * (1f / ol);
            if (_s.Points < 3) throw new ArgumentException("Канат має мати щонайменше три точки.");
            _p = new RopeVec[_s.Points];
            _prev = new RopeVec[_s.Points];
            _home = new RopeVec[_s.Points];
            for (var i = 0; i < _s.Points; i++)
            {
                var k = i / (float)(_s.Points - 1);
                _home[i] = _p[i] = _prev[i] = RopeVec.Lerp(start, end, k);
            }
            _segment = (end - start).Length / (_s.Points - 1) * (1f + _s.Stretch);
        }

        public int Count => _p.Length;
        public RopeVec this[int i] => _p[i];

        /// <summary>Найбільше відхилення точки від прямої між стійками (м).</summary>
        public float MaxDeflection()
        {
            var best = 0f;
            for (var i = 0; i < _p.Length; i++) best = Math.Max(best, (_p[i] - _home[i]).Length);
            return best;
        }

        /// <summary>Поштовх у точці прольоту (0..1 від стійки до стійки): швидкість, розподілена на сусідні точки.</summary>
        public void Hit(float along, RopeVec velocity)
        {
            var c = along * (_p.Length - 1);
            for (var i = 1; i < _p.Length - 1; i++)
            {
                var w = Math.Max(0f, 1f - Math.Abs(i - c) / 2.5f);
                _prev[i] = _prev[i] - velocity * (w * _s.FixedDt);
            }
        }

        /// <summary>Один фіксований крок: інерція, натяг, гравітація, тіла, довжина прольоту.</summary>
        public void Step(IReadOnlyList<RopeCapsule> bodies)
        {
            var dt2 = _s.FixedDt * _s.FixedDt;
            for (var i = 1; i < _p.Length - 1; i++)
            {
                var v = (_p[i] - _prev[i]) * _s.Damping;
                var acc = (_home[i] - _p[i]) * _s.Home + new RopeVec(0f, 0f, -_s.Gravity);
                _prev[i] = _p[i];
                _p[i] = _p[i] + v + acc * dt2;
            }
            for (var it = 0; it < _s.Iterations; it++)
            {
                if (bodies != null)
                    for (var b = 0; b < bodies.Count; b++) { PushOut(bodies[b]); PushSegments(bodies[b]); }
                Constrain();
            }
            if (bodies != null)
                for (var b = 0; b < bodies.Count; b++) { PushSegments(bodies[b]); PushOut(bodies[b]); }   // тіло — останнє слово
        }

        private void Constrain()
        {
            // відрізок не довший за допустимий: канат тягнеться лише до межі видовження, кінці не рухаються
            for (var i = 0; i < _p.Length - 1; i++)
            {
                var d = _p[i + 1] - _p[i];
                var len = d.Length;
                if (len <= _segment || len < 1e-6f) continue;
                var fix = d * ((len - _segment) / len);
                var a = i == 0 ? 0f : (i + 1 == _p.Length - 1 ? 1f : 0.5f);
                var b = i + 1 == _p.Length - 1 ? 0f : (i == 0 ? 1f : 0.5f);
                _p[i] = _p[i] + fix * a;
                _p[i + 1] = _p[i + 1] - fix * b;
            }
        }

        private void PushOut(RopeCapsule c)
        {
            var r = c.Radius + _s.Contact;
            for (var i = 1; i < _p.Length - 1; i++)
            {
                var q = Closest(c, _p[i]);
                var dist = (_p[i] - q).Length;
                if (dist >= r) continue;
                // тіло з боку рингу — канат іде назовні; тіло ззовні (з фартуха) — всередину
                var dir = RopeVec.Dot(_p[i] - q, _out) >= 0f ? _out : _out * -1f;
                for (var k = 0; k < 4 && dist < r; k++)
                {
                    _p[i] = _p[i] + dir * (r - dist + 1e-4f);
                    dist = (_p[i] - Closest(c, _p[i])).Length;
                }
            }
        }

        /// <summary>Відрізки між точками: у дешевому режимі точки рідкі, і тіло пролазило між ними.</summary>
        private void PushSegments(RopeCapsule c)
        {
            var r = c.Radius + _s.Contact;
            for (var i = 0; i < _p.Length - 1; i++)
            {
                float u;
                var q = SegSeg(_p[i], _p[i + 1], c.A, c.B, out u);
                var pt = RopeVec.Lerp(_p[i], _p[i + 1], u);
                var dist = (pt - q).Length;
                if (dist >= r) continue;
                var dir = RopeVec.Dot(pt - q, _out) >= 0f ? _out : _out * -1f;
                var push = dir * (r - dist + 1e-4f);
                // зсув розподілено на кінці відрізка так, щоб точка контакту зсунулась на push; стійки не рухаються
                var wa = i == 0 ? 0f : 1f - u;
                var wb = i + 1 == _p.Length - 1 ? 0f : u;
                var norm = wa * wa + wb * wb;
                if (norm < 1e-6f) continue;
                _p[i] = _p[i] + push * (wa / norm);
                _p[i + 1] = _p[i + 1] + push * (wb / norm);
            }
        }

        /// <summary>Найближчі точки двох відрізків: повертає точку на осі капсули, u — параметр на відрізку каната.</summary>
        private static RopeVec SegSeg(RopeVec p1, RopeVec q1, RopeVec p2, RopeVec q2, out float u)
        {
            var d1 = q1 - p1; var d2 = q2 - p2; var r = p1 - p2;
            float a = RopeVec.Dot(d1, d1), e = RopeVec.Dot(d2, d2), f = RopeVec.Dot(d2, r);
            float s, t;
            if (e < 1e-9f)
            {
                t = 0f;
                s = a > 1e-9f ? Math.Max(0f, Math.Min(1f, -RopeVec.Dot(d1, r) / a)) : 0f;
            }
            else
            {
                var c = RopeVec.Dot(d1, r);
                var b = RopeVec.Dot(d1, d2);
                var den = a * e - b * b;
                s = den > 1e-9f ? Math.Max(0f, Math.Min(1f, (b * f - c * e) / den)) : 0f;
                t = (b * s + f) / e;
                if (t < 0f) { t = 0f; s = a > 1e-9f ? Math.Max(0f, Math.Min(1f, -c / a)) : 0f; }
                else if (t > 1f) { t = 1f; s = a > 1e-9f ? Math.Max(0f, Math.Min(1f, (b - c) / a)) : 0f; }
            }
            u = s;
            return p2 + d2 * t;
        }

        private static RopeVec Closest(RopeCapsule c, RopeVec p)
        {
            var ab = c.B - c.A;
            var len2 = RopeVec.Dot(ab, ab);
            var t = len2 > 1e-9f ? Math.Max(0f, Math.Min(1f, RopeVec.Dot(p - c.A, ab) / len2)) : 0f;
            return c.A + ab * t;
        }
    }
}
