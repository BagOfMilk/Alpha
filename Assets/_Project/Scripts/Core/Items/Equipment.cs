using System.Collections.Generic;
using Game.Core.Stats;

namespace Game.Core.Items
{
    /// <summary>
    /// Спорядження напарника: по одному предмету на слот. Модифікатори
    /// надітого зливаються в єдиний агрегатор похідних (Source = Gear) —
    /// гір крутить числа, не роздаючи здібностей (US-6.2/18.2). Додається до
    /// <c>Companion._providers</c> нарівні з трейтами/шрамами/перками.
    /// </summary>
    public sealed class Equipment : IModifierProvider, IWorldEffectSource
    {
        private readonly Dictionary<EquipSlot, ItemInstance> _slots = new Dictionary<EquipSlot, ItemInstance>();

        public ItemInstance Get(EquipSlot slot) => _slots.TryGetValue(slot, out var i) ? i : null;

        /// <summary>
        /// Пошук серед НАДІТОГО за стабільним <see cref="ItemInstance.InstanceId"/> —
        /// дзеркало <see cref="Inventory.Find"/> для другої точки зберігання предмета.
        /// Контракт GameSession (docs/TEST_BUILD.md §4.1) адресує предмет лише
        /// itemInstanceId, а сам предмет може лежати в сташі АБО вже бути надітим
        /// (напр. перенадіти на іншого напарника, чи апгрейднути вже надіте);
        /// D1 резолвить id спершу через Inventory.Find, а тут — по кожному
        /// напарнику роздачі, чиє спорядження варто перевірити.
        /// </summary>
        public ItemInstance Find(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return null;
            foreach (var item in _slots.Values)
                if (string.Equals(item.InstanceId, instanceId, System.StringComparison.Ordinal))
                    return item;
            return null;
        }

        /// <summary>Надіває предмет у його слот; повертає раніше надіте в цьому слоті (або null).</summary>
        public ItemInstance Equip(ItemInstance item)
        {
            if (item == null) return null;
            var prev = Get(item.Slot);
            _slots[item.Slot] = item;
            return prev;
        }

        /// <summary>Знімає предмет зі слоту; повертає зняте (або null).</summary>
        public ItemInstance Unequip(EquipSlot slot)
        {
            var prev = Get(slot);
            _slots.Remove(slot);
            return prev;
        }

        public void CollectModifiers(List<StatModifier> into)
        {
            if (into == null) return;
            foreach (var kv in _slots)
                foreach (var m in kv.Value.Modifiers())
                    into.Add(m);
        }

        /// <summary>
        /// Ефекти надітого спорядження, що сягають за межі агрегатора статів
        /// (напр. «Ріг вивідника»). Equipment лише читає дані з визначення —
        /// застосування ефекту не тут (див. <see cref="ItemWorldEffect"/>).
        /// </summary>
        public IEnumerable<ItemWorldEffect> ActiveWorldEffects()
        {
            foreach (var kv in _slots)
                if (kv.Value.Definition.WorldEffect != null)
                    yield return kv.Value.Definition.WorldEffect;
        }
    }
}
