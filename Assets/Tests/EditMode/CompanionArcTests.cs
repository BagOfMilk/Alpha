using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Companions;
using Game.Core.Economy;
using Game.Core.Quests;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Личные арки напарников (US-9.5): гейт лояльностью/прогрессом, последовательные
    /// главы на QuestRun, обрыв смертью/предательством.
    /// </summary>
    public class CompanionArcTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();

        private static Companion Comp(Roster roster, string id)
        {
            var c = new Companion(id, new AttributeBlock(3, 3, 3, 3), 4);
            roster.Add(c);
            return c;
        }

        private static QuestDefinition Stub() =>
            new QuestDefinition("s", "S", QuestSource.RandomEvent)
                .Stage(QuestStage.OutcomeStage("end", "", true));

        // ---- Гейт лояльности ----
        [Test]
        public void Chapter_LockedUntilLoyalty()
        {
            var roster = new Roster();
            var c = Comp(roster, "c"); // старт 50 = Steady
            var arc = new CompanionArc("a", "c", "T")
                .Chapter(new ArcChapter("ch1", Stub()).Loyalty(LoyaltyBand.Devoted));
            var run = new CompanionArcRun(arc, new HashSet<string>());

            run.Refresh(c);
            Assert.AreEqual(ArcState.Locked, run.State, "Steady < Devoted — заперто");

            c.AdjustLoyalty(30); // 80 = Devoted
            run.Refresh(c);
            Assert.AreEqual(ArcState.Available, run.State);
        }

        // ---- Последовательность глав по флагу ----
        [Test]
        public void Chapters_GateByProgressFlag()
        {
            var roster = new Roster();
            var c = Comp(roster, "c");
            var flags = new HashSet<string>();
            var arc = new CompanionArc("a", "c", "T")
                .Chapter(new ArcChapter("ch1", Stub()).Loyalty(LoyaltyBand.Steady).SetsFlag("f1"))
                .Chapter(new ArcChapter("ch2", Stub()).Loyalty(LoyaltyBand.Steady).NeedsFlag("external"));
            var run = new CompanionArcRun(arc, flags);

            run.Refresh(c);
            Assert.AreEqual(ArcState.Available, run.State); // глава 1
            run.Begin(c);
            run.CompleteChapter();
            Assert.IsTrue(flags.Contains("f1"), "завершение главы ставит её флаг");

            run.Refresh(c);
            Assert.AreEqual(ArcState.Locked, run.State, "глава 2 ждёт внешний флаг");
            flags.Add("external");
            run.Refresh(c);
            Assert.AreEqual(ArcState.Available, run.State);
        }

        // ---- Обрыв арки ----
        [Test]
        public void Arc_AbortsOnDeath()
        {
            var roster = new Roster();
            var c = Comp(roster, "c");
            var run = new CompanionArcRun(new CompanionArc("a", "c", "T")
                .Chapter(new ArcChapter("ch1", Stub()).Loyalty(LoyaltyBand.Steady)), new HashSet<string>());
            run.Refresh(c);
            Assert.AreEqual(ArcState.Available, run.State);

            c.Kill();
            run.Refresh(c);
            Assert.AreEqual(ArcState.Aborted, run.State, "смерть обрывает арку (US-9.5)");
        }

        [Test]
        public void Arc_AbortsOnBetrayal()
        {
            var roster = new Roster();
            var c = Comp(roster, "c");
            var run = new CompanionArcRun(new CompanionArc("a", "c", "T")
                .Chapter(new ArcChapter("ch1", Stub()).Loyalty(LoyaltyBand.Steady)), new HashSet<string>());
            run.Refresh(c);

            c.Status = CompanionStatus.Antagonist;
            run.Refresh(c);
            Assert.AreEqual(ArcState.Aborted, run.State, "уход в антагонисты обрывает арку");
        }

        // ---- Завершение ----
        [Test]
        public void Arc_Completes_AfterLastChapter()
        {
            var roster = new Roster();
            var c = Comp(roster, "c");
            var run = new CompanionArcRun(new CompanionArc("a", "c", "T")
                .Chapter(new ArcChapter("ch1", Stub()).Loyalty(LoyaltyBand.Steady)), new HashSet<string>());

            Assert.IsTrue(run.Begin(c));
            run.CompleteChapter();
            Assert.AreEqual(ArcState.Completed, run.State);
            Assert.IsTrue(run.IsFinished);
        }

        // ---- Интеграция с QuestRun ----
        [Test]
        public void Chapter_PlayedThroughQuestRun_ThenAdvances()
        {
            var roster = new Roster();
            var c = Comp(roster, "medic");
            c.Skills.Set(SkillType.Medicine, 5);
            var baseState = new BaseState(roster, new ResourceLedger(), Cfg);
            var flags = new HashSet<string>();

            var quest = new QuestDefinition("q", "Q", QuestSource.NpcSettlement)
                .Stage(QuestStage.SkillCheck("heal", "", SkillType.Medicine, 3, 1, 2))
                .Stage(QuestStage.OutcomeStage("ok", "", true, new QuestReward(20)))
                .Stage(QuestStage.OutcomeStage("no", "", false));
            var run = new CompanionArcRun(new CompanionArc("a", "medic", "T")
                .Chapter(new ArcChapter("ch1", quest).Loyalty(LoyaltyBand.Steady)), flags);

            Assert.IsTrue(run.Begin(c));
            var qr = new QuestRun(run.CurrentChapter.Quest, baseState, Cfg, null, null, flags);
            qr.ResolveCheck(new List<Companion> { c });
            Assert.AreEqual(QuestState.Succeeded, qr.State);

            run.CompleteChapter();
            Assert.AreEqual(ArcState.Completed, run.State);
        }

        // ---- Структурная честность контента ----
        [Test]
        public void DefaultArcs_Chapters_Validate()
        {
            foreach (var ch in DefaultArcs.MedicOldDebt().Chapters)
                Assert.IsTrue(ch.Quest.Validate(out var err), $"{ch.Id}: {err}");
        }
    }
}
