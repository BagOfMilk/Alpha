using System.Collections.Generic;

namespace Game.Core.Characters
{
    /// <summary>
    /// Кастинг відкриття «Перевал» (docs/FIRST_HOUR.md §2.1).
    ///
    /// Першоджерело — повість Івана Франка «Захар Беркут» (1883); автор помер
    /// у 1916 році, твір у суспільному надбанні. Беруться ім'я і ядро
    /// характеру (Поправка №2). Екранізації 1971 і 2019 років охоронювані і не
    /// використовуються ні для чого.
    ///
    /// Переосмислення зрізу: загарбник — ФРАКЦІЯ, а не народ. У повісті ворог
    /// етнічно маркований; у нас це «орда з-за хребта», зовнішня банда.
    /// Це правило зрізу, а не обмовка.
    ///
    /// Дані, а не код: картки править автор, не програміст.
    /// </summary>
    public static class OpeningCast
    {
        public const string Work = "Иван Франко, «Захар Беркут» (1883), общественное достояние";

        /// <summary>Син старійшини: б'ється за громаду, проти крові заради помсти.</summary>
        public static CharacterCard Maksym() => new CharacterCard(
            "maksym", "Максим Беркут", SourceTier.Literary, Work,
            "сын старейшины; бьётся за общину, а не за месть");

        /// <summary>Донька боярина, переходить до громади за цінностями.</summary>
        public static CharacterCard Myroslava() => new CharacterCard(
            "myroslava", "Мирослава", SourceTier.Literary, Work,
            "честь выше рода: уходит от отца к общине");

        /// <summary>
        /// Іменний антагоніст. З'являється тричі: сценою до бою, сигналом на
        /// треті доби, обличчям у фіналі — тому й потрібна картка, інакше це
        /// три не пов'язані рядки (чекліст §3 стор. 17).
        /// </summary>
        public static CharacterCard TuharVovk() => new CharacterCard(
            "tuhar", "Тугар Вовк", SourceTier.Literary, Work,
            "боярин-чужак: хочет править по-новому и готов договориться с ордой");

        /// <summary>Голос громади на раді. У повісті помирає в епілозі — тут доживає до фіналу.</summary>
        public static CharacterCard Zakhar() => new CharacterCard(
            "zakhar", "Захар Беркут", SourceTier.Literary, Work,
            "голос общины: решает сходом, а не приказом");

        /// <summary>Комірник із фольклорного ярусу — з однією нав'язливою рисою (Поправка №5.2).</summary>
        public static CharacterCard Keeper() => new CharacterCard(
            "keeper", "Дід Овсій", SourceTier.Folklore, "украинская народная традиция (общественное достояние)",
            "считает каждый мешок вслух и не верит ничьим цифрам");

        /// <summary>Лікарка фольклорного ярусу.</summary>
        public static CharacterCard Healer() => new CharacterCard(
            "healer", "Знахарка Гафія", SourceTier.Folklore, "украинская народная традиция (общественное достояние)",
            "лечит всех, но каждому говорит правду в лицо");

        /// <summary>
        /// Командир орди — бос фіналу. Першоджерело ВІДКРИТЕ (§5.7): у зрізі він
        /// з'являється тільки ім'ям у сигналі боярина, обличчям — у фіналі, до
        /// якого зріз не доходить. Картка-заглушка чесніша за порожнечу: видно,
        /// що персонаж заявлений і чого йому не вистачає.
        /// </summary>
        public static CharacterCard HordeCommander() => new CharacterCard(
            "horde_commander", "Командир орди", SourceTier.Original, null,
            "ПЛЕЙСХОЛДЕР: первоисточник не выбран (открытый вопрос §5.7)");

        /// <summary>Весь кастинг відкриття.</summary>
        public static List<CharacterCard> All() => new List<CharacterCard>
        {
            Maksym(), Myroslava(), TuharVovk(), Zakhar(), Keeper(), Healer(), HordeCommander()
        };

        /// <summary>Хто може бути напарником: фольклорні — не можуть (немає арки).</summary>
        public static List<CharacterCard> CompanionCandidates()
        {
            var result = new List<CharacterCard>();
            foreach (var card in All())
                if (card.CanBeCompanion && card.Id != "horde_commander") result.Add(card);
            return result;
        }
    }
}
