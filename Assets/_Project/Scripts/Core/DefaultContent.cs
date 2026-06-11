using System.Collections.Generic;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Health;
using Game.Core.Stats;
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
            .WithCheck(SkillType.Intimidation, 2).WithCombat(DerivedStat.MaxHp, 2).WithValue("сила");

        public static Trait HotTempered() => new Trait("hot_tempered", "Вспыльчивый", TraitSign.Vice)
            .WithCheck(SkillType.Persuasion, -1).WithValue("гнев");

        public static Trait Handy() => new Trait("handy", "Рукастый", TraitSign.Virtue)
            .WithCheck(SkillType.Mechanics, 2).WithCheck(SkillType.Hacking, 1).WithValue("техника");

        public static Trait SteadyHands() => new Trait("steady_hands", "Спокойные руки", TraitSign.Virtue)
            .WithCheck(SkillType.Medicine, 2).WithValue("забота");

        public static Trait SilverTongue() => new Trait("silver_tongue", "Острый язык", TraitSign.Virtue)
            .WithCheck(SkillType.Persuasion, 2).WithCheck(SkillType.Trade, 1).WithValue("слово");

        public static Trait BornLeader() => new Trait("born_leader", "Прирождённый лидер", TraitSign.Virtue)
            .WithCheck(SkillType.Persuasion, 1).WithValue("долг");

        // ===== Шрамы (метят при Серьёзном+ ранении, US-4.2) =====
        public static Scar OneEye() => new Scar("one_eye", "Одноглазый").WithCombat(DerivedStat.Accuracy, -10);
        public static Scar Limp() => new Scar("limp", "Хромой").WithCombat(DerivedStat.ActionPoints, -1);

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

        /// <summary>Прорыв: быстрый рывок в ближний, когти с Кровотечением; мутант (уязвим к огню).</summary>
        public static EnemyDefinition FeralGhoul() =>
            new EnemyDefinition("feral_ghoul", "Дикий гул", EnemyRole.Breacher, EnemyFamily.Mutant)
            {
                MaxHp = 9, MaxAp = 10, Accuracy = 60, Defense = 5, Initiative = 8,
                CritChance = 10, Armor = 0, Resolve = 0,
                Resists = new ResistProfile().With(DamageType.Fire, 1.5),
                Weapon = GhoulClaws()
            };
    }
}
