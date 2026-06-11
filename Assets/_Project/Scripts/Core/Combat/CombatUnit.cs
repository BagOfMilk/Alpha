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
        /// <summary>Напарник → боевой юнит: производные через единый агрегатор + бонус скила оружия к точности.</summary>
        public static CombatUnit FromCompanion(Companion c, WeaponDefinition weapon, BalanceConfig cfg)
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
            return new CombatUnit("u_" + c.Id, Side.Player, profile, weapon, c.Id);
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
            return new CombatUnit(instanceId, Side.Enemy, profile, def.Weapon);
        }
    }
}
