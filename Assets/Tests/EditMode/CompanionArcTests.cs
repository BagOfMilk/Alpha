using System.Collections.Generic;
using Game.Core.Characters;
using Game.Core.Companions;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Личные арки напарников (B4, порт US-9.5 на модель Э2): гейт лояльностью/
    /// прогрессом, обрыв смертью/уходом в антагонисты. Содержание глав —
    /// id/ключи (<see cref="ArcChapter.QuestId"/>/<see cref="ArcChapter.TitleKey"/>),
    /// сам квест-рушій — B6, декаплінг рядком (§1.1).
    /// </summary>
    public class CompanionArcTests
    {
        private static Companion Comp(Roster roster, string id)
        {
            var c = new CompanionArchetype(id, id).CreateInstance(id);
            roster.Add(c);
            return c;
        }

        // ---- Гейт лояльности ----
        [Test]
        public void Chapter_LockedUntilLoyalty()
        {
            var roster = new Roster();
            var c = Comp(roster, "c"); // старт 50 = Steady
            var arc = new CompanionArc("a", "c", "arc.a.title")
                .Chapter(new ArcChapter("ch1", "quest.a.ch1", "arc.a.ch1.title").Loyalty(LoyaltyBand.Devoted));
            var run = new CompanionArcRun(arc, new HashSet<string>());

            run.Refresh(c);
            Assert.AreEqual(ArcState.Locked, run.State, "Steady < Devoted — заперто");

            c.ApplyLoyaltyDelta(30); // 80 = Devoted
            bool opened = run.Refresh(c);
            Assert.AreEqual(ArcState.Available, run.State);
            Assert.IsTrue(opened, "перехід у Available сигналізує подію arc.chapter_opened");
        }

        [Test]
        public void Refresh_DoesNotReopenSignal_OnRepeatedCalls()
        {
            var roster = new Roster();
            var c = Comp(roster, "c");
            var arc = new CompanionArc("a", "c", "arc.a.title")
                .Chapter(new ArcChapter("ch1", "quest.a.ch1", "arc.a.ch1.title").Loyalty(LoyaltyBand.Steady));
            var run = new CompanionArcRun(arc, new HashSet<string>());

            Assert.IsTrue(run.Refresh(c), "перший раз — щойно відкрилося");
            Assert.IsFalse(run.Refresh(c), "друге звернення без змін — вже не подія");
        }

        // ---- Последовательность глав по флагу ----
        [Test]
        public void Chapters_GateByProgressFlag()
        {
            var roster = new Roster();
            var c = Comp(roster, "c");
            var flags = new HashSet<string>();
            var arc = new CompanionArc("a", "c", "arc.a.title")
                .Chapter(new ArcChapter("ch1", "quest.a.ch1", "arc.a.ch1.title").Loyalty(LoyaltyBand.Steady).SetsFlag("f1"))
                .Chapter(new ArcChapter("ch2", "quest.a.ch2", "arc.a.ch2.title").Loyalty(LoyaltyBand.Steady).NeedsFlag("external"));
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
            var run = new CompanionArcRun(new CompanionArc("a", "c", "arc.a.title")
                .Chapter(new ArcChapter("ch1", "quest.a.ch1", "arc.a.ch1.title").Loyalty(LoyaltyBand.Steady)), new HashSet<string>());
            run.Refresh(c);
            Assert.AreEqual(ArcState.Available, run.State);

            c.MarkDead();
            run.Refresh(c);
            Assert.AreEqual(ArcState.Aborted, run.State, "смерть обрывает арку (US-9.5)");
        }

        [Test]
        public void Arc_AbortsOnDefection()
        {
            var roster = new Roster();
            var c = Comp(roster, "c");
            var run = new CompanionArcRun(new CompanionArc("a", "c", "arc.a.title")
                .Chapter(new ArcChapter("ch1", "quest.a.ch1", "arc.a.ch1.title").Loyalty(LoyaltyBand.Steady)), new HashSet<string>());
            run.Refresh(c);

            Defection.Defect(c);
            run.Refresh(c);
            Assert.AreEqual(ArcState.Aborted, run.State, "уход в антагонисты обрывает арку");
        }

        // ---- Завершение ----
        [Test]
        public void Arc_Completes_AfterLastChapter()
        {
            var roster = new Roster();
            var c = Comp(roster, "c");
            var run = new CompanionArcRun(new CompanionArc("a", "c", "arc.a.title")
                .Chapter(new ArcChapter("ch1", "quest.a.ch1", "arc.a.ch1.title").Loyalty(LoyaltyBand.Steady)), new HashSet<string>());

            Assert.IsTrue(run.Begin(c));
            run.CompleteChapter();
            Assert.AreEqual(ArcState.Completed, run.State);
            Assert.IsTrue(run.IsFinished);
        }

        // ---- Содержательная честность контента (id/ключи, а не текст) ----
        [Test]
        public void DefaultArcs_Chapters_HaveContentIdsAndConsistentFlagChain()
        {
            foreach (var arc in DefaultArcs.All())
            {
                Assert.IsNotEmpty(arc.CompanionId, arc.Id + ": companionId не задан");
                Assert.IsNotEmpty(arc.TitleKey, arc.Id + ": TitleKey не задан");
                Assert.IsTrue(arc.TitleKey.StartsWith("arc."), arc.Id + ": TitleKey обязан быть ключом, не текстом");

                string previousCompletionFlag = null;
                foreach (var ch in arc.Chapters)
                {
                    Assert.IsNotEmpty(ch.QuestId, ch.Id + ": QuestId (шов для B6) не задан");
                    Assert.IsNotEmpty(ch.TitleKey, ch.Id + ": TitleKey не задан");
                    if (previousCompletionFlag != null)
                        Assert.AreEqual(previousCompletionFlag, ch.RequiresFlag,
                            ch.Id + ": глава не продолжает цепочку флагов предыдущей");
                    previousCompletionFlag = ch.CompletionFlag;
                }
            }
        }

        [Test]
        public void DefaultArcs_BandsGrowMonotonically()
        {
            foreach (var arc in DefaultArcs.All())
            {
                LoyaltyBand? prev = null;
                foreach (var ch in arc.Chapters)
                {
                    if (prev.HasValue)
                        Assert.GreaterOrEqual((int)ch.RequiredLoyalty, (int)prev.Value,
                            arc.Id + "/" + ch.Id + ": полоса следующей главы не должна требовать МЕНЬШЕ доверия");
                    prev = ch.RequiredLoyalty;
                }
            }
        }
    }
}
