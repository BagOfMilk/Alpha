using System;
using System.IO;
using Game.Gameplay.Ring.Stage;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Зал і місце пари «за рингом» (<see cref="RingVenue"/>): розміри рингу — з якорів трьох залів (таблиця CAST_ANIM.md,
    /// «Ринг і місця кліпів»), смуга пари між фартухом і першим рядом — вільна від стільців, глядачів, сходів, столів і
    /// зброї з текстів залу. Огорожі й штативів світла в текстах немає — їх у грі додають трикутники сітки залу
    /// (<see cref="RingVenue.MarkTriangle"/>), правила яких перевіряються тут же на штучних трикутниках.
    /// </summary>
    public class RingVenueTests
    {
        private static string Root() { return Path.GetDirectoryName(Application.dataPath); }

        private static string Text(string venue, string kind)
        {
            return File.ReadAllText(Path.Combine(Root(), "Assets", "Art", "Ring", "venue_" + venue + "_" + kind + ".txt"));
        }

        private static RingVenue Load(string venue)
        {
            var v = RingVenue.Parse(Text(venue, "anchors"), venue);
            v.MarkAnchors();
            v.MarkProps(Text(venue, "props"));
            v.MarkCrowd(Text(venue, "crowd"));
            return v;
        }

        [TestCase("gym", 1.0f, 2.138f, 2.218f, 2.438f)]
        [TestCase("hall", 1.1f, 2.443f, 2.523f, 2.743f)]
        [TestCase("arena", 1.2f, 2.748f, 2.828f, 3.048f)]
        public void Dimensions_ComeFromAnchors(string venue, float mat, float rope, float post, float plat)
        {
            var v = RingVenue.Parse(Text(venue, "anchors"), venue);
            Assert.AreEqual(mat, v.MatY, 1e-3f, "висота мату");
            Assert.AreEqual(rope, v.RopeLine, 1e-3f, "лінія каната");
            Assert.AreEqual(post, v.PostLine, 1e-3f, "стійка");
            Assert.AreEqual(plat, v.Plat, 1e-3f, "край платформи");
        }

        [TestCase("gym", 2.24f)]
        [TestCase("hall", 2.34f)]
        [TestCase("arena", 2.44f)]
        public void TopRope_ComesFromAnchors(string venue, float top)
        {
            Assert.AreEqual(top, RingVenue.Parse(Text(venue, "anchors"), venue).TopRopeY, 1e-3f, "верхній канат");
        }

        [TestCase("gym")]
        [TestCase("hall")]
        [TestCase("arena")]
        public void CamOverRopes_SightToFeetClearsTopRope(string venue)
        {
            // Середній план бою (RingStage3D, StageCam.Close): камера на (1,3; мат + 2,75; −7,4) від пари; пара в центрі й біля
            // ближніх канатів. Лінія до стоп після підйому — над верхнім канатом там, де входить у квадрат канатів.
            var v = RingVenue.Parse(Text(venue, "anchors"), venue);
            foreach (float pz in new[] { 0f, -1.5f, 1.5f })
                foreach (float px in new[] { -1.2f, 0f, 1.2f })
                {
                    float cx = px + 1.3f, cz = pz - 7.4f, cy0 = v.MatY + 2.75f;
                    float cy = v.CamYOverRopes(cx, cy0, cz, px, pz);
                    Assert.GreaterOrEqual(cy, cy0, "камера не опускається");
                    float t = (-v.RopeLine - cz) / (pz - cz);
                    float h = cy + (v.MatY + 0.05f - cy) * t;
                    Assert.GreaterOrEqual(h, v.TopRopeY + 0.079f, string.Format("{0}: пара ({1}; {2}), камера {3:0.00}", venue, px, pz, cy));
                }
        }

        [Test]
        public void CamOverRopes_InsideRingOrHighCam_Unchanged()
        {
            var v = RingVenue.Parse(Text("arena", "anchors"), "arena");
            Assert.AreEqual(1.9f, v.CamYOverRopes(1.0f, 1.9f, -1.5f, 0f, 0f), 1e-5f, "камера в канатах");
            Assert.AreEqual(9.0f, v.CamYOverRopes(0f, 9.0f, -8.2f, 0f, 0f), 1e-5f, "висока камера");
            Assert.AreEqual(2.0f, v.CamYOverRopes(0f, 2.0f, -8.2f, 0f, -4.0f), 1e-5f, "борець за канатами");
        }

        [Test]
        public void Gym_RingHalf_MatchesStageConstant()
        {
            // RingStage3D.RingHalf (2,038) — бійцівський квадрат спортзалу, з яким досі жили режисер і сторож сцени.
            Assert.AreEqual(2.038f, RingVenue.Parse(Text("gym", "anchors"), "gym").RingHalf, 1e-3f);
        }

        [Test]
        public void EmptyText_FallsBackToGym()
        {
            var v = RingVenue.Parse(null);
            Assert.AreEqual(1.0f, v.MatY, 1e-4f);
            Assert.AreEqual(2.438f, v.Plat, 1e-4f);
            Assert.AreEqual(2.138f, v.RopeLine, 1e-4f);
        }

        [TestCase("gym", "E")]
        [TestCase("hall", "S")]   // схід — стіл коментаторів, захід — хронометрист і кендо
        [TestCase("arena", "E")]
        public void Outside_PicksAFreeSide_FullLength(string venue, string side)
        {
            var v = Load(venue);
            var s = v.PlanOutside();
            Assert.IsTrue(s.Ok, venue + ": смуга за рингом зайнята (" + s.Blocked + " м)");
            Assert.AreEqual(side, s.Side, venue + ": бік");
            Assert.AreEqual(RingVenue.HalfLengths[0], s.HalfLength, 1e-4f, venue + ": смуга скорочена");
            Assert.LessOrEqual(Math.Abs(s.Shift), RingVenue.MaxShift + 1e-4f);
            Assert.AreEqual(v.Plat + RingVenue.PairOut, s.Dist, 1e-4f);
        }

        [TestCase("gym")]
        [TestCase("hall")]
        [TestCase("arena")]
        public void Outside_PairStandsBetweenApronAndFirstRow(string venue)
        {
            var v = Load(venue);
            var s = v.PlanOutside();
            foreach (bool player in new[] { true, false })
            {
                float x, z;
                RingVenue.PairPoint(s, player, out x, out z);
                float r = Math.Max(Math.Abs(x), Math.Abs(z));
                Assert.Greater(r, v.Plat + 0.5f, venue + ": борець біля фартуха");
                Assert.Less(r, v.Plat + RingVenue.BandOut - 0.4f, venue + ": борець біля першого ряду");
                // Від борця до найближчих стільця, глядача, сходів чи столу — не менше 0,7 м (кліпи пари з бічним зсувом).
                Assert.GreaterOrEqual(v.Clearance(x, z, 2f), 0.7f, venue + ": " + (player ? "гравець" : "суперник") + " упритул до перешкоди");
            }
            // Гравець і суперник — уздовж боку, як на рингу: 1,7 м один від одного.
            float px, pz, rx, rz;
            RingVenue.PairPoint(s, true, out px, out pz);
            RingVenue.PairPoint(s, false, out rx, out rz);
            Assert.AreEqual(2f * RingVenue.PairApart, Math.Sqrt((px - rx) * (px - rx) + (pz - rz) * (pz - rz)), 1e-3f);
        }

        [TestCase("gym")]
        [TestCase("hall")]
        [TestCase("arena")]
        public void Outside_RefereeOnTheApron_ClearOfThePair(string venue)
        {
            var v = Load(venue);
            var s = v.PlanOutside();
            float x, z;
            RingVenue.RefPoint(s, out x, out z);
            float r = Math.Max(Math.Abs(x), Math.Abs(z));
            // На фартуху: за канатами, на лінії фартуха, навпроти центру пари.
            Assert.AreEqual(v.Plat - RingVenue.ApronIn, r, 1e-3f, venue + ": рефері не на лінії фартуха");
            Assert.Greater(r, v.RopeLine, venue + ": рефері всередині канатів");
            Assert.AreEqual(0f, (x - s.X) * s.AlongX + (z - s.Z) * s.AlongZ, 1e-3f, venue + ": рефері не навпроти центру пари");
            // Камера «за рингом» крізь квадрат рингу не дивиться — увесь тулуб рефері в ньому, пару він не закриває.
            Assert.LessOrEqual(r + RingVenue.RefBody, v.RingSightHalf, venue + ": тулуб рефері поза квадратом рингу");
            // Тіла пари (кидок, підйом — до 0,9 м від тазу) до тулуба рефері не дістають.
            Assert.GreaterOrEqual(s.Dist - (r + RingVenue.RefBody), RingVenue.PairReach, venue + ": тіла пари дістають рефері");
            foreach (bool player in new[] { true, false })
            {
                float px, pz;
                RingVenue.PairPoint(s, player, out px, out pz);
                double d = Math.Sqrt((px - x) * (px - x) + (pz - z) * (pz - z));
                Assert.GreaterOrEqual(d, 1.0, venue + ": рефері впритул до " + (player ? "гравця" : "суперника"));
            }
        }

        [Test]
        public void Hall_EastIsBlockedByCommentary()
        {
            var v = Load("hall");
            float x, y, z;
            Assert.IsTrue(v.TryAnchor("anchor_commentary", out x, out y, out z));
            Assert.Greater(v.TopAt(x, z), 0f, "стіл коментаторів не позначено");
            // Смуга сходу — та, що обрав би план без столу.
            var east = new RingVenue.OutsideSpot { Side = "E", OutX = 1f, AlongZ = 1f, Dist = v.Plat + RingVenue.PairOut, HalfLength = 2f };
            float x0, z0, x1, z1;
            east.Rect(v.Plat, 0f, out x0, out z0, out x1, out z1);
            Assert.Greater(v.TopIn(x0, z0, x1, z1), 0f);
        }

        [Test]
        public void Steps_BlockTheirCorner()
        {
            var v = Load("gym");
            float x, y, z;
            Assert.IsTrue(v.TryAnchor("anchor_steps_SE", out x, out y, out z));
            // Посередині між кутом платформи й якорем сходів.
            Assert.Greater(v.TopAt((x + v.Plat) * 0.5f, (z - v.Plat) * 0.5f), 0f);
        }

        [Test]
        public void CameraOperators_AreSoft()
        {
            var v = Load("gym");
            Assert.AreEqual(5, v.SoftCount, "операторів у спортзалі п'ятеро");
            // Оператор camop_2 (3,15; 1,4) стоїть у смузі сходу — у вибір місця не входить.
            Assert.AreEqual(0f, v.TopAt(3.15f, 1.4f), 1e-4f);
            var s = v.PlanOutside();
            float x0, z0, x1, z1;
            s.Rect(v.Plat, 0.35f, out x0, out z0, out x1, out z1);
            Assert.GreaterOrEqual(v.SoftIn(x0, z0, x1, z1), 1, "оператор у смузі — сцена має сховати");
        }

        [Test]
        public void Triangle_ProjectionNotBoundingBox()
        {
            var v = RingVenue.Parse(Text("arena", "anchors"), "arena");
            // Довгий діагональний трикутник трибун у куті чаші: габарит накрив би весь коридор, проєкція — смужка в куті.
            v.MarkTriangle(-20f, 2f, 6.5f, 6.5f, 2f, -20f, 6.4f, 5f, -19.8f);
            Assert.AreEqual(0f, v.TopAt(v.Plat + 1f, 0f), 1e-4f, "коридор сходу накрито габаритом");
            Assert.AreEqual(0f, v.TopAt(-v.Plat - 1f, 0f), 1e-4f, "коридор заходу накрито габаритом");
            Assert.Greater(v.TopAt(-6.75f, -6.75f), 0f, "сам трикутник не позначено");
        }

        [Test]
        public void Triangle_VerticalWallMarksItsLine()
        {
            var v = RingVenue.Parse(Text("hall", "anchors"), "hall");
            // Секція огорожі: вертикальний трикутник уздовж x = 4,943 (проєкція — відрізок).
            v.MarkTriangle(4.943f, 0f, -2f, 4.943f, 1.1f, -2f, 4.943f, 0f, 2f);
            Assert.Greater(v.TopAt(4.943f, 0f), 0f);
            Assert.AreEqual(0f, v.TopAt(4.5f, 0f), 1e-4f);
        }

        [Test]
        public void Triangle_LowIsGround_RingIsIgnored()
        {
            var v = RingVenue.Parse(Text("gym", "anchors"), "gym");
            // Мат довкола рингу (4 см) — підлога, на ньому стоять.
            v.MarkTriangle(2.5f, 0.04f, -1f, 4.0f, 0.04f, -1f, 4.0f, 0.04f, 1f);
            Assert.AreEqual(0f, v.TopAt(3.5f, 0f), 1e-4f);
            Assert.AreEqual(0.04f, v.GroundAt(3.5f, 0f), 1e-4f);
            // Стійка рингу (усередині платформи) — не перешкода.
            v.MarkTriangle(2.2f, 1f, 2.2f, 2.25f, 2.3f, 2.2f, 2.2f, 2.3f, 2.25f);
            Assert.AreEqual(0f, v.TopIn(-v.Plat, -v.Plat, v.Plat, v.Plat), 1e-4f);
            // Ферма над рингом — над головою.
            v.MarkTriangle(-5f, 6f, 3.5f, 5f, 6f, 3.5f, 0f, 6f, 4f);
            Assert.AreEqual(0f, v.TopAt(0f, 3.6f), 1e-4f);
        }

        [Test]
        public void OutsideCamera_NotThroughTheCornerPost()
        {
            // Огляд п. 8 (09.11.2026): камера на сусідньому боці дивилася на пару крізь кутову стійку — у кадрі стійка й подушки.
            var v = Load("hall");
            var s = v.PlanOutside();
            Assert.AreEqual("S", s.Side);
            float pairY = v.GroundAt(s.X, s.Z) + 1f;
            // Камера зі сходу біля кута SE (так її ставив план у клубній залі), лінія до пари йде повз стійку (2,52; −2,52).
            Assert.IsTrue(RingVenue.SegmentHitsSquare(3.94f, -2.18f, s.X, s.Z, v.RingSightHalf));
            Assert.IsFalse(v.SeesOutside(3.94f, 2.49f, -2.18f, s.X, pairY, s.Z), "лінія погляду крізь кут рингу");
            Assert.IsFalse(v.OutsideCameraOk(3.94f, 2.49f, -2.18f, s.X, s.Z), "крізь кут рингу");
            // Та сама сторона, біля фартуха, — вільно.
            Assert.IsTrue(v.SeesOutside(2.3f, 2.4f, -v.Plat - 0.3f, s.X, pairY, s.Z), "біля фартуха свого боку");
            // Квадрат рингу: точка всередині й відрізок, що його оминає.
            Assert.IsTrue(RingVenue.SegmentHitsSquare(-5f, 0f, 5f, 0f, 2f));
            Assert.IsFalse(RingVenue.SegmentHitsSquare(-5f, 3f, 5f, 3f, 2f));
            Assert.IsTrue(RingVenue.SegmentHitsSquare(0.5f, 0.5f, 9f, 9f, 2f));
            Assert.IsFalse(RingVenue.SegmentHitsSquare(3f, -4f, 4f, 3f, 2f));
        }

        [Test]
        public void OutsideCamera_KeepsTheCornerPostOutOfTheForeground()
        {
            var v = Load("gym");
            float h = v.PostLine;
            // Пара на півночі (z = Plat + 1). Камера біля кута NE (за 0,6 м від стійки), дивиться вздовж боку на пару — стійка в кадрі
            // ближче за пару (як кадр 172 огляду 8b).
            float cx = h + 0.4f, cz = h + 0.45f, px = 0.3f, pz = v.Plat + 1f;
            Assert.IsTrue(v.PostInView(cx, cz, px - cx, pz - cz, 33f, Dist(cx, cz, px, pz)), "стійка біля камери");
            // Та сама камера, але кадр вузький і повернутий назовні, від стійки, — стійка поза полем зору.
            Assert.IsFalse(v.PostInView(cx, cz + 0.6f, -0.2f, 1f, 20f, Dist(cx, cz + 0.6f, px, pz)), "стійка збоку поза кадром");
            // Стійка позаду камери.
            Assert.IsFalse(v.PostInView(h - 0.8f, h + 0.6f, -1f, 0.3f, 33f, 3f), "стійка позаду");
            // Стійка далі за пару (тло) — можна, хоч і в полі зору: NE — 7° від осі кадру, але за 4,2 м проти 1,95 до пари
            // (відсіює відстань, не кут).
            Assert.IsFalse(v.PostInView(-1.5f, 4.2f, 1f, -0.4f, 33f, Dist(-1.5f, 4.2f, px, pz)), "стійки NE і NW далі за пару (тло)");
            // Та сама стійка NE, коли пара далі за неї, — передній план (кут — у полі зору).
            Assert.IsTrue(v.PostInView(-1.5f, 4.2f, 1f, -0.4f, 33f, 6f), "стійка NE ближче за пару");
            // Камера впритул до стійки — завжди передній план.
            Assert.IsTrue(v.PostInView(h + 0.2f, h + 0.1f, 0f, 1f, 10f, 5f), "камера в подушці");
        }

        private static float Dist(float ax, float az, float bx, float bz)
        {
            return (float)Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));
        }

        [Test]
        public void OutsideCamera_FigureCutByTheFrameEdge()
        {
            const float aspect = 16f / 9f;
            // Камера на висоті 1 м за 5 м дивиться вздовж +Z: постать посередині — уся в кадрі.
            Assert.IsFalse(RingVenue.FigureCut(0f, 1f, -5f, 0f, 1f, 0f, 40f, aspect, 0f, 0f, 0f, 1.85f, 0.2f), "уся в кадрі");
            // Далеко праворуч — уся поза кадром.
            Assert.IsFalse(RingVenue.FigureCut(0f, 1f, -5f, 0f, 1f, 0f, 40f, aspect, 10f, 0f, 0f, 1.85f, 0.2f), "уся поза кадром");
            // Позаду камери — поза кадром.
            Assert.IsFalse(RingVenue.FigureCut(0f, 1f, -5f, 0f, 1f, 0f, 40f, aspect, 0f, 0f, -8f, 1.85f, 0.2f), "позаду камери");
            // На бічному краї (півширина кадру на 5 м — tan 20° · 16/9 · 5 ≈ 3,24 м) — розрізана.
            float edge = (float)(Math.Tan(20.0 * Math.PI / 180.0) * aspect * 5.0);
            Assert.IsTrue(RingVenue.FigureCut(0f, 1f, -5f, 0f, 1f, 0f, 40f, aspect, edge, 0f, 0f, 1.85f, 0.2f), "бічний край");
            // Стоїть на фартуху (0,5 м) за 3 м: стопи в кадрі, голова (2,35 м) вище верхнього краю (2,09 м) — без голови.
            Assert.IsTrue(RingVenue.FigureCut(0f, 1f, -3f, 0f, 1f, 0f, 40f, aspect, 0f, 0.5f, 0f, 1.85f, 0.2f), "без голови");
        }

        [Test]
        public void OutsideCamera_SeesOverALowBarrier_NotOverATable()
        {
            var v = RingVenue.Parse(Text("gym", "anchors"), "gym");
            float pz = v.Plat + RingVenue.PairOut;
            // Огорожа 1,04 м за 0,7 м від пари (як стійки огорожі спортзалу), камера ще 0,3 м за нею.
            v.MarkBox(-4f, pz + 0.65f, 4f, pz + 0.75f, 1.04f);
            Assert.IsTrue(v.SeesOutside(0.5f, 2.8f, pz + 1.05f, 0f, 0.3f, pz), "з висоти 2,8 м над низькою огорожею видно навіть того, хто лежить");
            Assert.IsFalse(v.SeesOutside(0.5f, 1.25f, pz + 1.05f, 0f, 0.3f, pz), "з 1,25 м огорожа закриває того, хто лежить");
            // Високий стіл (2,0 м) посередині між камерою й парою (впритул до камери перешкоди ловить її коло — TopDisc).
            v.MarkBox(-4f, pz + 0.45f, 4f, pz + 0.55f, 2.0f);
            Assert.IsFalse(v.SeesOutside(0.5f, 2.8f, pz + 1.15f, 0f, 1f, pz), "крізь високий стіл");
        }

        [Test]
        public void Outside_NarrowsTheBand_WhenTheBarrierIsClose()
        {
            // Спортзал у грі: стійки огорожі (1,04 м) за ~1,6–1,85 м від краю платформи по всіх боках — смуга [0,15; 1,85] ніде не
            // вільна (огляд п. 8: «ВІЛЬНОЇ СМУГИ НЕМАЄ», бік N). Смуга звужується назовні, центр пари не рухається.
            var v = Load("gym");
            float b0 = v.Plat + 1.62f, b1 = v.Plat + 1.84f, e = b1 + 0.2f;
            v.MarkBox(-e, b0, e, b1, 1.04f);
            v.MarkBox(-e, -b1, e, -b0, 1.04f);
            v.MarkBox(b0, -e, b1, e, 1.04f);
            v.MarkBox(-b1, -e, -b0, e, 1.04f);
            var s = v.PlanOutside();
            Assert.IsTrue(s.Ok, "смуга не знайшлась (" + s.Blocked + " м у " + s.BlockedX + "; " + s.BlockedZ + ")");
            Assert.Less(s.OuterEdge, RingVenue.BandOut, "смуга мала звузитись");
            Assert.LessOrEqual(s.OuterEdge, 1.62f, "межа смуги в огорожі");
            Assert.AreEqual(v.Plat + RingVenue.PairOut, s.Dist, 1e-4f, "центр пари зсунувся");
            // Вільна півширина назовні від центру пари — до зовнішньої межі (ближчої), не половина смуги.
            Assert.AreEqual(s.OuterEdge - RingVenue.PairOut, s.HalfWidth, 1e-4f, "півширина звуженої смуги");
            float x0, z0, x1, z1;
            s.Rect(v.Plat, 0f, out x0, out z0, out x1, out z1);
            Assert.AreEqual(0f, v.TopIn(x0, z0, x1, z1), 1e-4f);
            // Рефері — на фартуху, як і в повній смузі: звуження смуги його не підсуває до пари (огляд 8b — за парою на підлозі
            // у звуженій смузі він стояв за 0,35 м від лінії пари, і тіло з боді-слему проходило крізь нього).
            float rx, rz;
            RingVenue.RefPoint(s, out rx, out rz);
            Assert.AreEqual(v.Plat - RingVenue.ApronIn, Math.Max(Math.Abs(rx), Math.Abs(rz)), 1e-3f, "рефері не на фартуху");
            foreach (bool player in new[] { true, false })
            {
                float px, pz;
                RingVenue.PairPoint(s, player, out px, out pz);
                Assert.GreaterOrEqual(Math.Sqrt((px - rx) * (px - rx) + (pz - rz) * (pz - rz)), 1.0, "рефері впритул до пари");
            }
        }

        [Test]
        public void Outside_FullBand_KeepsTheOldOuterEdge()
        {
            foreach (string venue in new[] { "gym", "hall", "arena" })
            {
                var s = Load(venue).PlanOutside();
                Assert.AreEqual(RingVenue.BandOut, s.OuterEdge, 1e-4f, venue);
                Assert.AreEqual(0.5f * (RingVenue.BandOut - RingVenue.BandIn), s.HalfWidth, 1e-4f, venue + ": півширина повної смуги");
            }
        }

        [TestCase("gym")]
        [TestCase("hall")]
        [TestCase("arena")]
        public void CornerSpot_HeroCorner_IsFreeAndNearTheCorner(string venue)
        {
            // Менеджерка в кутку героя (рішення власника 12.11.2026, № 8): біля північно-західного кута, на підлозі поза
            // платформою, не на сходах, столах, стільцях, глядачах і операторах, не на шляху виходу й поза смугою пари.
            var v = Load(venue);
            var pair = v.PlanOutside();
            float x, z;
            Assert.IsTrue(v.CornerSpot(pair, -1f, 1f, out x, out z), venue + ": біля кута героя немає вільного місця");
            TestContext.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}: місце ({1:0.00}; {2:0.00}), смуга пари — бік {3}", venue, x, z, pair.Side));
            Assert.Less(x, 0f, "захід");
            Assert.Greater(z, 0f, "північ");
            float dx = x + v.Plat, dz = z - v.Plat;
            Assert.LessOrEqual(Math.Sqrt(dx * dx + dz * dz), 2.0, venue + ": далеко від кута");
            Assert.GreaterOrEqual(Math.Max(Math.Abs(x), Math.Abs(z)), v.Plat + RingVenue.CornerOutMin - 1e-3f, "на платформі");
            Assert.AreEqual(0f, v.TopDisc(x, z, RingVenue.CornerBody), "у перешкоді");
            Assert.GreaterOrEqual(RingVenue.PathDist(v.PathPoints(), x, z), RingVenue.CornerPathClear, "на шляху виходу");
            Assert.IsFalse(v.NearSoft(x, z, RingVenue.CornerBody), "на операторі");
            float x0, z0, x1, z1;
            pair.Rect(v.Plat, RingVenue.CornerPairPad - 1e-3f, out x0, out z0, out x1, out z1);
            Assert.IsFalse(x >= x0 && x <= x1 && z >= z0 && z <= z1, "у смузі пари");
        }

        [TestCase("gym")]
        [TestCase("hall")]
        [TestCase("arena")]
        public void PathPoints_EndAtTheEntranceSteps(string venue)
        {
            var v = Load(venue);
            var path = v.PathPoints();
            Assert.GreaterOrEqual(path.Count, 3, venue + ": шлях виходу з якорів");
            var last = path[path.Count - 1];
            float sx, sy, sz;
            bool any = false;
            foreach (var n in v.AnchorNames)
                if (n.StartsWith("anchor_steps_", StringComparison.Ordinal) && v.TryAnchor(n, out sx, out sy, out sz)
                    && Math.Abs(sx - last[0]) < 1e-4f && Math.Abs(sz - last[1]) < 1e-4f) any = true;
            Assert.IsTrue(any, venue + ": шлях кінчається на сходах");
        }

        [Test]
        public void CornerSpot_MovesAwayFromObstacle_AndGivesUpWhenAllTaken()
        {
            var v = RingVenue.Parse(null);
            var pair = new RingVenue.OutsideSpot { Side = "E", OutX = 1f, AlongZ = 1f, Dist = v.Plat + RingVenue.PairOut, HalfLength = 2f, Outer = RingVenue.BandOut };
            float x, z;
            Assert.IsTrue(v.CornerSpot(pair, -1f, 1f, out x, out z));
            Assert.AreEqual(-(v.Plat + RingVenue.CornerOutMin), x, 1e-3f, "найкраще — біля самого кута, впритул до спідниці");
            Assert.AreEqual(v.Plat, z, 1e-3f);
            // Стіл на найкращому місці — місце відходить і не зачіпає стола.
            v.MarkDisc(x, z, 0.4f, 0.9f);
            float x2, z2;
            Assert.IsTrue(v.CornerSpot(pair, -1f, 1f, out x2, out z2));
            Assert.AreEqual(0f, v.TopDisc(x2, z2, RingVenue.CornerBody), "у перешкоді");
            // Уся підлога біля кута зайнята — місця немає (сцена тоді нікого не ставить і пише в журнал).
            v.MarkBox(-v.Plat - 2f, -v.Plat - 2f, v.Plat + 2f, v.Plat + 2f, 1.0f);
            Assert.IsFalse(v.CornerSpot(pair, -1f, 1f, out x2, out z2));
            // Смуга пари на заході — місце не в ній.
            var w = RingVenue.Parse(null);
            var west = new RingVenue.OutsideSpot { Side = "W", OutX = -1f, AlongZ = 1f, Dist = w.Plat + RingVenue.PairOut, HalfLength = 2f, Outer = RingVenue.BandOut };
            Assert.IsTrue(w.CornerSpot(west, -1f, 1f, out x, out z));
            float x0, z0, x1, z1;
            west.Rect(w.Plat, RingVenue.CornerPairPad - 1e-3f, out x0, out z0, out x1, out z1);
            Assert.IsFalse(x >= x0 && x <= x1 && z >= z0 && z <= z1, "у смузі пари");
        }

        [Test]
        public void OutsideCamera_StaysOnTheFloorSideOfTheBarrier()
        {
            var v = Load("hall");
            // Огорожа півдня (у грі — з сітки залу).
            v.MarkBox(-4.943f, -4.99f, 4.943f, -4.90f, 1.1f);
            var s = v.PlanOutside();
            float pairX = s.X, pairZ = s.Z;
            Assert.IsTrue(v.OutsideCameraOk(1.5f, 2.2f, -v.Plat - 0.5f, pairX, pairZ), "у коридорі між фартухом і огорожею");
            Assert.IsFalse(v.OutsideCameraOk(0f, 2.2f, -1.0f, pairX, pairZ), "у рингу");
            Assert.IsFalse(v.OutsideCameraOk(1.5f, 2.2f, -v.Plat + 0.1f, pairX, pairZ), "над платформою");
            Assert.IsFalse(v.OutsideCameraOk(1.5f, 0.8f, -v.Plat - 0.5f, pairX, pairZ), "занизько");
            Assert.IsFalse(v.OutsideCameraOk(1.5f, 2.2f, -v.Plat - 2.4f, pairX, pairZ), "за огорожею в трибунах");
        }
    }
}
