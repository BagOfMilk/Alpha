using System;
using Game.Core.Characters;

namespace Game.Core.Balance
{
    /// <summary>
    /// Числа социального слоя напарников (B4/R2): пороги полос Лояльности,
    /// рябь ростера от смерти/предательства, таблица наслідків (LoyaltyRules) и
    /// порог дефекции. Отдельный файл-секция (R14) — B4 подключает его одной
    /// строкой в <see cref="BalanceConfig"/>, не трогая остальные поля.
    ///
    /// Все числа — ПЛЕЙСХОЛДЕРЫ: тестовая сборка их не настраивала харнесом,
    /// как City/Tension (см. docs/TEST_BUILD.md §9).
    /// </summary>
    [Serializable]
    public sealed class CompanionSocialBalance
    {
        // ---- Полосы Лояльности 0..100 (Broken/Resentful/Wary/Steady/Devoted) ----
        // Подобраны так, чтобы совпасть с якорными точками сценария доба 1
        // (docs/TEST_BUILD.md §3.1): старт Мирослави 45 -> Wary, старт Максима
        // 60 -> Steady; "-20 -> 25" и "-35 -> 10" из розв'язки вузла 1 обидва
        // лежать в Resentful (не в Broken) — тому Broken лише нижче 10.
        public int[] LoyaltyBandThresholds = { 10, 30, 50, 70 };

        public LoyaltyBand BandFor(int loyalty)
        {
            var t = LoyaltyBandThresholds;
            if (t == null || t.Length == 0) return LoyaltyBand.Wary;
            for (int i = 0; i < t.Length; i++)
                if (loyalty < t[i]) return (LoyaltyBand)i;
            return (LoyaltyBand)t.Length;
        }

        // ---- Рябь ростера (RosterDrama, US-9.6 порт) ----
        /// <summary>Тяжёлый отклик соратника на смерть (в пределах MaxRippleTargets).</summary>
        public int MournLoyaltyHit = 12;
        /// <summary>Тяжёлый отклик соратника на предательство — сильнее, чем на смерть.</summary>
        public int BetrayalKinLoyaltyHit = 18;
        /// <summary>Соперник павшего/предателя не скорбит — небольшое облегчение.</summary>
        public int RivalDeathLoyaltyRelief = 4;
        /// <summary>Нейтральный слегка тронут — лёгкий отклик и каскадный "хвост" после MaxRippleTargets.</summary>
        public int NeutralDeathLoyaltyHit = 3;
        /// <summary>Ограждение от каскада: не больше стольки тяжёлых откликов за одну рябь.</summary>
        public int MaxRippleTargets = 3;

        // ---- Таблица наслідків (LoyaltyRules) ----
        /// <summary>Кровавий шлях: удар лояльності мирно-ціннісного напарника.</summary>
        public int BloodyChoiceValuedHit = 6;
        /// <summary>Кровавий шлях: полегшення жорстко-ціннісному напарнику.</summary>
        public int BloodyChoiceRuthlessRelief = 3;
        /// <summary>Ігнорована прохання/квест-етап напарника.</summary>
        public int RequestIgnoredHit = 10;
        /// <summary>Захист/порятунок напарника у власній біді.</summary>
        public int CompanionProtectedBonus = 15;
        /// <summary>Покинутий напарник у власній біді.</summary>
        public int CompanionAbandonedHit = 20;
        /// <summary>Аудит G25: пасивний бонус "Morale" з council_seat підтримує лояльність складу.</summary>
        public int MoraleLoyaltyBonus = 2;

        // ---- Дефекція (R2/§2 №25) ----
        /// <summary>Скільки діб підряд полоса має бути ≤ Resentful, щоб дефекція спрацювала без прапора.</summary>
        public int DefectionDaysAtLowLoyalty = 5;
    }
}
