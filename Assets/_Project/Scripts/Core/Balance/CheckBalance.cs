using System;

namespace Game.Core.Balance
{
    /// <summary>
    /// Числа детермінованих перевірок і полос наслідку.
    ///
    /// ЯКІР: поріг — це стільки, скільки дає середняк, який вклався в навичку на
    /// поточній стадії. Хороша полоса — спеціаліст, Найкраща — спеціаліст
    /// із профільним трейтом.
    /// </summary>
    [Serializable]
    public sealed class CheckBalance
    {
        /// <summary>Запас над порогом, з якого починається Хороша полоса.</summary>
        public int GoodMargin = 3;

        /// <summary>Запас, з якого починається Найкраща полоса.</summary>
        public int BestMargin = 7;

        /// <summary>Переконання не дає Найгіршу, якщо недобір не глибший за подушку.</summary>
        public int PersuadeCushion = 2;

        /// <summary>Залякуванню Найкраща доступна з меншим запасом.</summary>
        public int IntimidateBestBonus = 2;

        /// <summary>Торгівля конвертує полосу в знижку.</summary>
        public double TradeBandDiscount = 0.10;

        /// <summary>Наскільки дорожчає повторне звернення за тією самою темою.</summary>
        public int RepeatPenaltyStep = 2;

        /// <summary>Вікно, всередині якого повтори рахуються.</summary>
        public int RepeatWindowDays = 7;

        // ---- Ціна кривавого шляху (Поправка №1: «швидко і дуже складно») ----

        /// <summary>
        /// Очки поранення виконавцю за кривавий розбір.
        /// ЯКІР: десять очок — це приблизно двоє діб лазарета при середньому
        /// лікарі, тобто виконавець випадає з розстановки на пару днів.
        /// </summary>
        public double BloodyPathInjury = 10.0;

        /// <summary>Скільки діб громада пам'ятає кров.</summary>
        public int FearDurationDays = 3;

        /// <summary>
        /// Поки громада боїться, Переконання і Торгівля дорожчі на стільки.
        /// ЯКІР: крок дорівнює штрафу за повтор — страх коштує рівно одного
        /// «зайвого» підходу до теми.
        /// </summary>
        public int FearPenaltyStep = 2;
    }
}
