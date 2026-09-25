using Game.Core.Balance;
using Game.Core.Combat;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Конвеєр шкоди: значення за AttackOutcome → +DamageBonus → множник типу →
    /// плоска броня (пробиття/Шред) → граза. DoT обходять броню. Перенесено з
    /// архівної бойової лінії, адаптовано під AttackOutcome/IDiceRoller (R1) —
    /// «чи влучив атакуючий» вирішує IHitRule зовні, цей клас вирішує тільки «скільки».
    /// </summary>
    public class DamageResolverTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();

        private static CombatUnit Unit(int armor = 0, ResistProfile resists = null, int damageBonus = 0)
        {
            var p = new UnitProfile
            {
                DisplayName = "Т", MaxHp = 10, MaxAp = 8, Accuracy = 70,
                Armor = armor, Resists = resists ?? new ResistProfile(), DamageBonus = damageBonus
            };
            return new CombatUnit("u" + System.Guid.NewGuid().ToString("N"), Side.Enemy, p, null);
        }

        private static WeaponDefinition Rifle(int pierce = 0) => new WeaponDefinition("w", "W", SkillType.Ranged)
        {
            Damage = DamageType.Ballistic, DamageMin = 3, DamageMax = 5, CritDamageBonus = 2, ArmorPierce = pierce
        };

        [Test]
        public void Miss_DealsNoDamage_NoRollerCall()
        {
            var roller = new ScriptedDiceRoller();
            var report = DamageResolver.RollAttackDamage(Unit(), Unit(), Rifle(), AttackOutcome.Miss,
                roller, deterministic: false, cfg: Cfg);
            Assert.AreEqual(0, report.Amount);
            Assert.IsFalse(report.Crit);
            Assert.AreEqual(0, roller.Streams.Count);
        }

        [Test]
        public void Percent_Hit_RollsWithinRange_MinusArmor()
        {
            // roll=0.5 -> min(3) + round(0.5*(5-3)) = 3+1 = 4; мінус броня 1 = 3.
            var report = DamageResolver.RollAttackDamage(Unit(), Unit(armor: 1), Rifle(), AttackOutcome.Hit,
                new ScriptedDiceRoller(0.5), deterministic: false, cfg: Cfg);
            Assert.AreEqual(3, report.Amount);
            Assert.IsFalse(report.Crit);
        }

        [Test]
        public void Threshold_Hit_IsFixedMidpoint_NoRollerCall()
        {
            // Детермінований режим: середина діапазону (3+5)/2=4, мінус броня 1 = 3.
            var roller = new ScriptedDiceRoller();
            var report = DamageResolver.RollAttackDamage(Unit(), Unit(armor: 1), Rifle(), AttackOutcome.Hit,
                roller, deterministic: true, cfg: Cfg);
            Assert.AreEqual(3, report.Amount);
            Assert.AreEqual(0, roller.Streams.Count, "детерминированный режим не должен трогать кубик");
        }

        [Test]
        public void Pierce_IgnoresPartOfArmor()
        {
            var report = DamageResolver.RollAttackDamage(Unit(), Unit(armor: 2), Rifle(pierce: 1), AttackOutcome.Hit,
                null, deterministic: true, cfg: Cfg);
            Assert.AreEqual(3, report.Amount); // (3+5)/2=4, броня 2 − пробиття 1 = 1
        }

        [Test]
        public void Shred_LowersEffectiveArmor()
        {
            var target = Unit(armor: 2);
            target.ArmorShred = 1;
            var report = DamageResolver.RollAttackDamage(Unit(), target, Rifle(), AttackOutcome.Hit,
                null, deterministic: true, cfg: Cfg);
            Assert.AreEqual(3, report.Amount); // ефективна броня 1
        }

        [Test]
        public void Vulnerability_MultipliesBeforeArmor()
        {
            var target = Unit(armor: 1, resists: new ResistProfile().With(DamageType.Ballistic, 1.5));
            var report = DamageResolver.RollAttackDamage(Unit(), target, Rifle(), AttackOutcome.Hit,
                null, deterministic: true, cfg: Cfg);
            Assert.AreEqual(5, report.Amount); // round(4×1.5)=6 → −1 броні
        }

        [Test]
        public void Resist_ReducesDamage()
        {
            var target = Unit(resists: new ResistProfile().With(DamageType.Ballistic, 0.5));
            var report = DamageResolver.RollAttackDamage(Unit(), target, Rifle(), AttackOutcome.Hit,
                null, deterministic: true, cfg: Cfg);
            Assert.AreEqual(2, report.Amount); // round(4×0.5)=2
        }

        [Test]
        public void DamageBonus_AddsBeforeTypeMultiplier()
        {
            var attacker = Unit(damageBonus: 3);
            var report = DamageResolver.RollAttackDamage(attacker, Unit(), Rifle(), AttackOutcome.Hit,
                null, deterministic: true, cfg: Cfg);
            Assert.AreEqual(7, report.Amount); // (4+3) − 0 броні
        }

        [Test]
        public void Crit_UsesMaxPlusBonus_NoRollerCall()
        {
            var roller = new ScriptedDiceRoller();
            var report = DamageResolver.RollAttackDamage(Unit(), Unit(), Rifle(), AttackOutcome.Crit,
                roller, deterministic: false, cfg: Cfg);
            Assert.IsTrue(report.Crit);
            Assert.AreEqual(7, report.Amount); // 5 + 2
            Assert.AreEqual(0, roller.Streams.Count, "крит фиксирован — не участвует ни один ролл диапазона");
        }

        [Test]
        public void Graze_HalvesAfterArmor()
        {
            var report = DamageResolver.RollAttackDamage(Unit(), Unit(armor: 2), Rifle(), AttackOutcome.Graze,
                null, deterministic: true, cfg: Cfg);
            Assert.IsFalse(report.Crit);
            Assert.AreEqual(1, report.Amount); // (4−2) × 50%
        }

        [Test]
        public void Damage_NeverNegative()
        {
            var report = DamageResolver.RollAttackDamage(Unit(), Unit(armor: 10), Rifle(), AttackOutcome.Hit,
                null, deterministic: true, cfg: Cfg);
            Assert.AreEqual(0, report.Amount);
        }

        [Test]
        public void Dot_BypassesArmor_AppliesTypeMultiplier()
        {
            var target = Unit(armor: 5, resists: new ResistProfile().With(DamageType.Fire, 1.5));
            Assert.AreEqual(3, DamageResolver.DotTick(2, DamageType.Fire, target)); // броня не враховується
            Assert.AreEqual(2, DamageResolver.DotTick(2, DamageType.True, target)); // True — без множників
        }

        [Test]
        public void FlatDamage_SubtractsArmor_NoRoll()
        {
            var target = Unit(armor: 2);
            Assert.AreEqual(1, DamageResolver.FlatDamage(3, DamageType.True, target));
        }
    }
}
