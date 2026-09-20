using System;
using System.Collections.Generic;

namespace Game.Core.Economy
{
    /// <summary>
    /// Кошелёк базы: текущее количество каждого ресурса. Понимает попытку
    /// списания (CanAfford / TrySpend), чтобы экономика не уходила в минус.
    /// </summary>
    [Serializable]
    public sealed class ResourceLedger
    {
        private readonly Dictionary<ResourceType, int> _amounts = new Dictionary<ResourceType, int>();

        // Событие на изменение баланса убрано: подписчиков не было ни одного, а
        // UI, ради которого оно заводилось, придёт вместе с UI Toolkit (Э18).
        // Опрос через Get/Snapshot покрывает все нынешние нужды.

        public int Get(ResourceType resource)
        {
            return _amounts.TryGetValue(resource, out var v) ? v : 0;
        }

        public void Add(ResourceType resource, int amount)
        {
            if (resource == ResourceType.None || amount == 0) return;
            var next = Get(resource) + amount;
            if (next < 0) next = 0;
            _amounts[resource] = next;
        }

        public bool CanAfford(ResourceType resource, int cost)
        {
            return cost <= 0 || Get(resource) >= cost;
        }

        public bool CanAfford(IReadOnlyDictionary<ResourceType, int> costs)
        {
            if (costs == null) return true;
            foreach (var kv in costs)
                if (!CanAfford(kv.Key, kv.Value)) return false;
            return true;
        }

        public bool TrySpend(ResourceType resource, int cost)
        {
            if (!CanAfford(resource, cost)) return false;
            Add(resource, -cost);
            return true;
        }

        public bool TrySpend(IReadOnlyDictionary<ResourceType, int> costs)
        {
            if (!CanAfford(costs)) return false;
            if (costs != null)
                foreach (var kv in costs)
                    Add(kv.Key, -kv.Value);
            return true;
        }

        public IReadOnlyDictionary<ResourceType, int> Snapshot() => _amounts;
    }
}
