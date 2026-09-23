using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Checks;
using Game.Core.Pressure;
using Game.Core.World;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Контент открытия «Перевал» (docs/FIRST_HOUR.md §2.2) — шаг 5 порядка
    /// сборки. Проверяется не механика конвейера, а связность авторского
    /// материала: он рассыпается тихо, и в прогоне это видно только как
    /// «почему-то всегда Худшая».
    /// </summary>
    public class OpeningContentTests
    {
        /// <summary>Поправка №1: у каждого инцидента есть ненасильственный путь.</summary>
        [Test]
        public void EveryOpeningIncident_HasAQuietPath()
        {
            foreach (var incident in OpeningContent.All())
                Assert.IsTrue(incident.HasQuietPath, $"{incident.Id}: нет тихого пути");
        }

        /// <summary>
        /// Навык инцидента совпадает с доменом его поста. Иначе человек, который
        /// на посту стоит, помочь не может — и исход Худший при ЛЮБОМ выборе.
        /// Это не решение, а ловушка; поймано прогоном среза 23.09.2026.
        /// </summary>
        [Test]
        public void IncidentSkill_MatchesTheSkillOfItsPost()
        {
            var postSkill = new Dictionary<string, SkillKey>
            {
                { "storehouse_dock", SkillKeys.Survival },
                { "settlement_market", SkillKeys.Trade },
                { "infirmary_bed", SkillKeys.Medicine },
                { "council_seat", SkillKeys.Persuade }
            };

            foreach (var incident in OpeningContent.All())
            {
                if (string.IsNullOrEmpty(incident.RelevantPositionId)) continue;
                if (!postSkill.TryGetValue(incident.RelevantPositionId, out var expected)) continue;

                Assert.AreEqual(expected, incident.QuietPathSkill,
                    $"{incident.Id}: тихий путь требует не того навыка, которым владеет пост " +
                    incident.RelevantPositionId);
            }
        }

        /// <summary>Узел первых суток доступен сразу: срез начинается с дела, а не с ожидания.</summary>
        [Test]
        public void OpeningNode_IsAvailableFromTheCalmestBand()
        {
            Assert.AreEqual(TensionBand.Calm, OpeningContent.PassVanguard().MinBand,
                "узел первых суток обязан быть доступен в самой тихой полосе");
        }

        /// <summary>Узел даёт ОБА пути: тихий и кровавый — на них построена развилка §2.2.</summary>
        [Test]
        public void OpeningNode_OffersBothPaths()
        {
            var node = OpeningContent.PassVanguard();
            Assert.IsTrue(node.HasQuietPath, "нет тихого пути");
            Assert.IsTrue(node.HasBloodyPath, "нет кровавого пути — развилка вырождается");
            Assert.AreNotEqual(node.QuietPathSkill, node.BloodyPathSkill,
                "пути различаются навыком, иначе выбор косметический");
        }

        /// <summary>
        /// Авторская последовательность: узел на первые сутки, припасы на
        /// вторые, девочка на третьи. Отдать это случайности значит, что первая
        /// игровая час каждый раз разная — а её проверяют по чеклисту.
        /// </summary>
        [Test]
        public void ScriptedSources_FireOnTheirAuthoredDayOnly()
        {
            var sources = OpeningContent.ScriptedSources();
            Assert.AreEqual(3, sources.Count);

            for (int day = 1; day <= 5; day++)
            {
                int active = 0;
                foreach (var source in sources)
                {
                    var ctx = new PulseContext(day, false, 1, 0, false);
                    if (source.IsActive(ctx)) active++;
                }
                Assert.LessOrEqual(active, 1, $"сутки {day}: авторских событий больше одного");
            }

            // И каждое своё утро всё-таки наступает.
            for (int i = 0; i < sources.Count; i++)
            {
                var day = new PulseContext(i + 1, false, 1, 0, false);
                Assert.IsTrue(sources[i].IsActive(day), $"событие {sources[i].Id} не наступает в свои сутки");
                Assert.IsFalse(sources[i].IsActive(new PulseContext(i + 1, true, 1, 0, false)),
                    $"{sources[i].Id}: открытие ставится днём, а не ночью");
            }
        }

        /// <summary>Каждый авторский инцидент привязан к своему источнику: предвестник не врёт о домене.</summary>
        [Test]
        public void EveryOpeningIncident_IsBoundToItsSource()
        {
            var sourceIds = new List<string>();
            foreach (var source in OpeningContent.ScriptedSources()) sourceIds.Add(source.Id);

            foreach (var incident in OpeningContent.All())
            {
                Assert.IsFalse(string.IsNullOrEmpty(incident.SourceId), $"{incident.Id}: без источника");
                CollectionAssert.Contains(sourceIds, incident.SourceId,
                    $"{incident.Id}: источник «{incident.SourceId}» не объявлен в последовательности");
            }
        }

        /// <summary>Именной накопитель копит с первых суток: слух обязан дойти к 3–4 суткам (§2.2).</summary>
        [Test]
        public void TuharSource_AccumulatesFromTheFirstDay()
        {
            var tuhar = new OpeningContent.TuharPressureSource();
            var calmDayOne = new PulseContext(1, false, 1, 0, false);

            Assert.IsTrue(tuhar.IsActive(calmDayOne), "боярин договаривается независимо от полосы");
            Assert.Greater(tuhar.InsistencePerDay(calmDayOne), 0);

            // Первая ступень предвестника — на 55% заполнения (PulseBalance).
            var cfg = new BalanceConfig();
            int perDay = tuhar.InsistencePerDay(calmDayOne);
            double daysToFirstStep = tuhar.Threshold * cfg.Pulse.Forewarn1At / perDay;
            Assert.LessOrEqual(daysToFirstStep, 4.0,
                "слух о боярине не успевает дойти к 3-4 суткам, как обещает §2.2");
        }
    }
}
