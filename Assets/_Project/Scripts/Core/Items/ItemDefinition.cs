using System;
using System.Collections.Generic;
using Game.Core.Stats;

namespace Game.Core.Items
{
    /// <summary>
    /// Один базовий вклад гіра в стат: ключ агрегатора + авторське БАЗОВЕ
    /// значення. Значення не «котиться» діапазоном — інваріант 1 (в ядрі
    /// немає джерела випадковості) заборонив би це і для лута — тож у Items
    /// нема діапазону [Min..Max]: є одне авторське число, яке масштабує
    /// рідкість (крім іменних — вони фіксовані, US-6.1).
    /// </summary>
    [Serializable]
    public struct ItemStatEntry
    {
        public StatKey Key;
        public double BaseValue;

        public ItemStatEntry(StatKey key, double baseValue)
        {
            Key = key;
            BaseValue = baseValue;
        }
    }

    /// <summary>
    /// Статичний опис предмета (Епік 6, US-18.1): у Unity обгортається
    /// ScriptableObject (ItemDefinition) + ItemInstance у рантаймі. Гір
    /// крутить ЧИСЛА (StatRolls), здібності гіром не видаються (US-6.2).
    /// Іменний предмет фіксований (рідкість не котиться) і може мати
    /// <see cref="Effect"/> (унікальний бонус статів) та/або
    /// <see cref="WorldEffect"/> (дані для системи поза інвентарем — B3 сам
    /// цей другий ефект не застосовує, лише оголошує; див. коментар типу).
    /// </summary>
    [Serializable]
    public sealed class ItemDefinition
    {
        public string Id;
        public string DisplayName;
        public EquipSlot Slot;

        /// <summary>Іменний предмет: фіксовані роли й рідкість, є унікальний ефект (US-6.1).</summary>
        public bool IsNamed;
        public Rarity NamedRarity = Rarity.Rare;

        public List<ItemStatEntry> StatRolls = new List<ItemStatEntry>();

        /// <summary>Унікальний ефект іменного предмета: додаткові модифікатори статів (опц.).</summary>
        public IItemEffect Effect;

        /// <summary>
        /// Ефект іменного предмета, що сягає ЗА межі агрегатора статів (опц.,
        /// напр. «Ріг вивідника» → передвісники). Тільки дані — див.
        /// <see cref="ItemWorldEffect"/>.
        /// </summary>
        public ItemWorldEffect WorldEffect;

        public ItemDefinition() { }

        public ItemDefinition(string id, string displayName, EquipSlot slot)
        {
            Id = id;
            DisplayName = displayName;
            Slot = slot;
        }

        // ---- Флюент-хелпери авторингу контенту ----

        /// <summary>Базове значення стата: рідкість помножить магнітуду (крім іменних).</summary>
        public ItemDefinition WithStat(StatKey key, double baseValue)
        {
            StatRolls.Add(new ItemStatEntry(key, baseValue));
            return this;
        }

        public ItemDefinition Named(Rarity rarity, IItemEffect effect = null)
        {
            IsNamed = true;
            NamedRarity = rarity;
            Effect = effect;
            return this;
        }

        public ItemDefinition WithWorldEffect(string key, int charges)
        {
            WorldEffect = new ItemWorldEffect(key, charges);
            return this;
        }
    }
}
