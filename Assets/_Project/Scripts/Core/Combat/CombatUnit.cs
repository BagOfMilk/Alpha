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

    /// <summary>Життєвий стан юніта в бою: даун + вікно на порятунок.</summary>
    public enum UnitLifeState
    {
        Active = 0,
        Downed = 1,     // 0 HP, тікає вікно на стабілізацію (тільки юніти з CanBeDowned)
        Stabilized = 2, // врятований, вибув із бою живим (поранення застосується на базі — RosterAdapter.Wound, Р5)
        Dead = 3        // смерть насовсім
    }

    /// <summary>
    /// Бойовий профіль юніта — знімок чисел на момент входу в бій. Для напарника
    /// будується з УСІХ релевантних похідних статів через єдиний агрегатор
    /// (StatResolver/StatSnapshot, аудит G18: 11 бойових похідних нарешті
    /// читаються), для ворога — з EnemyDefinition. Далі бій працює тільки з
    /// профілем — вороги і напарники симетричні.
    /// </summary>
    public sealed class UnitProfile
    {
        public string DisplayName;
        public int MaxHp;
        public int MaxAp;
        public int Accuracy;      // база (для напарника вже включає бонус скіла зброї)
        public int Defense;
        public int Initiative;
        public int CritChance;
        public int Armor;
        public int Resolve;       // з DerivedStat.StatusDurationReduction — скорочує тривалість станів
        public int DamageBonus;   // з DerivedStat.DamageBonus — плюс до урону зброї (канал гіру/перків)
        public int MoveApPerTile; // з DerivedStat.MoveApPerTile — ціна кроку руху ЦЬОГО юніта
        public int MedicineSkill; // для стабілізації дауну
        public bool CanBeDowned;  // напарники — так; рядові вороги вмирають одразу

        /// <summary>
        /// Сюжетний захист протагоніста: вікно дауну вийшло → втрачає свідомість і
        /// вибуває живим (Stabilized), а не гине. Параметр приходить ЗЗОВНІ
        /// (FromCompanion), тому що «це протагоніст» і «увімкнений айронмен»
        /// живуть у GameSession/NewGameOptions — пакетах, яких у цьому робочому
        /// дереві ще нема (D1). Combat не зав'язаний на їхнє API — тільки на bool.
        /// </summary>
        public bool ProtectedFromDeath;

        public ResistProfile Resists = new ResistProfile();

        /// <summary>Сімейство (для ворогів): роботів можна зламати і переманити.</summary>
        public EnemyFamily Family = EnemyFamily.Human;

        /// <summary>Роль — біас поведінки ШІ: танк лізе в клінч, застрільщик тримає оптимал.</summary>
        public EnemyRole Role = EnemyRole.Skirmisher;
    }

    /// <summary>
    /// Юніт у бою: профіль + рантайм-стан (HP/AP/позиція/Strike-метр/шред
    /// броні/статуси/вікно дауну). Правила (переходи, урон, тики) застосовує
    /// CombatState — юніт тільки зберігає і віддає дані.
    /// </summary>
    public sealed class CombatUnit
    {
        public string Id { get; }

        /// <summary>Сторона. Змінюється тільки зламом робота — через CombatState.</summary>
        public Side Side { get; internal set; }

        public UnitProfile Profile { get; }
        public WeaponDefinition Weapon { get; }

        /// <summary>Id напарника-джерела (null для рядових ворогів) — для наслідків на базі (D1/RosterAdapter).</summary>
        public string SourceCompanionId { get; }

        public GridPos Pos { get; internal set; }
        public int Hp { get; internal set; }
        public int Ap { get; internal set; }
        public int StrikeMeter { get; internal set; }
        public int ArmorShred { get; internal set; }
        public UnitLifeState LifeState { get; internal set; } = UnitLifeState.Active;
        public int DownWindowRemaining { get; internal set; }

        /// <summary>
        /// Зведений overwatch; null — юніт не в дозорі. Ставить і знімає
        /// тільки CombatState: вхід — дією Overwatch, зняття — пострілом,
        /// початком свого ходу, оглушенням, збиттям з ніг, перестановкою, дауном.
        /// </summary>
        public OverwatchStance Overwatch { get; internal set; }

        public bool IsOverwatching => Overwatch != null;

        public readonly List<StatusInstance> Statuses = new List<StatusInstance>();

        /// <summary>Відомі здібності (напарник — за порогами скілів; ворог — з визначення).</summary>
        public readonly List<AbilityDefinition> Abilities = new List<AbilityDefinition>();

        private readonly Dictionary<string, int> _cooldowns = new Dictionary<string, int>();

        public int CooldownRemaining(string abilityId)
            => abilityId != null && _cooldowns.TryGetValue(abilityId, out var v) ? v : 0;

        internal void SetCooldown(string abilityId, int turns)
        {
            if (!string.IsNullOrEmpty(abilityId) && turns > 0) _cooldowns[abilityId] = turns;
        }

        /// <summary>Тик кулдаунів на початку СВОГО ходу.</summary>
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
            Ap = 0; // видається на початку ходу
        }

        public bool IsActive => LifeState == UnitLifeState.Active;

        /// <summary>Ефективна броня з урахуванням накопиченого Шреду (не нижче нуля).</summary>
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

        /// <summary>Знімок похідних + бонус скіла зброї → бойовий профіль. Спільна частина FromCompanion/FromDefector.</summary>
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
        /// Напарник → бойовий юніт: похідні через єдиний агрегатор + бонус скіла
        /// зброї до точності (аудит G18 — усі релевантні похідні читаються,
        /// не тільки видимі в UI). Здібності набираються з каталогу за порогами
        /// скілів. protectedFromDeath — див. UnitProfile.ProtectedFromDeath.
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
        /// Перебіжчик → ворог-бос з УСІМ своїм рівнем і спорядженням (бойовий
        /// профіль будується тим самим агрегатором, що й у напарника — симетрія
        /// правил). Помирає насовсім (CanBeDowned=false): гір повертається вбивством.
        /// weapon передається параметром — Combat не читає Companion.Equipment
        /// (Items/Equipment — пакет Б3, у цьому робочому дереві ще не існує);
        /// D1 передає актуально надіту зброю дефектора, коли Б3 влиється.
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

        /// <summary>Ворог → бойовий юніт із визначення (роль × сімейство × профіль × зброя).</summary>
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
