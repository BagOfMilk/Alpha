using System;
using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Characters.Traits;
using Game.Core.Stats;

namespace Game.Core.Characters
{
    /// <summary>
    /// Шаблон напарника. Чисті дані: стартові атрибути, стартові скіли,
    /// профіль росту і стартові трейти. В Unity обгортається ScriptableObject,
    /// але логіка створення напарника живе тут, щоб тестуватися без рушія.
    ///
    /// Заповнюється сеттерами, а не об'єктним ініціалізатором: один рядок — один
    /// факт контенту. Так дифи читані, а генератор карти розбирає архетип
    /// простим регекспом замість розбору вкладених блоків.
    /// </summary>
    [Serializable]
    public sealed class CompanionArchetype
    {
        public string Id;
        public string DisplayName;

        /// <summary>Стартові атрибути (шкала 1–10). За рівні не ростуть.</summary>
        public AttributeSet Attributes = new AttributeSet();

        /// <summary>Стартові скіли (шкала 0–10). Нуль означає «не вміє».</summary>
        public SkillSet Skills = new SkillSet();

        /// <summary>Ваги розподілу очок скілів при підвищенні рівня.</summary>
        public SkillGrowthProfile Growth = new SkillGrowthProfile();

        /// <summary>Трейти, з якими напарник приходить (US-2.4).</summary>
        public List<TraitDefinition> StartingTraits = new List<TraitDefinition>();

        public CompanionArchetype() { }

        public CompanionArchetype(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }

        public CompanionArchetype SetAttribute(AttributeType attribute, int value)
        {
            Attributes[attribute] = value;
            return this;
        }

        public CompanionArchetype SetSkill(SkillType skill, int value)
        {
            Skills[skill] = value;
            return this;
        }

        public CompanionArchetype SetGrowth(SkillType skill, double weight)
        {
            Growth.SetWeight(skill, weight);
            return this;
        }

        public CompanionArchetype AddStartingTrait(TraitDefinition trait)
        {
            if (trait != null) StartingTraits.Add(trait);
            return this;
        }

        /// <summary>
        /// Створює нового напарника 1-го рівня за цим архетипом.
        /// Баланс потрібен для ємності слотів трейтів: null означає дефолти.
        /// </summary>
        public Companion CreateInstance(string instanceId, BalanceConfig cfg = null)
        {
            return new Companion(instanceId, this, cfg);
        }
    }
}
