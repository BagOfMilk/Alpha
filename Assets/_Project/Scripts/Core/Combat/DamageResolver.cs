using System;
using Game.Core.Balance;
using Game.Core.Randomness;

namespace Game.Core.Combat
{
    /// <summary>Результат расчёта урона одной атаки.</summary>
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
    /// Конвейер урона, порядок зафиксирован:
    ///   значение по AttackOutcome (крит = max+бонус; детерминированный Hit =
    ///   середина диапазона; PercentRule Hit = ролл через IDiceRoller)
    ///   → + DamageBonus атакующего (производная статов, аудит G18)
    ///   → × множитель типа (резист/уязвимость цели)
    ///   → − плоская броня (эффективная с Шредом, минус пробитие) → граза ×доля → не ниже 0.
    ///
    /// R1: диапазон урона тоже идёт через пару правило/кидальник — в
    /// детерминированном режиме (ThresholdRule) IDiceRoller не вызывается
    /// вовсе, damage берётся фиксированным по полосе исхода.
    /// </summary>
    public static class DamageResolver
    {
        /// <summary>
        /// Урон атаки по цели. outcome уже решён IHitRule (Miss/Graze/Hit/Crit) —
        /// этот метод больше не решает, попал ли атакующий, только СКОЛЬКО урона.
        /// deterministic=true (ThresholdRule) — фиксированное значение по полосе,
        /// без единого обращения к roller; false (PercentRule) — ролл диапазона.
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
                // Threshold-режим: полоса исхода задаёт фикс-значение, без броска (R1).
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

        /// <summary>Тик DoT: броню обходит, множитель типа применяется (Кровотечение — True, без множителя).</summary>
        public static int DotTick(int baseDamage, DamageType type, CombatUnit target)
            => Math.Max(0, ApplyTypeMultiplier(baseDamage, type, target));

        /// <summary>Фикс урон способности/ловушки: множитель типа + эффективная броня (без крита/гразы/ролла).</summary>
        public static int FlatDamage(int baseDamage, DamageType type, CombatUnit target)
            => Math.Max(0, ApplyTypeMultiplier(baseDamage, type, target) - target.EffectiveArmor);

        private static int ApplyTypeMultiplier(int damage, DamageType type, CombatUnit target)
            => (int)Math.Round(damage * target.Profile.Resists.Multiplier(type));

        private static string StreamId(CombatUnit attacker, CombatUnit target)
            => (attacker?.Id ?? "?") + ">" + (target?.Id ?? "?");
    }
}
