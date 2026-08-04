using System.Collections.Generic;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Companions;
using Game.Core.Health;
using Game.Core.Stats;
using Game.Core.Threats;
using Game.Core.Traits;

namespace Game.Core
{
    /// <summary>
    /// Дефолтный контент в коде: бэкграунды, трейты, шрамы и позиции базы (GDD v6).
    /// Это «затравка» для прототипа — позже её заменят/дополнят ScriptableObject-
    /// ассеты, но благодаря ей проект играбелен сразу. Имена — постапок-флавор
    /// (плейсхолдеры; полная тематическая переименовка — отдельный контент-пасс).
    ///
    /// Трейты и шрамы отдаются ФАБРИКАМИ (новый инстанс на каждого), чтобы у разных
    /// напарников были независимые экземпляры.
    /// </summary>
    public static class DefaultContent
    {
        // ===== Трейты =====
        public static Trait SharpEye() => new Trait("sharp_eye", "Меткий глаз", TraitSign.Virtue)
            .WithCombat(DerivedStat.Accuracy, 5).WithValue("точность");

        public static Trait Bruiser() => new Trait("bruiser", "Громила", TraitSign.Virtue)
            .WithCheck(SkillType.Intimidation, 2).WithCombat(DerivedStat.MaxHp, 2).WithValue("сила").WithValue(DefaultValues.Ruthless);

        public static Trait HotTempered() => new Trait("hot_tempered", "Вспыльчивый", TraitSign.Vice)
            .WithCheck(SkillType.Persuasion, -1).WithValue("гнев").WithValue(DefaultValues.Freedom);

        public static Trait Handy() => new Trait("handy", "Рукастый", TraitSign.Virtue)
            .WithCheck(SkillType.Mechanics, 2).WithCheck(SkillType.Hacking, 1).WithValue("техника");

        public static Trait SteadyHands() => new Trait("steady_hands", "Спокойные руки", TraitSign.Virtue)
            .WithCheck(SkillType.Medicine, 2).WithValue("забота").WithValue(DefaultValues.Mercy);

        public static Trait SilverTongue() => new Trait("silver_tongue", "Острый язык", TraitSign.Virtue)
            .WithCheck(SkillType.Persuasion, 2).WithCheck(SkillType.Trade, 1).WithValue("слово").WithValue(DefaultValues.Mercy);

        public static Trait BornLeader() => new Trait("born_leader", "Прирождённый лидер", TraitSign.Virtue)
            .WithCheck(SkillType.Persuasion, 1).WithValue("долг").WithValue(DefaultValues.Order).WithValue(DefaultValues.Duty);

        // ===== Шрамы (метят при Серьёзном+ ранении, US-4.2) =====
        public static Scar OneEye() => new Scar("one_eye", "Одноглазый").WithCombat(DerivedStat.Accuracy, -10);
        public static Scar Limp() => new Scar("limp", "Хромой").WithCombat(DerivedStat.ActionPoints, -1);

        /// <summary>Каталоги по id (для восстановления из сейва, US-16.1).</summary>
        public static List<Trait> AllTraits() => new List<Trait>
        {
            SharpEye(), Bruiser(), HotTempered(), Handy(), SteadyHands(), SilverTongue(), BornLeader()
        };

        public static List<Scar> AllScars() => new List<Scar> { OneEye(), Limp() };

        // ===== Бэкграунды (классов нет — лишь осмысленно разный старт) =====
        public static Background Marksman() => new Background("marksman", "Стрелок")
            .WithAttributes(3, 6, 4, 3)
            .WithSkill(SkillType.Ranged, 3).WithSkill(SkillType.Survival, 1)
            .WithTrait(SharpEye());

        public static Background Brawler() => new Background("brawler", "Боец")
            .WithAttributes(6, 4, 2, 4)
            .WithSkill(SkillType.Melee, 3).WithSkill(SkillType.Intimidation, 1)
            .WithTrait(Bruiser()).WithTrait(HotTempered()); // + и − трейты сосуществуют

