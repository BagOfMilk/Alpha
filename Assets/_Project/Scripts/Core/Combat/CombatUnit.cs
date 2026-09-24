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

    /// <summary>Жизненное состояние юнита в бою: даун + окно на спасение.</summary>
    public enum UnitLifeState
    {
        Active = 0,
        Downed = 1,     // 0 HP, тикает окно на стабилизацию (только юниты с CanBeDowned)
        Stabilized = 2, // спасён, выбыл из боя живым (ранение применится на базе — RosterAdapter.Wound, Р5)
        Dead = 3        // смерть насовсем
    }

    /// <summary>
    /// Боевой профиль юнита — снимок чисел на момент входа в бой. Для напарника
    /// строится из ВСЕХ релевантных производных статов через единый агрегатор
    /// (StatResolver/StatSnapshot, аудит G18: 11 боевых производных наконец
    /// читаются), для врага — из EnemyDefinition. Дальше бой работает только с
    /// профилем — враги и напарники симметричны.
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
        public int Resolve;       // из DerivedStat.StatusDurationReduction — сокращает длительность состояний
        public int DamageBonus;   // из DerivedStat.DamageBonus — плюс к урону оружия (канал гира/перков)
        public int MoveApPerTile; // из DerivedStat.MoveApPerTile — цена шага движения ЭТОГО юнита
        public int MedicineSkill; // для стабилизации дауна
        public bool CanBeDowned;  // напарники — да; рядовые враги умирают сразу

        /// <summary>
        /// Сюжетная защита протагониста: окно дауна вышло → теряет сознание и
        /// выбывает живым (Stabilized), а не погибает. Параметр приходит СНАРУЖИ
        /// (FromCompanion), потому что «это протагонист» и «включён айронмен»
        /// живут в GameSession/NewGameOptions — пакетах, которых в этом рабочем
        /// дереве ещё нет (D1). Combat не завязан на их API — только на bool.
        /// </summary>
        public bool ProtectedFromDeath;

        public ResistProfile Resists = new ResistProfile();

        /// <summary>Семейство (для врагов): роботов можно взломать и переманить.</summary>
        public EnemyFamily Family = EnemyFamily.Human;

        /// <summary>Роль — биас поведения ИИ: танк лезет в клинч, застрельщик держит оптимал.</summary>
        public EnemyRole Role = EnemyRole.Skirmisher;
    }

    /// <summary>
    /// Юнит в бою: профиль + рантайм-состояние (HP/AP/позиция/Strike-метр/шред
    /// брони/статусы/окно дауна). Правила (переходы, урон, тики) применяет
    /// CombatState — юнит только хранит и отдаёт данные.
    /// </summary>
    public sealed class CombatUnit
    {
        public string Id { get; }

        /// <summary>Сторона. Меняется только взломом робота — через CombatState.</summary>
        public Side Side { get; internal set; }

        public UnitProfile Profile { get; }
        public WeaponDefinition Weapon { get; }

        /// <summary>Id напарника-источника (null для рядовых врагов) — для последствий на базе (D1/RosterAdapter).</summary>
        public string SourceCompanionId { get; }

        public GridPos Pos { get; internal set; }
        public int Hp { get; internal set; }
        public int Ap { get; internal set; }
        public int StrikeMeter { get; internal set; }
        public int ArmorShred { get; internal set; }
        public UnitLifeState LifeState { get; internal set; } = UnitLifeState.Active;
        public int DownWindowRemaining { get; internal set; }

        /// <summary>
        /// Взведённый overwatch; null — юнит не в дозоре. Ставит и снимает
        /// только CombatState: вход — действием Overwatch, снятие — выстрелом,
        /// началом своего хода, оглушением, сбиванием с ног, перестановкой, дауном.
        /// </summary>
        public OverwatchStance Overwatch { get; internal set; }

        public bool IsOverwatching => Overwatch != null;

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

        /// <summary>Снимок производных + бонус скила оружия → боевой профиль. Общая часть FromCompanion/FromDefector.</summary>
        private static UnitProfile ProfileFromCompanion(Companion c, WeaponDefinition weapon, BalanceConfig cfg,
                                                         out int weaponSkill)
        {
            var snap = c.Resolve(cfg);
            weaponSkill = weapon != null ? snap.Skill(weapon.Skill) : 0;
            return new UnitProfile
            {
                DisplayName = c.DisplayName,
                MaxHp = snap.GetInt(StatKeys.Of(DerivedStat.MaxHp)),
                MaxAp = snap.GetInt(StatKeys.Of(DerivedStat.MaxAp)),
                Accuracy = snap.GetInt(StatKeys.Of(DerivedStat.Accuracy)) + weaponSkill * cfg.Combat.AccuracyPerWeaponSkill,
                Defense = snap.GetInt(StatKeys.Of(DerivedStat.Defense)),
                Initiative = snap.GetInt(StatKeys.Of(DerivedStat.Initiative)),
                CritChance = snap.GetInt(StatKeys.Of(DerivedStat.CritChance)),
                Armor = snap.GetInt(StatKeys.Of(DerivedStat.Armor)),
                Resolve = snap.GetInt(StatKeys.Of(DerivedStat.StatusDurationReduction)),
                DamageBonus = snap.GetInt(StatKeys.Of(DerivedStat.DamageBonus)),
                MoveApPerTile = Math.Max(1, snap.GetInt(StatKeys.Of(DerivedStat.MoveApPerTile))),
                MedicineSkill = snap.Skill(SkillType.Medicine),
            };
        }

        /// <summary>
        /// Напарник → боевой юнит: производные через единый агрегатор + бонус скила
        /// оружия к точности (аудит G18 — все релевантные производные читаются,
        /// не только видимые в UI). Способности набираются из каталога по порогам
        /// скилов. protectedFromDeath — см. UnitProfile.ProtectedFromDeath.
        /// </summary>
        public static CombatUnit FromCompanion(Companion c, WeaponDefinition weapon, BalanceConfig cfg,
                                               bool protectedFromDeath = false,
                                               IEnumerable<AbilityDefinition> abilityCatalog = null)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            cfg = cfg ?? new BalanceConfig();

            var profile = ProfileFromCompanion(c, weapon, cfg, out _);
            profile.CanBeDowned = true;
            profile.ProtectedFromDeath = protectedFromDeath;

            var unit = new CombatUnit("u_" + c.Id, Side.Player, profile, weapon, c.Id);
            AddGatedAbilities(unit, c, abilityCatalog);
            return unit;
        }

        /// <summary>
        /// Перебежчик → враг-босс со ВСЕМ своим уровнем и снаряжением (боевой
        /// профиль строится тем же агрегатором, что у напарника — симметрия
        /// правил). Умирает насовсем (CanBeDowned=false): гир возвращается убийством.
        /// weapon передаётся параметром — Combat не читает Companion.Equipment
        /// (Items/Equipment — пакет Б3, в этом рабочем дереве ещё не существует);
        /// D1 передаёт актуально надетое оружие дефектора, когда Б3 вольётся.
        /// </summary>
        public static CombatUnit FromDefector(Companion c, WeaponDefinition weapon, BalanceConfig cfg,
                                              IEnumerable<AbilityDefinition> abilityCatalog = null)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            cfg = cfg ?? new BalanceConfig();

            var profile = ProfileFromCompanion(c, weapon, cfg, out _);
            profile.CanBeDowned = false;

            var unit = new CombatUnit("defector_" + c.Id, Side.Enemy, profile, weapon, c.Id);
            AddGatedAbilities(unit, c, abilityCatalog);
            return unit;
        }

        private static void AddGatedAbilities(CombatUnit unit, Companion c, IEnumerable<AbilityDefinition> abilityCatalog)
        {
            if (abilityCatalog == null) return;
            foreach (var ability in abilityCatalog)
                if (ability != null && ability.Skill != SkillType.None
                    && c.Skill(ability.Skill) >= ability.RequiredSkillLevel)
                    unit.Abilities.Add(ability);
        }

        /// <summary>Враг → боевой юнит из определения (роль × семейство × профиль × оружие).</summary>
        public static CombatUnit FromEnemy(EnemyDefinition def, string instanceId)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
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
                Resists = def.Resists ?? new ResistProfile(),
                Family = def.Family,
                Role = def.Role
            };
            var unit = new CombatUnit(instanceId, Side.Enemy, profile, def.Weapon);
            if (def.Abilities != null)
                foreach (var ability in def.Abilities)
                    if (ability != null) unit.Abilities.Add(ability);
            return unit;
        }
    }
}
