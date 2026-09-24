using System;
using Game.Core.Factions;

namespace Game.Core.Balance
{
    /// <summary>
    /// Числа фракций и новых указов рады (R5; AUDIT П8/G12/G20). ПЛЕЙСХОЛДЕР —
    /// как и остальные секции городского слоя (Поправка №6): важно, что
    /// механика видна в тестах, точные числа ставит харнес.
    /// </summary>
    [Serializable]
    public sealed class FactionBalance
    {
        /// <summary>Границы полос: ниже первой — Ворожість, выше последней — Союз.</summary>
        public int[] BandThresholds = { 20, 40, 60, 80 };

        /// <summary>Стартовое отношение всех фракций — середина шкалы (Нейтральність).</summary>
        public int StartingStanding = 50;

        // ---- Облава (OrderRaid): силовой метод трогает и фракции, не только
        //      Напругу — бояри довольны порядком, громаде не нравится нагайка
        //      на своїх (ревью-фікс, тест Raid_LowersTension_PaysCosts_ShiftsFactions) ----
        public int RaidFactionFavoredDelta = 6;
        public int RaidFactionCostDelta = 8;

        // ---- Указ (OrderDecree): двигает Уклад и Напругу (AUDIT П8+G20) ----
        public int DecreeGoldCost = 20;
        public int DecreeCooldownDays = 8;
        /// <summary>На сколько шагов двигает DayProcessor.OrderLevel (индекс 1..4).</summary>
        public int DecreeOrderLevelStep = 1;
        /// <summary>Понижающая заявка в Напругу драйвером CouncilEdict (в QueueExternal идёт со знаком минус).</summary>
        public int DecreeTensionDelta = 6;
        public int DecreeFactionDelta = 8;

        // ---- Дипломатия ----
        public int DiplomacyGoldCost = 25;
        public int DiplomacyCooldownDays = 6;
        public int DiplomacyFactionDelta = 10;

        // ---- Инвестиция: платит золото сутками, потом останавливается ----
        public int InvestmentGoldCost = 40;
        public int InvestmentGoldPerDay = 8;
        public int InvestmentDays = 10;

        // ---- Подготовка к угрозе: маркер для Готовности (B6 забирает позже) ----
        public int PrepareThreatGoldCost = 15;
        public int PrepareThreatCooldownDays = 6;

        // ---- Снаряжение экспедиции: разовый бонус следующей вылазке ----
        public int OutfitExpeditionGoldCost = 25;
        public int OutfitExpeditionBonusValue = 10;

        /// <summary>
        /// Порог показанной проверки Торговли, которой решает скидка на заказы
        /// (AUDIT G12): чем ниже порог, тем чаще занятый рынок даёт хотя бы
        /// Базовую полосу и хотя бы минимальную скидку.
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
