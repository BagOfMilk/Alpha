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
    /// </summary>
    public static class DefaultItems
    {
        // ===== Базовий дроп-пул (порядок пулу — від гіршого до кращого, без ваг) =====

        public static ItemDefinition ScavengedKnife() =>
            new ItemDefinition("scavenged_knife", "Знайдений ніж", EquipSlot.Weapon)
                .WithStat(StatKey.DamageBonus, 1);

        public static ItemDefinition WornVest() =>
            new ItemDefinition("worn_vest", "Потертий каптан", EquipSlot.Armor)
                .WithStat(StatKey.Armor, 1)
                .WithStat(StatKey.MaxHp, 1);

        public static ItemDefinition HuntersBow() =>
            new ItemDefinition("hunters_bow", "Мисливський лук", EquipSlot.Weapon)
                .WithStat(StatKey.Accuracy, 2)
                .WithStat(StatKey.DamageBonus, 1);

        public static ItemDefinition ScoutingGear() =>
            new ItemDefinition("scouting_gear", "Розвідницьке спорядження", EquipSlot.Accessory)
                .WithStat(StatKey.Accuracy, 3)
                .WithStat(StatKey.CritChance, 2);

        /// <summary>Базова лут-таблиця вилазки: пул від гіршого до кращого (Worst..Best).</summary>
        public static LootTable DropTable() => new LootTable()
            .Add(ScavengedKnife())
            .Add(WornVest())
            .Add(HuntersBow())
            .Add(ScoutingGear());

        // ===== Іменні предмети (заслужені; фікс + унікальний ефект, US-6.1) =====

        /// <summary>«Егіда» — іменна броня: висока захисна база + сигнатурний бонус.</summary>
        public static ItemDefinition AegisPlate() =>
            new ItemDefinition("aegis_plate", "Егіда", EquipSlot.Armor)
                .WithStat(StatKey.Armor, 3)
                .WithStat(StatKey.MaxHp, 5)
                .Named(Rarity.Rare, new SignatureEffect("Незламність",
                    StatModifier.Flat(StatKey.Defense, 3, ModifierSource.Gear, "aegis_plate")));

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

        /// <summary>Усі визначення за id — для відновлення гіра зі слепка (Inventory.RestoreState).</summary>
        public static List<ItemDefinition> AllDefinitions() => new List<ItemDefinition>
        {
            ScavengedKnife(), WornVest(), HuntersBow(), ScoutingGear(),
            AegisPlate(), ScoutHorn()
        };
    }
}
