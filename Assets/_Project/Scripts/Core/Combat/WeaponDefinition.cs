using System;
using Game.Core.Stats;

namespace Game.Core.Combat
{
    /// <summary>
    /// Зброя: чисті дані (у Unity обгорнеться ScriptableObject/ItemDefinition,
    /// коли підключиться Items — Б3). Гір крутить ЧИСЛА; здібності живуть у скілах.
    /// Урон дрібний (2–8). Проки статусів на влучанні — гарантовані (граза
    /// проку не дає).
    /// </summary>
    [Serializable]
    public sealed class WeaponDefinition
    {
        public string Id;
        public string DisplayName;

        /// <summary>Яким бойовим скілом стріляє/б'є (Стрілецьке/Ближній бій).</summary>
        public SkillType Skill = SkillType.Ranged;

        public DamageType Damage = DamageType.Ballistic;
        public int DamageMin = 2;
        public int DamageMax = 5;
        public int CritDamageBonus = 2;  // крит = DamageMax + бонус

        public int ApCost = 4;           // атака 3–4 AP
        public int OptimalRange = 6;     // тайли (Чебишов); далі — штраф за дистанцію
        public int ArmorPierce = 0;      // пробиття: ігнорує N броні

        /// <summary>Шред: на влучанні знижує броню цілі на N (накопичується).</summary>
        public int ShredOnHit = 0;

        /// <summary>Статус, який гарантовано накладається на повному влучанні (None — нема проку).</summary>
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
