using Game.Core.Balance;
using Game.Core.Randomness;

namespace Game.Core.Combat
{
    /// <summary>
    /// R1: правило влучання «точність ≥ показаний поріг», повністю
    /// детерміноване. IDiceRoller НЕ викликається жодного разу — параметр
    /// присутній лише щоб правило підходило під інтерфейс IHitRule.
    ///
    /// shownChanceOrThreshold трактується як margin-від-рівноваги: margin =
    /// shown − ThresholdBaseline. Від'ємний margin — промах; далі —
    /// дві смуги шириною ThresholdGrazeBand/ThresholdCritBand (обидві з
    /// CombatBalance, крутяться балансом без перекомпіляції). Показане
    /// гравцю число рахує той самий HitChanceCalculator.Compute, що й для
    /// PercentRule — поріг не окрема формула, а те саме надійне
    /// число, прочитане інакше.
    /// </summary>
    public sealed class ThresholdRule : IHitRule
    {
        private readonly BalanceConfig _cfg;

        public ThresholdRule(BalanceConfig cfg)
        {
            _cfg = cfg ?? new BalanceConfig();
        }

        public AttackOutcome Resolve(CombatUnit attacker, CombatUnit target, int shownChanceOrThreshold, IDiceRoller roller)
        {
            var c = _cfg.Combat;
            int margin = shownChanceOrThreshold - c.ThresholdBaseline;

            if (margin < 0) return AttackOutcome.Miss;
            if (margin < c.ThresholdGrazeBand) return AttackOutcome.Graze;
            if (margin < c.ThresholdCritBand) return AttackOutcome.Hit;
            return AttackOutcome.Crit;
        }
    }
}
