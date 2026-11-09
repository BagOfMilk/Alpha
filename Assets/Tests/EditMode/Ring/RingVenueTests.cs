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