        public static Background Technician() => new Background("technician", "Техник")
            .WithAttributes(2, 4, 6, 3)
            .WithSkill(SkillType.Mechanics, 3).WithSkill(SkillType.Hacking, 2)
            .WithTrait(Handy());

        public static Background Medic() => new Background("medic", "Медик")
            .WithAttributes(3, 3, 5, 5)
            .WithSkill(SkillType.Medicine, 3).WithSkill(SkillType.Survival, 1)
            .WithTrait(SteadyHands());

        public static Background Negotiator() => new Background("negotiator", "Переговорщик")
            .WithAttributes(3, 3, 6, 5)
            .WithSkill(SkillType.Persuasion, 3).WithSkill(SkillType.Trade, 2)
            .WithTrait(SilverTongue());

        public static Background Leader() => new Background("leader", "Командир")
            .WithAttributes(4, 4, 5, 5)
            .WithSkill(SkillType.Tactics, 2).WithSkill(SkillType.Persuasion, 1).WithSkill(SkillType.Ranged, 1)
            .WithTrait(BornLeader());

        public static List<Background> AllBackgrounds() => new List<Background>
        {
            Marksman(), Brawler(), Technician(), Medic(), Negotiator(), Leader()
        };

        // ===== Позиции базы (ядро-здания; усиление и резолв событий — напарником) =====
        public static List<AssignmentSlotDefinition> AllSlots()
        {
            return new List<AssignmentSlotDefinition>
            {
                new AssignmentSlotDefinition("council_seat", "Место в совете", BaseSectionType.Council)
                {
                    RelevantSkill = SkillType.Persuasion, RelevantAttribute = AttributeType.Wits
                },
                new AssignmentSlotDefinition("infirmary_bed", "Койка лазарета", BaseSectionType.Infirmary)
                {
                    RelevantSkill = SkillType.Medicine
                },
                new AssignmentSlotDefinition("workshop_bench", "Верстак мастерской", BaseSectionType.Workshop)
                {
                    RelevantSkill = SkillType.Mechanics
                },
                new AssignmentSlotDefinition("storehouse_dock", "Склад", BaseSectionType.Storehouse)
                {
                    RelevantSkill = SkillType.Survival
                },
                // Рынок — специальное здание: позиция закрыта, пока не построят (см. демо).
                new AssignmentSlotDefinition("market_stall", "Прилавок рынка", BaseSectionType.Market)
                {
                    RelevantSkill = SkillType.Trade, UnlockedByDefault = false
                }
            };
        }

        // ===== Чертежи зданий (US-7.1: ядро — золото; спец — золото + строймат) =====
        /// <summary>
        /// Стройка здания с ценой из баланса (US-7.1/15.1). Ядро-здания — малая
        /// стройка за золото; спец — крупная за золото + строймат. Известные позиции
        /// открываются по достройке (Рынок → прилавок).
        /// </summary>
        public static Construction Blueprint(BaseSectionType section, Balance.BalanceConfig cfg)
        {
            bool core = section == BaseSectionType.Council || section == BaseSectionType.Infirmary
                        || section == BaseSectionType.Workshop || section == BaseSectionType.Storehouse;
            string id = "build_" + section.ToString().ToLowerInvariant();
            string name = BlueprintName(section);
            string unlocks = section == BaseSectionType.Market ? "market_stall" : null;
            var con = new Construction(id, name, section,
                core ? cfg.ConstructionSmallDays : cfg.ConstructionLargeDays, unlocks);
            return core
                ? con.Costs(cfg.CoreConstructionGold)
                : con.Costs(cfg.SpecialConstructionGold, cfg.SpecialConstructionMaterials);
        }

        private static string BlueprintName(BaseSectionType section)
        {
            switch (section)
            {
                case BaseSectionType.Council: return "Зал совета";
                case BaseSectionType.Infirmary: return "Лазарет";
                case BaseSectionType.Workshop: return "Мастерская";
                case BaseSectionType.Storehouse: return "Склад";
                case BaseSectionType.Market: return "Рынок";
                case BaseSectionType.Tavern: return "Таверна";
                case BaseSectionType.Temple: return "Храм";
                case BaseSectionType.Fortifications: return "Укрепления";
                case BaseSectionType.Armory: return "Оружейная";
                case BaseSectionType.Laboratory: return "Лаборатория";
                default: return section.ToString();
            }
        }

