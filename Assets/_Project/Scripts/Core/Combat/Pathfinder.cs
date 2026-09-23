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

        /// <summary>
        /// Кратчайший путь до dest по тем же правилам, что Reachable (ортогональные
        /// шаги, только свободные клетки), без стартовой клетки. Соседи перебираются
        /// в том же порядке — путь детерминирован. Длина пути × цена шага равна цене
        /// из Reachable: оба — BFS по одной сетке. Пусто, если dest недостижим.
        ///
        /// Нужен дозору (US-3.6): overwatch обязан видеть сам путь, а не только
        /// точку прибытия, иначе враг «проходит сквозь» сектор незамеченным.
        /// </summary>
        public static List<GridPos> Path(GridMap map, GridPos start, GridPos dest)
        {
            var path = new List<GridPos>();
            if (map == null || start == dest || !map.IsFree(dest)) return path;

            var parent = new Dictionary<GridPos, GridPos>();
            var visited = new HashSet<GridPos> { start };
            var frontier = new Queue<GridPos>();
            frontier.Enqueue(start);

            while (frontier.Count > 0)
            {
                var cur = frontier.Dequeue();
                if (cur == dest) break;
                foreach (var (dx, dy) in Neighbors)
                {
                    var next = new GridPos(cur.X + dx, cur.Y + dy);
                    if (visited.Contains(next) || !map.IsFree(next)) continue;
                    visited.Add(next);
                    parent[next] = cur;
                    frontier.Enqueue(next);
                }
            }

            if (!parent.ContainsKey(dest)) return path;
            for (var p = dest; p != start; p = parent[p]) path.Add(p);
            path.Reverse();
            return path;
        }
    }
}
