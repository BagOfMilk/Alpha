using System;
using System.Collections.Generic;
using Game.Core.Combat;
using Game.Core.Stats;

namespace Game.Core.Items
{
    /// <summary>Разброс одного стата базы: [Min..Max] до множителя редкости (Вариант Б).</summary>
    [Serializable]
    public struct StatRoll
    {
        public DerivedStat Stat;
        public int Min;
        public int Max;

        public StatRoll(DerivedStat stat, int min, int max)
        {
            Stat = stat;
            Min = min;
            Max = max;
        }
    }

    /// <summary>
    /// Статическое описание предмета (Эпик 6, US-18.1): в Unity обернётся
    /// ScriptableObject (ItemDefinition) + ItemInstance в рантайме. Гир крутит ЧИСЛА
    /// (StatRolls), способности гиром не выдаются (US-6.2). Оружие несёт
    /// WeaponDefinition (для именного — с уникальным проком). Именной предмет
    /// фиксирован (Min==Max, редкость не катается) + может иметь IItemEffect.
    /// </summary>
    [Serializable]
    public sealed class ItemDefinition
    {
        public string Id;
        public string DisplayName;
        public EquipSlot Slot;

        /// <summary>Именной предмет: фиксированные роллы и редкость, есть уникальный эффект (US-6.1).</summary>
        public bool IsNamed;
        public Rarity NamedRarity = Rarity.Rare;

        public List<StatRoll> StatRolls = new List<StatRoll>();

        /// <summary>Для Slot==Weapon: оружие, уходящее в бой. У именного — с уникальным проком.</summary>
        public WeaponDefinition Weapon;

        /// <summary>Уникальный эффект именного предмета (опц.).</summary>
        public IItemEffect Effect;

        public ItemDefinition() { }

        public ItemDefinition(string id, string displayName, EquipSlot slot)
        {
            Id = id;
            DisplayName = displayName;
            Slot = slot;
        }

        // ---- Флюент-хелперы авторинга контента ----
        public ItemDefinition Roll(DerivedStat stat, int min, int max)
        {
            StatRolls.Add(new StatRoll(stat, min, max));
            return this;
        }

        public ItemDefinition Fixed(DerivedStat stat, int value)
        {
            StatRolls.Add(new StatRoll(stat, value, value));
            return this;
        }

        public ItemDefinition WithWeapon(WeaponDefinition weapon) { Weapon = weapon; return this; }
        public ItemDefinition Named(Rarity rarity, IItemEffect effect = null)
        {
            IsNamed = true;
            NamedRarity = rarity;
            Effect = effect;
            return this;
        }
    }
}
