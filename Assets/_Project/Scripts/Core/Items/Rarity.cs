namespace Game.Core.Items
{
    /// <summary>
    /// Рідкість предмета (Епік 6.1, Варіант Б): окремих афіксів немає — у
    /// звичайного дропу є розкид статів бази, а рідкість МНОЖИТЬ магнітуду.
    /// Іменні предмети рідкість не котять (фіксовані, US-6.1) і не
    /// масштабуються нею — див. <see cref="ItemInstance"/>.
    /// </summary>
    public enum Rarity
    {
        Common = 0,
        Uncommon = 1,
        Rare = 2,
        Epic = 3
    }

    /// <summary>
    /// Числа рідкості. ПЛЕЙСХОЛДЕР (як і решта чисел білансу першого зрізу,
    /// §9.2 специфікації тестової збірки): формула, а не таблиця в
    /// <see cref="Balance.ItemBalance"/> — тут немає окремого «власника»
    /// числа, яке потребує підкрутки без перекомпіляції (на відміну від
    /// вартості крафту), тож SO-обгортку відкладено разом з рештою R14.
    /// </summary>
    public static class RarityTuning
    {
        /// <summary>Множник магнітуди роллів за рідкістю: 1.0 / 1.5 / 2.0 / 2.5.</summary>
        public static double MagnitudeMultiplier(Rarity rarity) => 1.0 + 0.5 * (int)rarity;

        /// <summary>Наступна рідкість для крафт-апгрейду; на Epic — стеля.</summary>
        public static Rarity Next(Rarity rarity) => rarity < Rarity.Epic ? rarity + 1 : Rarity.Epic;
    }
}
