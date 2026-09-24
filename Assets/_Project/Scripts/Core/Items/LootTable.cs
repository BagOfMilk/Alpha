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
        /// віддає перший іменний предмет; інакше — предмет пулу за позицією,
        /// на яку масштабується полоса (<see cref="IndexForBand"/>).
        /// </summary>
        public ItemInstance Roll(OutcomeBand band)
        {
            if (band == OutcomeBand.Best && _named.Count > 0)
                return ItemInstance.NamedFrom(_named[0]);

            if (Pool.Count == 0) return null;
            int idx = IndexForBand(band, Pool.Count);

            var def = Pool[idx];
            if (def.IsNamed) return ItemInstance.NamedFrom(def);
            return new ItemInstance(def, RarityFor(band));
        }

        /// <summary>
        /// Позиція в пулі за полосою виходу. <see cref="OutcomeBand"/> має рівно
        /// 4 значення (Worst..Best) — для пулу з ≤4 позицій індекс просто
        /// дорівнює значенню полоси (затиснутим до розміру пулу, як і раніше).
        /// Для БІЛЬШОГО пулу індекс полоси НЕ клемпиться (це раніше лишало
        /// позиції 4+ назавжди недосяжними, а Найкраща полоса на такому пулі
        /// віддавала б item[3] — предмет із середини пулу, а не найкращий) —
        /// натомість 0..3 рівномірно розтягуються на 0..Pool.Count-1, тож
        /// Найгірша й Найкраща полоси завжди впираються у справжні краї пулу.
        /// </summary>
        internal static int IndexForBand(OutcomeBand band, int poolCount)
        {
            const int bandCount = 4; // OutcomeBand: Worst, Base, Good, Best
            if (poolCount <= 1) return 0;
            if (poolCount <= bandCount) return Math.Min((int)band, poolCount - 1);

            double scaled = (double)(int)band / (bandCount - 1) * (poolCount - 1);
            return (int)Math.Round(scaled, MidpointRounding.AwayFromZero);
        }
    }
}