        // ===== Оружие (мелкий урон 2–8; в Unity станет ItemDefinition-ассетами) =====
        public static WeaponDefinition Rifle() => new WeaponDefinition("rifle", "Винтовка", SkillType.Ranged)
        {
            Damage = DamageType.Ballistic, DamageMin = 3, DamageMax = 5, CritDamageBonus = 2,
            ApCost = 4, OptimalRange = 6
        };

        public static WeaponDefinition Pistol() => new WeaponDefinition("pistol", "Пистолет", SkillType.Ranged)
        {
            Damage = DamageType.Ballistic, DamageMin = 2, DamageMax = 4, CritDamageBonus = 2,
            ApCost = 3, OptimalRange = 4
        };

        public static WeaponDefinition Machete() => new WeaponDefinition("machete", "Мачете", SkillType.Melee)
        {
            Damage = DamageType.Ballistic, DamageMin = 3, DamageMax = 6, CritDamageBonus = 3,
            ApCost = 3, ArmorPierce = 1
        };

        public static WeaponDefinition BruiserClub() => new WeaponDefinition("club", "Дубина", SkillType.Melee)
        {
            Damage = DamageType.Ballistic, DamageMin = 3, DamageMax = 6, CritDamageBonus = 2,
            ApCost = 4, ShredOnHit = 1
        };

        public static WeaponDefinition GhoulClaws() => new WeaponDefinition("claws", "Когти", SkillType.Melee)
        {
            Damage = DamageType.Ballistic, DamageMin = 2, DamageMax = 4, CritDamageBonus = 2,
            ApCost = 3, StatusOnHit = StatusType.Bleeding
        };

        public static WeaponDefinition StunGun() => new WeaponDefinition("stun_gun", "Станнер", SkillType.Ranged)
        {
            Damage = DamageType.Energy, DamageMin = 1, DamageMax = 3, CritDamageBonus = 1,
            ApCost = 3, OptimalRange = 5, StatusOnHit = StatusType.Suppressed
        };

        /// <summary>Токсин-оружие: тип урона накладывает Яд (US-3.12 — «принеси правильный инструмент»).</summary>
        public static WeaponDefinition VenomSpit() => new WeaponDefinition("venom_spit", "Ядовитый плевок", SkillType.Ranged)
        {
            Damage = DamageType.Toxin, DamageMin = 2, DamageMax = 4, CritDamageBonus = 1,
            ApCost = 3, OptimalRange = 4, StatusOnHit = StatusType.Poisoned
        };

        // ===== Бестиарий: роль × семейство × профиль × оружие (US-3.14) =====
        /// <summary>Танк: тянет фокус, резист к баллистике + броня — неси Шред/пробитие или огонь.</summary>
        public static EnemyDefinition RaiderBruiser() =>
            new EnemyDefinition("raider_bruiser", "Громила-рейдер", EnemyRole.Tank, EnemyFamily.Human)
            {
                MaxHp = 16, MaxAp = 8, Accuracy = 55, Defense = 0, Initiative = 3,
                CritChance = 5, Armor = 2, Resolve = 3,
                Resists = new ResistProfile().With(DamageType.Ballistic, 0.75).With(DamageType.Fire, 1.5),
                Weapon = BruiserClub()
            };

        /// <summary>Застрельщик: дальний ДПС из укрытия — рви линию обзора или сближайся.</summary>
        public static EnemyDefinition ScavGunner() =>
            new EnemyDefinition("scav_gunner", "Стрелок-падальщик", EnemyRole.Skirmisher, EnemyFamily.Human)
            {
                MaxHp = 9, MaxAp = 8, Accuracy = 65, Defense = 5, Initiative = 6,
                CritChance = 10, Armor = 0, Resolve = 0,
                Weapon = Rifle()
            };

