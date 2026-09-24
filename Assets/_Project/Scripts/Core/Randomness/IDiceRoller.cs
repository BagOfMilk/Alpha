namespace Game.Core.Randomness
{
    /// <summary>
    /// Единственный порт случайности, который Core разрешает себе знать (R1).
    ///
    /// В самом Core нет ни одной реализации: ArchitectureGuardTests
    /// (Core_NoTypeImplementsIDiceRoller) держит это рефлексией по сборке.
    /// Настоящая сидированная реализация (SeededDiceRoller) — чистый C# в
    /// Game.Gameplay, подключённый к headless-инструментам прямым
    /// &lt;Compile Include&gt;, как SceneText.cs/SignalText.cs. В режиме
    /// ThresholdRule этот интерфейс не вызывается вовсе — бой полностью
    /// детерминирован без него.
    /// </summary>
    public interface IDiceRoller
    {
        /// <summary>
        /// Равномерное число в [0,1). streamId называет источник ролла
        /// (например "hit:atk>tgt" или "dmg:atk>tgt") — не для отдельного
        /// потока состояния (состояние одно, см. CaptureState), а чтобы
        /// разные ролы одной атаки не совпадали по значению один-в-один.
        /// </summary>
        double Roll01(string streamId);

        /// <summary>Снимок состояния потока — строкой, чтобы легко лечь в сейв-блоб.</summary>
        string CaptureState();

        /// <summary>Восстановление потока: загрузка ПРОДОЛЖАЕТ поток случайностей, а не перезапускает его.</summary>
        void RestoreState(string blob);
    }
}
