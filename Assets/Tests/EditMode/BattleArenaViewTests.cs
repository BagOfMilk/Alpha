using Game.Gameplay;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Пакет E2: чиста математика показу бою — тайл→світ, кадрування камери,
    /// підсвітка тайлів, детермінована палітра юнітів. Той самий принцип, що
    /// й <c>VillageViewTests</c>: поведінка показу перевіряється тестами, а
    /// не очима на скріншоті.
    /// </summary>
    public class BattleArenaViewTests
    {
        // ================= тайл → світ =================

        [Test]
        public void TileToWorld_CentersEachCell()
        {
            var p00 = BattleArenaView.TileToWorld(0, 0);
            var p10 = BattleArenaView.TileToWorld(1, 0);
            var p01 = BattleArenaView.TileToWorld(0, 1);

            Assert.AreEqual(0.5f, p00.X, 1e-5f);
            Assert.AreEqual(0.5f, p00.Z, 1e-5f);
            Assert.AreEqual(0f, p00.Y, 1e-5f, "Арена пласка — Y завжди 0");

            Assert.AreEqual(1.5f, p10.X, 1e-5f, "Сусідня клітина по X зсунута рівно на розмір тайла");
            Assert.AreEqual(1.5f, p01.Z, 1e-5f, "Сусідня клітина по Z зсунута рівно на розмір тайла");
        }

        [Test]
        public void TileToWorld_ScalesWithTileSize()
        {
            var p = BattleArenaView.TileToWorld(2, 3, tileSize: 2f);
            Assert.AreEqual(5f, p.X, 1e-5f);
            Assert.AreEqual(7f, p.Z, 1e-5f);
        }

        // ================= кадрування камери =================

        [Test]
        public void FrameGrid_CentersOnGridMiddle()
        {
            var frame = BattleArenaView.FrameGrid(8, 8);
            Assert.AreEqual(4f, frame.CenterX, 1e-5f);
            Assert.AreEqual(4f, frame.CenterZ, 1e-5f);
        }

        [Test]
        public void FrameGrid_LargerGrid_NeedsLargerOrthographicSize()
        {
            var small = BattleArenaView.FrameGrid(8, 8);
            var large = BattleArenaView.FrameGrid(10, 10);
            Assert.Greater(large.OrthographicSize, small.OrthographicSize);
        }

        [Test]
        public void FrameGrid_UsesLongerSide_ForNonSquareGrids()
        {
            var wide = BattleArenaView.FrameGrid(10, 4);
            var tall = BattleArenaView.FrameGrid(4, 10);
            Assert.AreEqual(wide.OrthographicSize, tall.OrthographicSize, 1e-5f,
                "Розмір кадру залежить від довшої сторони — ширша й вища арени тієї ж довшої сторони кадруються однаково");
        }

        [Test]
        public void FrameGrid_DegenerateSize_DoesNotThrow_AndStaysPositive()
        {
            var frame = BattleArenaView.FrameGrid(0, 0);
            Assert.Greater(frame.OrthographicSize, 0f);
        }

        // ================= підсвітка тайлів =================

        [Test]
        public void TintFor_NonWalkable_AlwaysWins()
        {
            var tint = BattleArenaView.TintFor("Full", walkable: false, isReachable: true, isCurrentUnit: true, isHovered: true);
            var floor = BattleArenaView.TintFor("None", walkable: true, isReachable: false, isCurrentUnit: false, isHovered: false);
            Assert.AreNotEqual(floor.R, tint.R);
        }

        [Test]
        public void TintFor_PriorityOrder_CurrentBeatsHoveredBeatsReachableBeatsCover()
        {
            var current = BattleArenaView.TintFor("Full", true, isReachable: true, isCurrentUnit: true, isHovered: true);
            var hovered = BattleArenaView.TintFor("Full", true, isReachable: true, isCurrentUnit: false, isHovered: true);
            var reachable = BattleArenaView.TintFor("Full", true, isReachable: true, isCurrentUnit: false, isHovered: false);
            var cover = BattleArenaView.TintFor("Full", true, isReachable: false, isCurrentUnit: false, isHovered: false);

            Assert.AreNotEqual(current, hovered);
            Assert.AreNotEqual(hovered, reachable);
            Assert.AreNotEqual(reachable, cover);
        }

        [Test]
        public void TintFor_DistinguishesHalfAndFullCover_WhenNoHighlight()
        {
            var half = BattleArenaView.TintFor("Half", true, false, false, false);
            var full = BattleArenaView.TintFor("Full", true, false, false, false);
            var none = BattleArenaView.TintFor("None", true, false, false, false);

            Assert.AreNotEqual(half, full);
            Assert.AreNotEqual(half, none);
            Assert.AreNotEqual(full, none);
        }

        // ================= палітра юнітів =================

        [Test]
        public void CharacterTint_IsDeterministic_ForTheSameId()
        {
            var a = BattleArenaView.CharacterTint("u_maksym", "Player", "u_maksym");
            var b = BattleArenaView.CharacterTint("u_maksym", "Player", "u_maksym");
            Assert.AreEqual(a.R, b.R, 1e-6f);
            Assert.AreEqual(a.G, b.G, 1e-6f);
            Assert.AreEqual(a.B, b.B, 1e-6f);
        }

        [Test]
        public void CharacterTint_NamedCompanions_AreMutuallyDistinct()
        {
            var protagonist = BattleArenaView.CharacterTint("u_protagonist", "Player", "u_protagonist");
            var maksym = BattleArenaView.CharacterTint("u_maksym", "Player", "u_maksym");
            var myroslava = BattleArenaView.CharacterTint("u_myroslava", "Player", "u_myroslava");
            var zakhar = BattleArenaView.CharacterTint("u_zakhar", "Player", "u_zakhar");

            AssertDistinct(protagonist, maksym);
            AssertDistinct(protagonist, myroslava);
            AssertDistinct(protagonist, zakhar);
            AssertDistinct(maksym, myroslava);
            AssertDistinct(maksym, zakhar);
            AssertDistinct(myroslava, zakhar);
        }

        [Test]
        public void CharacterTint_EnemyFactions_AreDistinctBands()
        {
            var horde = BattleArenaView.CharacterTint("horde_scout_0", "Enemy", "horde_scout");
            var boyar = BattleArenaView.CharacterTint("tuhar_boyar_0", "Enemy", "tuhar_boyar");
            var burunda = BattleArenaView.CharacterTint("burunda_0", "Enemy", "burunda");

            AssertDistinct(horde, boyar);
            AssertDistinct(horde, burunda);
            AssertDistinct(boyar, burunda);
        }

        [Test]
        public void CharacterTint_FromDefector_IsMarked_ButKeepsCompanionColor()
        {
            var asAlly = BattleArenaView.CharacterTint("u_myroslava", "Player", "u_myroslava");
            var asDefector = BattleArenaView.CharacterTint("defector_myroslava", "FromDefector", "myroslava");

            Assert.IsFalse(asAlly.Marked);
            Assert.IsTrue(asDefector.Marked, "FromDefector завжди позначений — контракт §5.7 (зрадниця у фіналі)");
            Assert.AreEqual(asAlly.R, asDefector.R, 1e-5f, "Той самий персонаж — той самий базовий колір, позначка не колір");
            Assert.AreEqual(asAlly.G, asDefector.G, 1e-5f);
            Assert.AreEqual(asAlly.B, asDefector.B, 1e-5f);
        }

        [Test]
        public void Hash01_StaysInUnitRange_AndIsStable()
        {
            for (int i = 0; i < 50; i++)
            {
                string id = "enemy_" + i;
                float h1 = BattleArenaView.Hash01(id);
                float h2 = BattleArenaView.Hash01(id);
                Assert.AreEqual(h1, h2, 1e-6f);
                Assert.GreaterOrEqual(h1, 0f);
                Assert.Less(h1, 1f);
            }
        }

        [Test]
        public void HsvToRgb_PureRed_AtHueZero()
        {
            BattleArenaView.HsvToRgb(0f, 1f, 1f, out float r, out float g, out float b);
            Assert.AreEqual(1f, r, 1e-5f);
            Assert.AreEqual(0f, g, 1e-5f);
            Assert.AreEqual(0f, b, 1e-5f);
        }

        [Test]
        public void HsvToRgb_ZeroSaturation_IsGray()
        {
            BattleArenaView.HsvToRgb(0.37f, 0f, 0.6f, out float r, out float g, out float b);
            Assert.AreEqual(r, g, 1e-5f);
            Assert.AreEqual(g, b, 1e-5f);
            Assert.AreEqual(0.6f, r, 1e-5f);
        }

        // ================= AP-бар =================

        [Test]
        public void ReservedFraction_ZeroMax_DoesNotDivideByZero()
        {
            Assert.AreEqual(0f, BattleArenaView.ReservedFraction(0, 0));
        }

        [Test]
        public void ReservedFraction_IsShareOfMax()
        {
            Assert.AreEqual(0.25f, BattleArenaView.ReservedFraction(8, 2), 1e-5f);
        }

        [Test]
        public void FilledFraction_ClampsAboveMax()
        {
            Assert.AreEqual(1f, BattleArenaView.FilledFraction(12, 8), "AP понад максимум (бафи) не переповнює смужку за межі 1.0");
        }

        private static void AssertDistinct(PaletteColor a, PaletteColor b)
        {
            bool same = System.Math.Abs(a.R - b.R) < 1e-4f
                      && System.Math.Abs(a.G - b.G) < 1e-4f
                      && System.Math.Abs(a.B - b.B) < 1e-4f;
            Assert.IsFalse(same, "Кольори мали відрізнятися, а вийшли однакові");
        }
    }
}
