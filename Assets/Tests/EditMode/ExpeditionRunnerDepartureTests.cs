using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Checks;
using Game.Core.Economy;
using Game.Core.Expeditions;
using Game.Core.Loop;
using Game.Core.Pressure;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Уніфікація вилазки (R15, закриває D10): один потік
    /// <c>ExpeditionRunner.Depart</c> → <c>ExpeditionParty.Depart</c> →
    /// <c>ExpeditionResolver.Resolve</c> РІВНО ОДИН РАЗ → результат заморожений
    /// у блобі партії → <c>ExpeditionRunner.Complete</c> на поверненні.
    /// </summary>
    public class ExpeditionRunnerDepartureTests
    {
        private static BalanceConfig Cfg() => new BalanceConfig { FoodUpkeepPerCompanion = 0 };

        private static BaseState Build(BalanceConfig cfg, out ExpeditionParty party, out SiteLedger ledger)
        {
            var roster = new Roster();
            var state = new BaseState(roster, new ResourceLedger(), cfg);
            foreach (var slot in Game.Core.DefaultContent.AllSlots()) state.AddSlot(slot);

            var arch = new CompanionArchetype("scout", "Розвідник");
            arch.SetSkill(SkillType.Survival, 8);
            roster.Add(arch.CreateInstance("scout_1", cfg));
            state.TryAssign("scout_1", "storehouse_dock");

            party = new ExpeditionParty();
            ledger = new SiteLedger();
            return state;
        }

        private static ExpeditionSite Site() => new ExpeditionSite("outskirts_test", "Ближні розвалини")
        {
            QuietDays = 2, ForcefulDays = 1,
            QuietSkill = SkillKeys.Survival, ForcefulSkill = SkillKeys.Melee,
            Threshold = 3, BaseMaterials = 4, BaseGold = 8
        };

        [Test]
        public void FullCycle_DepartTwoDaysReturn_YieldsNonEmptyResult()
        {
            var cfg = Cfg();
            var state = Build(cfg, out var party, out var ledger);
            var site = Site();

            var dispatch = ExpeditionRunner.Depart(state, party, site, ExpeditionApproach.Quiet,
                new[] { "scout_1" }, days: 2, ledger, cfg);

            Assert.AreEqual(DispatchResult.Success, dispatch);
            Assert.IsNull(state.GetSlot("storehouse_dock").AssignedCompanionId, "пост опустел — цена вылазки");
            Assert.IsNotNull(party.PendingResult, "результат заморожен сразу при отправке (R15)");

            Assert.IsFalse(party.TickDay(), "первые сутки в пути");
            Assert.IsTrue(party.TickDay(), "вторые сутки — партия дома");

            party.Return(state, out var result);
            Assert.IsNotNull(result);

            ExpeditionRunner.Complete(state, result);

            Assert.Greater(result.Materials + result.Gold, 0, "непорожній результат: хоч матеріали, хоч золото");
            Assert.Greater(state.Resources.Get(ResourceType.Materials) + state.Resources.Get(ResourceType.Gold), 0);
        }

        [Test]
        public void SaveMidExpedition_RestoreState_ReturnGivesTheSameResult()
        {
            var cfg = Cfg();
            var state = Build(cfg, out var party, out var ledger);
            var site = Site();

            ExpeditionRunner.Depart(state, party, site, ExpeditionApproach.Forceful,
                new[] { "scout_1" }, days: 2, ledger, cfg);

            // Сейв посеред відкритої вилазки — до повернення.
            string blob = party.CaptureState();

            var reloadedParty = new ExpeditionParty();
            reloadedParty.RestoreState(blob);

            party.TickDay(); party.TickDay();
            reloadedParty.TickDay(); reloadedParty.TickDay();

            party.Return(state, out var direct);

            var reloadedState = Build(cfg, out _, out _); // незалежна база з тим самим ростером
            reloadedParty.Return(reloadedState, out var afterReload);

            Assert.IsNotNull(direct);
            Assert.IsNotNull(afterReload);
            Assert.AreEqual(direct.Materials, afterReload.Materials, "результат не пересчитался заново");
            Assert.AreEqual(direct.Gold, afterReload.Gold);
            Assert.AreEqual(direct.Band, afterReload.Band);
            Assert.AreEqual(direct.Wounded.Count, afterReload.Wounded.Count);
        }

        /// <summary>
        /// Рев'ю B7 (блокер): попередній тест
        /// <see cref="SaveMidExpedition_RestoreState_ReturnGivesTheSameResult"/>
        /// кличе <c>party.CaptureState()</c>/<c>RestoreState()</c> напряму і
        /// тому не ловить розрив у СПРАВЖНЬОМУ шляху збереження гри:
        /// складовий зліпок (<c>Core/Loop/SettlementSave.cs</c>, веде
        /// виключно Foundation/A1) ділить ВЕСЬ зліпок за ';' одним
        /// проходом і ріже значення поля <c>party=</c> на першому ж
        /// зустрінутому ';' — старий формат ховав заморожений результат
        /// саме за цим символом. Цей тест іде через
        /// <see cref="DayProcessor.SaveState"/>/<see cref="DayProcessor.RestoreState"/>
        /// — рівно той шлях, яким зберігається справжня гра.
        /// </summary>
        [Test]
        public void SaveMidExpedition_ThroughDayProcessor_KeepsFrozenResult()
        {
            var cfg = Cfg();
            var state = Build(cfg, out var party, out var ledger);
            var site = Site();

            var adapter = new RosterAdapter(state.Roster);
            var processor = new DayProcessor(new TensionState(cfg.Tension), cfg, DayProcessor.DefaultSteps())
            {
                Roster = adapter,
                Casualties = adapter,
                Party = party,
                Sites = ledger
            };

            ExpeditionRunner.Depart(state, party, site, ExpeditionApproach.Forceful,
                new[] { "scout_1" }, days: 2, ledger, cfg);
            Assert.IsNotNull(party.PendingResult, "результат заморожен сразу при отправке (R15)");

            // Сейв ЧЕРЕЗ справжній шлях гри, не через party.CaptureState() напряму.
            string blob = processor.SaveState();

            var reloadedParty = new ExpeditionParty();
            var reloadedProcessor = new DayProcessor(new TensionState(cfg.Tension), cfg, DayProcessor.DefaultSteps())
            {
                Party = reloadedParty,
                Sites = new SiteLedger()
            };
            reloadedProcessor.RestoreState(blob);

            Assert.IsTrue(reloadedParty.IsAway, "партия всё ещё в поле после восстановления через DayProcessor");
            Assert.IsNotNull(reloadedParty.PendingResult,
                "замороженный результат обязан переживать сейв через DayProcessor.SaveState/RestoreState " +
                "(не только через ExpeditionParty напрямую) — иначе на возврате Complete получает null и " +
                "добыча вылазки бесшумно пропадает");
            Assert.AreEqual(party.PendingResult.Materials, reloadedParty.PendingResult.Materials);
            Assert.AreEqual(party.PendingResult.Gold, reloadedParty.PendingResult.Gold);
            Assert.AreEqual(party.PendingResult.Band, reloadedParty.PendingResult.Band);
            Assert.AreEqual(party.PendingResult.Wounded.Count, reloadedParty.PendingResult.Wounded.Count);
        }

        [Test]
        public void DelveApproach_SkipsResolve_LeavesResultUnfrozen()
        {
            var cfg = Cfg();
            var state = Build(cfg, out var party, out var ledger);
            var site = Site();

            var dispatch = ExpeditionRunner.Depart(state, party, site, ExpeditionApproach.Delve,
                new[] { "scout_1" }, days: 2, ledger, cfg);

            Assert.AreEqual(DispatchResult.Success, dispatch);
            Assert.IsNull(party.PendingResult, "Delve резолвится комнатами данжа, не здесь (R15/§4.11)");
        }

        [Test]
        public void UnknownAndDuplicateAndOversizedParty_AreRefusedBeforeAnyoneLeaves()
        {
            var cfg = Cfg();
            var state = Build(cfg, out var party, out var ledger);
            var site = Site();

            Assert.AreEqual(DispatchResult.UnknownCompanion,
                ExpeditionRunner.Depart(state, party, site, ExpeditionApproach.Quiet, new[] { "ghost" }, 2, ledger, cfg));

            Assert.AreEqual(DispatchResult.DuplicateCompanion,
                ExpeditionRunner.Depart(state, party, site, ExpeditionApproach.Quiet,
                    new[] { "scout_1", "scout_1" }, 2, ledger, cfg));

            Assert.IsFalse(party.IsAway, "отказ не должен был сдвинуть партию хоть немного");
        }

        [Test]
        public void InjuredCompanion_CannotBeDispatched()
        {
            var cfg = Cfg();
            var state = Build(cfg, out var party, out var ledger);
            state.Roster.Get("scout_1").InjuryPoints = 10;

            var dispatch = ExpeditionRunner.Depart(state, party, Site(), ExpeditionApproach.Quiet,
                new[] { "scout_1" }, 2, ledger, cfg);

            Assert.AreEqual(DispatchResult.CompanionUnavailable, dispatch);
        }
    }
}