        /// <summary>Контролёр: вешает Подавление — приоритетная цель; робот (резист токсина, уязвим к энергии).</summary>
        public static EnemyDefinition RustDrone() =>
            new EnemyDefinition("rust_drone", "Ржавый дрон", EnemyRole.Controller, EnemyFamily.Robot)
            {
                MaxHp = 8, MaxAp = 8, Accuracy = 70, Defense = 5, Initiative = 7,
                CritChance = 5, Armor = 1, Resolve = 9,
                Resists = new ResistProfile().With(DamageType.Toxin, 0.5).With(DamageType.Energy, 1.5),
                Weapon = StunGun()
            };

        /// <summary>Контролёр-мутант: Яд через токсин-оружие (та же роль, другое семейство/профиль = другой пазл).</summary>
        public static EnemyDefinition PlagueBearer() =>
            new EnemyDefinition("plague_bearer", "Чумоносец", EnemyRole.Controller, EnemyFamily.Mutant)
            {
                MaxHp = 10, MaxAp = 8, Accuracy = 60, Defense = 3, Initiative = 5,
                CritChance = 5, Armor = 0, Resolve = 3,
                Resists = new ResistProfile().With(DamageType.Toxin, 0.5).With(DamageType.Fire, 1.5),
                Weapon = VenomSpit()
            };

        /// <summary>Прорыв: быстрый рывок в ближний (способность из общего пула), когти с Кровотечением.</summary>
        public static EnemyDefinition FeralGhoul() =>
            new EnemyDefinition("feral_ghoul", "Дикий гул", EnemyRole.Breacher, EnemyFamily.Mutant)
            {
                MaxHp = 9, MaxAp = 10, Accuracy = 60, Defense = 5, Initiative = 8,
                CritChance = 10, Armor = 0, Resolve = 0,
                Resists = new ResistProfile().With(DamageType.Fire, 1.5),
                Weapon = GhoulClaws(),
                Abilities = new List<AbilityDefinition> { Lunge() } // симметрия: тот же пул, что у игрока
            };

        // ===== Способности (US-3.9): по 3 на боевой скил; половина — состояния, половина — позиция/AP =====

        // -- Стрелковое --
        /// <summary>Размен AP на урон: два выстрела со штрафом точности.</summary>
        public static AbilityDefinition Burst() =>
            new AbilityDefinition("burst", "Очередь", SkillType.Ranged, 1)
                .Costs(ap: 6, cooldown: 2).Targets(AbilityTarget.Enemy, range: 8)
                .WithEffect(new AbilityEffect(AbilityEffectKind.WeaponAttack) { AccuracyBonus = -10 })
                .WithEffect(new AbilityEffect(AbilityEffectKind.WeaponAttack) { AccuracyBonus = -10 });

        /// <summary>Выстрел + гарантированное Подавление (состояние).</summary>
        public static AbilityDefinition SuppressingFire() =>
            new AbilityDefinition("suppressing_fire", "Подавляющий огонь", SkillType.Ranged, 3)
                .Costs(ap: 4, cooldown: 2).Targets(AbilityTarget.Enemy, range: 8)
                .WithEffect(new AbilityEffect(AbilityEffectKind.WeaponAttack) { AccuracyBonus = -10 })
                .WithEffect(new AbilityEffect(AbilityEffectKind.ApplyStatus) { Status = StatusType.Suppressed });

        /// <summary>Метка: +шанс попадания по цели для всех (состояние).</summary>
        public static AbilityDefinition MarkTarget() =>
            new AbilityDefinition("mark_target", "Метка", SkillType.Ranged, 5)
                .Costs(ap: 2, cooldown: 1).Targets(AbilityTarget.Enemy, range: 10)
                .WithEffect(new AbilityEffect(AbilityEffectKind.ApplyStatus) { Status = StatusType.Marked });

        // -- Ближнее --
        /// <summary>Рывок к цели + удар (позиция: ломает дистанцию).</summary>
        public static AbilityDefinition Lunge() =>
            new AbilityDefinition("lunge", "Рывок", SkillType.Melee, 1)
                .Costs(ap: 4, cooldown: 2).Targets(AbilityTarget.Enemy, range: 4, needsLos: false)
                .WithEffect(new AbilityEffect(AbilityEffectKind.LungeToTarget))
                .WithEffect(new AbilityEffect(AbilityEffectKind.WeaponAttack));

