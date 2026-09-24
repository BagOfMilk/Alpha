using Game.Core.Scenes;

namespace Game.Core.Companions
{
    /// <summary>
    /// Реєстр змісту глав арок (Поправка №7.8): <see cref="ArcChapter.QuestId"/>
    /// лишається просто ключем декаплінгу (див. коментар самого поля), а сам
    /// зміст може бути СЦЕНОЮ (Choice-сценарій, <see cref="Game.Core.Scenes"/>)
    /// АБО квестом (<see cref="Game.Core.Quests.QuestRun"/>) — цей клас каже
    /// GameSession, яким шляхом главу проганяти
    /// (<c>BeginArcChapterScene</c>/<c>BeginArcChapterQuest</c>).
    ///
    /// Лише дві живі главі цієї збірки квестові (Максим, гл.1 — «Не за
    /// кров», грається справжнім <c>CheckResolver</c>-квестом); решта —
    /// сцени з вибором.
    /// </summary>
    public static class CompanionArcContent
    {
        public const string MyroslavaId = "myroslava";
        public const string MaksymId = "maksym";

        public static Scene SceneFor(string companionId, string chapterId)
        {
            if (companionId == MyroslavaId && chapterId == "ch1") return CompanionScenes.MyroslavaTrustArc();
            if (companionId == MyroslavaId && chapterId == "ch2") return CompanionScenes.MyroslavaEpilogue();
            if (companionId == MaksymId && chapterId == "ch2") return CompanionScenes.MaksymEpilogue();
            return null;
        }

        public static bool IsQuestChapter(string companionId, string chapterId)
            => companionId == MaksymId && chapterId == "ch1";
    }
}
