using System;
using System.Collections.Generic;
using Game.Core.Stats;

namespace Game.Core.Characters
{
    /// <summary>
    /// Перк — пассивный бонус через цифры (GDD §3.2 US-3.10): билд идёт вложением
    /// очков в скилы, перки открываются по порогам (US-2.2) и опциональным
    /// пререквизитам-перкам. Боевые модификаторы льются в единый агрегатор
    /// (Source = Perk); флэт к проверкам — в CheckModifierFor (соц/утилита-пассив).
    /// В Unity обернётся ScriptableObject; респека нет — открытое не закрывается.
    /// </summary>
    [Serializable]
    public sealed class PerkDefinition
    {
        public string Id;
        public string DisplayName;

        /// <summary>Скил-гейт: открывается при уровне скила ≥ порога.</summary>
        public SkillType Skill = SkillType.None;
        public int RequiredSkillLevel = 1;

        /// <summary>Пререквизит (US-2.2: «порог скила + пререквизиты»): нужен уже открытый перк.</summary>
        public string RequiresPerkId;

        /// <summary>Пассивные модификаторы производных статов (Source = Perk).</summary>
        public List<StatModifier> Modifiers = new List<StatModifier>();

        /// <summary>Флэт к проверкам по скилам — пассив соц/утилитарных веток (US-3.11).</summary>
        public List<SkillCheckModifier> CheckModifiers = new List<SkillCheckModifier>();

        public PerkDefinition() { }

        public PerkDefinition(string id, string displayName, SkillType skill, int requiredLevel)
        {
            Id = id;
            DisplayName = displayName;
            Skill = skill;
            RequiredSkillLevel = requiredLevel;
        }

        public bool UnlockedFor(Companion companion)
            => companion != null && Skill != SkillType.None
               && companion.GetSkill(Skill) >= RequiredSkillLevel
               && (string.IsNullOrEmpty(RequiresPerkId) || companion.HasPerk(RequiresPerkId));

        public int CheckModifierFor(SkillType skill)
        {
            int sum = 0;
            for (int i = 0; i < CheckModifiers.Count; i++)
                if (CheckModifiers[i].Skill == skill) sum += CheckModifiers[i].Value;
            return sum;
        }

        // ---- Флюент-хелперы для авторинга контента ----
        public PerkDefinition With(DerivedStat stat, double value, ModMode mode = ModMode.Flat)
        {
            Modifiers.Add(new StatModifier(stat, value, mode, ModifierSource.Perk));
            return this;
        }

        public PerkDefinition WithCheck(SkillType skill, int value)
        {
            CheckModifiers.Add(new SkillCheckModifier(skill, value));
            return this;
        }

        public PerkDefinition Requires(string perkId)
        {
            RequiresPerkId = perkId;
            return this;
        }
    }
}
