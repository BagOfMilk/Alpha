using Game.Core.Balance;
using Game.Core.Randomness;

namespace Game.Core.Combat
{
    /// <summary>
    /// R1: правило попадания «точность ≥ показанный порог», полностью
    /// детерминированное. IDiceRoller НЕ вызывается ни разу — параметр
    /// присутствует только чтобы правило подходило под интерфейс IHitRule.
    ///
    /// shownChanceOrThreshold трактуется как margin-от-равновесия: margin =
    /// shown − ThresholdBaseline. Отрицательный margin — промах; дальше —
    /// две полосы ширины ThresholdGrazeBand/ThresholdCritBand (обе из
    /// CombatBalance, крутятся балансом без перекомпиляции). Показанное
    /// игроку число считает тот же HitChanceCalculator.Compute, что и для
    /// PercentRule — порог не отдельная формула, а то же самое надёжное
    /// число, прочитанное иначе.
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
