using System.Collections.Generic;
using Game.Core.Combat;
using Game.Core.Stats;

namespace Game.Core.Items
{
    /// <summary>
    /// Экипировка напарника: по одному предмету на слот. Модификаторы надетого
    /// сливаются в единый агрегатор производных (через Companion.CollectModifiers,
    /// Source = Gear) — гир крутит числа, не выдавая способностей (US-6.2/18.2).
    /// </summary>
    public sealed class Equipment
    {
        private readonly Dictionary<EquipSlot, ItemInstance> _slots = new Dictionary<EquipSlot, ItemInstance>();

        public ItemInstance Get(EquipSlot slot) => _slots.TryGetValue(slot, out var i) ? i : null;

        /// <summary>Оружие в бой (named — с уникальным проком); null если слот пуст.</summary>
        public WeaponDefinition EquippedWeapon => Get(EquipSlot.Weapon)?.Weapon;

        /// <summary>Надевает предмет в его слот; возвращает ранее надетое в этом слоте (или null).</summary>
        public ItemInstance Equip(ItemInstance item)
        {
            if (item == null) return null;
            var prev = Get(item.Slot);
            _slots[item.Slot] = item;
            return prev;
        }

        /// <summary>Снимает предмет со слота; возвращает снятое (или null).</summary>
        public ItemInstance Unequip(EquipSlot slot)
        {
            var prev = Get(slot);
            _slots.Remove(slot);
            return prev;
        }

        public IEnumerable<StatModifier> Modifiers()
        {
            foreach (var kv in _slots)
                foreach (var m in kv.Value.Modifiers())
                    yield return m;
        }
    }
}
