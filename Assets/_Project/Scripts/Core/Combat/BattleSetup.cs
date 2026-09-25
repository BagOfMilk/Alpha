using System.Collections.Generic;

namespace Game.Core.Combat
{
    /// <summary>Яке правило влучання використовує бій (BattleSetup.HitRule → конкретний IHitRule).</summary>
    public enum HitRuleKind
    {
        Threshold = 0,
        Percent = 1
    }

    /// <summary>Стіна на тайлі (непрохідна і блокує огляд) — найчастіший вид укриття арени.</summary>
    public readonly struct WallPlacement
    {
        public readonly GridPos Pos;
        public WallPlacement(GridPos pos) { Pos = pos; }
    }

    /// <summary>Спрямоване укриття (не стіна): напів/повне з конкретної сторони тайла.</summary>
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

    /// <summary>Де стає один боєць загону гравця — за id напарника (Roster не читаємо, тільки id).</summary>
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

    /// <summary>Де стає один ворог — за id з каталогу EnemyDefinition (DefaultCombatContent і контент-пакети).</summary>
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
    /// Вхід міського лупу в тактичний бій (§2 таблиці «одна гра»): усе, що
    /// потрібно, щоб зібрати CombatState, не заглядаючи в BaseState/Roster.
    /// Комбат дані про ростер не читає — той, хто просить бій (PassVanguardOutcome/
    /// DungeonRun/Finale, усі — D1), сам резолвить companion id → Companion і
    /// передає готові дані через BattleUnitFactory (див. DefaultCombatContent).
    ///
    /// BattleSetup НЕ несе сід/кубик: для HitRule=Percent сам IDiceRoller —
    /// окремий параметр CombatBattleBuilder.Build, і його власник — викликач
    /// (D1/GameSession), не Core (R1 — Core не реалізує IDiceRoller взагалі, див.
    /// ArchitectureGuardTests.Core_NoTypeImplementsIDiceRoller). Сід ігрової сесії
    /// живе в NewGameOptions.Seed (§4 TEST_BUILD.md) і перетворюється на один
    /// SeededDiceRoller ДО виклику Build — той самий екземпляр має використовуватись
    /// на весь бій (і, якщо потрібно, на всю сесію), інакше «той самий сід — той самий бій»
    /// не виконується.
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
        /// Id напарника-перебіжчика, якщо він б'ється на боці ворога в цьому
        /// бою (R8: Мирослава як FromDefector у фіналі, якщо зрада —
        /// рішення Б4/R2, тут — тільки параметр). Null — зрадника в бою немає.
        /// </summary>
        public string DefectorCompanionId;
        public GridPos DefectorPos;

        public HitRuleKind HitRule = HitRuleKind.Threshold;
    }
}
