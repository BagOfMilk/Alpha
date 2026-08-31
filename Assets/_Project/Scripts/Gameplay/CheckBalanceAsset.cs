using Game.Core.Balance;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Числа детерминированных проверок.
    /// Создание: ПКМ в Project → Create → Alpha → Balance → Проверки.
    /// </summary>
    [CreateAssetMenu(fileName = "CheckBalance", menuName = "Alpha/Balance/Проверки", order = 13)]
    public sealed class CheckBalanceAsset : ScriptableObject
    {
        [Header("Полосы исхода")]
        [Tooltip("Запас над порогом, с которого начинается Хорошая полоса.")]
        public int goodMargin = 3;
        [Tooltip("Запас, с которого начинается Лучшая.")]
        public int bestMargin = 7;

        [Header("Формы подходов")]
        [Tooltip("Убеждение не даёт Худшую, если недобор не глубже подушки.")]
        public int persuadeCushion = 2;
        [Tooltip("Запугиванию Лучшая доступна с меньшим запасом.")]
        public int intimidateBestBonus = 2;
        [Tooltip("Торговля конвертирует полосу в скидку.")]
        public double tradeBandDiscount = 0.10;

        [Header("Повторные обращения")]
        public int repeatPenaltyStep = 2;
        public int repeatWindowDays = 7;

        public CheckBalance ToConfig()
        {
            return new CheckBalance
            {
                GoodMargin = goodMargin,
                BestMargin = bestMargin,
                PersuadeCushion = persuadeCushion,
                IntimidateBestBonus = intimidateBestBonus,
                TradeBandDiscount = tradeBandDiscount,
                RepeatPenaltyStep = repeatPenaltyStep,
                RepeatWindowDays = repeatWindowDays
            };
        }
    }
}
