using System;
using Game.Core.Balance;
using Game.Core.Stats;
using Game.Core.Traits;

namespace Game.Core.Characters
{
    public enum CreationStep
    {
        Ok = 0,
        NoBudget = 1,        // очки кончились
        AtCap = 2,           // потолок атрибута/скила при создании
        NoTraitRoom = 3,     // нет бюджета трейтов или свободных слотов
        DuplicateTrait = 4
    }

    /// <summary>
    /// Создание протагониста-лидера (US-2.7): бэкграунд даёт осмысленно разный
    /// старт, игрок докидывает очки атрибутов/скилов и стартовые трейты в рамках
    /// бюджета (point-buy). Валидация на каждом шаге; Build выдаёт готового
    /// напарника с IsProtagonist. Чистый C# — UI поверх (роадмап).
    /// </summary>
    public sealed class ProtagonistBuilder
    {
        private readonly Background _background;
        private readonly BalanceConfig _cfg;
        private readonly AttributeBlock _attributes;
        private readonly SkillSet _skills = new SkillSet();
        private readonly TraitSet _traits;
        private int _extraTraitsUsed;

        public string DisplayName;

        public int AttributePointsRemaining { get; private set; }
        public int SkillPointsRemaining { get; private set; }

        public ProtagonistBuilder(Background background, BalanceConfig cfg)
        {
            _background = background ?? throw new ArgumentNullException(nameof(background));
            _cfg = cfg ?? new BalanceConfig();
            _attributes = background.StartingAttributes.Clone();
            _traits = new TraitSet(_cfg.TraitSlots);

            for (int i = 0; i < background.StartingSkills.Count; i++)
                _skills.Set(background.StartingSkills[i].Skill, background.StartingSkills[i].Level);
            for (int i = 0; i < background.StartingTraits.Count; i++)
                _traits.TryAdd(background.StartingTraits[i]);

            AttributePointsRemaining = _cfg.ProtagonistAttributePoints;
            SkillPointsRemaining = _cfg.ProtagonistSkillPoints;
            DisplayName = background.DisplayName;
        }

        public int Attribute(AttributeType type) => _attributes.Get(type);
        public int Skill(SkillType type) => _skills.Get(type);

        /// <summary>+1 к атрибуту за очко бюджета (потолок создания — CreationAttributeMax).</summary>
        public CreationStep RaiseAttribute(AttributeType type)
        {
            // None — не атрибут: без гарда очко списывалось в пустоту (симметрично RaiseSkill).
            if (type == AttributeType.None) return CreationStep.AtCap;
            if (AttributePointsRemaining <= 0) return CreationStep.NoBudget;
            if (_attributes.Get(type) >= _cfg.CreationAttributeMax) return CreationStep.AtCap;
            _attributes.Add(type, 1);
            AttributePointsRemaining--;
            return CreationStep.Ok;
        }

        /// <summary>+1 к скилу за очко бюджета (потолок старта — CreationSkillMax).</summary>
        public CreationStep RaiseSkill(SkillType type)
        {
            if (type == SkillType.None) return CreationStep.AtCap;
            if (SkillPointsRemaining <= 0) return CreationStep.NoBudget;
            if (_skills.Get(type) >= _cfg.CreationSkillMax) return CreationStep.AtCap;
            _skills.Raise(type, 1);
            SkillPointsRemaining--;
            return CreationStep.Ok;
        }

        /// <summary>Стартовый трейт сверх бэкграундных (бюджет + свободные слоты, US-2.4).</summary>
        public CreationStep AddTrait(Trait trait)
        {
            if (trait == null) return CreationStep.DuplicateTrait;
            if (_extraTraitsUsed >= _cfg.ProtagonistExtraTraits) return CreationStep.NoTraitRoom;

            var result = _traits.TryAdd(trait);
            switch (result)
            {
                case TraitAddResult.Added:
                    _extraTraitsUsed++;
                    return CreationStep.Ok;
                case TraitAddResult.AlreadyPresent:
                    return CreationStep.DuplicateTrait;
                default:
                    return CreationStep.NoTraitRoom; // слоты забиты — вытеснение не для создания
            }
        }

        /// <summary>Собирает протагониста-лидера. Нерастраченные очки пропадают осознанно.</summary>
        public Companion Build(string id)
        {
            var c = new Companion(id, _attributes.Clone(), _cfg.TraitSlots)
            {
                DisplayName = string.IsNullOrEmpty(DisplayName) ? _background.DisplayName : DisplayName,
                IsProtagonist = true
            };
            foreach (SkillType skill in Enum.GetValues(typeof(SkillType)))
            {
                if (skill == SkillType.None) continue;
                int level = _skills.Get(skill);
                if (level > 0) c.Skills.Set(skill, level);
            }
            foreach (var t in _traits.Traits) c.Traits.TryAdd(t);
            return c;
        }
    }
}
