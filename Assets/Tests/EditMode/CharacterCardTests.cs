using System.Collections.Generic;
using Game.Core.Characters;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Тести ЗМІСТУ карток (Поправка №5.6 п. 5).
    ///
    /// Перевіряються не механіки, а правила письма: у кожного запозиченого
    /// персонажа назване першоджерело, напарником фольклорний не стає,
    /// фольклорних не більше частки. Забутий рядок тут — не косметика, а
    /// юридичний ризик: «вільно доступно» не дорівнює «суспільне надбання».
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

        /// <summary>Фольклорний персонаж не має арки, тому в напарники не йде.</summary>
        [Test]
        public void FolkloreCharacter_IsNeverACompanion()
        {
            foreach (var card in OpeningCast.CompanionCandidates())
                Assert.AreNotEqual(SourceTier.Folklore, card.Tier,
                    $"{card.DisplayName}: фольклорный в кандидатах в напарники");
        }

        /// <summary>
        /// Частка фольклорних обмежена. Точне число — відкрите питання власника
        /// (§5.7), тому тест тримає слабку, але осмислену межу: їх не
        /// повинно бути більшість. Коли число назвуть, поріг сюди і приїде.
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

        /// <summary>Іменний антагоніст впізнається за карткою, а не за рядком у трьох місцях.</summary>
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
        /// Заглушка командира орди оголошена чесно: першоджерела немає, і
        /// ярус у неї авторський, а не «літературний без джерела».
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
