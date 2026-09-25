using System;
using System.Collections.Generic;
using Game.Core.Stats;

namespace Game.Core.Combat
{
    /// <summary>Кого/що таргетить здібність.</summary>
    public enum AbilityTarget
    {
        Self = 0,
        Ally = 1,       // союзник (не сам)
        Enemy = 2,
        Tile = 3,       // точка на карті (пастка тощо)
        AllyOrSelf = 4
    }

    /// <summary>
    /// Примітив ефекту — здібності збираються композицією примітивів (звичайний
    /// контент без нового коду). Половина набору — про стани, половина —
    /// про позицію і AP.
    /// </summary>
    public enum AbilityEffectKind
    {
        WeaponAttack = 0,     // удар поточною зброєю (повний конвеєр; Amount не використовується)
        FlatDamage = 1,       // фікс-урон типом Damage (множник типу + броня)
        ApplyStatus = 2,      // накласти Status (гарантовано)
        RemoveStatus = 3,     // зняти Status, якщо є
        Shred = 4,            // −броня цілі на Amount (накопичується)
        Heal = 5,             // +HP до максимуму
        GrantAp = 6,          // +AP цілі (розмін економії дій)
        LungeToTarget = 7,    // ривок: стати впритул до цілі-ворога (вільна клітина)
        RepositionTarget = 8, // переставити ціль-союзника в targetTile (≤ Amount клітин від нього)
        PlaceTrap = 9,        // пастка в targetTile: Amount урону типом Damage + Status
        HackRobot = 10        // переманити ворожого робота на свій бік
    }

    [Serializable]
    public sealed class AbilityEffect
    {
        public AbilityEffectKind Kind;
        public int Amount;                          // урон/хіл/AP/шред/дальність перестановки
        public DamageType Damage = DamageType.True; // для FlatDamage/PlaceTrap
        public StatusType Status = StatusType.None; // для Apply/RemoveStatus/PlaceTrap
        public int AccuracyBonus;                   // для WeaponAttack

        public AbilityEffect() { }

        public AbilityEffect(AbilityEffectKind kind, int amount = 0)
        {
            Kind = kind;
            Amount = amount;
        }
    }

    /// <summary>
    /// Активна здібність: небагато, але кожна відчутна. Гейтиться рівнем
    /// скіла, коштує AP, має КД у своїх ходах. Чисті дані — в Unity
    /// обернеться ScriptableObject; вороги беруть здібності з того самого
    /// спільного пулу (симетрія).
    /// </summary>
    [Serializable]
    public sealed class AbilityDefinition
    {
        public string Id;
        public string DisplayName;

        /// <summary>Скіл-гейт: напарник знає здібність при рівні скіла ≥ порога.</summary>
        public SkillType Skill = SkillType.None;
        public int RequiredSkillLevel = 1;

        public int ApCost = 3;
        public int CooldownTurns = 2;   // знову доступна через N своїх ходів
        public int Range = 1;           // дальність до цілі/тайла (Чебишов); 0 = тільки на себе
        public AbilityTarget Targeting = AbilityTarget.Enemy;
        public bool RequiresLineOfSight = true;

        public List<AbilityEffect> Effects = new List<AbilityEffect>();

        public AbilityDefinition() { }

        public AbilityDefinition(string id, string displayName, SkillType skill, int requiredLevel)
        {
            Id = id;
            DisplayName = displayName;
            Skill = skill;
            RequiredSkillLevel = requiredLevel;
        }

        // ---- Флюент-хелпери для авторингу контенту ----
        public AbilityDefinition Costs(int ap, int cooldown)
        {
            ApCost = ap;
            CooldownTurns = cooldown;
            return this;
        }

        public AbilityDefinition Targets(AbilityTarget targeting, int range, bool needsLos = true)
        {
            Targeting = targeting;
            Range = range;
            RequiresLineOfSight = needsLos;
            return this;
        }

        public AbilityDefinition WithEffect(AbilityEffect effect)
        {
            Effects.Add(effect);
            return this;
        }

        /// <summary>Скільки ОКРЕМИХ роллів атаки робить здібність (наприклад, «Черга» — два постріли).</summary>
        public int WeaponAttackCount()
        {
            int n = 0;
            for (int i = 0; i < Effects.Count; i++)
                if (Effects[i] != null && Effects[i].Kind == AbilityEffectKind.WeaponAttack) n++;
            return n;
        }

        /// <summary>
        /// Бонус точності ОДНОГО ролла — для чесного превʼю. Саме так його
        /// бере CombatState.UseAbility: кожен ефект WeaponAttack котиться зі
        /// СВОЇМ бонусом, тому підсумовувати бонуси залпу не можна — показаний
        /// відсоток розійшовся б із роллом.
        /// </summary>
        public int PreviewAccuracyBonus()
        {
            for (int i = 0; i < Effects.Count; i++)
                if (Effects[i] != null && Effects[i].Kind == AbilityEffectKind.WeaponAttack)
                    return Effects[i].AccuracyBonus;
            return 0;
        }
    }
}
