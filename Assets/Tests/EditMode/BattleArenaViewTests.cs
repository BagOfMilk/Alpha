using System;
using Game.Core.Characters.Creation;
using Game.Core.Combat;
using Game.Gameplay;
using Game.Gameplay.Text;
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

        // ================= стани бою: значок -> ключ UkrainianText =================

        /// <summary>
        /// Дебаг §6.1 №32 (24.09.2026): <c>combat.status.*</c> ключі в
        /// UkrainianText існували з пакету E3, але <c>StatusLabelKey</c> (і
        /// HUD, що його читає) з'явились лише тепер — кожне значення
        /// <see cref="StatusType"/> (крім <see cref="StatusType.None"/>) мусить
        /// мати мапінг на ІСНУЮЧИЙ ключ, інакше гравець побачить порожній
        /// значок або (гірше) нічого замість накладеного стану.
        /// </summary>
        [TestCase(StatusType.Bleeding)]
        [TestCase(StatusType.Stunned)]
        [TestCase(StatusType.Suppressed)]
        [TestCase(StatusType.KnockedDown)]
        [TestCase(StatusType.Marked)]
        [TestCase(StatusType.Burning)]
        [TestCase(StatusType.Poisoned)]
        public void StatusLabelKey_MapsToExistingUkrainianTextKey(StatusType status)
        {
            string key = BattleArenaView.StatusLabelKey(status.ToString());
            Assert.IsNotNull(key, $"{status}: StatusLabelKey не мав повернути null для реалізованого стану");
            Assert.IsTrue(UkrainianText.Has(key, Gender.Male), $"{status}: ключ '{key}' відсутній у UkrainianText");
            Assert.IsTrue(UkrainianText.Has(key, Gender.Female), $"{status}: ключ '{key}' відсутній у UkrainianText (жін.)");
        }

        [Test]
        public void StatusLabelKey_UnknownName_ReturnsNull()
        {
            Assert.IsNull(BattleArenaView.StatusLabelKey("НевідомийСтан"));
            Assert.IsNull(BattleArenaView.StatusLabelKey(StatusType.None.ToString()));
        }

        /// <summary>Жоден стан з реального enum'а (крім None) не лишився без мапінгу — інакше майбутній StatusType мовчки випаде з HUD.</summary>
        [Test]
        public void StatusLabelKey_CoversEveryStatusTypeExceptNone()
        {
            foreach (StatusType status in Enum.GetValues(typeof(StatusType)))
            {
                if (status == StatusType.None) continue;
                Assert.IsNotNull(BattleArenaView.StatusLabelKey(status.ToString()),
                    $"{status}: додай мапінг у BattleArenaView.StatusLabelKey (і за потреби ключ combat.status.* у UkrainianText)");
            }
        }

        // ================= Бій v2: порядкові номери дублікатів =================

        [TestCase(1, "I")]
        [TestCase(2, "II")]
        [TestCase(3, "III")]
        [TestCase(4, "IV")]
        [TestCase(5, "V")]
        [TestCase(9, "IX")]
        [TestCase(14, "XIV")]
        [TestCase(40, "XL")]
        public void OrdinalRoman_MatchesStandardNumerals(int ordinal, string expected)
        {
            Assert.AreEqual(expected, BattleArenaView.OrdinalRoman(ordinal));
        }

        [Test]
        public void OrdinalRoman_ZeroOrNegative_IsEmpty()
        {
            Assert.AreEqual(string.Empty, BattleArenaView.OrdinalRoman(0));
            Assert.AreEqual(string.Empty, BattleArenaView.OrdinalRoman(-3));
        }

        [Test]
        public void WithOrdinal_AppendsRomanNumeral_WhenOrdinalPositive()
        {
            Assert.AreEqual("Розвідник орди II", BattleArenaView.WithOrdinal("Розвідник орди", 2));
        }

        [Test]
        public void WithOrdinal_LeavesNameUnchanged_WhenOrdinalIsZero()
        {
            Assert.AreEqual("Оксана", BattleArenaView.WithOrdinal("Оксана", 0));
        }

        // ================= Бій v2: підсвітка тайла з наміром гравця =================

        [Test]
        public void TintForIntent_HoveredUnreachable_DiffersFromHoveredReachable()
        {
            var unreachable = BattleArenaView.TintForIntent("None", true, isReachable: false, isCurrentUnit: false,
                isHovered: true, isHoveredUnreachable: true, isAbilityRange: false, isOverwatchAim: false, isOverwatchThreat: false);
            var reachable = BattleArenaView.TintForIntent("None", true, isReachable: true, isCurrentUnit: false,
                isHovered: true, isHoveredUnreachable: false, isAbilityRange: false, isOverwatchAim: false, isOverwatchThreat: false);

            AssertDistinct(new PaletteColor(unreachable.R, unreachable.G, unreachable.B, false),
                           new PaletteColor(reachable.R, reachable.G, reachable.B, false));
        }

        [Test]
        public void TintForIntent_CurrentUnit_BeatsEveryIntentOverlay()
        {
            var current = BattleArenaView.TintForIntent("None", true, false, isCurrentUnit: true,
                isHovered: true, isHoveredUnreachable: true, isAbilityRange: true, isOverwatchAim: true, isOverwatchThreat: true);
            var plainCurrent = BattleArenaView.TintFor("None", true, false, true, true);
            Assert.AreEqual(plainCurrent.R, current.R, 1e-5f);
            Assert.AreEqual(plainCurrent.G, current.G, 1e-5f);
            Assert.AreEqual(plainCurrent.B, current.B, 1e-5f);
        }

        [Test]
        public void TintForIntent_AbilityRangeOverwatchAimAndThreat_AreMutuallyDistinct()
        {
            var ability = BattleArenaView.TintForIntent("None", true, false, false, false, false, true, false, false);
            var overwatch = BattleArenaView.TintForIntent("None", true, false, false, false, false, false, true, false);
            var threat = BattleArenaView.TintForIntent("None", true, false, false, false, false, false, false, true);
            var plain = BattleArenaView.TintForIntent("None", true, false, false, false, false, false, false, false);

            AssertDistinct(AsPalette(ability), AsPalette(overwatch));
            AssertDistinct(AsPalette(ability), AsPalette(threat));
            AssertDistinct(AsPalette(overwatch), AsPalette(threat));
            AssertDistinct(AsPalette(ability), AsPalette(plain));
        }

        [Test]
        public void TintForIntent_NonWalkable_StillWinsOverEverything()
        {
            var tint = BattleArenaView.TintForIntent("None", false, true, true, true, true, true, true, true);
            var floor = BattleArenaView.TintFor("None", false, false, false, false);
            Assert.AreEqual(floor.R, tint.R, 1e-5f);
            Assert.AreEqual(floor.G, tint.G, 1e-5f);
            Assert.AreEqual(floor.B, tint.B, 1e-5f);
        }

        private static PaletteColor AsPalette(TileTint tint) => new PaletteColor(tint.R, tint.G, tint.B, false);

        // ================= Бій v2: камера =================

        [Test]
        public void CameraOffsetFromFocus_StraightDown_HasNoHorizontalOffset()
        {
            var offset = BattleArenaView.CameraOffsetFromFocus(90f, 45f, 10f);
            Assert.AreEqual(0f, offset.X, 1e-4f);
            Assert.AreEqual(0f, offset.Z, 1e-4f);
            Assert.AreEqual(10f, offset.Y, 1e-4f);
        }

        [Test]
        public void CameraOffsetFromFocus_LevelWithGround_HasNoHeight()
        {
            var offset = BattleArenaView.CameraOffsetFromFocus(0f, 0f, 10f);
            Assert.AreEqual(0f, offset.Y, 1e-4f);
            Assert.Greater(System.Math.Abs(offset.X) + System.Math.Abs(offset.Z), 0f);
        }

        [Test]
        public void CameraOffsetFromFocus_DifferentYaws_GiveDifferentHorizontalDirections()
        {
            var a = BattleArenaView.CameraOffsetFromFocus(52f, 0f, 10f);
            var b = BattleArenaView.CameraOffsetFromFocus(52f, 90f, 10f);
            // yaw=0 дивиться вздовж -Z (X≈0), yaw=90 — вздовж +X (Z≈0): Q/E
            // повертають камеру на 90° — ортогональні напрямки, не просто інше число.
            Assert.AreEqual(0f, a.X, 1e-4f);
            Assert.AreEqual(0f, b.Z, 1e-4f);
            Assert.AreNotEqual(a.X, b.X, "Q/E повертають камеру на 90° — інший напрямок має дати інший зсув");
        }

        [Test]
        public void ClampPanTarget_KeepsPointsInsideBounds_Unchanged()
        {
            var p = BattleArenaView.ClampPanTarget(5f, 5f, 10, 10, 2f);
            Assert.AreEqual(5f, p.X, 1e-5f);
            Assert.AreEqual(5f, p.Z, 1e-5f);
        }

        [Test]
        public void ClampPanTarget_ClampsOutsideBounds_ToMarginEdge()
        {
            var p = BattleArenaView.ClampPanTarget(-50f, 500f, 10, 10, 2f);
            Assert.AreEqual(-2f, p.X, 1e-5f);
            Assert.AreEqual(12f, p.Z, 1e-5f);
        }
    }
}
