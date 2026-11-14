namespace Game.Gameplay.Ring.Stage
{
    /// <summary>Хто стоїть у петлі очікування: від ролі залежить, з якої миті петлі він починає і в якому темпі дихає.</summary>
    public enum RingIdleRole { Player, Rival, Referee, Other }

    /// <summary>
    /// Фаза й темп петель очікування (стійка, втома, оглушення, очікування рефері). Кожна така петля стартувала з t = 0 і в
    /// одному темпі, тож борці й рефері хитались синхронно, «як дзеркальні манекени» (арт-директор, 14.11.2026, за
    /// дорученням власника «добавь более живие айдл анимации когда просто стояние происходит, что бы чуть живее выглядело»).
    /// Тепер кожна роль має свою частку довжини петлі як мить старту і свій темп. Усе детерміновано, без випадковості:
    /// однаковий матч дає однакову картинку. Ковзання це не додає: петля стоїть на місці, змінюється лише мить і швидкість.
    /// Читає <c>RingStage3D.Place</c>; охоронці — <c>RingIdlePhaseTests</c>.
    /// </summary>
    public static class RingIdlePhase
    {
        /// <summary>Частка довжини петлі (0…1), з якої вона стартує для цієї ролі.</summary>
        public static float StartFraction(RingIdleRole role)
        {
            switch (role)
            {
                case RingIdleRole.Rival: return 0.43f;
                case RingIdleRole.Referee: return 0.71f;
                case RingIdleRole.Other: return 0.21f;   // посередині між гравцем (0) і суперником (0,43)
                default: return 0f;
            }
        }

        /// <summary>Множник темпу петлі для ролі (до темпу, з яким петлю грає сцена).</summary>
        public static float SpeedFactor(RingIdleRole role)
        {
            switch (role)
            {
                case RingIdleRole.Rival: return 1.04f;
                case RingIdleRole.Referee: return 0.95f;
                case RingIdleRole.Other: return 0.98f;
                default: return 1f;
            }
        }

        /// <summary>
        /// Петля очікування, якій можна зсувати фазу й темп. Петлі з власним ритмом — відлік рефері за рингом
        /// (<c>Ref_Count_Outside_Loop</c>) і утримання в прийомах — сюди не входять: їхній темп іде з тактом режисера.
        /// </summary>
        public static bool IsWaitingLoop(string clip)
        {
            switch (clip)
            {
                case "Ring_Stance_Loop":
                case "Idle_Tired_Loop":
                case "Idle_Hurt_Head_Loop":
                case "Ref_Idle_Loop":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Мить старту петлі (с часу кліпа) для ролі; поза петлями очікування — 0.</summary>
        public static float StartTime(string clip, float length, RingIdleRole role)
        {
            if (!IsWaitingLoop(clip) || length <= 0f) return 0f;
            return StartFraction(role) * length;
        }

        /// <summary>Темп петлі для ролі; поза петлями очікування — без змін (1).</summary>
        public static float Speed(string clip, RingIdleRole role)
        {
            return IsWaitingLoop(clip) ? SpeedFactor(role) : 1f;
        }
    }
}
