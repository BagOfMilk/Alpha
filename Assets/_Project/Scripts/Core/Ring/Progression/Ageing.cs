using System;

namespace Game.Core.Ring
{
    /// <summary>
    /// Тіло старіє (TRPG_DESIGN §5.5; черга П5, пункт 5; умови ПМ 12.11.2026): «після кількох сезонів витривалість росте важче;
    /// травми лишають сліди». Обидва ефекти — лише з сезону <see cref="FromSeason"/>, тож М1 (сезони 1–3) і темп кар'єри ті самі.
    /// Числа — ПЛЕЙСХОЛДЕР (на приймання власнику).
    /// <list type="bullet">
    /// <item>Витривалість дорожчає: +1 до ціни атрибута (і до кроку тренера витривалості) з сезону 4, ще +1 кожні
    /// <see cref="SeasonsPerStep"/> сезони, не більше <see cref="MaxStaminaSurcharge"/> (сезон 4–6: 3 очки, з сезону 7: 4).</item>
    /// <item>Слід травми: зона, що вже травмувалась раніше, з сезону 4 травмується на <see cref="ScarExtraWeeks"/> тиждень довше
    /// (і погіршується до стелі, довшої на стільки ж). Сліди записуються з сезону 1.</item>
    /// </list>
    /// Слід — довша травма, а не нижчий поріг: рядок «ушкодження {1}/{2}» на екрані лишається чесним.
    /// </summary>
    public static class Ageing
    {
        public const int FromSeason = 4;
        public const int SeasonsPerStep = 3;
        public const int MaxStaminaSurcharge = 2;
        public const int ScarExtraWeeks = 1;

        /// <summary>Чи тіло вже старіє цього сезону.</summary>
        public static bool IsOn(int season) { return season >= FromSeason; }

        /// <summary>Надбавка до ціни витривалості (очок розвитку) і до кроку тренера витривалості.</summary>
        public static int StaminaSurcharge(int season)
        {
            if (!IsOn(season)) return 0;
            return Math.Min(MaxStaminaSurcharge, 1 + (season - FromSeason) / SeasonsPerStep);
        }

        /// <summary>
        /// Скільки тижнів додає слід: <paramref name="earlierInjuries"/> — травм цієї зони ДО поточної (умова ПМ: перевіряти до
        /// інкременту).
        /// </summary>
        public static int ScarWeeks(int season, int earlierInjuries) { return IsOn(season) && earlierInjuries > 0 ? ScarExtraWeeks : 0; }
    }
}
