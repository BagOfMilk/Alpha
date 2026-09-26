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
    /// Пакет B5: нові укази ради (R5, AUDIT П8/G12/G20) поверх наявного
    /// CityWorks/CityWorksStep. Архівний Council/CouncilAction (окремий клас
    /// з Influence/ThreatSystem, Епік 10) на поточну модель не переноситься —
    /// R14 віддає нові дії прямо CityWorks, тим самим прийомом, яким уже
    /// зроблені Order/OrderRaid/OrderSettlers.
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

            // AUDIT G20: CouncilEdict зобов'язаний реально застосуватися наступним тіком.
            var report = c.Processor.Advance();
            var edict = report.TensionChanges.Where(x => x.Driver == TensionDriver.CouncilEdict).ToList();
            Assert.IsNotEmpty(edict, "CouncilEdict стоял в белом списке без единого вызова — теперь указ его вызывает");
            Assert.Less(edict[0].Applied, 0, "Указ снижает Напругу (понижающий драйвер)");

            // Ревью-фікс: указ застосувався ОДРАЗУ (Applied), але зобов'язаний і прозвучати —
            // інакше місто змінюється мовчки (docs/TEST_BUILD.md §2 стор. 15, §7.13).
            Assert.IsTrue(Heard(new[] { report }).Any(r => r.TopicId == "council.decree.ordered"),
                "Указ обязан объявить о себе тем же днём, когда заказан");

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

        /// <summary>
        /// Ревью-фікс: без цієї перевірки указ на незареєстровану фракцію
        /// списував золото, рухав Уклад і мовчки не чіпав жодної фракції —
        /// той, хто викликає, не міг відрізнити це від успіху (обидві гілки повертали
        /// Applied). CouncilOrderResult.UnknownFaction саме для цього і заведений.
        /// </summary>
        [Test]
        public void Decree_UnknownFavoredFaction_DoesNothing_ReturnsUnknownFaction()
        {
            var cfg = new BalanceConfig();
            var c = Build(cfg);
            Give(c.State, 100);

            int goldBefore = c.State.Resources.Get(ResourceType.Gold);
            int orderLevelBefore = c.Processor.OrderLevel;

            var result = c.Works.OrderDecree(c.State, c.Processor, c.Factions,
                "no_such_faction_favored", DefaultFactions.Community, 1, cfg);

            Assert.AreEqual(CouncilOrderResult.UnknownFaction, result);
            Assert.AreEqual(goldBefore, c.State.Resources.Get(ResourceType.Gold), "Золото не списано — эффекта не было");
            Assert.AreEqual(orderLevelBefore, c.Processor.OrderLevel, "Уклад не сдвинут — эффекта не было");
        }

        [Test]
        public void Decree_UnknownCostFaction_DoesNothing_ReturnsUnknownFaction()
        {
            var cfg = new BalanceConfig();
            var c = Build(cfg);
            Give(c.State, 100);

            int goldBefore = c.State.Resources.Get(ResourceType.Gold);

            var result = c.Works.OrderDecree(c.State, c.Processor, c.Factions,
                DefaultFactions.TuharBoyars, "no_such_faction_cost", 1, cfg);

            Assert.AreEqual(CouncilOrderResult.UnknownFaction, result);
            Assert.AreEqual(goldBefore, c.State.Resources.Get(ResourceType.Gold));
            Assert.AreEqual(50, c.Factions.Get(DefaultFactions.TuharBoyars).Value,
                "Незнакомая costFactionId обязана заблокировать весь заказ, а не только свою половину");
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

        /// <summary>
        /// Ревью-фікс (major): раніше Указ клав Напругу в DayProcessor.QueueExternal
        /// (_externalTension) — ця черга не входить у SettlementSave. Order* і
        /// SaveState обидва легальні у фазі Morning (docs/TEST_BUILD.md §4.1), отже
        /// "Указ -> SaveState -> перезавантаження -> Advance" — легальна послідовність,
        /// і раніше вона мовчки губила сплачений CouncilEdict, хоча золото/Уклад/фракції
        /// з того самого виклику вже зберігалися. Тепер Напруга Указа накопичується в самому
        /// CityWorks (входить у його CaptureState) і застосовується CityWorksStep напряму.
        /// </summary>
        [Test]
        public void Decree_TensionSurvivesSaveAndLoad_BeforeNextAdvance()
        {
            var cfg = new BalanceConfig();
            var c = Build(cfg);
            Give(c.State, 100);

            var result = c.Works.OrderDecree(c.State, c.Processor, c.Factions,
                DefaultFactions.TuharBoyars, DefaultFactions.Community, 1, cfg);
            Assert.AreEqual(CouncilOrderResult.Applied, result);

            string blob = c.Processor.SaveState();

            var fresh = Build(cfg);
            fresh.Processor.RestoreState(blob);

            var report = fresh.Processor.Advance();
            var edict = report.TensionChanges.Where(x => x.Driver == TensionDriver.CouncilEdict).ToList();
            Assert.IsNotEmpty(edict,
                "Напруга Указа обязана пережить Order -> SaveState -> перезагрузку, а не только немедленный Advance без сейва");
            Assert.Less(edict[0].Applied, 0, "Указ остаётся понижающим драйвером и после перезагрузки");
        }

        // ================= Дипломатія =================

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

            var reports = c.Processor.AdvanceFullDay();
            Assert.IsTrue(Heard(reports).Any(r => r.TopicId == "council.diplomacy.ordered"),
                "Дипломатия применилась сразу, но обязана прозвучать тем же днём");

            Assert.AreEqual(CouncilOrderResult.OnCooldown,
                c.Works.OrderDiplomacy(c.State, c.Factions, DefaultFactions.Horde, 1, cfg));
        }

        /// <summary>
        /// Перший реальний споживач FactionStandingBand (Робочий пакет 1,
        /// 25.09.2026): раніше щабель довіри ніде в грі не читався. Hostile
        /// не купується простою дипломатією — грошей теж не списує.
        /// </summary>
        [Test]
        public void Diplomacy_WithHostileFaction_IsRejected_NoGoldSpent()
        {
            var cfg = new BalanceConfig();
            var c = Build(cfg);
            Give(c.State, 100);
            c.Factions.ApplySocialConsequence(DefaultFactions.Horde, -999); // -> Hostile (поріг 20)
            Assert.AreEqual(FactionStandingBand.Hostile, c.Factions.BandOf(DefaultFactions.Horde));

            int goldBefore = c.State.Resources.Get(ResourceType.Gold);
            var result = c.Works.OrderDiplomacy(c.State, c.Factions, DefaultFactions.Horde, 1, cfg);

            Assert.AreEqual(CouncilOrderResult.StandingTooLow, result);
            Assert.AreEqual(goldBefore, c.State.Resources.Get(ResourceType.Gold));
        }

        // ================= Інвестиція =================

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

        // ================= Підготовка до загрози =================

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

            var reports = c.Processor.AdvanceFullDay();
            Assert.IsTrue(Heard(reports).Any(r => r.TopicId == "council.prepare_threat.ordered"),
                "Подготовка применилась сразу, но обязана прозвучать тем же днём");

            Assert.AreEqual(CouncilOrderResult.OnCooldown, c.Works.OrderPrepareThreat(c.State, 1, cfg));

            Assert.AreEqual(1, c.Works.TakeReadinessMilestones(),
                "Пакет сам не знает про ReadinessTrack (§1.1) — только копит маркер для D1/B6");
            Assert.AreEqual(0, c.Works.TakeReadinessMilestones(), "Забор обнуляет счётчик");
        }

        // ================= Спорядження експедиції =================

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

            var reports = c.Processor.AdvanceFullDay();
            Assert.IsTrue(Heard(reports).Any(r => r.TopicId == "council.outfit_expedition.ordered"),
                "Снаряжение применилось сразу, но обязано прозвучать тем же днём");

            var buff = c.Works.TakeExpeditionOutfitBuff();
            Assert.IsNotNull(buff);
            Assert.AreEqual("abandoned_camp", buff.SiteId);
            Assert.AreEqual(cfg.Faction.OutfitExpeditionBonusValue, buff.BonusValue);

            Assert.IsNull(c.Works.TakeExpeditionOutfitBuff(), "Второй забор — пусто: бонус разовый");
        }

        // ================= AUDIT G12: знижка зайнятого ринку =================

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

            // ---- будівництво ----
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

            // ---- указ ради ----
            int goldBeforeDecreeStaffed = staffed.State.Resources.Get(ResourceType.Gold);
            int goldBeforeDecreeBare = bare.State.Resources.Get(ResourceType.Gold);

            staffed.Works.OrderDecree(staffed.State, staffed.Processor, staffed.Factions,
                DefaultFactions.TuharBoyars, DefaultFactions.Community, 1, cfg);
            bare.Works.OrderDecree(bare.State, bare.Processor, bare.Factions,
                DefaultFactions.TuharBoyars, DefaultFactions.Community, 1, cfg);

            int decreeSpentStaffed = goldBeforeDecreeStaffed - staffed.State.Resources.Get(ResourceType.Gold);
            int decreeSpentBare = goldBeforeDecreeBare - bare.State.Resources.Get(ResourceType.Gold);

            Assert.Less(decreeSpentStaffed, decreeSpentBare, "Скидка обязана работать и для указов рады, не только стройки");

            // ---- дипломатія ----
            int goldBeforeDiplomacyStaffed = staffed.State.Resources.Get(ResourceType.Gold);
            int goldBeforeDiplomacyBare = bare.State.Resources.Get(ResourceType.Gold);

            staffed.Works.OrderDiplomacy(staffed.State, staffed.Factions, DefaultFactions.Horde, 1, cfg);
            bare.Works.OrderDiplomacy(bare.State, bare.Factions, DefaultFactions.Horde, 1, cfg);

            int diplomacySpentStaffed = goldBeforeDiplomacyStaffed - staffed.State.Resources.Get(ResourceType.Gold);
            int diplomacySpentBare = goldBeforeDiplomacyBare - bare.State.Resources.Get(ResourceType.Gold);

            Assert.Less(diplomacySpentStaffed, diplomacySpentBare, "Скидка обязана работать и для Дипломатии");

            // ---- підготовка до загрози ----
            int goldBeforePrepareStaffed = staffed.State.Resources.Get(ResourceType.Gold);
            int goldBeforePrepareBare = bare.State.Resources.Get(ResourceType.Gold);

            staffed.Works.OrderPrepareThreat(staffed.State, 1, cfg);
            bare.Works.OrderPrepareThreat(bare.State, 1, cfg);

            int prepareSpentStaffed = goldBeforePrepareStaffed - staffed.State.Resources.Get(ResourceType.Gold);
            int prepareSpentBare = goldBeforePrepareBare - bare.State.Resources.Get(ResourceType.Gold);

            Assert.Less(prepareSpentStaffed, prepareSpentBare, "Скидка обязана работать и для Подготовки к угрозе");

            // ---- спорядження експедиції ----
            int goldBeforeOutfitStaffed = staffed.State.Resources.Get(ResourceType.Gold);
            int goldBeforeOutfitBare = bare.State.Resources.Get(ResourceType.Gold);

            staffed.Works.OrderOutfitExpedition(staffed.State, "abandoned_camp", 1, cfg);
            bare.Works.OrderOutfitExpedition(bare.State, "abandoned_camp", 1, cfg);

            int outfitSpentStaffed = goldBeforeOutfitStaffed - staffed.State.Resources.Get(ResourceType.Gold);
            int outfitSpentBare = goldBeforeOutfitBare - bare.State.Resources.Get(ResourceType.Gold);

            Assert.Less(outfitSpentStaffed, outfitSpentBare, "Скидка обязана работать и для Спорядження експедиції");

            // ---- інвестиція (buildingId: null — минуємо перевірку «будівля вже
            //      збудована», тут перевіряємо тільки знижку) ----
            int goldBeforeInvestStaffed = staffed.State.Resources.Get(ResourceType.Gold);
            int goldBeforeInvestBare = bare.State.Resources.Get(ResourceType.Gold);

            staffed.Works.OrderInvestment(staffed.State, null, 1, cfg);
            bare.Works.OrderInvestment(bare.State, null, 1, cfg);

            int investSpentStaffed = goldBeforeInvestStaffed - staffed.State.Resources.Get(ResourceType.Gold);
            int investSpentBare = goldBeforeInvestBare - bare.State.Resources.Get(ResourceType.Gold);

            Assert.Less(investSpentStaffed, investSpentBare, "Скидка обязана работать и для Инвестиции");
        }

        // ================= регресія: старі замовлення ради не зламані =================

        /// <summary>
        /// docs/TEST_BUILD.md §5 (акцептанс B5) називає цей тест на ім'я:
        /// облава — силовий метод, і тепер, коли реєстр фракцій існує,
        /// вона зобов'язана рухати не тільки Напругу, а й стосунки (бояри Тугара
        /// задоволені порядком, громаді не подобається нагайка на своїх).
        /// </summary>
        [Test]
        public void Raid_LowersTension_PaysCosts_ShiftsFactions()
        {
            var cfg = new BalanceConfig();
            var c = Build(cfg);
            Give(c.State, 200);

            int goldBefore = c.State.Resources.Get(ResourceType.Gold);

            var result = c.Works.OrderRaid(c.State, 1, cfg, c.Factions);
            Assert.AreEqual(CouncilOrderResult.Queued, result);
            Assert.AreEqual(goldBefore - cfg.City.RaidGoldCost, c.State.Resources.Get(ResourceType.Gold),
                "Облава платится сразу");

            Assert.AreEqual(50 + cfg.Faction.RaidFactionFavoredDelta, c.Factions.Get(DefaultFactions.TuharBoyars).Value,
                "Силовой метод доволен боярам");
            Assert.AreEqual(50 - cfg.Faction.RaidFactionCostDelta, c.Factions.Get(DefaultFactions.Community).Value,
                "Громаде нагайка на своих не нравится");

            var report = c.Processor.Advance();
            var raidTension = report.TensionChanges.Where(x => x.Driver == TensionDriver.CouncilRaid).ToList();
            Assert.IsNotEmpty(raidTension, "Облава обязана снизить Напругу — она и заводилась для этого");
            Assert.Less(raidTension[0].Applied, 0, "Облава — понижающий драйвер");

            Assert.IsTrue(Heard(new[] { report }).Any(r => r.TopicId == "council.raid"),
                "Облава обязана прозвучать тем же приёмом, что и раньше (B5 её не трогает)");
        }

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
