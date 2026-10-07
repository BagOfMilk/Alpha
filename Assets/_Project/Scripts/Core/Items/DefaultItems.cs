using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Stats;

namespace Game.Core.Items
{
    /// <summary>
    /// Сід-контент предметів (Епік 6, ПЛЕЙСХОЛДЕР-флавор; повне переймення —
    /// окремий прохід нарративної майстерні). Базовий пул — дроп вилазки/
    /// данжу (роли × рідкість, детерміновано за полосою — R1); іменні —
    /// заслужені, фіксовані, можуть мати унікальний ефект (US-6.1).
    /// Поправка №19.2: кожна річ, яку видно на тілі, має <see cref="ItemDefinition.VisualKey"/>
    /// (частина модульного набору), зброя — <see cref="ItemDefinition.CombatWeaponId"/>.
    /// Числа статів — ПЛЕЙСХОЛДЕР; броня плоска й мала (GDD R13: 1–2).
    /// </summary>
    public static class DefaultItems
    {
        // ===== Базовий дроп-пул (порядок пулу — від гіршого до кращого, без ваг) =====

        public static ItemDefinition ScavengedKnife() =>
            new ItemDefinition("scavenged_knife", "Знайдений ніж", EquipSlot.Weapon)
                .WithStat(StatKey.DamageBonus, 1)
                .Visual("wpn_dagger").Fights("weapon.dagger");

        public static ItemDefinition WornVest() =>
            new ItemDefinition("worn_vest", "Потертий каптан", EquipSlot.Armor)
                .WithStat(StatKey.Armor, 1)
                .WithStat(StatKey.MaxHp, 1)
                .Visual("kaftan");

        public static ItemDefinition HuntersBow() =>
            new ItemDefinition("hunters_bow", "Мисливський лук", EquipSlot.Weapon)
                .WithStat(StatKey.Accuracy, 2)
                .WithStat(StatKey.DamageBonus, 1)
                .Visual("wpn_bow").Fights("weapon.horde_bow", twoHanded: true);

        public static ItemDefinition ScoutingGear() =>
            new ItemDefinition("scouting_gear", "Розвідницьке спорядження", EquipSlot.Accessory)
                .WithStat(StatKey.Accuracy, 3)
                .WithStat(StatKey.CritChance, 2);

        /// <summary>Базова лут-таблиця вилазки: пул від гіршого до кращого (Worst..Best).
        /// Новий гір (№19.2) сюди НЕ доданий: розширений пул змістив би детермінований дроп
        /// полос, на якому стоять тести темпу; він приходить із кузні Збройні.</summary>
        public static LootTable DropTable() => new LootTable()
            .Add(ScavengedKnife())
            .Add(WornVest())
            .Add(HuntersBow())
            .Add(ScoutingGear());

        // ===== Зброя (кузня Збройні; №19.2) =====

        public static ItemDefinition ArmingSword() =>
            new ItemDefinition("arming_sword", "Меч", EquipSlot.Weapon)
                .WithStat(StatKey.DamageBonus, 1).WithStat(StatKey.Defense, 1)
                .Visual("wpn_sword").Fights("weapon.sword");

        public static ItemDefinition Sabre() =>
            new ItemDefinition("sabre", "Шабля", EquipSlot.Weapon)
                .WithStat(StatKey.DamageBonus, 1).WithStat(StatKey.Initiative, 1)
                .Visual("wpn_sabre").Fights("weapon.sabre");

        public static ItemDefinition CurvedBlade() =>
            new ItemDefinition("curved_blade", "Довгий вигнутий клинок", EquipSlot.Weapon)
                .WithStat(StatKey.DamageBonus, 2).WithStat(StatKey.CritChance, 2)
                .Visual("wpn_katana").Fights("weapon.curved_blade", twoHanded: true);

        public static ItemDefinition WarAxe() =>
            new ItemDefinition("war_axe", "Бойова сокира", EquipSlot.Weapon)
                .WithStat(StatKey.DamageBonus, 2)
                .Visual("wpn_axe").Fights("weapon.axe");

        public static ItemDefinition Mace() =>
            new ItemDefinition("mace", "Булава", EquipSlot.Weapon)
                .WithStat(StatKey.DamageBonus, 1)
                .Visual("wpn_mace").Fights("weapon.mace");

        public static ItemDefinition Spear() =>
            new ItemDefinition("spear", "Спис", EquipSlot.Weapon)
                .WithStat(StatKey.DamageBonus, 1).WithStat(StatKey.Initiative, 1)
                .Visual("wpn_spear").Fights("weapon.horde_spear", twoHanded: true);

        public static ItemDefinition Club() =>
            new ItemDefinition("club", "Кийок", EquipSlot.Weapon)
                .WithStat(StatKey.DamageBonus, 1)
                .Visual("wpn_club").Fights("weapon.club");

        public static ItemDefinition Musket() =>
            new ItemDefinition("musket", "Крем'яна рушниця", EquipSlot.Weapon)
                .WithStat(StatKey.DamageBonus, 2)
                .Visual("wpn_musket").Fights("weapon.musket", twoHanded: true);

        // ===== Броня тулуба =====

        public static ItemDefinition Gambeson() =>
            new ItemDefinition("gambeson", "Стьобаний гамбезон", EquipSlot.Armor)
                .WithStat(StatKey.Armor, 1).WithStat(StatKey.MaxHp, 2)
                .Visual("gambeson");

        public static ItemDefinition MailHauberk() =>
            new ItemDefinition("mail_hauberk", "Кольчуга", EquipSlot.Armor)
                .WithStat(StatKey.Armor, 2)
                .Visual("mail");

