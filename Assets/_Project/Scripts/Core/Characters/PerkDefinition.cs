using System;
using System.Collections.Generic;
using Game.Core.Stats;

namespace Game.Core.Characters
{
    /// <summary>
    /// Перк — пассивный бонус через цифры (GDD §3.2 US-3.10): билд идёт вложением
    /// очков в скилы, перки открываются по порогам (US-2.2). Модификаторы льются
    /// в единый агрегатор производных статов (Source = Perk). В Unity обернётся
    /// ScriptableObject; респека нет — открытое не закрывается.
    /// </summary>
    [Serializable]
    public sealed class PerkDefinition
    {
        public string Id;
        public string DisplayName;

        /// <summary>Скил-гейт: открывается при уровне скила ≥ порога.</summary>
        public SkillType Skill = SkillType.None;
        public int RequiredSkillLevel = 1;

        /// <summary>Пассивные модификаторы производных статов (Source = Perk).</summary>
        public List<StatModifier> Modifiers = new List<StatModifier>();

        public PerkDefinition() { }

        public PerkDefinition(string id, string displayName, SkillType skill, int requiredLevel)
        {
            Id = id;
            DisplayName = displayName;
            Skill = skill;
            RequiredSkillLevel = requiredLevel;
        }

        public bool UnlockedFor(Companion companion)
            => companion != null && Skill != SkillType.None && companion.GetSkill(Skill) >= RequiredSkillLevel;

        // ---- Флюент-хелпер для авторинга контента ----
        public PerkDefinition With(DerivedStat stat, double value, ModMode mode = ModMode.Flat)
        {
            Modifiers.Add(new StatModifier(stat, value, mode, ModifierSource.Perk));
            return this;
        }
    }
}
