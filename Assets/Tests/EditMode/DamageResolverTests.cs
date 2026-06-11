using Game.Core.Balance;
using Game.Core.Combat;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Конвейер урона: ролл/крит → множитель типа → плоская броня (пробитие/Шред) →
    /// граза. DoT обходят броню. Порядок обращений к RNG: крит d100 (не на гразе),
    /// затем ролл урона min..max.
    /// </summary>
    public class DamageResolverTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();

        private static CombatUnit Unit(int armor = 0, int crit = 0, ResistProfile resists = null)
        {
            var p = new UnitProfile
            {
                DisplayName = "Т", MaxHp = 10, MaxAp = 8, Accuracy = 70, CritChance = crit,
                Armor = armor, Resists = resists ?? new ResistProfile()
            };
            return new CombatUnit("u" + System.Guid.NewGuid().ToString("N"), Side.Enemy, p, null);
        }

        private static WeaponDefinition Rifle(int pierce = 0) => new WeaponDefinition("w", "W", SkillType.Ranged)
        {
            Damage = DamageType.Ballistic, DamageMin = 3, DamageMax = 5, CritDamageBonus = 2, ArmorPierce = pierce
        };

        [Test]
        public void Hit_RollMinusArmor()
        {
            var report = DamageResolver.RollAttackDamage(Unit(), Unit(armor: 1), Rifle(),
                graze: false, new ScriptedRng(100, 4), Cfg); // нет крита, ролл 4
            Assert.AreEqual(3, report.Amount);
            Assert.IsFalse(report.Crit);
        }

        [Test]
        public void Pierce_IgnoresPartOfArmor()
        {
            var report = DamageResolver.RollAttackDamage(Unit(), Unit(armor: 2), Rifle(pierce: 1),
                graze: false, new ScriptedRng(100, 4), Cfg);
            Assert.AreEqual(3, report.Amount); // броня 2 − пробитие 1 = 1
        }

        [Test]
        public void Shred_LowersEffectiveArmor()
        {
            var target = Unit(armor: 2);
            target.ArmorShred = 1;
            var report = DamageResolver.RollAttackDamage(Unit(), target, Rifle(),
                graze: false, new ScriptedRng(100, 4), Cfg);
            Assert.AreEqual(3, report.Amount); // эффективная броня 1
        }

        [Test]
        public void Vulnerability_MultipliesBeforeArmor()
        {
            var target = Unit(armor: 1, resists: new ResistProfile().With(DamageType.Ballistic, 1.5));
            var report = DamageResolver.RollAttackDamage(Unit(), target, Rifle(),
                graze: false, new ScriptedRng(100, 4), Cfg);
            Assert.AreEqual(5, report.Amount); // round(4×1.5)=6 → −1 брони
        }

        [Test]
        public void Resist_ReducesDamage()
        {
            var target = Unit(resists: new ResistProfile().With(DamageType.Ballistic, 0.5));
            var report = DamageResolver.RollAttackDamage(Unit(), target, Rifle(),
                graze: false, new ScriptedRng(100, 4), Cfg);
            Assert.AreEqual(2, report.Amount);
        }

        [Test]
        public void Crit_UsesMaxPlusBonus_NoDamageRoll()
        {
            var report = DamageResolver.RollAttackDamage(Unit(crit: 100), Unit(), Rifle(),
                graze: false, new ScriptedRng(1), Cfg); // d100=1 ≤ 100 → крит, ролла урона нет
            Assert.IsTrue(report.Crit);
            Assert.AreEqual(7, report.Amount); // 5 + 2
        }

        [Test]
        public void Graze_HalvesAfterArmor_NoCritRoll()
        {
            var report = DamageResolver.RollAttackDamage(Unit(crit: 100), Unit(armor: 2), Rifle(),
                graze: true, new ScriptedRng(4), Cfg); // сразу ролл урона: крита на гразе нет
            Assert.IsFalse(report.Crit);
            Assert.AreEqual(1, report.Amount); // (4−2) × 50%
        }

        [Test]
        public void Damage_NeverNegative()
        {
            var report = DamageResolver.RollAttackDamage(Unit(), Unit(armor: 10), Rifle(),
                graze: false, new ScriptedRng(100, 3), Cfg);
            Assert.AreEqual(0, report.Amount);
        }

        [Test]
        public void Dot_BypassesArmor_AppliesTypeMultiplier()
        {
            var target = Unit(armor: 5, resists: new ResistProfile().With(DamageType.Fire, 1.5));
            Assert.AreEqual(3, DamageResolver.DotTick(2, DamageType.Fire, target)); // броня не считается
            Assert.AreEqual(2, DamageResolver.DotTick(2, DamageType.True, target)); // True — без множителей
        }
    }
}
