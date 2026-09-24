using System;
using System.Collections.Generic;
using Game.Core.Checks;

namespace Game.Core.Items
{
    /// <summary>
    /// Лут-таблиця: авторський пул визначень у ФІКСОВАНОМУ порядку — від
    /// найгіршого до найкращого. Жодних вагових рандом-роллів (інваріант 1,
    /// R1 специфікації тестової збірки): дроп — детермінована функція від
    /// <see cref="OutcomeBand"/> самої вилазки/данжу/бою, а не кубика.
    /// Іменні предмети (гарантований лут — напр. «Ріг вивідника») додаються
    /// окремою таблицею вище цього пулу через <see cref="AddNamed"/> і
    /// видаються тим самим детермінованим правилом «за полосою».
    /// </summary>
    public sealed class LootTable
    {
        public readonly List<ItemDefinition> Pool = new List<ItemDefinition>();
        private readonly List<ItemDefinition> _named = new List<ItemDefinition>();

        /// <summary>Додає звичайний (не іменний) предмет наступним за якістю в пулі.</summary>
        public LootTable Add(ItemDefinition def)
        {
            if (def != null) Pool.Add(def);
            return this;
        }

        /// <summary>Додає іменний предмет — видається гарантовано на Найкращій полосі.</summary>
        public LootTable AddNamed(ItemDefinition def)
        {
            if (def != null && def.IsNamed) _named.Add(def);
            return this;
        }

        /// <summary>
        /// Рідкість дропу детермінована полосою виходу (Варіант Б): чим краще
        /// вийшло, тим вища ГАРАНТОВАНА рідкість — не шанс на неї.
        /// </summary>
        public static Rarity RarityFor(OutcomeBand band)
        {
            switch (band)
            {
                case OutcomeBand.Best: return Rarity.Epic;
                case OutcomeBand.Good: return Rarity.Rare;
                case OutcomeBand.Base: return Rarity.Uncommon;
                default: return Rarity.Common;
            }
        }

        /// <summary>
        /// Один дроп за полосою — детермінований вибір (без випадковості):
        /// той самий пул + та сама полоса завжди дають той самий предмет і ту
        /// саму рідкість. Найкраща полоса з непорожнім <see cref="_named"/>
        /// віддає перший іменний предмет; інакше — предмет пулу з індексом
        /// полоси (затиснутим до розміру пулу).
        /// </summary>
        public ItemInstance Roll(OutcomeBand band)
        {
            if (band == OutcomeBand.Best && _named.Count > 0)
                return ItemInstance.NamedFrom(_named[0]);

            if (Pool.Count == 0) return null;
            int idx = Math.Min((int)band, Pool.Count - 1);
            if (idx < 0) idx = 0;

            var def = Pool[idx];
            if (def.IsNamed) return ItemInstance.NamedFrom(def);
            return new ItemInstance(def, RarityFor(band));
        }
    }
}
