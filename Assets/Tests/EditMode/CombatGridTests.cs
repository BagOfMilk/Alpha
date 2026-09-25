using Game.Core.Combat;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Сітка: спрямоване укриття, лінія огляду, досяжність за AP.
    /// Перенесено з архівної бойової лінії (коміт 20b8dcf) без змін —
    /// GridMap/Pathfinder/LineOfSight не зав'язані на модель персонажа.
    /// </summary>
    public class CombatGridTests
    {
        [Test]
        public void CoverAgainst_IsDirectional_FlankHasNoCover()
        {
            var map = new GridMap(10, 10);
            var defender = new GridPos(5, 5);
            map.SetCover(defender, Direction.East, CoverType.Half);

            Assert.AreEqual(CoverType.Half, map.CoverAgainst(defender, new GridPos(8, 5))); // атака зі сходу
            Assert.AreEqual(CoverType.None, map.CoverAgainst(defender, new GridPos(2, 5))); // фланг із заходу
        }

        [Test]
        public void CoverAgainst_Diagonal_TakesBestOfTwoSides()
        {
            var map = new GridMap(10, 10);
            var defender = new GridPos(5, 5);
            map.SetCover(defender, Direction.East, CoverType.Half);
            map.SetCover(defender, Direction.North, CoverType.Full);

            Assert.AreEqual(CoverType.Full, map.CoverAgainst(defender, new GridPos(8, 8)));
        }

        [Test]
        public void LineOfSight_BlockedByWall_TargetTileDoesNotBlock()
        {
            var map = new GridMap(10, 1);
            map.SetWall(new GridPos(2, 0));

            Assert.IsFalse(LineOfSight.HasLine(map, new GridPos(0, 0), new GridPos(4, 0)));
            Assert.IsTrue(LineOfSight.HasLine(map, new GridPos(0, 0), new GridPos(2, 0)));
            Assert.IsTrue(LineOfSight.HasLine(map, new GridPos(0, 0), new GridPos(1, 0)));
        }

        [Test]
        public void LineOfSight_IsMutual_InBothDirections()
        {
            var map = new GridMap(12, 8);
            map.SetWall(new GridPos(6, 3));
            map.SetWall(new GridPos(6, 4));

            for (int x = 0; x < 12; x++)
                for (int y = 0; y < 8; y++)
                {
                    var a = new GridPos(x, y);
                    foreach (var b in new[] { new GridPos(4, 3), new GridPos(8, 2), new GridPos(0, 7) })
                        Assert.AreEqual(LineOfSight.HasLine(map, a, b), LineOfSight.HasLine(map, b, a),
                            $"видимость {a.X},{a.Y} ↔ {b.X},{b.Y} обязана быть взаимной");
                }
        }

        [Test]
        public void Reachable_RespectsBudget_WallsAndOccupants()
        {
            var map = new GridMap(5, 5);
            var start = new GridPos(0, 0);
            map.SetWall(new GridPos(1, 0));
            map.SetOccupant(new GridPos(0, 1), "blocker");

            var reach = Pathfinder.Reachable(map, start, apBudget: 2, costPerTile: 1);

            Assert.IsFalse(reach.ContainsKey(new GridPos(1, 0)));
            Assert.IsFalse(reach.ContainsKey(new GridPos(0, 1)));
            Assert.IsFalse(reach.ContainsKey(new GridPos(1, 1)));
            Assert.IsFalse(reach.ContainsKey(new GridPos(0, 3)));
        }

        [Test]
        public void Reachable_CostScalesWithPerTilePrice()
        {
            var map = new GridMap(5, 1);
            var reach = Pathfinder.Reachable(map, new GridPos(0, 0), apBudget: 4, costPerTile: 2);
            Assert.AreEqual(2, reach[new GridPos(1, 0)]);
            Assert.AreEqual(4, reach[new GridPos(2, 0)]);
            Assert.IsFalse(reach.ContainsKey(new GridPos(3, 0)));
        }
    }
}
