using Game.Core.Balance;

namespace Game.Core.Combat
{
    /// <summary>
    /// Надійний % / поріг:
    ///   число = точність (скіл/зброя) − захист цілі − укриття (напів −20 / повне −40)
    ///           − дистанція за оптималом − Придушення [+ Мітка]; клемп у межі
    ///           CombatBalance.HitChanceMin..HitChanceMax.
    ///
    /// ОДНА формула годує ОБИДВА правила влучання (R1): PercentRule читає
    /// результат як %, ThresholdRule — як margin-від-рівноваги (див.
    /// ThresholdRule.Resolve). Сам цей клас киданням кубика не займається —
    /// це справа IHitRule/IDiceRoller.
    /// </summary>
    public static class HitChanceCalculator
    {
        /// <summary>Низькорівнева чиста формула — для тестів і передперегляду в UI.</summary>
        public static int Compute(int attackerAccuracy, bool attackerSuppressed,
                                  int targetDefense, CoverType cover, bool ignoreCover,
                                  int distance, int optimalRange, BalanceConfig cfg,
                                  bool targetMarked = false, bool targetKnockedDown = false,
                                  int accuracyBonus = 0)
        {
            var c = cfg.Combat;

            // Збитий з ніг — легка ціль: захист просідає (не нижче нуля).
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

        /// <summary>Число юніта по юніту на карті поточною зброєю (+бонус точності від здібності).</summary>
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
