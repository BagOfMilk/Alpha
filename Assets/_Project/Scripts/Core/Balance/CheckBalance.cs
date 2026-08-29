using System;

namespace Game.Core.Balance
{
    /// <summary>
    /// Числа детерминированных проверок и полос исхода.
    ///
    /// ЯКОРЬ: порог — это столько, сколько даёт середняк, вложившийся в навык на
    /// текущей стадии. Хорошая полоса — специалист, Лучшая — специалист
    /// с профильным трейтом.
    /// </summary>
    [Serializable]
    public sealed class CheckBalance
    {
        /// <summary>Запас над порогом, с которого начинается Хорошая полоса.</summary>
        public int GoodMargin = 3;

        /// <summary>Запас, с которого начинается Лучшая полоса.</summary>
        public int BestMargin = 7;

        /// <summary>Убеждение не даёт Худшую, если недобор не глубже подушки.</summary>
        public int PersuadeCushion = 2;

        /// <summary>Запугиванию Лучшая доступна с меньшим запасом.</summary>
        public int IntimidateBestBonus = 2;

        /// <summary>Торговля конвертирует полосу в скидку.</summary>
        public double TradeBandDiscount = 0.10;

        /// <summary>Насколько дорожает повторное обращение по той же теме.</summary>
        public int RepeatPenaltyStep = 2;

        /// <summary>Окно, внутри которого повторы считаются.</summary>
        public int RepeatWindowDays = 7;
    }
}
