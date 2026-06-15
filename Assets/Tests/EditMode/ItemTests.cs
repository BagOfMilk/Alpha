using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Economy;
using Game.Core.Items;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Лут/гир/крафт (Эпик 6): редкость множит магнитуду роллов, именные фиксированы
    /// с уникальным эффектом, гир льётся в единый агрегатор, именное оружие прокает в
    /// бою, крафт апгрейдит за крафтовый компонент.
    /// </summary>
    public class ItemTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();

        private static int ModValue(ItemInstance item, DerivedStat stat)
        {
            int sum = 0;
            foreach (var m in item.Modifiers())
                if (m.Stat == stat) sum += (int)m.Value;
            return sum;
        }

        // ---- Редкость множит магнитуду (Вариант Б) ----
        [Test]
        public void Rarity_MultipliesRollMagnitude()
        {
            var def = new ItemDefinition("a", "A", EquipSlot.Armor).Fixed(DerivedStat.Armor, 2);
            Assert.AreEqual(2, ModValue(new ItemInstance(def, Rarity.Common, new ScriptedRng()), DerivedStat.Armor));
            Assert.AreEqual(3, ModValue(new ItemInstance(def, Rarity.Uncommon, new ScriptedRng()), DerivedStat.Armor)); // ×1.5
            Assert.AreEqual(4, ModValue(new ItemInstance(def, Rarity.Rare, new ScriptedRng()), DerivedStat.Armor));     // ×2
            Assert.AreEqual(5, ModValue(new ItemInstance(def, Rarity.Epic, new ScriptedRng()), DerivedStat.Armor));     // ×2.5
        }

        [Test]
        public void Drop_RollsWithinRange_FromRng()
        {
            var def = new ItemDefinition("r", "R", EquipSlot.Accessory).Roll(DerivedStat.Accuracy, 2, 5);
            var item = new ItemInstance(def, Rarity.Common, new ScriptedRng(3)); // Range(2,5) → 3
            Assert.AreEqual(3, ModValue(item, DerivedStat.Accuracy));
        }

        // ---- Именные: фикс + уникальный эффект ----
        [Test]
        public void NamedItem_FixedRolls_NotScaled_PlusUniqueEffect()
        {
            var aegis = ItemInstance.NamedFrom(DefaultItems.AegisPlate()); // Rare, но фикс
            Assert.AreEqual(Rarity.Rare, aegis.Rarity);
            Assert.AreEqual(3, ModValue(aegis, DerivedStat.Armor), "именное не множится магнитудой");
            Assert.AreEqual(5, ModValue(aegis, DerivedStat.MaxHp));
            Assert.AreEqual(3, ModValue(aegis, DerivedStat.Resolve), "сигнатурный эффект поверх базы");
        }

        // ---- Экипировка ----
        [Test]
        public void Equip_ReturnsPreviousInSlot()
        {
            var eq = new Equipment();
            var a = new ItemInstance(DefaultItems.ArmorVest(), Rarity.Common, new ScriptedRng(1, 1));
            var b = new ItemInstance(DefaultItems.ArmorVest(), Rarity.Rare, new ScriptedRng(2, 2));

            Assert.IsNull(eq.Equip(a));
            Assert.AreSame(a, eq.Equip(b), "тот же слот → возвращается прежний предмет");
            Assert.AreSame(b, eq.Get(EquipSlot.Armor));
            Assert.AreSame(b, eq.Unequip(EquipSlot.Armor));
            Assert.IsNull(eq.Get(EquipSlot.Armor));
        }

        [Test]
        public void EquippedWeapon_ExposedFromSlot()
        {
            var eq = new Equipment();
            Assert.IsNull(eq.EquippedWeapon);
            eq.Equip(ItemInstance.NamedFrom(DefaultItems.Widowmaker()));
            Assert.IsNotNull(eq.EquippedWeapon);
            Assert.AreEqual(StatusType.Bleeding, eq.EquippedWeapon.StatusOnHit);
        }

        // ---- Гир в едином агрегаторе ----
        [Test]
        public void Gear_FlowsIntoDerivedStats()
        {
            var c = new Companion("c", new AttributeBlock(3, 3, 3, 3), 4);
            int baseHp = c.GetDerived(DerivedStat.MaxHp, Cfg);
            int baseArmor = c.GetDerived(DerivedStat.Armor, Cfg);

            // Бронежилет Common с детерминированными роллами Armor=2, MaxHp=2.
            c.Equipment.Equip(new ItemInstance(DefaultItems.ArmorVest(), Rarity.Common, new ScriptedRng(2, 2)));

            Assert.AreEqual(baseArmor + 2, c.GetDerived(DerivedStat.Armor, Cfg));
            Assert.AreEqual(baseHp + 2, c.GetDerived(DerivedStat.MaxHp, Cfg));
        }

        [Test]
        public void NamedWeapon_ProcsInCombat()
        {
            var c = new Companion("hero", new AttributeBlock(4, 5, 3, 3), 4);
            c.Skills.Raise(SkillType.Ranged, 3);
            c.Equipment.Equip(ItemInstance.NamedFrom(DefaultItems.Widowmaker()));

            var map = new GridMap(8, 1);
            var cs = new CombatState(map, Cfg, new ScriptedRng(1, 100, 5)); // hit, no crit, dmg 5
            var hero = CombatUnit.FromCompanion(c, c.Equipment.EquippedWeapon, Cfg);
            var enemy = CombatUnit.FromEnemy(Game.Core.DefaultContent.ScavGunner(), "e");
            cs.AddUnit(hero, new GridPos(0, 0));
            cs.AddUnit(enemy, new GridPos(5, 0));
            cs.Begin();

            // Если первым в инициативе оказался враг — пропустим к ходу героя.
            if (cs.Current != hero) cs.EndTurn();
            Assert.AreSame(hero, cs.Current);

            Assert.AreEqual(CombatActionResult.Success, cs.Attack("e"));
            Assert.IsTrue(enemy.HasStatus(StatusType.Bleeding), "уникальный прок именной винтовки");
        }

        // ---- Крафт ----
        [Test]
        public void Craft_Upgrade_SpendsMaterial_RaisesRarity_Rerolls()
        {
            var ledger = new ResourceLedger();
            ledger.Add(ResourceType.CraftingMaterial, 5);
            var item = new ItemInstance(DefaultItems.ArmorVest(), Rarity.Common, new ScriptedRng(2, 2)); // Armor 2

            var res = CraftSystem.TryUpgrade(item, ledger, craftingCost: 3, new ScriptedRng(2, 2));
            Assert.AreEqual(CraftResult.Success, res);
            Assert.AreEqual(Rarity.Uncommon, item.Rarity);
            Assert.AreEqual(2, ledger.Get(ResourceType.CraftingMaterial)); // 5 − 3
            Assert.AreEqual(3, ModValue(item, DerivedStat.Armor));         // round(2 × 1.5)
        }

        [Test]
        public void Craft_CannotAfford_OrNamed_OrMaxed()
        {
            var poor = new ResourceLedger();
            poor.Add(ResourceType.CraftingMaterial, 1);
            var item = new ItemInstance(DefaultItems.ArmorVest(), Rarity.Common, new ScriptedRng(2, 2));
            Assert.AreEqual(CraftResult.CannotAfford, CraftSystem.TryUpgrade(item, poor, 3, new ScriptedRng()));

            var named = ItemInstance.NamedFrom(DefaultItems.Widowmaker());
            Assert.AreEqual(CraftResult.NamedNotUpgradable, CraftSystem.TryUpgrade(named, poor, 0, new ScriptedRng()));

            var epic = new ItemInstance(DefaultItems.ArmorVest(), Rarity.Epic, new ScriptedRng(2, 2));
            Assert.AreEqual(CraftResult.AlreadyMaxRarity, CraftSystem.TryUpgrade(epic, poor, 0, new ScriptedRng()));
        }

        // ---- Лут-генерация ----
        [Test]
        public void RollRarity_RespectsWeights()
        {
            Assert.AreEqual(Rarity.Epic, LootGenerator.RollRarity(new[] { 0, 0, 0, 1 }, new ScriptedRng(1)));
            Assert.AreEqual(Rarity.Common, LootGenerator.RollRarity(new[] { 1, 0, 0, 0 }, new ScriptedRng(1)));
        }

        [Test]
        public void Loot_Roll_IsDeterministicBySeed()
        {
            var table = DefaultItems.DropTable();
            var a = LootGenerator.Roll(table, new SeededRng(123));
            var b = LootGenerator.Roll(table, new SeededRng(123));
            Assert.IsNotNull(a);
            Assert.AreEqual(a.Definition.Id, b.Definition.Id);
            Assert.AreEqual(a.Rarity, b.Rarity);
        }
    }
}
