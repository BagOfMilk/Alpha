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

    /// <summary>Боевой RNG с сидом — детерминированный реплей одного боя.</summary>
    public sealed class SeededRng : IRng
    {
        private readonly Random _random;

        public SeededRng(int seed) => _random = new Random(seed);

        public int D100() => _random.Next(1, 101);
        public int Range(int minInclusive, int maxInclusive) => _random.Next(minInclusive, maxInclusive + 1);
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
