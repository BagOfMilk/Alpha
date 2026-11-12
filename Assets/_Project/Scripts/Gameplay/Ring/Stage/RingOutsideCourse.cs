using System;
using Game.Core.Ring.Match;
using Game.Gameplay.Ring.Models;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Прийом за рингом: реквізит предмета і курс пари в смузі проходу. Чистий C# без рушія — тестується окремо від режисера
    /// (<c>RingMatchDirector</c> лише подає сюди точки шляху з кліпів і застосовує вибір).
    /// </summary>
    public static class RingOutsideCourse
    {
        // ПЛЕЙСХОЛДЕР, м: запас від краю смуги до осі тіла (радіус капсули борця ≈ 0,25 м мінус допуск на лікоть) — до кадрів.
        public const float BodyMargin = 0.2f;

        // Найменша корисна півширина/пів-довжина смуги після запасу, м: вузький спортзал (півширина 0,55) не стає нулем.
        private const float MinHalf = 0.1f;

        // ============================ реквізит ============================

        /// <summary>
        /// Реквізит чистого удару предметом 10+ (стілець чи стіл): режисер чергує за номером події — детерміновано, без випадку, і
        /// той самий матч щоразу показує те саме (ПЛЕЙСХОЛДЕР до рішення про справжню чергу предметів у ядрі). Лише 10+ — для решти
        /// звук реквізит ігнорує (7–9 — стілець по дотичній, ≤6 — нападник падає сам).
        /// </summary>
        public static RingProp ChooseProp(MatchEvent ev)
        {
            if (ev == null) return RingProp.Chair;
            return (ev.Seq & 1) == 1 ? RingProp.Table : RingProp.Chair;
        }

        // ============================ курс у смузі ============================

        /// <summary>Вибір курсу: номер кандидата, зсув початку пари в осях смуги і чи вміщується шлях.</summary>
        public struct Course
        {
            public int Candidate;
            /// <summary>Зсув початку пари (точки, відносно якої задані шляхи) від центру пари: вздовж боку і назовні, м.</summary>
            public float ShiftAlong, ShiftOut;
            /// <summary>Увесь шлях у смузі. Ні — запасний варіант: склейка на загальний план.</summary>
            public bool Fits;
            /// <summary>Скільки шляху не вміщається (сума перевищень по осях), м; 0 — вміщається.</summary>
            public float Overflow;
        }

        /// <summary>
        /// Локальна точка (x вправо, z уперед від курсу <paramref name="yawDeg"/>) в осях смуги: u — уздовж боку, v — назовні.
        /// Поворот — як у рушія (Quaternion.Euler(0, yaw, 0): вперед = (sin, cos)).
        /// </summary>
        public static void ToLane(float x, float z, float yawDeg, float alongX, float alongZ, float outX, float outZ, out float u, out float v)
        {
            double r = yawDeg * Math.PI / 180.0, s = Math.Sin(r), c = Math.Cos(r);
            float wx = (float)(x * c + z * s), wz = (float)(-x * s + z * c);
            u = wx * alongX + wz * alongZ;
            v = wx * outX + wz * outZ;
        }

        /// <summary>
        /// Обрати першого кандидата, чий шлях (точки в осях смуги <paramref name="u"/>/<paramref name="v"/> для кожного кандидата,
        /// від центру пари) вміщається в смугу ±<paramref name="halfLength"/> уздовж боку і ±<paramref name="halfWidth"/> назовні
        /// (мінус <see cref="BodyMargin"/>) після зсуву: шлях центрується в смузі, тож старт сам стає «назад проти руху»
        /// (ларіат ~2,8 м уперед). Порядок кандидатів — перевага. Жоден не вміщається — запасний: той, що виходить найменше
        /// (<see cref="Course.Fits"/> = false; режисер склеює на загальний план).
        /// </summary>
        public static Course Choose(float[][] u, float[][] v, float halfLength, float halfWidth, float margin = BodyMargin)
        {
            float lu = Math.Max(MinHalf, halfLength - margin), lv = Math.Max(MinHalf, halfWidth - margin);
            var best = new Course { Candidate = 0, Overflow = float.MaxValue };
            for (int k = 0; k < u.Length; k++)
            {
                float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
                for (int i = 0; i < u[k].Length; i++)
                {
                    u0 = Math.Min(u0, u[k][i]); u1 = Math.Max(u1, u[k][i]);
                    v0 = Math.Min(v0, v[k][i]); v1 = Math.Max(v1, v[k][i]);
                }
                if (u[k].Length == 0) { u0 = u1 = v0 = v1 = 0f; }
                float over = Math.Max(0f, (u1 - u0) - 2f * lu) + Math.Max(0f, (v1 - v0) - 2f * lv);
                // Шлях центрується: початок пари зсувається на мінус середину коробки шляху.
                var c = new Course { Candidate = k, ShiftAlong = -(u0 + u1) * 0.5f, ShiftOut = -(v0 + v1) * 0.5f, Overflow = over, Fits = over <= 0f };
                if (c.Fits) return c;
                if (over < best.Overflow) best = c;
            }
            return best;
        }

        /// <summary>
        /// Найменший зсув пари, яку не повернути, — суперник лежить, де впав, або ланцюг кліпів одного запису йде далі з тих самих
        /// коренів: точки шляху (<paramref name="u"/> уздовж боку, <paramref name="v"/> назовні) — уже від центру смуги. Шлях у смузі
        /// (±<paramref name="halfLength"/>, ±<paramref name="halfWidth"/> мінус <see cref="BodyMargin"/>) — зсув нуль; виходить за край —
        /// рівно на перевищення, до краю (лежачого переставляють на мінімум, як у рингу). Коробка шляху довша чи ширша за смугу —
        /// по цій осі центрується, <see cref="Course.Fits"/> = false (режисер склеює на загальний план).
        /// </summary>
        public static Course Nudge(float[] u, float[] v, float halfLength, float halfWidth, float margin = BodyMargin)
        {
            float lu = Math.Max(MinHalf, halfLength - margin), lv = Math.Max(MinHalf, halfWidth - margin);
            float u0 = 0f, u1 = 0f, v0 = 0f, v1 = 0f;
            int n = Math.Min(u.Length, v.Length);
            for (int i = 0; i < n; i++)
            {
                if (i == 0) { u0 = u1 = u[0]; v0 = v1 = v[0]; continue; }
                u0 = Math.Min(u0, u[i]); u1 = Math.Max(u1, u[i]);
                v0 = Math.Min(v0, v[i]); v1 = Math.Max(v1, v[i]);
            }
            float over = Math.Max(0f, (u1 - u0) - 2f * lu) + Math.Max(0f, (v1 - v0) - 2f * lv);
            return new Course { Candidate = 0, ShiftAlong = ToEdge(u0, u1, lu), ShiftOut = ToEdge(v0, v1, lv), Overflow = over, Fits = over <= 0f };
        }

        // Зсув відрізка [a0, a1] у межі ±l: нуль, якщо вже там; до краю, якщо виходить; центр, якщо довший за 2l.
        private static float ToEdge(float a0, float a1, float l)
        {
            if (a1 - a0 > 2f * l) return -(a0 + a1) * 0.5f;
            if (a0 < -l) return -l - a0;
            if (a1 > l) return l - a1;
            return 0f;
        }
    }
}
