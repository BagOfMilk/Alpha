using System;
using System.Collections.Generic;

namespace Game.Core.Combat
{
    /// <summary>
    /// Боевая сетка: проходимость, блокировка обзора (стены/глыбы), укрытия по
    /// сторонам тайлов и занятость юнитами. Чистые данные + запросы; правил боя
    /// здесь нет. Укрытие направленное: защищает, если элемент стоит на стороне
    /// тайла защитника, обращённой к атакующему, — фланкирование возникает само
    /// (зашёл с открытой стороны → штрафа укрытия нет).
    /// </summary>
    public sealed class GridMap
    {
        public int Width { get; }
        public int Height { get; }

        private readonly bool[,] _walkable;
        private readonly bool[,] _blocksSight;
        private readonly CoverType[,,] _cover; // [x, y, direction]
        private readonly Dictionary<GridPos, string> _occupants = new Dictionary<GridPos, string>();

        public GridMap(int width, int height)
        {
            if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width));
            Width = width;
            Height = height;
            _walkable = new bool[width, height];
            _blocksSight = new bool[width, height];
            _cover = new CoverType[width, height, 4];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    _walkable[x, y] = true;
        }

        public bool InBounds(GridPos p) => p.X >= 0 && p.X < Width && p.Y >= 0 && p.Y < Height;

        // ---- Проходимость и обзор ----
        public bool IsWalkable(GridPos p) => InBounds(p) && _walkable[p.X, p.Y];
        public void SetWalkable(GridPos p, bool walkable) { if (InBounds(p)) _walkable[p.X, p.Y] = walkable; }

        public bool BlocksSight(GridPos p) => InBounds(p) && _blocksSight[p.X, p.Y];
        public void SetBlocksSight(GridPos p, bool blocks) { if (InBounds(p)) _blocksSight[p.X, p.Y] = blocks; }

        /// <summary>Сплошная стена: непроходима и непрозрачна.</summary>
        public void SetWall(GridPos p)
        {
            SetWalkable(p, false);
            SetBlocksSight(p, true);
        }

        // ---- Укрытия ----
        public CoverType GetCover(GridPos p, Direction side)
            => InBounds(p) ? _cover[p.X, p.Y, (int)side] : CoverType.None;

        public void SetCover(GridPos p, Direction side, CoverType cover)
        {
            if (InBounds(p)) _cover[p.X, p.Y, (int)side] = cover;
        }

        /// <summary>
        /// Укрытие защитника против атаки с позиции атакующего: смотрим стороны
        /// тайла защитника, обращённые к атакующему (по диагонали — обе), берём
        /// лучшее. Атакующий на открытой стороне = укрытия нет (фланг).
        /// </summary>
        public CoverType CoverAgainst(GridPos defender, GridPos attacker)
        {
            int dx = attacker.X - defender.X;
            int dy = attacker.Y - defender.Y;
            var best = CoverType.None;

            if (dx > 0 && GetCover(defender, Direction.East) > best) best = GetCover(defender, Direction.East);
            if (dx < 0 && GetCover(defender, Direction.West) > best) best = GetCover(defender, Direction.West);
            if (dy > 0 && GetCover(defender, Direction.North) > best) best = GetCover(defender, Direction.North);
            if (dy < 0 && GetCover(defender, Direction.South) > best) best = GetCover(defender, Direction.South);
            return best;
        }

        // ---- Занятость ----
        public bool IsOccupied(GridPos p) => _occupants.ContainsKey(p);
        public string OccupantAt(GridPos p) => _occupants.TryGetValue(p, out var id) ? id : null;

        public void SetOccupant(GridPos p, string unitId)
        {
            if (!InBounds(p)) return;
            if (string.IsNullOrEmpty(unitId)) _occupants.Remove(p);
            else _occupants[p] = unitId;
        }

        public void ClearOccupant(GridPos p) => _occupants.Remove(p);

        /// <summary>Свободна для входа: в границах, проходима и не занята.</summary>
        public bool IsFree(GridPos p) => IsWalkable(p) && !IsOccupied(p);
    }
}
