using System.Collections.Generic;
using System.Linq;
using Game.Core;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Checks;
using Game.Core.Economy;
using Game.Core.Expeditions;
using Game.Core.Loop;
using Game.Core.Pressure;
using Game.Core.Settlement;
using Game.Core.Signals;
using Game.Core.Stats;
using Game.Core.World;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Город отвечает (Поправка №6).
    ///
    /// Замер харнеса до этой работы: шестнадцать кампаний из шестнадцати
    /// кончались Расколом, Напряжение поднималось на 680–1010 и опускалось на 8.
    /// Процветание было привязано к тиру, который никто не менял; население не
    /// росло; четыре понижающих драйвера из пяти не вызывались нигде. Здесь
    /// закреплено, что у города появилось чем ответить — и что каждый ответ
    /// стоит цены, как решил владелец: «усі мають шось коштувати».
    /// </summary>
    public class CityWorksTests
    {
        // ================= фикстура =================

        private sealed class City
        {
            public DayProcessor Processor;
            public CityWorks Works;
            public BaseState State;
        }

        private static City Build(BalanceConfig cfg, int population = 80, int tension = 300,
            IEnumerable<string> built = null)
        {
            var roster = new Roster();
            var state = new BaseState(roster, new ResourceLedger(), cfg);
            foreach (var slot in DefaultContent.AllSlots()) state.AddSlot(slot);

            var works = new CityWorks(built ?? DefaultBuildings.StartingSet);
            works.ApplyToSlots(state);

            var steps = new List<IDayStep>(DayProcessor.DefaultSteps())
            {
                new CityWorksStep(works, state),
                new PopulationStep(works)
            };

            var p = new DayProcessor(new TensionState(cfg.Tension, tension), cfg, steps)
            {
                Tier = 1,
                Population = new PopulationState(population),
                CityState = works
            };

            return new City { Processor = p, Works = works, State = state };
        }

        private static List<SignalRequest> Heard(IEnumerable<DayReport> reports)
        {
            var all = new List<SignalRequest>();
            foreach (var r in reports)
                if (r.Signals != null) all.AddRange(r.Signals.Requests);
            return all;
        }

        private static List<TensionChange> Ledger(IEnumerable<DayReport> reports)
        {
            var all = new List<TensionChange>();
            foreach (var r in reports) all.AddRange(r.TensionChanges);
            return all;
        }

        private static void Give(BaseState state, int gold, int materials = 0, int food = 0)
        {
            if (gold > 0) state.Resources.Add(ResourceType.Gold, gold);
            if (materials > 0) state.Resources.Add(ResourceType.Materials, materials);
            if (food > 0) state.Resources.Add(ResourceType.Food, food);
        }

        // ================= стройка =================

        [Test]
        public void Build_PaysUpFront_AndNeverChargesHalf()
        {
            var cfg = new BalanceConfig();
            var c = Build(cfg);
            var temple = DefaultBuildings.Get(DefaultBuildings.Temple);

            Give(c.State, temple.GoldCost - 1);
            Assert.AreEqual(BuildOrderResult.NotEnoughGold, c.Works.Order(DefaultBuildings.Temple, c.State));

            Give(c.State, 100, temple.MaterialsCost - 1);
            int goldBefore = c.State.Resources.Get(ResourceType.Gold);
            Assert.AreEqual(BuildOrderResult.NotEnoughMaterials, c.Works.Order(DefaultBuildings.Temple, c.State));
            Assert.AreEqual(goldBefore, c.State.Resources.Get(ResourceType.Gold),
                "Нехватка материалов не имеет права съесть золото: либо стройка, либо ничего");

            Give(c.State, 0, 10);
            int matBefore = c.State.Resources.Get(ResourceType.Materials);
            Assert.AreEqual(BuildOrderResult.Started, c.Works.Order(DefaultBuildings.Temple, c.State));
            Assert.AreEqual(goldBefore - temple.GoldCost, c.State.Resources.Get(ResourceType.Gold));
            Assert.AreEqual(matBefore - temple.MaterialsCost, c.State.Resources.Get(ResourceType.Materials));

            Assert.AreEqual(BuildOrderResult.AlreadyInProgress, c.Works.Order(DefaultBuildings.Temple, c.State),
                "Дважды одно здание не заложить");
        }

        [Test]
        public void Build_EveryBuildingCostsSomething()
        {
            // Решение владельца: «усі мають шось коштувати». Бесплатное здание —
            // это не решение игрока, а подарок, и оно ломает цену всех остальных.
            foreach (var b in DefaultBuildings.All())
                Assert.Greater(b.GoldCost + b.MaterialsCost, 0, "Бесплатное здание: " + b.Id);
        }

        [Test]
        public void Build_AdvancedBuildings_NeedMaterialsFromOutside()
        {
            // Продвинутые здания (GDD US-7.1) стоят строительного компонента,
            // который город не производит. Это и замыкает петлю «вылазка → стройка».
            foreach (var id in new[] { DefaultBuildings.Market, DefaultBuildings.Tavern,
                                       DefaultBuildings.Temple, DefaultBuildings.Fortifications })
                Assert.Greater(DefaultBuildings.Get(id).MaterialsCost, 0, id + " обязан стоить материалов");
        }

        [Test]
        public void Build_Laboratory_ComesOnlyByQuest()
        {
            var cfg = new BalanceConfig();
            var c = Build(cfg);
            Give(c.State, 1000, 100);

            Assert.AreEqual(BuildOrderResult.QuestOnly, c.Works.Order(DefaultBuildings.Laboratory, c.State),
                "Лаборатория приходит по надёжному квесту (GDD), а не покупкой");
        }

        [Test]
        public void Build_TakesItsDays_ThroughVisibleStages_AndOpensItsPost()
        {
            var cfg = new BalanceConfig();
            var c = Build(cfg);
            var infirmary = DefaultBuildings.Get(DefaultBuildings.Infirmary);

            Assert.IsFalse(c.State.GetSlot("infirmary_bed").Unlocked,
                "Пока лазарета нет, на койку никого не поставить — это и есть цена пустого поста");

            Give(c.State, infirmary.GoldCost);
            Assert.AreEqual(BuildOrderResult.Started, c.Works.Order(DefaultBuildings.Infirmary, c.State));

            var stages = new List<int> { c.Works.StageOf(DefaultBuildings.Infirmary) };
            var reports = new List<DayReport>();
            for (int d = 0; d < infirmary.Days; d++)
            {
                reports.AddRange(c.Processor.AdvanceFullDay());
                stages.Add(c.Works.StageOf(DefaultBuildings.Infirmary));
            }

            for (int i = 1; i < stages.Count; i++)
                Assert.GreaterOrEqual(stages[i], stages[i - 1], "Стадии стройки не откатываются");
            Assert.AreEqual(5, stages.Last(), "Через свой срок здание готово");
            Assert.IsTrue(c.Works.Has(DefaultBuildings.Infirmary));
            Assert.IsTrue(c.State.GetSlot("infirmary_bed").Unlocked, "Готовый лазарет открывает свой пост");
            Assert.IsTrue(Heard(reports).Any(r => r.TopicId == "city.built.infirmary"),
                "Готовое здание обязано прозвучать: город не меняется молча");
        }

        // ================= действие построенного =================

        [Test]
        public void Temple_CalmsTheCityEveryDay_ByItsOwnDriver()
        {
            var cfg = new BalanceConfig();
            var c = Build(cfg, built: new[] { DefaultBuildings.CouncilHall, DefaultBuildings.Temple });

            var reports = new List<DayReport>();
            for (int d = 0; d < 6; d++) reports.AddRange(c.Processor.AdvanceFullDay());

            var aura = Ledger(reports).Where(x => x.Driver == TensionDriver.TempleAura).ToList();
            Assert.IsNotEmpty(aura, "Храм обязан действовать драйвером TempleAura — до Поправки №6 его не вызывал никто");
            Assert.IsTrue(aura.All(x => !x.Rejected), "TempleAura — понижающий драйвер белого списка");
            Assert.Less(aura.Sum(x => x.Applied), 0, "Храм снижает Напряжение, а не поднимает");
        }

        [Test]
        public void Fortifications_CalmTheCityEveryDay_ByTheirOwnDriver()
        {
            var cfg = new BalanceConfig();
            var c = Build(cfg, built: new[] { DefaultBuildings.CouncilHall, DefaultBuildings.Fortifications });

            var reports = new List<DayReport>();
            for (int d = 0; d < 8; d++) reports.AddRange(c.Processor.AdvanceFullDay());

            var walls = Ledger(reports).Where(x => x.Driver == TensionDriver.Fortifications).ToList();
            Assert.IsNotEmpty(walls, "Укрепления обязаны действовать своим драйвером");
            Assert.Less(walls.Sum(x => x.Applied), 0);
        }

        // ================= совет =================

        [Test]
        public void Raid_NeedsHall_CostsGold_HasCooldown_AndIsHeard()
        {
            var cfg = new BalanceConfig();

            var bare = Build(cfg, built: new string[0]);
            Give(bare.State, 100);
            Assert.AreEqual(CouncilOrderResult.NoCouncilHall,
                bare.Works.OrderRaid(bare.State, 1, cfg), "Без Зала совета облаву звать некому");

            var c = Build(cfg);
            Assert.AreEqual(CouncilOrderResult.NotEnoughGold, c.Works.OrderRaid(c.State, 1, cfg));

            Give(c.State, 100);
            Assert.AreEqual(CouncilOrderResult.Queued, c.Works.OrderRaid(c.State, 1, cfg));
            Assert.AreEqual(100 - cfg.City.RaidGoldCost, c.State.Resources.Get(ResourceType.Gold),
                "Облава оплачивается в момент приказа");

            var reports = c.Processor.AdvanceFullDay();
            var raid = Ledger(reports).Where(x => x.Driver == TensionDriver.CouncilRaid).ToList();
            Assert.AreEqual(1, raid.Count, "Облава срабатывает ровно один раз");
            Assert.AreEqual(cfg.Tension.RaidDelta, raid[0].Applied);
            Assert.IsTrue(Heard(reports).Any(r => r.TopicId == "council.raid"), "Облава обязана прозвучать");

            Assert.AreEqual(CouncilOrderResult.OnCooldown,
                c.Works.OrderRaid(c.State, c.Processor.CurrentDay + 1, cfg),
                "Облава с откатом: иначе Напряжение выкупалось бы золотом без предела");
        }

        [Test]
        public void Settlers_CostFood_ArriveNextDay_AndAreHeard()
        {
            var cfg = new BalanceConfig();
            var c = Build(cfg, population: 80);

            Assert.AreEqual(CouncilOrderResult.NotEnoughFood, c.Works.OrderSettlers(c.State, 1, cfg),
                "Людей зовут, когда есть чем кормить");

            Give(c.State, 0, 0, cfg.City.SettlersFoodCost);
            Assert.AreEqual(CouncilOrderResult.Queued, c.Works.OrderSettlers(c.State, 1, cfg));
            Assert.AreEqual(0, c.State.Resources.Get(ResourceType.Food), "Цена — еда, списывается сразу");

            int before = c.Processor.Population.Count;
            var reports = c.Processor.AdvanceFullDay();

            Assert.GreaterOrEqual(c.Processor.Population.Count, before + cfg.City.SettlersPerOrder);
            Assert.IsTrue(Heard(reports).Any(r => r.TopicId == "city.people.arrived" && r.Tags.Contains("reason:council")),
                "Пришедшие по решению совета обязаны прозвучать");

            Give(c.State, 0, 0, cfg.City.SettlersFoodCost);
            Assert.AreEqual(CouncilOrderResult.OnCooldown,
                c.Works.OrderSettlers(c.State, c.Processor.CurrentDay + 1, cfg),
                "Приём людей — решение с откатом, а не ежедневная кнопка: " +
                "без отката хутор становился селом на одиннадцатые сутки");
        }

        /// <summary>
        /// Фікс-ревью (major, DepartExpedition/D1): PeekExpeditionOutfitBuff()
        /// не знімає накопичений бонус — лише TakeExpeditionOutfitBuff() знімає,
        /// і рівно раз. До цього фіксу єдиним доступом ззовні був Take, і
        /// GameSession.DepartExpedition кликав його безумовно на першому ж
        /// відправленні (навіть на ІНШУ площадку, ніж замовлено), губля разовий
        /// бонус назавжди — цей тест ловить саме контракт примітиву, від якого
        /// залежить фікс на боці D1.
        /// </summary>
        [Test]
        public void OutfitExpedition_PeekDoesNotConsume_TakeConsumesExactlyOnce()
        {
            var cfg = new BalanceConfig();
            var c = Build(cfg);
            Give(c.State, cfg.Faction.OutfitExpeditionGoldCost);

            Assert.AreEqual(CouncilOrderResult.Applied, c.Works.OrderOutfitExpedition(c.State, "outskirts", 1, cfg));

            var peeked1 = c.Works.PeekExpeditionOutfitBuff();
            Assert.IsNotNull(peeked1, "Peek має бачити щойно замовлений бонус");
            Assert.AreEqual("outskirts", peeked1.SiteId);

            var peeked2 = c.Works.PeekExpeditionOutfitBuff();
            Assert.IsNotNull(peeked2, "повторний Peek нічого не знімає — бонус лишається на місці");
            Assert.AreEqual("outskirts", peeked2.SiteId);

            var taken = c.Works.TakeExpeditionOutfitBuff();
            Assert.IsNotNull(taken);
            Assert.AreEqual("outskirts", taken.SiteId);

            Assert.IsNull(c.Works.PeekExpeditionOutfitBuff(), "після Take бонуса більше нема — Peek бачить порожньо");
            Assert.IsNull(c.Works.TakeExpeditionOutfitBuff(), "і Take вдруге теж нічого не бере");
        }

        // ================= люди уходят =================

        [Test]
        public void Hunger_MakesPeopleLeave_AndItIsHeard()
        {
            var cfg = new BalanceConfig();
            var c = Build(cfg, population: 80);
            c.Processor.IsHungry = true;

            int before = c.Processor.Population.Count;
            var reports = c.Processor.AdvanceFullDay();

            Assert.Less(c.Processor.Population.Count, before + 1, "В голод люди уходят, а не прибывают");
            Assert.IsTrue(Heard(reports).Any(r => r.TopicId == "city.people.left" && r.Tags.Contains("reason:hunger")),
                "Уход людей обязан прозвучать: это следствие, которое игрок должен заметить");
        }

        // ================= тир =================

        [Test]
        public void Tier_NeedsBothPeopleAndTheKeyBuilding()
        {
            var cfg = new BalanceConfig();
            int enough = cfg.City.TierPopulation[0] + 10;

            var crowdOnly = Build(cfg, population: enough);
            crowdOnly.Processor.AdvanceFullDay();
            Assert.AreEqual(1, crowdOnly.Processor.Tier, "Толпа без таверны — ещё не село");

            var tavernOnly = Build(cfg, population: 80,
                built: new[] { DefaultBuildings.CouncilHall, DefaultBuildings.Tavern });
            tavernOnly.Processor.AdvanceFullDay();
            Assert.AreEqual(1, tavernOnly.Processor.Tier, "Таверна в пустом хуторе — ещё не село");

            var both = Build(cfg, population: enough,
                built: new[] { DefaultBuildings.CouncilHall, DefaultBuildings.Tavern });
            var reports = both.Processor.AdvanceFullDay();
            Assert.AreEqual(2, both.Processor.Tier, "Люди и таверна вместе делают хутор селом");
            Assert.IsTrue(Heard(reports).Any(r => r.TopicId == "city.tier.2"),
                "Смена тира слышна всегда: это главное событие дуги кампании");
        }

        [Test]
        public void Tier_ShowsOnTheMoodboard()
        {
            var cfg = new BalanceConfig();
            var c = Build(cfg, population: cfg.City.TierPopulation[0] + 10,
                built: new[] { DefaultBuildings.CouncilHall, DefaultBuildings.Tavern });

            var still = Build(cfg, population: cfg.City.TierPopulation[0] + 10,
                built: new[] { DefaultBuildings.CouncilHall });

            var grew = c.Processor.Advance(DayPhase.Day);
            var stayed = still.Processor.Advance(DayPhase.Day);

            Assert.Greater(grew.Signals.Moodboard.Prosperity, stayed.Signals.Moodboard.Prosperity,
                "Выросший город обязан выглядеть богаче: до Поправки №6 процветание стояло на нуле всю кампанию");

            // Тот же отчёт, что объявил рост, уже показывает село. Плёнка суток
            // поймала отставание: в ленте «хутор стал селом», а в заголовке кадра
            // ещё «хутор» — мудборд собирался со вчерашним тиром.
            Assert.IsTrue(grew.Signals.Requests.Any(r => r.TopicId == "city.tier.2"));
            Assert.AreEqual(1, grew.Signals.Moodboard.Prosperity, "Село — это процветание 1 (тир − 1)");
        }

        // ================= люди приходят по событию и из вылазки =================

        [Test]
        public void Expedition_BringsPeople_ThroughTheCityWorks()
        {
            var cfg = new BalanceConfig();
            var c = Build(cfg, population: 80);

            ExpeditionRunner.Complete(c.State, new ExpeditionResult { People = 4 }, c.Works);
            Assert.AreEqual(80, c.Processor.Population.Count,
                "Возврат отряда идёт между сутками: люди входят в город только внутри суток и со звуком");

            var reports = c.Processor.AdvanceFullDay();
            Assert.GreaterOrEqual(c.Processor.Population.Count, 84);
            Assert.IsTrue(Heard(reports).Any(r => r.TopicId == "city.people.arrived" && r.Tags.Contains("reason:expedition")));
        }

        [Test]
        public void Incident_GoodOutcome_BringsPeople()
        {
            var cfg = new BalanceConfig();
            var missing = DefaultIncidents.All().First(i => i.Id == "missing_person");
            Assert.Greater(missing.ArrivalsOnGood, 0, "Предпосылка: у поиска пропавшего есть находка");

            var roster = new Roster();
            var arch = new CompanionArchetype("scout", "scout").SetSkill(SkillType.Survival, 10);
            var scout = arch.CreateInstance("scout");
            scout.AssignedSlotId = missing.RelevantPositionId;
            roster.Add(scout);

            var population = new PopulationState(80);
            var tension = new TensionState(cfg.Tension, 500);
            tension.BeginDay();

            var outcome = IncidentResolver.Resolve(missing, new RosterAdapter(roster), null, null,
                population, tension, 1, cfg);

            Assert.GreaterOrEqual((int)outcome.Band, (int)OutcomeBand.Good, "Предпосылка: разбор удался");
            Assert.AreEqual(missing.ArrivalsOnGood, outcome.PeopleArrived);
            Assert.AreEqual(80 + missing.ArrivalsOnGood, population.Count, "Нашли — и привели людей");
        }

        // ================= слепок =================

        [Test]
        public void Save_KeepsWorksAcrossReload()
        {
            var cfg = new BalanceConfig();
            var c = Build(cfg);
            Give(c.State, 500, 20, 100);

            c.Works.Order(DefaultBuildings.Temple, c.State);
            c.Processor.AdvanceFullDay();
            c.Works.OrderRaid(c.State, c.Processor.CurrentDay + 1, cfg);
            c.Processor.AdvanceFullDay();
            c.Works.OrderSettlers(c.State, c.Processor.CurrentDay + 1, cfg);

            int stage = c.Works.StageOf(DefaultBuildings.Temple);
            string blob = c.Processor.SaveState();

            var fresh = Build(cfg);
            fresh.Processor.RestoreState(blob);

            Assert.IsTrue(fresh.Works.IsBuilding(DefaultBuildings.Temple),
                "Загрузка не имеет права отменять стройку, за которую уже заплачено");
            Assert.AreEqual(stage, fresh.Works.StageOf(DefaultBuildings.Temple));

            Give(fresh.State, 100);
            Assert.AreEqual(CouncilOrderResult.OnCooldown,
                fresh.Works.OrderRaid(fresh.State, fresh.Processor.CurrentDay + 1, cfg),
                "Иначе перезапуск — бесплатная облава");

            int before = fresh.Processor.Population.Count;
            fresh.Processor.AdvanceFullDay();
            Assert.GreaterOrEqual(fresh.Processor.Population.Count, before + cfg.City.SettlersPerOrder,
                "Позванные до сохранения переселенцы приходят и после загрузки");
        }

        // ================= люди на постах =================

        /// <summary>
        /// Здание открывает пост, но не занимает его. Плёнка суток нашла это
        /// раньше тестов: лазарет стоял с четвёртых суток, а ночью «на посту
        /// никого не было» — лекарь сидел без дела. Хозяин обязан поставить на
        /// открывшийся пост лучшего свободного по профильному навыку — и не
        /// трогать занятых, мёртвых, ушедших в вылазку и тех, кто в деле не смыслит.
        /// </summary>
        [Test]
        public void Steward_StaffsAnOpenedPost_WithTheBestFreeHand()
        {
            var cfg = new BalanceConfig();
            var c = Build(cfg, population: 80, tension: 0);
            var roster = c.State.Roster;

            Companion Hire(string id, int medicine)
            {
                var companion = new CompanionArchetype(id, id)
                    .SetSkill(SkillType.Medicine, medicine).CreateInstance(id);
                roster.Add(companion);
                return companion;
            }

            Hire("surgeon", 9);
            Assert.AreEqual(AssignmentResult.Success, c.State.TryAssign("surgeon", "council_seat"));
            Hire("porter", 2);
            Hire("medic", 7);
            Hire("ghost", 10).Status = CompanionStatus.Dead;
            Hire("walker", 8).Status = CompanionStatus.OnMission;
            Hire("layman", 0);

            // Лазарета нет — ставить некуда: поле и склад лекарям не по навыку.
            Assert.IsNull(Steward.Staff(c.State), "Без здания пост закрыт, и никто никуда не встал");
            Assert.IsFalse(c.State.GetSlot("infirmary_bed").IsOccupied);

            // Лазарет достроен.
            new CityWorks(DefaultBuildings.StartingSet.Concat(new[] { DefaultBuildings.Infirmary }))
                .ApplyToSlots(c.State);

            Assert.AreEqual("staff:medic@infirmary_bed", Steward.Staff(c.State),
                "Лучший свободный: хирург занят, призрак мёртв, ходок в вылазке");
            Assert.AreEqual("council_seat", roster.Get("surgeon").AssignedSlotId, "Занятых хозяин не переставляет");
            Assert.IsFalse(roster.Get("porter").IsAssigned);

            // Повтор ничего не меняет: детерминизм и идемпотентность.
            Assert.IsNull(Steward.Staff(c.State));
        }

        // ================= клапан =================

        /// <summary>
        /// Приёмка Поправки №6.5. До неё любая кампания кончалась Расколом: у
        /// лупа было давление и не было клапана. Здесь один и тот же город живёт
        /// девяносто суток дважды — без хозяина и с рачительным хозяином, который
        /// строит, зовёт облаву и принимает людей. Кошелёк у обоих одинаковый:
        /// разница только в том, играет ли кто-то.
        /// </summary>
        [Test]
        public void Valve_GoodPlay_MakesADifference()
        {
            var cfg = new BalanceConfig();

            var passive = FullWorld(cfg);
            var steward = FullWorld(cfg);
            var policy = new Steward();

            int villageDay = -1;
            for (int day = 0; day < 90; day++)
            {
                passive.Processor.AdvanceFullDay();

                policy.Act(steward.Works, steward.State, steward.Processor, cfg);
                steward.Processor.AdvanceFullDay();
                if (villageDay < 0 && steward.Processor.Tier >= 2) villageDay = steward.Processor.CurrentDay;
            }

            TestContext.WriteLine("Хутор стал селом на сутки {0}", villageDay);

            TestContext.WriteLine("Без хозяина: полоса {0}, Напряжение {1}, тир {2}, людей {3}",
                passive.Processor.Tension.Band, passive.Processor.Tension.Value,
                passive.Processor.Tier, passive.Processor.Population.Count);
            TestContext.WriteLine("С хозяином:  полоса {0}, Напряжение {1}, тир {2}, людей {3}, построено: {4}",
                steward.Processor.Tension.Band, steward.Processor.Tension.Value,
                steward.Processor.Tier, steward.Processor.Population.Count,
                string.Join(",", steward.Works.Built.OrderBy(x => x).ToArray()));

            Assert.Less(steward.Processor.Tension.Value, passive.Processor.Tension.Value,
                "Хорошая игра обязана давать более спокойный город — иначе клапана нет");
            Assert.Greater(steward.Processor.Population.Count, passive.Processor.Population.Count,
                "И более людный: город отвечает на заботу ростом");

            // Темп дуги (Поправка №6.4): село — около тридцатых суток. Окно
            // широкое, пока числа плейсхолдерные, но нижняя граница — защита от
            // регрессии: без отката приёма переселенцев хутор становился селом
            // на одиннадцатые сутки, и это было видно только на плёнке.
            Assert.GreaterOrEqual(villageDay, 20, "Село не должно появляться за неделю-другую");
            Assert.LessOrEqual(villageDay, 60, "Но и хорошая игра обязана довести хутор до села");
        }

        private static City FullWorld(BalanceConfig cfg)
        {
            var c = Build(cfg, population: 80, tension: 0);

            var roster = c.State.Roster;
            foreach (var pair in new Dictionary<string, SkillType>
            {
                { "elder", SkillType.Persuade }, { "keeper", SkillType.Survival },
                { "farmer", SkillType.Survival }, { "scout", SkillType.Survival },
                { "healer", SkillType.Medicine }, { "trader", SkillType.Trade }
            })
            {
                var arch = new CompanionArchetype(pair.Key, pair.Key)
                    .SetSkill(pair.Value, 7).SetSkill(SkillType.Persuade, 5).SetSkill(SkillType.Intimidate, 4);
                roster.Add(arch.CreateInstance(pair.Key));
            }

            c.State.TryAssign("elder", "council_seat");
            c.State.TryAssign("keeper", "storehouse_dock");
            c.State.TryAssign("farmer", "settlement_farms");
            c.State.TryAssign("scout", "scouting_post");

            var adapter = new RosterAdapter(roster, "elder", cfg);
            var pulse = new WorldPulse(cfg.Pulse);
            foreach (var s in DefaultPressureSources.All()) pulse.AddSource(s);

            c.Processor.Roster = adapter;
            c.Processor.Casualties = adapter;
            c.Processor.Pulse = pulse;
            c.Processor.Incidents = DefaultIncidents.BuildTable();
            c.Processor.Repeats = new RepeatTracker();

            // Одинаковый стартовый кошелёк у обоих: сравнивается игра, а не удача.
            Give(c.State, 400, 40, 200);
            return c;
        }
    }
}