        /// <summary>Удар + Сбит с ног (состояние: −защита, встать стоит AP).</summary>
        public static AbilityDefinition TripStrike() =>
            new AbilityDefinition("trip_strike", "Подсечка", SkillType.Melee, 3)
                .Costs(ap: 4, cooldown: 2).Targets(AbilityTarget.Enemy, range: 1, needsLos: false)
                .WithEffect(new AbilityEffect(AbilityEffectKind.WeaponAttack))
                .WithEffect(new AbilityEffect(AbilityEffectKind.ApplyStatus) { Status = StatusType.KnockedDown });

        /// <summary>Удар + Кровотечение (состояние, True-DoT мимо брони).</summary>
        public static AbilityDefinition Rend() =>
            new AbilityDefinition("rend", "Вспороть", SkillType.Melee, 5)
                .Costs(ap: 3, cooldown: 2).Targets(AbilityTarget.Enemy, range: 1, needsLos: false)
                .WithEffect(new AbilityEffect(AbilityEffectKind.WeaponAttack))
                .WithEffect(new AbilityEffect(AbilityEffectKind.ApplyStatus) { Status = StatusType.Bleeding });

        // -- Тактика --
        /// <summary>Размен экономии действий: +AP союзнику.</summary>
        public static AbilityDefinition Rally() =>
            new AbilityDefinition("rally", "Перегруппировка", SkillType.Tactics, 1)
                .Costs(ap: 3, cooldown: 3).Targets(AbilityTarget.Ally, range: 6, needsLos: false)
                .WithEffect(new AbilityEffect(AbilityEffectKind.GrantAp, 4));

        /// <summary>Переставить союзника до 3 клеток (позиция: бесплатное движение).</summary>
        public static AbilityDefinition MoveOrder() =>
            new AbilityDefinition("move_order", "Командный рывок", SkillType.Tactics, 3)
                .Costs(ap: 2, cooldown: 2).Targets(AbilityTarget.Ally, range: 6, needsLos: false)
                .WithEffect(new AbilityEffect(AbilityEffectKind.RepositionTarget, 3));

        /// <summary>Снять с союзника Подавление/Метку/Сбит-с-ног (контр-состояния).</summary>
        public static AbilityDefinition SnapOut() =>
            new AbilityDefinition("snap_out", "Очнись!", SkillType.Tactics, 5)
                .Costs(ap: 2, cooldown: 2).Targets(AbilityTarget.Ally, range: 6, needsLos: false)
                .WithEffect(new AbilityEffect(AbilityEffectKind.RemoveStatus) { Status = StatusType.Suppressed })
                .WithEffect(new AbilityEffect(AbilityEffectKind.RemoveStatus) { Status = StatusType.Marked })
                .WithEffect(new AbilityEffect(AbilityEffectKind.RemoveStatus) { Status = StatusType.KnockedDown });

        // -- Утилита (US-3.11) --
        /// <summary>Медицина: полевой хил вплотную (стабилизация дауна — отдельное действие).</summary>
        public static AbilityDefinition FieldDressing() =>
            new AbilityDefinition("field_dressing", "Перевязка", SkillType.Medicine, 1)
                .Costs(ap: 4, cooldown: 2).Targets(AbilityTarget.AllyOrSelf, range: 1, needsLos: false)
                .WithEffect(new AbilityEffect(AbilityEffectKind.Heal, 4));

        /// <summary>Механика: гаджет — энергоурон + Шред брони.</summary>
        public static AbilityDefinition ShockCharge() =>
            new AbilityDefinition("shock_charge", "Шоковый разряд", SkillType.Mechanics, 1)
                .Costs(ap: 3, cooldown: 2).Targets(AbilityTarget.Enemy, range: 5)
                .WithEffect(new AbilityEffect(AbilityEffectKind.FlatDamage, 3) { Damage = DamageType.Energy })
                .WithEffect(new AbilityEffect(AbilityEffectKind.Shred, 1));

