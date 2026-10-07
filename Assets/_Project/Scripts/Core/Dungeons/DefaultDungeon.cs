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
    ///
    /// Поправка №12.5 (два компоненти): данжі — головний кран КРАФТОВОГО
    /// компонента (схованки зброї й припасів, льох скиту), бойові кімнати
    /// дають будівельний (розібрані укріплення табору). Подія «жадібно»
    /// дає обидва і більше, «обережно» — трохи обох. Числа — ПЛЕЙСХОЛДЕР.
    /// </summary>
    public static class DefaultDungeon
    {
        public const string AbandonedCamp = "abandoned_camp";
        public const string OldHermitage = "old_hermitage";

        /// <summary>
        /// Подія «жадібно» в «Покинутому таборі»: забране зерно авангарду. Крім прямої ціни (страх, Тугар),
        /// лишає рядок у підсумку (<c>StoryEchoes</c>) — M1.2.
        /// </summary>
        public const string AbandonedCampGrainTakenFlag = "abandoned_camp_grain_taken";

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
                GuaranteedBuildComponent = 2,
                GuaranteedGold = 4
            };
            // Тихо: обійти (Виживання ≥5) АБО переконати здатися (Переконання ≥5) — §3.4.
            room1.QuietChecks.Add(new CheckRequest(SkillKeys.Survival, 5, ApproachForm.Neutral,
                topicId: "dungeon.abandoned_camp.room1"));
            room1.QuietChecks.Add(new CheckRequest(SkillKeys.Persuade, 5, ApproachForm.Persuade,
                topicId: "dungeon.abandoned_camp.room1"));
            // Поправка №11 (25.09.2026, дослівно власника: «обрізання на 5-му
            // раунді - обрізання не треба, це дизайнерсьеке рішення, щоб данжі
            // були побудовані так що саме бої в ниж були короткими але тяжкими»):
            // раніше тут стояли два горд-розвідники (Skirmisher, лук 2-4) —
            // під грамотним автобоєм 2v3 вирішувалось за 1-2 раунди без
            // жодного ризику (перемога 20/20, ніхто не падав), під наївним —
            // теж рідко довше 5 раундів, але й рідко тяжко. Менше ворогів,
            // небезпечніший (HordeVanguard: Ривок з контакту, вища точність,
            // урон без броні) + один розвідник на дистанції — рішення з
          // першого ходу, а не розмінна відсидка. SkirmishPacingTests тримає ціль.
            room1.EnemyIds.Add("horde_vanguard");
            room1.EnemyIds.Add("horde_scout");

            var room2 = new DungeonRoomDefinition("hidden_cache", "dungeon.room2.title", DungeonRoomKind.Cache)
            {
                GuaranteedBuildComponent = 0,
                GuaranteedCraftComponent = 3,
                GuaranteedGold = 0,
                NamedItemId = "scout_horn" // item.scout_horn.found / .effect (§7.11)
            };

            var room3 = new DungeonRoomDefinition("hidden_ashes", "dungeon.room3.title", DungeonRoomKind.Event);
            room3.EventOptions.Add(new DungeonEventOption(
                id: "greedy", labelKey: "dungeon.room3.greedy",
                buildGain: 3, goldGain: 0, threatDelta: 2,
                causesFear: true,
                factionDeltas: new Dictionary<string, int> { ["tuhar_boyars"] = -5 },
                flagsToSet: new[] { AbandonedCampGrainTakenFlag },
                craftGain: 2));
            room3.EventOptions.Add(new DungeonEventOption(
                id: "cautious", labelKey: "dungeon.room3.cautious",
                buildGain: 1, goldGain: 0, threatDelta: 0, craftGain: 1));

            return new List<DungeonRoomDefinition> { room1, room2, room3 };
        }

        // ---- «Старий скит» — другий, менший данж для вільної гри ----

        private static List<DungeonRoomDefinition> OldHermitageRooms()
        {
            var room1 = new DungeonRoomDefinition("hermitage_watch", "dungeon.hermitage.room1.title",
                DungeonRoomKind.Combat)
            {
                ArenaKey = "arena_hermitage_yard_8x8",
                GuaranteedBuildComponent = 1,
                GuaranteedCraftComponent = 1,
                GuaranteedGold = 2
            };
            room1.QuietChecks.Add(new CheckRequest(SkillKeys.Survival, 4, ApproachForm.Neutral,
                topicId: "dungeon.old_hermitage.room1"));
            // Поправка №14.4: підкріплення з відліком — розвідник скиту на 3-му раунді.
            // Під старим правилом порогу бій ішов у глухий кут (шанс < 50 не влучав
            // ніколи), тому його було знято; з правилом без кубика, де кожен відсоток
            // справджується (власник, 29.09.2026), підкріплення повернуто.
            // Поправка №11 (розвідка SkirmishPacingTests, 25.09.2026): цей рядок
            // до сьогодні не резолвився в жоден EnemyDefinition —
            // GameSession.ResolveEnemyById повертав null, CombatBattleBuilder
            // мовчки пропускав спавн (жоден каталог не знав "forest_bandit"/
            // "enemy.forest_bandit"), і кровавий шлях цієї кімнати розв'язувався
            // проти НУЛЯ ворогів: перемога за один раунд без жодного ризику.
            // DefaultCombatContent.ForestBandit() тепер заповнює цей id.
            room1.EnemyIds.Add("forest_bandit");
            room1.Reinforcements.Add(new RoomReinforcement(3, "horde_scout"));

            var room2 = new DungeonRoomDefinition("hermitage_cellar", "dungeon.hermitage.room2.title",
                DungeonRoomKind.Cache)
            {
                GuaranteedBuildComponent = 0,
                GuaranteedCraftComponent = 3,
                GuaranteedGold = 2
            };

            return new List<DungeonRoomDefinition> { room1, room2 };
        }
    }
}
