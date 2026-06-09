using System;

namespace Game.Core.Balance
{
    /// <summary>
    /// Чистые формулы прокачки. Не хранит состояния — на вход уровень/опыт и
    /// конфиг, на выход пороги и результат начисления. Это упрощает unit-тесты
    /// и гарантирует, что бой и база считают прогресс одинаково.
    /// </summary>
    public static class ProgressionMath
    {
        /// <summary>Опыт, необходимый для перехода с уровня level на level+1.</summary>
        public static int XpToNext(int level, BalanceConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            if (level < 1) level = 1;
            var raw = cfg.XpBase * Math.Pow(level, cfg.XpExponent);
            return (int)Math.Round(raw);
        }

        /// <summary>
        /// Совокупный опыт, необходимый чтобы достичь уровня targetLevel с нуля
        /// (полезно для предпросмотра кривой и отладки баланса).
        /// </summary>
        public static long TotalXpForLevel(int targetLevel, BalanceConfig cfg)
        {
            long total = 0;
            for (int l = 1; l < targetLevel; l++)
                total += XpToNext(l, cfg);
            return total;
        }

        /// <summary>
        /// Начисляет опыт и возвращает, сколько уровней получено и остаток XP.
        /// Не превышает cfg.MaxLevel: лишний опыт «срезается».
        /// </summary>
        public static LevelUpResult GrantXp(int level, int currentXp, int gainedXp, BalanceConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            if (level < 1) level = 1;
            if (gainedXp < 0) gainedXp = 0;

            int xp = currentXp + gainedXp;
            int levelsGained = 0;

            while (level < cfg.MaxLevel)
            {
                int threshold = XpToNext(level, cfg);
                if (xp < threshold) break;
                xp -= threshold;
                level++;
                levelsGained++;
            }

            if (level >= cfg.MaxLevel)
                xp = 0; // на максимальном уровне опыт не копится

            return new LevelUpResult(level, xp, levelsGained);
        }

        public readonly struct LevelUpResult
        {
            public readonly int Level;
            public readonly int RemainderXp;
            public readonly int LevelsGained;

            public LevelUpResult(int level, int remainderXp, int levelsGained)
            {
                Level = level;
                RemainderXp = remainderXp;
                LevelsGained = levelsGained;
            }

            public bool LeveledUp => LevelsGained > 0;
        }
    }
}
