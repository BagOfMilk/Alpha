using System;
using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Stats;

namespace Game.Core.Combat
{
    public enum Side
    {
        Player = 0,
        Enemy = 1
    }

    /// <summary>Жизненное состояние юнита в бою (Эпик 4: даун + окно на спасение).</summary>
    public enum UnitLifeState
    {
        Active = 0,
        Downed = 1,     // 0 HP, тикает окно на стабилизацию (только юниты с CanBeDowned)
        Stabilized = 2, // спасён, выбыл из боя живым (ранение применится на базе)
        Dead = 3        // смерть насовсем
    }

    /// <summary>
    /// Боевой профиль юнита — снимок чисел на момент входа в бой. Для напарника
    /// строится из производных статов (атрибуты + трейты/шрамы через агрегатор),
    /// для врага — из EnemyDefinition. Дальше бой работает только с профилем —
    /// враги и напарники симметричны.
    /// </summary>
    public sealed class UnitProfile
    {
        public string DisplayName;
        public int MaxHp;
        public int MaxAp;
        public int Accuracy;      // база (для напарника уже включает бонус скила оружия)
        public int Defense;
        public int Initiative;
        public int CritChance;
        public int Armor;
        public int Resolve;       // сокращает длительность состояний
        public int MedicineSkill; // для стабилизации дауна
        public bool CanBeDowned;  // напарники — да; рядовые враги умирают сразу

        /// <summary>
        /// Сюжетная защита протагониста (US-4.4): окно дауна вышло → теряет сознание
        /// и выбывает живым (Stabilized), а не погибает. В айронмене выключена.
        /// </summary>
        public bool ProtectedFromDeath;

        public ResistProfile Resists = new ResistProfile();
    }

    /// <summary>
    /// Юнит в бою: профиль + рантайм-состояние (HP/AP/позиция/Strike-метр/шред
    /// брони/статусы/окно дауна). Правила (переходы, урон, тики) применяет
    /// CombatState — юнит только хранит и отдаёт данные.
    /// </summary>
    public sealed class CombatUnit
    {
        public string Id { get; }
        public Side Side { get; }
        public UnitProfile Profile { get; }
        public WeaponDefinition Weapon { get; }

        /// <summary>Id напарника-источника (null для врагов) — для применения последствий на базе.</summary>
        public string SourceCompanionId { get; }

        public GridPos Pos { get; internal set; }
        public int Hp { get; internal set; }
        public int Ap { get; internal set; }
        public int StrikeMeter { get; internal set; }
        public int ArmorShred { get; internal set; }
        public UnitLifeState LifeState { get; internal set; } = UnitLifeState.Active;
        public int DownWindowRemaining { get; internal set; }

        public readonly List<StatusInstance> Statuses = new List<StatusInstance>();

        /// <summary>Известные способности (напарник — по порогам скилов; враг — из определения).</summary>
        public readonly List<AbilityDefinition> Abilities = new List<AbilityDefinition>();

        private readonly Dictionary<string, int> _cooldowns = new Dictionary<string, int>();

        public int CooldownRemaining(string abilityId)
            => abilityId != null && _cooldowns.TryGetValue(abilityId, out var v) ? v : 0;

        internal void SetCooldown(string abilityId, int turns)
        {
            if (!string.IsNullOrEmpty(abilityId) && turns > 0) _cooldowns[abilityId] = turns;
        }

        /// <summary>Тик кулдаунов в начале СВОЕГО хода.</summary>
        internal void TickCooldowns()
        {
            if (_cooldowns.Count == 0) return;
            var keys = new List<string>(_cooldowns.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                int v = _cooldowns[keys[i]] - 1;
                if (v <= 0) _cooldowns.Remove(keys[i]);
                else _cooldowns[keys[i]] = v;
            }
        }

        public AbilityDefinition FindAbility(string abilityId)
        {
            for (int i = 0; i < Abilities.Count; i++)
                if (Abilities[i].Id == abilityId) return Abilities[i];
            return null;
        }

        public CombatUnit(string id, Side side, UnitProfile profile, WeaponDefinition weapon,
                          string sourceCompanionId = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Side = side;
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            Weapon = weapon;
            SourceCompanionId = sourceCompanionId;
            Hp = profile.MaxHp;
            Ap = 0; // выдаётся в начале хода
        }

        public bool IsActive => LifeState == UnitLifeState.Active;

        /// <summary>Эффективная броня с учётом накопленного Шреда (не ниже нуля).</summary>
        public int EffectiveArmor => Math.Max(0, Profile.Armor - ArmorShred);

        public bool HasStatus(StatusType type)
        {
            for (int i = 0; i < Statuses.Count; i++)
                if (Statuses[i].Type == type) return true;
            return false;
        }

        public StatusInstance GetStatus(StatusType type)
        {
            for (int i = 0; i < Statuses.Count; i++)
                if (Statuses[i].Type == type) return Statuses[i];
            return null;
        }

        // ---- Фабрики ----
        /// <summary>
        /// Напарник → боевой юнит: производные через единый агрегатор + бонус скила
        /// оружия к точности. Способности набираются из каталога по порогам скилов
        /// (US-2.2: гейт уровнем скила).
        /// </summary>
        public static CombatUnit FromCompanion(Companion c, WeaponDefinition weapon, BalanceConfig cfg,
                                               IEnumerable<AbilityDefinition> abilityCatalog = null)
        {
            var d = c.EffectiveDerived(cfg);
            int weaponSkill = weapon != null ? c.GetSkill(weapon.Skill) : 0;
            var profile = new UnitProfile
            {
                DisplayName = c.DisplayName,
                MaxHp = d[DerivedStat.MaxHp],
                MaxAp = d[DerivedStat.ActionPoints],
                Accuracy = d[DerivedStat.Accuracy] + weaponSkill * cfg.AccuracyPerWeaponSkill,
                Defense = d[DerivedStat.Defense],
                Initiative = d[DerivedStat.Initiative],
                CritChance = d[DerivedStat.CritChance],
                Armor = d[DerivedStat.Armor],
                Resolve = d[DerivedStat.Resolve],
                MedicineSkill = c.GetSkill(SkillType.Medicine),
                CanBeDowned = true,
                ProtectedFromDeath = c.IsProtagonist && !cfg.Ironman
            };
            var unit = new CombatUnit("u_" + c.Id, Side.Player, profile, weapon, c.Id);
            if (abilityCatalog != null)
            {
                foreach (var ability in abilityCatalog)
                    if (ability != null && ability.Skill != Stats.SkillType.None
                        && c.GetSkill(ability.Skill) >= ability.RequiredSkillLevel)
                        unit.Abilities.Add(ability);
            }
            return unit;
        }

        /// <summary>Враг → боевой юнит из определения (роль × семейство × профиль × оружие).</summary>
        public static CombatUnit FromEnemy(EnemyDefinition def, string instanceId)
        {
            var profile = new UnitProfile
            {
                DisplayName = def.DisplayName,
                MaxHp = def.MaxHp,
                MaxAp = def.MaxAp,
                Accuracy = def.Accuracy,
                Defense = def.Defense,
                Initiative = def.Initiative,
                CritChance = def.CritChance,
                Armor = def.Armor,
                Resolve = def.Resolve,
                MedicineSkill = 0,
                CanBeDowned = false,
                Resists = def.Resists ?? new ResistProfile()
            };
            var unit = new CombatUnit(instanceId, Side.Enemy, profile, def.Weapon);
            if (def.Abilities != null)
                foreach (var ability in def.Abilities)
                    if (ability != null) unit.Abilities.Add(ability);
            return unit;
        }
    }
}
