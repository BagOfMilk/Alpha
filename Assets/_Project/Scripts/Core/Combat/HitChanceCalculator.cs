using Game.Core.Balance;

namespace Game.Core.Combat
{
    /// <summary>
    /// Надёжный % / порог:
    ///   число = точность (скил/оружие) − защита цели − укрытие (полу −20 / полное −40)
    ///           − дистанция за оптималом − Подавление [+ Метка]; кламп в границы
    ///           CombatBalance.HitChanceMin..HitChanceMax.
    ///
    /// ОДНА формула кормит ОБА правила попадания (R1): PercentRule читает
    /// результат как %, ThresholdRule — как margin-от-равновесия (см.
    /// ThresholdRule.Resolve). Сам этот класс броском кубика не занимается —
    /// это дело IHitRule/IDiceRoller.
    /// </summary>
    public static class HitChanceCalculator
    {
        /// <summary>Низкоуровневая чистая формула — для тестов и предпросмотра в UI.</summary>
        public static int Compute(int attackerAccuracy, bool attackerSuppressed,
                                  int targetDefense, CoverType cover, bool ignoreCover,
                                  int distance, int optimalRange, BalanceConfig cfg,
                                  bool targetMarked = false, bool targetKnockedDown = false,
                                  int accuracyBonus = 0)
        {
            var c = cfg.Combat;

            // Сбит с ног — лёгкая цель: защита проседает (не ниже нуля).
            if (targetKnockedDown)
                targetDefense = System.Math.Max(0, targetDefense - c.KnockdownDefensePenalty);

            int chance = attackerAccuracy + accuracyBonus - targetDefense;

            if (targetMarked) chance += c.MarkedHitBonus;

            if (!ignoreCover)
            {
                if (cover == CoverType.Half) chance -= c.CoverHalfHitPenalty;
                else if (cover == CoverType.Full) chance -= c.CoverFullHitPenalty;
            }

            int beyond = distance - optimalRange;
            if (beyond > 0) chance -= beyond * c.DistancePenaltyPerTile;

            if (attackerSuppressed) chance -= c.SuppressionAccuracyPenalty;

            if (chance < c.HitChanceMin) chance = c.HitChanceMin;
            if (chance > c.HitChanceMax) chance = c.HitChanceMax;
            return chance;
        }

        /// <summary>Число юнита по юниту на карте текущим оружием (+бонус точности от способности).</summary>
        public static int Compute(CombatUnit attacker, CombatUnit target, GridMap map,
                                  BalanceConfig cfg, int accuracyBonus = 0)
        {
            var w = attacker.Weapon;
            if (w == null) return 0;
            var cover = map != null ? map.CoverAgainst(target.Pos, attacker.Pos) : CoverType.None;
            int distance = GridPos.Chebyshev(attacker.Pos, target.Pos);
            return Compute(attacker.Profile.Accuracy, attacker.HasStatus(StatusType.Suppressed),
                           target.Profile.Defense, cover, w.IsMelee,
                           distance, w.OptimalRange, cfg,
                           target.HasStatus(StatusType.Marked), target.HasStatus(StatusType.KnockedDown),
                           accuracyBonus);
        }
    }
}
