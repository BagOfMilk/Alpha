using System;
using System.Collections.Generic;
using Game.Core.Dungeons;
using Game.Core.Quests;
using Game.Core.Scenes;
using Game.Core.Story;

namespace Game.Core.Session
{
    /// <summary>
    /// «Відлуння рішень» (ROADMAP M1.2): рядки підсумку, які збираються з сюжетних прапорів — що громада
    /// запам'ятала з вибору гравця. Закриває прапори виборів у портретних сценах, квестах і данжі, що
    /// раніше лише ставилися й нічого не міняли (<c>docs/STORY_FLAGS.md</c>): тепер кожен із них
    /// видимий у підсумку доби 5.
    ///
    /// ЧИСТА ФУНКЦІЯ: прапори й завершені арки на вході, ключі тексту на виході (текст живе в
    /// <c>UkrainianText</c> під ключем <see cref="TextKeyPrefix"/> + id). Порядок виводу фіксований
    /// (Мирослава → Максим → світ), детермінований — без випадковості (інваріант 1). Жодної нової
    /// шкали: це не лічильник, а читання вже виставлених прапорів (інваріант 6). Епілоги глав 2 рахуються,
    /// лише коли арку справді завершено (<paramref name="isArcCompleted"/>): вибір до фіналу глави не
    /// відлунює.
    /// </summary>
    public static class StoryEchoes
    {
        /// <summary>Префікс ключа тексту рядка: «summary.echo.&lt;id&gt;».</summary>
        public const string TextKeyPrefix = "summary.echo.";

        public const string MyroslavaTrusted = "myroslava_trusted";
        public const string MyroslavaWatched = "myroslava_watched";
        public const string MyroslavaSentAway = "myroslava_sent_away";
        public const string MyroslavaAsked = "myroslava_asked";
        public const string MyroslavaHint = "myroslava_hint";
        public const string MyroslavaCheckupReassure = "myroslava_checkup_reassure";
        public const string MyroslavaCheckupSpace = "myroslava_checkup_space";
        public const string MyroslavaCh2Remember = "myroslava_ch2_remember";
        public const string MyroslavaCh2Silence = "myroslava_ch2_silence";
        public const string MaksymRevengeClean = "maksym_revenge_clean";
        public const string MaksymCh2Forgive = "maksym_ch2_forgive";
        public const string MaksymCh2Guard = "maksym_ch2_guard";
        public const string AbandonedCampGrainTaken = "abandoned_camp_grain_taken";

        /// <summary>Усі id рядків у порядку виводу — для тестів і перевірки тексту (кожен має ключ перекладу).</summary>
        public static IReadOnlyList<string> AllIds { get; } = new[]
        {
            MyroslavaTrusted, MyroslavaWatched, MyroslavaSentAway, MyroslavaHint, MyroslavaAsked,
            MyroslavaCheckupReassure, MyroslavaCheckupSpace, MyroslavaCh2Remember, MyroslavaCh2Silence,
            MaksymRevengeClean, MaksymCh2Forgive, MaksymCh2Guard, AbandonedCampGrainTaken
        };

        /// <summary>
        /// Ключі тексту рядків підсумку за виставленими прапорами. <paramref name="isArcCompleted"/> —
        /// «чи завершив напарник (id) особисту арку»; <c>null</c> = ніхто не завершив.
        /// </summary>
        public static IReadOnlyList<string> Collect(StoryFlags flags, Func<string, bool> isArcCompleted = null)
        {
            var ids = new List<string>();
            if (flags == null) return ids;

            // --- Мирослава: розмова про батька (доба 1) ---
            if (flags.Get(OpeningScenes.MyroslavaHintFlag)) ids.Add(MyroslavaHint);
            else if (flags.Get(OpeningScenes.MyroslavaAskedFlag)) ids.Add(MyroslavaAsked); // питав, але вона відмовчалась

            // --- Мирослава: глава 1 арки (доба 2) ---
            if (flags.Get(CompanionScenes.MyroslavaTrustedFlag)) ids.Add(MyroslavaTrusted);
            if (flags.Get(CompanionScenes.MyroslavaWatchedFlag)) ids.Add(MyroslavaWatched);
            if (flags.Get(CompanionScenes.MyroslavaSentAwayFlag)) ids.Add(MyroslavaSentAway);

            // --- Мирослава: перевірка стану (доба 3, коли зрада не насувається) ---
            if (flags.Get(CompanionScenes.MyroslavaCheckupReassureFlag)) ids.Add(MyroslavaCheckupReassure);
            if (flags.Get(CompanionScenes.MyroslavaCheckupSpaceFlag)) ids.Add(MyroslavaCheckupSpace);

            // --- Мирослава: глава 2 (епілог довіри) — лише коли арку завершено ---
            if (Completed(isArcCompleted, "myroslava"))
            {
                if (flags.Get(CompanionScenes.MyroslavaCh2RememberFlag)) ids.Add(MyroslavaCh2Remember);
                if (flags.Get(CompanionScenes.MyroslavaCh2SilenceFlag)) ids.Add(MyroslavaCh2Silence);
            }

            // --- Максим ---
            if (flags.Get(DefaultQuests.MaksymCh1RevengeCleanFlag)) ids.Add(MaksymRevengeClean);
            if (Completed(isArcCompleted, "maksym"))
            {
                if (flags.Get(CompanionScenes.MaksymCh2ForgiveFlag)) ids.Add(MaksymCh2Forgive);
                if (flags.Get(CompanionScenes.MaksymCh2GuardFlag)) ids.Add(MaksymCh2Guard);
            }

            // --- Світ ---
            if (flags.Get(DefaultDungeon.AbandonedCampGrainTakenFlag)) ids.Add(AbandonedCampGrainTaken);

            var keys = new List<string>(ids.Count);
            foreach (var id in ids) keys.Add(TextKeyPrefix + id);
            return keys;
        }

        private static bool Completed(Func<string, bool> isArcCompleted, string companionId)
            => isArcCompleted != null && isArcCompleted(companionId);
    }
}
