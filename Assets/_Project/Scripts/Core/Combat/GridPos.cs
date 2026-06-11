using System;

namespace Game.Core.Combat
{
    /// <summary>Позиция на боевой сетке. Лёгкая value-структура с метриками дистанций.</summary>
    [Serializable]
    public readonly struct GridPos : IEquatable<GridPos>
    {
        public readonly int X;
        public readonly int Y;

        public GridPos(int x, int y)
        {
            X = x;
            Y = y;
        }

        /// <summary>Дистанция Чебышёва (король) — используется для дальности оружия и смежности.</summary>
        public static int Chebyshev(GridPos a, GridPos b)
            => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

        /// <summary>Манхэттен — длина ортогонального пути без препятствий.</summary>
        public static int Manhattan(GridPos a, GridPos b)
            => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

        public bool Equals(GridPos other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is GridPos other && Equals(other);
        public override int GetHashCode() => (X * 397) ^ Y;
        public static bool operator ==(GridPos a, GridPos b) => a.Equals(b);
        public static bool operator !=(GridPos a, GridPos b) => !a.Equals(b);
        public override string ToString() => $"({X},{Y})";
    }
}
