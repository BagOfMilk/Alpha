using System.Collections.Generic;

namespace Game.Core.Session.Views
{
    public readonly struct GridPosView
    {
        public readonly int X, Y;

        public GridPosView(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    public sealed class BattleGridView
    {
        public int Width, Height;

        /// <summary>За індексом x+y*Width: "None"|"Half"|"Full".</summary>
        public IReadOnlyList<string> TileCover;
        public IReadOnlyList<bool> TileWalkable;
    }

    public sealed class BattleUnitView
    {
        public string Id, DisplayNameKey;
        public GridPosView Pos;

        /// <summary>"Player"|"Enemy"|"FromDefector".</summary>
        public string Side;

        public int Hp, HpMax, Ap, ApMax, ApReserved;
        public bool IsOverwatching;
        public IReadOnlyList<string> Statuses;
        public bool IsDowned;

        /// <summary>0, якщо цей юніт не поточна ціль прев'ю (заповнюється <see cref="GameSession.PreviewHitChance"/>).</summary>
        public int HitChancePreview;
    }

    /// <summary>Повний контракт бою (R18/§4.2.1): грид+юніти+хід+лог, жодного типу Game.Core.Combat напряму.</summary>
    public sealed class BattleView
    {
        public int Round;

        /// <summary>"Ongoing"|"Victory"|"Defeat"|"Retreat"|"Draw".</summary>
        public string Outcome;

        public BattleGridView Grid;
        public IReadOnlyList<BattleUnitView> Units;

        /// <summary>Достяжні тайли для активного юніта — {x,y,apCost} лінеаризовано парами Pos/Ap не тут; лишень позиції.</summary>
        public IReadOnlyList<GridPosView> ReachableTiles;

        /// <summary>
        /// Фікс-ревью пакета D2 (блокер): Id юніта, чий зараз хід, null поза боєм
        /// активного юніта (Outcome != Ongoing). Раніше водій ботів вгадував його
        /// за тим, чия клітинка входить у <see cref="ReachableTiles"/> — хибно,
        /// бо <c>Pathfinder.Reachable</c> НЕ включає стартовий тайл юніта в
        /// результат (див. коментар класу), тож евристика ніколи не спрацьовувала.
        /// Явне поле — прямий проекція <c>CombatState.Current.Id</c>, того самого
        /// джерела, яким уже рахується <see cref="ReachableTiles"/>.
        /// </summary>
        public string CurrentUnitId;

        public IReadOnlyList<string> InitiativeOrder;

        /// <summary>
        /// Журнал бою для гравця (R7): ключі таблиці з аргументами, НЕ готові
        /// рядки. Слова підставляє Gameplay (<c>BattleLogText</c>). Раніше тут
        /// ішов внутрішній трейс <c>CombatState.Log</c> — російський текст із
        /// сирими іменами enum, і фолбек-екран бою показував його гравцю як є.
        /// </summary>
        public IReadOnlyList<BattleLogLineView> Log;

        public bool IsHitRulePercent;
    }

    /// <summary>
    /// Один рядок журналу бою: ключ <c>combat.log.*</c> + аргументи — id юнітів
    /// (<c>unitId</c> — підмет рядка, <c>targetId</c> — другий учасник), числа
    /// і токени (<c>status</c>, <c>damageType</c>, <c>abilityId</c>).
    /// </summary>
    public sealed class BattleLogLineView
    {
        public int Round;
        public string Key;
        public IReadOnlyDictionary<string, string> Args;
    }
}
