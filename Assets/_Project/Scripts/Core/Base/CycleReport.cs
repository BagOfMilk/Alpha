using System.Collections.Generic;
using Game.Core.Economy;

namespace Game.Core.Base
{
    /// <summary>
    /// Сводка по итогам одного цикла (дня). Возвращается из AdvanceCycle и
    /// удобна для показа игроку: «что произвела база за день».
    /// </summary>
    public sealed class CycleReport
    {
        public int Cycle;

        /// <summary>Произведённые ресурсы за цикл (resource -> сколько).</summary>
        public readonly Dictionary<ResourceType, int> Produced = new Dictionary<ResourceType, int>();

        /// <summary>Накопленные пассивные бонусы за цикл (bonusId -> величина).</summary>
        public readonly Dictionary<string, int> PassiveBonuses = new Dictionary<string, int>();

        /// <summary>Напарники, повысившие уровень в этом цикле.</summary>
        public readonly List<string> LeveledUp = new List<string>();

        /// <summary>Напарники, полностью вылечившиеся в этом цикле.</summary>
        public readonly List<string> Recovered = new List<string>();

        /// <summary>Не хватило еды на содержание поселения.</summary>
        public bool FoodShortage;

        internal void AddProduced(ResourceType resource, int amount)
        {
            if (resource == ResourceType.None || amount == 0) return;
            Produced.TryGetValue(resource, out var cur);
            Produced[resource] = cur + amount;
        }

        internal void AddPassive(string bonusId, int amount)
        {
            if (string.IsNullOrEmpty(bonusId) || amount == 0) return;
            PassiveBonuses.TryGetValue(bonusId, out var cur);
            PassiveBonuses[bonusId] = cur + amount;
        }
    }
}
