using Game.Core.Stats;

namespace Game.Core.Characters.Creation
{
    /// <summary>Стать протагоніста (R7): граматика, не бойові числа.</summary>
    public enum Gender
    {
        Male = 0,
        Female = 1
    }

    /// <summary>
    /// Застосування швидкого екрана створення (R12) до вже наявного
    /// протагоніста <see cref="Session.FirstHourWorld"/>. GameSession (D1)
    /// кличе <see cref="Apply"/> у <c>ConfirmCreation()</c> до першого
    /// <c>AdvanceDay()</c> — тут лише чиста функція без стану екрана: вона
    /// не знає ні про <c>SessionState</c>, ні про UI, тільки про
    /// <see cref="Companion"/> і <see cref="BackgroundPreset"/>.
    ///
    /// Стать (<see cref="Gender"/>) сама по собі не змінює жодного числа — GDD не
    /// заводить бойових відмінностей за статтю. Вона потрібна лише текстовому
    /// шару (E3, ключі <c>.m</c>/<c>.f</c>) і тому тут не застосовується до
    /// Companion, а лишається параметром виклику для того, хто його зберігає
    /// (GameSession/ProtagonistCreationView, D1).
    /// </summary>
    public static class ProtagonistCreation
    {
        /// <summary>
        /// Переписує атрибути і скіли протагоніста значеннями обраного
        /// preset'а і, якщо задано, ім'я. Порожнє/null ім'я не чіпає
        /// <see cref="Companion.DisplayName"/>: за замовчуванням текстовий шар
        /// сам обере ключ <c>ui.creation.name.default.m</c>/<c>.f</c> (§7.17) —
        /// Core не вирішує, який рядок показати гравцю (інваріант «Core віддає
        /// лише ключі»).
        /// </summary>
        public static void Apply(Companion protagonist, BackgroundPreset preset, string customName = null)
        {
            if (protagonist == null || preset == null) return;

            var attrs = Attributes.All;
            for (int i = 0; i < attrs.Length; i++)
                protagonist.Attributes[attrs[i]] = preset.Attributes[attrs[i]];

            var skills = Skills.All;
            for (int i = 0; i < skills.Length; i++)
                protagonist.Skills[skills[i]] = preset.Skills[skills[i]];

            if (!string.IsNullOrEmpty(customName))
                protagonist.DisplayName = customName;
        }
    }
}
