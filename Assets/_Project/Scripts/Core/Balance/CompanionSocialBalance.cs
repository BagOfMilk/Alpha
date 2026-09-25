using System;
using Game.Core.Characters;

namespace Game.Core.Balance
{
    /// <summary>
    /// Числа соціального шару напарників (B4/R2): пороги полос Лояльності,
    /// брижі ростера від смерті/зради, таблиця наслідків (LoyaltyRules) і
    /// поріг дефекції. Окремий файл-секція (R14) — B4 підключає його одним
    /// рядком у <see cref="BalanceConfig"/>, не чіпаючи решту полів.
    ///
    /// Усі числа — ПЛЕЙСХОЛДЕРИ: тестова збірка їх не налаштовувала харнесом,
    /// як City/Tension (див. docs/TEST_BUILD.md §9).
    /// </summary>
    [Serializable]
    public sealed class CompanionSocialBalance
    {
        // ---- Полоси Лояльності 0..100 (Broken/Resentful/Wary/Steady/Devoted) ----
        // Підібрані так, щоб збігтися з якірними точками сценарію доба 1
        // (docs/TEST_BUILD.md §3.1): старт Мирослави 45 -> Wary, старт Максима
        // 60 -> Steady; "-20 -> 25" і "-35 -> 10" із розв'язки вузла 1 обидва
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

        // ---- Брижі ростера (RosterDrama, US-9.6 порт) ----
        /// <summary>Важкий відгук соратника на смерть (у межах MaxRippleTargets).</summary>
        public int MournLoyaltyHit = 12;
        /// <summary>Важкий відгук соратника на зраду — сильніший, ніж на смерть.</summary>
        public int BetrayalKinLoyaltyHit = 18;
        /// <summary>Суперник полеглого/зрадника не сумує — невелике полегшення.</summary>
        public int RivalDeathLoyaltyRelief = 4;
        /// <summary>Нейтральний трохи зворушений — легкий відгук і каскадний "хвіст" після MaxRippleTargets.</summary>
        public int NeutralDeathLoyaltyHit = 3;
        /// <summary>Огородження від каскаду: не більше стількох важких відгуків за одну брижу.</summary>
        public int MaxRippleTargets = 3;

        // ---- Таблиця наслідків (LoyaltyRules) ----
        /// <summary>Кривавий шлях: удар лояльності мирно-ціннісного напарника.</summary>
        public int BloodyChoiceValuedHit = 6;
        /// <summary>Кривавий шлях: полегшення жорстко-ціннісному напарнику.</summary>
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
