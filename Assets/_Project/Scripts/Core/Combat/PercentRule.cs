using System;
using Game.Core.Balance;
using Game.Core.Randomness;

namespace Game.Core.Combat
{
    /// <summary>
    /// R1: правило попадания «показанный %», нужен внедрённый IDiceRoller.
    /// Логика Miss/Graze/Hit повторяет проверенную архивную (промах в
    /// пределах гразы даёт частичный урон; при шансе ≥HighHitNoFullMiss
    /// полностью слиться нельзя — худшее Graze), а Crit — НОВАЯ полоса
    /// поверх Hit, решается ВТОРЫМ броском того же roller (тот же порт,
    /// тот же принцип «весь рандом — через IDiceRoller»), а не отдельным
    /// System.Random или встроенным IRng, как было в архиве.
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
