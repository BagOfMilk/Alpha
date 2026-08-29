namespace Game.Core.Signals
{
    /// <summary>
    /// Визуальное состояние города. Два НЕЗАВИСИМЫХ числа, а не одно: богатый и
    /// напряжённый город должен выглядеть иначе, чем бедный и спокойный —
    /// одной шкалой это не показать.
    /// </summary>
    public readonly struct MoodboardState
    {
        /// <summary>Процветание 0..4: стройки, людность, ухоженность.</summary>
        public readonly int Prosperity;
        /// <summary>Упадок 0..4: заколоченное, мусор, баррикады.</summary>
        public readonly int Decay;
        /// <summary>Точечные наложения: «баррикады», «палатки», «граффити:X».</summary>
        public readonly string[] Overlays;

        public MoodboardState(int prosperity, int decay, string[] overlays)
        {
            Prosperity = prosperity;
            Decay = decay;
            Overlays = overlays ?? System.Array.Empty<string>();
        }
    }
}
