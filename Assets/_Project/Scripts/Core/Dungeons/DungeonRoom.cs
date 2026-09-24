using System.Collections.Generic;
using Game.Core.Checks;

namespace Game.Core.Dungeons
{
    /// <summary>Тип кімнати данжу (Эпик 12, R4): бій / гарантований лут / подія-вибір.</summary>
    public enum DungeonRoomKind
    {
        Combat = 0,
        Cache = 1,
        Event = 2
    }

    /// <summary>
    /// Авторська кімната push-your-luck данжу (Core/Dungeons).
    ///
    /// НАВМИСНЕ БЕЗ ГЕНЕРАТОРА (R1/R4): попередня версія (архів) збирала кімнати
    /// з модулів кубиком; тестова збірка вимагає повного детермінізму в ядрі, тож
    /// кожен сайт — фіксований, впорядкований список таких кімнат
    /// (<see cref="DefaultDungeon"/>). Непередбачуваність лишається — вона йде не
    /// з кидка, а з неповноти інформації в самого гравця (яку саме полосу дасть
    /// тихий обхід залежить від того, кого він взяв у відряд).
    ///
    /// Жодного типу з Game.Core.Combat чи Game.Core.Items: вороги й предмети —
    /// рядкові id, які D1 перетворює на реальні дані вже за межами цього пакета
    /// (R4 — данж не залежить від бою; охоронний тест —
    /// ArchitectureGuardTests.Dungeons_DoNotReferenceCombatOrItems).
    /// </summary>
    public sealed class DungeonRoomDefinition
    {
        public readonly string Id;
        public readonly string DisplayNameKey;
        public readonly DungeonRoomKind Kind;

        // ---- Combat ----
        /// <summary>
        /// Тихий обхід (Поправка №1 для данжу): АЛЬТЕРНАТИВНІ («або») перевірки —
        /// відряд бере ту, що дає кращу полосу. Кожна бойова кімната має хоч
        /// одну (R1 — «кожна Combat-кімната має QuietBypass»).
        /// </summary>
        public readonly List<CheckRequest> QuietChecks = new List<CheckRequest>();

        /// <summary>Id ворогів для кровавого шляху — дані для BattleRequest, не тип бою.</summary>
        public readonly List<string> EnemyIds = new List<string>();

        /// <summary>Ключ арени (розмір/викладку знає вже бойовий шар).</summary>
        public string ArenaKey;

        // ---- Спільне для Combat (нагорода за зачистку) і Cache (гарантоване) ----
        public int GuaranteedMaterials;
        public int GuaranteedGold;

        /// <summary>Тільки Cache: іменний предмет, який видається завжди. Null — немає.</summary>
        public string NamedItemId;

        // ---- Event ----
        /// <summary>Мінімум два варіанти — інакше це не вибір (US-12.3).</summary>
        public readonly List<DungeonEventOption> EventOptions = new List<DungeonEventOption>();

        public DungeonRoomDefinition(string id, string displayNameKey, DungeonRoomKind kind)
        {
            Id = id;
            DisplayNameKey = displayNameKey;
            Kind = kind;
        }
    }

    /// <summary>
    /// Один варіант кімнати-події (кімната 3, §3.4): розмін «здобич у незабанковане
    /// ↔ Threat» і, іноді, соціальні наслідки. Наслідки — ЛИШЕ ДАНІ: Core/Dungeons
    /// не знає ні про FearState, ні про FactionRegistry (інші пакети) — D1 читає
    /// їх з <see cref="DungeonConsequence"/> і застосовує сам (список — seamsForD1).
    /// </summary>
    public sealed class DungeonEventOption
    {
        public readonly string Id;          // "greedy" | "cautious" — ключ, не текст
        public readonly string LabelKey;
        public readonly int MaterialsGain;
        public readonly int GoldGain;
        public readonly int ThreatDelta;
        public readonly bool CausesFear;
        public readonly IReadOnlyDictionary<string, int> FactionDeltas;
        public readonly IReadOnlyList<string> FlagsToSet;

        public DungeonEventOption(string id, string labelKey, int materialsGain, int goldGain, int threatDelta,
            bool causesFear = false, IReadOnlyDictionary<string, int> factionDeltas = null,
            IReadOnlyList<string> flagsToSet = null)
        {
            Id = id;
            LabelKey = labelKey;
            MaterialsGain = materialsGain;
            GoldGain = goldGain;
            ThreatDelta = threatDelta;
            CausesFear = causesFear;
            FactionDeltas = factionDeltas ?? EmptyDeltas;
            FlagsToSet = flagsToSet ?? EmptyFlags;
        }

        private static readonly Dictionary<string, int> EmptyDeltas = new Dictionary<string, int>();
        private static readonly List<string> EmptyFlags = new List<string>();
    }
}
