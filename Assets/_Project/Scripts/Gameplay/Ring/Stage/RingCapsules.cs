using System;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>Точка в метрах сцени (власна, щоб геометрія не залежала від рушія й тестувалась без Unity).</summary>
    public struct CapPoint
    {
        public double X, Y, Z;
        public CapPoint(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static CapPoint operator -(CapPoint a, CapPoint b) { return new CapPoint(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static CapPoint operator +(CapPoint a, CapPoint b) { return new CapPoint(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static CapPoint operator *(CapPoint a, double k) { return new CapPoint(a.X * k, a.Y * k, a.Z * k); }
        public static double Dot(CapPoint a, CapPoint b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }
        public double Length { get { return Math.Sqrt(Dot(this, this)); } }
    }

    /// <summary>
    /// Геометрія вимірювача контакту (QA, критерій М1 №4 «взаємопроникнення &lt; 3 см», §11.2): тіло — набір капсул
    /// (відрізок між двома кістками + радіус). Чиста математика: найкоротша відстань відрізок–відрізок і глибина
    /// проникнення двох капсул. Читає <c>RingContactWatch</c>; охоронці — <c>RingCapsuleTests</c>. На гру не впливає.
    /// </summary>
    public static class RingCapsules
    {
        private const double Eps = 1e-12;

        /// <summary>
        /// Найкоротша відстань між відрізками [p1,q1] і [p2,q2] (класичний розв'язок через параметри s,t ∈ [0,1] з
        /// обрізанням; вироджені відрізки-точки враховано).
        /// </summary>
        public static double SegmentDistance(CapPoint p1, CapPoint q1, CapPoint p2, CapPoint q2)
        {
            CapPoint d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            double a = CapPoint.Dot(d1, d1), e = CapPoint.Dot(d2, d2), f = CapPoint.Dot(d2, r);
            double s, t;
            if (a <= Eps && e <= Eps) return r.Length;
            if (a <= Eps)
            {
                s = 0; t = Clamp01(f / e);
            }
            else
            {
                double c = CapPoint.Dot(d1, r);
                if (e <= Eps) { t = 0; s = Clamp01(-c / a); }
                else
                {
                    double b = CapPoint.Dot(d1, d2), denom = a * e - b * b;
                    s = denom > Eps ? Clamp01((b * f - c * e) / denom) : 0;
                    t = (b * s + f) / e;
                    if (t < 0) { t = 0; s = Clamp01(-c / a); }
                    else if (t > 1) { t = 1; s = Clamp01((b - c) / a); }
                }
            }
            CapPoint c1 = p1 + d1 * s, c2 = p2 + d2 * t;
            return (c1 - c2).Length;
        }

        /// <summary>Найкоротша відстань від точки до відрізка (кисть до тулуба — зазор контакту).</summary>
        public static double PointSegmentDistance(CapPoint p, CapPoint a, CapPoint b)
        {
            return SegmentDistance(p, p, a, b);
        }

        /// <summary>
        /// Глибина проникнення двох капсул, м: r1 + r2 − відстань між осями. Додатна — капсули входять одна в одну на
        /// стільки; нуль чи від'ємна — не торкаються (від'ємна = зазор).
        /// </summary>
        public static double Penetration(CapPoint p1, CapPoint q1, double r1, CapPoint p2, CapPoint q2, double r2)
        {
            return r1 + r2 - SegmentDistance(p1, q1, p2, q2);
        }

        private static double Clamp01(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }
    }
}
