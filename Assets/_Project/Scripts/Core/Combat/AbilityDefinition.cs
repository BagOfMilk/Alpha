using System;
using System.Collections.Generic;
using Game.Core.Stats;

namespace Game.Core.Combat
{
    /// <summary>Кого/что таргетит способность.</summary>
    public enum AbilityTarget
    {
        Self = 0,
        Ally = 1,       // союзник (не сам)
        Enemy = 2,
        Tile = 3,       // точка на карте (ловушка и т.п.)
        AllyOrSelf = 4
    }

    /// <summary>
    /// Примитив эффекта — способности собираются композицией примитивов (обычный
    /// контент без нового кода). Половина набора — про состояния, половина —
    /// про позицию и AP.
    /// </summary>
    public enum AbilityEffectKind
    {
        WeaponAttack = 0,     // удар текущим оружием (полный конвейер; Amount не используется)
        FlatDamage = 1,       // фикс урон типом Damage (множитель типа + броня)
        ApplyStatus = 2,      // наложить Status (гарантированно)
        RemoveStatus = 3,     // снять Status, если есть
        Shred = 4,            // −броня цели на Amount (копится)
        Heal = 5,             // +HP до максимума
        GrantAp = 6,          // +AP цели (размен экономии действий)
        LungeToTarget = 7,    // рывок: встать вплотную к цели-врагу (свободная клетка)
        RepositionTarget = 8, // переставить цель-союзника в targetTile (≤ Amount клеток от него)
        PlaceTrap = 9,        // ловушка в targetTile: Amount урона типом Damage + Status
        HackRobot = 10        // переманить вражеского робота на свою сторону
    }

    [Serializable]
    public sealed class AbilityEffect
    {
        public AbilityEffectKind Kind;
        public int Amount;                          // урон/хил/AP/шред/дальность перестановки
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
    /// Активная способность: немного, но каждая отчётлива. Гейтится уровнем
    /// скила, стоит AP, имеет КД в своих ходах. Чистые данные — в Unity
    /// обернётся ScriptableObject; враги берут способности из того же общего
    /// пула (симметрия).
    /// </summary>
    [Serializable]
    public sealed class AbilityDefinition
    {
        public string Id;
        public string DisplayName;

        /// <summary>Скил-гейт: напарник знает способность при уровне скила ≥ порога.</summary>
        public SkillType Skill = SkillType.None;
        public int RequiredSkillLevel = 1;

        public int ApCost = 3;
        public int CooldownTurns = 2;   // снова доступна через N своих ходов
        public int Range = 1;           // дальность до цели/тайла (Чебышёв); 0 = только на себя
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

        // ---- Флюент-хелперы для авторинга контента ----
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

        /// <summary>Сколько ОТДЕЛЬНЫХ роллов атаки делает способность (например, «Черга» — два выстрела).</summary>
        public int WeaponAttackCount()
        {
            int n = 0;
            for (int i = 0; i < Effects.Count; i++)
                if (Effects[i] != null && Effects[i].Kind == AbilityEffectKind.WeaponAttack) n++;
            return n;
        }

        /// <summary>
        /// Бонус точности ОДНОГО ролла — для честного превью. Именно так его
        /// берёт CombatState.UseAbility: каждый эффект WeaponAttack катится со
        /// СВОИМ бонусом, поэтому суммировать бонусы залпа нельзя — показанный
        /// процент разошёлся бы с роллом.
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
