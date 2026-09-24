using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Checks;

namespace Game.Core.Dungeons
{
    /// <summary>
    /// Авторський контент данжів (R1/R4) — жодного генератора: кожен сайт це
    /// фіксований, впорядкований список кімнат.
    ///
    /// <c>abandoned_camp</c> — «Покинутий табір авангарду», доба 4 основного
    /// сценарію тестової збірки (§3.4/§7.11 специфікації): кімната бою з тихим
    /// обходом, гарантована схованка з іменним предметом, подія-вибір
    /// «жадібно/обережно». <c>old_hermitage</c> — другий, менший данж на іншій
    /// точці — для вільної гри після доби 5 (Поправка №7.5, §3.6).
    /// </summary>
    public static class DefaultDungeon
    {
        public const string AbandonedCamp = "abandoned_camp";
        public const string OldHermitage = "old_hermitage";

        public static IReadOnlyList<string> KnownSiteIds { get; } = new[] { AbandonedCamp, OldHermitage };

        public static IReadOnlyList<DungeonRoomDefinition> Rooms(string siteId)
        {
            switch (siteId)
            {
                case AbandonedCamp: return AbandonedCampRooms();
                case OldHermitage: return OldHermitageRooms();
                default: return null;
            }
        }

        /// <summary>Зручний вхід: збирає прогін одразу з авторських кімнат сайту.</summary>
        public static DungeonRun Start(string siteId, IReadOnlyList<string> partyIds, BalanceConfig cfg)
        {
            var rooms = Rooms(siteId);
            return rooms == null ? null : new DungeonRun(siteId, rooms, partyIds, cfg);
        }

        // ---- «Покинутий табір авангарду» (§3.4, §7.11) ----

        private static List<DungeonRoomDefinition> AbandonedCampRooms()
        {
            var room1 = new DungeonRoomDefinition("scouts_left_behind", "dungeon.room1.title", DungeonRoomKind.Combat)
            {
                ArenaKey = "arena_camp_yard_8x8",
                GuaranteedMaterials = 2,
                GuaranteedGold = 4
            };
            // Тихо: обійти (Виживання ≥5) АБО переконати здатися (Переконання ≥5) — §3.4.
            room1.QuietChecks.Add(new CheckRequest(SkillKeys.Survival, 5, ApproachForm.Neutral,
                topicId: "dungeon.abandoned_camp.room1"));
            room1.QuietChecks.Add(new CheckRequest(SkillKeys.Persuade, 5, ApproachForm.Persuade,
                topicId: "dungeon.abandoned_camp.room1"));
            room1.EnemyIds.Add("horde_skirmisher");
            room1.EnemyIds.Add("horde_skirmisher");

            var room2 = new DungeonRoomDefinition("hidden_cache", "dungeon.room2.title", DungeonRoomKind.Cache)
            {
                GuaranteedMaterials = 3,
                GuaranteedGold = 0,
                NamedItemId = "scout_horn" // item.scout_horn.found / .effect (§7.11)
            };

            var room3 = new DungeonRoomDefinition("hidden_ashes", "dungeon.room3.title", DungeonRoomKind.Event);
            room3.EventOptions.Add(new DungeonEventOption(
                id: "greedy", labelKey: "dungeon.room3.greedy",
                materialsGain: 5, goldGain: 0, threatDelta: 2,
                causesFear: true,
                factionDeltas: new Dictionary<string, int> { ["tuhar_boyars"] = -5 },
                flagsToSet: new[] { "abandoned_camp_grain_taken" }));
            room3.EventOptions.Add(new DungeonEventOption(
                id: "cautious", labelKey: "dungeon.room3.cautious",
                materialsGain: 2, goldGain: 0, threatDelta: 0));

            return new List<DungeonRoomDefinition> { room1, room2, room3 };
        }

        // ---- «Старий скит» — другий, менший данж для вільної гри ----

        private static List<DungeonRoomDefinition> OldHermitageRooms()
        {
            var room1 = new DungeonRoomDefinition("hermitage_watch", "dungeon.hermitage.room1.title",
                DungeonRoomKind.Combat)
            {
                ArenaKey = "arena_hermitage_yard_8x8",
                GuaranteedMaterials = 2,
                GuaranteedGold = 2
            };
            room1.QuietChecks.Add(new CheckRequest(SkillKeys.Survival, 4, ApproachForm.Neutral,
                topicId: "dungeon.old_hermitage.room1"));
            room1.EnemyIds.Add("forest_bandit");

            var room2 = new DungeonRoomDefinition("hermitage_cellar", "dungeon.hermitage.room2.title",
                DungeonRoomKind.Cache)
            {
                GuaranteedMaterials = 3,
                GuaranteedGold = 2
            };

            return new List<DungeonRoomDefinition> { room1, room2 };
        }
    }
}
