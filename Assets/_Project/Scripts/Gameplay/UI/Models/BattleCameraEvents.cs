using System.Collections.Generic;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Подієва камера бою без залипання (подача бою П7, docs/research/RT_COMBAT_PRESENTATION.md): на важливій події
    /// (смерть, здача, підкріплення) камера на мить летить до її учасника й **завжди** повертається до бійця, що
    /// ходить: щойно такти скінчились і минуло <see cref="HoldSeconds"/>, або, що б не сталося, через
    /// <see cref="MaxSeconds"/>. У референсі камера залипала на ворогові після черги пострілів — тут це
    /// неможливо за побудовою.
    ///
    /// П11 (будова камери RT, §1.1: черга завдань із пріоритетом): кілька подій поспіль (бочка поклала трьох) не
    /// перебивають одна одну, а стають у чергу — кожна отримує свій підліт і спостереження; важливіша подія
    /// перебиває поточну, у черзі не більше <see cref="MaxQueued"/>. Після останньої — назад до того, хто ходить.
    /// Чистий C#: арена лише летить до того, кого повертає <see cref="Tick"/>; охоронець — <c>BattlePresentationTests</c>.
    /// Числа — ПЛЕЙСХОЛДЕР.
    /// </summary>
    public sealed class BattleCameraEvents
    {
        public const float HoldSeconds = 0.6f;
        public const float MaxSeconds = 1.5f;
        public const int MaxQueued = 3;

        /// <summary>Пріоритети подій: смерть важливіша за здачу й підкріплення.</summary>
        public const int PriorityDeath = 3, PrioritySurrender = 2, PriorityReinforcement = 2, PriorityMinor = 1;

        private struct Pending
        {
            public string UnitId;
            public int Priority;
        }

        private readonly List<Pending> _queue = new List<Pending>();
        private string _eventUnitId;
        private int _eventPriority;
        private float _elapsed;

        /// <summary>Камера зараз дивиться на подію, а не на бійця, що ходить.</summary>
        public bool IsActive { get; private set; }

        /// <summary>Скільки подій чекає після поточної.</summary>
        public int QueuedCount => _queue.Count;

        /// <summary>Подія зі смертю (найвищий пріоритет) — так кликав П7.</summary>
        public void Focus(string unitId) => Focus(unitId, PriorityDeath);

        /// <summary>
        /// Подія з учасником <paramref name="unitId"/>. Камера вільна — подія одразу поточна. Нова подія важливіша за
        /// поточну — перебиває її (поточна не повертається). Інакше — у чергу за пріоритетом, без повторів того самого
        /// бійця; переповнена черга втрачає найменш важливу.
        /// </summary>
        public void Focus(string unitId, int priority)
        {
            if (string.IsNullOrEmpty(unitId)) return;
            if (!IsActive || priority > _eventPriority)
            {
                Start(unitId, priority);
                return;
            }
            if (unitId == _eventUnitId) return;
            for (int i = 0; i < _queue.Count; i++)
                if (_queue[i].UnitId == unitId)
                {
                    if (_queue[i].Priority >= priority) return;
                    _queue.RemoveAt(i);
                    break;
                }
            int at = _queue.Count;
            while (at > 0 && _queue[at - 1].Priority < priority) at--;
            _queue.Insert(at, new Pending { UnitId = unitId, Priority = priority });
            if (_queue.Count > MaxQueued) _queue.RemoveAt(_queue.Count - 1);
        }

        private void Start(string unitId, int priority)
        {
            _eventUnitId = unitId;
            _eventPriority = priority;
            _elapsed = 0f;
            IsActive = true;
        }

        /// <summary>
        /// Кого показувати цього кадру: учасника події, доки вона триває, далі — наступну з черги, інакше —
        /// <paramref name="activeUnitId"/>. <paramref name="busy"/> — ще йдуть такти (подію тримаємо, але не довше
        /// <see cref="MaxSeconds"/>).
        /// </summary>
        public string Tick(float deltaSeconds, string activeUnitId, bool busy)
        {
            if (!IsActive) return activeUnitId;
            _elapsed += deltaSeconds < 0f ? 0f : deltaSeconds;
            if (_elapsed >= MaxSeconds || (_elapsed >= HoldSeconds && !busy))
            {
                if (_queue.Count > 0)
                {
                    var next = _queue[0];
                    _queue.RemoveAt(0);
                    Start(next.UnitId, next.Priority);
                    return _eventUnitId;
                }
                IsActive = false;
                _eventUnitId = null;
                return activeUnitId;
            }
            return _eventUnitId;
        }

        public void Reset()
        {
            IsActive = false;
            _eventUnitId = null;
            _eventPriority = 0;
            _elapsed = 0f;
            _queue.Clear();
        }
    }

    /// <summary>
    /// Безпечна рамка камери (подача П10; будова камери RT §1.1: камера летить лише до того, хто поза кадром):
    /// центральна частина вільної від HUD області. Якщо боєць чи подія вже всередині — камера не смикається.
    /// </summary>
    public static class BattleCameraFraming
    {
        /// <summary>Частка вільної області (по кожній осі), всередині якої точка вважається «в кадрі».</summary>
        public const float SafeFraction = 0.6f;

        /// <summary>
        /// Чи точка екрана (<paramref name="x"/>, <paramref name="y"/> — у тих самих координатах, що поля) у безпечній
        /// рамці вільної області <paramref name="left"/>…<paramref name="right"/> × <paramref name="top"/>…<paramref name="bottom"/>.
        /// </summary>
        public static bool IsInsideSafeArea(float x, float y, float left, float top, float right, float bottom,
            float fraction = SafeFraction)
        {
            float w = right - left, h = bottom - top;
            if (w <= 0f || h <= 0f) return false;
            float mx = w * (1f - fraction) * 0.5f, my = h * (1f - fraction) * 0.5f;
            return x >= left + mx && x <= right - mx && y >= top + my && y <= bottom - my;
        }
    }
}
