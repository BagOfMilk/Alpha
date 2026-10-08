namespace Game.Gameplay.UI
{
    /// <summary>
    /// Подієва камера бою без залипання (подача бою П7, docs/research/RT_COMBAT_PRESENTATION.md): на важливій події
    /// (смерть, здача, підкріплення) камера на мить летить до її учасника й **завжди** повертається до бійця, що
    /// ходить: щойно такти скінчились і минуло <see cref="HoldSeconds"/>, або, що б не сталося, через
    /// <see cref="MaxSeconds"/>. У референсі камера залипала на ворогові після черги пострілів — тут це
    /// неможливо за побудовою. Чистий C#: арена лише летить до того, кого повертає <see cref="Tick"/>; охоронець —
    /// <c>BattlePresentationTests</c>. Числа — ПЛЕЙСХОЛДЕР.
    /// </summary>
    public sealed class BattleCameraEvents
    {
        public const float HoldSeconds = 0.6f;
        public const float MaxSeconds = 1.5f;

        private string _eventUnitId;
        private float _elapsed;

        /// <summary>Камера зараз дивиться на подію, а не на бійця, що ходить.</summary>
        public bool IsActive { get; private set; }

        /// <summary>Подія з учасником <paramref name="unitId"/>; нова подія перекриває попередню й починає відлік знову.</summary>
        public void Focus(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return;
            _eventUnitId = unitId;
            _elapsed = 0f;
            IsActive = true;
        }

        /// <summary>
        /// Кого показувати цього кадру: учасника події, доки вона триває, інакше — <paramref name="activeUnitId"/>.
        /// <paramref name="busy"/> — ще йдуть такти (подію тримаємо, але не довше <see cref="MaxSeconds"/>).
        /// </summary>
        public string Tick(float deltaSeconds, string activeUnitId, bool busy)
        {
            if (!IsActive) return activeUnitId;
            _elapsed += deltaSeconds < 0f ? 0f : deltaSeconds;
            if (_elapsed >= MaxSeconds || (_elapsed >= HoldSeconds && !busy))
            {
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
            _elapsed = 0f;
        }
    }
}
