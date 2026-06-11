using System;
using System.Collections.Generic;
using Game.Core.Stats;

namespace Game.Core.Traits
{
    /// <summary>Знак трейта — добродетель (+), порок (−) или нейтральный.</summary>
    public enum TraitSign
    {
        Neutral = 0,
        Virtue = 1,
        Vice = 2
    }

    /// <summary>
    /// Трейт — тег со знаком (GDD §2.3). Влияет на проверки (флэт-модификаторы),
    /// на бой (модификаторы производных статов) и несёт «ценности» для лёгкого
    /// эмерджентного слоя связей напарников (US-9.6). Ограничен слотами
    /// (<see cref="TraitSet"/>). В Unity оборачивается ScriptableObject.
    /// </summary>
    [Serializable]
    public sealed class Trait
    {
        public string Id;
        public string DisplayName;
        public TraitSign Sign = TraitSign.Neutral;
        public int SlotCost = 1;

        /// <summary>Флэт-модификаторы к проверкам по скилам (US-2.6).</summary>
        public List<SkillCheckModifier> CheckModifiers = new List<SkillCheckModifier>();

        /// <summary>Боевые модификаторы производных статов (US-3.8). Source = Trait.</summary>
        public List<StatModifier> CombatModifiers = new List<StatModifier>();

        /// <summary>Теги-ценности для связей/баентера напарников (US-9.6).</summary>
        public List<string> ValueTags = new List<string>();

        public Trait() { }

        public Trait(string id, string displayName, TraitSign sign = TraitSign.Neutral)
        {
            Id = id;
            DisplayName = displayName;
            Sign = sign;
        }

        public int CheckModifierFor(SkillType skill)
        {
            int sum = 0;
            for (int i = 0; i < CheckModifiers.Count; i++)
                if (CheckModifiers[i].Skill == skill) sum += CheckModifiers[i].Value;
            return sum;
        }

        // ---- Флюент-хелперы для авторинга контента в коде (DefaultContent) ----
        public Trait WithCheck(SkillType skill, int value)
        {
            CheckModifiers.Add(new SkillCheckModifier(skill, value));
            return this;
        }

        public Trait WithCombat(DerivedStat stat, double value, ModMode mode = ModMode.Flat)
        {
            CombatModifiers.Add(new StatModifier(stat, value, mode, ModifierSource.Trait));
            return this;
        }

        public Trait WithValue(string tag)
        {
            ValueTags.Add(tag);
            return this;
        }
    }
}
