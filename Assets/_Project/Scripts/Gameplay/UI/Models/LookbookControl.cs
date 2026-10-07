namespace Game.Gameplay.UI
{
    /// <summary>
    /// Керування прев'ю моделі з автотуру «лукбук» (<c>-autoplay-lookbook</c>): кут повороту, який прев'ю
    /// бере замість миші, поки він заданий. Чистий C#, щоб водій туру (лінтується) міг керувати станком
    /// (не лінтується) без посилання на його тип.
    /// </summary>
    public static class LookbookControl
    {
        /// <summary>Кут повороту моделі на прев'ю (°); null — як зазвичай, мишею.</summary>
        public static float? Yaw { get; set; }
    }
}