        /// <summary>Механика 3: концентрированный разряд — Оглушение (цель теряет ход, US-3.7).</summary>
        public static AbilityDefinition Concussion() =>
            new AbilityDefinition("concussion", "Оглушающий разряд", SkillType.Mechanics, 3)
                .Costs(ap: 4, cooldown: 3).Targets(AbilityTarget.Enemy, range: 4)
                .WithEffect(new AbilityEffect(AbilityEffectKind.FlatDamage, 2) { Damage = DamageType.Energy })
                .WithEffect(new AbilityEffect(AbilityEffectKind.ApplyStatus) { Status = StatusType.Stunned });

        /// <summary>
        /// Взлом 2: перехват управления — переманить вражеского робота (US-3.11/3.14).
        /// Порог 2 (а не 3): штатный техник ростера имеет ровно Взлом 2, иначе фирменная
        /// механика не игралась бы ни разу за кампанию без узкого билда протагониста.
        /// </summary>
        public static AbilityDefinition HackDrone() =>
            new AbilityDefinition("hack_drone", "Перехват управления", SkillType.Hacking, 2)
                .Costs(ap: 5, cooldown: 10) // фактически раз за бой
                .Targets(AbilityTarget.Enemy, range: 4)
                .WithEffect(new AbilityEffect(AbilityEffectKind.HackRobot));

        /// <summary>Выживание: ловушка на тайле — срабатывает на вошедшем враге.</summary>
        public static AbilityDefinition SetTrap() =>
            new AbilityDefinition("set_trap", "Ловушка", SkillType.Survival, 1)
                .Costs(ap: 3, cooldown: 3).Targets(AbilityTarget.Tile, range: 3)
                .WithEffect(new AbilityEffect(AbilityEffectKind.PlaceTrap, 3) { Status = StatusType.Bleeding });

        /// <summary>Общий пул способностей (игрок — по порогам скилов; враги — по ссылкам в определении).</summary>
        public static List<AbilityDefinition> AbilityCatalog() => new List<AbilityDefinition>
        {
            Burst(), SuppressingFire(), MarkTarget(),
            Lunge(), TripStrike(), Rend(),
            Rally(), MoveOrder(), SnapOut(),
            FieldDressing(), ShockCharge(), Concussion(), HackDrone(), SetTrap()
        };

        // ===== Инциденты «Напряжения» (US-11.3; конкретика — продакшен-наполнение) =====
        public static IncidentDefinition WarehouseTheft() =>
            new IncidentDefinition("warehouse_theft", "Кража со склада", IncidentSeverity.Minor)
            { Skill = SkillType.Survival, Threshold = 2, TensionOnSuccess = -3, TensionOnFailure = 4, Weight = 3, XpOnSuccess = 15 };

        public static IncidentDefinition MarketSquabble() =>
            new IncidentDefinition("market_squabble", "Свара на рынке", IncidentSeverity.Minor)
            { Skill = SkillType.Persuasion, Threshold = 2, TensionOnSuccess = -3, TensionOnFailure = 4, Weight = 3, XpOnSuccess = 15 };

        public static IncidentDefinition ProtectionRacket() =>
            new IncidentDefinition("protection_racket", "Рэкет лавочников", IncidentSeverity.Organized)
            { Skill = SkillType.Persuasion, Threshold = 4, TensionOnSuccess = -6, TensionOnFailure = 8, Weight = 2, XpOnSuccess = 25 };

        public static IncidentDefinition WorkshopSabotage() =>
            new IncidentDefinition("workshop_sabotage", "Саботаж в мастерской", IncidentSeverity.Organized)
            { Skill = SkillType.Mechanics, Threshold = 4, TensionOnSuccess = -5, TensionOnFailure = 8, Weight = 2, XpOnSuccess = 25 };

        /// <summary>Кризис: непредотвратимый отток населения; проверка лишь смягчает дельту.</summary>
        public static IncidentDefinition NightPogrom() =>
            new IncidentDefinition("night_pogrom", "Ночной погром", IncidentSeverity.Crisis)
            {
                Skill = SkillType.Persuasion, Threshold = 6, TensionOnSuccess = -8, TensionOnFailure = 12,
                Crisis = CrisisEffect.PopulationExodus, Weight = 1
            };

