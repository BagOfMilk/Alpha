using System;
using Game.Core.Balance;
using Game.Core.Randomness;

namespace Game.Core.Combat
{
    /// <summary>Результат розрахунку урону однієї атаки.</summary>
    public readonly struct DamageReport
    {
        public readonly int Amount;
        public readonly bool Crit;

        public DamageReport(int amount, bool crit)
        {
            Amount = amount;
            Crit = crit;
        }
    }

    /// <summary>
    /// Конвеєр урону, порядок зафіксований:
    ///   значення за AttackOutcome (крит = max+бонус; детермінований Hit =
    ///   середина діапазону; PercentRule Hit = рол через IDiceRoller)
    ///   → + DamageBonus атакуючого (похідна статів, аудит G18)
    ///   → × множник типу (резист/вразливість цілі)
    ///   → − плоска броня (ефективна з Шредом, мінус пробиття) → граза ×частка → не нижче 0.
    ///
    /// R1: діапазон урону теж іде через пару правило/кидальник — у
    /// детермінованому режимі (ThresholdRule) IDiceRoller не викликається
    /// зовсім, damage береться фіксованим за полосою наслідку.
    /// </summary>
    public static class DamageResolver
    {
        /// <summary>
        /// Урон атаки по цілі. outcome вже вирішений IHitRule (Miss/Graze/Hit/Crit) —
        /// цей метод більше не вирішує, чи влучив атакуючий, тільки СКІЛЬКИ урону.
        /// deterministic=true (ThresholdRule) — фіксоване значення за полосою,
        /// без жодного звернення до roller; false (PercentRule) — рол діапазону.
        /// </summary>
        public static DamageReport RollAttackDamage(CombatUnit attacker, CombatUnit target,
                                                    WeaponDefinition w, AttackOutcome outcome,
                                                    IDiceRoller roller, bool deterministic, BalanceConfig cfg)
        {
            if (outcome == AttackOutcome.Miss) return new DamageReport(0, false);

            bool crit = outcome == AttackOutcome.Crit;
            int damage;
            if (crit)
            {
                damage = w.DamageMax + w.CritDamageBonus;
            }
            else if (deterministic || roller == null)
            {
                // Threshold-режим: полоса наслідку задає фікс-значення, без кидка (R1).
                damage = (int)Math.Round((w.DamageMin + w.DamageMax) / 2.0, MidpointRounding.AwayFromZero);
            }
            else
            {
                double roll = roller.Roll01(StreamId(attacker, target) + ":dmg");
                damage = w.DamageMin + (int)Math.Round(roll * (w.DamageMax - w.DamageMin));
            }

            damage += attacker != null ? attacker.Profile.DamageBonus : 0;
            damage = ApplyTypeMultiplier(damage, w.Damage, target);

            int armor = Math.Max(0, target.EffectiveArmor - w.ArmorPierce);
            damage -= armor;

            if (outcome == AttackOutcome.Graze)
                damage = (int)Math.Round(damage * (cfg.Combat.GrazePartialPercent / 100.0));

            return new DamageReport(Math.Max(0, damage), crit);
        }

        /// <summary>Тик DoT: броню обходить, множник типу застосовується (Кровотеча — True, без множника).</summary>
        public static int DotTick(int baseDamage, DamageType type, CombatUnit target)
            => Math.Max(0, ApplyTypeMultiplier(baseDamage, type, target));

        /// <summary>Фікс урон здібності/пастки: множник типу + ефективна броня (без криту/грази/ролу).</summary>
        public static int FlatDamage(int baseDamage, DamageType type, CombatUnit target)
            => Math.Max(0, ApplyTypeMultiplier(baseDamage, type, target) - target.EffectiveArmor);

        private static int ApplyTypeMultiplier(int damage, DamageType type, CombatUnit target)
            => (int)Math.Round(damage * target.Profile.Resists.Multiplier(type));

        private static string StreamId(CombatUnit attacker, CombatUnit target)
            => (attacker?.Id ?? "?") + ">" + (target?.Id ?? "?");
    }
}
