using System;

namespace Game.Core.Combat
{
    /// <summary>
    /// Линия обзора по Брезенхэму: блокируют только тайлы с BlocksSight (стены,
    /// сплошные глыбы). Низкие укрытия обзор НЕ блокируют — за ними можно прятаться
    /// и через них можно стрелять. Концевые тайлы (стрелок и цель) не считаются.
    /// Видимость ВЗАИМНА: концы канонизируются, иначе тай-брейк Брезенхэма ведёт
    /// A→B и B→A разными тайлами и «я тебя не вижу, а ты меня — да» (правила
    /// симметричны для игрока и врагов).
    /// </summary>
    public static class LineOfSight
    {
        public static bool HasLine(GridMap map, GridPos from, GridPos to)
        {
            if (map == null || !map.InBounds(from) || !map.InBounds(to)) return false;
            if (from == to) return true;

            // Канонизация концов: трассируем всегда от «меньшего» к «большему».
            if (to.X < from.X || (to.X == from.X && to.Y < from.Y))
            {
                var swap = from; from = to; to = swap;
            }

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
