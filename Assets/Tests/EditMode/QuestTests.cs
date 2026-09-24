using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Checks;
using Game.Core.Quests;
using Game.Core.Story;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Квестовий рушій поза конвеєром дня (R6, пакет B6): 4 полоси з першого
    /// рядка (Поправка №3.7, не бінарний успіх/провал), наслідок — ДАНІ
    /// (<see cref="QuestConsequence"/>), а не дія — рушій сам нічого не
    /// застосовує ні до Напруги, ні до фракцій/лояльності (їх пакетів тут ще
    /// нема, §1.1 — паралельна фаза B).
    /// </summary>
    public class QuestTests
    {
        private sealed class FakeActor : ISettlementActor
        {
            public string Id { get; set; }
            public bool IsPresentInSettlement { get; set; } = true;
            public bool IsProtagonist { get; set; }
            public string HeldPositionId { get; set; }
            public int Value { get; set; }

            public int GetCheckValue(SkillKey skill) => Value;
            public int GetTraitModifier(SkillKey skill) => 0;
        }

        private sealed class FakeRoster : IRosterView
        {
            public List<ISettlementActor> Actors { get; } = new List<ISettlementActor>();
            public IReadOnlyList<ISettlementActor> PresentActors => Actors;
            public ISettlementActor Protagonist { get; set; }
        }

        private static BalanceConfig Cfg() => new BalanceConfig();

        private static FakeRoster RosterOf(params ISettlementActor[] actors)
        {
            var r = new FakeRoster();
            r.Actors.AddRange(actors);
            return r;
        }

        // ---- Перевірка: 4 полоси гілкують і несуть нагороду як ДАНІ ----

        [Test]
        public void Check_Success_BranchesAndRewards()
        {
            var cfg = Cfg();
            var roster = RosterOf(new FakeActor { Id = "a", Value = 8 }); // порог 3, запас 5 -> Хороша

            var def = new QuestDefinition("q", "offer")
                .Stage(QuestStage.Check("c", null, SkillKeys.Survival, 3, ApproachForm.Neutral,
                    nextByBand: new[] { 1, 1, 2, 2 },
                    consequenceByBand: new[]
                    {
                        null, null,
                        new QuestConsequence().WithXp(50),
                        new QuestConsequence().WithXp(50)
                    }))
                .Stage(QuestStage.OutcomeStage("bad", "bad", success: false))
                .Stage(QuestStage.OutcomeStage("ok", "ok", success: true, new QuestConsequence().Tension(-5)));

            var run = new QuestRun(def);
            var report = run.ResolveCheck(roster, null, 1, cfg);

            Assert.AreEqual(OutcomeBand.Good, report.Band);
            Assert.AreEqual("a", report.ResolvedActorId);
            Assert.IsTrue(report.Terminal);
            Assert.AreEqual(QuestState.Succeeded, run.State);
            Assert.AreEqual(50, report.Consequence.Xp, "квест дає XP як ДАНІ (R6), рушій сам нікого не якає");
            Assert.AreEqual(-5, report.Consequence.TensionDelta,
                "фінальна нагорода термінала об'єднується з наслідком переходу (QuestConsequence.Merge)");
        }

        [Test]
        public void Check_UnmannedPosition_YieldsWorstBand()
        {
            var roster = RosterOf(new FakeActor { Id = "a", Value = 20, HeldPositionId = "other" });
            var def = new QuestDefinition("q", "offer")
                .Stage(QuestStage.Check("c", null, SkillKeys.Survival, 3, ApproachForm.Neutral,
                    nextByBand: new[] { 1, 1, 1, 1 }, requiredPositionId: "hafiya_post"))
                .Stage(QuestStage.OutcomeStage("bad", "bad", false));

            var report = new QuestRun(def).ResolveCheck(roster, null, 1, Cfg());

            Assert.AreEqual(OutcomeBand.Worst, report.Band);
            Assert.IsFalse(report.HasCandidate, "порожня позиція — Найгірша полоса (US-8.2), як і в інцидентах");
        }

        /// <summary>
        /// «Контекст перевірки» у моделі R6 — це ApproachForm (Убеждення/
        /// Запугивание/Торговля змінюють ФОРМУ лестниці — CheckResolver.BandFor),
        /// а не окремий атрибут, як у колишньому рушії. Тут перевіряємо, що
        /// квестова перевірка чесно передає Approach у той самий CheckResolver,
        /// що й будь-яка інша перевірка гри.
        /// </summary>
        [Test]
        public void SocialCheck_UsesContextualAttribute()
        {
            var cfg = Cfg();
            var roster = RosterOf(new FakeActor { Id = "a", Value = 8 }); // проти порогу 10 -> запас -2

            var def = new QuestDefinition("q", "offer")
                .Stage(QuestStage.Check("scare", null, SkillKeys.Intimidate, 10, ApproachForm.Persuade,
                    nextByBand: new[] { 1, 2, 2, 2 }))
                .Stage(QuestStage.OutcomeStage("fail", "fail", false))
                .Stage(QuestStage.OutcomeStage("ok", "ok", true));

            var run = new QuestRun(def);
            var report = run.ResolveCheck(roster, null, 1, cfg);

            Assert.AreEqual(OutcomeBand.Base, report.Band,
                "Убеждение вытягивает недобор 2 при подушке 2 до Базовой (той самий CheckResolver.BandFor)");
            Assert.AreEqual(QuestState.Succeeded, run.State);
        }

        // ---- Вибір: наслідок як дані, гейт за прапором ----

        [Test]
        public void Choice_ReturnsConsequenceData_AndRespectsFlagGate()
        {
            var def = new QuestDefinition("q", "offer")
                .Stage(QuestStage.ChoiceStage("pick", "pick")
                    .Option(new QuestOption("locked", next: 1).GateFlag("unlocked"))
                    .Option(new QuestOption("open", next: 1,
                        new QuestConsequence().Faction("community", 10).Loyalty("n", -6).Flag("hardline").Tension(4))))
                .Stage(QuestStage.OutcomeStage("done", "done", true));

            var flags = new StoryFlags();
            var run = new QuestRun(def);

            var blocked = run.Choose(0, flags);
            Assert.IsFalse(blocked.Accepted, "варіант закритий, поки прапор не стоїть");
            Assert.AreEqual(0, run.CurrentIndex, "недоступний варіант не рухає квест");
            Assert.AreEqual(QuestState.Active, run.State);

            var accepted = run.Choose(1, flags);
            Assert.IsTrue(accepted.Accepted);
            Assert.AreEqual(10, accepted.Consequence.FactionDeltas["community"]);
            Assert.AreEqual(-6, accepted.Consequence.LoyaltyDeltas["n"]);
            CollectionAssert.Contains(accepted.Consequence.Flags, "hardline");
            Assert.AreEqual(4, accepted.Consequence.TensionDelta);
            Assert.AreEqual(QuestState.Succeeded, run.State);
        }

        [Test]
        public void Choice_UnlockedByFlag_Works()
        {
            var def = new QuestDefinition("q", "offer")
                .Stage(QuestStage.ChoiceStage("pick", "pick")
                    .Option(new QuestOption("locked", next: 1).GateFlag("unlocked")))
                .Stage(QuestStage.OutcomeStage("done", "done", true));

            var flags = new StoryFlags();
            flags.Set("unlocked");
            var run = new QuestRun(def);

            var report = run.Choose(0, flags);
            Assert.IsTrue(report.Accepted);
            Assert.AreEqual(QuestState.Succeeded, run.State);
        }

        // ---- Структурна чесність контенту ----

        [Test]
        public void Validate_DetectsDeadEnd()
        {
            var bad = new QuestDefinition("q", "offer")
                .Stage(QuestStage.Check("c", null, SkillKeys.Survival, 3, ApproachForm.Neutral,
                    nextByBand: new[] { 1, 1, 99, 1 }))
                .Stage(QuestStage.OutcomeStage("ok", "ok", true));

            string error;
            Assert.IsFalse(bad.Validate(out error));
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void DefaultQuests_AllValidate_NoDeadEnds()
        {
            var cfg = Cfg();
            foreach (var q in DefaultQuests.All(cfg))
            {
                string error;
                Assert.IsTrue(q.Validate(out error), q.Id + ": " + error);
            }
        }

        // ---- Наслідок: об'єднання (перехід + нагорода термінала) ----

        [Test]
        public void QuestConsequence_Merge_SumsAndUnions()
        {
            var a = new QuestConsequence().Tension(5).WithXp(10).Faction("f", 3).Flag("x");
            var b = new QuestConsequence().Tension(-2).WithXp(4).Faction("f", 2).Flag("y");

            var merged = QuestConsequence.Merge(a, b);

            Assert.AreEqual(3, merged.TensionDelta);
            Assert.AreEqual(14, merged.Xp);
            Assert.AreEqual(5, merged.FactionDeltas["f"]);
            CollectionAssert.AreEquivalent(new[] { "x", "y" }, merged.Flags);
        }

        // ---- Квест Гафії «Гірка розрада» (§3.2–3.5): кінець-в-кінець через власне API ----

        [Test]
        public void Hafiya_Accepted_GrassFound_ReachesBestThanksAndFlag()
        {
            var cfg = Cfg();
            var roster = RosterOf(new FakeActor { Id = "scout", Value = 9 }); // поріг 5, запас 4 -> Хороша+
            var log = new QuestLog(DefaultQuests.All(cfg));

            var run = log.Start(DefaultQuests.HafiyaId);
            Assert.IsNotNull(run);

            var offer = run.Choose(0); // взятися
            Assert.IsTrue(offer.Accepted);
            Assert.AreEqual(1, run.CurrentIndex);

            var check = run.ResolveCheck(roster, null, 3, cfg);
            Assert.GreaterOrEqual(check.Band, OutcomeBand.Good);
            Assert.IsTrue(check.Terminal);
            Assert.AreEqual(QuestState.Succeeded, run.State);
            Assert.AreEqual(DefaultQuests.Stage3BestKey, run.Current.TextKey);
            CollectionAssert.Contains(check.Consequence.Flags, DefaultQuests.HafiyaGrassFoundFlag);
            Assert.Greater(check.Consequence.Xp, 0);
        }

        [Test]
        public void Hafiya_Accepted_GrassMissing_ReachesWorstThanks_NoFlag()
        {
            var cfg = Cfg();
            var roster = RosterOf(new FakeActor { Id = "scout", Value = 1 }); // явно нижче порогу
            var log = new QuestLog(DefaultQuests.All(cfg));

            var run = log.Start(DefaultQuests.HafiyaId);
            run.Choose(0);
            var check = run.ResolveCheck(roster, null, 3, cfg);

            Assert.LessOrEqual(check.Band, OutcomeBand.Base);
            Assert.AreEqual(QuestState.Failed, run.State);
            Assert.AreEqual(DefaultQuests.Stage3WorstKey, run.Current.TextKey);
            CollectionAssert.DoesNotContain(check.Consequence.Flags, DefaultQuests.HafiyaGrassFoundFlag);
        }

        /// <summary>
        /// Фікс-рев'ю B6: <c>QuestBalance.TensionByBand</c> — правлений власником
        /// Balance SO масив (R14); контентна правка, що скоротила його нижче 4
        /// елементів, не має валити побудову квесту <c>IndexOutOfRangeException</c> —
        /// так само, як короткий масив деградує у <c>IncidentResolver</c>/
        /// <c>ReadinessBalance</c>.
        /// </summary>
        [Test]
        public void Hafiya_ToleratesShortTensionByBand_NoException()
        {
            var cfg = Cfg();
            cfg.Quest.TensionByBand = new[] { 40 }; // контент-патч скоротив масив до одного елемента

            Assert.DoesNotThrow(() => DefaultQuests.Hafiya(cfg));
        }

        [Test]
        public void Hafiya_Declined_EndsWithoutCheck()
        {
            var cfg = Cfg();
            var log = new QuestLog(DefaultQuests.All(cfg));
            var run = log.Start(DefaultQuests.HafiyaId);

            var declined = run.Choose(1); // відмовити
            Assert.IsTrue(declined.Accepted);
            Assert.IsTrue(declined.Terminal);
            Assert.AreEqual(QuestState.Failed, run.State);
            Assert.AreEqual(DefaultQuests.DeclinedKey, run.Current.TextKey);
        }

        // ---- Сейв/завантаження: квест поза конвеєром теж переживає перезапуск ----

        [Test]
        public void QuestLog_SaveRoundTrip_PreservesActiveRunMidway()
        {
            var cfg = Cfg();
            var pool = DefaultQuests.All(cfg);
            var log = new QuestLog(pool);

            var run = log.Start(DefaultQuests.HafiyaId);
            run.Choose(0); // прийняв, зупинився на перевірці (index 1), ще Active

            var blob = log.CaptureState();
            Assert.IsNotEmpty(blob);

            var restoredLog = new QuestLog(pool);
            restoredLog.RestoreState(blob);
            var restoredRun = restoredLog.Get(DefaultQuests.HafiyaId);

            Assert.IsNotNull(restoredRun);
            Assert.AreEqual(run.CurrentIndex, restoredRun.CurrentIndex);
            Assert.AreEqual(run.State, restoredRun.State);
            Assert.IsTrue(restoredRun.IsActive);
        }

        [Test]
        public void QuestLog_RestoreState_DropsQuestsMissingFromPool()
        {
            var cfg = Cfg();
            var pool = DefaultQuests.All(cfg);
            var log = new QuestLog(pool);
            log.Start(DefaultQuests.HafiyaId);
            var blob = log.CaptureState();

            var restoredLog = new QuestLog(); // порожній пул — контент-патч
            restoredLog.RestoreState(blob);

            Assert.IsNull(restoredLog.Get(DefaultQuests.HafiyaId),
                "квест із сейву, якого нема в пулі, тихо відкидається (як і в колишньому рушії)");
        }
    }
}
