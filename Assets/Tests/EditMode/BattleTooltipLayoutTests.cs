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
