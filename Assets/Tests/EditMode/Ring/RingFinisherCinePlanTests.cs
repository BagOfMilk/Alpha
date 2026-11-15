using System;
using System.Collections.Generic;
using Game.Gameplay.Ring.Stage;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Фінішер як кіно (власник 14.11.2026: «А фінішери це шось кінематографічне»; docs/ring/FINISHER_CINE.md): плани йдуть
    /// по черзі й не миготять, сусідні різняться, промах іде без удару, сповільнення — рампою в межах бюджету, доступність
    /// прибирає рух, а не склейки; камера — всередині рингу з одного боку від осі, головне — між панеллю й словом.
    /// </summary>
    public sealed class RingFinisherCinePlanTests
    {
        private const float Eps = 1e-4f;
        private const double Aspect = 16.0 / 9.0;

        private static CineOptions Hit(int gimmick = 0)
        {
            return new CineOptions { Hit = true, Flashes = true, ScreenShake = true, Gimmick = gimmick };
        }

        private static CineMarks NoMarks(float contact, float length)
        {
            return new CineMarks { Call = -1f, Grip = -1f, Apex = -1f, After = -1f, Contact = contact, Length = length };
        }

        private static CineMarks Marked()
        {
            return new CineMarks { Call = 0f, Grip = 0.8f, Apex = 1.9f, Contact = 2.4f, After = 2.85f, Length = 4.2f };
        }

        private static List<CineBeatKind> Kinds(FinisherCineTimeline tl)
        {
            var k = new List<CineBeatKind>();
            foreach (var b in tl.Beats) k.Add(b.Kind);
            return k;
        }

        /// <summary>Пара в центрі рингу: нападник ліворуч, жертва праворуч по X, обоє стоять; рефері — де сказала розкадровка.</summary>
        private static CineScene Center(double ax = -0.4, double az = 0.0, double vx = 0.4, double vz = 0.0)
        {
            var s = new CineScene
            {
                Attacker = new CapPoint(ax, 0.0, az), Victim = new CapPoint(vx, 0.0, vz),
                AttackerHead = new CapPoint(ax, 1.62, az), VictimHead = new CapPoint(vx, 1.6, vz),
                Facing = new CapPoint(1.0, 0.0, 0.0), MatY = 0.0, RingHalf = 2.8,
                Referee = new CapPoint(ax - 9.0 * (vx - ax), 0.0, az - 9.0 * (vz - az)),   // далеко за спиною нападника, на осі
            };
            s.Referee = RingFinisherCinePlan.RefereeSpot(s, RingFinisherCinePlan.Side(s));
            return s;
        }

        // ---------------------------------------------------------------- розклад

        [Test]
        public void NoMarks_FallbackSchedule_FromContact()
        {
            var tl = RingFinisherCinePlan.Build(NoMarks(1.6f, 3.0f), Hit());
            Assert.That(Kinds(tl), Is.EqualTo(new[] { CineBeatKind.Call, CineBeatKind.Grip, CineBeatKind.Impact, CineBeatKind.After }));
            Assert.That(tl.Beats[0].ClipTime, Is.EqualTo(0f).Within(Eps));
            Assert.That(tl.Beats[1].ClipTime, Is.EqualTo(1.6f - RingFinisherCinePlan.GripBeforeContact).Within(Eps));
            Assert.That(tl.Beats[2].ClipTime, Is.EqualTo(1.6f - RingFinisherCinePlan.ImpactLead).Within(Eps));
            Assert.That(tl.Beats[3].ClipTime, Is.EqualTo(1.6f + RingFinisherCinePlan.AfterDelay).Within(Eps));
            Assert.That(tl.End, Is.EqualTo(3.0f).Within(Eps), "кіно не довше за кліп");
            Assert.That(tl.CutToWideAtEnd, Is.False);
        }

        [Test]
        public void Marks_AreUsed_ApexOnlyWhenMarked()
        {
            var tl = RingFinisherCinePlan.Build(Marked(), Hit());
            Assert.That(Kinds(tl), Is.EqualTo(new[] { CineBeatKind.Call, CineBeatKind.Grip, CineBeatKind.Apex, CineBeatKind.Impact, CineBeatKind.After }));
            Assert.That(tl.Beats[1].ClipTime, Is.EqualTo(0.8f).Within(Eps));
            Assert.That(tl.Beats[2].ClipTime, Is.LessThanOrEqualTo(1.9f + Eps), "склейка на вершину — не пізніше за саму вершину");
            Assert.That(tl.Beats[4].ClipTime, Is.EqualTo(2.85f).Within(Eps));
            Assert.That(Kinds(RingFinisherCinePlan.Build(NoMarks(2.4f, 4.2f), Hit())), Has.No.Member(CineBeatKind.Apex));
        }

        [Test]
        public void Beats_AreOrdered_AndNeverShorterThanMinShot()
        {
            for (float contact = 0.05f; contact < 4.0f; contact += 0.07f)
                for (float tail = 0.1f; tail < 2.6f; tail += 0.31f)
                    foreach (bool hit in new[] { true, false })
                        foreach (bool repeat in new[] { true, false })
                        {
                            var o = Hit();
                            o.Hit = hit; o.Repeat = repeat;
                            var m = NoMarks(contact, contact + tail);
                            m.Apex = contact - 0.4f;                       // мітка вершини є завжди — хай уміщається, як зможе
                            var tl = RingFinisherCinePlan.Build(m, o);
                            string at = "удар " + contact + ", хвіст " + tail + ", влучив " + hit + ", повтор " + repeat;
                            for (int i = 0; i < tl.Beats.Count; i++)
                            {
                                Assert.That(tl.Beats[i].ClipTime, Is.GreaterThanOrEqualTo(0f), at);
                                float next = i + 1 < tl.Beats.Count ? tl.Beats[i + 1].ClipTime : tl.End;
                                if (i + 1 < tl.Beats.Count)
                                {
                                    Assert.That((int)tl.Beats[i + 1].Kind, Is.GreaterThan((int)tl.Beats[i].Kind), "порядок тактів: " + at);
                                    Assert.That(next - tl.Beats[i].ClipTime, Is.GreaterThanOrEqualTo(RingFinisherCinePlan.MinShot - Eps), "план коротший за MinShot: " + at);
                                }
                                else Assert.That(next, Is.GreaterThanOrEqualTo(tl.Beats[i].ClipTime), "кінець раніше за останню склейку: " + at);
                            }
                            if (!tl.Empty) Assert.That(tl.End, Is.LessThanOrEqualTo(contact + tail + Eps), "кіно довше за кліп: " + at);
                            if (hit) Assert.That(Kinds(tl), Has.Member(CineBeatKind.Impact), "удар — завжди: " + at);
                        }
        }

        [Test]
        public void Repeat_HasNoCall()
        {
            var o = Hit();
            o.Repeat = true;
            var tl = RingFinisherCinePlan.Build(Marked(), o);
            Assert.That(Kinds(tl), Has.No.Member(CineBeatKind.Call));
            Assert.That(tl.Beats[0].Kind, Is.EqualTo(CineBeatKind.Grip));
        }

        [Test]
        public void Miss_StopsBeforeTheWhiff_NoSlowMotion()
        {
            var o = Hit();
            o.Hit = false;
            var tl = RingFinisherCinePlan.Build(Marked(), o);
            Assert.That(Kinds(tl), Is.EqualTo(new[] { CineBeatKind.Call, CineBeatKind.Grip }));
            Assert.That(tl.End, Is.EqualTo(2.4f - RingFinisherCinePlan.MissLead).Within(Eps));
            Assert.That(tl.CutToWideAtEnd, Is.True);
            Assert.That(tl.Ramp, Is.False);
            Assert.That(tl.Freeze, Is.False);
            Assert.That(tl.Flash || tl.Strobe || tl.Shake, Is.False);
            for (float t = 0f; t < 4.2f; t += 0.01f) Assert.That(RingFinisherCinePlan.Speed(tl, t), Is.EqualTo(1f));
            Assert.That(RingFinisherCinePlan.ExtraRealSeconds(tl, 1.2f), Is.EqualTo(0f));
        }

        [Test]
        public void ShortClip_KeepsTheImpact_DropsWhatDoesNotFit()
        {
            var tl = RingFinisherCinePlan.Build(NoMarks(0.6f, 2.0f), Hit());
            Assert.That(Kinds(tl), Is.EqualTo(new[] { CineBeatKind.Impact, CineBeatKind.After }));
            Assert.That(tl.Beats[0].ClipTime, Is.EqualTo(0.4f).Within(Eps));
            var tiny = RingFinisherCinePlan.Build(NoMarks(0.1f, 0.3f), Hit());
            Assert.That(Kinds(tiny), Is.EqualTo(new[] { CineBeatKind.Impact }));
            Assert.That(tiny.Beats[0].ClipTime, Is.EqualTo(0f).Within(Eps));
        }

        [Test]
        public void NoContact_NoCine()
        {
            Assert.That(RingFinisherCinePlan.Build(NoMarks(-1f, 3f), Hit()).Empty, Is.True);
            Assert.That(RingFinisherCinePlan.Build(NoMarks(float.NaN, 3f), Hit()).Empty, Is.True);
            var miss = Hit();
            miss.Hit = false;
            Assert.That(RingFinisherCinePlan.Build(NoMarks(0.3f, 2f), miss).Empty, Is.True, "промах без часу на плани — звичайна камера");
        }

        [Test]
        public void AdjacentShots_ChangeSizeAndAngle()
        {
            foreach (var m in new[] { Marked(), NoMarks(1.6f, 3.0f), NoMarks(0.9f, 2.0f) })
                for (int g = -1; g < 8; g++)
                    foreach (bool repeat in new[] { false, true })
                    {
                        var o = Hit(g);
                        o.Repeat = repeat;
                        var tl = RingFinisherCinePlan.Build(m, o);
                        for (int i = 1; i < tl.Beats.Count; i++)
                        {
                            var a = tl.Beats[i - 1];
                            var b = tl.Beats[i];
                            Assert.That(b.Size, Is.Not.EqualTo(a.Size), a.Kind + " → " + b.Kind + ": той самий розмір плану");
                            Assert.That(Math.Abs(b.Azimuth - a.Azimuth), Is.GreaterThanOrEqualTo(RingFinisherCinePlan.MinAngleChange),
                                a.Kind + " → " + b.Kind + ": курс камери майже той самий — стрибок кадру");
                        }
                    }
        }

        [Test]
        public void BeatAt_FollowsTheCuts()
        {
            var tl = RingFinisherCinePlan.Build(NoMarks(1.6f, 3.0f), Hit());
            Assert.That(RingFinisherCinePlan.BeatAt(tl, 0f), Is.EqualTo(0));
            Assert.That(RingFinisherCinePlan.BeatAt(tl, 0.69f), Is.EqualTo(0));
            Assert.That(RingFinisherCinePlan.BeatAt(tl, 0.71f), Is.EqualTo(1));
            Assert.That(RingFinisherCinePlan.BeatAt(tl, 1.6f), Is.EqualTo(2), "у мить удару — план удару");
            Assert.That(RingFinisherCinePlan.BeatAt(tl, 2.5f), Is.EqualTo(3));
            Assert.That(RingFinisherCinePlan.BeatAt(tl, 3.0f), Is.EqualTo(-1), "після кінця камера — режисерова");
            var late = RingFinisherCinePlan.Build(NoMarks(0.6f, 2.0f), Hit());
            Assert.That(RingFinisherCinePlan.BeatAt(late, 0.2f), Is.EqualTo(-1), "до першої склейки камера — режисерова");
            Assert.That(RingFinisherCinePlan.BeatAt(new FinisherCineTimeline(), 1f), Is.EqualTo(-1));
        }

        [Test]
        public void Marks_AreReadFromClipEvents_ByPairBase()
        {
            var ev = RingClipEvents.Parse("# мітки\nTitan_Press cam_call 0.000 head -\nTitan_Press cam_grip 0.850 spine_03 spine_03\nTitan_Press cam_apex 1.900 - spine_02\nTitan_Press cam_after 2.900 - head\n");
            var m = CineMarks.Read(ev, "Titan_Press_A", 2.4f, 4.2f);
            Assert.That(m.Call, Is.EqualTo(0f).Within(Eps));
            Assert.That(m.Grip, Is.EqualTo(0.85f).Within(Eps));
            Assert.That(m.Apex, Is.EqualTo(1.9f).Within(Eps));
            Assert.That(m.After, Is.EqualTo(2.9f).Within(Eps));
            var none = CineMarks.Read(ev, "Crusher_A", 1.6f, 3f);
            Assert.That(none.Call, Is.LessThan(0f));
            Assert.That(none.Apex, Is.LessThan(0f));
            Assert.That(none.Contact, Is.EqualTo(1.6f));
        }

        [Test]
        public void AfterShot_LooksAtWhoTheGimmickSays()
        {
            Assert.That(RingFinisherCinePlan.AfterSubject(0), Is.EqualTo(CineSubject.Victim));     // Важковаговик
            Assert.That(RingFinisherCinePlan.AfterSubject(1), Is.EqualTo(CineSubject.Attacker));   // Літун
            Assert.That(RingFinisherCinePlan.AfterSubject(2), Is.EqualTo(CineSubject.Victim));     // Технік
            Assert.That(RingFinisherCinePlan.AfterSubject(3), Is.EqualTo(CineSubject.Attacker));   // Шоумен
            Assert.That(RingFinisherCinePlan.AfterSubject(4), Is.EqualTo(CineSubject.Victim));     // Бувалий
            Assert.That(RingFinisherCinePlan.AfterSubject(5), Is.EqualTo(CineSubject.Attacker));   // Хардкорник
            Assert.That(RingFinisherCinePlan.AfterSubject(6), Is.EqualTo(CineSubject.Attacker));   // Самородок
            Assert.That(RingFinisherCinePlan.AfterSubject(7), Is.EqualTo(CineSubject.Pair));       // Силач
            Assert.That(RingFinisherCinePlan.AfterSubject(-1), Is.EqualTo(CineSubject.Victim));
            var tl = RingFinisherCinePlan.Build(Marked(), Hit(5));
            Assert.That(tl.Beats[tl.Beats.Count - 1].Subject, Is.EqualTo(CineSubject.Attacker));
        }

        // ---------------------------------------------------------------- час

        [Test]
        public void SlowMotion_IsARamp_NotAStep()
        {
            var tl = RingFinisherCinePlan.Build(Marked(), Hit());
            Assert.That(tl.Ramp, Is.True);
            float c = tl.Contact;
            Assert.That(RingFinisherCinePlan.Speed(tl, c - RingFinisherCinePlan.RampLead - 0.01f), Is.EqualTo(1f));
            Assert.That(RingFinisherCinePlan.Speed(tl, c), Is.EqualTo(RingFinisherCinePlan.SlowSpeed).Within(Eps), "удар — на повному сповільненні");
            Assert.That(RingFinisherCinePlan.Speed(tl, c + RingFinisherCinePlan.SlowAfter + RingFinisherCinePlan.RampOut + 0.01f), Is.EqualTo(1f));
            float min = 1f, prev = RingFinisherCinePlan.Speed(tl, 0f);
            const float dt = 1f / 240f;
            for (float t = dt; t < tl.End; t += dt)
            {
                float s = RingFinisherCinePlan.Speed(tl, t);
                Assert.That(Math.Abs(s - prev), Is.LessThan(0.05f), "стрибок швидкості на " + t + " с — сходинка, а не рампа");
                Assert.That(s, Is.GreaterThanOrEqualTo(RingFinisherCinePlan.SlowSpeed - Eps));
                Assert.That(s, Is.LessThanOrEqualTo(1f + Eps));
                if (s < min) min = s;
                prev = s;
            }
            Assert.That(min, Is.EqualTo(RingFinisherCinePlan.SlowSpeed).Within(Eps));
        }

        [Test]
        public void ExtraTime_IsFelt_AndWithinBudget()
        {
            var tl = RingFinisherCinePlan.Build(Marked(), Hit());
            foreach (float playSpeed in new[] { 1.0f, 1.2f })
            {
                float extra = RingFinisherCinePlan.ExtraRealSeconds(tl, playSpeed);
                Assert.That(extra, Is.GreaterThan(0.5f), "сповільнення має бути помітне");
                Assert.That(extra, Is.LessThanOrEqualTo(RingFinisherCinePlan.MaxExtraReal), "бюджет часу фінішера");
            }
        }

        [Test]
        public void ExtraTime_ScalesWithPlaySpeed_AndCountsTheFreezeOnce()
        {
            var tl = RingFinisherCinePlan.Build(Marked(), Hit());
            float e10 = RingFinisherCinePlan.ExtraRealSeconds(tl, 1.0f), e12 = RingFinisherCinePlan.ExtraRealSeconds(tl, 1.2f);
            float f = RingFinisherCinePlan.FreezeReal;
            // Рампа: тримання 0,4 с кліпа на 0,35× + вхід 0,1 с і вихід 0,3 с — разом ≈ 1,02 с за темпу 1; стоп-кадр — реальні 0,06 с.
            Assert.That(e10, Is.EqualTo(1.08f).Within(0.04f));
            Assert.That(e12, Is.EqualTo(0.91f).Within(0.04f));
            Assert.That(e12, Is.LessThan(e10 - 0.1f), "швидший темп відтворення — коротше сповільнення в реальному часі");
            Assert.That((e12 - f) * 1.2f, Is.EqualTo(e10 - f).Within(2e-3f), "сповільнення ділиться на темп, стоп-кадр — ні");
            Assert.That(RingFinisherCinePlan.ExtraRealSeconds(tl, 0f), Is.EqualTo(0f));
            // Удар на самому початку кліпа: сповільнення до нього коротше, а не «з мінусового часу».
            var early = RingFinisherCinePlan.Build(NoMarks(0.1f, 3.0f), Hit());
            Assert.That(RingFinisherCinePlan.ExtraRealSeconds(early, 1.0f), Is.LessThan(e10 - 0.1f));
        }

        [Test]
        public void End_IsAftermathPlusHold_WhenTheClipIsLong()
        {
            var m = Marked();
            m.Length = 9f;
            var tl = RingFinisherCinePlan.Build(m, Hit());
            Assert.That(tl.End, Is.EqualTo(2.85f + RingFinisherCinePlan.AfterHold).Within(Eps), "наслідок тримається AfterHold — і камера режисерові");
            // Скільки тримати наслідок: коротше 0,8 с — обличчя не встигає прочитатись, довше 1,5 с — кіно затримує утримання.
            Assert.That(tl.End - 2.85f, Is.GreaterThanOrEqualTo(0.8f));
            Assert.That(tl.End - 2.85f, Is.LessThanOrEqualTo(1.5f));
            var noMark = RingFinisherCinePlan.Build(NoMarks(1.6f, 9f), Hit());
            Assert.That(noMark.End, Is.EqualTo(1.6f + RingFinisherCinePlan.AfterDelay + RingFinisherCinePlan.AfterHold).Within(Eps));
            // Довжина невідома (0): кіно рахує, ніби кліп довгий.
            var unknown = RingFinisherCinePlan.Build(NoMarks(1.6f, 0f), Hit());
            Assert.That(Kinds(unknown), Has.Member(CineBeatKind.After));
            Assert.That(unknown.End, Is.EqualTo(noMark.End).Within(Eps));
            // Хвіст рівно AfterMinTail — наслідок ще є; на 0,01 коротший — уже немає.
            float after = 1.6f + RingFinisherCinePlan.AfterDelay;
            Assert.That(Kinds(RingFinisherCinePlan.Build(NoMarks(1.6f, after + RingFinisherCinePlan.AfterMinTail + 0.001f), Hit())), Has.Member(CineBeatKind.After));
            Assert.That(Kinds(RingFinisherCinePlan.Build(NoMarks(1.6f, after + RingFinisherCinePlan.AfterMinTail - 0.01f), Hit())), Has.No.Member(CineBeatKind.After));
        }

        [Test]
        public void ContactAfterTheClipEnd_IsClampedToTheClip()
        {
            var tl = RingFinisherCinePlan.Build(NoMarks(2.0f, 1.5f), Hit());
            Assert.That(tl.Contact, Is.EqualTo(1.5f).Within(Eps));
            Assert.That(Kinds(tl), Has.Member(CineBeatKind.Impact));
            Assert.That(Kinds(tl), Has.No.Member(CineBeatKind.After));
            Assert.That(tl.End, Is.LessThanOrEqualTo(1.5f + Eps));
            foreach (var b in tl.Beats) Assert.That(b.ClipTime, Is.LessThanOrEqualTo(1.5f + Eps));
        }

        [Test]
        public void Miss_OnARepeat_ShowsOnlyTheGrip()
        {
            var o = Hit();
            o.Hit = false; o.Repeat = true;
            var tl = RingFinisherCinePlan.Build(Marked(), o);
            Assert.That(Kinds(tl), Is.EqualTo(new[] { CineBeatKind.Grip }));
            Assert.That(tl.End, Is.EqualTo(2.4f - RingFinisherCinePlan.MissLead).Within(Eps));
            Assert.That(tl.CutToWideAtEnd, Is.True);
            Assert.That(RingFinisherCinePlan.BeatAt(tl, 0.2f), Is.EqualTo(-1), "до хвату камера — режисерова");
        }

        [Test]
        public void ReduceMotion_KeepsTheCuts_DropsTheMotion()
        {
            var normal = RingFinisherCinePlan.Build(Marked(), Hit());
            var o = Hit();
            o.ReduceMotion = true;
            var calm = RingFinisherCinePlan.Build(Marked(), o);
            Assert.That(Kinds(calm), Is.EqualTo(Kinds(normal)), "плани лишаються");
            for (int i = 0; i < calm.Beats.Count; i++)
            {
                Assert.That(calm.Beats[i].ClipTime, Is.EqualTo(normal.Beats[i].ClipTime).Within(Eps));
                Assert.That(calm.Beats[i].Dolly, Is.EqualTo(0f), "без наїздів");
            }
            Assert.That(calm.Ramp, Is.False);
            Assert.That(calm.Freeze, Is.False);
            Assert.That(calm.Flash || calm.Strobe || calm.Shake, Is.False);
            Assert.That(RingFinisherCinePlan.Speed(calm, calm.Contact), Is.EqualTo(1f));
            Assert.That(RingFinisherCinePlan.ExtraRealSeconds(calm, 1.2f), Is.EqualTo(0f));
            bool anyDolly = false;
            foreach (var b in normal.Beats) anyDolly |= b.Dolly > 0f;
            Assert.That(anyDolly, Is.True, "у звичайному режимі наїзди є");
        }

        [Test]
        public void FlashShakeCheap_GateOnlyTheirEffects()
        {
            var all = RingFinisherCinePlan.Build(Marked(), Hit());
            Assert.That(all.Flash && all.Strobe && all.Shake && all.Freeze && all.Ramp, Is.True);

            var o = Hit();
            o.Cheap = true;                                                  // PERF-01: дешевий режим
            var cheap = RingFinisherCinePlan.Build(Marked(), o);
            Assert.That(cheap.Strobe, Is.False, "строб світла — дороге");
            Assert.That(cheap.Flash && cheap.Ramp && cheap.Shake && cheap.Freeze, Is.True, "спалах удару, сповільнення й тремтіння не коштують нічого");

            o = Hit();
            o.Flashes = false;
            var noFlash = RingFinisherCinePlan.Build(Marked(), o);
            Assert.That(noFlash.Flash || noFlash.Strobe, Is.False);
            Assert.That(noFlash.Shake && noFlash.Ramp, Is.True);

            o = Hit();
            o.ScreenShake = false;
            var noShake = RingFinisherCinePlan.Build(Marked(), o);
            Assert.That(noShake.Shake, Is.False);
            Assert.That(noShake.Flash && noShake.Ramp, Is.True);
        }

        // ---------------------------------------------------------------- камера

        private static void AssertInsideRing(CineShot shot, CineScene s, string what)
        {
            double lim = s.RingHalf - RingFinisherCinePlan.RopeMargin + 1e-6;
            Assert.That(shot.Ok, Is.True, what);
            Assert.That(Math.Abs(shot.Pos.X), Is.LessThanOrEqualTo(lim), what + ": камера за канатами по X");
            Assert.That(Math.Abs(shot.Pos.Z), Is.LessThanOrEqualTo(lim), what + ": камера за канатами по Z");
            Assert.That(shot.Pos.Y, Is.GreaterThanOrEqualTo(s.MatY + 0.2 - 1e-6), what + ": камера в маті");
        }

        [Test]
        public void Camera_StaysInsideTheRing_OnOneSideOfTheAxis()
        {
            var s = Center();
            int side = RingFinisherCinePlan.Side(s);
            Assert.That(side, Is.EqualTo(-1), "вісь уздовж X, рефері не заважає — камери з боку трансляції (−Z)");
            foreach (var b in RingFinisherCinePlan.Build(Marked(), Hit()).Beats)
            {
                var shot = RingFinisherCinePlan.Solve(b, s, side);
                AssertInsideRing(shot, s, b.Kind.ToString());
                Assert.That(shot.Pos.Z, Is.LessThan(-0.3), b.Kind + ": камера перескочила вісь дії");
                Assert.That(shot.Tight, Is.False, b.Kind + ": у центрі рингу місця досить");
                Assert.That(shot.Blocked, Is.False, b.Kind + ": рефері за спиною нападника не заважає");
                Assert.That(shot.Fov, Is.GreaterThanOrEqualTo(b.FovMin - 0.01f));
                Assert.That(shot.Fov, Is.LessThanOrEqualTo(b.FovMax + 0.01f));
            }
        }

        [Test]
        public void Camera_PairNearTheRopes_StillInside_AndPicksTheRoomySide()
        {
            // Пара біля канатів з боку трансляції (−Z): там місця немає — камери йдуть з протилежного боку.
            var s = Center(-0.4, -2.1, 0.4, -2.1);
            int side = RingFinisherCinePlan.Side(s);
            Assert.That(side, Is.EqualTo(1));
            foreach (var b in RingFinisherCinePlan.Build(Marked(), Hit()).Beats)
            {
                var shot = RingFinisherCinePlan.Solve(b, s, side);
                AssertInsideRing(shot, s, b.Kind.ToString());
                Assert.That(shot.Pos.Z, Is.GreaterThan(-2.1), b.Kind + ": камера між парою й канатами");
            }
            // Пара в куті: план або стає всередині рингу, або його немає (камері ніде стати) — за канати вона не виходить.
            var corner = Center(1.9, 1.9, 2.3, 2.3);
            int cs = RingFinisherCinePlan.Side(corner);
            int ok = 0;
            foreach (var b in RingFinisherCinePlan.Build(Marked(), Hit()).Beats)
            {
                var shot = RingFinisherCinePlan.Solve(b, corner, cs);
                if (!shot.Ok) continue;
                ok++;
                AssertInsideRing(shot, corner, "кут, " + b.Kind);
                Assert.That(shot.Distance, Is.GreaterThanOrEqualTo(b.MinDistance * 0.5f), "кут, " + b.Kind + ": камера впритул");
            }
            Assert.That(ok, Is.GreaterThanOrEqualTo(2), "і в куті частина планів стає");
        }

        [Test]
        public void Camera_ActionOutsideTheRing_IsNotOurShot()
        {
            var s = Center(3.4, 0.0, 4.0, 0.0);
            foreach (var b in RingFinisherCinePlan.Build(Marked(), Hit()).Beats)
                Assert.That(RingFinisherCinePlan.Solve(b, s, 1).Ok, Is.False, b.Kind.ToString());
        }

        [Test]
        public void Side_KeepsTheBroadcastSide_RefereeDecidesOnlyATie()
        {
            // Вісь уздовж X: бік трансляції (−Z) виражений — рефері поруч його не міняє (камера обійде курсом, а рефері відійде).
            var s = Center();
            s.Referee = new CapPoint(0.0, 0.0, -1.4);
            Assert.That(RingFinisherCinePlan.Side(s), Is.EqualTo(-1), "перша склейка не перескакує лінію дії");
            // Вісь уздовж Z (нападник лицем до трансляції): боки рівні — камери стають не там, де рефері біля пари.
            var z = Center(0.0, 0.4, 0.0, -0.4);
            z.Referee = new CapPoint(1.2, 0.0, 0.0);
            int away = RingFinisherCinePlan.Side(z);
            z.Referee = new CapPoint(-1.2, 0.0, 0.0);
            Assert.That(RingFinisherCinePlan.Side(z), Is.EqualTo(-away));
            var grip = RingFinisherCinePlan.Build(Marked(), Hit()).Beats[1];
            var shot = RingFinisherCinePlan.Solve(grip, z, RingFinisherCinePlan.Side(z));
            Assert.That(shot.Pos.X, Is.GreaterThan(0.0), "рефері ліворуч (−X) — камери праворуч");
            // Рефері за спиною нападника (де його ставить розкадровка) боку не міняє: вибір стійкий.
            var c = Center();
            Assert.That(RingFinisherCinePlan.Side(c), Is.EqualTo(-1));
        }

        [Test]
        public void Camera_GoesAroundTheReferee()
        {
            var s = Center();
            var grip = RingFinisherCinePlan.Build(Marked(), Hit()).Beats[1];
            Assert.That(grip.Kind, Is.EqualTo(CineBeatKind.Grip));
            s.Referee = new CapPoint(0.0, 0.0, -1.5);                         // рівно на лінії профільної камери
            var shot = RingFinisherCinePlan.Solve(grip, s, -1);
            Assert.That(shot.Ok, Is.True);
            Assert.That(shot.Blocked, Is.False, "є відхил курсу, з якого рефері не закриває пару");
            Assert.That(Math.Abs(shot.Azimuth - grip.Azimuth), Is.GreaterThan(1f));
            var mid = new CapPoint(0.0, 1.1, 0.0);
            double d = RingCapsules.SegmentDistance(shot.Pos, mid, new CapPoint(0.0, 0.0, -1.5), new CapPoint(0.0, 1.8, -1.5));
            Assert.That(d, Is.GreaterThanOrEqualTo(RingFinisherCinePlan.RefClear - 1e-6));
            Assert.That(shot.Pos.Z, Is.LessThan(0.0), "бік осі той самий");
        }

        [Test]
        public void Framing_KeepsTheSubjectBetweenThePanelAndTheWord()
        {
            var s = Center();
            int side = RingFinisherCinePlan.Side(s);
            var beats = RingFinisherCinePlan.Build(Marked(), Hit()).Beats;
            double lo = RingFinisherCinePlan.SafeBottom - 0.01, hi = RingFinisherCinePlan.SafeTop + 0.01;

            // До удару слова «ФІНІШЕР!» на екрані ще немає: верх — у межах кадру (CineBeat.Top), низ — над панеллю.
            var call = RingFinisherCinePlan.Solve(beats[0], s, side);
            double top = RingFinisherCinePlan.FromBottom(call, new CapPoint(s.AttackerHead.X, s.AttackerHead.Y + 0.14, s.AttackerHead.Z));
            Assert.That(top, Is.LessThanOrEqualTo(beats[0].Top + 0.01), "заклик: маківка в кадрі");
            Assert.That(RingFinisherCinePlan.FromBottom(call, s.AttackerHead), Is.GreaterThan(0.45), "заклик: обличчя у верхній половині");
            Assert.That(RingFinisherCinePlan.FromBottom(call, new CapPoint(s.AttackerHead.X, s.AttackerHead.Y - 0.32, s.AttackerHead.Z)), Is.GreaterThanOrEqualTo(lo), "заклик: плечі над панеллю");

            var grip = RingFinisherCinePlan.Solve(beats[1], s, side);
            Assert.That(RingFinisherCinePlan.FromBottom(grip, new CapPoint(0.0, 0.5, 0.0)), Is.GreaterThanOrEqualTo(lo), "хват: коліна над панеллю");
            Assert.That(RingFinisherCinePlan.FromBottom(grip, new CapPoint(0.0, 1.76, 0.0)), Is.LessThanOrEqualTo(beats[1].Top + 0.01), "хват: голови в кадрі");

            var up = s;
            up.VictimHead = new CapPoint(s.Attacker.X, 2.2, s.Attacker.Z);     // жертва над головою нападника
            var apex = RingFinisherCinePlan.Solve(beats[2], up, side);
            Assert.That(RingFinisherCinePlan.FromBottom(apex, new CapPoint(s.Attacker.X, 2.4, s.Attacker.Z)), Is.LessThanOrEqualTo(beats[2].Top + 0.01), "вершина: жертва в кадрі");
            Assert.That(RingFinisherCinePlan.FromBottom(apex, new CapPoint(s.Attacker.X, 1.05, s.Attacker.Z)), Is.GreaterThanOrEqualTo(lo), "вершина: груди нападника над панеллю");
            Assert.That(apex.Look.Y, Is.GreaterThan(apex.Pos.Y), "вершина: камера дивиться знизу вгору");
            double ahx = apex.Look.X - apex.Pos.X, ahz = apex.Look.Z - apex.Pos.Z;
            double pitch = Math.Atan2(apex.Look.Y - apex.Pos.Y, Math.Sqrt(ahx * ahx + ahz * ahz)) * 180.0 / Math.PI;
            Assert.That(pitch, Is.GreaterThanOrEqualTo(10.0), "вершина: героїчний план — помітно знизу");
            Assert.That(pitch, Is.LessThanOrEqualTo(35.0), "вершина: не «в стелю»");

            // Удар: обоє на маті в точці жертви.
            var hitScene = s;
            hitScene.AttackerHead = new CapPoint(s.Victim.X - 0.3, 0.75, s.Victim.Z);
            hitScene.VictimHead = new CapPoint(s.Victim.X + 0.5, 0.2, s.Victim.Z);
            var impact = RingFinisherCinePlan.Solve(beats[3], hitScene, side);
            Assert.That(RingFinisherCinePlan.FromBottom(impact, new CapPoint(s.Victim.X, 0.0, s.Victim.Z)), Is.GreaterThanOrEqualTo(lo), "удар: мат у точці удару над панеллю");
            Assert.That(RingFinisherCinePlan.FromBottom(impact, new CapPoint(s.Victim.X, 0.9, s.Victim.Z)), Is.LessThanOrEqualTo(hi), "удар: тіла під словом");

            var after = RingFinisherCinePlan.Solve(beats[4], hitScene, side);
            double face = RingFinisherCinePlan.FromBottom(after, hitScene.VictimHead);
            Assert.That(face, Is.GreaterThanOrEqualTo(lo), "наслідок: обличчя над панеллю");
            Assert.That(face, Is.LessThanOrEqualTo(hi), "наслідок: обличчя під словом");
        }

        [Test]
        public void Referee_IsOutOfFrame_UntilTheAftermath()
        {
            var s = Center();
            int side = RingFinisherCinePlan.Side(s);
            foreach (var b in RingFinisherCinePlan.Build(Marked(), Hit()).Beats)
            {
                if (b.Kind == CineBeatKind.After) continue;                  // на наслідку рефері може входити в кадр
                var sc = s;
                if (b.Kind == CineBeatKind.Apex) sc.VictimHead = new CapPoint(s.Attacker.X, 2.2, s.Attacker.Z);
                var shot = RingFinisherCinePlan.Solve(b, sc, side);
                foreach (double h in new[] { 0.1, 0.9, 1.7 })
                    Assert.That(RingFinisherCinePlan.InFrame(shot, new CapPoint(s.Referee.X, h, s.Referee.Z), Aspect, 0.03), Is.False,
                        b.Kind + ": рефері в кадрі на висоті " + h + " м");
            }
        }

        [Test]
        public void FixedAxis_KeepsTheCameraSide_WhenThePairTurnsOrStacks()
        {
            var s = Center();
            int side = RingFinisherCinePlan.Side(s);
            var axis0 = RingFinisherCinePlan.Axis(s);
            Assert.That(axis0.X, Is.EqualTo(1.0).Within(1e-9));
            var beats = RingFinisherCinePlan.Build(Marked(), Hit()).Beats;

            // Вершина: жертва над нападником (тази в одній точці), а нападник за прийом розвернувся спиною до старту.
            var up = s;
            up.Victim = s.Attacker;
            up.VictimHead = new CapPoint(s.Attacker.X, 2.2, s.Attacker.Z);
            up.Facing = new CapPoint(-1.0, 0.0, 0.0);
            var loose = RingFinisherCinePlan.Solve(beats[2], up, side);
            Assert.That(loose.Pos.Z, Is.GreaterThan(0.3), "без зафіксованої осі камера перескакує лінію дії — саме це й лікуємо");

            up.AxisFixed = true;
            up.AxisDir = axis0;
            var a = RingFinisherCinePlan.Axis(up);
            Assert.That(a.X, Is.EqualTo(axis0.X).Within(1e-9));
            Assert.That(a.Z, Is.EqualTo(axis0.Z).Within(1e-9));
            foreach (var b in beats)
            {
                var shot = RingFinisherCinePlan.Solve(b, up, side);
                Assert.That(shot.Ok, Is.True, b.Kind.ToString());
                Assert.That(shot.Pos.Z, Is.LessThan(-0.3), b.Kind + ": з зафіксованою віссю камера лишається з боку трансляції");
            }

            // Довжина зафіксованої осі довільна; нульова — не вісь, діє звичайне правило.
            up.AxisDir = new CapPoint(7.0, 0.0, 0.0);
            Assert.That(RingFinisherCinePlan.Axis(up).X, Is.EqualTo(1.0).Within(1e-9));
            up.AxisDir = new CapPoint(0.0, 0.0, 0.0);
            Assert.That(RingFinisherCinePlan.Axis(up).X, Is.EqualTo(-1.0).Within(1e-9), "пара впритул — куди дивиться нападник");
        }

        [Test]
        public void CoincidentPair_WithoutFacing_StillGivesAnAxisAndAShot()
        {
            var s = Center(0.0, 0.0, 0.0, 0.0);
            s.Facing = new CapPoint(0.0, 0.0, 0.0);
            var d = RingFinisherCinePlan.Axis(s);
            Assert.That(double.IsNaN(d.X) || double.IsNaN(d.Z), Is.False);
            Assert.That(Math.Sqrt(d.X * d.X + d.Z * d.Z), Is.EqualTo(1.0).Within(1e-9));
            int side = RingFinisherCinePlan.Side(s);
            foreach (var b in RingFinisherCinePlan.Build(Marked(), Hit()).Beats)
            {
                var shot = RingFinisherCinePlan.Solve(b, s, side);
                Assert.That(shot.Ok, Is.True, b.Kind.ToString());
                Assert.That(double.IsNaN(shot.Pos.X) || double.IsNaN(shot.Pos.Z) || double.IsNaN(shot.Look.Y) || float.IsNaN(shot.Fov), Is.False, b.Kind.ToString());
            }
        }

        [Test]
        public void RefereeSpot_NearTheRopes_StaysInsideTheRing()
        {
            // Нападник спиною до канатів: «2,2 м за його спиною» було б за рингом — рефері стає біля канатів, усередині.
            var s = Center(-2.3, 0.0, -1.5, 0.0);
            foreach (int side in new[] { 1, -1 })
            {
                var r = RingFinisherCinePlan.RefereeSpot(s, side);
                double lim = s.RingHalf - RingFinisherCinePlan.RopeMargin - 0.25 + 1e-9;
                Assert.That(Math.Abs(r.X), Is.LessThanOrEqualTo(lim));
                Assert.That(Math.Abs(r.Z), Is.LessThanOrEqualTo(lim));
                Assert.That(r.Y, Is.EqualTo(s.MatY));
            }
            // У центрі: за спиною нападника (проти осі) і з боку камер.
            var c = Center();
            var spot = RingFinisherCinePlan.RefereeSpot(c, -1);
            Assert.That(spot.X, Is.LessThan(c.Attacker.X - 1.5), "за спиною нападника");
            Assert.That(spot.Z, Is.LessThan(-0.8), "з боку камер (−Z)");
        }

        [Test]
        public void SameInput_SameStoryboard()
        {
            var a = RingFinisherCinePlan.Build(Marked(), Hit(3));
            var b = RingFinisherCinePlan.Build(Marked(), Hit(3));
            Assert.That(Kinds(a), Is.EqualTo(Kinds(b)));
            var s = Center();
            for (int i = 0; i < a.Beats.Count; i++)
            {
                var x = RingFinisherCinePlan.Solve(a.Beats[i], s, -1);
                var y = RingFinisherCinePlan.Solve(b.Beats[i], s, -1);
                Assert.That(x.Pos.X, Is.EqualTo(y.Pos.X));
                Assert.That(x.Pos.Z, Is.EqualTo(y.Pos.Z));
                Assert.That(x.Look.Y, Is.EqualTo(y.Look.Y));
                Assert.That(x.Fov, Is.EqualTo(y.Fov));
            }
        }
    }
}
