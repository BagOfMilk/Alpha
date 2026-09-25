using System;
using Game.Core.Factions;

namespace Game.Core.Balance
{
    /// <summary>
    /// Числа фракцій і нових указів ради (R5; AUDIT П8/G12/G20). ПЛЕЙСХОЛДЕР —
    /// як і решта секцій міського шару (Поправка №6): важливо, щоб
    /// механіка була видна в тестах, точні числа ставить харнес.
    /// </summary>
    [Serializable]
    public sealed class FactionBalance
    {
        /// <summary>Межі полос: нижче першої — Ворожість, вище останньої — Союз.</summary>
        public int[] BandThresholds = { 20, 40, 60, 80 };

        /// <summary>Стартове ставлення всіх фракцій — середина шкали (Нейтральність).</summary>
        public int StartingStanding = 50;

        // ---- Облава (OrderRaid): силовий метод чіпає і фракції, не тільки
        //      Напругу — бояри задоволені порядком, громаді не подобається нагайка
        //      на своїх (ревью-фікс, тест Raid_LowersTension_PaysCosts_ShiftsFactions) ----
        public int RaidFactionFavoredDelta = 6;
        public int RaidFactionCostDelta = 8;

        // ---- Указ (OrderDecree): рухає Уклад і Напругу (AUDIT П8+G20) ----
        public int DecreeGoldCost = 20;
        public int DecreeCooldownDays = 8;
        /// <summary>На скільки кроків рухає DayProcessor.OrderLevel (індекс 1..4).</summary>
        public int DecreeOrderLevelStep = 1;
        /// <summary>Знижувальна заявка в Напругу драйвером CouncilEdict (у QueueExternal іде зі знаком мінус).</summary>
        public int DecreeTensionDelta = 6;
        public int DecreeFactionDelta = 8;

        // ---- Дипломатія ----
        public int DiplomacyGoldCost = 25;
        public int DiplomacyCooldownDays = 6;
        public int DiplomacyFactionDelta = 10;

        // ---- Інвестиція: платить золото добами, потім зупиняється ----
        public int InvestmentGoldCost = 40;
        public int InvestmentGoldPerDay = 8;
        public int InvestmentDays = 10;

        // ---- Підготовка до загрози: маркер для Готовності (B6 забирає пізніше) ----
        public int PrepareThreatGoldCost = 15;
        public int PrepareThreatCooldownDays = 6;

        // ---- Спорядження експедиції: разовий бонус наступній вилазці ----
        public int OutfitExpeditionGoldCost = 25;
        public int OutfitExpeditionBonusValue = 10;

        /// <summary>
        /// Поріг показаної перевірки Торгівлі, якою вирішується знижка на замовлення
        /// (AUDIT G12): чим нижче поріг, тим частіше зайнятий ринок дає хоча б
        /// Базову полосу і хоча б мінімальну знижку.
        /// </summary>
        public int TradeDiscountThreshold = 4;

        public FactionStandingBand BandFor(int value)
        {
            var t = BandThresholds;
            if (t == null || t.Length == 0) return FactionStandingBand.Neutral;
            for (int i = 0; i < t.Length; i++)
                if (value < t[i]) return (FactionStandingBand)i;
            return (FactionStandingBand)t.Length;
        }
    }
}
