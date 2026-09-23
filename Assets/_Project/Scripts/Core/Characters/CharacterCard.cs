using System.Collections.Generic;

namespace Game.Core.Characters
{
    /// <summary>
    /// Откуда взят именной персонаж (Поправка №2 и №5.2).
    ///
    /// Ярус важен не для красоты: напарником фольклорный персонаж быть не может
    /// (у него нет арки), а доля фольклорных в ростере ограничена. Без поля эти
    /// правила проверить нечем.
    /// </summary>
    public enum SourceTier
    {
        /// <summary>Безымянный — карточки нет (и быть не должно, см. Поправку №5.2).</summary>
        None = 0,

        /// <summary>Литературный: конкретное произведение в общественном достоянии.</summary>
        Literary = 1,

        /// <summary>Фольклорный: народная традиция, автора нет.</summary>
        Folklore = 2,

        /// <summary>Авторский: придуман нами, первоисточника нет.</summary>
        Original = 3
    }

    /// <summary>
    /// Карточка персонажа (Поправка №5.6 п. 1): имя, откуда он взят, что за
    /// ядро характера переносится и что он помнит.
    ///
    /// Зачем отдельный тип, а не поля в архетипе: карточка есть и у тех, кто
    /// НЕ напарник — у антагониста, у старейшины на совете, у жителя. Архетип
    /// же описывает боевые и рабочие данные. Их жизненные циклы разные:
    /// карточка пишется автором один раз, статы крутит баланс.
    ///
    /// Именной антагонист обязан быть узнаваем при каждой встрече (чеклист
    /// §3 стр. 17: «до боя, в сигнале, в финале») — узнаётся он именно по
    /// карточке, а не по строке в трёх разных местах.
    /// </summary>
    public sealed class CharacterCard
    {
        /// <summary>Идентификатор: тот же, что у напарника в ростере, если он напарник.</summary>
        public string Id;

        /// <summary>Имя, которое видит игрок.</summary>
        public string DisplayName;

        public SourceTier Tier = SourceTier.Original;

        /// <summary>
        /// Первоисточник: произведение и автор. Для фольклорного — традиция.
        /// Обязателен для всех, кроме авторских: проверяется тестом содержания
        /// (Поправка №5.6 п. 5), потому что «вільно доступне» не равно
        /// «общественное достояние», и забытая строка здесь — юридический риск.
        /// </summary>
        public string Source;

        /// <summary>Одна фраза: ядро характера, которое переносится из первоисточника.</summary>
        public string Core;

        /// <summary>
        /// Что персонаж помнит о происходившем. Ключи событий, не текст: реплики
        /// подбираются по ключу, как и сигналы, — писателю не нужен программист.
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

        /// <summary>Персонаж из чужого произведения — к нему применяются правила Поправки №2.</summary>
        public bool IsBorrowed => Tier == SourceTier.Literary || Tier == SourceTier.Folklore;

        /// <summary>Годится ли в напарники: у фольклорного нет арки (Поправка №5.2).</summary>
        public bool CanBeCompanion => Tier != SourceTier.Folklore && Tier != SourceTier.None;

        /// <summary>Запомнить событие. Повтор не дублируется: память — множество, а не лента.</summary>
        public void Remember(string eventKey)
        {
            if (string.IsNullOrEmpty(eventKey)) return;
            if (!Memory.Contains(eventKey)) Memory.Add(eventKey);
        }

        public bool Remembers(string eventKey) => eventKey != null && Memory.Contains(eventKey);
    }
}
