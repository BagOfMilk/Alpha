using System;
using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Stats;

namespace Game.Core.Characters
{
    /// <summary>Стартовый уровень одного скила в бэкграунде.</summary>
    [Serializable]
    public struct SkillSeed
    {
        public SkillType Skill;
        public int Level;

        public SkillSeed(SkillType skill, int level)
        {
            Skill = skill;
            Level = level;
        }
    }

    /// <summary>
    /// Бэкграунд — стартовый шаблон персонажа (GDD §2.5 US-2.7). Классов НЕТ:
    /// бэкграунд лишь задаёт осмысленно разный старт (атрибуты + базовые скилы +
    /// стартовые трейты), а дальше любой может качать любую ветку. Ростового
    /// профиля нет — очки скилов распределяет игрок. В Unity — ScriptableObject.
    /// </summary>
    [Serializable]
    public sealed class Background
    {
        public string Id;
        public string DisplayName;

        public AttributeBlock StartingAttributes = new AttributeBlock();
        public List<SkillSeed> StartingSkills = new List<SkillSeed>();
        public List<Traits.Trait> StartingTraits = new List<Traits.Trait>();

        public Background() { }

        public Background(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }

        /// <summary>Создаёт нового напарника 1-го уровня по этому бэкграунду.</summary>
        public Companion CreateInstance(string instanceId, BalanceConfig cfg)
        {
            int slots = cfg != null ? cfg.TraitSlots : 4;
            var comp = new Companion(instanceId, StartingAttributes.Clone(), slots)
            {
                DisplayName = DisplayName
            };
            for (int i = 0; i < StartingSkills.Count; i++)
                comp.Skills.Set(StartingSkills[i].Skill, StartingSkills[i].Level);
            for (int i = 0; i < StartingTraits.Count; i++)
                comp.Traits.TryAdd(StartingTraits[i]);
            return comp;
        }

        // ---- Флюент-хелперы для авторинга контента в коде ----
        public Background WithAttributes(int strength, int agility, int wits, int will)
        {
            StartingAttributes = new AttributeBlock(strength, agility, wits, will);
            return this;
        }

        public Background WithSkill(SkillType skill, int level)
        {
            StartingSkills.Add(new SkillSeed(skill, level));
            return this;
        }

        public Background WithTrait(Traits.Trait trait)
        {
            if (trait != null) StartingTraits.Add(trait);
            return this;
        }
    }
}
