using Game.Core.Characters;

namespace Game.Core.Companions
{
    /// <summary>
    /// Сид-арки напарників тестової збірки «Перевал» (US-9.5, R2/§1.1: "зрада й
    /// арки — тут, поруч із Лояльністю"). Лише ідентифікатори/ключі контенту —
    /// сам квест (B6/Core.Quests) і текст (E3) не входять до цього пакета,
    /// декаплінг рядком (<see cref="ArcChapter.QuestId"/>/<see cref="ArcChapter.TitleKey"/>).
    /// Обидві арки — дві глави, гейтяться Steady -> Devoted, як і архівний зразок.
    /// </summary>
    public static class DefaultArcs
    {
        /// <summary>
        /// Арка Мирослави: вона — кандидат на зраду з доби 1 (старт Wary,
        /// docs/TEST_BUILD.md §3.0), арка — шлях лишитися і довести довіру.
        /// </summary>
        public static CompanionArc Myroslava()
        {
            return new CompanionArc("arc_myroslava", "myroslava", "arc.myroslava.title")
                .Chapter(new ArcChapter("ch1", "quest.myroslava.ch1", "arc.myroslava.ch1.title")
                    .Loyalty(LoyaltyBand.Steady).SetsFlag("arc_myroslava_ch1"))
                .Chapter(new ArcChapter("ch2", "quest.myroslava.ch2", "arc.myroslava.ch2.title")
                    .Loyalty(LoyaltyBand.Devoted).NeedsFlag("arc_myroslava_ch1").SetsFlag("arc_myroslava_done"));
        }

        /// <summary>Арка Максима: вірність громаді понад помсту (docs/TEST_BUILD.md §3.0).</summary>
        public static CompanionArc Maksym()
        {
            return new CompanionArc("arc_maksym", "maksym", "arc.maksym.title")
                .Chapter(new ArcChapter("ch1", "quest.maksym.ch1", "arc.maksym.ch1.title")
                    .Loyalty(LoyaltyBand.Steady).SetsFlag("arc_maksym_ch1"))
                .Chapter(new ArcChapter("ch2", "quest.maksym.ch2", "arc.maksym.ch2.title")
                    .Loyalty(LoyaltyBand.Devoted).NeedsFlag("arc_maksym_ch1").SetsFlag("arc_maksym_done"));
        }

        public static System.Collections.Generic.IEnumerable<CompanionArc> All()
        {
            yield return Myroslava();
            yield return Maksym();
        }
    }
}
