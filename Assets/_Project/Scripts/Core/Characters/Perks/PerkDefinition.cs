using System;
using System.Collections.Generic;
using Game.Core.Stats;

namespace Game.Core.Characters.Perks
{
    /// <summary>
    /// Перк — преимущественно пассивный бонус (GDD US-3.10). Билд строится
    /// вложением в скилы, а перки превращают вложение в числа.
    ///
    /// Именно сюда ложится рост живучести: US-5.2 требует, чтобы HP и защита
    /// росли перками и гиром при почти статичных атрибутах, и перк это умеет,
    /// потому что производные живут в агрегаторе как обычные статы.
    /// </summary>
    [Serializable]
    public sealed class PerkDefinition
    {
        public string Id;
        public string DisplayName;

        /// <summary>Скил-гейт: какой скил и до какого уровня надо вкачать.</summary>
        public SkillType GatingSkill = SkillType.None;
        public int RequiredSkillLevel;

        /// <summary>Перки, без которых этот недоступен.</summary>
        public List<string> PrerequisitePerkIds = new List<string>();

        public List<StatModifier> Modifiers = new List<StatModifier>();

        /// <summary>Активная способность, если перк её выдаёт (редкий случай).</summary>
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

    /// <summary>Почему перк недоступен. Причина нужна интерфейсу, чтобы объяснить игроку.</summary>
    public enum PerkAvailability
    {
        Available = 0,
        SkillTooLow = 1,
        MissingPrerequisite = 2,
        AlreadyTaken = 3,
        Invalid = 4
    }
}
