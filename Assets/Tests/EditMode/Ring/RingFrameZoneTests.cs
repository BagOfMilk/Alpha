using System;
using System.Collections.Generic;
using Game.Gameplay.Ring.Stage;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Вільна зона кадру (<see cref="RingFrameZone"/>; огляд слотів 32–33, ПМ 13.11.2026; У0 арт-директора 14.11.2026): кожен
    /// прямокутник «маківка — ключиці» вільний від панелей з полем 2,5 % ширини, зсув — найменший у межах повороту, бажане — лише
    /// коли влазить разом із головним, не влазить — кадр як є. Числа розкладки — з екранів кар'єри (CareerTileLayout, ArenaKit):
    /// панель 900 одиниць заввишки, ширина 900·аспект; ліва колонка 80…490, права — 410 від правого краю (звіт сезону — 520).
    /// </summary>
    public class RingFrameZoneTests
    {
        private static RingFrameZone.Box B(float x0, float y0, float x1, float y1) { return new RingFrameZone.Box(x0, y0, x1, y1); }
        private static RingFrameZone.Box U(float x0, float y0, float x1, float y1, float w) { return B(x0 / w, y0 / 900f, x1 / w, y1 / 900f); }
        private static List<RingFrameZone.Box> L(params RingFrameZone.Box[] b) { return new List<RingFrameZone.Box>(b); }
        private const float P = RingFrameZone.Pad;

        [Test]
        public void FreeGap_NoPanels_IsWholeScreen()
        {
            float a, b;
            Assert.IsTrue(RingFrameZone.FreeGap(null, 0.1f, 0.7f, 0.1f, out a, out b));
            Assert.AreEqual(0f, a, 1e-6); Assert.AreEqual(1f, b, 1e-6);
            Assert.IsTrue(RingFrameZone.FreeGap(L(), 0.1f, 0.7f, 0.1f, out a, out b));
            Assert.AreEqual(1f, b - a, 1e-6);
        }

        [Test]
        public void FreeGap_WidestGapInTheBand_PanelsOutsideTheBandDoNotCount()
        {
            var panels = L(B(0f, 0.1f, 0.3f, 0.9f), B(0.7f, 0.1f, 1f, 0.9f), B(0.35f, 0f, 0.65f, 0.08f));
            float a, b;
            Assert.IsTrue(RingFrameZone.FreeGap(panels, 0.12f, 0.7f, 0.1f, out a, out b));
            Assert.AreEqual(0.3f, a, 1e-6); Assert.AreEqual(0.7f, b, 1e-6);
            Assert.IsFalse(RingFrameZone.FreeGap(panels, 0.05f, 0.7f, 0.1f, out a, out b), "плита згори ділить проміжок на два вузькі");
            Assert.AreEqual(0.05f, b - a, 1e-5);
        }

        [Test]
        public void FreeGap_OverlappingAndNestedPanels_Merge()
        {
            var panels = L(B(0.05f, 0.1f, 0.4f, 0.9f), B(0.1f, 0.2f, 0.3f, 0.5f), B(0.38f, 0.3f, 0.45f, 0.6f), B(-0.2f, 0.4f, 0.02f, 0.5f),
                B(0.9f, 0.2f, 1.3f, 0.8f));
            float a, b;
            Assert.IsTrue(RingFrameZone.FreeGap(panels, 0.1f, 0.8f, 0.1f, out a, out b));
            Assert.AreEqual(0.45f, a, 1e-6); Assert.AreEqual(0.9f, b, 1e-6);
        }

        [Test]
        public void Gaps_AllFreeSpansInTheBand_LeftToRight()
        {
            var g = RingFrameZone.Gaps(L(B(0.1f, 0f, 0.3f, 1f), B(0.5f, 0f, 0.6f, 1f), B(0.55f, 0f, 0.7f, 1f)), 0.2f, 0.4f);
            Assert.AreEqual(3, g.Count);
            Assert.AreEqual(0f, g[0].Key, 1e-6); Assert.AreEqual(0.1f, g[0].Value, 1e-6);
            Assert.AreEqual(0.3f, g[1].Key, 1e-6); Assert.AreEqual(0.5f, g[1].Value, 1e-6);
            Assert.AreEqual(0.7f, g[2].Key, 1e-6); Assert.AreEqual(1f, g[2].Value, 1e-6);
            Assert.AreEqual(1, RingFrameZone.Gaps(null, 0f, 1f).Count, "без панелей — увесь екран одним проміжком");
        }

        [Test]
        public void IsFree_PadFromPanels_NoPadAtScreenEdge_OffScreenIsNotFree()
        {
            var panels = L(B(0.3f, 0f, 1f, 1f));
            Assert.IsTrue(RingFrameZone.IsFree(panels, B(0f, 0.2f, 0.27f, 0.3f), 0.025f), "до краю екрана поле не треба");
            Assert.IsFalse(RingFrameZone.IsFree(panels, B(0f, 0.2f, 0.28f, 0.3f), 0.025f), "до панелі ближче за 2,5 %");
            Assert.IsFalse(RingFrameZone.IsFree(panels, B(-0.05f, 0.2f, 0.2f, 0.3f), 0.025f), "за краєм екрана — не вільно");
            Assert.IsTrue(RingFrameZone.IsFree(L(B(0.3f, 0.5f, 1f, 1f)), B(0.2f, 0.2f, 0.5f, 0.3f), 0.025f), "панель нижче смуги не заважає");
        }

        [Test]
        public void Shift_SubjectAlreadyFree_FrameUnchanged_EvenInANarrowerGap()
        {
            float dx; bool nice;
            Assert.IsTrue(RingFrameZone.Shift(L(B(0.2f, 0f, 0.3f, 1f)), L(B(0.05f, 0.2f, 0.12f, 0.3f)), null, P, -1f, 1f, out dx, out nice));
            Assert.AreEqual(0f, dx, 1e-6, "голова в лівому вузькому проміжку й так видна — не тягти в ширший");
        }

        [Test]
        public void Shift_UnderAPanel_GoesToTheNearestFreeSpot_TieGoesRight()
        {
            var panels = L(B(0.3f, 0f, 0.6f, 1f));
            float dx; bool nice;
            Assert.IsTrue(RingFrameZone.Shift(panels, L(B(0.5f, 0.2f, 0.56f, 0.3f)), null, P, -1f, 1f, out dx, out nice));
            Assert.AreEqual(0.6f + P - 0.5f, dx, 1e-5, "ближче праворуч — до краю панелі з полем");
            Assert.IsTrue(RingFrameZone.Shift(panels, L(B(0.33f, 0.2f, 0.39f, 0.3f)), null, P, -1f, 1f, out dx, out nice));
            Assert.AreEqual(0.3f - P - 0.39f, dx, 1e-5, "ближче ліворуч");
            // двійкові дроби — обидва зсуви рівні точно (0,25)
            Assert.IsTrue(RingFrameZone.Shift(L(B(0.375f, 0f, 0.625f, 1f)), L(B(0.4375f, 0.2f, 0.5625f, 0.3f)), null, 0.0625f, -1f, 1f,
                out dx, out nice));
            Assert.AreEqual(0.25f, dx, 1e-6, "рівно посередині — праворуч, щоразу однаково");
        }

        [Test]
        public void Shift_OnlyWithinTheTurnLimit_ElseFrameStays()
        {
            var panels = L(B(0.3f, 0f, 0.6f, 1f));
            var who = L(B(0.5f, 0.2f, 0.56f, 0.3f));
            float dx; bool nice;
            Assert.IsTrue(RingFrameZone.Shift(panels, who, null, P, -0.3f, 0.1f, out dx, out nice));
            Assert.AreEqual(0.3f - P - 0.56f, dx, 1e-5, "праворуч за межею повороту — ліворуч");
            Assert.IsFalse(RingFrameZone.Shift(panels, who, null, P, -0.2f, 0.1f, out dx, out nice), "в обидва боки за межею — не влізло");
            Assert.AreEqual(0f, dx, 1e-6, "кадр як є");
        }

        /// <summary>
        /// Картка шоу на 4:3 (слот 33: афіша закриває суперника): афіша 710…1120 × 162…520 з 1200, ліва колонка лише до 266.
        /// Кожен борець — свій прямокутник; пара йде ліворуч рівно настільки, щоб суперник вийшов з-під афіші з полем.
        /// </summary>
        [Test]
        public void ShowCard_43_EachFighterFree_MinimalShift()
        {
            var panels = L(U(80, 45, 490, 151, 1200), U(80, 162, 490, 266, 1200), U(710, 45, 1120, 151, 1200), U(710, 162, 1120, 520, 1200),
                U(80, 668, 1120, 900, 1200));
            var hero = B(0.36f, 0.36f, 0.42f, 0.46f);
            var rival = B(0.56f, 0.36f, 0.62f, 0.46f);
            float dx; bool nice;
            Assert.IsFalse(RingFrameZone.IsFree(panels, rival, P), "до: суперник під афішею");
            Assert.IsTrue(RingFrameZone.Shift(panels, L(hero, rival), null, P, -0.2f, 0.2f, out dx, out nice));
            Assert.AreEqual(710f / 1200f - P - 0.62f, dx, 1e-5);
            Assert.IsTrue(RingFrameZone.IsFree(panels, hero.Moved(dx), P));
            Assert.IsTrue(RingFrameZone.IsFree(panels, rival.Moved(dx), P));
        }

        /// <summary>
        /// Кінець сезону (плейтест слоту 33: рука героя під «РОЗВИТОК»): плита розвитку 80…490 × 280…700, звіт 520 завширшки
        /// праворуч. На 16:9 герой із плечима й піднята рука вміщаються обидва; на 4:3 проміжок між плитою і звітом (9 % ширини) вужчий
        /// за плечі — не влізло, кадр лишається, сцена пише це в лог (арт-директор дивиться такі випадки окремо).
        /// </summary>
        [Test]
        public void SeasonEnd_16x9_HeroAndRaisedHand_4x3_DoesNotFit()
        {
            var p169 = L(U(80, 45, 490, 151, 1600), U(80, 162, 490, 266, 1600), U(80, 280, 490, 700, 1600), U(1000, 45, 1520, 560, 1600),
                U(80, 668, 1520, 900, 1600));
            var hero = B(0.26f, 0.18f, 0.38f, 0.30f);
            var hand = B(0.21f, 0.10f, 0.25f, 0.16f);
            float dx; bool nice;
            Assert.IsTrue(RingFrameZone.Shift(p169, L(hero), L(hand), P, -0.3f, 0.3f, out dx, out nice));
            Assert.IsTrue(nice, "на 16:9 і рука виходить з-під плити");
            Assert.AreEqual(490f / 1600f + P - 0.21f, dx, 1e-5, "найменший зсув, з яким вільна й рука");

            var p43 = L(U(80, 45, 490, 151, 1200), U(80, 162, 490, 266, 1200), U(80, 280, 490, 700, 1200), U(600, 45, 1120, 560, 1200),
                U(80, 668, 1120, 900, 1200));
            var hero43 = B(0.25f, 0.18f, 0.41f, 0.30f);
            Assert.IsFalse(RingFrameZone.Shift(p43, L(hero43), L(hand), P, -0.3f, 0.3f, out dx, out nice), "плечі ширші за проміжок");
            Assert.AreEqual(0f, dx, 1e-6);
        }

        /// <summary>
        /// Створення героя (fov 30°, 3,1 м): на 16:9 плечі (~15 % ширини) лягають між «ВИГЛЯД» і афішею; на 4:3 (~20 %) проміжок
        /// 490…710 з 1200 з полями — 13 %: не влізло (питання арт-директору — ширший план на вузьких екранах).
        /// </summary>
        [Test]
        public void Create_16x9_FitsBetweenColumns_4x3_DoesNotFit()
        {
            var p169 = L(U(80, 45, 490, 151, 1600), U(80, 162, 490, 414, 1600), U(1110, 45, 1520, 620, 1600), U(80, 668, 1520, 900, 1600));
            float dx; bool nice;
            Assert.IsTrue(RingFrameZone.Shift(p169, L(B(0.244f, 0.12f, 0.396f, 0.30f)), null, P, -0.3f, 0.3f, out dx, out nice));
            Assert.AreEqual(490f / 1600f + P - 0.244f, dx, 1e-5);
            var p43 = L(U(80, 45, 490, 151, 1200), U(80, 162, 490, 414, 1200), U(710, 45, 1120, 620, 1200), U(80, 668, 1120, 900, 1200));
            Assert.IsFalse(RingFrameZone.Shift(p43, L(B(0.22f, 0.12f, 0.42f, 0.30f)), null, P, -0.3f, 0.3f, out dx, out nice));
        }

        [Test]
        public void YawFor_MovesThePointExactlyOnTheHorizon_SignAsUnity_ShiftForYawIsItsInverse()
        {
            foreach (float aspect in new[] { 4f / 3f, 16f / 10f, 16f / 9f, 21f / 9f })
                foreach (float fov in new[] { 30f, 40f, 46f })
                    foreach (float x in new[] { 0.2f, 0.32f, 0.5f, 0.8f })
                        foreach (float dx in new[] { -0.15f, 0.07f, 0.2f })
                        {
                            float yaw = RingFrameZone.YawFor(x, dx, fov, aspect);
                            if (dx > 0f) Assert.Less(yaw, 0f, "точку праворуч — камеру ліворуч");
                            double tanH = Math.Tan(fov * 0.5 * Math.PI / 180.0) * aspect;
                            double ang = Math.Atan((2.0 * x - 1.0) * tanH) - yaw * Math.PI / 180.0;
                            float after = RingFrameZone.ScreenX((float)(Math.Sin(ang) * 4.0), (float)(Math.Cos(ang) * 4.0), fov, aspect);
                            string at = string.Format("аспект {0}, fov {1}, x {2}, dx {3}", aspect, fov, x, dx);
                            Assert.AreEqual(x + dx, after, 1e-4, at);
                            Assert.AreEqual(dx, RingFrameZone.ShiftForYaw(x, yaw, fov, aspect), 1e-4, at);
                        }
        }

        [Test]
        public void ScreenXY_CentreAndEdges()
        {
            Assert.AreEqual(0.5f, RingFrameZone.ScreenX(0f, 3f, 40f, 16f / 9f), 1e-6);
            float halfW = (float)(3.0 * Math.Tan(20.0 * Math.PI / 180.0) * 16.0 / 9.0);
            Assert.AreEqual(1f, RingFrameZone.ScreenX(halfW, 3f, 40f, 16f / 9f), 1e-5);
            Assert.AreEqual(0f, RingFrameZone.ScreenY((float)(3.0 * Math.Tan(20.0 * Math.PI / 180.0)), 3f, 40f), 1e-5, "верх кадру — y 0");
            Assert.IsTrue(float.IsNaN(RingFrameZone.ScreenX(0f, -1f, 40f, 1f)), "позаду камери");
        }
    }
}