        public static ItemDefinition Cuirass() =>
            new ItemDefinition("cuirass", "Кіраса", EquipSlot.Armor)
                .WithStat(StatKey.Armor, 2).WithStat(StatKey.MaxHp, 1).WithStat(StatKey.Initiative, -1)
                .Visual("cuirass");

        // ===== Голова, руки, ноги, стопи =====

        public static ItemDefinition SpangenHelm() =>
            new ItemDefinition("spangen_helm", "Шолом із бармицею", EquipSlot.Head)
                .WithStat(StatKey.Armor, 1)
                .Visual("helm_spangen");

        public static ItemDefinition ConicalHelm() =>
            new ItemDefinition("conical_helm", "Шпичастий шолом", EquipSlot.Head)
                .WithStat(StatKey.Armor, 1)
                .Visual("helm_conical");

        public static ItemDefinition KettleHat() =>
            new ItemDefinition("kettle_hat", "Капелюх-шолом", EquipSlot.Head)
                .WithStat(StatKey.MaxHp, 1).WithStat(StatKey.Defense, 1)
                .Visual("helm_kettle");

        public static ItemDefinition LamellarHelm() =>
            new ItemDefinition("lamellar_helm", "Шолом зі ступінчастим назатильником", EquipSlot.Head)
                .WithStat(StatKey.Armor, 1).WithStat(StatKey.Defense, 1)
                .Visual("helm_kabuto");

        public static ItemDefinition LeatherBracers() =>
            new ItemDefinition("leather_bracers", "Шкіряні наручі", EquipSlot.Hands)
                .WithStat(StatKey.Defense, 1)
                .Visual("bracers");

        public static ItemDefinition Greaves() =>
            new ItemDefinition("greaves", "Поножі", EquipSlot.Legs)
                .WithStat(StatKey.Armor, 1)
                .Visual("greaves");

        public static ItemDefinition MarchingBoots() =>
            new ItemDefinition("marching_boots", "Похідні чоботи", EquipSlot.Feet)
                .WithStat(StatKey.Initiative, 1)
                .Visual("boots");

        // ===== Щити (друга рука) =====

        public static ItemDefinition RoundShield() =>
            new ItemDefinition("round_shield", "Круглий щит", EquipSlot.Offhand)
                .WithStat(StatKey.Defense, 2)
                .Visual("shield_round");

        public static ItemDefinition KiteShield() =>
            new ItemDefinition("kite_shield", "Мигдалеподібний щит", EquipSlot.Offhand)
                .WithStat(StatKey.Defense, 2).WithStat(StatKey.Armor, 1).WithStat(StatKey.Initiative, -1)
                .Visual("shield_kite");

        public static ItemDefinition Buckler() =>
            new ItemDefinition("buckler", "Баклер", EquipSlot.Offhand)
                .WithStat(StatKey.Defense, 1).WithStat(StatKey.Initiative, 1)
                .Visual("buckler");

        /// <summary>
        /// Що кує Збройня (№19.2): базовий гір без рідкості понад Common (рідкість піднімає
        /// Майстерня — <see cref="CraftSystem"/>). Ціна — <see cref="ItemBalance.ForgeCost"/>.
        /// </summary>
        public static List<ItemDefinition> ForgeCatalog() => new List<ItemDefinition>
        {
            ArmingSword(), Sabre(), CurvedBlade(), WarAxe(), Mace(), Spear(), Club(), Musket(), HuntersBow(),
            Gambeson(), MailHauberk(), Cuirass(),
            SpangenHelm(), ConicalHelm(), KettleHat(), LamellarHelm(),
            LeatherBracers(), Greaves(), MarchingBoots(),
            RoundShield(), KiteShield(), Buckler()
        };

        // ===== Іменні предмети (заслужені; фікс + унікальний ефект, US-6.1) =====

        /// <summary>«Егіда» — іменна броня: висока захисна база + сигнатурний бонус.</summary>
        public static ItemDefinition AegisPlate() =>
            new ItemDefinition("aegis_plate", "Егіда", EquipSlot.Armor)
                .WithStat(StatKey.Armor, 3)
                .WithStat(StatKey.MaxHp, 5)
                .Named(Rarity.Rare, new SignatureEffect("Незламність",
                    StatModifier.Flat(StatKey.Defense, 3, ModifierSource.Gear, "aegis_plate")))
                .Visual("cuirass");

        /// <summary>
        /// «Ріг вивідника» (item.scout_horn) — данж «Покинутий табір авангарду»,
        /// кімната 2 (гарантований лут, §7.11). Ефект «forewarn_boost»: наступні
        /// N передвісників чуються чіткіше й раніше (N — Balance.ItemBalance.
        /// ScoutHornForewarnCharges, R14) — дані для D1, Items сам пульс не
        /// рухає (див. <see cref="ItemWorldEffect"/>).
        /// </summary>
        public static ItemDefinition ScoutHorn(ItemBalance cfg = null)
        {
            cfg = cfg ?? new ItemBalance();
            return new ItemDefinition("scout_horn", "Ріг вивідника", EquipSlot.Accessory)
                .Named(Rarity.Rare)
                .WithWorldEffect("forewarn_boost", cfg.ScoutHornForewarnCharges);
        }

        /// <summary>Усі визначення за id — для відновлення гіра зі слепка (сташ і надіте).</summary>
        public static List<ItemDefinition> AllDefinitions()
        {
            var all = new List<ItemDefinition> { ScavengedKnife(), WornVest(), HuntersBow(), ScoutingGear(), AegisPlate(), ScoutHorn() };
            foreach (var d in ForgeCatalog())
                if (all.TrueForAll(x => x.Id != d.Id)) all.Add(d);
            return all;
        }
    }
}
