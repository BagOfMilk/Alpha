using System;
using System.Collections.Generic;
using Game.Core.Combat;
using Game.Core.Items;
using Game.Core.Stats;

namespace Game.Core.Dungeons
{
    /// <summary>
    /// Сборка комнат данжа из модулей (US-12.1), детерминированная по RNG. Глубже —
    /// опаснее: состав боя растёт по глубине, на пороге угрозы появляется элита
    /// (US-12.2/3.15 — сложность составом, не раздуванием HP). Числа — тюнинг здесь
    /// (не в BalanceConfig), чтобы данж был самодостаточен.
    /// </summary>
    public sealed class DungeonGenerator
    {
        public readonly List<EnemyDefinition> EnemyPool = new List<EnemyDefinition>();
        public LootTable ItemTable;

        // Веса типов комнат: Combat / Loot / Check / Event.
        public int[] TypeWeights = { 50, 25, 15, 10 };

        public int ThreatPerDepth = 2;        // +угроза за каждый уход вглубь
        public int EliteThreatThreshold = 6;  // ≥ → элита/подкрепление
        public int MaxEnemies = 6;

        public int BaseCombatGold = 8;
        public int BaseLootGold = 12;

        public DungeonGenerator(IEnumerable<EnemyDefinition> enemyPool, LootTable itemTable)
        {
            if (enemyPool != null) EnemyPool.AddRange(enemyPool);
            ItemTable = itemTable;
        }

        public DungeonRoom NextRoom(int depth, int threat, IRng rng)
        {
            var type = (RoomType)PickWeighted(TypeWeights, rng);
            var room = new DungeonRoom(type, depth);
            switch (type)
            {
                case RoomType.Combat: FillCombat(room, depth, threat, rng); break;
                case RoomType.Loot: FillLoot(room, depth, rng); break;
                case RoomType.Check: FillCheck(room, depth); break;
                case RoomType.Event: FillEvent(room, depth, rng); break;
            }
            return room;
        }

        private void FillCombat(DungeonRoom room, int depth, int threat, IRng rng)
        {
            room.DisplayName = "Бой";
            int count = Math.Min(2 + depth / 2, MaxEnemies);
            for (int i = 0; i < count && EnemyPool.Count > 0; i++)
                room.Enemies.Add(EnemyPool[rng.Range(0, EnemyPool.Count - 1)]);

            if (threat >= EliteThreatThreshold && EnemyPool.Count > 0)
            {
                room.Elite = true;
                room.DisplayName = "Бой (элита)";
                room.Enemies.Add(EnemyPool[rng.Range(0, EnemyPool.Count - 1)]); // подкрепление
            }

            // Награда за зачистку (глубже — больше); материалы — основной профит данжа.
            room.LootGold = BaseCombatGold * depth;
            room.LootBuilding = 1 + depth / 2;
            room.LootCrafting = 1 + depth / 3;
        }

        private void FillLoot(DungeonRoom room, int depth, IRng rng)
        {
            room.DisplayName = "Схрон";
            room.LootGold = BaseLootGold * depth;
            room.LootBuilding = 2 + depth / 2;
            room.LootCrafting = 1 + depth / 2;
            room.ItemTable = ItemTable;
            room.ItemDrops = 1 + (rng.D100() <= 25 ? 1 : 0); // иногда два
        }

        private void FillCheck(DungeonRoom room, int depth)
        {
            // Утилитарная проверка (US-12.3): провал — мягкий сетбэк (рост угрозы).
            room.DisplayName = "Препятствие";
            room.CheckSkill = SkillType.Hacking;
            room.CheckThreshold = 2 + depth / 3;
            room.CheckRewardGold = 10 * depth;
            room.EventThreatDelta = ThreatPerDepth; // провал → опаснее дальше
        }

        private void FillEvent(DungeonRoom room, int depth, IRng rng)
        {
            if (rng.D100() <= 55)
            {
                room.EventId = "cache";
                room.DisplayName = "Тайник";
                room.EventLootGold = 6 * depth;
            }
            else
            {
                room.EventId = "collapse";
                room.DisplayName = "Обвал";
                room.EventThreatDelta = ThreatPerDepth * 2; // опаснее дальше
            }
        }

        private static int PickWeighted(int[] weights, IRng rng)
        {
            int total = 0;
            for (int i = 0; i < weights.Length; i++) total += Math.Max(0, weights[i]);
            if (total <= 0) return 0;
            int roll = rng.Range(1, total);
            int acc = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                acc += Math.Max(0, weights[i]);
                if (roll <= acc) return i;
            }
            return weights.Length - 1;
        }
    }
}
