using System;
using Game.Core.Story;

namespace Game.Core.Balance
{
    /// <summary>
    /// Числа Готовності громади до фіналу (R8, пакет B6).
    ///
    /// ЯКОРЬ: Готовність — це «скільки громада встигла підготуватись», і росте
    /// вона ТІЛЬКИ від віх, які приносить гравець (вилазка, квест, стройка,
    /// відсутність страху, указ ради «Готуватись») — так само, як Напруга,
    /// друга шкала того ж роду (інваріант 2: не менше трьох активних
    /// накопичувачів різних ставок).
    ///
    /// ПОТРЕБИТЕЛЬ: <see cref="ReadinessBand"/> зсуває пороги фіналу
    /// (<c>Finale.BuildDam</c>/<c>Finale.BuildAssault</c>) — гірша Готовність,
    /// важчий фінал.
    /// </summary>
    [Serializable]
    public sealed class ReadinessBalance
    {
        /// <summary>
        /// Границі полос: нижче першої — Unprepared, вище останньої — Fortified.
        /// ПЛЕЙСХОЛДЕРИ, як і решта чисел тестової сборки.
        /// </summary>
        public int[] BandThresholds = { 25, 55, 90 };

        // ---- Віхи, які підвищують Готовність (ReadinessTickStep + D1) ----

        /// <summary>Вилазка завершилась Хорошою/Найкращою полосою.</summary>
        public int ExpeditionSuccessAmount = 12;

        /// <summary>Квест завершився (Succeeded) — застосовує D1 (квести поза конвеєром, R6).</summary>
        public int QuestDoneAmount = 15;

        /// <summary>Будівля добудована сьогодні (з <c>DayContext.CityEvents</c>, ключ <c>city.built.*</c>).</summary>
        public int BuildingCompletedAmount = 10;

        /// <summary>Доба без страху громади (<c>FearState.IsAfraid</c> == false).</summary>
        public int NoFearDayAmount = 2;

        /// <summary>Рада оголосила «Готуватись» (майбутній <c>council.prepare_threat.*</c>, B5).</summary>
        public int PrepareThreatAmount = 20;

        // ---- Фінал: пороги/склад ворога зсунуті полосою (§3.5, R8) ----

        /// <summary>«Загатити річку»: поріг Mechanics за полосою Готовності (Unprepared..Fortified).</summary>
        public int[] DamThresholdByBand = { 9, 7, 5, 3 };

        /// <summary>
        /// «Загатити річку»: поріг Tactics за полосою Готовності — ДРУГА перевірка
        /// тихого шляху фіналу (TEST_BUILD.md R8, §3.5 «перевірки Mechanics≥7/
        /// Tactics≥5», §7.15 <c>finale.option.quiet</c> — обидва навики в тексті).
        /// Тихий шлях фіналу — не одна перевірка, а дві: <see cref="Finale.BuildDam"/>
        /// (Mechanics) і <see cref="Finale.BuildDamTactics"/> (цей поріг). Індекс 1
        /// (Bracing) навмисно = 5 — узгоджено з буквальним прикладом §3.5.
        /// </summary>
        public int[] TacticsThresholdByBand = { 7, 5, 3, 2 };

        /// <summary>Скільки рядових ворогів у фінальному штурмі за полосою (Бурунда — завжди понад це).</summary>
        public int[] AssaultEnemyCountByBand = { 6, 5, 4, 3 };

        public ReadinessBand BandFor(int value)
        {
            var t = BandThresholds;
            if (t == null || t.Length == 0) return ReadinessBand.Unprepared;
            for (int i = 0; i < t.Length; i++)
                if (value < t[i]) return (ReadinessBand)i;
            return (ReadinessBand)t.Length;
        }

        internal static int Pick(int[] arr, int index, int fallback)
        {
            if (arr == null || arr.Length == 0) return fallback;
            if (index < 0) index = 0;
            if (index >= arr.Length) index = arr.Length - 1;
            return arr[index];
        }
    }
}