        /// <summary>Авторский всплеск порога 75: гибель напарника (не протагониста), US-11.1.</summary>
        public static IncidentDefinition InsiderStrike() =>
            new IncidentDefinition("insider_strike", "Удар по своим", IncidentSeverity.Crisis)
            {
                Skill = SkillType.Survival, Threshold = 6, TensionOnSuccess = -6, TensionOnFailure = 10,
                Crisis = CrisisEffect.KillCompanion, Weight = 1
            };

        public static List<IncidentDefinition> IncidentPool() => new List<IncidentDefinition>
        {
            WarehouseTheft(), MarketSquabble(), ProtectionRacket(), WorkshopSabotage(), NightPogrom()
        };

        public static List<ThresholdSpike> TensionSpikes() => new List<ThresholdSpike>
        {
            new ThresholdSpike(75, InsiderStrike())
        };

        // ===== Фоновая телеграфия (US-11.2/17.2): «температура» читается без чисел =====
        public static string[] AmbientSignals(TensionBand band)
        {
            switch (band)
            {
                case TensionBand.Critical:
                    return new[]
                    {
                        "Ночью где-то горело; на улицах пахнет дымом.",
                        "У ворот толпа: одни требуют впустить, другие — выпустить."
                    };
                case TensionBand.Tense:
                    return new[]
                    {
                        "На перекрёстках выросли наспех сколоченные баррикады.",
                        "Люди ходят группами — по одному никто не рискует."
                    };
                case TensionBand.Uneasy:
                    return new[]
                    {
                        "Торговцы запирают лавки задолго до заката.",
                        "У колодца шепчутся: ночью опять кого-то обчистили."
                    };
                default:
                    return new[]
                    {
                        "Рынок гудит, дети носятся между прилавками.",
                        "Стражник у ворот лениво зевает на солнце."
                    };
            }
        }

        // ===== Перки (US-3.10): пассивные бонусы по порогам скилов, билд через цифры =====
        public static List<PerkDefinition> PerkCatalog() => new List<PerkDefinition>
        {
            new PerkDefinition("steady_hand", "Твёрдая рука", SkillType.Ranged, 2).With(DerivedStat.Accuracy, 5),
            new PerkDefinition("cold_blood", "Хладнокровие", SkillType.Ranged, 4).With(DerivedStat.CritChance, 5),
            // Пререквизит-цепочка (US-2.2): вершина стрелковой ветки требует «Хладнокровие».
            new PerkDefinition("dead_eye", "Мёртвый глаз", SkillType.Ranged, 6)
                .Requires("cold_blood").With(DerivedStat.CritChance, 5).With(DerivedStat.Accuracy, 3),
            new PerkDefinition("thick_hide", "Крепкая шкура", SkillType.Melee, 2).With(DerivedStat.MaxHp, 2),
            new PerkDefinition("battering_ram", "Таран", SkillType.Melee, 4).With(DerivedStat.Armor, 1),
            new PerkDefinition("light_step", "Лёгкий шаг", SkillType.Tactics, 2).With(DerivedStat.ActionPoints, 1),
            new PerkDefinition("unshakeable", "Невозмутимость", SkillType.Tactics, 4).With(DerivedStat.Resolve, 3),
            // Утилита/соц (US-3.11: пассив + бонусы к проверкам, активок не дают).
            new PerkDefinition("triage", "Сортировка раненых", SkillType.Medicine, 2)
                .With(DerivedStat.Resolve, 2).WithCheck(SkillType.Medicine, 1),
            new PerkDefinition("grease_monkey", "Смазчик", SkillType.Mechanics, 2)
                .With(DerivedStat.Initiative, 1).WithCheck(SkillType.Mechanics, 1),
            new PerkDefinition("scrounger", "Хомяк", SkillType.Survival, 2)
                .With(DerivedStat.Carry, 2).WithCheck(SkillType.Survival, 1),
            new PerkDefinition("born_orator", "Прирождённый оратор", SkillType.Persuasion, 3)
                .WithCheck(SkillType.Persuasion, 1).WithCheck(SkillType.Trade, 1)
        };
    }
}
