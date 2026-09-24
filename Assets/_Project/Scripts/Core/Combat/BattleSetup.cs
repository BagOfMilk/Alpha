using System.Collections.Generic;

namespace Game.Core.Combat
{
    /// <summary>Какое правило попадания использует бой (BattleSetup.HitRule → конкретный IHitRule).</summary>
    public enum HitRuleKind
    {
        Threshold = 0,
        Percent = 1
    }

    /// <summary>Стена на тайле (непроходима и блокирует обзор) — самый частый вид укрытия арены.</summary>
    public readonly struct WallPlacement
    {
        public readonly GridPos Pos;
        public WallPlacement(GridPos pos) { Pos = pos; }
    }

    /// <summary>Направленное укрытие (не стена): полу/полное с конкретной стороны тайла.</summary>
    public readonly struct CoverPlacement
    {
        public readonly GridPos Pos;
        public readonly Direction Side;
        public readonly CoverType Cover;

        public CoverPlacement(GridPos pos, Direction side, CoverType cover)
        {
            Pos = pos;
            Side = side;
            Cover = cover;
        }
    }

    /// <summary>Где встаёт один боец отряда игрока — по id напарника (Roster не читаем, только id).</summary>
    public readonly struct PlayerSpawn
    {
        public readonly string CompanionId;
        public readonly GridPos Pos;

        public PlayerSpawn(string companionId, GridPos pos)
        {
            CompanionId = companionId;
            Pos = pos;
        }
    }

    /// <summary>Где встаёт один враг — по id из каталога EnemyDefinition (DefaultCombatContent и контент-пакеты).</summary>
    public readonly struct EnemySpawn
    {
        public readonly string EnemyDefinitionId;
        public readonly GridPos Pos;

        public EnemySpawn(string enemyDefinitionId, GridPos pos)
        {
            EnemyDefinitionId = enemyDefinitionId;
            Pos = pos;
        }
    }

    /// <summary>
    /// Вход городского лупа в тактический бой (§2 таблицы «одна гра»): всё, что
    /// нужно, чтобы собрать CombatState, не заглядывая в BaseState/Roster.
    /// Комбат данные о ростере не читает — тот, кто просит бой (PassVanguardOutcome/
    /// DungeonRun/Finale, все — D1), сам резолвит companion id → Companion и
    /// передаёт готовые данные через BattleUnitFactory (см. DefaultCombatContent).
    ///
    /// BattleSetup НЕ несёт сид/кубик: для HitRule=Percent сам IDiceRoller —
    /// отдельный параметр CombatBattleBuilder.Build, и его владелец — вызывающий
    /// (D1/GameSession), не Core (R1 — Core не реализует IDiceRoller вовсе, см.
    /// ArchitectureGuardTests.Core_NoTypeImplementsIDiceRoller). Сид игровой сессии
    /// живёт в NewGameOptions.Seed (§4 TEST_BUILD.md) и превращается в один
    /// SeededDiceRoller ДО вызова Build — тот же экземпляр должен использоваться
    /// на весь бой (и, если нужно, на всю сессию), иначе «тот же сид — тот же бой»
    /// не выполняется.
    /// </summary>
    public sealed class BattleSetup
    {
        public int Width = 8;
        public int Height = 8;

        public List<WallPlacement> Walls = new List<WallPlacement>();
        public List<CoverPlacement> Cover = new List<CoverPlacement>();

        public List<PlayerSpawn> PlayerUnits = new List<PlayerSpawn>();
        public List<EnemySpawn> EnemyUnits = new List<EnemySpawn>();

        /// <summary>
        /// Id напарника-перебежчика, если он бьётся на стороне врага в этом
        /// бою (R8: Мирослава как FromDefector в финале, если зрада —
        /// решение Б4/R2, здесь — только параметр). Null — зрадника в бою нет.
        /// </summary>
        public string DefectorCompanionId;
        public GridPos DefectorPos;

        public HitRuleKind HitRule = HitRuleKind.Threshold;
    }
}
