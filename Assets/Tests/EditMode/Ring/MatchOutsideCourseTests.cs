using Game.Core.Ring.Match;
using Game.Gameplay.Ring.Models;
using Game.Gameplay.Ring.Stage;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Курс прийому за рингом (звіт 8f, кадр 07): шлях пари мусить лежати в смузі проходу, а не вести тіло на фартух чи за межу.
    /// Геометрія — чиста (<see cref="RingOutsideCourse"/>), режисер лише подає точки з кліпів.
    /// </summary>
    public class MatchOutsideCourseTests
    {
        private const float HalfLength = 2.0f, HalfWidth = 0.85f;

        // Шлях «уперед на len метрів» від початку, з бічним розкидом side (рука, корпус): локально x — вправо, z — вперед.
        private static void Forward(float len, float side, out float[] x, out float[] z)
        {
            x = new[] { 0f, side, -side, 0f };
            z = new[] { 0f, len * 0.3f, len * 0.7f, len };
        }

        // Кандидати для шляху: 0 — вперед уздовж боку, 1 — назад, 2 — назовні (спиною до рингу). Вісь u — уздовж, v — назовні.
        private static void Candidates(float[] x, float[] z, out float[][] u, out float[][] v)
        {
            float[] yaws = { 90f, -90f, 0f };   // уздовж = (1,0), назовні = (0,1): курс 90° дивиться вздовж +u, 0° — назовні
            u = new float[3][];
            v = new float[3][];
            for (int k = 0; k < 3; k++)
            {
                u[k] = new float[x.Length];
                v[k] = new float[x.Length];
                for (int i = 0; i < x.Length; i++) RingOutsideCourse.ToLane(x[i], z[i], yaws[k], 1f, 0f, 0f, 1f, out u[k][i], out v[k][i]);
            }
        }

        [Test]
        public void ToLane_ForwardAtYaw90_IsAlongTheLane()
        {
            float u, v;
            RingOutsideCourse.ToLane(0f, 2.8f, 90f, 1f, 0f, 0f, 1f, out u, out v);
            Assert.AreEqual(2.8f, u, 1e-4f);
            Assert.AreEqual(0f, v, 1e-4f);
        }

        [Test]
        public void Lariat_AlongTheLane_Fits_StartShiftedBack()
        {
            float[] x, z;
            Forward(2.8f, 0.1f, out x, out z);
            float[][] u, v;
            Candidates(x, z, out u, out v);
            var c = RingOutsideCourse.Choose(u, v, HalfLength, HalfWidth);
            Assert.IsTrue(c.Fits, "ларіат 2,8 м уздовж проходу вміщається");
            Assert.AreEqual(0, c.Candidate, "уздовж проходу вперед — перший кандидат");
            Assert.Less(c.ShiftAlong, -1f, "початок пари — назад проти руху: шлях центрований, а він іде вперед на 2,8 м");
        }

        [Test]
        public void Lariat_AcrossTheLane_DoesNotFit()
        {
            float[] x, z;
            Forward(2.8f, 0.1f, out x, out z);
            float[][] u, v;
            Candidates(x, z, out u, out v);
            // Лише «поперек» (назовні): смуга завширшки 1,7 м ларіата не вміщає.
            var c = RingOutsideCourse.Choose(new[] { u[2] }, new[] { v[2] }, HalfLength, HalfWidth);
            Assert.IsFalse(c.Fits);
            Assert.Greater(c.Overflow, 0.5f);
        }

        [Test]
        public void FirstCandidateFails_NextOneThatFitsIsChosen()
        {
            float[] x, z;
            Forward(2.8f, 0.1f, out x, out z);
            float[][] u, v;
            Candidates(x, z, out u, out v);
            // Поперечний кандидат першим: не вміщається — береться наступний (уздовж).
            var c = RingOutsideCourse.Choose(new[] { u[2], u[1] }, new[] { v[2], v[1] }, HalfLength, HalfWidth);
            Assert.IsTrue(c.Fits);
            Assert.AreEqual(1, c.Candidate);
        }

        [Test]
        public void NothingFits_PicksTheLeastOverflow_AndSaysSo()
        {
            // Шлях 6 м уздовж і 3 м поперек: не вміщається в жодному курсі; менше виходить той, що довшою віссю уздовж проходу.
            var x = new[] { 0f, 3f };
            var z = new[] { 0f, 6f };
            float[][] u, v;
            Candidates(x, z, out u, out v);
            var c = RingOutsideCourse.Choose(u, v, HalfLength, HalfWidth);
            Assert.IsFalse(c.Fits, "запасний варіант — склейка на загальний план");
            Assert.AreEqual(0, c.Candidate, "уздовж довшої осі перевищення менше, ніж назовні");
        }

        [Test]
        public void Shift_PutsTheWholePathInsideTheLane()
        {
            float[] x, z;
            Forward(2.8f, 0.1f, out x, out z);
            float[][] u, v;
            Candidates(x, z, out u, out v);
            var c = RingOutsideCourse.Choose(u, v, HalfLength, HalfWidth);
            float lu = HalfLength - RingOutsideCourse.BodyMargin, lv = HalfWidth - RingOutsideCourse.BodyMargin;
            for (int i = 0; i < u[c.Candidate].Length; i++)
            {
                float uu = u[c.Candidate][i] + c.ShiftAlong, vv = v[c.Candidate][i] + c.ShiftOut;
                Assert.LessOrEqual(System.Math.Abs(uu), lu + 1e-4f, "точка " + i + " за межею проходу вздовж");
                Assert.LessOrEqual(System.Math.Abs(vv), lv + 1e-4f, "точка " + i + " на фартусі чи за краєм");
            }
        }

        [Test]
        public void NarrowGym_LariatStillFitsAlong()
        {
            float[] x, z;
            Forward(2.8f, 0.1f, out x, out z);
            float[][] u, v;
            Candidates(x, z, out u, out v);
            // Спортзал: півширина 0,55 (звужена смуга), вільна пів-довжина 2,3.
            var c = RingOutsideCourse.Choose(u, v, 2.3f, 0.55f);
            Assert.IsTrue(c.Fits);
        }

        // ---------------- найменший зсув пари, яку не повернути (лежачий, ланцюг) ----------------

        private static float Lu { get { return HalfLength - RingOutsideCourse.BodyMargin; } }
        private static float Lv { get { return HalfWidth - RingOutsideCourse.BodyMargin; } }

        [Test]
        public void Nudge_PathAlreadyInLane_DoesNotMoveThePair()
        {
            var c = RingOutsideCourse.Nudge(new[] { -0.5f, 0.3f, 1.0f }, new[] { -0.2f, 0.1f, 0.4f }, HalfLength, HalfWidth);
            Assert.IsTrue(c.Fits);
            Assert.AreEqual(0f, c.ShiftAlong, 1e-6f, "лежачого не переставляють, якщо й так у смузі");
            Assert.AreEqual(0f, c.ShiftOut, 1e-6f);
        }

        [Test]
        public void Nudge_OverhangAlong_ShiftsExactlyToTheEdge()
        {
            // Шлях 1,2…2,4 м уздовж (за межею 1,8): зсув рівно на вихід — назад на 0,6, а не центрування (−1,8).
            var u = new[] { 1.2f, 1.8f, 2.4f };
            var v = new[] { 0f, 0.1f, 0.2f };
            var c = RingOutsideCourse.Nudge(u, v, HalfLength, HalfWidth);
            Assert.IsTrue(c.Fits);
            Assert.AreEqual(Lu - 2.4f, c.ShiftAlong, 1e-4f, "найменший зсув — до краю смуги");
            Assert.AreEqual(0f, c.ShiftOut, 1e-6f, "по осі, що вміщається, не рухає");
            for (int i = 0; i < u.Length; i++) Assert.LessOrEqual(System.Math.Abs(u[i] + c.ShiftAlong), Lu + 1e-4f, "точка " + i);
        }

        [Test]
        public void Nudge_BodyOverTheApron_IsPushedOutwards()
        {
            // Тіло лягає на фартух (v < −межі): зсув назовні, від рингу, на перевищення.
            var u = new[] { 0f, 0.5f };
            var v = new[] { -0.9f, 0.1f };
            var c = RingOutsideCourse.Nudge(u, v, HalfLength, HalfWidth);
            Assert.IsTrue(c.Fits);
            Assert.AreEqual(-Lv + 0.9f, c.ShiftOut, 1e-4f);
            Assert.Greater(c.ShiftOut, 0f, "назовні — від фартуха");
            Assert.AreEqual(0f, c.ShiftAlong, 1e-6f);
        }

        [Test]
        public void Nudge_PathWiderThanTheLane_IsCentred_AndSaysItDoesNotFit()
        {
            // Поперек смуги 2,4 м (півширина 0,85): зсув не допоможе — центр і склейка на загальний план.
            var u = new[] { 0f, 0.2f };
            var v = new[] { -0.4f, 2.0f };
            var c = RingOutsideCourse.Nudge(u, v, HalfLength, HalfWidth);
            Assert.IsFalse(c.Fits);
            Assert.AreEqual(-0.8f, c.ShiftOut, 1e-4f, "по осі, що не вміщається, — центр коробки");
            Assert.AreEqual(2.4f - 2f * Lv, c.Overflow, 1e-4f);
        }

        [Test]
        public void Nudge_NarrowPathFarFromCentre_FitsAfterShift()
        {
            // Вузький шлях далеко назовні (1,5…1,6 м, не перетинає нуль): коробка — від самих точок, а не від центру смуги;
            // вміщається після зсуву на 0,95 м до рингу, склейки на загальний план не треба (рев'ю 12.11.2026).
            var c = RingOutsideCourse.Nudge(new[] { 0f, 0.1f }, new[] { 1.5f, 1.6f }, HalfLength, HalfWidth);
            Assert.IsTrue(c.Fits);
            Assert.AreEqual(Lv - 1.6f, c.ShiftOut, 1e-4f);
            Assert.AreEqual(0f, c.Overflow, 1e-6f);
        }

        [Test]
        public void Nudge_NarrowGym_KeepsAMinimalLane_AndHonoursMargin()
        {
            // Півширина 0,15 мінус запас 0,2 — від'ємна: смуга не стає нулем, лишається 0,1 (MinHalf).
            var c = RingOutsideCourse.Nudge(new[] { 0f }, new[] { 0.3f }, HalfLength, 0.15f);
            Assert.IsTrue(c.Fits);
            Assert.AreEqual(0.1f - 0.3f, c.ShiftOut, 1e-4f);
            // Свій запас: 0,5 замість 0,2 — межа 0,35, точка 0,5 зсувається на 0,15.
            var m = RingOutsideCourse.Nudge(new[] { 0f }, new[] { 0.5f }, HalfLength, HalfWidth, 0.5f);
            Assert.AreEqual(0.35f - 0.5f, m.ShiftOut, 1e-4f);
        }

        [Test]
        public void Nudge_NoPoints_IsAZeroFit()
        {
            var c = RingOutsideCourse.Nudge(new float[0], new float[0], HalfLength, HalfWidth);
            Assert.IsTrue(c.Fits);
            Assert.AreEqual(0f, c.ShiftAlong);
            Assert.AreEqual(0f, c.ShiftOut);
        }

        [Test]
        public void ChooseProp_AlternatesDeterministically_BySeq()
        {
            var a = new MatchEvent { Seq = 4 };
            var b = new MatchEvent { Seq = 5 };
            Assert.AreEqual(RingProp.Chair, RingOutsideCourse.ChooseProp(a));
            Assert.AreEqual(RingProp.Table, RingOutsideCourse.ChooseProp(b));
            Assert.AreEqual(RingProp.Table, RingOutsideCourse.ChooseProp(new MatchEvent { Seq = 5 }), "той самий номер — той самий предмет");
            Assert.AreEqual(RingProp.Chair, RingOutsideCourse.ChooseProp(null));
        }
    }
}
