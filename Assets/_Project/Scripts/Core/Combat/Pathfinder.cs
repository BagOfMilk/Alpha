using System.Collections.Generic;

namespace Game.Core.Combat
{
    /// <summary>
    /// Пошук досяжних тайлів BFS по ортогональних сусідах з єдиною ціною за крок
    /// (діагоналей немає — ПЛЕЙСХОЛДЕР простоти). Крізь зайняті/непрохідні тайли
    /// йти не можна; стартовий тайл юніта у видачу не входить.
    /// </summary>
    public static class Pathfinder
    {
        private static readonly (int dx, int dy)[] Neighbors = { (0, 1), (1, 0), (0, -1), (-1, 0) };

        /// <summary>
        /// Усі тайли, досяжні не дорожче apBudget при ціні costPerTile за крок.
        /// Ключ — тайл, значення — вартість шляху до нього в AP.
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
        /// Найкоротший шлях до dest за тими самими правилами, що Reachable (ортогональні
        /// кроки, лише вільні клітини), без стартової клітини. Сусіди перебираються
        /// в тому самому порядку — шлях детермінований. Потрібен дозору (US-3.6): overwatch
        /// зобов'язаний бачити сам шлях, а не тільки точку прибуття, інакше ворог «проходить
        /// крізь» сектор непоміченим. Порожньо, якщо dest недосяжний.
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
