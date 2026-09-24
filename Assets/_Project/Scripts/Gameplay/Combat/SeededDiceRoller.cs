using System.Globalization;
using Game.Core.Randomness;

namespace Game.Gameplay.Combat
{
    /// <summary>
    /// Сидированная реализация IDiceRoller (R1): splitmix64-подобный поток,
    /// чистый C#, БЕЗ UnityEngine — подключается к tools/Alpha.Play,
    /// tools/Alpha.Sim и headless-тестам прямым &lt;Compile Include&gt;, как
    /// уже сделано для SceneText.cs/SignalText.cs, и линтуется вместе со
    /// всем Gameplay/**/*.cs без исключения (файл не трогает движок).
    ///
    /// Состояние — один ulong: сейв хранит его строкой (CaptureState), чтобы
    /// загрузка ПРОДОЛЖАЛА поток случайностей, а не перезапускала его
    /// (анти-save-scum по инцидентам, тот же принцип, что у архивного SeededRng).
    ///
    /// streamId подмешивается в каждый ролл (через FNV-1a), но НЕ создаёт
    /// отдельных потоков состояния: счётчик всегда один и всегда продвигается,
    /// поэтому повторный вызов с тем же streamId детерминированно даёт другое
    /// число, а разные streamId — разные числа при одном и том же counter.
    /// Реплей воспроизводится только по порядку вызовов, что и требуется:
    /// один и тот же сид плюс одна и та же последовательность действий даёт
    /// один и тот же бой (детерминизм R1).
    /// </summary>
    public sealed class SeededDiceRoller : IDiceRoller
    {
        private const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;
        private const ulong Mix1 = 0xBF58476D1CE4E5B9UL;
        private const ulong Mix2 = 0x94D049BB133111EBUL;
        private const ulong FnvOffset = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        private ulong _state;

        public SeededDiceRoller(ulong seed)
        {
            _state = Finalize(seed);
            if (_state == 0) _state = GoldenGamma;
        }

        /// <summary>Полный финализатор splitmix64: рвёт аффинную связь сид→состояние.</summary>
        private static ulong Finalize(ulong seed)
        {
            unchecked
            {
                ulong z = seed * GoldenGamma + Mix1;
                z = (z ^ (z >> 30)) * Mix1;
                z = (z ^ (z >> 27)) * Mix2;
                return z ^ (z >> 31);
            }
        }

        public double Roll01(string streamId)
        {
            unchecked
            {
                _state += GoldenGamma;
                ulong z = _state ^ Hash(streamId);
                z = (z ^ (z >> 30)) * Mix1;
                z = (z ^ (z >> 27)) * Mix2;
                z ^= z >> 31;
                // 53 бита мантиссы -> [0,1); тот же трюк, что у большинства
                // сидируемых генераторов double из целого потока.
                return (z >> 11) * (1.0 / (1UL << 53));
            }
        }

        private static ulong Hash(string s)
        {
            unchecked
            {
                ulong h = FnvOffset;
                if (s == null) return h;
                for (int i = 0; i < s.Length; i++)
                {
                    h ^= s[i];
                    h *= FnvPrime;
                }
                return h;
            }
        }

        public string CaptureState() => _state.ToString(CultureInfo.InvariantCulture);

        public void RestoreState(string blob)
        {
            if (ulong.TryParse(blob, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                _state = v == 0 ? GoldenGamma : v;
        }
    }
}
