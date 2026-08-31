using System;

namespace Game.Core.Balance
{
    /// <summary>
    /// Числа «Пульса мира» — накопителей давления, заменяющих бросок кубика.
    ///
    /// ЯКОРЬ: порог 100 → дней до вмешательства ≈ 100 / Настойчивость.
    /// Настойчивость 10 = «примерно раз в десять дней». Это якорь для дизайнера;
    /// игрок вместо числа получает лестницу предвестников.
    /// </summary>
    [Serializable]
    public sealed class PulseBalance
    {
        public int DefaultThreshold = 100;

        /// <summary>Доля заполнения, с которой идёт предвестник 1-й ступени (амбиент).</summary>
        public double Forewarn1At = 0.55;
        /// <summary>2-я ступень: обязана назвать домен и место.</summary>
        public double Forewarn2At = 0.80;
        /// <summary>3-я ступень: названа близость, но НИКОГДА не точный день.</summary>
        public double Forewarn3At = 0.95;

        public int MaxFiresPerDay = 1;
        /// <summary>Ночью инциденты кучнее (US-11.1).</summary>
        public int MaxFiresPerNight = 2;

        /// <summary>Потолок заряда, чтобы простой не копил бесконечную очередь.</summary>
        public int ChargeCapMultiplier = 2;

        /// <summary>
        /// ИНВАРИАНТ, а не совет: меньше трёх одновременных накопителей —
        /// и полный детерминизм читается насквозь. Проверяется тестом.
        /// </summary>
        public int MinActiveTracks = 3;
    }
}
