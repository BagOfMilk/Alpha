using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Checks;
using Game.Core.Economy;
using Game.Core.Items;
using Game.Core.Loop;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Лут/гір/крафт (Епік 6, пакет B3): рідкість множить магнітуду роллів,
    /// іменні фіксовані з унікальним ефектом, гір ллється в єдиний агрегатор,
    /// лут детермінований за полосою виходу (R1 — жодної випадковості), крафт
    /// ніколи не знижує стат, сташ переживає save/load побайтово точно.
    /// </summary>
    public class ItemTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();

        private static double ModValue(ItemInstance item, StatKey key)
        {
            double sum = 0;
            foreach (var m in item.Modifiers())
                if (m.Key == key) sum += m.Value;
            return sum;
        }

        private static Companion MakeCompanion(string id)
        {
            var archetype = new CompanionArchetype(id, id)
                .SetAttribute(AttributeType.Strength, 3)
                .SetAttribute(AttributeType.Agility, 3)
                .SetAttribute(AttributeType.Wits, 3)
                .SetAttribute(AttributeType.Will, 3);
            return archetype.CreateInstance(id + "_i", Cfg);
        }

        // ---- Рідкість множить магнітуду (Варіант Б) ----
        [Test]
        public void Rarity_MultipliesRollMagnitude()
        {
            var def = new ItemDefinition("a", "A", EquipSlot.Armor).WithStat(StatKey.Armor, 2);
            Assert.AreEqual(2, ModValue(new ItemInstance(def, Rarity.Common), StatKey.Armor));
            Assert.AreEqual(3, ModValue(new ItemInstance(def, Rarity.Uncommon), StatKey.Armor)); // ×1.5
            Assert.AreEqual(4, ModValue(new ItemInstance(def, Rarity.Rare), StatKey.Armor));     // ×2
            Assert.AreEqual(5, ModValue(new ItemInstance(def, Rarity.Epic), StatKey.Armor));     // ×2.5
        }

        [Test]
        public void Drop_HalfValues_RoundAwayFromZero_NotToEven()
        {
            // Math.Round(double) за замовчуванням округлює «до парного»: 4.5 → 4,
            // а 7.5 → 8. На множниках рідкості (1.5/2.5) цілі базові значення
            // регулярно дають рівно .5 — магнітуда залежала б від парності.
            var scope = new ItemDefinition("scope", "Приціл", EquipSlot.Accessory).WithStat(StatKey.Accuracy, 3);
            Assert.AreEqual(5, ModValue(new ItemInstance(scope, Rarity.Uncommon), StatKey.Accuracy), "3×1.5=4.5 → 5, не 4");

            var vest = new ItemDefinition("v", "V", EquipSlot.Armor).WithStat(StatKey.Armor, 1);
            Assert.AreEqual(3, ModValue(new ItemInstance(vest, Rarity.Epic), StatKey.Armor), "1×2.5=2.5 → 3, не 2");
        }

        // ---- Іменні: фікс + унікальний ефект ----
        [Test]
        public void NamedItem_FixedRolls_NotScaled_PlusUniqueEffect()
        {
            var aegis = ItemInstance.NamedFrom(DefaultItems.AegisPlate()); // Rare, але фікс
            Assert.AreEqual(Rarity.Rare, aegis.Rarity);
            Assert.AreEqual(3, ModValue(aegis, StatKey.Armor), "іменне не множиться магнітудою");
            Assert.AreEqual(5, ModValue(aegis, StatKey.MaxHp));
            Assert.AreEqual(3, ModValue(aegis, StatKey.Defense), "сигнатурний ефект поверх бази");
        }

        [Test]
        public void ScoutHorn_IsNamed_WithWorldEffectData_NoPulseDependency()
        {
            var def = DefaultItems.ScoutHorn();
            Assert.IsTrue(def.IsNamed);
            Assert.IsNotNull(def.WorldEffect);
            Assert.AreEqual("forewarn_boost", def.WorldEffect.Key);
            Assert.AreEqual(2, def.WorldEffect.Charges);

            var withCfg = DefaultItems.ScoutHorn(new ItemBalance { ScoutHornForewarnCharges = 5 });
            Assert.AreEqual(5, withCfg.WorldEffect.Charges, "число заряду береться з ItemBalance (R14)");
        }

        // ---- Екіпіровка ----
        [Test]
        public void Equip_ReturnsPreviousInSlot()
        {
            var eq = new Equipment();
            var a = new ItemInstance(DefaultItems.WornVest(), Rarity.Common);
            var b = new ItemInstance(DefaultItems.WornVest(), Rarity.Rare);

            Assert.IsNull(eq.Equip(a));
            Assert.AreSame(a, eq.Equip(b), "той самий слот → повертається попередній предмет");
            Assert.AreSame(b, eq.Get(EquipSlot.Armor));
            Assert.AreSame(b, eq.Unequip(EquipSlot.Armor));
            Assert.IsNull(eq.Get(EquipSlot.Armor));
        }

        [Test]
        public void Equipment_ExposesActiveWorldEffects_FromEquippedItems()
        {
            var eq = new Equipment();
            Assert.AreEqual(0, new List<ItemWorldEffect>(eq.ActiveWorldEffects()).Count);

            eq.Equip(ItemInstance.NamedFrom(DefaultItems.ScoutHorn()));
            var effects = new List<ItemWorldEffect>(eq.ActiveWorldEffects());
            Assert.AreEqual(1, effects.Count);
            Assert.AreEqual("forewarn_boost", effects[0].Key);
        }

        // ---- Гір у єдиному агрегаторі (US-6.2/18.2) ----
        [Test]
        public void Gear_FlowsIntoDerivedStats()
        {
            var c = MakeCompanion("hero");
            double baseHp = c.Resolve(Cfg).Derived(DerivedStat.MaxHp);
            double baseArmor = c.Resolve(Cfg).Derived(DerivedStat.Armor);

            // Потертий каптан Common: детерміновано Armor=1, MaxHp=1 (без рандому).
            c.Equipment.Equip(new ItemInstance(DefaultItems.WornVest(), Rarity.Common));

            var snap = c.Resolve(Cfg);
            Assert.AreEqual(baseArmor + 1, snap.Derived(DerivedStat.Armor));
            Assert.AreEqual(baseHp + 1, snap.Derived(DerivedStat.MaxHp));
        }

        [Test]
        public void Companion_Equipment_IsRegisteredProvider_ByConstruction()
        {
            var c = MakeCompanion("hero2");
            c.Equipment.Equip(new ItemInstance(DefaultItems.ScoutingGear(), Rarity.Common));
            var snap = c.Resolve(Cfg);
            // ScoutingGear Common: Accuracy=3, CritChance=2 — якщо провайдер не
            // зареєстрований у Companion._providers, ці числа в снапшот не потраплять.
            Assert.Greater(snap.SourcesOf(StatKey.Accuracy).Count, 0);
        }

        // ---- Крафт ----
        [Test]
        public void Craft_Upgrade_SpendsMaterialsAndGold_RaisesRarity()
        {
            var ledger = new ResourceLedger();
            ledger.Add(ResourceType.Materials, 5);
            ledger.Add(ResourceType.Gold, 10);
            var item = new ItemInstance(DefaultItems.WornVest(), Rarity.Common); // Armor=1

            var res = CraftSystem.TryUpgrade(item, ledger, workshopOpen: true, materialsCost: 3, goldCost: 5);
            Assert.AreEqual(CraftResult.Success, res);
            Assert.AreEqual(Rarity.Uncommon, item.Rarity);
            Assert.AreEqual(2, ledger.Get(ResourceType.Materials)); // 5 − 3
            Assert.AreEqual(5, ledger.Get(ResourceType.Gold));      // 10 − 5
        }

        [Test]
        public void Craft_RequiresOpenWorkshop_NotCityWorksInternals()
        {
            var ledger = new ResourceLedger();
            ledger.Add(ResourceType.Materials, 100);
            ledger.Add(ResourceType.Gold, 100);
            var item = new ItemInstance(DefaultItems.WornVest(), Rarity.Common);

            Assert.AreEqual(CraftResult.WorkshopClosed,
                CraftSystem.TryUpgrade(item, ledger, workshopOpen: false, materialsCost: 1, goldCost: 1));
            Assert.AreEqual(Rarity.Common, item.Rarity, "закрита майстерня не змінює предмет");
        }

        [Test]
        public void Craft_CannotAfford_OrNamed_OrMaxed()
        {
            var poor = new ResourceLedger();
            poor.Add(ResourceType.Materials, 1);
            poor.Add(ResourceType.Gold, 1);
            var item = new ItemInstance(DefaultItems.WornVest(), Rarity.Common);
            Assert.AreEqual(CraftResult.CannotAfford,
                CraftSystem.TryUpgrade(item, poor, true, materialsCost: 3, goldCost: 5));

            var named = ItemInstance.NamedFrom(DefaultItems.AegisPlate());
            Assert.AreEqual(CraftResult.NamedNotUpgradable,
                CraftSystem.TryUpgrade(named, poor, true, materialsCost: 0, goldCost: 0));

            var epic = new ItemInstance(DefaultItems.WornVest(), Rarity.Epic);
            Assert.AreEqual(CraftResult.AlreadyMaxRarity,
                CraftSystem.TryUpgrade(epic, poor, true, materialsCost: 0, goldCost: 0));
        }

        [Test]
        public void Craft_UsesItemBalanceOverload_ForDefaultCosts()
        {
            var ledger = new ResourceLedger();
            ledger.Add(ResourceType.Materials, 3);
            ledger.Add(ResourceType.Gold, 5);
            var item = new ItemInstance(DefaultItems.WornVest(), Rarity.Common);

            var res = CraftSystem.TryUpgrade(item, ledger, true, Cfg.Items);
            Assert.AreEqual(CraftResult.Success, res);
            Assert.AreEqual(0, ledger.Get(ResourceType.Materials));
            Assert.AreEqual(0, ledger.Get(ResourceType.Gold));
        }

        // ---- Апгрейд монотонний: за дефіцитний компонент не можна ослабнути ----
        [Test]
        public void Craft_Upgrade_NeverLowersStats()
        {
            var ledger = new ResourceLedger();
            ledger.Add(ResourceType.Materials, 100);
            ledger.Add(ResourceType.Gold, 100);

            var item = new ItemInstance(DefaultItems.HuntersBow(), Rarity.Common);
            var before = new List<StatModifier>(item.StatMods);
            Assert.Greater(before.Count, 0);

            // Два апгрейди підряд: саме тут пряме Resolve(rarity) з бази
            // могло б дати нижче значення через накопичене округлення.
            Assert.AreEqual(CraftResult.Success, CraftSystem.TryUpgrade(item, ledger, true, 1, 1));
            Assert.AreEqual(CraftResult.Success, CraftSystem.TryUpgrade(item, ledger, true, 1, 1));
            Assert.AreEqual(Rarity.Rare, item.Rarity);

            foreach (var old in before)
            {
                double now = 0;
                foreach (var m in item.StatMods)
                    if (m.Key == old.Key) { now = m.Value; break; }
                Assert.Greater(now, old.Value,
                    $"{old.Key}: апгрейд обов'язково покращує, а не перекочує наосліп");
            }
        }

        [Test]
        public void Craft_Upgrade_IsFullyDeterministic()
        {
            var ledger = new ResourceLedger();
            ledger.Add(ResourceType.Materials, 100);
            ledger.Add(ResourceType.Gold, 100);
            var a = new ItemInstance(DefaultItems.WornVest(), Rarity.Common);
            var b = new ItemInstance(DefaultItems.WornVest(), Rarity.Common);

            CraftSystem.TryUpgrade(a, ledger, true, 1, 1);
            CraftSystem.TryUpgrade(b, ledger, true, 1, 1);

            for (int i = 0; i < a.StatMods.Count; i++)
                Assert.AreEqual(a.StatMods[i].Value, b.StatMods[i].Value,
                    "результат апгрейду детермінований — рандому в ядрі немає (R1)");
        }

        // ---- Лут-таблиця: детермінована за полосою, без випадковості ----
        [Test]
        public void Loot_Roll_IsDeterministicByBand()
        {
            var table = DefaultItems.DropTable();
            var a = table.Roll(OutcomeBand.Good);
            var b = table.Roll(OutcomeBand.Good);
            Assert.IsNotNull(a);
            Assert.AreEqual(a.Definition.Id, b.Definition.Id);
            Assert.AreEqual(a.Rarity, b.Rarity);
        }

        [Test]
        public void Loot_BetterBand_NeverGivesLowerRarity()
        {
            var table = DefaultItems.DropTable();
            var prev = Rarity.Common;
            var bands = new[] { OutcomeBand.Worst, OutcomeBand.Base, OutcomeBand.Good, OutcomeBand.Best };
            for (int i = 0; i < bands.Length; i++)
            {
                var item = table.Roll(bands[i]);
                Assert.IsNotNull(item);
                Assert.GreaterOrEqual((int)item.Rarity, (int)prev);
                prev = item.Rarity;
            }
        }

        [Test]
        public void Loot_BestBand_PrefersNamedItem_WhenTableHasOne()
        {
            var table = new LootTable().Add(DefaultItems.WornVest());
            table.AddNamed(DefaultItems.ScoutHorn());

            var drop = table.Roll(OutcomeBand.Best);
            Assert.AreEqual("scout_horn", drop.Definition.Id);
            Assert.IsTrue(drop.Definition.IsNamed);
        }

        // ---- Сташ: побайтова безперервність save/load ----
        [Test]
        public void Inventory_SaveRoundTrip_PreservesItemsAndStats()
        {
            var inv = new Inventory();
            inv.Add(new ItemInstance(DefaultItems.WornVest(), Rarity.Rare));
            inv.Add(ItemInstance.NamedFrom(DefaultItems.AegisPlate()));
            Assert.AreEqual(2, inv.Count);

            var blob = ((IStateBlob)inv).CaptureState();
            var restored = new Inventory();
            ((IStateBlob)restored).RestoreState(blob);

            Assert.AreEqual(inv.Count, restored.Count);
            for (int i = 0; i < inv.Items.Count; i++)
            {
                var original = inv.Items[i];
                var back = restored.Items[i];
                Assert.AreEqual(original.Definition.Id, back.Definition.Id);
                Assert.AreEqual(original.Rarity, back.Rarity);
                Assert.AreEqual(original.StatMods.Count, back.StatMods.Count);
                for (int j = 0; j < original.StatMods.Count; j++)
                {
                    Assert.AreEqual(original.StatMods[j].Key, back.StatMods[j].Key);
                    Assert.AreEqual(original.StatMods[j].Value, back.StatMods[j].Value, 1e-9);
                }
            }
        }

        [Test]
        public void Inventory_SaveRoundTrip_PreservesCraftedValues_NotRecomputedFromBase()
        {
            // Два апгрейди підряд: якщо RestoreState пере-обчислював би з бази
            // (Definition.StatRolls × RarityTuning), число розійшлося б із
            // живим — бо UpgradeTo масштабує ВІД поточного, а не з нуля.
            var item = new ItemInstance(DefaultItems.HuntersBow(), Rarity.Common);
            var ledger = new ResourceLedger();
            ledger.Add(ResourceType.Materials, 100);
            ledger.Add(ResourceType.Gold, 100);
            CraftSystem.TryUpgrade(item, ledger, true, 1, 1);
            CraftSystem.TryUpgrade(item, ledger, true, 1, 1);

            var inv = new Inventory();
            inv.Add(item);
            var blob = ((IStateBlob)inv).CaptureState();

            var restored = new Inventory();
            ((IStateBlob)restored).RestoreState(blob);
            var back = restored.Items[0];

            Assert.AreEqual(item.Rarity, back.Rarity);
            for (int j = 0; j < item.StatMods.Count; j++)
                Assert.AreEqual(item.StatMods[j].Value, back.StatMods[j].Value, 1e-9,
                    "збережене значення — а не пере-роздача з бази");
        }

        [Test]
        public void Inventory_RestoreState_SkipsUnknownItems_DoesNotThrow()
        {
            var restored = new Inventory();
            Assert.DoesNotThrow(() => ((IStateBlob)restored).RestoreState("nonexistent_item:0:"));
            Assert.AreEqual(0, restored.Count);
        }

        [Test]
        public void Inventory_EmptyState_RoundTrips()
        {
            var inv = new Inventory();
            var blob = ((IStateBlob)inv).CaptureState();
            Assert.AreEqual(string.Empty, blob);

            var restored = new Inventory();
            ((IStateBlob)restored).RestoreState(blob);
            Assert.AreEqual(0, restored.Count);
        }
    }
}
