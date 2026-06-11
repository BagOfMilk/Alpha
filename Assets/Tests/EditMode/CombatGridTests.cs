using Game.Core.Combat;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>Сетка: направленное укрытие, линия обзора, достижимость по AP.</summary>
    public class CombatGridTests
    {
        [Test]
        public void CoverAgainst_IsDirectional_FlankHasNoCover()
        {
            var map = new GridMap(10, 10);
            var defender = new GridPos(5, 5);
            map.SetCover(defender, Direction.East, CoverType.Half);

            Assert.AreEqual(CoverType.Half, map.CoverAgainst(defender, new GridPos(8, 5))); // атака с востока
            Assert.AreEqual(CoverType.None, map.CoverAgainst(defender, new GridPos(2, 5))); // фланг с запада
        }

        [Test]
        public void CoverAgainst_Diagonal_TakesBestOfTwoSides()
        {
            var map = new GridMap(10, 10);
            var defender = new GridPos(5, 5);
            map.SetCover(defender, Direction.East, CoverType.Half);
            map.SetCover(defender, Direction.North, CoverType.Full);

            // Атака с северо-востока: видны обе стороны, берётся лучшее укрытие.
            Assert.AreEqual(CoverType.Full, map.CoverAgainst(defender, new GridPos(8, 8)));
        }

        [Test]
        public void LineOfSight_BlockedByWall_TargetTileDoesNotBlock()
        {
            var map = new GridMap(10, 1);
            map.SetWall(new GridPos(2, 0));

            Assert.IsFalse(LineOfSight.HasLine(map, new GridPos(0, 0), new GridPos(4, 0)));
            // Сам тайл цели не считается блокирующим.
            Assert.IsTrue(LineOfSight.HasLine(map, new GridPos(0, 0), new GridPos(2, 0)));
            Assert.IsTrue(LineOfSight.HasLine(map, new GridPos(0, 0), new GridPos(1, 0)));
        }

        [Test]
        public void Reachable_RespectsBudget_WallsAndOccupants()
        {
            var map = new GridMap(5, 5);
            var start = new GridPos(0, 0);
            map.SetWall(new GridPos(1, 0));
            map.SetOccupant(new GridPos(0, 1), "blocker");

            var reach = Pathfinder.Reachable(map, start, apBudget: 2, costPerTile: 1);

            Assert.IsFalse(reach.ContainsKey(new GridPos(1, 0))); // стена
            Assert.IsFalse(reach.ContainsKey(new GridPos(0, 1))); // занято
            // Вокруг стены и блокера: до (1,1) только через (0,1)? Нет — (0,1) занят,
            // путь (0,0)→(0,1) закрыт; (1,1) достижим лишь в обход — дороже бюджета.
            Assert.IsFalse(reach.ContainsKey(new GridPos(1, 1)));
            Assert.IsFalse(reach.ContainsKey(new GridPos(0, 3))); // дистанция 3 > бюджет 2
        }

        [Test]
        public void Reachable_CostScalesWithPerTilePrice()
        {
            var map = new GridMap(5, 1);
            var reach = Pathfinder.Reachable(map, new GridPos(0, 0), apBudget: 4, costPerTile: 2);
            Assert.AreEqual(2, reach[new GridPos(1, 0)]);
            Assert.AreEqual(4, reach[new GridPos(2, 0)]);
            Assert.IsFalse(reach.ContainsKey(new GridPos(3, 0))); // 6 AP > 4
        }
    }
}
