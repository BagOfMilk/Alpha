using System;
using Game.Core.Balance;

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
    /// Конвейер урона (US-3.4/3.12), порядок зафиксирован:
    ///   ролл (крит = max + бонус) → × множитель типа (резист/уязвимость цели)
    ///   → − плоская броня (эффективная с Шредом, минус пробитие) → граза ×50% → не ниже 0.
    /// Две независимые оси: броня — флэт; тип урона — множитель. DoT обходят броню
    /// (в мелких числах флэт-броня обнуляла бы тик), но множатся типом.
    /// </summary>
    public static class DamageResolver
    {
        /// <summary>
        /// Урон атаки по цели. Крит роллится только на полном попадании (граза — нет);
        /// порядок обращений к RNG: 1) d100 крита (не на гразе), 2) ролл урона min..max.
        /// </summary>
        public static DamageReport RollAttackDamage(CombatUnit attacker, CombatUnit target,
                                                    WeaponDefinition w, bool graze,
                                                    IRng rng, BalanceConfig cfg)
        {
            bool crit = false;
            int damage;
            if (!graze && rng.D100() <= attacker.Profile.CritChance)
            {
                crit = true;
                damage = w.DamageMax + w.CritDamageBonus;
            }
            else
            {
                damage = rng.Range(w.DamageMin, w.DamageMax);
            }

            damage = ApplyTypeMultiplier(damage, w.Damage, target);

            int armor = Math.Max(0, target.EffectiveArmor - w.ArmorPierce);
            damage -= armor;

            if (graze)
                damage = (int)Math.Round(damage * (cfg.GrazePartialPercent / 100.0));

            return new DamageReport(Math.Max(0, damage), crit);
        }

        /// <summary>Тик DoT: броню обходит, множитель типа применяется (Кровотечение — True, без множителя).</summary>
        public static int DotTick(int baseDamage, DamageType type, CombatUnit target)
            => Math.Max(0, ApplyTypeMultiplier(baseDamage, type, target));

        /// <summary>Фикс урон способности/ловушки: множитель типа + эффективная броня (без крита/гразы).</summary>
        public static int FlatDamage(int baseDamage, DamageType type, CombatUnit target)
            => Math.Max(0, ApplyTypeMultiplier(baseDamage, type, target) - target.EffectiveArmor);

        private static int ApplyTypeMultiplier(int damage, DamageType type, CombatUnit target)
            => (int)Math.Round(damage * target.Profile.Resists.Multiplier(type));
    }
}
