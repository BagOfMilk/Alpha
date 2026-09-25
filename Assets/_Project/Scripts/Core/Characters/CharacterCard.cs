using System.Collections.Generic;

namespace Game.Core.Characters
{
    /// <summary>
    /// Звідки взятий іменний персонаж (Поправка №2 і №5.2).
    ///
    /// Ярус важливий не для краси: напарником фольклорний персонаж бути не
    /// може (у нього немає арки), а частка фольклорних у ростері обмежена.
    /// Без поля ці правила перевірити нема чим.
    /// </summary>
    public enum SourceTier
    {
        /// <summary>Безіменний — картки немає (і бути не повинно, див. Поправку №5.2).</summary>
        None = 0,

        /// <summary>Літературний: конкретний твір у суспільному надбанні.</summary>
        Literary = 1,

        /// <summary>Фольклорний: народна традиція, автора немає.</summary>
        Folklore = 2,

        /// <summary>Авторський: вигаданий нами, першоджерела немає.</summary>
        Original = 3
    }

    /// <summary>
    /// Картка персонажа (Поправка №5.6 п. 1): ім'я, звідки він узятий, яке
    /// ядро характеру переноситься і що він пам'ятає.
    ///
    /// Навіщо окремий тип, а не поля в архетипі: картка є і в тих, хто
    /// НЕ напарник — в антагоніста, у старійшини на раді, у жителя. Архетип
    /// же описує бойові й робочі дані. Їхні життєві цикли різні:
    /// картка пишеться автором один раз, стати крутить баланс.
    ///
    /// Іменний антагоніст зобов'язаний бути впізнаваним при кожній зустрічі
    /// (чекліст §3 стор. 17: «до бою, в сигналі, у фіналі») — впізнається він
    /// саме по картці, а не по рядку у трьох різних місцях.
    /// </summary>
    public sealed class CharacterCard
    {
        /// <summary>Ідентифікатор: той самий, що в напарника в ростері, якщо він напарник.</summary>
        public string Id;

        /// <summary>Ім'я, яке бачить гравець.</summary>
        public string DisplayName;

        public SourceTier Tier = SourceTier.Original;

        /// <summary>
        /// Першоджерело: твір і автор. Для фольклорного — традиція.
        /// Обов'язковий для всіх, крім авторських: перевіряється тестом вмісту
        /// (Поправка №5.6 п. 5), бо «вільно доступне» не дорівнює
        /// «суспільному надбанню», і забутий рядок тут — юридичний ризик.
        /// </summary>
        public string Source;

        /// <summary>Одна фраза: ядро характеру, яке переноситься з першоджерела.</summary>
        public string Core;

        /// <summary>
        /// Що персонаж пам'ятає про те, що відбувалося. Ключі подій, не текст:
        /// репліки підбираються за ключем, як і сигнали, — письменнику не
        /// потрібен програміст.
        /// </summary>
        public readonly List<string> Memory = new List<string>();

        public CharacterCard() { }

        public CharacterCard(string id, string displayName, SourceTier tier, string source, string core = null)
        {
            Id = id;
            DisplayName = displayName;
            Tier = tier;
            Source = source;
            Core = core;
        }

        /// <summary>Персонаж із чужого твору — до нього застосовуються правила Поправки №2.</summary>
        public bool IsBorrowed => Tier == SourceTier.Literary || Tier == SourceTier.Folklore;

        /// <summary>Чи годиться в напарники: у фольклорного немає арки (Поправка №5.2).</summary>
        public bool CanBeCompanion => Tier != SourceTier.Folklore && Tier != SourceTier.None;

        /// <summary>Запам'ятати подію. Повтор не дублюється: пам'ять — множина, а не стрічка.</summary>
        public void Remember(string eventKey)
        {
            if (string.IsNullOrEmpty(eventKey)) return;
            if (!Memory.Contains(eventKey)) Memory.Add(eventKey);
        }

        public bool Remembers(string eventKey) => eventKey != null && Memory.Contains(eventKey);
    }
}
