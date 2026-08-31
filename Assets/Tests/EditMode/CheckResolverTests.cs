using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Checks;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Проверки — фундамент всего резолва в игре. Здесь защищается US-2.6
    /// (костей нет, порог виден заранее) и Поправка №3.7 (полосы исхода).
    /// </summary>
    public class CheckResolverTests
    {
        private sealed class FakeActor : ISettlementActor
        {
            public string Id { get; set; }
            public bool IsPresentInSettlement { get; set; } = true;
            public bool IsProtagonist { get; set; }
            public string HeldPositionId { get; set; }
            public int Value { get; set; }
            public int Trait { get; set; }

            public int GetCheckValue(SkillKey skill) => Value;
            public int GetTraitModifier(SkillKey skill) => Trait;
        }

        private sealed class FakeRoster : IRosterView
        {
            public List<ISettlementActor> Actors { get; } = new List<ISettlementActor>();
            public IReadOnlyList<ISettlementActor> PresentActors => Actors;
            public ISettlementActor Protagonist { get; set; }
        }

        private static BalanceConfig Cfg() => new BalanceConfig();
        private static SkillKey Skill => SkillKeys.Persuade;

        private static FakeRoster RosterOf(params ISettlementActor[] actors)
        {
            var r = new FakeRoster();
            r.Actors.AddRange(actors);
            return r;
        }

        // ---- US-2.6: лучший релевантный скил среди присутствующих ----

        [Test]
        public void Check_UsesBestSkillAmongPresent()
        {
            var roster = RosterOf(
                new FakeActor { Id = "a", Value = 4 },
                new FakeActor { Id = "b", Value = 9 },
                new FakeActor { Id = "c", Value = 7 },
                new FakeActor { Id = "away", Value = 12, IsPresentInSettlement = false });

            var preview = CheckResolver.Preview(
                new CheckRequest(Skill, 5), roster, null, 1, Cfg());

            Assert.AreEqual("b", preview.BestActorId, "Берётся лучший из присутствующих");
            Assert.AreEqual(9, preview.BestValue, "Ушедший в экспедицию не считается");
        }

        [Test]
        public void Check_Protagonist_IsValidCandidate()
        {
            var roster = RosterOf(
                new FakeActor { Id = "weak", Value = 2 },
                new FakeActor { Id = "leader", Value = 8, IsProtagonist = true });

            var preview = CheckResolver.Preview(
                new CheckRequest(Skill, 5), roster, null, 1, Cfg());

            Assert.AreEqual("leader", preview.BestActorId, "Протагонист — валидный кандидат (US-8.2)");
        }

        [Test]
        public void Check_TraitModifier_IsAddedFlat()
        {
            var roster = RosterOf(new FakeActor { Id = "a", Value = 5, Trait = 3 });

            var preview = CheckResolver.Preview(
                new CheckRequest(Skill, 5), roster, null, 1, Cfg());

            Assert.AreEqual(8, preview.BestValue);
        }

        // ---- US-8.2: незанятая позиция даёт худший исход ----

        [Test]
        public void Check_UnmannedPosition_YieldsWorstBand()
        {
            var roster = RosterOf(new FakeActor { Id = "a", Value = 20, HeldPositionId = "other" });

            var outcome = CheckResolver.Resolve(
                new CheckRequest(Skill, 5, ApproachForm.Neutral, "t", "market"),
                roster, null, 1, Cfg());

            Assert.AreEqual(OutcomeBand.Worst, outcome.Band);
            Assert.IsTrue(outcome.WasUnmanned, "Позицию никто не держит — это должно быть видно");
        }

        [Test]
        public void Check_MannedPosition_UsesThatActor()
        {
            var roster = RosterOf(
                new FakeActor { Id = "strong", Value = 20, HeldPositionId = "other" },
                new FakeActor { Id = "onpost", Value = 6, HeldPositionId = "market" });

            var outcome = CheckResolver.Resolve(
                new CheckRequest(Skill, 5, ApproachForm.Neutral, "t", "market"),
                roster, null, 1, Cfg());

            Assert.AreEqual("onpost", outcome.ActorId, "Отвечает тот, кто держит позицию");
            Assert.IsFalse(outcome.WasUnmanned);
        }

        // ---- US-17.3: показанный порог равен применённому ----

        [Test]
        public void Check_PreviewEqualsResolve()
        {
            var cfg = Cfg();
            var roster = RosterOf(new FakeActor { Id = "a", Value = 7 });
            var repeats = new SimpleRepeats();
            var request = new CheckRequest(Skill, 5, ApproachForm.Neutral, "topic");

            var preview = CheckResolver.Preview(request, roster, repeats, 1, cfg);
            var outcome = CheckResolver.Resolve(request, roster, repeats, 1, cfg);

            Assert.AreEqual(preview.ExpectedBand, outcome.Band, "Обещание равно результату");
            Assert.AreEqual(preview.Margin, outcome.Margin);
        }

        // ---- Поправка №3.7: формы подходов ----

        [Test]
        public void Check_Persuade_NeverWorstWithinCushion()
        {
            var cfg = Cfg();
            // Недобор 2 при подушке 2 — Убеждение обязано вытянуть до Базовой.
            var band = CheckResolver.BandFor(-2, ApproachForm.Persuade, cfg.Checks);
            Assert.AreEqual(OutcomeBand.Base, band);

            // А недобор 3 — уже нет.
            Assert.AreEqual(OutcomeBand.Worst, CheckResolver.BandFor(-3, ApproachForm.Persuade, cfg.Checks));
        }

        [Test]
        public void Check_Persuade_CannotReachBest()
        {
            var cfg = Cfg();
            var band = CheckResolver.BandFor(20, ApproachForm.Persuade, cfg.Checks);
            Assert.AreEqual(OutcomeBand.Good, band, "Убеждение — страховка, а не триумф");
        }

        [Test]
        public void Check_Intimidate_ShiftsBothTails()
        {
            var cfg = Cfg();

            // Хвост вверх: Лучшая доступна с меньшим запасом.
            int early = cfg.Checks.BestMargin - cfg.Checks.IntimidateBestBonus;
            Assert.AreEqual(OutcomeBand.Best, CheckResolver.BandFor(early, ApproachForm.Intimidate, cfg.Checks));
            Assert.AreEqual(OutcomeBand.Good, CheckResolver.BandFor(early, ApproachForm.Neutral, cfg.Checks));

            // Хвост вниз: провал добавляет страх.
            var roster = RosterOf(new FakeActor { Id = "a", Value = 1 });
            var outcome = CheckResolver.Resolve(
                new CheckRequest(SkillKeys.Intimidate, 10, ApproachForm.Intimidate), roster, null, 1, cfg);

            Assert.AreEqual(OutcomeBand.Worst, outcome.Band);
            Assert.IsTrue(outcome.CausedFear, "Провал запугивания бьёт по отношению");
        }

        [Test]
        public void Check_Trade_ConvertsBandIntoPrice()
        {
            var cfg = Cfg();
            var roster = RosterOf(new FakeActor { Id = "a", Value = 15 });

            var outcome = CheckResolver.Resolve(
                new CheckRequest(SkillKeys.Trade, 5, ApproachForm.Trade), roster, null, 1, cfg);

            Assert.AreEqual(OutcomeBand.Best, outcome.Band);
            Assert.Less(outcome.PriceMultiplier, 1.0, "Хороший торг — это скидка, а не драма");
        }

        // ---- Защита от спама обращений ----

        [Test]
        public void Check_RepeatWithinWindow_RaisesThreshold()
        {
            var cfg = Cfg();
            var roster = RosterOf(new FakeActor { Id = "a", Value = 10 });
            var repeats = new SimpleRepeats();
            var request = new CheckRequest(Skill, 5, ApproachForm.Neutral, "same-topic");

            int first = CheckResolver.Preview(request, roster, repeats, 1, cfg).EffectiveThreshold;
            CheckResolver.Resolve(request, roster, repeats, 1, cfg);
            int second = CheckResolver.Preview(request, roster, repeats, 2, cfg).EffectiveThreshold;

            Assert.AreEqual(first + cfg.Checks.RepeatPenaltyStep, second,
                "Повторное обращение по той же теме дороже");
        }

        [Test]
        public void Check_RepeatOutsideWindow_DoesNotPenalize()
        {
            var cfg = Cfg();
            var roster = RosterOf(new FakeActor { Id = "a", Value = 10 });
            var repeats = new SimpleRepeats();
            var request = new CheckRequest(Skill, 5, ApproachForm.Neutral, "same-topic");

            CheckResolver.Resolve(request, roster, repeats, 1, cfg);
            int later = CheckResolver.Preview(request, roster, repeats,
                1 + cfg.Checks.RepeatWindowDays, cfg).EffectiveThreshold;

            Assert.AreEqual(5, later, "За пределами окна штраф снимается");
        }

        [Test]
        public void Check_TieBrokenById_ForReproducibility()
        {
            var roster = RosterOf(
                new FakeActor { Id = "zeta", Value = 7 },
                new FakeActor { Id = "alpha", Value = 7 });

            var preview = CheckResolver.Preview(new CheckRequest(Skill, 5), roster, null, 1, Cfg());

            Assert.AreEqual("alpha", preview.BestActorId,
                "Ничья решается по Id — иначе результат зависел бы от порядка в списке");
        }

        private sealed class SimpleRepeats : IRepeatTracker
        {
            private readonly List<KeyValuePair<string, int>> _log = new List<KeyValuePair<string, int>>();

            public int AttemptsInWindow(string topicId, int day, int windowDays)
            {
                int n = 0;
                foreach (var e in _log)
                    if (e.Key == topicId && day - e.Value < windowDays) n++;
                return n;
            }

            public void Register(string topicId, int day) =>
                _log.Add(new KeyValuePair<string, int>(topicId, day));
        }
    }
}
