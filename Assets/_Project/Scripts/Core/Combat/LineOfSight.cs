using System;

namespace Game.Core.Combat
{
    /// <summary>
    /// Линия обзора по Брезенхэму: блокируют только тайлы с BlocksSight (стены,
    /// сплошные глыбы). Низкие укрытия обзор НЕ блокируют — за ними можно прятаться
    /// и через них можно стрелять. Концевые тайлы (стрелок и цель) не считаются.
    /// </summary>
    public static class LineOfSight
    {
        public static bool HasLine(GridMap map, GridPos from, GridPos to)
        {
            if (map == null || !map.InBounds(from) || !map.InBounds(to)) return false;
            if (from == to) return true;

            int x = from.X, y = from.Y;
            int dx = Math.Abs(to.X - from.X), dy = Math.Abs(to.Y - from.Y);
            int sx = from.X < to.X ? 1 : -1;
            int sy = from.Y < to.Y ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x += sx; }
                if (e2 < dx) { err += dx; y += sy; }

                if (x == to.X && y == to.Y) return true; // дошли до цели — её тайл не блокирует
                if (map.BlocksSight(new GridPos(x, y))) return false;
            }
        }
    }
}
