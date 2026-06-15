namespace Game.Core.Characters
{
    /// <summary>
    /// Полу-читаемая полоса лояльности (US-9.2/17.2): игроку показывается полоса и
    /// реакции, НЕ число. Низкая → риск ухода/предательства; высокая → бонусы/перки
    /// (полный слой арок/антагонистов — Эпик 9, позже).
    /// </summary>
    public enum LoyaltyBand
    {
        Resentful = 0, // на грани — риск предательства читается по репликам
        Wary = 1,
        Steady = 2,
        Devoted = 3    // предан делу/лидеру
    }

    public static class LoyaltyBands
    {
        public static LoyaltyBand Of(int loyalty)
        {
            if (loyalty < 25) return LoyaltyBand.Resentful;
            if (loyalty < 50) return LoyaltyBand.Wary;
            if (loyalty < 75) return LoyaltyBand.Steady;
            return LoyaltyBand.Devoted;
        }
    }
}
