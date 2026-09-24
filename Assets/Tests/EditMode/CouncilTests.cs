using System.Collections.Generic;
using System.Linq;
using Game.Core;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Economy;
using Game.Core.Factions;
using Game.Core.Loop;
using Game.Core.Pressure;
using Game.Core.Settlement;
using Game.Core.Signals;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Пакет B5: новые указы рады (R5, AUDIT П8/G12/G20) поверх существующего
    /// CityWorks/CityWorksStep. Архивный Council/CouncilAction (отдельный класс
    /// с Influence/ThreatSystem, Эпик 10) на текущую модель не переносится —
    /// R14 отдаёт новые действия прямо CityWorks, тем же приёмом, каким уже
    /// сделаны Order/OrderRaid/OrderSettlers.
    /// </summary>
    public class CouncilTests
    {
        private sealed class City
        {
            public DayProcessor Processor;
            public CityWorks Works;
            public BaseState State;
            public FactionRegistry Factions;
        }

        private static City Build(BalanceConfig cfg, IEnumerable<string> built = null)
        {
            var roster = new Roster();
            var state = new BaseState(roster, new ResourceLedger(), cfg);
            foreach (var slot in DefaultContent.AllSlots()) state.AddSlot(slot);

            var works = new CityWorks(built ?? DefaultBuildings.StartingSet);
            works.ApplyToSlots(state);

            var steps = new List<IDayStep>(DayProcessor.DefaultSteps()) { new CityWorksStep(works, state) };

            var processor = new DayProcessor(new TensionState(cfg.Tension, 300), cfg, steps)
            {
                Tier = 1,
                CityState = works,
                Population = new PopulationState(80)
            };

            return new City { Processor = processor, Works = works, State = state, Factions = DefaultFactions.NewRegistry(cfg.Faction) };
        }

        private static void Give(BaseState state, int gold, int materials = 0, int food = 0)
        {
            if (gold > 0) state.Resources.Add(ResourceType.Gold, gold);
            if (materials > 0) state.Resources.Add(ResourceType.Materials, materials);
            if (food > 0) state.Resources.Add(ResourceType.Food, food);
        }

        private static Companion Hire(BaseState state, string id, SkillType skill, int value)
        {
            var companion = new CompanionArchetype(id, id).SetSkill(skill, value).CreateInstance(id);
            state.Roster.Add(companion);
            return companion;
        }

        private static List<SignalRequest> Heard(IEnumerable<DayReport> reports)
        {
            var all = new List<SignalRequest>();
            foreach (var r in reports)
                if (r.Signals != null) all.AddRange(r.Signals.Requests);
            return all;
        }

        // ================= Указ =================

        [Test]
        public void Decree_TradesOneFactionForAnother()
        {
            var cfg = new BalanceConfig();
            var c = Build(cfg);
            Give(c.State, 100);

            int levelBefore = c.Processor.OrderLevel;
            int goldBefore = c.State.Resources.Get(ResourceType.Gold);

            var result = c.Works.OrderDecree(c.State, c.Processor, c.Factions,
                DefaultFactions.TuharBoyars, DefaultFactions.Community, 1, cfg);

            Assert.AreEqual(CouncilOrderResult.Applied, result);
            Assert.AreEqual(levelBefore + cfg.Faction.DecreeOrderLevelStep, c.Processor.OrderLevel,
                "Указ обязан двигать Уклад (AUDIT П8)");
            Assert.AreEqual(goldBefore - cfg.Faction.DecreeGoldCost, c.State.Resources.Get(ResourceType.Gold));

            Assert.AreEqual(50 + cfg.Faction.DecreeFactionDelta, c.Factions.Get(DefaultFactions.TuharBoyars).Value,
                "Выгодная фракция получает прирост");
            Assert.AreEqual(50 - cfg.Faction.DecreeFactionDelta, c.Factions.Get(DefaultFactions.Community).Value,
                "Фракция, которой указ стоит, теряет ровно столько же — это и есть 'trade'");

            // AUDIT G20: CouncilEdict обязан реально примениться следующим тиком.
            var report = c.Processor.Advance();
            var edict = report.TensionChanges.Where(x => x.Driver == TensionDriver.CouncilEdict).ToList();
            Assert.IsNotEmpty(edict, "CouncilEdict стоял в белом списке без единого вызова — теперь указ его вызывает");
            Assert.Less(edict[0].Applied, 0, "Указ снижает Напругу (понижающий драйвер)");

            Assert.AreEqual(CouncilOrderResult.OnCooldown,
                c.Works.OrderDecree(c.State, c.Processor, c.Factions,
                    DefaultFactions.TuharBoyars, DefaultFactions.Community, 1, cfg),
                "Указ с откатом — иначе Уклад двигался бы бесплатно каждый ход");
        }

        [Test]
        public void Decree_NeedsCouncilHall()
        {
            var cfg = new BalanceConfig();
            var c = Build(cfg, built: new string[0]);
            Give(c.State, 100);

            Assert.AreEqual(CouncilOrderResult.NoCouncilHall,
                c.Works.OrderDecree(c.State, c.Processor, c.Factions,
                    DefaultFactions.TuharBoyars, DefaultFactions.Community, 1, cfg));
        }

        [Test]
        public void Decree_OrderLevel_NeverLeavesValidRange()
        {
            var cfg = new BalanceConfig();
            var c = Build(cfg);
            Give(c.State, 1000);

            int day = 1;
            for (int i = 0; i < 10; i++)
            {
                var result = c.Works.OrderDecree(c.State, c.Processor, c.Factions,
                    DefaultFactions.TuharBoyars, DefaultFactions.Community, day, cfg);
                Assert.AreEqual(CouncilOrderResult.Applied, result, "Золота и Зала совета хватает на каждую попытку");
                day += cfg.Faction.DecreeCooldownDays;
                Assert.GreaterOrEqual(c.Processor.OrderLevel, 1);
                Assert.LessOrEqual(c.Processor.OrderLevel, 4);
            }

            Assert.AreEqual(4, c.Processor.OrderLevel, "Десять указов подряд обязаны упереться в потолок, а не переполнить его");
        }

        // ================= Дипломатия =================

        [Test]
        public void Diplomacy_RaisesTargetFactionStanding_CostsGold_HasCooldown()
        {
            var cfg = new BalanceConfig();
            var c = Build(cfg);
            Give(c.State, 100);

            Assert.AreEqual(CouncilOrderResult.UnknownFaction,
                c.Works.OrderDiplomacy(c.State, c.Factions, "no_such_faction", 1, cfg));

            int goldBefore = c.State.Resources.Get(ResourceType.Gold);
            var result = c.Works.OrderDiplomacy(c.State, c.Factions, DefaultFactions.Horde, 1, cfg);

            Assert.AreEqual(CouncilOrderResult.Applied, result);
            Assert.AreEqual(50 + cfg.Faction.DiplomacyFactionDelta, c.Factions.Get(DefaultFactions.Horde).Value);
            Assert.AreEqual(goldBefore - cfg.Faction.DiplomacyGoldCost, c.State.Resources.Get(ResourceType.Gold));

            Assert.AreEqual(CouncilOrderResult.OnCooldown,
                c.Works.OrderDiplomacy(c.State, c.Factions, DefaultFactions.Horde, 1, cfg));
        }

        // ================= Инвестиция =================

        [Test]
        public void Investment_PaysGoldOverTime_ThenStops()
        {
            var cfg = new BalanceConfig();
            var c = Build(cfg);
            Give(c.State, 200);

            Assert.AreEqual(CouncilOrderResult.BuildingNotBuilt,
                c.Works.OrderInvestment(c.State, DefaultBuildings.Market, 1, cfg),
                "Рынок не построен — инвестировать в него нельзя");

            int goldAfterOrder = c.State.Resources.Get(ResourceType.Gold) - cfg.Faction.InvestmentGoldCost;
            Assert.AreEqual(CouncilOrderResult.Queued,
                c.Works.OrderInvestment(c.State, DefaultBuildings.Storehouse, 1, cfg),
                "Склад уже в стартовом наборе — инвестировать можно");
            Assert.AreEqual(goldAfterOrder, c.State.Resources.Get(ResourceType.Gold), "Цена списывается сразу");

            Assert.AreEqual(CouncilOrderResult.AlreadyQueued,
                c.Works.OrderInvestment(c.State, DefaultBuildings.Storehouse, 1, cfg),
                "Вторая Инвестиция поверх активной не встаёт в очередь");

            var reports = new List<DayReport>();
            for (int d = 0; d < cfg.Faction.InvestmentDays; d++)
                reports.AddRange(c.Processor.AdvanceFullDay());

            int totalPaid = c.State.Resources.Get(ResourceType.Gold) - goldAfterOrder;
            Assert.AreEqual(cfg.Faction.InvestmentGoldPerDay * cfg.Faction.InvestmentDays, totalPaid,
                "За весь срок Инвестиция обязана окупиться ровно на заявленную сумму");
            Assert.IsTrue(Heard(reports).Any(r => r.TopicId == "council.invest.payout"),
                "Выплата обязана прозвучать — город не меняется молча");

            int paidSoFar = c.State.Resources.Get(ResourceType.Gold);
            c.Processor.AdvanceFullDay();
            Assert.AreEqual(paidSoFar, c.State.Resources.Get(ResourceType.Gold),
                "После истечения срока Инвестиция обязана остановиться, а не платить вечно");
        }

        // ================= Подготовка к угрозе =================

        [Test]
        public void PrepareThreat_QueuesReadinessMilestone_CostsGold_HasCooldown()
        {
            var cfg = new BalanceConfig();
            var c = Build(cfg);
            Give(c.State, 100);

            Assert.AreEqual(0, c.Works.TakeReadinessMilestones(), "Пусто, пока не заказали");

            int goldBefore = c.State.Resources.Get(ResourceType.Gold);
            var result = c.Works.OrderPrepareThreat(c.State, 1, cfg);

            Assert.AreEqual(CouncilOrderResult.Applied, result);
            Assert.AreEqual(goldBefore - cfg.Faction.PrepareThreatGoldCost, c.State.Resources.Get(ResourceType.Gold));

            Assert.AreEqual(CouncilOrderResult.OnCooldown, c.Works.OrderPrepareThreat(c.State, 1, cfg));

            Assert.AreEqual(1, c.Works.TakeReadinessMilestones(),
                "Пакет сам не знает про ReadinessTrack (§1.1) — только копит маркер для D1/B6");
            Assert.AreEqual(0, c.Works.TakeReadinessMilestones(), "Забор обнуляет счётчик");
        }

        // ================= Снаряжение экспедиции =================

        [Test]
        public void OutfitExpedition_StoresOneShotBonus_ConsumedOnce()
        {
            var cfg = new BalanceConfig();
            var c = Build(cfg);
            Give(c.State, 100);

            Assert.IsNull(c.Works.TakeExpeditionOutfitBuff(), "Пусто, пока не заказали");

            var result = c.Works.OrderOutfitExpedition(c.State, "abandoned_camp", 1, cfg);
            Assert.AreEqual(CouncilOrderResult.Applied, result);

            Assert.AreEqual(CouncilOrderResult.AlreadyQueued,
                c.Works.OrderOutfitExpedition(c.State, "abandoned_camp", 1, cfg),
                "Разовый бонус не копится второй раз, пока первый не забрали");

            var buff = c.Works.TakeExpeditionOutfitBuff();
            Assert.IsNotNull(buff);
            Assert.AreEqual("abandoned_camp", buff.SiteId);
            Assert.AreEqual(cfg.Faction.OutfitExpeditionBonusValue, buff.BonusValue);

            Assert.IsNull(c.Works.TakeExpeditionOutfitBuff(), "Второй забор — пусто: бонус разовый");
        }

        // ================= AUDIT G12: скидка занятого рынка =================

        [Test]
        public void PriceMultiplier_Is1_WhenMarketNotStaffed()
        {
            var cfg = new BalanceConfig();
            var c = Build(cfg, built: new[] { DefaultBuildings.CouncilHall, DefaultBuildings.Market });

            Assert.AreEqual(1.0, CityWorks.TradeDiscount(c.State, cfg, 1),
                "Рынок построен, но пуст — скидки нет");
        }

        [Test]
        public void PriceMultiplier_DiscountsCouncilAndBuildingOrders_WhenMarketStaffed()
        {
            var cfg = new BalanceConfig();
            var c = Build(cfg, built: new[] { DefaultBuildings.CouncilHall, DefaultBuildings.Market });
            Hire(c.State, "trader", SkillType.Trade, 10);
            Assert.AreEqual(AssignmentResult.Success, c.State.TryAssign("trader", CityWorks.MarketSlotId));

            double discount = CityWorks.TradeDiscount(c.State, cfg, 1);
            Assert.Less(discount, 1.0, "Профильный торговец на рынке обязан дать хоть какую-то скидку");
            Assert.GreaterOrEqual(discount, 1.0 - cfg.Checks.TradeBandDiscount * 3 - 1e-9,
                "Скидка не может быть больше, чем даёт лучшая полоса");

            // ---- стройка ----
            var staffed = Build(cfg, built: new[] { DefaultBuildings.CouncilHall, DefaultBuildings.Market });
            Hire(staffed.State, "trader", SkillType.Trade, 10);
            staffed.State.TryAssign("trader", CityWorks.MarketSlotId);
            Give(staffed.State, 1000, 100);

            var bare = Build(cfg, built: new[] { DefaultBuildings.CouncilHall, DefaultBuildings.Market });
            Give(bare.State, 1000, 100);

            int goldBeforeStaffed = staffed.State.Resources.Get(ResourceType.Gold);
            int goldBeforeBare = bare.State.Resources.Get(ResourceType.Gold);

            var infirmary = DefaultBuildings.Get(DefaultBuildings.Infirmary);
            staffed.Works.Order(DefaultBuildings.Infirmary, staffed.State, 1, cfg);
            bare.Works.Order(DefaultBuildings.Infirmary, bare.State, 1, cfg);

            int spentStaffed = goldBeforeStaffed - staffed.State.Resources.Get(ResourceType.Gold);
            int spentBare = goldBeforeBare - bare.State.Resources.Get(ResourceType.Gold);

            Assert.Less(spentStaffed, spentBare, "Занятый рынок обязан скидывать цену стройки (AUDIT G12)");
            Assert.AreEqual(infirmary.GoldCost, spentBare, "Без рынка — прежняя, недисконтированная цена");

            // ---- указ рады ----
            int goldBeforeDecreeStaffed = staffed.State.Resources.Get(ResourceType.Gold);
            int goldBeforeDecreeBare = bare.State.Resources.Get(ResourceType.Gold);

            staffed.Works.OrderDecree(staffed.State, staffed.Processor, staffed.Factions,
                DefaultFactions.TuharBoyars, DefaultFactions.Community, 1, cfg);
            bare.Works.OrderDecree(bare.State, bare.Processor, bare.Factions,
                DefaultFactions.TuharBoyars, DefaultFactions.Community, 1, cfg);

            int decreeSpentStaffed = goldBeforeDecreeStaffed - staffed.State.Resources.Get(ResourceType.Gold);
            int decreeSpentBare = goldBeforeDecreeBare - bare.State.Resources.Get(ResourceType.Gold);

            Assert.Less(decreeSpentStaffed, decreeSpentBare, "Скидка обязана работать и для указов рады, не только стройки");
        }

        // ================= регрессия: старые заказы совета не сломаны =================

        [Test]
        public void Raid_And_Settlers_StillWork_AfterB5()
        {
            var cfg = new BalanceConfig();
            var c = Build(cfg);
            Give(c.State, 200, 0, 100);

            Assert.AreEqual(CouncilOrderResult.Queued, c.Works.OrderRaid(c.State, 1, cfg));
            Assert.AreEqual(CouncilOrderResult.Queued, c.Works.OrderSettlers(c.State, 1, cfg));

            var reports = c.Processor.AdvanceFullDay();
            Assert.IsTrue(Heard(reports).Any(r => r.TopicId == "council.raid"));
            Assert.IsTrue(Heard(reports).Any(r => r.TopicId == "city.people.arrived"));
        }
    }
}
