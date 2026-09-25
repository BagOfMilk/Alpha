namespace Game.Core.Signals
{
    /// <summary>
    /// Візуальний стан міста. Два НЕЗАЛЕЖНИХ числа, а не одне: багате і
    /// напружене місто має виглядати інакше, ніж бідне і спокійне —
    /// однією шкалою це не показати.
    /// </summary>
    public readonly struct MoodboardState
    {
        /// <summary>Процвітання 0..4: стройки, людність, доглянутість.</summary>
        public readonly int Prosperity;
        /// <summary>Занепад 0..4: забите дошками, сміття, барикади.</summary>
        public readonly int Decay;
        /// <summary>Точкові накладення: «барикади», «намети», «графіті:X».</summary>
        public readonly string[] Overlays;

        public MoodboardState(int prosperity, int decay, string[] overlays)
        {
            Prosperity = prosperity;
            Decay = decay;
            Overlays = overlays ?? System.Array.Empty<string>();
        }
    }
}
