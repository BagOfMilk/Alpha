using Game.Core.Randomness;

namespace Game.Core.Combat
{
    /// <summary>
    /// Исход броска атаки. Крит — своя полоса исхода, а не отдельный флаг поверх
    /// Hit (R1): и ThresholdRule (по марже), и PercentRule (по дополнительному
    /// броску) решают эту полосу сами, одним и тем же способом, которым решают
    /// Miss/Graze/Hit.
    /// </summary>
    public enum AttackOutcome
    {
        Miss = 0,
        Graze = 1,   // частичное попадание (доля урона, без крита и проков оружия)
        Hit = 2,
        Crit = 3
    }

    /// <summary>
    /// R1: единственный порт «попал/не попал» в Core. Ровно две реализации:
    /// ThresholdRule (полностью детерминированная — margin по показанному
    /// порогу, IDiceRoller не трогает вовсе) и PercentRule (показанный %,
    /// требует IDiceRoller). Обе живут в Core (сами правила — не случайность),
    /// а бросает кубик только внедрённый IDiceRoller (интерфейс — тоже в Core,
    /// реализация — в Game.Gameplay). CombatState никогда не обращается к
    /// случайности напрямую — только через этот порт.
    /// </summary>
    public interface IHitRule
    {
        /// <summary>
        /// shownChanceOrThreshold — то самое число, которое видит игрок:
        /// для PercentRule это % (0..100), для ThresholdRule — порог, который
        /// margin проверяет относительно CombatBalance.ThresholdBaseline.
        /// Оба числа считает один и тот же HitChanceCalculator.Compute —
        /// разница только в том, как эта реализация его читает.
        /// </summary>
        AttackOutcome Resolve(CombatUnit attacker, CombatUnit target, int shownChanceOrThreshold, IDiceRoller roller);
    }
}
