using System;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Уповільнення на вбивстві (подача бою П6, docs/research/RT_COMBAT_PRESENTATION.md): коротке, у реальному
    /// часі, **ніколи не стакається** (кілька вбивств поспіль лише продовжують вікно, глибина та сама) і вимикається
    /// в налаштуваннях. У референсі гравці скаржились саме на стакання й на те, що вимкнути не можна. Чистий C#:
    /// арена бою лише ставить <c>Time.timeScale</c> з <see cref="TimeScale"/>; охоронець — <c>BattlePresentationTests</c>.
    /// Числа — ПЛЕЙСХОЛДЕР.
    /// </summary>
    public sealed class KillSlowMo
    {
        /// <summary>Темп часу у вікні уповільнення.</summary>
        public const float Scale = 0.35f;

        /// <summary>Скільки реальних секунд триває вікно від останнього вбивства.</summary>
        public const float DurationSeconds = 0.45f;

        /// <summary>
        /// Режим «рідко»: після вікна наступне не раніше ніж за стільки реальних секунд (розбір BG3, 08.10.2026: у
        /// кінематографічних кадрів бою обмежена частота, щоб ефект не набрид).
        /// </summary>
        public const float RareGapSeconds = 8f;

        private float _until = float.NegativeInfinity;

        /// <summary>Налаштування гравця (меню паузи).</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Пауза між вікнами: 0 — щоразу, <see cref="RareGapSeconds"/> — рідко.</summary>
        public float MinGapSeconds { get; set; }

        /// <summary>
        /// Вбивство в реальну секунду <paramref name="nowReal"/>: у вікні — вікно продовжується, глибина не росте; одразу
        /// після вікна, ближче ніж <see cref="MinGapSeconds"/>, — уповільнення немає.
        /// </summary>
        public void Trigger(float nowReal)
        {
            if (!Enabled) return;
            if (nowReal >= _until && nowReal - _until < MinGapSeconds) return;
            _until = Math.Max(_until, nowReal + DurationSeconds);
        }

        /// <summary>Темп часу в реальну секунду <paramref name="nowReal"/>: <see cref="Scale"/> у вікні, 1 поза ним.</summary>
        public float TimeScale(float nowReal) => Enabled && nowReal < _until ? Scale : 1f;

        /// <summary>
        /// Вибіркове уповільнення (П13; будова RT §1.1): множник власного темпу того, хто вбив, поверх загального —
        /// у вікні він компенсує <see cref="Scale"/> і боєць добиває в звичайному темпі, поки світ гальмує.
        /// </summary>
        public float ActorScale(float nowReal) => TimeScale(nowReal) < 1f ? 1f / Scale : 1f;

        /// <summary>Бій скінчився чи пауза: темп одразу 1.</summary>
        public void Reset() => _until = float.NegativeInfinity;

        /// <summary>Типове значення налаштування: на Низькій графіці (Статут PERF-01) — вимкнено.</summary>
        public static bool DefaultEnabled(bool lowGraphics) => !lowGraphics;
    }
}
