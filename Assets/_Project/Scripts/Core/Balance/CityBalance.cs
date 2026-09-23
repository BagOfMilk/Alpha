using System;

namespace Game.Core.Balance
{
    /// <summary>
    /// Числа того, как город отвечает на игрока: люди, совет, тиры (Поправка №6).
    ///
    /// ВСЁ ЗДЕСЬ — ПЛЕЙСХОЛДЕР. Числа выставлены так, чтобы механика была
    /// видна в тестах и на плёнке суток; настоящие значения ставит харнес по
    /// цели §6.4: при хорошей игре тир 2 около тридцатых суток, тир 3 около
    /// шестидесятых.
    /// </summary>
    [Serializable]
    public sealed class CityBalance
    {
        // ---- Люди приходят (§6.3) ----

        /// <summary>Естественный прирост: один человек раз в столько суток.</summary>
        public int NaturalGrowthEveryDays = 3;

        /// <summary>Таверна: столько людей в сутки сверх естественного прироста.</summary>
        public int TavernArrivalsPerDay = 1;

        /// <summary>Приём переселенцев решением совета: сколько приходит.</summary>
        public int SettlersPerOrder = 10;

        /// <summary>Цена приёма в еде: новых ртов надо кормить.</summary>
        public int SettlersFoodCost = 30;

        /// <summary>
        /// Откат приёма. Без него совет звал людей через день, и плёнка суток
        /// показала: хутор становился селом на одиннадцатые сутки вместо
        /// тридцатых — приём был дешёвой кнопкой, а не решением.
        /// </summary>
        public int SettlersCooldownDays = 7;

        // ---- Люди уходят (§6.3) ----

        /// <summary>Столько уходит в каждые голодные сутки.</summary>
        public int HungryDepartures = 2;

        /// <summary>Столько уходит в сутки, пока община помнит кровь.</summary>
        public int FearDepartures = 1;

        // ---- Совет (§6.2) ----

        /// <summary>Цена облавы золотом. Сила и откат — в TensionBalance (RaidDelta, RaidCooldownDays).</summary>
        public int RaidGoldCost = 15;

        // ---- Тиры (§6.4): население И ключевое здание ----

        /// <summary>Порог населения для тира 2, 3, 4 (индекс 0 — тир 2).</summary>
        public int[] TierPopulation = { 150, 250, 400 };
    }
}
