using System;
using System.Collections.Generic;
using Game.Core.Combat;

namespace Game.Core.Items
{
    /// <summary>
    /// Лут-таблица: пул базовых определений + веса редкости (Common..Epic). Источник
    /// рандом-дропа (Эпик 6.1). Именные предметы тут НЕ катаются — они зарабатываются
    /// (боссы/квесты/локации), для них есть отдельные определения.
    /// </summary>
    public sealed class LootTable
    {
        public readonly List<ItemDefinition> Pool = new List<ItemDefinition>();

        /// <summary>Веса редкости по индексу Rarity (Common, Uncommon, Rare, Epic).</summary>
        public int[] RarityWeights = { 60, 25, 12, 3 };

        public LootTable Add(ItemDefinition def)
        {
            if (def != null) Pool.Add(def);
            return this;
        }
    }

    /// <summary>
    /// Генерация дропа (Вариант Б): редкость взвешена, магнитуда роллов множится
    /// редкостью. RNG инъецируется — дроп детерминирован в тестах и по сиду.
    /// </summary>
    public static class LootGenerator
    {
        public static Rarity RollRarity(int[] weights, IRng rng)
        {
            if (weights == null || weights.Length == 0) return Rarity.Common;
            int total = 0;
            for (int i = 0; i < weights.Length; i++) total += Math.Max(0, weights[i]);
            if (total <= 0) return Rarity.Common;

            int roll = rng.Range(1, total);
            int acc = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                acc += Math.Max(0, weights[i]);
                if (roll <= acc) return (Rarity)i;
            }
            return Rarity.Common;
        }

        /// <summary>Один дроп из таблицы: выбор базы + редкость + свёрнутые роллы.</summary>
        public static ItemInstance Roll(LootTable table, IRng rng)
        {
            if (table == null || table.Pool.Count == 0) return null;
            var def = table.Pool[rng.Range(0, table.Pool.Count - 1)];
            if (def.IsNamed) return ItemInstance.NamedFrom(def);
            var rarity = RollRarity(table.RarityWeights, rng);
            return new ItemInstance(def, rarity, rng);
        }
    }
}
