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
        /// Пошук серед НАДІТОГО на цього напарника за стабільним
        /// <see cref="ItemInstance.InstanceId"/> — дзеркало <see cref="Inventory.Find"/>
        /// для другої точки зберігання предмета. Це первинний примітив (одне
        /// спорядження); D1 не мусить перебирати roster вручну і викликати
        /// його по одному — готовий, вже правильно впорядкований пошук по обох
        /// точках зберігання одразу — <see cref="Inventory.FindAnywhere"/>.
        /// </summary>
        public ItemInstance Find(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return null;
            foreach (var item in _slots.Values)
                if (string.Equals(item.InstanceId, instanceId, System.StringComparison.Ordinal))
                    return item;
            return null;
        }

        /// <summary>Надіває предмет у його слот; повертає раніше надіте в цьому слоті (або null).
        /// Предмети, витіснені дворучністю (щит), — лише через <see cref="EquipDisplacing"/>.</summary>
        public ItemInstance Equip(ItemInstance item)
        {
            var displaced = EquipDisplacing(item);
            return displaced.Count > 0 ? displaced[0] : null;
        }

        /// <summary>
        /// Надіває предмет і повертає ВСЕ, що довелося зняти (№19.2): попереднє в тому ж слоті;
        /// для дворучної зброї — ще й щит; для щита — дворучну зброю. Нічого не губиться: викликач
        /// кладе витіснене в сташ.
        /// </summary>
        public List<ItemInstance> EquipDisplacing(ItemInstance item)
        {
            var displaced = new List<ItemInstance>();
            if (item == null) return displaced;
            var prev = Get(item.Slot);
            if (prev != null) displaced.Add(prev);
            if (item.Slot == EquipSlot.Weapon && item.Definition.TwoHanded)
            {
                var shield = Unequip(EquipSlot.Offhand);
                if (shield != null) displaced.Add(shield);
            }
            else if (item.Slot == EquipSlot.Offhand)
            {
                var weapon = Get(EquipSlot.Weapon);
                if (weapon != null && weapon.Definition.TwoHanded)
                {
                    Unequip(EquipSlot.Weapon);
                    displaced.Add(weapon);
                }
            }
            _slots[item.Slot] = item;
            return displaced;
        }

        /// <summary>Усе надіте в порядку слотів (для сейву, «ляльки» і моделі).</summary>
        public IEnumerable<ItemInstance> All()
        {
            foreach (EquipSlot slot in System.Enum.GetValues(typeof(EquipSlot)))
            {
                var item = Get(slot);
                if (item != null) yield return item;
            }
        }

        /// <summary>Надіте — тим самим форматом, що й сташ (<see cref="ItemCodec"/>); слот береться з визначення.</summary>
        public string CaptureState() => ItemCodec.Encode(All());

        /// <summary>Відновлення надітого зі слепка; чужі/невідомі визначення пропускаються.</summary>
        public void RestoreState(string blob)
        {
            _slots.Clear();
            foreach (var item in ItemCodec.Decode(blob))
                _slots[item.Slot] = item;
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
