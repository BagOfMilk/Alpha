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

        private float _until = float.NegativeInfinity;

        /// <summary>Налаштування гравця (меню паузи).</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Вбивство в реальну секунду <paramref name="nowReal"/>: вікно продовжується, глибина не росте.</summary>
        public void Trigger(float nowReal)
        {
            if (!Enabled) return;
            _until = Math.Max(_until, nowReal + DurationSeconds);
        }

        /// <summary>Темп часу в реальну секунду <paramref name="nowReal"/>: <see cref="Scale"/> у вікні, 1 поза ним.</summary>
        public float TimeScale(float nowReal) => Enabled && nowReal < _until ? Scale : 1f;

        /// <summary>Бій скінчився чи пауза: темп одразу 1.</summary>
        public void Reset() => _until = float.NegativeInfinity;

        /// <summary>Типове значення налаштування: на Низькій графіці (Статут PERF-01) — вимкнено.</summary>
        public static bool DefaultEnabled(bool lowGraphics) => !lowGraphics;
    }
}
