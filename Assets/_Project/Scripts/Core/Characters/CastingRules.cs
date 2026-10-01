using System.Collections.Generic;

namespace Game.Core.Characters
{
    /// <summary>
    /// Правила кастингу іменних персонажів, які можна перевірити машиною (Поправка №17.3,
    /// уточнює №2, №5.2, №12.9): частка українських ≈20 % від УСІХ іменних (власник, 01.10.2026:
    /// «УСІХ»; протагоніст — виняток і в каталог не входить) і «персонажі з російських першоджерел —
    /// лише вороги». Чисті функції над каталогом карток: тест перевіряє і справжній каталог, і
    /// навмисно зіпсовані копії (інакше охоронець не мав би зубів).
    /// </summary>
    public static class CastingRules
    {
        /// <summary>Скільки іменних має бути в каталозі, щоб вилка частки стала обов'язковою (ПЛЕЙСХОЛДЕР).</summary>
        public const int QuotaBandFromCount = 20;

        /// <summary>Вилка частки українських, коли іменних достатньо (ПЛЕЙСХОЛДЕР: «≈20 %» ± 5 п. п.).</summary>
        public const double QuotaMin = 0.15;
        public const double QuotaMax = 0.25;

        /// <summary>Частка карток з українським першоджерелом серед усіх; 0 для порожнього каталогу.</summary>
        public static double UkrainianShare(IReadOnlyCollection<CharacterCard> cards)
        {
            if (cards == null || cards.Count == 0) return 0.0;
            int ukrainian = 0;
            foreach (var card in cards)
                if (card.Culture == SourceCulture.Ukrainian) ukrainian++;
            return (double)ukrainian / cards.Count;
        }

        /// <summary>Скільки нових НЕукраїнських іменних треба, щоб при <paramref name="ukrainian"/> українських вийшло ≈20 %: 4×UA − наявні неукраїнські.</summary>
        public static int NonUkrainianNeeded(int ukrainian, int nonUkrainianPresent)
            => System.Math.Max(0, 4 * ukrainian - nonUkrainianPresent);

        /// <summary>Порушення правил кастингу в каталозі; порожньо — каталог чистий.</summary>
        public static List<string> Violations(IReadOnlyCollection<CharacterCard> cards)
        {
            var problems = new List<string>();
            if (cards == null) return problems;

            foreach (var card in cards)
            {
                if (card.IsBorrowed && card.Culture == SourceCulture.Unspecified)
                    problems.Add(card.Id + ": запозичена картка без культури першоджерела");
                // Ворог не може бути напарником (CharacterCard.CanBeCompanion), тож «лише ворог» уже
                // означає «ніколи не напарник і не переманюється».
                if (card.Culture == SourceCulture.Russian && !card.IsEnemy)
                    problems.Add(card.Id + ": російське першоджерело лише для ворога (Поправка №12.9)");
            }

            if (cards.Count >= QuotaBandFromCount)
            {
                double share = UkrainianShare(cards);
                if (share < QuotaMin || share > QuotaMax)
                    problems.Add("частка українських " + share.ToString("P0") + " поза вилкою " +
                                 QuotaMin.ToString("P0") + "–" + QuotaMax.ToString("P0") + " при " + cards.Count + " іменних");
            }
            return problems;
        }
    }
}
