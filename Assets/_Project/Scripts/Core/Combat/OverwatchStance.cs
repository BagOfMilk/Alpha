using System;

namespace Game.Core.Combat
{
    /// <summary>
    /// Зведений overwatch (GDD US-3.6, перенесено з архівної гілки
    /// claude/combat-overwatch, коміт 20b8dcf) — чисті дані, без правил.
    /// Юніт заплатив AP за постріл заздалегідь і тримає сектор: конус від своєї
    /// клітини в бік точки прицілу. Коли і як він стріляє і коли дозор
    /// знімається, вирішує CombatState.
    /// </summary>
    public sealed class OverwatchStance
    {
        /// <summary>Клітина стрільця в момент входу в дозор — вершина конуса.</summary>
        public GridPos Origin { get; }

        /// <summary>Точка, у бік якої дивиться конус (вісь сектора).</summary>
        public GridPos Aim { get; }

        /// <summary>
        /// Скільки AP пішло в резерв — ціна пострілу зброєю. Списані при вході,
        /// при спрацюванні вдруге не беруться і не повертаються, якщо ворог
        /// так і не увійшов у сектор: постріл оплачено один раз («без подвійного профіту»).
        /// </summary>
        public int ReservedAp { get; }

        public OverwatchStance(GridPos origin, GridPos aim, int reservedAp)
        {
            Origin = origin;
            Aim = aim;
            ReservedAp = reservedAp;
        }

        /// <summary>Клітина в секторі цього дозору (тільки геометрія; огляд перевіряє CombatState).</summary>
        public bool Covers(GridPos tile, int slopeNum, int slopeDen)
            => InCone(Origin, Aim, tile, slopeNum, slopeDen);

        /// <summary>
        /// Клітина в конусі: кут між віссю (aim − origin) і напрямом на клітину
        /// (tile − origin) не більший за півширину, тангенс якої = slopeNum / slopeDen.
        ///
        /// Лише цілі числа — ні тригонометрії, ні double: при куті меншому за 90°
        /// тангенс дорівнює |векторний добуток| / скалярний, і порівняння зводиться до двох
        /// множень. Бій детермінований на будь-якій платформі, а межа включена
        /// (клітина рівно на краю конуса — у конусі).
        /// </summary>
        public static bool InCone(GridPos origin, GridPos aim, GridPos tile, int slopeNum, int slopeDen)
        {
            if (slopeNum < 0 || slopeDen <= 0) return false;

            long ax = aim.X - origin.X, ay = aim.Y - origin.Y;
            long tx = tile.X - origin.X, ty = tile.Y - origin.Y;
            if ((ax == 0 && ay == 0) || (tx == 0 && ty == 0)) return false;

            long dot = ax * tx + ay * ty;
            if (dot <= 0) return false; // збоку під прямим кутом або за спиною

            long cross = Math.Abs(ax * ty - ay * tx);
            return cross * slopeDen <= dot * slopeNum;
        }
    }
}
