using Game.Core.Stats;

namespace Game.Core.Characters.Creation
{
    /// <summary>Пол протагониста (R7): грамматика, не боевые числа.</summary>
    public enum Gender
    {
        Male = 0,
        Female = 1
    }

    /// <summary>
    /// Применение быстрого экрана создания (R12) к уже существующему
    /// протагонисту <see cref="Session.FirstHourWorld"/>. GameSession (D1)
    /// зовёт <see cref="Apply"/> в <c>ConfirmCreation()</c> до первого
    /// <c>AdvanceDay()</c> — здесь лишь чистая функция без состояния экрана: она
    /// не знает ни про <c>SessionState</c>, ни про UI, только про
    /// <see cref="Companion"/> и <see cref="BackgroundPreset"/>.
    ///
    /// Пол (<see cref="Gender"/>) сам по себе не меняет ни одного числа — GDD не
    /// заводит боевых различий по полу. Он нужен только текстовому
    /// слою (E3, ключи <c>.m</c>/<c>.f</c>) и поэтому здесь не применяется к
    /// Companion, а остаётся параметром вызова для того, кто его хранит
    /// (GameSession/ProtagonistCreationView, D1).
    /// </summary>
    public static class ProtagonistCreation
    {
        /// <summary>
        /// Переписывает атрибуты и скилы протагониста значениями выбранного
        /// preset'а и, если задано, имя. Пустое/null имя не трогает
        /// <see cref="Companion.DisplayName"/>: по умолчанию текстовый слой
        /// сам выберет ключ <c>ui.creation.name.default.m</c>/<c>.f</c> (§7.17) —
        /// Core не решает, какую строку показать игроку (инвариант «Core отдаёт
        /// только ключи»).
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
