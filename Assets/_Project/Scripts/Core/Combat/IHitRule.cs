using Game.Core.Randomness;

namespace Game.Core.Combat
{
    /// <summary>
    /// Результат кидка атаки. Крит — своя смуга результату, а не окремий прапорець
    /// поверх Hit (R1): і ThresholdRule (за маржею), і PercentRule (за додатковим
    /// кидком) вирішують цю смугу самі, тим самим способом, яким вирішують
    /// Miss/Graze/Hit.
    /// </summary>
    public enum AttackOutcome
    {
        Miss = 0,
        Graze = 1,   // часткове влучання (частка урону, без криту й проків зброї)
        Hit = 2,
        Crit = 3
    }

    /// <summary>
    /// R1: єдиний порт «влучив/не влучив» у Core. Рівно дві реалізації:
    /// ThresholdRule (повністю детермінована — margin за показаним
    /// порогом, IDiceRoller не чіпає зовсім) і PercentRule (показаний %,
    /// потребує IDiceRoller). Обидві живуть у Core (самі правила — не випадковість),
    /// а кидає кубик лише впроваджений IDiceRoller (інтерфейс — теж у Core,
    /// реалізація — у Game.Gameplay). CombatState ніколи не звертається до
    /// випадковості напряму — тільки через цей порт.
    /// </summary>
    public interface IHitRule
    {
        /// <summary>
        /// shownChanceOrThreshold — те саме число, яке бачить гравець:
        /// для PercentRule це % (0..100), для ThresholdRule — поріг, який
        /// margin перевіряє відносно CombatBalance.ThresholdBaseline.
        /// Обидва числа рахує один і той самий HitChanceCalculator.Compute —
        /// різниця лише в тому, як ця реалізація його читає.
        /// </summary>
        AttackOutcome Resolve(CombatUnit attacker, CombatUnit target, int shownChanceOrThreshold, IDiceRoller roller);
    }
}
