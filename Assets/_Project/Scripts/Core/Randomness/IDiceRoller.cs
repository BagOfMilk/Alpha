namespace Game.Core.Randomness
{
    /// <summary>
    /// Єдиний порт випадковості, який Core дозволяє собі знати (R1).
    ///
    /// У самому Core нема жодної реалізації: ArchitectureGuardTests
    /// (Core_NoTypeImplementsIDiceRoller) тримає це рефлексією по збірці.
    /// Справжня сидована реалізація (SeededDiceRoller) — чистий C# у
    /// Game.Gameplay, підключений до headless-інструментів прямим
    /// &lt;Compile Include&gt;, як UkrainianText.cs (пакет E3b). У режимі
    /// ThresholdRule цей інтерфейс не викликається взагалі — бій повністю
    /// детермінований без нього.
    /// </summary>
    public interface IDiceRoller
    {
        /// <summary>
        /// Рівномірне число в [0,1). streamId називає джерело ролу
        /// (наприклад "hit:atk>tgt" або "dmg:atk>tgt") — не для окремого
        /// потоку стану (стан один, див. CaptureState), а щоб
        /// різні роли однієї атаки не збігалися за значенням один-в-один.
        /// </summary>
        double Roll01(string streamId);

        /// <summary>Знімок стану потоку — рядком, щоб легко лягти в сейв-блоб.</summary>
        string CaptureState();

        /// <summary>Відновлення потоку: завантаження ПРОДОВЖУЄ потік випадковостей, а не перезапускає його.</summary>
        void RestoreState(string blob);
    }
}
