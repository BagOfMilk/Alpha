using System;
using Game.Core.Balance;
using Game.Core.Randomness;

namespace Game.Core.Combat
{
    /// <summary>
    /// R1: правило влучання «показаний %», потрібен впроваджений IDiceRoller.
    /// Логіка Miss/Graze/Hit повторює перевірену архівну (промах у
    /// межах гризу дає частковий урон; при шансі ≥HighHitNoFullMiss
    /// повністю злитись не можна — найгірше Graze), а Crit — НОВА смуга
    /// поверх Hit, вирішується ДРУГИМ кидком того самого roller (той самий порт,
    /// той самий принцип «весь рандом — через IDiceRoller»), а не окремим
    /// System.Random чи вбудованим IRng, як було в архіві.
    /// </summary>
    public sealed class PercentRule : IHitRule
    {
        private readonly BalanceConfig _cfg;

        public PercentRule(BalanceConfig cfg)
        {
            _cfg = cfg ?? new BalanceConfig();
        }

        public AttackOutcome Resolve(CombatUnit attacker, CombatUnit target, int shownChanceOrThreshold, IDiceRoller roller)
        {
            if (roller == null) throw new ArgumentNullException(nameof(roller));
            var c = _cfg.Combat;

            double roll = roller.Roll01(StreamId(attacker, target, "hit")) * 100.0;
            if (roll < shownChanceOrThreshold)
            {
                double critRoll = roller.Roll01(StreamId(attacker, target, "crit")) * 100.0;
                int critChance = attacker != null ? attacker.Profile.CritChance : 0;
                return critRoll < critChance ? AttackOutcome.Crit : AttackOutcome.Hit;
            }

            double overshoot = roll - shownChanceOrThreshold;
            if (overshoot <= c.GrazeThresholdPercent) return AttackOutcome.Graze;
            if (shownChanceOrThreshold >= c.HighHitNoFullMiss) return AttackOutcome.Graze;
            return AttackOutcome.Miss;
        }

        private static string StreamId(CombatUnit attacker, CombatUnit target, string tag)
            => (attacker?.Id ?? "?") + ">" + (target?.Id ?? "?") + ":" + tag;
    }
}
