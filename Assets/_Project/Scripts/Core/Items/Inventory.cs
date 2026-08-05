using System.Collections.Generic;

namespace Game.Core.Items
{
    /// <summary>
    /// Сташ отряда (Эпик 6/15): лут с вылазок копится здесь — это faucet гира для
    /// экипировки и крафта. Живёт на базе; экспедиция при победе складывает дроп
    /// сюда. Простой список рантайм-экземпляров — без слотов и веса (ПЛЕЙСХОЛДЕР).
    /// </summary>
    public sealed class Inventory
    {
        private readonly List<ItemInstance> _items = new List<ItemInstance>();

        public IReadOnlyList<ItemInstance> Items => _items;
        public int Count => _items.Count;

        public void Add(ItemInstance item)
        {
            if (item != null) _items.Add(item);
        }

        public bool Remove(ItemInstance item) => _items.Remove(item);

        /// <summary>
        /// «Снаряжение возвращается с телом»: снимает всё надетое с погибшего и
        /// кладёт в сташ. Без этого дефицитный (и единственный в кампании именной)
        /// гир навсегда исчезал вместе с бойцом — снять его было уже нечем.
        /// НЕ применять к ушедшим в антагонисты: их гир возвращает убийство босса
        /// (US-9.4, DefectionSystem.ReturnGearOnKill) — иначе предмет задвоится.
        /// Возвращает число вернувшихся предметов.
        /// </summary>
        public int RecoverGearFrom(Characters.Companion fallen)
        {
            if (fallen == null) return 0;
            int recovered = 0;
            foreach (EquipSlot slot in System.Enum.GetValues(typeof(EquipSlot)))
            {
                var item = fallen.Equipment.Unequip(slot);
                if (item == null) continue;
                Add(item);
                recovered++;
            }
            return recovered;
        }
    }
}
