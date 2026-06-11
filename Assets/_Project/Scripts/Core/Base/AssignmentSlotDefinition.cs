using System;
using Game.Core.Stats;

namespace Game.Core.Base
{
    /// <summary>
    /// Позиция на базе (GDD §8.1). Назначенный напарник прежде всего РЕЗОЛВИТ
    /// события здания через свой релевантный скил/трейты (US-8.2), а затем усиливает
    /// эффект здания. Рабочие держат базовый уровень; усиливать может только
    /// напарник (нанятых спецов нет). Полностью data-driven (в Unity — ScriptableObject).
    /// </summary>
    [Serializable]
    public sealed class AssignmentSlotDefinition
    {
        public string Id;
        public string DisplayName;
        public BaseSectionType Section;

        /// <summary>Скил, которым позиция резолвит свои события и оценивает эффективность (US-8.2).</summary>
        public SkillType RelevantSkill = SkillType.None;

        /// <summary>Вторичный фактор — атрибут (опционально).</summary>
        public AttributeType RelevantAttribute = AttributeType.None;

        /// <summary>Открыта ли позиция изначально (false — требует постройки здания).</summary>
        public bool UnlockedByDefault = true;

        public AssignmentSlotDefinition() { }

        public AssignmentSlotDefinition(string id, string displayName, BaseSectionType section)
        {
            Id = id;
            DisplayName = displayName;
            Section = section;
        }
    }
}
