using System.Collections.Generic;
using Game.Gameplay.Walk;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Вибір будівлі під курсором (docs/UX_DESIGN.md §4.3, UX-07): промінь
    /// камери проти меж. Камера села — ізометрична (нахил 30°, поворот 45°),
    /// тож промінь іде згори-збоку вниз, як у тестах нижче.
    /// </summary>
    public class WorldPickerTests
    {
        // Промінь з точки над і перед будівлею вниз-уперед: (0, 10, −2) → (0, −1, 1);
        // у точці t він на висоті 10 − t і глибині −2 + t.
        private const float Ox = 0f, Oy = 10f, Oz = -2f, Dx = 0f, Dy = -1f, Dz = 1f;

        [Test]
        public void RoofHit_PicksTheBuilding_NotTheGroundBehind()
        {
            // Хата (−1..1, 0..3, 4..8): промінь входить через дах (t = 7, висота 3), а землю
            // за нею торкнувся б лише на t = 10 — вибрано будівлю.
            var house = new Pickable("building:storehouse", PickKind.Building, -1f, 0f, 4f, 1f, 3f, 8f);
            Assert.AreEqual("building:storehouse", WorldPicker.Pick(Ox, Oy, Oz, Dx, Dy, Dz, new[] { house }));
        }

        [Test]
        public void NearestAlongTheRay_Wins()
        {
            // Мутація: вибирати останню влучену замість найближчої — тест падає.
            var far = new Pickable("far", PickKind.Building, -1f, 0f, 7f, 1f, 3f, 10f);   // t ∈ [9, 10]
            var near = new Pickable("near", PickKind.Building, -1f, 0f, 3f, 1f, 3f, 6f);  // t ∈ [7, 8]
            Assert.AreEqual("near", WorldPicker.Pick(Ox, Oy, Oz, Dx, Dy, Dz, new List<Pickable> { far, near }));
            Assert.AreEqual("near", WorldPicker.Pick(Ox, Oy, Oz, Dx, Dy, Dz, new List<Pickable> { near, far }));
        }

        [Test]
        public void Miss_ReturnsNull()
        {
            var aside = new Pickable("aside", PickKind.Plot, 5f, 0f, 5f, 7f, 3f, 7f);
            Assert.IsNull(WorldPicker.Pick(Ox, Oy, Oz, Dx, Dy, Dz, new[] { aside }));
            Assert.IsNull(WorldPicker.Pick(Ox, Oy, Oz, Dx, Dy, Dz, new Pickable[0]));
            Assert.IsNull(WorldPicker.Pick(Ox, Oy, Oz, Dx, Dy, Dz, null));
        }

        [Test]
        public void BehindTheCamera_IsNotPicked()
        {
            // Коробка позаду початку променя: t < 0 — не влучання.
            var behind = new Pickable("behind", PickKind.Building, -1f, 11f, -5f, 1f, 13f, -3f);
            Assert.IsNull(WorldPicker.Pick(Ox, Oy, Oz, Dx, Dy, Dz, new[] { behind }));
        }

        [Test]
        public void RayParallelToFaces_InsideSlab_Hits_OutsideSlab_Misses()
        {
            // Мутація: без перевірки паралельного променя — ділення на нуль дає хибне влучання.
            var box = new Pickable("box", PickKind.Landmark, -1f, 0f, 0f, 1f, 2f, 10f);
            Assert.IsNotNull(WorldPicker.Pick(0f, 1f, -5f, 0f, 0f, 1f, new[] { box }), "промінь уздовж осі Z усередині плит X і Y");
            Assert.IsNull(WorldPicker.Pick(3f, 1f, -5f, 0f, 0f, 1f, new[] { box }), "поза плитою X — промах");
        }

        [Test]
        public void EqualDistance_FirstInList_Wins_Deterministic()
        {
            var a = new Pickable("a", PickKind.Building, -1f, 0f, 3f, 1f, 3f, 6f);
            var b = new Pickable("b", PickKind.Building, -1f, 0f, 3f, 1f, 3f, 6f);
            Assert.AreEqual("a", WorldPicker.Pick(Ox, Oy, Oz, Dx, Dy, Dz, new[] { a, b }));
        }

        [Test]
        public void SwappedBounds_AreNormalized()
        {
            var swapped = new Pickable("s", PickKind.Building, 1f, 3f, 8f, -1f, 0f, 4f);
            Assert.AreEqual("s", WorldPicker.Pick(Ox, Oy, Oz, Dx, Dy, Dz, new[] { swapped }));
        }
    }
}
