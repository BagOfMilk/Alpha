using System;
using System.Collections.Generic;
using Game.Gameplay.Ring.Stage;
using NUnit.Framework;
using V2 = Game.Gameplay.Ring.Stage.RingWalkPlan.V2;
using Pose = Game.Gameplay.Ring.Stage.RingWalkPlan.Pose;

namespace Game.Tests.EditMode.Ring
{
    /// <summary>
    /// Охоронці крокової ходи виходу (умови арт-директора 15.11.2026): опорний носок не їде, поворот за опору не більший
    /// за 15°, кінцева мітка — у межах 3 см, точка жестів біля завіси — у межах 0,3 м від старої, борець до удару за шторою.
    /// </summary>
    public class RingWalkPlanTests
    {
        private const int M = 72;

        /// <summary>
        /// Петля ходи, як її бачить сцена після перенесення кліпа на тіло: хід за оберт, таз із погойдуванням убік, носки, що
        /// в опорі «гуляють» відносно слота на <paramref name="drift"/> м (так виходить у рушії: ≈ 2 см за опору). Мах правої —
        /// 0,11–0,43 оберту, лівої — 0,61–0,93, як у кліпах виходу.
        /// </summary>
        private static RingWalkPlan.Gait Gait(float stride, float width = 0.12f, float sway = 0.035f, float drift = 0.02f)
        {
            var g = new RingWalkPlan.Gait
            {
                Stride = new V2(0f, stride),
                ToeL = new V2[M], ToeR = new V2[M], Hip = new V2[M],
                LA = 0.14f, LB = 0.40f, RA = 0.64f, RB = 0.90f, LandL = 0.955f, LandR = 0.455f,   // стає на 0,94 / 0,44; опорою — на зразок пізніше
            };
            V2 pl = new V2(-width, 0.27f * stride + 0.12f), pr = new V2(width, 0.77f * stride + 0.12f), s = g.Stride;
            for (int i = 0; i < M; i++)
            {
                float ph = (float)i / M;
                g.Hip[i] = new V2(0.004f - sway * (float)Math.Cos(2.0 * Math.PI * (ph - 0.27f)), 0.01f + stride * ph);
                // ліва: стоїть з 0,94 минулого оберту до 0,61; мах 0,61–0,94 — до наступного сліду
                if (ph < 0.61f) g.ToeL[i] = pl + new V2(0f, drift * ((ph + 0.06f) / 0.67f - 0.5f));
                else if (ph >= 0.94f) g.ToeL[i] = pl + s + new V2(0f, drift * ((ph - 0.94f) / 0.67f - 0.5f));
                else g.ToeL[i] = Swing(pl + new V2(0f, drift * 0.5f), pl + s + new V2(0f, -drift * 0.5f), (ph - 0.61f) / 0.33f);
                // права: стоїть з 0,44 до 1,11; мах 0,11–0,44
                if (ph >= 0.44f) g.ToeR[i] = pr + new V2(0f, drift * ((ph - 0.44f) / 0.67f - 0.5f));
                else if (ph < 0.11f) g.ToeR[i] = pr - s + new V2(0f, drift * ((ph + 0.56f) / 0.67f - 0.5f));
                else g.ToeR[i] = Swing(pr - s + new V2(0f, drift * 0.5f), pr + new V2(0f, -drift * 0.5f), (ph - 0.11f) / 0.33f);
            }
            return g;
        }

        private static V2 Swing(V2 a, V2 b, float u)
        {
            u = Math.Min(1f, Math.Max(0f, u));
            return a + (b - a) * (u * u * (3f - 2f * u));
        }

        /// <summary>Шлях виходу зі сходами (RingShow.Path): від точки жестів до сходинок північно-західного кута.</summary>
        private static List<V2> StepsPath()
        {
            return new List<V2>
            {
                new V2(-14.3f, 0f), new V2(-12f, 0f), new V2(-8f, -0.15f), new V2(-6.2f, -0.25f), new V2(-4.6f, 1.0f),
                new V2(-3.674f, 3.674f),
            };
        }

