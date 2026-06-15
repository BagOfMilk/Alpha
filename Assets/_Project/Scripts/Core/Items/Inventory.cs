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
    }
}
