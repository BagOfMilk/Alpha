using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Dungeons;
using Game.Core.Economy;
using Game.Core.Items;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Данж (Эпик 12, push-your-luck): детерминизм генерации, рост угрозы/глубины,
    /// элита по порогу, банк ТОЛЬКО на экстракте, вайп губит незабанкованное,
    /// проверки (мягкий сетбэк на провале).
    /// </summary>
    public class DungeonTests
    {
        private static readonly List<EnemyDefinition> Pool = new List<EnemyDefinition>
        {
            Game.Core.DefaultContent.ScavGunner()
        };

        /// <summary>Генератор с жёстко заданным типом комнат (для детерминированных тестов).</summary>
        private static DungeonGenerator Gen(int[] typeWeights)
            => new DungeonGenerator(Pool, DefaultItems.DropTable()) { TypeWeights = typeWeights };

        private static readonly int[] CombatOnly = { 1, 0, 0, 0 };
        private static readonly int[] LootOnly = { 0, 1, 0, 0 };
        private static readonly int[] CheckOnly = { 0, 0, 1, 0 };
        private static readonly int[] EventOnly = { 0, 0, 0, 1 };

        private static Companion Hacker(int hacking)
        {
            var c = new Companion("hacker", new AttributeBlock(3, 3, 3, 3), 4);
            c.Skills.Raise(SkillType.Hacking, hacking);
            return c;
        }
        private static IReadOnlyList<Companion> Squad(params Companion[] cs) => cs;

        // ---- Генерация ----
        [Test]
        public void Generation_IsDeterministic_BySeed()
        {
            var g = DefaultDungeon.NewGenerator();
            var a = g.NextRoom(3, 4, new SeededRng(99));
            var b = g.NextRoom(3, 4, new SeededRng(99));
            Assert.AreEqual(a.Type, b.Type);
            Assert.AreEqual(a.DisplayName, b.DisplayName);
        }

        [Test]
        public void Combat_ScalesEnemyCountWithDepth()
        {
            var g = Gen(CombatOnly);
            var shallow = g.NextRoom(1, 0, new SeededRng(1)); // 2 + 1/2 = 2
            var deep = g.NextRoom(8, 0, new SeededRng(1));    // 2 + 8/2 = 6
            Assert.AreEqual(2, shallow.Enemies.Count);
            Assert.Greater(deep.Enemies.Count, shallow.Enemies.Count);
        }

        // ---- Push: глубина и угроза ----
        [Test]
        public void Push_RaisesDepthAndThreat()
        {
            var run = new DungeonRun(Gen(CombatOnly), new SeededRng(5));
            Assert.AreEqual(1, run.Depth);
            Assert.AreEqual(2, run.Threat); // ThreatPerDepth

            run.ReportCombat(true);          // зачистили текущую
            run.Push();
            Assert.AreEqual(2, run.Depth);
            Assert.AreEqual(4, run.Threat);
        }

        [Test]
        public void Elite_AppearsAtThreatThreshold()
        {
            var g = Gen(CombatOnly);
            g.EliteThreatThreshold = 2; // первый же заход (Threat=2) даёт элиту
            var run = new DungeonRun(g, new SeededRng(3));
            Assert.IsTrue(run.CurrentRoom.Elite, "на пороге угрозы — подкрепление/элита");
        }

        // ---- Лут банкуется только на экстракте ----
        [Test]
        public void Loot_AccumulatesUnbanked_BanksOnlyOnExtract()
        {
            var run = new DungeonRun(Gen(LootOnly), new SeededRng(7));
            var ledger = new ResourceLedger();
            var baseState = new BaseState(new Roster(), ledger, new BalanceConfig());

            run.ResolveRoom(Squad());                 // первая лут-комната
            run.Push(); run.ResolveRoom(Squad());      // вторая
            Assert.Greater(run.UnbankedGold, 0);
            Assert.AreEqual(0, ledger.Get(ResourceType.Gold), "до экстракта в город ничего не зачислено");

            int expectGold = run.UnbankedGold, expectBuild = run.UnbankedBuilding, expectCraft = run.UnbankedCrafting;
            var rep = run.Extract(baseState);

            Assert.AreEqual(DungeonOutcome.Extracted, run.Outcome);
            Assert.AreEqual(expectGold, ledger.Get(ResourceType.Gold));
            Assert.AreEqual(expectBuild, ledger.Get(ResourceType.BuildingMaterial));
            Assert.AreEqual(expectCraft, ledger.Get(ResourceType.CraftingMaterial));
            Assert.AreEqual(0, run.UnbankedGold, "незабанкованное очищено");
            Assert.AreEqual(expectGold, rep.Gold);
        }

        // ---- Вайп губит незабанкованное ----
        [Test]
        public void Wipe_LosesUnbanked_NothingBanked()
        {
            var run = new DungeonRun(Gen(CombatOnly), new SeededRng(11));
            var ledger = new ResourceLedger();
            var baseState = new BaseState(new Roster(), ledger, new BalanceConfig());

            run.ReportCombat(true);   // накопили награду
            Assert.Greater(run.UnbankedGold, 0);
            run.Push();
            run.ReportCombat(false);  // вайп

            Assert.AreEqual(DungeonOutcome.Wiped, run.Outcome);
            Assert.AreEqual(0, run.UnbankedGold, "незабанкованное потеряно");
            Assert.AreEqual(0, ledger.Get(ResourceType.Gold), "в город ничего не попало");
            Assert.Throws<System.InvalidOperationException>(() => run.Extract(baseState), "экстракт после вайпа невозможен");
        }

        // ---- Проверки: успех/провал ----
        [Test]
        public void Check_Success_GainsGold()
        {
            var run = new DungeonRun(Gen(CheckOnly), new SeededRng(2));
            int threatBefore = run.Threat;
            var res = run.ResolveRoom(Squad(Hacker(5))); // порог 2 на глубине 1
            Assert.IsTrue(res.CheckSuccess);
            Assert.Greater(run.UnbankedGold, 0);
            Assert.AreEqual(threatBefore, run.Threat, "успех угрозу не растит");
        }

        [Test]
        public void Check_Fail_SoftSetback_RaisesThreat_NoLoot()
        {
            var run = new DungeonRun(Gen(CheckOnly), new SeededRng(2));
            int threatBefore = run.Threat;
            var res = run.ResolveRoom(Squad(Hacker(0))); // не вывезли
            Assert.IsFalse(res.CheckSuccess);
            Assert.AreEqual(0, run.UnbankedGold);
            Assert.Greater(run.Threat, threatBefore, "провал — дальше опаснее (мягкий сетбэк)");
        }

        // ---- Защита инвариантов ----
        [Test]
        public void Push_RequiresCurrentRoomCleared()
        {
            var run = new DungeonRun(Gen(CombatOnly), new SeededRng(1));
            Assert.Throws<System.InvalidOperationException>(() => run.Push(), "нельзя глубже, не пройдя комнату");
        }

        [Test]
        public void ResolveRoom_RejectsCombatRoom()
        {
            var run = new DungeonRun(Gen(CombatOnly), new SeededRng(1));
            Assert.Throws<System.InvalidOperationException>(() => run.ResolveRoom(Squad()),
                "боевую комнату резолвит ReportCombat");
        }

        // ---- Мини-события С ВЫБОРОМ (US-12.3) ----
        [Test]
        public void Event_OffersChoices_AutoResolveRejected()
        {
            var run = new DungeonRun(Gen(EventOnly), new SeededRng(4));
            Assert.AreEqual(RoomType.Event, run.CurrentRoom.Type);
            Assert.GreaterOrEqual(run.CurrentRoom.EventOptions.Count, 2, "у мини-события есть выбор");
            Assert.Throws<System.InvalidOperationException>(() => run.ResolveRoom(Squad()),
                "событие резолвится только выбором игрока");
        }

        [Test]
        public void Event_GreedyChoice_TradesThreatForGold()
        {
            var run = new DungeonRun(Gen(EventOnly), new SeededRng(4));
            var opt = run.CurrentRoom.EventOptions[0]; // жадный вариант
            int threatBefore = run.Threat;

            var res = run.ResolveEvent(0);
            Assert.Greater(opt.ThreatDelta, 0, "жадный вариант шумит");
            Assert.AreEqual(opt.GoldGain, run.UnbankedGold, "хабар копится в незабанкованное");
            Assert.AreEqual(threatBefore + opt.ThreatDelta, run.Threat);
            Assert.AreEqual(opt.Label, res.Note);
            Assert.IsTrue(run.CurrentCleared);
        }

        [Test]
        public void Event_CautiousChoice_KeepsThreatQuiet()
        {
            var run = new DungeonRun(Gen(EventOnly), new SeededRng(4));
            int threatBefore = run.Threat;
            run.ResolveEvent(1); // осторожный вариант
            Assert.AreEqual(threatBefore, run.Threat, "тихий вариант угрозу не растит");
        }
    }
}
