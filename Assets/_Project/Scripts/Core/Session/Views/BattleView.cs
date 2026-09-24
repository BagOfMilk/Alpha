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

        public IReadOnlyList<string> InitiativeOrder;
        public IReadOnlyList<string> Log;
        public bool IsHitRulePercent;
    }
}
