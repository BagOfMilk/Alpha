using System;
using System.Collections.Generic;

namespace Game.Gameplay.Ring.Models
{
    /// <summary>Криві руху меню (ТЗ B08 §3): вхід — з перельотом, вихід — розгін, фокус — м'яке гальмування.</summary>
    public enum Ease { Linear = 0, OutCubic, InCubic, InOutSine, OutBack }

    /// <summary>Чисті функції руху меню без рушія: значення кривої, крок анімації, тремтіння помилки.</summary>
    public static class MenuMotion
    {
        /// <summary>Значення кривої в точці t∈[0;1]. OutBack — переліт <paramref name="overshoot"/> (за токеном 1,25 ≈ 8 % перельоту).</summary>
        public static float Eval(Ease e, float t, float overshoot = BrandTokens.MotionStingerOvershoot)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            switch (e)
            {
                case Ease.OutCubic: { float u = 1f - t; return 1f - u * u * u; }
                case Ease.InCubic: return t * t * t;
                case Ease.InOutSine: return 0.5f - 0.5f * (float)Math.Cos(Math.PI * t);
                case Ease.OutBack:
                {
                    // Стандартна OutBack: s=1.70158 дає ≈10 % перельоту; беремо s так, щоб пік був overshoot−1 від шляху / 3.
                    float s = 1.70158f * Math.Max(0f, (overshoot - 1f) / 0.25f);
                    float u = t - 1f;
                    return 1f + (s + 1f) * u * u * u + s * u * u;
                }
                default: return t;
            }
        }

        /// <summary>Тремтіння помилки: зсув по X, що згасає за <paramref name="durationMs"/> (3 коливання, амплітуда в одиницях панелі).</summary>
        public static float Shake(float elapsedMs, float durationMs, float amplitude)
        {
            if (elapsedMs <= 0f || elapsedMs >= durationMs || durationMs <= 0f) return 0f;
            float t = elapsedMs / durationMs;
            return amplitude * (1f - t) * (float)Math.Sin(t * Math.PI * 6.0);
        }

