using System;
using Game.Core.Balance;

namespace Game.Core.Pressure
{
    /// <summary>
    /// Публичный вход для драйверов Напряжения.
    ///
    /// Асимметрия намеренная: ПИСАТЬ в шкалу можно снаружи (иначе квесты и
    /// события не смогли бы на неё влиять), а ЧИТАТЬ число — нельзя, метод
    /// доступа internal. Запрещено именно подсматривание, а не воздействие.
    ///
    /// Каждый драйвер получает здесь свой именованный метод, поэтому список
    /// остаётся закрытым и обозримым (Поправка №3.5): «просто прибавить очков»
    /// технически невозможно.
    /// </summary>
    public static class TensionDrivers
    {
        /// <summary>Вес выбора в квесте. Числа — в TensionBalance.</summary>
        public enum ChoiceWeight
        {
            Minor = 0,
            Major = 1,
            Monstrous = 2
        }

        /// <summary>
        /// Выбор игрока в квесте поднял напряжение в городе.
        /// Валентность выбора игроку не сообщается (US-11.1): он узнаёт эффект
        /// по последствиям, а не по всплывающей цифре.
        /// </summary>
        public static void QuestChoice(TensionState state, ChoiceWeight weight,
            string sourceId, BalanceConfig balance)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (balance == null) throw new ArgumentNullException(nameof(balance));

            state.Apply(TensionDriver.QuestChoice, Weight(weight, balance.Tension), sourceId);
        }

        /// <summary>
        /// Исход события/квеста разрядил обстановку. Тот же список весов,
        /// но со знаком минус — так исход умеет и поднимать, и опускать.
        /// </summary>
        public static void EventOutcome(TensionState state, ChoiceWeight weight,
            string sourceId, BalanceConfig balance)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (balance == null) throw new ArgumentNullException(nameof(balance));

            state.Apply(TensionDriver.EventOutcome, -Weight(weight, balance.Tension), sourceId);
        }

        private static int Weight(ChoiceWeight weight, TensionBalance cfg)
        {
            switch (weight)
            {
                case ChoiceWeight.Minor: return cfg.ChoiceMinor;
                case ChoiceWeight.Major: return cfg.ChoiceMajor;
                default: return cfg.ChoiceMonstrous;
            }
        }
    }
}
