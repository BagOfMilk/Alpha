using System;

namespace Game.Core.Balance
{
    /// <summary>
    /// Чисті формули прокачки. Не зберігає стану — на вхід рівень/досвід і
    /// конфіг, на вихід пороги і результат нарахування. Це спрощує unit-тести
    /// і гарантує, що бій і база рахують прогрес однаково.
    /// </summary>
    public static class ProgressionMath
    {
        /// <summary>Досвід, потрібний для переходу з рівня level на level+1.</summary>
        public static int XpToNext(int level, BalanceConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            if (level < 1) level = 1;
            var raw = cfg.XpBase * Math.Pow(level, cfg.XpExponent);
            return (int)Math.Round(raw);
        }

        /// <summary>
        /// Сукупний досвід, потрібний щоб досягти рівня targetLevel з нуля
        /// (корисно для передперегляду кривої і налагодження балансу).
        /// </summary>
        public static long TotalXpForLevel(int targetLevel, BalanceConfig cfg)
        {
            long total = 0;
            for (int l = 1; l < targetLevel; l++)
                total += XpToNext(l, cfg);
            return total;
        }

        /// <summary>
        /// Нараховує досвід і повертає, скільки рівнів отримано і залишок XP.
        /// Не перевищує cfg.MaxLevel: зайвий досвід «зрізається».
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
                xp = 0; // на максимальному рівні досвід не накопичується

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
