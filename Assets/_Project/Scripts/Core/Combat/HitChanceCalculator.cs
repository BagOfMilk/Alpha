using System;

namespace Game.Core.Combat
{
    /// <summary>Исход ролла атаки. Граза — частичное попадание (50% урона, без крита и проков).</summary>
    public enum HitOutcome
    {
        Miss = 0,
        Graze = 1,
        Hit = 2
    }

    /// <summary>
    /// Надёжный % попадания (US-3.3):
    /// шанс = точность (скил/оружие) − защита цели − укрытие (полу −20 / полное −40)
    ///        − дистанция за оптималом − Подавление; кламп 1..99.
    /// Ролл: промах в пределах гразы (≤15%) даёт частичный урон; при шансе ≥85%
    /// полностью слиться нельзя (худшее — граза). Ближний бой игнорирует укрытие.
    /// </summary>
    public static class HitChanceCalculator
    {
        /// <summary>Низкоуровневая чистая формула — для тестов и предпросмотра в UI.</summary>
        public static int Compute(int attackerAccuracy, bool attackerSuppressed,
                                  int targetDefense, CoverType cover, bool ignoreCover,
                                  int distance, int optimalRange, Balance.BalanceConfig cfg)
        {
            int chance = attackerAccuracy - targetDefense;

            if (!ignoreCover)
            {
                if (cover == CoverType.Half) chance -= cfg.CoverHalfHitPenalty;
                else if (cover == CoverType.Full) chance -= cfg.CoverFullHitPenalty;
            }

            int beyond = distance - optimalRange;
            if (beyond > 0) chance -= beyond * cfg.DistancePenaltyPerTile;

            if (attackerSuppressed) chance -= cfg.SuppressionAccuracyPenalty;

            if (chance < cfg.HitChanceMin) chance = cfg.HitChanceMin;
            if (chance > cfg.HitChanceMax) chance = cfg.HitChanceMax;
            return chance;
        }

        /// <summary>Шанс юнита по юниту на карте текущим оружием.</summary>
        public static int Compute(CombatUnit attacker, CombatUnit target, GridMap map, Balance.BalanceConfig cfg)
        {
            var w = attacker.Weapon;
            if (w == null) return 0;
            var cover = map != null ? map.CoverAgainst(target.Pos, attacker.Pos) : CoverType.None;
            int distance = GridPos.Chebyshev(attacker.Pos, target.Pos);
            return Compute(attacker.Profile.Accuracy, attacker.HasStatus(StatusType.Suppressed),
                           target.Profile.Defense, cover, w.IsMelee,
                           distance, w.OptimalRange, cfg);
        }

        /// <summary>Ролл d100 против шанса с правилами гразы и пола высокого шанса.</summary>
        public static HitOutcome Roll(int chance, IRng rng, Balance.BalanceConfig cfg)
        {
            int roll = rng.D100();
            if (roll <= chance) return HitOutcome.Hit;
            if (roll - chance <= cfg.GrazeThresholdPercent) return HitOutcome.Graze;
            if (chance >= cfg.HighHitNoFullMiss) return HitOutcome.Graze; // очень высокий шанс не сливается полностью
            return HitOutcome.Miss;
        }
    }
}
