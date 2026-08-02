using System;
using System.Collections.Generic;

namespace Game.Core.Combat
{
    /// <summary>Источник случайности боя. Инъецируется, чтобы тесты были детерминированы.</summary>
    public interface IRng
    {
        /// <summary>Равномерное целое 1..100 (ролл d100).</summary>
        int D100();

        /// <summary>Равномерное целое в [min..max] включительно.</summary>
        int Range(int minInclusive, int maxInclusive);
    }

    /// <summary>
    /// Сидируемый RNG (splitmix64): детерминированный реплей + СЕРИАЛИЗУЕМОЕ
    /// состояние (System.Random своё не отдаёт). Состояние — один ulong: сейв
    /// хранит его, чтобы загрузка ПРОДОЛЖАЛА поток случайностей, а не
    /// перезапускала его (анти-save-scum по инцидентам, айронмен-канон US-16.1).
    /// </summary>
    public sealed class SeededRng : IRng
    {
        /// <summary>Текущее состояние потока. Экспорт для сейва; восстановление — RestoreState.</summary>
        public ulong State { get; private set; }

        public SeededRng(int seed)
        {
            // Полный финализатор splitmix64: рвёт аффинную связь сид→состояние.
            // (Голое умножение на гамму NextRaw делало SeededRng(s+k) тем же
            // потоком, что SeededRng(s), со сдвигом на k вызовов.)
            ulong z = unchecked((ulong)seed * 0x9E3779B97F4A7C15UL + 0xBF58476D1CE4E5B9UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            State = z ^ (z >> 31);
            if (State == 0) State = 0x9E3779B97F4A7C15UL;
        }

        public void RestoreState(ulong state) => State = state == 0 ? 0x9E3779B97F4A7C15UL : state;

        private ulong NextRaw()
        {
            // splitmix64 (Steele/Lea/Flood) — быстрый, равномерный, один ulong состояния.
            ulong z = State += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public int D100() => Range(1, 100);

        public int Range(int minInclusive, int maxInclusive)
        {
            if (maxInclusive <= minInclusive) return minInclusive;
            ulong span = (ulong)((long)maxInclusive - minInclusive + 1);
            return minInclusive + (int)(NextRaw() % span);
        }
    }

    /// <summary>
    /// Скриптованный RNG для тестов: выдаёт заранее заданную последовательность.
    /// Когда очередь пуста — возвращает середину диапазона (предсказуемый дефолт).
    /// </summary>
    public sealed class ScriptedRng : IRng
    {
        private readonly Queue<int> _values;

        public ScriptedRng(params int[] values) => _values = new Queue<int>(values ?? new int[0]);

        public int D100() => _values.Count > 0 ? _values.Dequeue() : 50;

        public int Range(int minInclusive, int maxInclusive)
            => _values.Count > 0 ? _values.Dequeue() : (minInclusive + maxInclusive) / 2;
    }
}
