using System;
using Game.Core.Stats;

namespace Game.Core.Combat
{
    /// <summary>
    /// Оружие: чистые данные (в Unity обернётся ScriptableObject / ItemDefinition,
    /// Эпик 6). Гир крутит ЧИСЛА; способности живут в скилах. Урон мелкий (2–8).
    /// Проки статусов на попадании — гарантированные (граза прок не даёт).
    /// </summary>
    [Serializable]
    public sealed class WeaponDefinition
    {
        public string Id;
        public string DisplayName;

        /// <summary>Каким боевым скилом стреляет/бьёт (Стрелковое/Ближнее).</summary>
        public SkillType Skill = SkillType.Ranged;

        public DamageType Damage = DamageType.Ballistic;
        public int DamageMin = 2;
        public int DamageMax = 5;
        public int CritDamageBonus = 2;  // крит = DamageMax + бонус

        public int ApCost = 4;           // атака 3–4 AP (Прил. Б)
        public int OptimalRange = 6;     // тайлы (Чебышёв); дальше — штраф за дистанцию
        public int ArmorPierce = 0;      // пробитие: игнорирует N брони

        /// <summary>Шред: на попадании снижает броню цели на N (копится).</summary>
        public int ShredOnHit = 0;

        /// <summary>Статус, гарантированно накладываемый на полном попадании (None — нет прока).</summary>
        public StatusType StatusOnHit = StatusType.None;

        public bool IsMelee => Skill == SkillType.Melee;

        public WeaponDefinition() { }

        public WeaponDefinition(string id, string displayName, SkillType skill)
        {
            Id = id;
            DisplayName = displayName;
            Skill = skill;
            if (skill == SkillType.Melee) OptimalRange = 1;
        }
    }
}
