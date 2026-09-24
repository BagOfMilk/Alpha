using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// CombatUnit.FromCompanion/FromDefector/FromEnemy: аудит G18 требует, чтобы
    /// все релевантные производные статы наконец читались боем через единый
    /// агрегатор (StatResolver/StatSnapshot), а не заново считались формулой в
    /// самом Combat. Тест ловит рассинхрон, если кто-то в будущем забудет
    /// прочитать один из них.
    /// </summary>
    public class CombatUnitFactoryTests
    {
        private static CompanionArchetype Archetype(string id = "t") => new CompanionArchetype(id, "Тестовий")
            .SetAttribute(AttributeType.Strength, 6)
            .SetAttribute(AttributeType.Agility, 7)
            .SetAttribute(AttributeType.Wits, 5)
            .SetAttribute(AttributeType.Will, 8)
            .SetSkill(SkillType.Ranged, 4)
            .SetSkill(SkillType.Melee, 2)
            .SetSkill(SkillType.Medicine, 3);

        private static WeaponDefinition Bow() => new WeaponDefinition("w", "W", SkillType.Ranged)
        { DamageMin = 2, DamageMax = 4, ApCost = 3, OptimalRange = 6 };

        [Test]
        public void FromCompanion_ReadsAllRelevantDerivedStats_ViaResolver()
        {
            var cfg = new BalanceConfig();
            var companion = Archetype().CreateInstance("t1", cfg);
            var snap = companion.Resolve(cfg);
            var weapon = Bow();

            var unit = CombatUnit.FromCompanion(companion, weapon, cfg);

            Assert.AreEqual(snap.GetInt(StatKeys.Of(DerivedStat.MaxHp)), unit.Profile.MaxHp);
            Assert.AreEqual(snap.GetInt(StatKeys.Of(DerivedStat.MaxAp)), unit.Profile.MaxAp);
            Assert.AreEqual(snap.GetInt(StatKeys.Of(DerivedStat.Defense)), unit.Profile.Defense);
            Assert.AreEqual(snap.GetInt(StatKeys.Of(DerivedStat.Initiative)), unit.Profile.Initiative);
            Assert.AreEqual(snap.GetInt(StatKeys.Of(DerivedStat.CritChance)), unit.Profile.CritChance);
            Assert.AreEqual(snap.GetInt(StatKeys.Of(DerivedStat.Armor)), unit.Profile.Armor);
            Assert.AreEqual(snap.GetInt(StatKeys.Of(DerivedStat.StatusDurationReduction)), unit.Profile.Resolve);
            Assert.AreEqual(snap.GetInt(StatKeys.Of(DerivedStat.DamageBonus)), unit.Profile.DamageBonus);
            Assert.AreEqual(System.Math.Max(1, snap.GetInt(StatKeys.Of(DerivedStat.MoveApPerTile))), unit.Profile.MoveApPerTile);
            Assert.AreEqual(snap.Skill(SkillType.Medicine), unit.Profile.MedicineSkill);

            // Accuracy — единственная производная, куда бой добавляет СВОЁ (бонус
            // скила оружия), поэтому сверяется отдельно, не «в лоб».
            int expectedAcc = snap.GetInt(StatKeys.Of(DerivedStat.Accuracy))
                + snap.Skill(SkillType.Ranged) * cfg.Combat.AccuracyPerWeaponSkill;
            Assert.AreEqual(expectedAcc, unit.Profile.Accuracy);
        }

        [Test]
        public void FromCompanion_NoWeapon_SkipsWeaponSkillBonus()
        {
            var cfg = new BalanceConfig();
            var companion = Archetype().CreateInstance("t2", cfg);
            var snap = companion.Resolve(cfg);

            var unit = CombatUnit.FromCompanion(companion, null, cfg);
            Assert.AreEqual(snap.GetInt(StatKeys.Of(DerivedStat.Accuracy)), unit.Profile.Accuracy);
        }

        [Test]
        public void FromCompanion_CanBeDowned_AndCarriesSourceCompanionId()
        {
            var cfg = new BalanceConfig();
            var companion = Archetype().CreateInstance("t3", cfg);
            var unit = CombatUnit.FromCompanion(companion, Bow(), cfg);

            Assert.IsTrue(unit.Profile.CanBeDowned);
            Assert.AreEqual(Side.Player, unit.Side);
            Assert.AreEqual("t3", unit.SourceCompanionId);
        }

        [Test]
        public void FromCompanion_ProtectedFromDeath_IsExternalParameter()
        {
            var cfg = new BalanceConfig();
            var companion = Archetype().CreateInstance("t4", cfg);

            Assert.IsFalse(CombatUnit.FromCompanion(companion, Bow(), cfg).Profile.ProtectedFromDeath);
            Assert.IsTrue(CombatUnit.FromCompanion(companion, Bow(), cfg, protectedFromDeath: true).Profile.ProtectedFromDeath);
        }

        [Test]
        public void FromDefector_BuildsEnemySideUnit_CannotBeDowned_KeepsSourceId()
        {
            var cfg = new BalanceConfig();
            var companion = Archetype().CreateInstance("defector1", cfg);
            var unit = CombatUnit.FromDefector(companion, Bow(), cfg);

            Assert.AreEqual(Side.Enemy, unit.Side, "зрадник б'ється на стороні ворога (R8)");
            Assert.IsFalse(unit.Profile.CanBeDowned, "перебежчик умирает насовсем — гир возвращается убийством");
            Assert.AreEqual("defector1", unit.SourceCompanionId);
            Assert.AreEqual("defector_defector1", unit.Id);
        }

        [Test]
        public void FromDefector_UsesSameAggregatorAsFromCompanion()
        {
            var cfg = new BalanceConfig();
            var source = Archetype().CreateInstance("defector2", cfg);
            var asCompanion = CombatUnit.FromCompanion(source, Bow(), cfg);
            var asDefector = CombatUnit.FromDefector(source, Bow(), cfg);

            // Тот же боевой профиль — симметрия правил (перебежчик дерётся теми
            // же формулами, что и напарник, просто по другую сторону поля).
            Assert.AreEqual(asCompanion.Profile.MaxHp, asDefector.Profile.MaxHp);
            Assert.AreEqual(asCompanion.Profile.Accuracy, asDefector.Profile.Accuracy);
            Assert.AreEqual(asCompanion.Profile.Armor, asDefector.Profile.Armor);
        }

        [Test]
        public void AbilityCatalog_GatesBySkillLevel_ForCompanion()
        {
            var cfg = new BalanceConfig();
            var archetype = Archetype()
                .SetSkill(SkillType.Tactics, 5)   // ≥4 — знает MoveOrder
                .SetSkill(SkillType.Survival, 1); // <4 — SetTrap недоступен
            var companion = archetype.CreateInstance("gated", cfg);

            var unit = CombatUnit.FromCompanion(companion, Bow(), cfg, abilityCatalog: DefaultCombatContent.AbilityCatalog());

            Assert.IsNotNull(unit.FindAbility("ability.move_order"), "Тактика 5 ≥ порога 4 — способность известна");
            Assert.IsNull(unit.FindAbility("ability.set_trap"), "Выживание 1 < порога 4 — способность НЕ известна");
        }

        [Test]
        public void FromEnemy_BuildsFromDefinition_WithAbilities()
        {
            var def = DefaultCombatContent.TuharBoyar();
            var unit = CombatUnit.FromEnemy(def, "boyar_1");

            Assert.AreEqual(Side.Enemy, unit.Side);
            Assert.AreEqual(def.MaxHp, unit.Profile.MaxHp);
            Assert.AreEqual(def.Weapon.Id, unit.Weapon.Id);
            Assert.IsNotNull(unit.FindAbility("ability.lunge"));
            Assert.IsNull(unit.SourceCompanionId, "рядовой враг не связан с ростером");
        }

        [Test]
        public void EnemyCatalog_HasFourDistinctEntries_KeyedById()
        {
            var catalog = DefaultCombatContent.EnemyCatalog();
            Assert.AreEqual(4, catalog.Count);
            Assert.IsTrue(catalog.ContainsKey("enemy.horde_scout"));
            Assert.IsTrue(catalog.ContainsKey("enemy.horde_skirmisher"));
            Assert.IsTrue(catalog.ContainsKey("enemy.tuhar_boyar"));
            Assert.IsTrue(catalog.ContainsKey("enemy.burunda"));
        }
    }
}
