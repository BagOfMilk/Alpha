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

        /// <summary>
        /// Укриття тайла по боках (Поправка №14.4 — «укриття видно»): за індексом
        /// x+y*Width рядок "Північ|Схід|Південь|Захід", кожне значення
        /// "None"|"Half"|"Full". HUD показує, з якого боку тут захищає.
        /// </summary>
        public IReadOnlyList<string> TileCoverSides;
    }

    /// <summary>Об'єкт поля (Поправка №14.4): перепона, бочка з порохом, сіно.</summary>
    public sealed class BattleObjectView
    {
        public GridPosView Pos;

        /// <summary>"LowCover"|"HighCover"|"PowderKeg"|"Haystack".</summary>
        public string Kind;

        /// <summary>По ньому можна вдарити (бочка, сіно).</summary>
        public bool IsTargetable;

        /// <summary>Бочка: радіус і шкода вибуху; сіно: радіус і тривалість вогню (раунди). 0 — не діє.</summary>
        public int EffectRadius, EffectDamage, EffectRounds;
    }

    /// <summary>Зона вогню від спаленого сіна.</summary>
    public sealed class BattleFireView
    {
        public GridPosView Center;
        public int Radius, RoundsLeft;
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

        /// <summary>Ключ UkrainianText екіпірованої зброї (WeaponDefinition.Id, напр. "weapon.horde_bow") — null, якщо юніт безоружний.</summary>
        public string WeaponId;

        /// <summary>0, якщо цей юніт не поточна ціль прев'ю (заповнюється <see cref="GameSession.PreviewHitChance"/>).</summary>
        public int HitChancePreview;

        /// <summary>
        /// Способності, які САМЕ цей юніт знає (напарник — за порогом скіла,
        /// ворог — з <c>EnemyDefinition</c>; <c>CombatUnit.Abilities</c>) — не
        /// фіксований каталог однаковий для всіх. Раніше Gameplay-шар показував
        /// той самий набір із чотирьох кнопок кожному юніту незалежно від того,
        /// чи той їх узагалі знає (§BattleArenaView.KnownAbilityIds, відомий
        /// розрив звіту пакета E2).
        /// </summary>
        public IReadOnlyList<BattleAbilityView> Abilities;

        // ---- Бій v2 (docs/COMBAT_V2.md §7.1): поля заморожені — додавати можна, перейменовувати ні ----

        /// <summary>0 — ім'я унікальне в цьому бою; 1, 2, … — порядковий номер серед юнітів з тим самим DisplayNameKey (показується «I», «II», …).</summary>
        public int Ordinal;

        /// <summary>Стани з тривалістю (те саме, що <see cref="Statuses"/>, але з числами).</summary>
        public IReadOnlyList<BattleStatusView> StatusDetails;

        /// <summary>Дозор: куди націлено конус (лише коли <see cref="IsOverwatching"/>).</summary>
        public bool HasOverwatchAim;
        public GridPosView OverwatchAim;

        /// <summary>Ціна звичайної атаки зброєю в ОД (0 — без зброї).</summary>
        public int AttackApCost;

        /// <summary>Дальність зброї (максимальна) і оптимальна дальність (далі — штраф до шансу).</summary>
        public int WeaponRange, WeaponOptimalRange;
        public bool WeaponIsMelee;

        /// <summary>Лише для <see cref="IsDowned"/>: скільки ходів до смерті без допомоги.</summary>
        public int DownWindowRemaining;

        /// <summary>true — юнітом керує ШІ (вороги, перебіжчики); гравець ним не ходить.</summary>
        public bool IsAiControlled;

        /// <summary>Вибув із бою: загинув або врятований і винесений (Поправка №14.5 — колесо черги його не показує).</summary>
        public bool IsOutOfBattle;

        // ---- Здача (Поправка №14.2) — лише для ворогів ----
        /// <summary>"Grunt" | "MiniBoss" | "Boss".</summary>
        public string Rank;
        /// <summary>Може здатися (позначений рядовий чи міні-бос).</summary>
        public bool CanSurrender;
        /// <summary>Здасться при здоров'ї ≤ цієї частки (%) — з урахуванням залякування; видно заздалегідь.</summary>
        public int SurrenderAtHpPercent;
        /// <summary>Уже здався — вибув із бою живим.</summary>
        public bool IsSurrendered;

        /// <summary>Утік із поля («Залякати» на звіра, docs/ABILITIES.md) — арена прибирає фігуру.</summary>
        public bool IsFled;

        // ---- Досьє (Поправка №14.6) — лише для ворогів ----
        /// <summary>"Contact" — знаємо роль і здоров'я; "Studied" — ще й прийоми, опори, умову здачі; null — свій чи перебіжчик.</summary>
        public string Dossier;
        /// <summary>"Tank" | "Skirmisher" | "Controller" | "Breacher" — видно з першого контакту.</summary>
        public string Role;
        /// <summary>Опори вивченого ворога: "Fire:weak", "Ballistic:strong"; null — ще не вивчений.</summary>
        public IReadOnlyList<string> ResistNotes;

        /// <summary>Побратими в цьому бою (id юнітів, Поправка №14.8): поруч раз за раунд прикривають.</summary>
        public IReadOnlyList<string> BondUnitIds;
    }

    /// <summary>Один стан юніта з тривалістю (docs/COMBAT_V2.md §7.1).</summary>
    public sealed class BattleStatusView
    {
        /// <summary>Назва стану — як у <see cref="BattleUnitView.Statuses"/> ("Bleeding", "Suppressed", …).</summary>
        public string Type;
        public int RemainingTurns;
        /// <summary>Шкода за хід для станів-DoT (кровотеча, горіння, отрута); 0 — не DoT.</summary>
        public int DotDamagePerTurn;
    }

    /// <summary>
    /// Один доданок формули шансу (docs/COMBAT_V2.md §7.2). Ключі:
    /// accuracy, ability, defense, knocked_down, marked, cover_half,
    /// cover_full, distance, suppressed, clamp. Сума ChanceDelta всіх доданків
    /// дорівнює <see cref="AttackPreviewView.Chance"/>.
    /// </summary>
    public sealed class ChanceTermView
    {
        public string Key;
        public int ChanceDelta;
    }

    /// <summary>
    /// Прев'ю дії «атакувати ціль» (зброєю або озброєною здібністю) ДО кліку:
    /// усе, що гравець має бачити, щоб зважити ризик (docs/COMBAT_V2.md §3).
    /// </summary>
    public sealed class AttackPreviewView
    {
        public string AttackerId, TargetId;

        /// <summary>null — звичайна атака зброєю; інакше id здібності.</summary>
        public string AbilityId;

        /// <summary>"Success" — дію можна виконати; інакше назва CombatActionResult (NotEnoughAp, OutOfRange, NoLineOfSight, InvalidTarget, OnCooldown, InvalidAction).</summary>
        public string Result;

        /// <summary>false — дія без кидка влучання (лікування, стан, переміщення): шанс не показується.</summary>
        public bool HasAttackRoll;

        /// <summary>Підсумок формули — те саме число, що піде в роль.</summary>
        public int Chance;
        public bool IsPercent;
        public IReadOnlyList<ChanceTermView> Terms;

        /// <summary>НАПРЯМЛЕНЕ укриття цілі проти цього атакуючого: "None"|"Half"|"Full".</summary>
        public string Cover;
        /// <summary>Укриття не діє (ближній бій, здібність ігнорує укриття).</summary>
        public bool CoverIgnored;

        /// <summary>Фланг (Поправка №14.4): у цілі є укриття, але з боку атакуючого — ні.</summary>
        public bool IsFlanked;

        public int DamageMin, DamageMax, DamageCrit;
        /// <summary>Правило «поріг»: звичайне влучання дає рівно <see cref="DamageExpected"/>, діапазон не показувати.</summary>
        public bool IsDamageDeterministic;
        public int DamageExpected;
        /// <summary>Опори й броня цілі ще невідомі (досьє, №14.6) — HUD ставить до шкоди «?».</summary>
        public bool DamageUncertain;

        /// <summary>
        /// Правило без кубика: скільки ударів у дії і скільки з них влучать — відомо
        /// наперед (накопичувач бійця). 0/0 — правило з кубиком.
        /// </summary>
        public int PredictedShots, PredictedHits;

        /// <summary>Скільки ОД коштує саме ця дія.</summary>
        public int ApCost;
        /// <summary>Відстань до цілі (клітинки, Чебишев) і максимальна дальність дії.</summary>
        public int Distance, Range;
        public bool HasLineOfSight;

        // ---- Перевірка здібності (перша партія docs/ABILITIES.md; інваріант 8 — видно до кліку) ----
        /// <summary>"Contest" — навичка проти Волі цілі; "Condition" — умова на полі; null — перевірки немає.</summary>
        public string CheckKind;
        /// <summary>"intimidate" | "shred" | null.</summary>
        public string CheckSkill;
        /// <summary>Що в тебе (навичка / стерто броні) і скільки треба.</summary>
        public int CheckValue, CheckThreshold;
        /// <summary>Ціль не піддається зовсім («Імунітет»: бос, оглушений).</summary>
        public bool CheckImmune;
        public bool CheckPasses;
        /// <summary>Чому дію не почати: "immune" | "rallied" | "armor_intact"; null — можна.</summary>
        public string CheckBlockKey;
    }

    /// <summary>
    /// Пастка, яку бачить гравець: лише СВОЇ (ворожі лишаються прихованими, поки
    /// не спрацюють). Власник, 25.09.2026: «Поставлену пастку на арені поки не
    /// видно. погано».
    /// </summary>
    public sealed class BattleTrapView
    {
        public GridPosView Pos;
        /// <summary>Здібність, що поставила пастку (назва для підказки).</summary>
        public string AbilityId;
        public int TrapDamage;
        /// <summary>Стан при спрацюванні ("KnockedDown" тощо), null — лише шкода.</summary>
        public string StatusOnTrigger;
    }

    /// <summary>Прев'ю руху поточного юніта до тайла (docs/COMBAT_V2.md §3).</summary>
    /// <summary>
    /// Шанс удару поточного юніта по ворогу з гіпотетичної клітинки — «голограма» руху (подача бою П3,
    /// docs/research/RT_COMBAT_PRESENTATION.md). Те саме число, що покаже прев'ю атаки, коли юніт уже
    /// стоятиме там (інваріант 8: поріг/шанс видно заздалегідь).
    /// </summary>
    public sealed class ShotFromView
    {
        public string TargetId;
        public int Chance;
        /// <summary>Укриття цілі проти цієї точки: "None" | "Half" | "Full".</summary>
        public string TargetCover;
    }

    /// <summary>Укриття клітинки з кожного боку (подача бою П4): "None" | "Half" | "Full".</summary>
    public sealed class TileCoverView
    {
        public int X, Y;
        public string North, East, South, West;
        /// <summary>Найкраще з чотирьох — значок біля юніта.</summary>
        public string Best;
    }

    public sealed class MovePathView
    {
        /// <summary>"Success" | "NotReachable" | "NotEnoughAp" | "InvalidAction".</summary>
        public string Result;
        public int ApCost;
        /// <summary>Тайли шляху без стартового, до кінцевого включно.</summary>
        public IReadOnlyList<GridPosView> Tiles;
        /// <summary>Тайли шляху, які накриває ворожий дозор.</summary>
        public IReadOnlyList<GridPosView> OverwatchThreatTiles;
    }

    /// <summary>Одна здібність поточного юніта з ціною і станом відкату — те, що HUD показує на кнопці (§BattleHudScreen.DrawAbilities).</summary>
    public sealed class BattleAbilityView
    {
        public string Id;
        public int ApCost;

        /// <summary>0 — здібність готова просто зараз; N — ще N власних ходів юніта до готовності.</summary>
        public int CooldownRemaining;

        /// <summary>Дальність (Чебишев) — HUD і арена показують зону дії озброєної здібності.</summary>
        public int Range;

        /// <summary>Тип цілі: "Self" | "Ally" | "AllyOrSelf" | "Enemy" | "Tile".</summary>
        public string Targeting;

        /// <summary>Потрібна клітинка: пастка на тайл або «Наказ пересунутися» (союзник, потім клітинка).</summary>
        public bool NeedsTargetTile;
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

        /// <summary>Ціна в ОД до кожного з <see cref="ReachableTiles"/> — той самий порядок (docs/COMBAT_V2.md §7.1).</summary>
        public IReadOnlyList<int> ReachableTileCosts;

        /// <summary>Поточний юніт під керуванням ШІ — презентер зобов'язаний сам вести його хід (<see cref="GameSession.CombatAiStepOneAction"/>).</summary>
        public bool IsAiTurn;

        /// <summary>Пастки гравця на арені (ворожі приховані).</summary>
        public IReadOnlyList<BattleTrapView> Traps;

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

        /// <summary>Черга поточного раунду (у раунді 1 може бути переставлена стартом бою, Поправка №14.1).</summary>
        public IReadOnlyList<string> InitiativeOrder;

        /// <summary>Черга наступних раундів — звичайна впереміш; колесо черги показує її після межі раунду.</summary>
        public IReadOnlyList<string> NextRoundOrder;

        /// <summary>Як почався бій: "Encounter"|"FirstStrike"|"Ambush"|"Spotted"|"UnderFire"|"Surrounded" (Поправка №14.1).</summary>
        public string Opening;

        /// <summary>Досьє (Поправка №14.6): розвідка відкриває картку ворога — Виживання ≥ N або Кмітливість ≥ M; видно до бою.</summary>
        public int DossierScoutSurvival, DossierScoutWits;

        /// <summary>
        /// Ключ тексту «що буде, якщо відступити» для підтвердження відступу
        /// (Поправка №14.7, ROADMAP B13) — залежить від того, хто просив бій
        /// (данж, вузол чи фінал, тренування).
        /// </summary>
        public string RetreatConsequenceKey;

        /// <summary>
        /// Журнал бою для гравця (R7): ключі таблиці з аргументами, НЕ готові
        /// рядки. Слова підставляє Gameplay (<c>BattleLogText</c>). Раніше тут
        /// ішов внутрішній трейс <c>CombatState.Log</c> — російський текст із
        /// сирими іменами enum, і фолбек-екран бою показував його гравцю як є.
        /// </summary>
        public IReadOnlyList<BattleLogLineView> Log;

        public bool IsHitRulePercent;

        /// <summary>Об'єкти поля (Поправка №14.4).</summary>
        public IReadOnlyList<BattleObjectView> Objects;

        /// <summary>Зони вогню.</summary>
        public IReadOnlyList<BattleFireView> Fires;

        /// <summary>Раунд найближчого підкріплення ворога (0 — не чекається) і скільки прийде — відлік у HUD.</summary>
        public int ReinforcementRound, ReinforcementCount;
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
