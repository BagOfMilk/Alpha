using System;

namespace Game.Core.Combat
{
    /// <summary>
    /// Взведённый overwatch (GDD US-3.6, перенесено из архивной ветки
    /// claude/combat-overwatch, коммит 20b8dcf) — чистые данные, без правил.
    /// Юнит заплатил AP за выстрел заранее и держит сектор: конус от своей
    /// клетки в сторону точки прицела. Когда и как он стреляет и когда дозор
    /// снимается, решает CombatState.
    /// </summary>
    public sealed class OverwatchStance
    {
        /// <summary>Клетка стрелка в момент входа в дозор — вершина конуса.</summary>
        public GridPos Origin { get; }

        /// <summary>Точка, в сторону которой смотрит конус (ось сектора).</summary>
        public GridPos Aim { get; }

        /// <summary>
        /// Сколько AP ушло в резерв — цена выстрела оружием. Списаны при входе,
        /// при срабатывании второй раз не берутся и не возвращаются, если враг
        /// так и не вошёл в сектор: выстрел оплачен один раз («без двойного профита»).
        /// </summary>
        public int ReservedAp { get; }

        public OverwatchStance(GridPos origin, GridPos aim, int reservedAp)
        {
            Origin = origin;
            Aim = aim;
            ReservedAp = reservedAp;
        }

        /// <summary>Клетка в секторе этого дозора (только геометрия; обзор проверяет CombatState).</summary>
        public bool Covers(GridPos tile, int slopeNum, int slopeDen)
            => InCone(Origin, Aim, tile, slopeNum, slopeDen);

        /// <summary>
        /// Клетка в конусе: угол между осью (aim − origin) и направлением на клетку
        /// (tile − origin) не больше полуширины, у которой тангенс = slopeNum / slopeDen.
        ///
        /// Только целые числа — ни тригонометрии, ни double: при угле меньше 90°
        /// тангенс равен |векторное| / скалярное, и сравнение сводится к двум
        /// умножениям. Бой детерминирован на любой платформе, а граница включена
        /// (клетка ровно на краю конуса — в конусе).
        /// </summary>
        public static bool InCone(GridPos origin, GridPos aim, GridPos tile, int slopeNum, int slopeDen)
        {
            if (slopeNum < 0 || slopeDen <= 0) return false;

            long ax = aim.X - origin.X, ay = aim.Y - origin.Y;
            long tx = tile.X - origin.X, ty = tile.Y - origin.Y;
            if ((ax == 0 && ay == 0) || (tx == 0 && ty == 0)) return false;

            long dot = ax * tx + ay * ty;
            if (dot <= 0) return false; // сбоку под прямым углом или за спиной

            long cross = Math.Abs(ax * ty - ay * tx);
            return cross * slopeDen <= dot * slopeNum;
        }
    }
}
