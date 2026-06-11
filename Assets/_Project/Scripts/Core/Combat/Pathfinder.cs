using System.Collections.Generic;

namespace Game.Core.Combat
{
    /// <summary>
    /// Поиск достижимых тайлов BFS по ортогональным соседям с единой ценой за шаг
    /// (диагоналей нет — ПЛЕЙСХОЛДЕР простоты). Сквозь занятые/непроходимые тайлы
    /// идти нельзя; стартовый тайл юнита в выдачу не входит.
    /// </summary>
    public static class Pathfinder
    {
        private static readonly (int dx, int dy)[] Neighbors = { (0, 1), (1, 0), (0, -1), (-1, 0) };

        /// <summary>
        /// Все тайлы, достижимые не дороже apBudget при цене costPerTile за шаг.
        /// Ключ — тайл, значение — стоимость пути до него в AP.
        /// </summary>
        public static Dictionary<GridPos, int> Reachable(GridMap map, GridPos start, int apBudget, int costPerTile)
        {
            var result = new Dictionary<GridPos, int>();
            if (map == null || costPerTile <= 0 || apBudget < costPerTile) return result;

            var frontier = new Queue<GridPos>();
            var visited = new HashSet<GridPos> { start };
            frontier.Enqueue(start);
            var costAt = new Dictionary<GridPos, int> { [start] = 0 };

            while (frontier.Count > 0)
            {
                var cur = frontier.Dequeue();
                int curCost = costAt[cur];
                foreach (var (dx, dy) in Neighbors)
                {
                    var next = new GridPos(cur.X + dx, cur.Y + dy);
                    if (visited.Contains(next) || !map.IsFree(next)) continue;
                    int nextCost = curCost + costPerTile;
                    if (nextCost > apBudget) continue;

                    visited.Add(next);
                    costAt[next] = nextCost;
                    result[next] = nextCost;
                    frontier.Enqueue(next);
                }
            }
            return result;
        }
    }
}
