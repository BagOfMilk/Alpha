using System.Collections.Generic;
using System.Linq;
using Game.Core.Characters.Creation;
using Game.Core.Loop;
using Game.Core.Session;
using Game.Core.Session.Views;
using Game.Gameplay.UI;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Спайк H4 (docs/HUD_DESIGN.md §4.3, §11): модель стрічки — три зони.
    /// Головна гарантія — інв. 4 (Статут UI-07): обов'язковий сигнал
    /// (зміна полоси, ступінь передвісника) лишається в закріпленій зоні,
    /// навіть коли новини переповнені.
    /// </summary>
    public class FeedModelTests
    {
        private const Gender G = Gender.Male;
        private static readonly RosterView NoRoster = new RosterView { Companions = new List<CompanionSummary>() };

        private static GameEvent E(string key, int day, params string[] kv)
        {
            var args = new Dictionary<string, string>();
            for (int i = 0; i + 1 < kv.Length; i += 2) args[kv[i]] = kv[i + 1];
            return new GameEvent(key, day, DayPhase.Day, args);
        }

        private static List<GameEvent> Noise(int count, int day)
        {
            var list = new List<GameEvent>();
            for (int i = 0; i < count; i++) list.Add(E("test.news", day, "n", i.ToString()));
            return list;
        }

        [Test]
        public void Inv4_BandChange_StaysPinned_WhenNewsOverflow()
        {
            var log = new List<GameEvent> { E("tension.band.Ferment", 5) };
            log.AddRange(Noise(500, 5)); // п'ятсот новин ПІСЛЯ сигналу

            var feed = FeedModel.Build(log, null, 5, G, NoRoster, maxNews: 40);

            Assert.AreEqual(1, feed.Important.Count, "перехід полоси — у закріпленій зоні");
            Assert.AreEqual("tension.band.Ferment", feed.Important[0].Key);
            StringAssert.Contains("Бродіння", feed.Important[0].Text, "поруч — слово полоси (HUD_DESIGN §5.1)");
            Assert.AreEqual(FeedSeverity.Mandatory, feed.Important[0].Severity);
            Assert.AreEqual(40, feed.News.Count, "новини обрізані лімітом");
            Assert.AreEqual(460, feed.HiddenNews);
            Assert.IsFalse(feed.News.Any(n => n.Text.Contains("Бродить")), "сигнал не дублюється в новинах (HP-1)");
        }

        [Test]
        public void Inv4_ForewarnLevels_ArePinned_WithSeverityByLevel()
        {
            var log = new List<GameEvent>
            {
                E("forewarn.level1", 4, "subject", "crisis"),
                E("forewarn.level3", 5, "subject", "crisis", "domain", "road"),
            };
            log.AddRange(Noise(100, 5));
            var feed = FeedModel.Build(log, null, 5, G, NoRoster, maxNews: 10);

            Assert.AreEqual(2, feed.Important.Count, "обидві ступені — учорашня і сьогоднішня — закріплені");
            Assert.AreEqual("forewarn.level3", feed.Important[0].Key, "найновіше зверху");
            Assert.AreEqual(FeedSeverity.Danger, feed.Important[0].Severity);
            Assert.AreEqual(FeedSeverity.Warning, feed.Important[1].Severity);
        }

        [Test]
        public void Mandatory_OlderThanYesterday_GoesToNews()
        {
            var log = new List<GameEvent> { E("tension.band.Murmur", 2), E("test.news", 5, "n", "x") };
            var feed = FeedModel.Build(log, null, 5, G, NoRoster);
            Assert.IsEmpty(feed.Important, "закріплення — до кінця доби, а не назавжди");
            Assert.AreEqual(2, feed.News.Count, "старий сигнал не зникає — він у новинах");
        }

        [Test]
        public void TextKeysUnderTensionBand_AreNotBandChanges()
        {
            Assert.IsFalse(FeedModel.IsMandatory(E("tension.band.risen", 1)));
            Assert.IsFalse(FeedModel.IsMandatory(E("tension.band.label.calm", 1)));
            Assert.IsFalse(FeedModel.IsMandatory(E("forewarn_boosted", 1)));
            Assert.IsTrue(FeedModel.IsMandatory(E("tension.band.Calm", 1)));
            Assert.IsTrue(FeedModel.IsMandatory(E("forewarn.level2", 1)));
        }

        [Test]
        public void News_CollapseRepeats_IntoTimesN_NewestFirst()
        {
            var log = new List<GameEvent>
            {
                E("test.first", 3),
                E("test.same", 3), E("test.same", 3), E("test.same", 3),
                E("test.last", 3),
            };
            var feed = FeedModel.Build(log, null, 3, G, NoRoster);
            Assert.AreEqual(3, feed.News.Count);
            StringAssert.StartsWith("test.last", feed.News[0].Text, "найновіше зверху");
            Assert.AreEqual(3, feed.News[1].Count, "три однакові підряд — один рядок ×3");
            StringAssert.EndsWith("×3", FeedModel.DisplayText(feed.News[1], G));
            Assert.AreEqual(1, feed.News[2].Count);
            StringAssert.DoesNotContain("×", FeedModel.DisplayText(feed.News[2], G));
        }

        [Test]
        public void Important_CollapseRepeats_ToTimesN()
        {
            var log = new List<GameEvent> { E("forewarn.level1", 5), E("forewarn.level1", 5) };
            var feed = FeedModel.Build(log, null, 5, G, NoRoster);
            Assert.AreEqual(1, feed.Important.Count);
            Assert.AreEqual(2, feed.Important[0].Count);
        }

        [Test]
        public void Pending_ShowsTheOfferTitle_CrisisIsDanger()
        {
            var offer = new PendingOfferView { Kind = "Incident", TopicId = "incident.no_such_topic_for_test", IsCrisis = false };
            var feed = FeedModel.Build(new List<GameEvent>(), offer, 3, G, NoRoster);
            Assert.AreEqual(1, feed.Pending.Count);
            Assert.AreEqual("Рішення чекає", feed.Pending[0].Text, "без заголовка теми — загальний підпис, не сирий TopicId");
            Assert.AreEqual(FeedSeverity.Warning, feed.Pending[0].Severity);

            offer.IsCrisis = true;
            Assert.AreEqual(FeedSeverity.Danger, FeedModel.Build(null, offer, 3, G, NoRoster).Pending[0].Severity);
            Assert.IsEmpty(FeedModel.Build(null, null, 3, G, NoRoster).Pending);
        }

        [Test]
        public void Inv3_ImportantLines_CarryNoHiddenNumbers()
        {
            // Слово полоси — так; число шкали — ні. Жоден аргумент події з
            // цифрами тут не підставляється в текст переходу.
            foreach (var band in HudHeaderModel.BandOrder)
            {
                var feed = FeedModel.Build(new List<GameEvent> { E("tension.band." + band, 9) }, null, 9, G, NoRoster);
                Assert.AreEqual(1, feed.Important.Count, band);
                Assert.IsFalse(feed.Important[0].Text.Any(char.IsDigit), band + ": «" + feed.Important[0].Text + "»");
            }
        }
    }
}
