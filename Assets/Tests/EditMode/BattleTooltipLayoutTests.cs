using Game.Gameplay.UI;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Бій v2, раунд 2: чиста математика клемпа підказки біля курсора
    /// (docs/COMBAT_V2.md §3, аудит знімків — «підказка прилипає до кута
    /// поверх «Раунд 1»»). <see cref="BattleTooltipLayout"/> не знає про
    /// UnityEngine — перевіряється звичайним headless-тестом, без Unity.
    /// </summary>
    public class BattleTooltipLayoutTests
    {
        [Test]
        public void ResolveVerticalOverlaps_SideBySideNames_AreStackedNotMerged()
        {
            // Два сусідні юніти: імена на тій самій висоті й перетинаються по x.
            var top = BattleTooltipLayout.ResolveVerticalOverlaps(
                new[] { 400f, 450f }, new[] { 300f, 300f }, new[] { 120f, 160f }, 30f, 2f);
            Assert.AreEqual(300f, top[0], 0.01f, "перший лишається на місці");
            Assert.AreEqual(300f - 30f - 2f, top[1], 0.01f, "другий піднімається рівно над першим");
        }

        [Test]
        public void ResolveVerticalOverlaps_FarApart_Untouched()
        {
            var top = BattleTooltipLayout.ResolveVerticalOverlaps(
                new[] { 100f, 600f, 350f }, new[] { 300f, 300f, 120f }, new[] { 120f, 120f, 120f }, 30f, 2f);
            CollectionAssert.AreEqual(new[] { 300f, 300f, 120f }, top);
        }

        [Test]
        public void ResolveVerticalOverlaps_ThreeInAPile_NoPairOverlapsAfter()
        {
            float[] cx = { 500f, 510f, 520f };
            float[] w = { 140f, 140f, 140f };
            var top = BattleTooltipLayout.ResolveVerticalOverlaps(cx, new[] { 400f, 402f, 398f }, w, 30f, 2f);
            for (int i = 0; i < 3; i++)
                for (int j = i + 1; j < 3; j++)
                    Assert.GreaterOrEqual(System.Math.Abs(top[i] - top[j]), 32f - 0.01f, i + "/" + j);
        }

        [Test]
        public void PlacesTooltip_RightAndBelow_TheAnchor_WhenRoomAllows()
        {
            var (x, y) = BattleTooltipLayout.PlaceNearAnchor(
                anchorX: 400f, anchorY: 300f, width: 260f, height: 150f,
                freeLeft: 0f, freeTop: 60f, freeRight: 1600f, freeBottom: 800f);

            Assert.Greater(x, 400f, "підказка мала стати ПРАВІШЕ якоря (не в кут)");
            Assert.Greater(y, 300f, "підказка мала стати НИЖЧЕ якоря (не в кут)");
        }

        [Test]
        public void NeverOverlapsTopBar_WhenAnchorIsNearTop()
        {
            // Той самий випадок, що на знімку 07-hover-tile: наведений тайл
            // біля лівого верхнього кута — підказка раніше лягала на «Раунд 1».
            var (_, y) = BattleTooltipLayout.PlaceNearAnchor(
                anchorX: 20f, anchorY: 10f, width: 260f, height: 150f,
                freeLeft: 0f, freeTop: 60f, freeRight: 1600f, freeBottom: 800f);

            Assert.GreaterOrEqual(y, 60f, "верхня межа підказки не має заходити на смугу «Раунд N»/шапку");
        }

        [Test]
        public void NeverOverflowsRightEdge_WhenAnchorIsNearLogPanel()
        {
            var (x, _) = BattleTooltipLayout.PlaceNearAnchor(
                anchorX: 1550f, anchorY: 300f, width: 260f, height: 150f,
                freeLeft: 0f, freeTop: 60f, freeRight: 1600f, freeBottom: 800f);

            Assert.LessOrEqual(x + 260f, 1600f, "права межа підказки не має заходити на журнал бою");
        }

        [Test]
        public void NeverOverflowsBottomEdge_WhenAnchorIsNearActionPanel()
        {
            var (_, y) = BattleTooltipLayout.PlaceNearAnchor(
                anchorX: 400f, anchorY: 790f, width: 260f, height: 150f,
                freeLeft: 0f, freeTop: 60f, freeRight: 1600f, freeBottom: 800f);

            Assert.LessOrEqual(y + 150f, 800f, "нижня межа підказки не має заходити на панель дій");
        }

        [Test]
        public void PinsToNearEdge_WhenFreeAreaIsNarrowerThanTooltip()
        {
            // Вільна смуга вужча за підказку (720p, дуже вузький простір) —
            // притискає до найближчого краю, а не ламає координати.
            var (x, y) = BattleTooltipLayout.PlaceNearAnchor(
                anchorX: 100f, anchorY: 100f, width: 260f, height: 150f,
                freeLeft: 40f, freeTop: 60f, freeRight: 200f, freeBottom: 120f);

            Assert.AreEqual(40f, x);
            Assert.AreEqual(60f, y);
        }
    }
}
