using System.Collections.Generic;

namespace Game.Core.Combat
{
    /// <summary>Яке правило влучання використовує бій (BattleSetup.HitRule → конкретний IHitRule).</summary>
    public enum HitRuleKind
    {
        Threshold = 0,
        Percent = 1
    }

    /// <summary>
    /// Як почався бій — від того, як загін до нього дійшов (Поправка №14.1;
    /// власник, 29.09.2026: «Супер, але додай більше варіантів, може навіть
    /// поранення…» → старт пораненими). Детерміновано: варіант вибирає той,
    /// хто просить бій (данж, подія), за шляхом і полосою, а не кубиком.
    /// Бонус за перевагу знімає штраф, а не дає зайвих ходів.
    /// </summary>
    public enum BattleOpening
    {
        /// <summary>Зустрічний бій — звичайна ініціатива впереміш.</summary>
        Encounter = 0,
        /// <summary>Перший удар — свідомо обрано кривавий шлях: у раунді 1 загін гравця ходить першим.</summary>
        FirstStrike = 1,
        /// <summary>Засідка — кривавий шлях після розвідки: як «Перший удар», і вороги в раунді 1 позначені (наявний стан Marked).</summary>
        Ambush = 2,
        /// <summary>Вас помітили — тихий шлях зірвався: у раунді 1 першими ходять вороги.</summary>
        Spotted = 3,
        /// <summary>Під обстрілом — помітили глибоко в небезпеці: вороги першими, а загін стартує пораненим (мінус HP і Кровотеча).</summary>
        UnderFire = 4,
        /// <summary>Оточені — вороги першими, загін розставлено врозкид (розстановку робить той, хто будує бій).</summary>
        Surrounded = 5,
        /// <summary>
        /// Ультиматум відкинуто («Скласти зброю!», docs/ABILITIES.md §4.6): звичайна
        /// ініціатива, але розлючений ворог у раунді 1 б'є влучніше.
        /// </summary>
        Provoked = 6
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

        /// <summary>Як почався бій (Поправка №14.1). За замовчуванням — зустрічний.</summary>
        public BattleOpening Opening = BattleOpening.Encounter;

        /// <summary>Об'єкти поля (Поправка №14.4): перепони, бочки з порохом, сіно.</summary>
        public List<MapObjectPlacement> Objects = new List<MapObjectPlacement>();

        /// <summary>Підкріплення ворога з відліком (Поправка №14.4; правило контенту, не рушія).</summary>
        public List<ReinforcementSpawn> Reinforcements = new List<ReinforcementSpawn>();

        /// <summary>Погода доби бою (Поправка №21.2): туман і дощ заважають дальнім пострілам.</summary>
        public Game.Core.World.WeatherKind Weather = Game.Core.World.WeatherKind.Clear;
    }
}