        /// <summary>Шлях до закочування (RingShow.RollPath у спортзалі).</summary>
        private static List<V2> RollPath()
        {
            return new List<V2>
            {
                new V2(-14.3f, 0f), new V2(-12f, 0f), new V2(-8f, -0.15f), new V2(-6.2f, -0.25f), new V2(-4.5f, -0.1f),
                new V2(-2.795f, 0f),
            };
        }

        private static RingWalkPlan.Stand Stand(float width = 0.13f, float fwd = 0.1f, float yaw0 = 0f, float yaw1 = 0f)
        {
            return new RingWalkPlan.Stand
            {
                ToeL0 = new V2(-width, fwd), ToeR0 = new V2(width, fwd), ToeL1 = new V2(-width, fwd + 0.02f), ToeR1 = new V2(width, fwd),
                Yaw0 = yaw0, Yaw1 = yaw1,
            };
        }

        /// <summary>
        /// Відтворення того, що робить сцена, з кадрами нерівної довжини: слот щокадру ставиться від опорного носка, опора
        /// змінюється в перший кадр після того, як друга стопа стала, поворот наростає плавно у вікні опори. Повертає таз у
        /// мить зупинки й найбільший зсув носка, поки він опора.
        /// </summary>
        private static V2 Replay(RingWalkPlan.Gait g, RingWalkPlan.Leg leg, float frame, out float worstSlip, out List<V2> prints)
        {
            int foot = leg.StartFoot;
            V2 anchor = leg.StartToe;
            float yaw = leg.StartYaw, ph = leg.Phase0, nextLand = RingWalkPlan.Gait.Next(leg.Phase0, g.Land(1 - foot));
            int ti = 0;
            float applied = 0f, done = 0f;
            worstSlip = 0f;
            prints = new List<V2> { anchor };
            int k = 0;
            while (ph < leg.PhaseStop - 1e-6f)
            {
                // кадри різної довжини: 0,6…1,4 від середньої
                ph = Math.Min(leg.PhaseStop, ph + frame * (0.6f + 0.8f * ((k * 37) % 11) / 10f));
                k++;
                // повороти: довкола опорного носка, плавно у вікні; пропущений кінець вікна доводиться до кінця
                while (ti < leg.Turns.Count)
                {
                    var t = leg.Turns[ti];
                    if (ph < t.A) break;
                    float u = Math.Min(1f, Math.Max(0f, (ph - t.A) / (t.B - t.A)));
                    applied = t.Deg * (u * u * (3f - 2f * u));
                    if (ph < t.B) break;
                    done += t.Deg; applied = 0f; ti++;
                }
                yaw = leg.StartYaw + done + applied;
                Pose slot = Pose.ByPoint(anchor, g.Toe(foot, ph), yaw);
                float slip = (slot.At(g.Toe(foot, ph)) - anchor).Length;
                if (slip > worstSlip) worstSlip = slip;
                if (ph >= nextLand)
                {
                    foot = 1 - foot;
                    anchor = slot.At(g.Toe(foot, ph));
                    prints.Add(anchor);
                    nextLand = RingWalkPlan.Gait.Next(ph, g.Land(1 - foot));
                }
            }
            return Pose.ByPoint(anchor, g.Toe(foot, ph), yaw).At(g.HipAt(ph));
        }

        [Test]
        public void StraightLeg_WalksWholeSteps_WithoutTurns()
        {
            var g = Gait(1.05f, 0.12f, 0f, 0f);
            var path = new List<V2> { new V2(0f, 0f), new V2(10f, 0f) };
            V2 toe = Pose.ByPoint(new V2(0f, 0f), g.HipAt(g.MidL), 90f).At(g.Toe(0, g.MidL));
            var leg = RingWalkPlan.Plan(g, toe, 90f, 0, path, 10f, RingWalkPlan.LookAhead);
            Assert.AreEqual(19, leg.Steps, "10 м кроком 0,525 м — 19 кроків (найближча середина опори)");
            Assert.AreEqual(0, leg.Turns.Count, "на прямій поворотів немає");
            Assert.AreEqual(19 * 0.525f, leg.HipEnd.X, 2e-3f);
            Assert.AreEqual(0f, leg.HipEnd.Z, 2e-3f);
            Assert.AreEqual(90f, leg.Heading, 1e-3f);
            Assert.AreEqual(1, leg.StopFoot, "непарна кількість кроків з лівої опори — зупинка на правій");
            Assert.AreEqual(20, leg.Prints.Count, "слід на кожну опору, разом зі стартовою");
        }

