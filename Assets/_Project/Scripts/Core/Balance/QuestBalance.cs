using System;

namespace Game.Core.Balance
{
    /// <summary>
    /// Числа квестового рушія (R6, пакет B6): вага вибору в Напрузі та пороги
    /// контенту квесту Гафії «Гірка розрада».
    ///
    /// ЯКОРЬ: квест — це чотири полоси наслідку скрізь (Поправка №3.7), тому
    /// вага в Напрузі задана ОДНИМ масивом на всі квести (як
    /// <c>IncidentDefinition.TensionByBand</c> для інцидентів), а не числом на
    /// квест — інакше кожен новий квест зобов'язаний придумувати свою вагу
    /// заново, а драйвер лишається один (<c>TensionDriver.QuestChoice</c>,
    /// R6, інваріант 5).
    /// </summary>
    [Serializable]
    public sealed class QuestBalance
    {
        /// <summary>
        /// Напруга по кожній із 4 полос: Найгірша .. Найкраща. Додатне піднімає,
        /// від'ємне — знижує; застосовується мостом
        /// <c>DayProcessor.QueueExternal(TensionDriver.QuestChoice, amount)</c>
        /// (R6) тим, хто застосовує наслідки квесту (D1), а не самим рушієм.
        /// </summary>
        public int[] TensionByBand = { 40, 15, -15, -30 };

        // ---- Квест Гафії «Гірка розрада» (§3.2–3.5 TEST_BUILD.md) ----

        /// <summary>ПЛЕЙСХОЛДЕР: поріг етапу 2 (Виживання — знайти гірку траву).</summary>
        public int HafiyaGrassThreshold = 5;

        /// <summary>
        /// Наскільки трава Гафії полегшує поріг інциденту <c>sick_child</c>
        /// (доба 3), якщо вона знайдена (прапор <c>DefaultQuests.HafiyaGrassFoundFlag</c>).
        /// Сам <c>OpeningContent.SickChild()</c> цей бонус ще не читає — це чужий
        /// файл (володіє A1), і B6 лише ЕКСПОНУЄ число та прапор; застосування —
        /// робота D1 (аудит G7/П10, TEST_BUILD.md §5 рядок B6).
        /// </summary>
        public int HafiyaGrassBonusToSickChild = 2;

        /// <summary>
        /// Захищений доступ до масиву за полосою (той самий приём, що
        /// <see cref="ReadinessBalance.Pick"/>): <see cref="TensionByBand"/> —
        /// публічне поле, власник править його через Balance SO без
        /// перекомпіляції (R14), і скорочений контентом масив не має валити
        /// побудову квесту винятком, як і будь-який інший баланс-масив у грі.
        /// </summary>
        internal static int Pick(int[] arr, int index, int fallback)
        {
            if (arr == null || arr.Length == 0) return fallback;
            if (index < 0) index = 0;
            if (index >= arr.Length) index = arr.Length - 1;
            return arr[index];
        }
    }
}
