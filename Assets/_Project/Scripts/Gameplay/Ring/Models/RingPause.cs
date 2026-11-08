using Game.Core.Ring.Career;

namespace Game.Gameplay.Ring.Models
{
    /// <summary>
    /// Темп часу гри однією формулою (ТЗ B01 §6.2, пауза): базовий темп (1; бот огляду кар'єри — ×3), миттєвий множник
    /// (стоп-кадр удару, сповільнення фінішера) і пауза. Хто змінює свою частину — ставить <c>Time.timeScale = RingTime.Scale</c>,
    /// тож кінець сповільнення під час паузи не «відпускає» гру, а вихід з паузи не губить сповільнення.
    /// </summary>
    public static class RingTime
    {
        /// <summary>Базовий темп (огляд кар'єри грає втричі швидше).</summary>
        public static float Base = 1f;

        /// <summary>Миттєвий множник: стоп-кадр ≈ 0,05, сповільнення фінішера 0,4, звичайно 1.</summary>
        public static float Warp = 1f;

        /// <summary>Пауза (меню паузи відкрите): час гри стоїть.</summary>
        public static bool Paused;

        public static float Scale { get { return Paused ? 0f : Base * Warp; } }

        /// <summary>Скинути на старті сцени (статичні поля переживають перезапуск сцени в редакторі).</summary>
        public static void Reset() { Base = 1f; Warp = 1f; Paused = false; }
    }

    /// <summary>Що меню паузи каже перед «На титул»: чи збережеться поточний стан (правило — як у <c>CareerSession.CaptureState</c>).</summary>
    public static class PauseRules
    {
        public const string LeaveSafeKey = "m.pause.leave.safe";
        public const string LeaveShowKey = "m.pause.leave.show";
        public const string LeaveBackstageKey = "m.pause.leave.backstage";
        public const string LeaveCreateKey = "m.pause.leave.create";

        /// <summary>
        /// Ключ тексту пояснення. Зліпок кар'єри матч, що йде, відкочує до афіші (і промо шоу — теж), сцена закулісся живе
        /// поза зліпком, а створення героя не зберігається зовсім — у цих випадках гравця попереджають.
        /// </summary>
        public static string LeaveBodyKey(CareerStage stage, ShowPhase phase, bool backstageOpen)
        {
            if (stage == CareerStage.Create) return LeaveCreateKey;
            if (stage == CareerStage.Show && (phase == ShowPhase.Match || phase == ShowPhase.Feud)) return LeaveShowKey;
            if (stage == CareerStage.Week && backstageOpen) return LeaveBackstageKey;
            return LeaveSafeKey;
        }

        /// <summary>Чи втратиться щось (тоді фокус діалогу — «Залишитись»).</summary>
        public static bool LosesProgress(CareerStage stage, ShowPhase phase, bool backstageOpen)
        {
            return LeaveBodyKey(stage, phase, backstageOpen) != LeaveSafeKey;
        }
    }
}
