using System.Collections.Generic;

namespace Game.Core.Session
{
    /// <summary>
    /// Один запис стрічки подій сесії (§4.3 TEST_BUILD.md): <c>Key</c> —
    /// текстовий ключ конвенції "&lt;domain&gt;.&lt;action&gt;[.detail]" (R7,
    /// Core віддає лише ключі — гравцю текст показує E3/UkrainianText), Args —
    /// рядкові пари контексту ("band", "path", "companionId", ...).
    ///
    /// <see cref="GameSession.DayLog"/> — ЄДИНЕ публічне джерело доказу для
    /// тесту покриття (§6): кожен рядок §2 таблиці "одна гра" підтверджується
    /// подією з цього списку, а не читанням внутрішнього стану.
    /// </summary>
    public sealed class GameEvent
    {
        public readonly string Key;
        public readonly IReadOnlyDictionary<string, string> Args;
        public readonly int Day;
        public readonly Loop.DayPhase Phase;

        public GameEvent(string key, int day, Loop.DayPhase phase, IReadOnlyDictionary<string, string> args = null)
        {
            Key = key;
            Day = day;
            Phase = phase;
            Args = args ?? EmptyArgs;
        }

        private static readonly Dictionary<string, string> EmptyArgs = new Dictionary<string, string>();
    }
}
