using System;
using System.Collections.Generic;
using Game.Core.Combat;
using Game.Core.Stats;

namespace Game.Core.Items
{
    /// <summary>
    /// Рантайм-экземпляр предмета: определение + конкретная редкость + СВёрнутые
    /// роллы статов (Source = Gear, уходят в единый агрегатор). Для дропа роллы
    /// катаются в [Min..Max] × множитель редкости (Вариант Б); для именного —
    /// фиксированы. Уникальный эффект именного добавляет модификаторы поверх.
    /// </summary>
    public sealed class ItemInstance
    {
        public ItemDefinition Definition { get; }
        public Rarity Rarity { get; private set; }

        private readonly List<StatModifier> _mods = new List<StatModifier>();
        public IReadOnlyList<StatModifier> StatMods => _mods;

        public EquipSlot Slot => Definition.Slot;
        public WeaponDefinition Weapon => Definition.Weapon;
        public string DisplayName => Definition.DisplayName;

        public ItemInstance(ItemDefinition definition, Rarity rarity, IRng rng)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Resolve(rarity, rng);
        }

        /// <summary>Именной предмет: фиксированные роллы и редкость (детерминирован).</summary>
        public static ItemInstance NamedFrom(ItemDefinition def)
            => new ItemInstance(def, def.NamedRarity, new ScriptedRng());

        /// <summary>
        /// Восстановление из сейва (US-16.1): редкость и УЖЕ свёрнутые БАЗОВЫЕ роллы
        /// (<see cref="StatMods"/>) берутся как есть — без повторного броска, чтобы гир
        /// после загрузки был идентичен. Эффект именного добавится из Definition сам.
        /// </summary>
        public static ItemInstance FromSaved(ItemDefinition def, Rarity rarity, IEnumerable<StatModifier> mods)
        {
            var inst = new ItemInstance(def, rarity, null);
            inst._mods.Clear();
            if (mods != null) inst._mods.AddRange(mods);
            return inst;
        }

        /// <summary>Все модификаторы: базовые роллы + уникальный эффект (Source = Gear).</summary>
        public IEnumerable<StatModifier> Modifiers()
        {
            for (int i = 0; i < _mods.Count; i++) yield return _mods[i];
            if (Definition.Effect != null)
                foreach (var m in Definition.Effect.ExtraModifiers()) yield return m;
        }

        /// <summary>Крафт-апгрейд: поднять редкость и пере-катать роллы на новую магнитуду.</summary>
        internal void RerollAt(Rarity rarity, IRng rng) => Resolve(rarity, rng);

        private void Resolve(Rarity rarity, IRng rng)
        {
            Rarity = rarity;
            _mods.Clear();
            // Именные фиксированы: роллов не катают и магнитудой редкости не множатся (US-6.1).
            double mult = Definition.IsNamed ? 1.0 : RarityTuning.MagnitudeMultiplier(rarity);
            var rolls = Definition.StatRolls;
            for (int i = 0; i < rolls.Count; i++)
            {
                var r = rolls[i];
                int baseRoll = r.Min >= r.Max ? r.Min : (rng != null ? rng.Range(r.Min, r.Max) : r.Min);
                int value = (int)Math.Round(baseRoll * mult);
                if (value != 0) _mods.Add(new StatModifier(r.Stat, value)); // Mode=Flat, Source=Gear
            }
        }
    }
}
