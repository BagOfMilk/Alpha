using Game.Core.Session;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Головна кнопка фази (HUD, HP-2): завжди на тому самому місці. Вранці й
    /// у вільній грі — «Почати день». Чиста логіка, щоб кнопка, автотур і
    /// headless-бот ходили одним шляхом (UI-14).
    /// </summary>
    public static class UxPhaseButton
    {
        /// <summary>
        /// «Почати день»: підтвердити ранок і прокрутити день до першого
        /// рішення або до вечора. Раніше кнопка лише підтверджувала ранок, а
        /// сам день (<see cref="GameSession.AdvanceDay"/>) не кликав ніхто —
        /// людина застрягала на першому ранку (аудит журналу 25.09.2026).
        /// </summary>
        public static void StartDay(GameSession s)
        {
            if (s.State == SessionState.Morning || s.State == SessionState.FreePlay)
                s.ConfirmMorning();
            if (s.State == SessionState.Day)
                s.AdvanceDay();
        }
    }
}
