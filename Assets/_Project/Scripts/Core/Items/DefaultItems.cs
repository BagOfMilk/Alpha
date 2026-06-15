using Game.Core.Combat;
using Game.Core.Stats;

namespace Game.Core.Items
{
    /// <summary>
    /// Сид-контент предметов (Эпик 6, ПЛЕЙСХОЛДЕР-флавор; полная переименовка — отдельный
    /// пасс). В новом неймспейсе, чтобы не трогать общий DefaultContent.cs. Базовый пул
    /// — рандом-дроп (роллы × редкость); именные — заработанные, фиксированы, с
    /// уникальным эффектом/проком (US-6.1).
    /// </summary>
    public static class DefaultItems
    {
        // ===== Базовый дроп-пул (роллы катаются, US-6.1 Вариант Б) =====
        public static ItemDefinition ScavengedRifle() =>
            new ItemDefinition("rifle_item", "Сборная винтовка", EquipSlot.Weapon)
                .WithWeapon(new WeaponDefinition("rifle_item_w", "Сборная винтовка", SkillType.Ranged)
                {
                    Damage = DamageType.Ballistic, DamageMin = 3, DamageMax = 5, CritDamageBonus = 2,
                    ApCost = 4, OptimalRange = 6
                })
                .Roll(DerivedStat.Accuracy, 2, 5);

        public static ItemDefinition ArmorVest() =>
            new ItemDefinition("armor_vest", "Бронежилет", EquipSlot.Armor)
                .Roll(DerivedStat.Armor, 1, 2)
                .Roll(DerivedStat.MaxHp, 1, 3);

        public static ItemDefinition TargetingScope() =>
            new ItemDefinition("scope", "Прицел", EquipSlot.Accessory)
                .Roll(DerivedStat.Accuracy, 3, 6)
                .Roll(DerivedStat.CritChance, 2, 4);

        /// <summary>Базовая лут-таблица вылазки.</summary>
        public static LootTable DropTable() => new LootTable()
            .Add(ScavengedRifle())
            .Add(ArmorVest())
            .Add(TargetingScope());

        // ===== Именные предметы (заработанные; фикс + уникальный эффект, US-6.1) =====
        /// <summary>«Вдоводел» — именная винтовка: уникальный прок Кровотечения (через WeaponDefinition).</summary>
        public static ItemDefinition Widowmaker() =>
            new ItemDefinition("widowmaker", "Вдоводел", EquipSlot.Weapon)
                .WithWeapon(new WeaponDefinition("widowmaker_w", "Вдоводел", SkillType.Ranged)
                {
                    Damage = DamageType.Ballistic, DamageMin = 4, DamageMax = 6, CritDamageBonus = 3,
                    ApCost = 4, OptimalRange = 7, StatusOnHit = StatusType.Bleeding // унікальний прок
                })
                .Fixed(DerivedStat.Accuracy, 8)
                .Fixed(DerivedStat.CritChance, 5)
                .Named(Rarity.Epic);

        /// <summary>«Эгида» — именная броня: высокая защита + сигнатурный +Resolve.</summary>
        public static ItemDefinition AegisPlate() =>
            new ItemDefinition("aegis_plate", "Эгида", EquipSlot.Armor)
                .Fixed(DerivedStat.Armor, 3)
                .Fixed(DerivedStat.MaxHp, 5)
                .Named(Rarity.Rare, new SignatureEffect("Несгибаемость",
                    new StatModifier(DerivedStat.Resolve, 3)));
    }
}
