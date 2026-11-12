namespace Game.Gameplay.Ring.Models
{
    /// <summary>Хто приймає ввід цього кадру на екрані (див. <see cref="CareerPadFeature.Route(bool,bool,bool)"/>).</summary>
    public enum CareerInputRoute
    {
        /// <summary>Старі клавіші екрана кар'єри (Enter — головна дія, цифри — плитки, Esc — пауза).</summary>
        Legacy = 0,
        /// <summary>Екран-меню (пауза, налаштування): клавіатура, геймпад і колесо водять його власний вибір.</summary>
        Menu = 1,
        /// <summary>Поле вводу у фокусі: клавіатура друкує, клавіші екрана кар'єри цього кадру не діють.</summary>
        Typing = 2,
        /// <summary>Фокус без миші: хрестовина, стік і стрілки водять фокус, A / Enter натискає ціль.</summary>
        Pad = 3,
    }

    /// <summary>
    /// Фокус без миші й геймпад на екранах кар'єри (P3, 3191c83) — ітерація 2. Рішення власника 11.11.2026: «Геймпад ітерація
    /// 2, навіть реальний xbox підключу.» Код шару лишається; у М1 він вимкнений цим прапорцем: стрілки, WASD і хрестовина
    /// фокус не рухають, підписи плит — «Enter», поведінка екранів — як до 3191c83. Захист поля імені (цифри, Пробіл, Enter
    /// не діють, поки поле у фокусі) від прапорця не залежить і лишається в М1. Охоронець — <c>CareerPadFeatureTests</c>.
    /// </summary>
    public static class CareerPadFeature
    {
        /// <summary>Прапорець М1: false — шар фокуса не діє. Увімкнути в ітерації 2 разом із перевіркою наживо на Xbox.</summary>
        public static readonly bool Enabled = false;

        /// <summary>Хто приймає ввід кадру за поточним прапорцем.</summary>
        public static CareerInputRoute Route(bool menuView, bool typing, bool viewHasPadTargets)
        {
            return Route(Enabled, menuView, typing, viewHasPadTargets);
        }

        /// <summary>
        /// Те саме з явним прапорцем: екран-меню — своє; поле вводу у фокусі — друк (незалежно від прапорця); фокус без миші —
        /// лише з увімкненим прапорцем і на екрані, де всі дії мають ціль фокусу; інакше — старі клавіші.
        /// </summary>
        public static CareerInputRoute Route(bool padOn, bool menuView, bool typing, bool viewHasPadTargets)
        {
            if (menuView) return CareerInputRoute.Menu;
            if (typing) return CareerInputRoute.Typing;
            return padOn && viewHasPadTargets ? CareerInputRoute.Pad : CareerInputRoute.Legacy;
        }

        /// <summary>Екран бере фокус без миші: прапорець і цілі фокусу на ньому.</summary>
        public static bool PadOn(bool viewHasPadTargets) { return Enabled && viewHasPadTargets; }

        /// <summary>Підпис геймпада («A») замість «Enter» — лише коли шар увімкнений і гравець узяв геймпад.</summary>
        public static bool PadHints(bool padLast) { return Enabled && padLast; }
    }
}