        [Test]
        public void AnchorToeStays_AndTheSceneArrivesWhereThePlanSays()
        {
            foreach (float stride in new[] { 0.81f, 0.92f, 1.0f, 1.055f, 1.16f })
            {
                var g = Gait(stride);
                var path = StepsPath();
                var leg = RingWalkPlan.Plan(g, new V2(-14.3f, 0.12f), 90f, 0, path, RingWalkPlan.Cumulative(path)[path.Count - 1], RingWalkPlan.LookAhead);
                Assert.Greater(leg.Turns.Count, 2, "шлях до сходів повертає — без поворотів його не пройти");
                Assert.LessOrEqual(leg.MaxTurnDeg, RingWalkPlan.MaxTurn + 1e-3f, "поворот за опору — у межах стелі");
                Assert.Less(leg.MaxTurnDeg, 15f, "умова АД: опорна стопа провертається не більше ніж на 15° за крок");
                Assert.Less(leg.MaxOff, 0.3f, "таз не відходить від шляху далі за 0,3 м (оператор з камерою — за 0,7 м), хід " + stride);
                foreach (var t in leg.Turns)
                {
                    Assert.Greater(t.A, leg.Phase0, "стартова опора — без повороту");
                    Assert.Less(t.B, leg.PhaseStop, "остання опора — без повороту");
                    float fa = t.A - (float)Math.Floor(t.A), fb = t.B - (float)Math.Floor(t.B);
                    Assert.AreEqual(t.Foot == 0 ? g.LA : g.RA, fa, 1e-3f, "поворот — у вікні, де ця стопа стоїть сама");
                    Assert.AreEqual(t.Foot == 0 ? g.LB : g.RB, fb, 1e-3f);
                }
                // Сцена з дрібними й із великими кадрами (≈ 85 і ≈ 12 кадрів на секунду) приходить туди ж, куди план.
                foreach (float frame in new[] { 0.006f, 0.04f })
                {
                    float slip;
                    List<V2> prints;
                    V2 hip = Replay(g, leg, frame, out slip, out prints);
                    Assert.Less(slip, 1e-4f, "носок, поки він опора, стоїть (хід " + stride + ")");
                    Assert.Less((hip - leg.HipEnd).Length, 0.015f, "таз у мить зупинки — де в плані, у межах 1,5 см (хід " + stride + ", кадр " + frame + ")");
                    Assert.AreEqual(leg.Prints.Count, prints.Count, "стільки ж опор, скільки в плані");
                    for (int i = 0; i < prints.Count; i++)
                        Assert.Less((prints[i] - leg.Prints[i]).Length, 0.015f, "слід " + i + " — де в плані (хід " + stride + ", кадр " + frame + ")");
                }
            }
        }

