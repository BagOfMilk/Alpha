using System.Collections.Generic;

namespace Game.Core.Characters
{
    /// <summary>
    /// Кастинг открытия «Перевал» (docs/FIRST_HOUR.md §2.1).
    ///
    /// Первоисточник — повесть Ивана Франко «Захар Беркут» (1883); автор умер
    /// в 1916 году, произведение в общественном достоянии. Берутся имя и ядро
    /// характера (Поправка №2). Экранизации 1971 и 2019 годов охраняемые и не
    /// используются ни для чего.
    ///
    /// Переосмысление среза: захватчик — ФРАКЦИЯ, а не народ. В повести враг
    /// этнически маркирован; у нас это «орда из-за хребта», внешняя банда.
    /// Это правило среза, а не оговорка.
    ///
    /// Данные, а не код: карточки правит автор, не программист.
    /// </summary>
    public static class OpeningCast
    {
        public const string Work = "Иван Франко, «Захар Беркут» (1883), общественное достояние";

        /// <summary>Сын старейшины: бьётся за общину, против крови ради мести.</summary>
        public static CharacterCard Maksym() => new CharacterCard(
            "maksym", "Максим Беркут", SourceTier.Literary, Work,
            "сын старейшины; бьётся за общину, а не за месть");

        /// <summary>Дочь боярина, переходит к общине по ценностям.</summary>
        public static CharacterCard Myroslava() => new CharacterCard(
            "myroslava", "Мирослава", SourceTier.Literary, Work,
            "честь выше рода: уходит от отца к общине");

        /// <summary>
        /// Именной антагонист. Появляется трижды: сценой до боя, сигналом на
        /// третьи сутки, лицом в финале — потому и нужна карточка, иначе это
        /// три не связанные строки (чеклист §3 стр. 17).
        /// </summary>
        public static CharacterCard TuharVovk() => new CharacterCard(
            "tuhar", "Тугар Волк", SourceTier.Literary, Work,
            "боярин-чужак: хочет править по-новому и готов договориться с ордой");

        /// <summary>Голос общины на совете. В повести умирает в эпилоге — здесь доживает до финала.</summary>
        public static CharacterCard Zakhar() => new CharacterCard(
            "zakhar", "Захар Беркут", SourceTier.Literary, Work,
            "голос общины: решает сходом, а не приказом");

        /// <summary>Кладовщик из фольклорного яруса — с одной навязчивой чертой (Поправка №5.2).</summary>
        public static CharacterCard Keeper() => new CharacterCard(
            "keeper", "Дід Овсій", SourceTier.Folklore, "украинская народная традиция (общественное достояние)",
            "считает каждый мешок вслух и не верит ничьим цифрам");

        /// <summary>Лекарка фольклорного яруса.</summary>
        public static CharacterCard Healer() => new CharacterCard(
            "healer", "Знахарка Гафія", SourceTier.Folklore, "украинская народная традиция (общественное достояние)",
            "лечит всех, но каждому говорит правду в лицо");

        /// <summary>
        /// Командир орды — босс финала. Первоисточник ОТКРЫТ (§5.7): в срезе он
        /// появляется только именем в сигнале боярина, лицом — в финале, до
        /// которого срез не доходит. Карточка-заглушка честнее пустоты: видно,
        /// что персонаж заявлен и чего ему не хватает.
        /// </summary>
        public static CharacterCard HordeCommander() => new CharacterCard(
            "horde_commander", "Командир орды", SourceTier.Original, null,
            "ПЛЕЙСХОЛДЕР: первоисточник не выбран (открытый вопрос §5.7)");

        /// <summary>Весь кастинг открытия.</summary>
        public static List<CharacterCard> All() => new List<CharacterCard>
        {
            Maksym(), Myroslava(), TuharVovk(), Zakhar(), Keeper(), Healer(), HordeCommander()
        };

        /// <summary>Кто может быть напарником: фольклорные — не могут (нет арки).</summary>
        public static List<CharacterCard> CompanionCandidates()
        {
            var result = new List<CharacterCard>();
            foreach (var card in All())
                if (card.CanBeCompanion && card.Id != "horde_commander") result.Add(card);
            return result;
        }
    }
}
