using System.Collections.Generic;
using System.Linq;
using Game.Core.Checks;
using Game.Core.Loop;
using Game.Core.Pressure;
using Game.Core.Signals;
using Game.Core.World;
using Game.Gameplay;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Правила показа села.
    ///
    /// Сцена — тоже код, и её поведение проверяется тестами, а не разглядыванием
    /// скриншота. Здесь закреплено главное: ночь отличается от дня, состояние
    /// города видно по свету, а пропавшая реплика ВИДНА в ленте, а не молчит.
    /// </summary>
    public class VillageViewTests
    {
        private static MoodboardState Mood(int prosperity, int decay)
        {
            return new MoodboardState(prosperity, decay, null);
        }

        private static DayReport Report(DayPhase phase, IReadOnlyList<IncidentOutcome> incidents,
            IReadOnlyList<SignalRequest> signals)
        {
            var digest = new SignalDigest(signals ?? new List<SignalRequest>(), Mood(2, 0));
            return new DayReport(7, phase, new List<TensionChange>(), digest,
                incidents ?? new List<IncidentOutcome>(), new List<Forewarning>(), null);
        }

        // ================= свет =================

        [Test]
        public void Night_IsDarkerAndColderThanDay()
        {
            var day = VillageView.SunFor(DayPhase.Day);
            var night = VillageView.SunFor(DayPhase.Night);

            Assert.Less(night.Intensity, day.Intensity,
                "Ночь обязана быть темнее: иначе фаза суток не читается глазами");
            Assert.Less(night.Pitch, day.Pitch, "Ночное солнце стоит низко");
            Assert.Greater(night.B - night.R, day.B - day.R,
                "Ночь холоднее дня: синего больше, красного меньше");
        }

        [Test]
        public void Decay_DrainsColourFromTheLight()
        {
            var clean = VillageView.AmbientFor(DayPhase.Day, Mood(2, 0));
            var rotten = VillageView.AmbientFor(DayPhase.Day, Mood(2, 4));

            float cleanSpread = System.Math.Abs(clean.SkyB - clean.SkyR);
            float rottenSpread = System.Math.Abs(rotten.SkyB - rotten.SkyR);

            Assert.Less(rottenSpread, cleanSpread,
                "Упадок обязан обесцвечивать свет — это единственный разрешённый способ " +
                "показать состояние города, раз число игроку не показывают");
            Assert.Less(rotten.SkyB, clean.SkyB, "И гасить его");
        }

        [Test]
        public void Mood_IsWordsNotNumbers()
        {
            // Игрок никогда не видит «достаток 3»: он видит город.
            Assert.AreNotEqual(VillageView.MoodWords(Mood(0, 0)), VillageView.MoodWords(Mood(4, 0)));
            StringAssert.Contains("хутір", VillageView.MoodWords(Mood(0, 0)));
            StringAssert.Contains("село", VillageView.MoodWords(Mood(1, 0)));
            StringAssert.Contains("забитий", VillageView.MoodWords(Mood(2, 4)));
            foreach (char c in VillageView.MoodWords(Mood(3, 2)))
                Assert.IsFalse(char.IsDigit(c), "В описании города не должно быть цифр");
        }

        // ================= лента =================

        [Test]
        public void Crisis_IsNamedCrisis()
        {
            var crisis = new IncidentOutcome("crisis_riot", "incident.crisis_riot", "площадь",
                OutcomeBand.Worst, false, true, "guard", CrisisBite.WoundCompanion, 12);

            var lines = VillageView.Lines(Report(DayPhase.Day, new[] { crisis }, null));

            Assert.AreEqual(1, lines.Count);
            StringAssert.Contains("КРИЗА", lines[0], "Кризис не имеет права выглядеть как рядовое происшествие");
            StringAssert.Contains("люди йдуть", lines[0], "Отток населения обязан прозвучать словами");
        }

        [Test]
        public void EmptyPost_IsSaidOutLoud()
        {
            var outcome = new IncidentOutcome("sick_child", "incident.sick_child", "лазарет",
                OutcomeBand.Worst, true, false, null, null, 0);

            var lines = VillageView.Lines(Report(DayPhase.Day, new[] { outcome }, null));

            StringAssert.Contains("на посту нікого не було", lines[0],
                "Цена пустого поста — урок расстановки, и он обязан быть произнесён");
        }

        [Test]
        public void Fear_IsSaidOutLoud()
        {
            var outcome = new IncidentOutcome("petty_theft", "incident.petty_theft", "склад",
                OutcomeBand.Base, false, false, null, null, 0, causedFear: true);

            var lines = VillageView.Lines(Report(DayPhase.Day, new[] { outcome }, null));

            StringAssert.Contains("налякана", lines[0],
                "Страх общины — скрытая цена; по инварианту 6 у неё обязан быть голос");
        }

        [Test]
        public void Forewarning_GrowsWithItsLevel()
        {
            var first = Signal(SignalChannel.Forewarning, "forewarn.level1", "level:1", "domain:улицы");
            var third = Signal(SignalChannel.Forewarning, "forewarn.level3", "level:3", "domain:улицы");

            var early = VillageView.Lines(Report(DayPhase.Day, null, new[] { first }));
            var late = VillageView.Lines(Report(DayPhase.Day, null, new[] { third }));

            Assert.AreNotEqual(early[0], late[0], "Ступени предвестника обязаны звучать по-разному");
            StringAssert.Contains("улицы", late[0], "Вторая ступень и выше называет домен");
            StringAssert.DoesNotContain("улицы", early[0], "Первая ступень домена НЕ называет");
        }

        [Test]
        public void PostReports_DoNotFloodTheFeed()
        {
            var report = Signal(SignalChannel.PostReport, "post.склад.Good", "domain:склад");
            var lines = VillageView.Lines(Report(DayPhase.Day, null, new[] { report }));

            Assert.IsEmpty(lines,
                "Ежедневные доклады — фон: в ленте остаётся то, что ИЗМЕНИЛОСЬ");
        }

        [Test]
        public void MissingLine_IsVisibleNotSilent()
        {
            // Пока таблиц реплик нет, незнакомый ключ показывается как есть.
            // Молчание было бы хуже: пропажа текста осталась бы незамеченной.
            var unknown = Signal(SignalChannel.CompanionLine, "нечто.неизвестное");
            var lines = VillageView.Lines(Report(DayPhase.Day, null, new[] { unknown }));

            Assert.AreEqual(1, lines.Count);
            StringAssert.Contains("нечто.неизвестное", lines[0],
                "Ключ без реплики обязан быть виден, иначе пропажа текста молчит");
        }

        [Test]
        public void CityEvents_AreSpokenNotKeys()
        {
            var built = Signal(SignalChannel.CitizenLine, "city.built.temple", "building:temple");
            var tier = Signal(SignalChannel.CitizenLine, "city.tier.2", "tier:2");
            var left = Signal(SignalChannel.CitizenLine, "city.people.left", "reason:hunger", "count:2");
            var came = Signal(SignalChannel.CitizenLine, "city.people.arrived", "reason:expedition", "count:4");

            var lines = VillageView.Lines(Report(DayPhase.Day, null, new[] { built, tier, left, came }));

            StringAssert.Contains("Храм", lines[0], "Здание называется по имени, а не ключом");
            StringAssert.Contains("селом", lines[1], "Смена тира — словами дуги: хутір, село, слобода, містечко");
            StringAssert.Contains("голодно", lines[2], "Уход людей называет причину");
            StringAssert.Contains("загін", lines[3], "Пришедшие — откуда пришли");
            foreach (var line in lines)
                StringAssert.DoesNotContain("city.", line, "Ни одного сырого ключа в ленте");
        }

        [Test]
        public void CouncilActions_AreSpokenNotRawTopics()
        {
            // Фікс-ревью пакета E3b: п'ять реальних council.*-топіків з
            // Core/Base/Buildings/CityWorks*.cs (§2, рядок 15 — «Нові дії
            // ради») повинні звучати перекладом, а не падати у "· <topic>".
            var decree = Signal(SignalChannel.CitizenLine, "council.decree.ordered");
            var diplomacy = Signal(SignalChannel.CitizenLine, "council.diplomacy.ordered");
            var prepareThreat = Signal(SignalChannel.CitizenLine, "council.prepare_threat.ordered");
            var outfitExpedition = Signal(SignalChannel.CitizenLine, "council.outfit_expedition.ordered");
            var investPayout = Signal(SignalChannel.CitizenLine, "council.invest.payout");

            var lines = VillageView.Lines(Report(DayPhase.Day, null,
                new[] { decree, diplomacy, prepareThreat, outfitExpedition, investPayout }));

            Assert.AreEqual(5, lines.Count);
            StringAssert.Contains("Рада видає указ", lines[0]);
            StringAssert.Contains("Посольство вирушає", lines[1]);
            StringAssert.Contains("Громада готується", lines[2]);
            StringAssert.Contains("Загін споряджають", lines[3]);
            StringAssert.Contains("Вкладення ради дало віддачу", lines[4]);
            foreach (var line in lines)
                StringAssert.DoesNotContain("council.", line,
                    "Ни один council.*-топик не должен прорываться сырым ключом в ленту");
        }

        [Test]
        public void Crisis_NamesTheCompanionInsteadOfRawId()
        {
            // Фікс-ревью пакета E3b: суфікс кризи не має показувати сирий
            // Core-id напарника — тільки перекладене ім'я з "char.<id>".
            var crisis = new IncidentOutcome("crisis_riot", "incident.crisis_riot", "площадь",
                OutcomeBand.Worst, false, true, "maksym", CrisisBite.WoundCompanion, 0);

            var lines = VillageView.Lines(Report(DayPhase.Day, new[] { crisis }, null));

            Assert.AreEqual(1, lines.Count);
            StringAssert.Contains("Максим Беркут", lines[0], "Напарник обязан быть назван по имени");
            StringAssert.DoesNotContain("maksym", lines[0], "Сырой Core-id не должен просачиваться в реплику");
        }

        [Test]
        public void Headline_TellsDayAndPhase()
        {
            var night = VillageView.Headline(Report(DayPhase.Night, null, null), Mood(1, 0));

            StringAssert.Contains("Доба 7", night);
            StringAssert.Contains("ніч", night);
        }

        private static SignalRequest Signal(SignalChannel channel, string topicId, params string[] tags)
        {
            return new SignalRequest(channel, topicId, SignalUrgency.Notable, null, true, tags);
        }
    }
}
