using System.Collections.Generic;
using Game.Core.Stats;

namespace Game.Core.Characters.Perks
{
    /// <summary>
    /// Взяті персонажем перки.
    ///
    /// Методу «забути перк» немає — «респеку немає» (US-2.2) забезпечується
    /// відсутністю API, а не пам'яттю розробника. Незворотність вкладень —
    /// частина дизайну, тому вона вшита в тип.
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
        /// Чи доступний перк при таких скілах. Чиста перевірка без побічних
        /// ефектів — її ж кличе передперегляд білда, щоб показати пороги
        /// заздалегідь (US-2.3).
        /// </summary>
        public PerkAvailability Evaluate(PerkDefinition perk, SkillSet skills)
            => Evaluate(perk, skills, null);

        /// <summary>
        /// Те саме, але «наче вже взяті ще й ось ці».
        ///
        /// Потрібно планувальнику білда: перки в одному плані бувають пререквізитами
        /// один одного, і без цього він показував би хибну відмову. Гейти лишаються
        /// в одному місці — друга копія правил розійшлася б із першою.
        /// </summary>
        public PerkAvailability Evaluate(PerkDefinition perk, SkillSet skills, ICollection<string> alsoTaken)
        {
            if (perk == null || string.IsNullOrEmpty(perk.Id)) return PerkAvailability.Invalid;
            if (Has(perk.Id) || (alsoTaken != null && alsoTaken.Contains(perk.Id))) return PerkAvailability.AlreadyTaken;

            if (perk.GatingSkill != SkillType.None)
            {
                int level = skills == null ? 0 : skills[perk.GatingSkill];
                if (level < perk.RequiredSkillLevel) return PerkAvailability.SkillTooLow;
            }

            var prereqs = perk.PrerequisitePerkIds;
            if (prereqs != null)
                for (int i = 0; i < prereqs.Count; i++)
                    if (!Has(prereqs[i]) && (alsoTaken == null || !alsoTaken.Contains(prereqs[i])))
                        return PerkAvailability.MissingPrerequisite;

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