        /// <summary>Затримка входу i-ї пластини (стагер).</summary>
        public static float StaggerMs(int index) { return index * BrandTokens.MotionMenuStaggerMs; }
    }

    /// <summary>Одна анімація числа: від, до, тривалість, затримка, крива. Хід — <see cref="Value"/> за часом від старту.</summary>
    public struct MenuTween
    {
        public float From, To, DurationMs, DelayMs;
        public Ease Curve;

        public MenuTween(float from, float to, float durationMs, Ease curve, float delayMs = 0f)
        {
            From = from; To = to; DurationMs = durationMs; Curve = curve; DelayMs = delayMs;
        }

        public bool Done(float elapsedMs) { return elapsedMs >= DelayMs + DurationMs; }

        public float Value(float elapsedMs)
        {
            float t = DurationMs <= 0f ? 1f : (elapsedMs - DelayMs) / DurationMs;
            return From + (To - From) * MenuMotion.Eval(Curve, t);
        }
    }

    /// <summary>
    /// Навігація фокусом по списку пунктів (клавіатура, геймпад, колесо): по колу, неактивні пропускаються. Миша ставить фокус
    /// наведенням (<see cref="Set"/>).
    /// </summary>
    public sealed class MenuFocus
    {
        private readonly List<bool> _enabled = new List<bool>();

        public int Index { get; private set; }
        public int Count { get { return _enabled.Count; } }

        public void Reset(IList<bool> enabled, int preferred)
        {
            _enabled.Clear();
            _enabled.AddRange(enabled);
            Index = -1;
            if (_enabled.Count == 0) return;
            Index = Clamp(preferred);
            if (!_enabled[Index]) Move(1);
        }

        public bool Set(int index)
        {
            if (index < 0 || index >= _enabled.Count || !_enabled[index] || index == Index) return false;
            Index = index;
            return true;
        }

        /// <summary>Крок фокусу (+1 вниз, −1 вгору). true — фокус змінився (звук «курсор»).</summary>
        public bool Move(int dir)
        {
            if (_enabled.Count == 0) return false;
            int start = Index < 0 ? 0 : Index;
            int i = start;
            for (int n = 0; n < _enabled.Count; n++)
            {
                i = (i + dir + _enabled.Count) % _enabled.Count;
                if (!_enabled[i]) continue;
                bool changed = i != Index;
                Index = i;
                return changed;
            }
            return false;
        }

        private int Clamp(int i) { return i < 0 ? 0 : i >= _enabled.Count ? _enabled.Count - 1 : i; }
    }

    /// <summary>
    /// Прогрес екрана завантаження (ТЗ B08 §6): реальні кроки з вагами, показ ніколи не йде назад і не стрибає (доганяє ціль
    /// з обмеженою швидкістю), мінімальний час показу — щоб екран не блимав, і поради, що змінюються за часом.
    /// </summary>
    public sealed class LoadingProgress
    {
        private readonly float[] _weights;
        private int _done;
        private float _shown;

        public float ElapsedMs { get; private set; }
        public float MinShowMs { get; private set; }

        public LoadingProgress(IList<float> weights, float minShowMs)
        {
            _weights = new float[weights.Count];
            float sum = 0f;
            for (int i = 0; i < weights.Count; i++) sum += Math.Max(0.0001f, weights[i]);
            for (int i = 0; i < weights.Count; i++) _weights[i] = Math.Max(0.0001f, weights[i]) / sum;
            MinShowMs = minShowMs;
        }

        public int StepCount { get { return _weights.Length; } }
        public int StepsDone { get { return _done; } }

        /// <summary>Частка виконаної роботи (за вагами), 0…1.</summary>
        public float Target
        {
            get
            {
                float t = 0f;
                for (int i = 0; i < _done && i < _weights.Length; i++) t += _weights[i];
                return _done >= _weights.Length ? 1f : Math.Min(1f, t);
            }
        }

        /// <summary>Показаний прогрес 0…1 (плавний).</summary>
        public float Shown { get { return _shown; } }

        public void CompleteStep() { if (_done < _weights.Length) _done++; }

        /// <summary>
        /// Етап за ПОКАЗАНОЮ шкалою (0…кроків−1): підпис «Етап N / M» іде разом зі шкалою, а не з роботою, яка на швидкій
        /// машині закінчується раніше (кадр огляду 08.11.2026: «Етап 4 / 4» при 31 %).
        /// </summary>
        public int StepAtShown()
        {
            float cum = 0f;
            for (int i = 0; i < _weights.Length; i++)
            {
                cum += _weights[i];
                if (_shown < cum - 0.0001f) return i;
            }
            return Math.Max(0, _weights.Length - 1);
        }

        public bool WorkDone { get { return _done >= _weights.Length; } }

        /// <summary>Можна йти далі: робота готова, показ дійшов до 100 % і минув мінімальний час.</summary>
        public bool CanExit { get { return WorkDone && _shown >= 0.999f && ElapsedMs >= MinShowMs; } }

        /// <summary>
        /// Хід часу. Показ доганяє ціль не швидше, ніж за мінімальний час пройшов би весь шлях (тоді швидке завантаження
        /// не блимає 0→100 за кадр), але й не повільніше за 0,9 с на залишок, коли робота вже готова.
        /// </summary>
        public void Tick(float dtMs)
        {
            if (dtMs < 0f) dtMs = 0f;
            ElapsedMs += dtMs;
            float target = Target;
            float rate = 1f / Math.Max(1f, MinShowMs * 0.85f);                 // частка за мс
            if (WorkDone) rate = Math.Max(rate, (1f - _shown) / 900f);
            float next = _shown + rate * dtMs;
            _shown = next > target ? target : next;
        }

        /// <summary>Номер поради для показу: змінюється кожні <paramref name="everyMs"/> мс, починаючи з <paramref name="first"/>.</summary>
        public static int TipIndex(float elapsedMs, float everyMs, int first, int count)
        {
            if (count <= 0) return -1;
            int k = everyMs <= 0f ? 0 : (int)(elapsedMs / everyMs);
            return ((first + k) % count + count) % count;
        }
    }
}
