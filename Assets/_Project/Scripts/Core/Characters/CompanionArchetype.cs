using System;
using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Characters.Traits;
using Game.Core.Stats;

namespace Game.Core.Characters
{
    /// <summary>
    /// Шаблон напарника. Чистые данные: стартовые атрибуты, стартовые скилы,
    /// профиль роста и стартовые трейты. В Unity оборачивается ScriptableObject,
    /// но логика создания напарника живёт здесь, чтобы тестироваться без движка.
    ///
    /// Заполняется сеттерами, а не объектным инициализатором: одна строка — один
    /// факт контента. Так диффы читаемы, а генератор карты разбирает архетип
    /// простым регекспом вместо разбора вложенных блоков.
    /// </summary>
    [Serializable]
    public sealed class CompanionArchetype
    {
        public string Id;
        public string DisplayName;

        /// <summary>Стартовые атрибуты (шкала 1–10). За уровни не растут.</summary>
        public AttributeSet Attributes = new AttributeSet();

        /// <summary>Стартовые скилы (шкала 0–10). Ноль означает «не умеет».</summary>
        public SkillSet Skills = new SkillSet();

        /// <summary>Веса распределения очков скилов при повышении уровня.</summary>
        public SkillGrowthProfile Growth = new SkillGrowthProfile();

        /// <summary>Трейты, с которыми напарник приходит (US-2.4).</summary>
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
        /// Создаёт нового напарника 1-го уровня по этому архетипу.
        /// Баланс нужен для ёмкости слотов трейтов: null означает дефолты.
        /// </summary>
        public Companion CreateInstance(string instanceId, BalanceConfig cfg = null)
        {
            return new Companion(instanceId, this, cfg);
        }
    }
}