        [Test]
        public void Entrance_HitsTheRingMark_AndKeepsTheSoftMarksClose()
        {
            var curtainOut = new V2(-14.3f, 0f);
            foreach (bool steps in new[] { true, false })
            {
                var path = steps ? StepsPath() : RollPath();
                float[] cum = RingWalkPlan.Cumulative(path);
                foreach (float stride in new[] { 0.81f, 0.86f, 0.92f, 0.97f, 1.0f, 1.03f, 1.055f, 1.11f, 1.16f })
                {
                    foreach (bool withBarrier in new[] { false, true })
                    {
                        var g = Gait(stride);
                        var gestures = new List<RingWalkPlan.Stand> { Stand(0.13f, 0.1f, 3f, -8f), Stand(0.15f, 0.08f, 2f, 5f) };
                        var e = RingWalkPlan.PlanEntrance(g, curtainOut, -15.25f, gestures, path, withBarrier ? cum[2] : -1f,
                            withBarrier ? Stand() : null, RingWalkPlan.LookAhead);
                        string tag = (steps ? "сходи" : "закочування") + ", хід " + stride + (withBarrier ? ", бар'єр" : "");
                        Assert.Less(e.EndError, 0.03f, "мітка рингу — у межах 3 см (" + tag + "): " + e.EndError);
                        Assert.LessOrEqual(Math.Abs(e.Shift), g.Step * 0.5f + 1e-4f, "зсув старту — не більше пів кроку (" + tag + ")");
                        Assert.Less(Math.Abs(e.CurtainHip.X - curtainOut.X), g.Step * 0.5f + 0.04f, "точка жестів — у межах пів кроку від старої: до 0,3 м, умова АД на склейки (" + tag + ")");
                        Assert.LessOrEqual(e.Curtain.Start.At(g.HipAt(g.MidL)).X, -15.25f + 1e-3f, "до удару борець за шторою (" + tag + ")");
                        Assert.GreaterOrEqual(e.Curtain.Steps, 2, "крізь штору — щонайменше два кроки (" + tag + ")");
                        Assert.AreEqual(2, e.GestureYaw.Count, "курс пози — на кожен жест (" + tag + ")");
                        Assert.AreEqual(90f, e.GestureYaw[0], 1e-3f, "перший жест дивиться курсом ходу (" + tag + ")");
                        Assert.AreEqual(90f - 3f - 8f, e.GestureYaw[1], 1e-3f, "другий — курсом, яким закінчився перший (" + tag + ")");
                        Assert.AreEqual(e.GestureYaw[1] - 2f + 5f, (e.ToBarrier ?? e.ToRing).StartYaw + g.TravelYaw, 1e-3f,
                            "хода стартує курсом, яким закінчився останній жест (" + tag + ")");
                        foreach (var leg in new[] { e.Curtain, e.ToBarrier, e.ToRing })
                        {
                            if (leg == null) continue;
                            Assert.Less(leg.MaxTurnDeg, 15f, "поворот за опору (" + tag + ")");
                            float slip;
                            List<V2> prints;
                            V2 hip = Replay(g, leg, 0.012f, out slip, out prints);
                            Assert.Less((hip - leg.HipEnd).Length, 0.015f, "сцена приходить, куди план (" + tag + ")");
                        }
                        TestContext.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{0}: кроків {1}+{2}+{3}, зсув старту {4:+0.000;-0.000} м, мітка {5:0.0} мм, поворот до {6:0.0}°, від шляху до {7:0.00} м",
                            tag, e.Curtain.Steps, e.ToBarrier != null ? e.ToBarrier.Steps : 0, e.ToRing.Steps, e.Shift, e.EndError * 1000f,
                            Math.Max(e.ToRing.MaxTurnDeg, e.ToBarrier != null ? e.ToBarrier.MaxTurnDeg : 0f),
                            Math.Max(e.ToRing.MaxOff, e.ToBarrier != null ? e.ToBarrier.MaxOff : 0f)));
                        Assert.AreEqual(withBarrier, e.ToBarrier != null, "зупинка біля бар'єра — окремим відрізком (" + tag + ")");
                        if (withBarrier)
                        {
                            float off;
                            float s = RingWalkPlan.Project(path, cum, e.ToBarrier.HipEnd, 0f, out off);
                            Assert.Less(Math.Abs(s - cum[2]), g.Step * 0.5f + 0.05f, "зупинка — біля точки бар'єра, у межах пів кроку (" + tag + ")");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Шлях і перешкоди: за обраної дальності прицілу постать не відходить від шляху далі за 0,3 м і не підходить до
        /// оператора з камерою (anchor_camop_5, (−7; 0,55)) ближче ніж на 0,6 м — раніше борець ішов крізь нього (огляд 08.11.2026).
        /// </summary>
        [Test]
        public void Walker_KeepsToThePath_AndClearOfTheCameraOperator()
        {
            var cam = new V2(-7f, 0.55f);
            foreach (bool steps in new[] { true, false })
            {
                float worstOff = 0f, nearCam = 99f, worstEnd = 0f, worstTurn = 0f;
                var path = steps ? StepsPath() : RollPath();
                float[] cum = RingWalkPlan.Cumulative(path);
                foreach (float stride in new[] { 0.81f, 0.86f, 0.92f, 0.97f, 1.0f, 1.03f, 1.055f, 1.11f, 1.16f })
                {
                    foreach (bool withBarrier in new[] { false, true })
                    {
                        var g = Gait(stride);
                        var e = RingWalkPlan.PlanEntrance(g, new V2(-14.3f, 0f), -15.25f, new List<RingWalkPlan.Stand> { Stand() }, path,
                            withBarrier ? cum[2] : -1f, withBarrier ? Stand() : null, RingWalkPlan.LookAhead);
                        foreach (var leg in new[] { e.ToBarrier, e.ToRing })
                        {
                            if (leg == null) continue;
                            worstOff = Math.Max(worstOff, leg.MaxOff);
                            worstTurn = Math.Max(worstTurn, leg.MaxTurnDeg);
                            foreach (var h in leg.Track) nearCam = Math.Min(nearCam, (h - cam).Length);
                        }
                        worstEnd = Math.Max(worstEnd, e.EndError);
                        if (e.EndError > 0.008f)
                            TestContext.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                "  гірший випадок: хід {0}, бар'єр {1}: мітка {2:0.0} мм, приціл {3:0.00} м", stride, withBarrier, e.EndError * 1000f, e.LookAhead));
                    }
                }
                TestContext.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0}: від шляху до {1:0.00} м, до оператора {2:0.00} м, мітка до {3:0.0} мм, поворот до {4:0.0}°",
                    steps ? "сходи" : "закочування", worstOff, nearCam, worstEnd * 1000f, worstTurn));
                Assert.Less(worstOff, 0.3f, "відхід від шляху");
                Assert.Greater(nearCam, 0.6f, "відстань до оператора з камерою");
                Assert.Less(worstEnd, 0.015f, "мітка рингу — із запасом до 3 см");
            }
        }

        [Test]
        public void Stop_IsMeasuredAtTheRealHip_NotTheMidline()
        {
            // Погойдування 3,5 см: таз посеред опори зсунутий до опорної стопи, а мітка все одно в межах.
            var path = StepsPath();
            foreach (float stride in new[] { 0.92f, 1.0f, 1.055f })
            {
                var g = Gait(stride);
                var e = RingWalkPlan.PlanEntrance(g, new V2(-14.3f, 0f), -15.25f, new List<RingWalkPlan.Stand> { Stand() }, path, -1f, null, RingWalkPlan.LookAhead);
                Assert.Less(e.EndError, 0.015f, "мітка за справжнім тазом, хід " + stride);
                var leg = e.ToRing;
                V2 real = leg.End.At(g.HipAt(leg.PhaseStop));
                Assert.Less((leg.HipEnd - real).Length, 1e-5f, "HipEnd — справжній таз у мить зупинки");
                V2 mid = leg.End.At(new V2(0.004f, 0.01f + stride * (leg.PhaseStop - (float)Math.Floor(leg.PhaseStop))));
                Assert.Greater((real - mid).Length, 0.03f, "і він не на середній лінії (погойдування)");
            }
        }

        [Test]
        public void Stand_TakesOverThePlantedToe()
        {
            var s = new RingWalkPlan.Stand { ToeL0 = new V2(-0.14f, 0.09f), ToeR0 = new V2(0.15f, 0.07f), ToeL1 = new V2(-0.14f, 0.09f), ToeR1 = new V2(0.15f, 0.07f), Yaw0 = 4f, Yaw1 = -3f };
            var toe = new V2(-7.9f, -0.2f);
            foreach (int foot in new[] { 0, 1 })
            {
                var p = RingWalkPlan.PlaceStand(s, toe, foot, 83f);
                V2 at = p.At(s.Toe0(foot));
                Assert.Less((at - toe).Length, 1e-5f, "носок жесту — рівно там, де стояв опорний носок ходи");
                Assert.AreEqual(83f, p.Yaw + s.Yaw0, 1e-4f, "поза жесту дивиться курсом ходи");
            }
            var g = Gait(1.0f);
            var leg = RingWalkPlan.Plan(g, toe, 83f, 0, new List<V2> { toe, toe + RingWalkPlan.Rot(83f, new V2(0f, 5f)) }, 5f, RingWalkPlan.LookAhead, 2);
            Assert.Less((leg.Start.At(g.Toe(0, leg.Phase0)) - toe).Length, 1e-5f, "хода стартує з лівого носка попереднього кліпа");
            Assert.AreEqual(83f, leg.StartYaw, 1e-4f);
        }

        [Test]
        public void Plan_IsDeterministic()
        {
            var g = Gait(1.055f);
            var path = StepsPath();
            float[] cum = RingWalkPlan.Cumulative(path);
            var a = RingWalkPlan.PlanEntrance(g, new V2(-14.3f, 0f), -15.25f, new List<RingWalkPlan.Stand> { Stand() }, path, cum[2], Stand(), RingWalkPlan.LookAhead);
            var b = RingWalkPlan.PlanEntrance(g, new V2(-14.3f, 0f), -15.25f, new List<RingWalkPlan.Stand> { Stand() }, path, cum[2], Stand(), RingWalkPlan.LookAhead);
            Assert.AreEqual(a.Shift, b.Shift);
            Assert.AreEqual(a.ToRing.Steps, b.ToRing.Steps);
            Assert.AreEqual(a.ToRing.Turns.Count, b.ToRing.Turns.Count);
            for (int i = 0; i < a.ToRing.Turns.Count; i++) Assert.AreEqual(a.ToRing.Turns[i].Deg, b.ToRing.Turns[i].Deg);
            Assert.AreEqual(a.ToRing.HipEnd.X, b.ToRing.HipEnd.X);
        }

        [Test]
        public void Path_ProjectionAndPoints()
        {
            var path = new List<V2> { new V2(0f, 0f), new V2(4f, 0f), new V2(4f, 3f) };
            float[] cum = RingWalkPlan.Cumulative(path);
            Assert.AreEqual(7f, cum[2], 1e-5f);
            float off;
            Assert.AreEqual(2f, RingWalkPlan.Project(path, cum, new V2(2f, 0.5f), 0f, out off), 1e-5f);
            Assert.AreEqual(0.5f, off, 1e-5f);
            Assert.AreEqual(5f, RingWalkPlan.Project(path, cum, new V2(4.2f, 1f), 0f, out off), 1e-5f);
            Assert.AreEqual(3f, RingWalkPlan.Project(path, cum, new V2(1f, 0f), 3f, out off), 1e-5f, "шлях не йде назад");
            V2 beyond = RingWalkPlan.PointAt(path, cum, 8f);
            Assert.AreEqual(4f, beyond.X, 1e-5f);
            Assert.AreEqual(4f, beyond.Z, 1e-5f, "за кінцем — продовження останнього відрізка");
            Assert.AreEqual(90f, new V2(1f, 0f).Yaw, 1e-4f);
            Assert.AreEqual(-170f, RingWalkPlan.DeltaAngle(100f, -70f), 1e-4f);
            Assert.AreEqual(1.44f, RingWalkPlan.Gait.Next(0.75f, 0.44f), 1e-5f, "наступна фаза з часткою 0,44 після 0,75 — в наступному оберті");
            Assert.AreEqual(0.94f, RingWalkPlan.Gait.Next(0.75f, 0.94f), 1e-5f);
        }
    }
}
