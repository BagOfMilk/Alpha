using System.Collections.Generic;
using System.Linq;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Checks;
using Game.Core.Dungeons;
using Game.Core.Economy;
using Game.Core.Expeditions;
using Game.Core.Session;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Поправка №12.5 (рішення власника 29.09.2026, «Давай 2»): замість одного
    /// спільного Materials — будівельний і крафтовий компоненти, як у GDD
    /// (Е6.2). Охоронці: у кожного компонента свій кран і свій споживач
    /// (Статут MECH-03), джерела різні (куди йти — рішення гравця), старі
    /// зліпки зі спільними матеріалами відновлюються.
    /// </summary>
    public class TwoComponentsTests
    {
        private static BalanceConfig Cfg() => new BalanceConfig { FoodUpkeepPerCompanion = 0 };

        private static List<ISettlementActor> Specialist(BalanceConfig cfg)
        {
            var arch = new CompanionArchetype("spec", "Фахівець");
            arch.SetSkill(SkillType.Survival, 8);
            arch.SetSkill(SkillType.Mechanics, 8);
            arch.SetSkill(SkillType.Trade, 8);
            return new List<ISettlementActor> { new CompanionActorAdapter(arch.CreateInstance("spec_1", cfg), false, cfg) };
        }

        // ---- джерела: різні точки дають різне ----

        [Test]
        public void Sites_DifferInWhatTheyYield_SoTheRouteIsADecision()
        {
            var cfg = Cfg();
            var party = Specialist(cfg);
            bool buildLeaning = false, craftLeaning = false;
            foreach (var site in DefaultSites.All())
            {
                var preview = ExpeditionResolver.Preview(site, ExpeditionApproach.Quiet, party, new SiteLedger(), cfg);
                if (preview.BuildComponent > preview.CraftComponent) buildLeaning = true;
                if (preview.CraftComponent > preview.BuildComponent) craftLeaning = true;
            }
            Assert.IsTrue(buildLeaning, "хоч одна точка мусить давати переважно будівельний компонент");
            Assert.IsTrue(craftLeaning, "хоч одна точка мусить давати переважно крафтовий компонент");
        }

        [Test]
        public void Expedition_BanksBothComponents_IntoTheirOwnWallets()
        {
            var cfg = Cfg();
            var state = new BaseState(new Roster(), new ResourceLedger(), cfg);
            var result = ExpeditionResolver.Resolve(DefaultSites.Workshop(), ExpeditionApproach.Quiet,
                Specialist(cfg), new SiteLedger(), cfg);
            Assert.Greater(result.CraftComponent, 0, "майстерня-руїна — кран крафтового компонента");

            ExpeditionRunner.Complete(state, result);

            Assert.AreEqual(result.BuildComponent, state.Resources.Get(ResourceType.BuildComponent));
            Assert.AreEqual(result.CraftComponent, state.Resources.Get(ResourceType.CraftComponent));
        }

        [Test]
        public void Dungeons_AreACraftFaucet()
        {
            int craft = 0;
            foreach (var siteId in DefaultDungeon.KnownSiteIds)
                foreach (var room in DefaultDungeon.Rooms(siteId))
                {
                    craft += room.GuaranteedCraftComponent;
                    foreach (var opt in room.EventOptions) craft += opt.CraftComponentGain;
                }
            Assert.Greater(craft, 0, "данжі — кран крафтового компонента (Поправка №12.5)");
        }

        // ---- споживачі: будівлі — будівельний, крафт — крафтовий ----

        [Test]
        public void SpecialBuildings_PayWithBuildComponent_CoreBuildingsWithGoldOnly()
        {
            foreach (var def in DefaultBuildings.All())
            {
                bool core = def.Id == DefaultBuildings.Infirmary || def.Id == DefaultBuildings.Workshop ||
                            def.Id == DefaultBuildings.Storehouse || def.Id == DefaultBuildings.CouncilHall ||
                            def.Id == DefaultBuildings.Watch;
                if (core) Assert.AreEqual(0, def.BuildComponentCost, def.Id + ": ядро — лише золото (GDD US-7.2)");
                else Assert.Greater(def.BuildComponentCost, 0, def.Id + ": спеціальна — золото + будівельний компонент");
            }
        }

        [Test]
        public void Building_IgnoresCraftComponent_NeedsBuildComponent()
        {
            var state = new BaseState(new Roster(), new ResourceLedger(), Cfg());
            state.Resources.Add(ResourceType.Gold, 1000);
            state.Resources.Add(ResourceType.CraftComponent, 1000);
            var works = new CityWorks();

            Assert.AreEqual(BuildOrderResult.NotEnoughBuildComponent, works.Order(DefaultBuildings.Temple, state),
                "крафтовий компонент не будує храму");
            Assert.AreEqual(1000, state.Resources.Get(ResourceType.CraftComponent), "невдалий наказ нічого не списує");

            var temple = DefaultBuildings.Get(DefaultBuildings.Temple);
            state.Resources.Add(ResourceType.BuildComponent, temple.BuildComponentCost);
            Assert.AreEqual(BuildOrderResult.Started, works.Order(DefaultBuildings.Temple, state));
            Assert.AreEqual(0, state.Resources.Get(ResourceType.BuildComponent));
            Assert.AreEqual(1000, state.Resources.Get(ResourceType.CraftComponent), "будівництво крафтовий не чіпає");
        }

        // ---- зліпки: нові несуть обидва, старі відновлюються ----

        [Test]
        public void BaseState_Snapshot_CarriesBothComponents()
        {
            var cfg = Cfg();
            var a = new BaseState(new Roster(), new ResourceLedger(), cfg);
            a.Resources.Add(ResourceType.BuildComponent, 7);
            a.Resources.Add(ResourceType.CraftComponent, 4);

            var b = new BaseState(new Roster(), new ResourceLedger(), cfg);
            b.RestoreState(a.CaptureState());

            Assert.AreEqual(7, b.Resources.Get(ResourceType.BuildComponent));
            Assert.AreEqual(4, b.Resources.Get(ResourceType.CraftComponent));
        }

        [Test]
        public void BaseState_OldSnapshot_SharedMaterialsBecomeBuildComponent_CraftIsZero()
        {
            var b = new BaseState(new Roster(), new ResourceLedger(), Cfg());
            // Свіжий світ міг мати стартовий крафтовий — старий зліпок його не знав.
            b.Resources.Add(ResourceType.CraftComponent, 3);

            b.RestoreState("g:12|m:9|f:5|u:|x:|h:0"); // формат до Поправки №12.5: без «c:»

            Assert.AreEqual(12, b.Resources.Get(ResourceType.Gold));
            Assert.AreEqual(9, b.Resources.Get(ResourceType.BuildComponent), "спільні матеріали → будівельний");
            Assert.AreEqual(0, b.Resources.Get(ResourceType.CraftComponent), "крафтового в старому зліпку не було");
            Assert.AreEqual(5, b.Resources.Get(ResourceType.Food));
        }

        [Test]
        public void ExpeditionResult_Blob_RoundTripsCraft_AndReadsOldBlob()
        {
            var r = new ExpeditionResult
            {
                SiteId = "old_workshop", Approach = ExpeditionApproach.Quiet, Band = OutcomeBand.Good,
                Days = 6, BuildComponent = 1, CraftComponent = 5, Gold = 9, People = 2
            };
            r.PartyIds.Add("maksym");
            var back = ExpeditionResult.FromBlob(r.ToBlob());
            Assert.AreEqual(1, back.BuildComponent);
            Assert.AreEqual(5, back.CraftComponent);
            Assert.AreEqual(9, back.Gold);
            CollectionAssert.AreEqual(r.PartyIds, back.PartyIds);

            // Зліпок до Поправки №12.5: дев'ять полів, п'яте — спільні матеріали.
            var old = ExpeditionResult.FromBlob("outskirts|0|2|4|6|8|0||maksym");
            Assert.AreEqual(6, old.BuildComponent);
            Assert.AreEqual(0, old.CraftComponent);
            Assert.AreEqual(8, old.Gold);
            CollectionAssert.AreEqual(new[] { "maksym" }, old.PartyIds);
        }

        [Test]
        public void DungeonRun_OldSnapshot_WithoutCraft_RestoresBuildOnly()
        {
            var run = DefaultDungeon.Start(DefaultDungeon.AbandonedCamp, new[] { "protagonist" }, new BalanceConfig());
            run.RestoreState("o:0|r:0|c:0|a:0|t:2|rc:0|um:4|ug:1|it:|lb:1|eb:0");
            Assert.AreEqual(4, run.UnbankedBuildComponent);
            Assert.AreEqual(0, run.UnbankedCraftComponent);
        }

        [Test]
        public void GameSession_FreshRestore_KeepsBothComponents()
        {
            var s = new GameSession();
            s.NewGame(new NewGameOptions { SkipCreation = true });
            while (s.State == SessionState.Scene)
            {
                var step = s.AdvanceScene();
                if (step.IsChoice) s.ChooseSceneOption(0);
            }
            var before = s.GetEconomyView();
            Assert.Greater(before.BuildComponent, 0, "стартовий будівельний компонент (плейсхолдер)");
            Assert.Greater(before.CraftComponent, 0, "стартовий крафтовий компонент (плейсхолдер)");
            string blob = s.SaveState(0);

            var fresh = new GameSession();
            fresh.NewGame(new NewGameOptions { SkipCreation = true });
            fresh.RestoreFromBlob(blob);
            var after = fresh.GetEconomyView();

            Assert.AreEqual(before.BuildComponent, after.BuildComponent);
            Assert.AreEqual(before.CraftComponent, after.CraftComponent);
        }

        [Test]
        public void ResourceType_BuildComponent_KeepsOldMaterialsValue()
        {
            // Число 2 — старий Materials: старі нотатки/сейви з числовим ресурсом
            // мусять означати будівельний, а не щось інше.
            Assert.AreEqual(2, (int)ResourceType.BuildComponent);
            Assert.AreNotEqual((int)ResourceType.BuildComponent, (int)ResourceType.CraftComponent);
            Assert.IsFalse(new[] { 3, 4 }.Contains((int)ResourceType.CraftComponent),
                "3 і 4 звільнені від Intel/Research і не перевикористовуються");
        }
    }
}
