using System.Collections.Generic;
using Game.Core.Stats;

namespace Game.Core.Characters.Perks
{
    /// <summary>
    /// Взятые персонажем перки.
    ///
    /// Метода «забыть перк» нет — «респека нет» (US-2.2) обеспечивается
    /// отсутствием API, а не памятью разработчика. Необратимость вложений —
    /// часть дизайна, поэтому она вшита в тип.
    /// </summary>
    public sealed class CompanionPerks : IModifierProvider
    {
        private readonly List<PerkDefinition> _taken = new List<PerkDefinition>();

        public IReadOnlyList<PerkDefinition> Taken => _taken;
        public int Count => _taken.Count;

        public bool Has(string perkId)
        {
            if (string.IsNullOrEmpty(perkId)) return false;
            for (int i = 0; i < _taken.Count; i++)
                if (_taken[i].Id == perkId) return true;
            return false;
        }

        /// <summary>
        /// Доступен ли перк при таких скилах. Чистая проверка без побочных
        /// эффектов — её же зовёт предпросмотр билда, чтобы показать пороги
        /// заранее (US-2.3).
        /// </summary>
        public PerkAvailability Evaluate(PerkDefinition perk, SkillSet skills)
        {
            if (perk == null || string.IsNullOrEmpty(perk.Id)) return PerkAvailability.Invalid;
            if (Has(perk.Id)) return PerkAvailability.AlreadyTaken;

            if (perk.GatingSkill != SkillType.None)
            {
                int level = skills == null ? 0 : skills[perk.GatingSkill];
                if (level < perk.RequiredSkillLevel) return PerkAvailability.SkillTooLow;
            }

            var prereqs = perk.PrerequisitePerkIds;
            if (prereqs != null)
                for (int i = 0; i < prereqs.Count; i++)
                    if (!Has(prereqs[i])) return PerkAvailability.MissingPrerequisite;

            return PerkAvailability.Available;
        }

        public PerkAvailability TryTake(PerkDefinition perk, SkillSet skills)
        {
            var verdict = Evaluate(perk, skills);
            if (verdict == PerkAvailability.Available) _taken.Add(perk);
            return verdict;
        }

        public void CollectModifiers(List<StatModifier> into)
        {
            if (into == null) return;
            for (int i = 0; i < _taken.Count; i++)
            {
                var mods = _taken[i].Modifiers;
                if (mods == null) continue;
                for (int j = 0; j < mods.Count; j++) into.Add(mods[j]);
            }
        }
    }
}
