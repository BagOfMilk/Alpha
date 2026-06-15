namespace Game.Core.Items
{
    /// <summary>
    /// Редкость дропа (Эпик 6.1, Вариант Б): отдельных аффиксов НЕТ — у дропа разброс
    /// статов базы, а редкость МНОЖИТ магнитуду роллов. Именные предметы редкость не
    /// катают (фиксированы).
    /// </summary>
    public enum Rarity
    {
        Common = 0,
        Uncommon = 1,
        Rare = 2,
        Epic = 3
    }

    public static class RarityTuning
    {
        /// <summary>Множитель магнитуды роллов по редкости (ПЛЕЙСХОЛДЕР): 1.0 / 1.5 / 2.0 / 2.5.</summary>
        public static double MagnitudeMultiplier(Rarity rarity) => 1.0 + 0.5 * (int)rarity;

        public static Rarity Next(Rarity rarity) => rarity < Rarity.Epic ? rarity + 1 : Rarity.Epic;
    }
}
