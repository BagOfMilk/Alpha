using System;
using System.Collections.Generic;
using Game.Core.Stats;

namespace Game.Core.Characters.Perks
{
    /// <summary>
    /// Перк — переважно пасивний бонус (GDD US-3.10). Білд будується
    /// вкладенням у скіли, а перки перетворюють вкладення на числа.
    ///
    /// Саме сюди лягає ріст живучості: US-5.2 вимагає, щоб HP і захист
    /// росли перками й гіром при майже статичних атрибутах, і перк це вміє,
    /// бо похідні живуть в агрегаторі як звичайні стати.
    /// </summary>
    [Serializable]
    public sealed class PerkDefinition
    {
        public string Id;
        public string DisplayName;

        /// <summary>Скіл-гейт: який скіл і до якого рівня треба прокачати.</summary>
        public SkillType GatingSkill = SkillType.None;
        public int RequiredSkillLevel;

        /// <summary>Перки, без яких цей недоступний.</summary>
        public List<string> PrerequisitePerkIds = new List<string>();

        public List<StatModifier> Modifiers = new List<StatModifier>();

        /// <summary>Активна здатність, якщо перк її дає (рідкісний випадок).</summary>
        public string GrantedAbilityId;

        public PerkDefinition() { }

        public PerkDefinition(string id, string displayName, SkillType gate, int requiredLevel)
        {
            Id = id;
            DisplayName = displayName;
            GatingSkill = gate;
            RequiredSkillLevel = requiredLevel;
        }

        public PerkDefinition WithModifier(StatKey key, double value, ModMode mode = ModMode.Flat)
        {
            Modifiers.Add(new StatModifier(key, value, mode, ModifierSource.Perk, Id));
            return this;
        }

        public PerkDefinition Requiring(string perkId)
        {
            if (!string.IsNullOrEmpty(perkId)) PrerequisitePerkIds.Add(perkId);
            return this;
        }
    }

    /// <summary>Чому перк недоступний. Причина потрібна інтерфейсу, щоб пояснити гравцю.</summary>
    public enum PerkAvailability
    {
        Available = 0,
        SkillTooLow = 1,
        MissingPrerequisite = 2,
        AlreadyTaken = 3,
        Invalid = 4
    }
}
