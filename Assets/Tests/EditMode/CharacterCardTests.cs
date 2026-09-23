using System.Collections.Generic;
using Game.Core.Characters;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Тесты СОДЕРЖАНИЯ карточек (Поправка №5.6 п. 5).
    ///
    /// Проверяются не механики, а правила письма: у каждого заимствованного
    /// персонажа назван первоисточник, напарником фольклорный не становится,
    /// фольклорных не больше доли. Забытая строка здесь — не косметика, а
    /// юридический риск: «свободно доступно» не равно «общественное достояние».
    /// </summary>
    public class CharacterCardTests
    {
        [Test]
        public void EveryBorrowedCharacter_NamesItsSource()
        {
            foreach (var card in OpeningCast.All())
            {
                if (!card.IsBorrowed) continue;
                Assert.IsFalse(string.IsNullOrWhiteSpace(card.Source),
                    $"{card.DisplayName}: заимствованный персонаж без первоисточника");
            }
        }

        [Test]
        public void EveryCharacter_HasNameAndCore()
        {
            foreach (var card in OpeningCast.All())
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(card.DisplayName), $"{card.Id}: без имени");
                Assert.IsFalse(string.IsNullOrWhiteSpace(card.Core), $"{card.Id}: без ядра характера");
            }
        }

        /// <summary>Фольклорный персонаж не имеет арки, поэтому в напарники не идёт.</summary>
        [Test]
        public void FolkloreCharacter_IsNeverACompanion()
        {
            foreach (var card in OpeningCast.CompanionCandidates())
                Assert.AreNotEqual(SourceTier.Folklore, card.Tier,
                    $"{card.DisplayName}: фольклорный в кандидатах в напарники");
        }

        /// <summary>
        /// Доля фольклорных ограничена. Точное число — открытый вопрос владельца
        /// (§5.7), поэтому тест держит слабую, но осмысленную границу: их не
        /// должно быть большинство. Когда число назовут, порог сюда и приедет.
        /// </summary>
        [Test]
        public void FolkloreCharacters_AreAMinority()
        {
            var all = OpeningCast.All();
            int folklore = 0;
            foreach (var card in all)
                if (card.Tier == SourceTier.Folklore) folklore++;

            Assert.Less(folklore * 2, all.Count,
                "фольклорных больше половины — ярус перестал быть приправой");
        }

        /// <summary>Именной антагонист узнаётся по карточке, а не по строке в трёх местах.</summary>
        [Test]
        public void NamedAntagonist_IsOneCharacterAcrossAppearances()
        {
            var first = OpeningCast.TuharVovk();
            var later = OpeningCast.TuharVovk();

            Assert.AreEqual(first.Id, later.Id);
            Assert.AreEqual(first.DisplayName, later.DisplayName);
            Assert.AreEqual(SourceTier.Literary, first.Tier);
        }

        [Test]
        public void Memory_KeepsKeysOnce()
        {
            var card = OpeningCast.Maksym();
            card.Remember("pass.held");
            card.Remember("pass.held");

            Assert.AreEqual(1, card.Memory.Count, "память — множество, а не лента");
            Assert.IsTrue(card.Remembers("pass.held"));
            Assert.IsFalse(card.Remembers("pass.lost"));
        }

        /// <summary>
        /// Заглушка командира орды объявлена честно: первоисточника нет, и
        /// ярус у неё авторский, а не «литературный без источника».
        /// </summary>
        [Test]
        public void HordeCommander_IsDeclaredAsUnresolved()
        {
            var boss = OpeningCast.HordeCommander();

            Assert.AreEqual(SourceTier.Original, boss.Tier);
            Assert.IsFalse(boss.IsBorrowed, "пока первоисточник не выбран, персонаж не заимствованный");
            StringAssert.Contains("ПЛЕЙСХОЛДЕР", boss.Core);
        }
    }
}
