using System;
using System.Collections.Generic;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Тости (docs/UX_DESIGN.md §5.15): лише успіх, по одному на екрані, 3–4 с,
    /// черга. Подія гри водночас іде в стрічку — тост її не замінює (UI-05).
    /// Годинник передається ззовні, тому черга детермінована в тестах.
    /// </summary>
    public sealed class UxToastQueue
    {
        public const double DefaultSeconds = 3.5;
        public const int MaxQueued = 4;

        private readonly Func<double> _clock;
        private readonly double _seconds;
        private readonly Queue<string> _pending = new Queue<string>();
        private string _current;
        private double _shownAt;

        public UxToastQueue(Func<double> clock, double seconds = DefaultSeconds)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _seconds = seconds > 0 ? seconds : DefaultSeconds;
        }

        /// <summary>Поставити тост у чергу; найстаріші з надлишку відкидаються (старе повідомлення менш цінне за свіже).</summary>
        public void Push(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            _pending.Enqueue(text);
            while (_pending.Count > MaxQueued) _pending.Dequeue();
        }

        /// <summary>Тост, який видно зараз, або null.</summary>
        public string Current
        {
            get
            {
                double now = _clock();
                if (_current != null && now - _shownAt >= _seconds) _current = null;
                if (_current == null && _pending.Count > 0)
                {
                    _current = _pending.Dequeue();
                    _shownAt = now;
                }
                return _current;
            }
        }

        public int PendingCount => _pending.Count;
    }

    /// <summary>
    /// Відмови біля своєї кнопки (UX-11): текст живе під дією, що відмовила,
    /// доки гравець не зробить наступну дію. Ключ — ідентифікатор дії.
    /// </summary>
    public sealed class UxInlineRefusals
    {
        private readonly Dictionary<string, string> _byAction = new Dictionary<string, string>(StringComparer.Ordinal);

        public void Record(string actionId, UxOutcome outcome)
        {
            if (string.IsNullOrEmpty(actionId) || outcome == null) return;
            _byAction.Clear();
            if (!outcome.Ok && !string.IsNullOrEmpty(outcome.Refusal)) _byAction[actionId] = outcome.Refusal;
        }

        public string For(string actionId)
        {
            string text;
            return actionId != null && _byAction.TryGetValue(actionId, out text) ? text : null;
        }

        public void Clear() => _byAction.Clear();
    }
}
