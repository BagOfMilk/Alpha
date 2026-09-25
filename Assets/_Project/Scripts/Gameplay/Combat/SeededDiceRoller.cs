using System.Globalization;
using Game.Core.Randomness;

namespace Game.Gameplay.Combat
{
    /// <summary>
    /// Сидована реалізація IDiceRoller (R1): splitmix64-подібний потік,
    /// чистий C#, БЕЗ UnityEngine — підключається до tools/Alpha.Play,
    /// tools/Alpha.Sim і headless-тестам прямим &lt;Compile Include&gt;, як
    /// і UkrainianText.cs (пакет E3b), і лінтується разом з
    /// усім Gameplay/**/*.cs без винятку (файл не займає рушій).
    ///
    /// Стан — один ulong: сейв зберігає його рядком (CaptureState), щоб
    /// завантаження ПРОДОВЖУВАЛО потік випадковостей, а не перезапускало його
    /// (анти-save-scum за інцидентами, той самий принцип, що й у архівного SeededRng).
    ///
    /// streamId підмішується в кожен ролл (через FNV-1a), але НЕ створює
    /// окремих потоків стану: лічильник завжди один і завжди просувається,
    /// тому повторний виклик з тим самим streamId детерміновано дає інше
    /// число, а різні streamId — різні числа при тому самому counter.
    /// Реплей відтворюється лише за порядком викликів, що й вимагається:
    /// той самий сид плюс та сама послідовність дій дає
    /// той самий бій (детермінізм R1).
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

        /// <summary>Повний фіналізатор splitmix64: рве афінний зв'язок сид→стан.</summary>
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
                // 53 біти мантиси -> [0,1); той самий трюк, що й у більшості
                // сидованих генераторів double з цілого потоку.
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
