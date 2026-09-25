using System;
using Game.Core.Balance;

namespace Game.Core.Pressure
{
    /// <summary>
    /// Публічний вхід для драйверів Напруги.
    ///
    /// Асиметрія навмисна: ПИСАТИ в шкалу можна ззовні (інакше квести і
    /// події не змогли б на неї впливати), а ЧИТАТИ число — не можна, метод
    /// доступу internal. Заборонено саме підглядання, а не вплив.
    ///
    /// Кожен драйвер отримує тут свій іменований метод, тому список
    /// лишається закритим і оглядним (Поправка №3.5): «просто додати очок»
    /// технічно неможливо.
    /// </summary>
    public static class TensionDrivers
    {
        /// <summary>Вага вибору в квесті. Числа — у TensionBalance.</summary>
        public enum ChoiceWeight
        {
            Minor = 0,
            Major = 1,
            Monstrous = 2
        }

        /// <summary>
        /// Вибір гравця в квесті підняв напругу в місті.
        /// Валентність вибору гравцю не повідомляється (US-11.1): він дізнається ефект
        /// за наслідками, а не за спливаючою цифрою.
        /// </summary>
        public static void QuestChoice(TensionState state, ChoiceWeight weight,
            string sourceId, BalanceConfig balance)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (balance == null) throw new ArgumentNullException(nameof(balance));

            state.Apply(TensionDriver.QuestChoice, Weight(weight, balance.Tension), sourceId);
        }

        /// <summary>
        /// Результат події/квесту розрядив обстановку. Той самий список ваг,
        /// але зі знаком мінус — так результат уміє і піднімати, і опускати.
        /// </summary>
        public static void EventOutcome(TensionState state, ChoiceWeight weight,
            string sourceId, BalanceConfig balance)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (balance == null) throw new ArgumentNullException(nameof(balance));

            state.Apply(TensionDriver.EventOutcome, -Weight(weight, balance.Tension), sourceId);
        }

        /// <summary>
        /// Голодний день у поселенні (Поправка №4). Єдиний драйвер, який
        /// заводиться не вибором гравця і не результатом загрози, а станом бази:
        /// перечекати голод удома не можна, бо гасять Напругу Храм і
        /// Укріплення, а будуються вони за компонент із вилазок.
        /// </summary>
        public static void Hunger(TensionState state, string sourceId, BalanceConfig balance)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (balance == null) throw new ArgumentNullException(nameof(balance));

            state.Apply(TensionDriver.Hunger, balance.Tension.HungerDeltaPerDay, sourceId);
        }

        /// <summary>
        /// internal, а не private (G22): DayProcessor.QueueQuestChoice/
        /// QueueEventOutcome (міст R6) рахують тією самою таблицею ваг — список
        /// драйверів лишається закритим (інваріант 5), а число не дублюється
        /// в двох місцях.
        /// </summary>
        internal static int Weight(ChoiceWeight weight, TensionBalance cfg)
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
