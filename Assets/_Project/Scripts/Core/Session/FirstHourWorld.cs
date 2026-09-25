using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Characters.Progression;
using Game.Core.Characters.Traits;
using Game.Core.Checks;
using Game.Core.Economy;
using Game.Core.Expeditions;
using Game.Core.Factions;
using Game.Core.Items;
using Game.Core.Loop;
using Game.Core.Pressure;
using Game.Core.Quests;
using Game.Core.Scenes;
using Game.Core.Settlement;
using Game.Core.Stats;
using Game.Core.Story;
using Game.Core.World;
using BaseState = Game.Core.Base.BaseState;
using EconomyBlob = Game.Core.Base.EconomyBlob;
using CityWorksType = Game.Core.Base.CityWorks;
using RosterAdapter = Game.Core.Base.RosterAdapter;
using RepeatTracker = Game.Core.Base.RepeatTracker;
using CityWorksStep = Game.Core.Base.CityWorksStep;
using PopulationStep = Game.Core.Base.PopulationStep;
using DefaultBuildings = Game.Core.Base.DefaultBuildings;
using ProductionStep = Game.Core.Base.ProductionStep;
using SettlementCycleType = Game.Core.Base.SettlementCycle;

namespace Game.Core.Session
{
    /// <summary>
    /// Мир первого часа «Перевал» (Поправка №7, аудит G8/G9/G15) — построение
    /// перенесено из tools/Shared/SettlementWorld СЮДА, в ядро, чтобы Unity,
    /// консольная сборка и харнес темпа собирали ОДИН И ТОТ ЖЕ мир, а не три
    /// похожих копии.
    ///
    /// Раньше ростер среза был generic-архетипами («guard», «trader», ...), а
    /// именной каст открытия существовал только как текст сцен — карточки не
    /// были на ростере, протагонист не был актором (аудит G8/G9). Здесь ростер
    /// — сам именной каст: Захар на совете, Дід Овсій на складе, Знахарка Гафія
    /// в лазареті, Максим и Мирослава в поле (доступны вилазці доби 4),
    /// протагонист — Companion с id "protagonist", зарегистрированный в
    /// RosterAdapter как актор.
    ///
    /// Городской цикл подключён ПОЛНОСТЬЮ: производство, стройка (совет уже
    /// стоит, склад уже стоит — <see cref="DefaultBuildings.StartingSet"/>),
    /// население. Раньше срез и харнес темпа собирались через
    /// DayProcessor.DefaultSteps() — без единого зерна выработки и без голода
    /// (см. CLAUDE.md «Мост производства»). Поправка №7.1 подключает их здесь.
    ///
    /// Полностью детерминирован: два вызова Build с одними аргументами дают
    /// тождественные по структуре миры (никакого System.Random — инвариант 1).
    /// </summary>
    public sealed class FirstHourWorld
    {
        public const string ProtagonistId = "protagonist";

        /// <summary>Семь постов общины — тот же порядок, что у tools/Shared/SettlementWorld раньше.</summary>
        public static readonly string[] Positions =
        {
            "storehouse_dock", "settlement_market", "settlement_farms",
            "infirmary_bed", "council_seat", "scouting_post", "workshop_bench"
        };

        /// <summary>
        /// Кто по умолчанию доступен для вилазки доби 4 (FIRST_HOUR §2.2): сам
        /// протагонист и двое напарників у полі — Максим і Мирослава. Всі троє
        /// не стоять на посту зі старту (§3.0), тож відряд не звільняє чужого
        /// поста.
        /// </summary>
        public static readonly string[] PartyIds = { ProtagonistId, "maksym", "myroslava" };

        public BaseState BaseState { get; }
        public CityWorksType CityWorks { get; }
        public DayProcessor Processor { get; }
        public SettlementCycleType Cycle { get; }
        public Roster Roster { get; }
        public ExpeditionParty Party { get; }
        public SiteLedger Sites { get; }
        public StoryFlags Flags { get; }

        /// <summary>
        /// Доповнення D1 (Фаза D): квести (R6, поза конвеєром), фракції (R5),
        /// банк очків білда протагоніста (R11) і Готовність громади (R8).
        /// Жоден з чотирьох не заводив A1 у першій збірці світу — тут вони
        /// нарешті отримують контент/реєстр/крок конвеєра, симетрично тому, як
        /// A1 вже підключив Sites/Flags/Party вище. Правка цього файлу —
        /// виняток із §5.1 (A1-виключний), санкціонований інтегратором Фази D:
        /// без реального гачка в конвеєрі дня жодна з чотирьох систем не могла
        /// би працювати з живого GameSession, лишаючись «підключи сам» на
        /// довільний виклик ззовні, якого в контракті §4.1 просто немає.
        /// </summary>
        public QuestLog Quests { get; }
        public FactionRegistry Factions { get; }
        public SpendablePoints Points { get; }
        public ReadinessTrack Readiness { get; }

        /// <summary>Сташ поселення (B3): лут із вилазок/данжу і ціль Equip/CraftUpgrade (§4.1 D1).</summary>
        public Inventory Inventory { get; }

        private FirstHourWorld(BaseState baseState, CityWorksType cityWorks, DayProcessor processor,
            SettlementCycleType cycle, Roster roster, ExpeditionParty party, SiteLedger sites, StoryFlags flags,
            QuestLog quests, FactionRegistry factions, SpendablePoints points, ReadinessTrack readiness,
            Inventory inventory)
        {
            BaseState = baseState;
            CityWorks = cityWorks;
            Processor = processor;
            Cycle = cycle;
            Roster = roster;
            Party = party;
            Sites = sites;
            Flags = flags;
            Quests = quests;
            Factions = factions;
            Points = points;
            Readiness = readiness;
            Inventory = inventory;
        }

        /// <summary>
        /// Собрать мир первого часа. Деталь: тир и режим точки решения —
        /// параметры (Alpha.Play спрашивает игрока, Alpha.Sim меряет темп в
        /// автономном режиме), всё остальное — фиксированный контент открытия.
        ///
        /// <paramref name="testBuildOneDayConstruction"/> — Поправка №7.7.
        /// Default здесь — false: прямые вызовы Build() (CampaignPacingTests,
        /// SettlementSaveTests, FirstHourWorldTests) — это замер темпа/баланса
        /// кампании, а не тестовая сборка, и не должны тихо поменять поведение
        /// от одного лишь добавления параметра. GameSession.NewGame —
        /// единственный вызывающий, который явно передаёт значение из
        /// NewGameOptions (там default true) — так тестовая сборка (Unity,
        /// Alpha.Play, боты) получает один день, а кампания и харнес темпа —
        /// нет.
        ///
        /// <paramref name="testBuildTensionPace"/> — Поправка №7 (рішення власника
        /// 24.09.2026, <see cref="TestBuildTensionPace"/>): той самий приём, що й
        /// <paramref name="testBuildOneDayConstruction"/> вище. Default — false з
        /// тієї самої причини: прямі виклики Build() (CampaignPacingTests,
        /// SettlementSaveTests, CityWorksTests, FirstHourWorldTests) міряють темп/
        /// баланс КАМПАНІЇ і не повинні мовчки отримати стиснуту шкалу Напруги
        /// від самого лише додавання параметра.
        /// </summary>
        public static FirstHourWorld Build(int tier = 1, bool requirePlayerDecision = false, BalanceConfig balance = null,
            bool testBuildOneDayConstruction = false, bool testBuildTensionPace = false)
        {
            var cfg = balance ?? new BalanceConfig();

            // Тестова збірка (Поправка №7): підмінюємо ЛИШЕ Tension/Pulse ЦЬОГО
            // щойно узгодженого cfg — GameSession.NewGame завжди передає сюди
            // свіжий BalanceConfig (див. коментар класу), тож кампанійний дефолт,
            // яким користуються прямі виклики Build() без прапорця, не бачить
            // цієї підміни узагалі.
            if (testBuildTensionPace)
            {
                cfg.Tension = TestBuildTensionPace.BuildTensionBalance(cfg.Tension);
                cfg.Pulse = TestBuildTensionPace.BuildPulseBalance(cfg.Pulse);
            }

            var roster = BuildRoster(cfg);
            var resources = new ResourceLedger();
            var baseState = new BaseState(roster, resources, cfg);

            foreach (var slot in Game.Core.DefaultContent.AllSlots()) baseState.AddSlot(slot);

            // Совет и склад уже стоят — хутор встречает игрока работающей
            // общиной, а не стройплощадкой (Поправка №6.1, §3.0 FIRST_HOUR).
            var works = new CityWorksType(DefaultBuildings.StartingSet, testBuildOneDayConstruction);
            works.ApplyToSlots(baseState);

            // Лазарет открывается зданием, которого в StartingSet нет — но
            // Знахарка Гафія стоит на посту с вечера первых суток (§3.0), а не
            // ждёт стройки. Пост среза считается уже оборудованным: срез
            // начинается с работающей общины, а не со строительной площадки
            // (тот же приём раньше держал открытым storehouse_dock).
            var infirmary = baseState.GetSlot("infirmary_bed");
            if (infirmary != null) infirmary.Unlocked = true;

            // Стартовый кошелёк — ПЛЕЙСХОЛДЕР (числа баланса поправит владелец):
            // хватает на первые сутки без паники, не хватает навсегда — фермы
            // никто не держит все пять суток открытия (§3.0-3.5), и голод —
            // честная, а не срежиссированная цена этого пробела.
            resources.Add(ResourceType.Gold, 40);
            resources.Add(ResourceType.Materials, 10);
            resources.Add(ResourceType.Food, 20);

            Assign(baseState, "zakhar", "council_seat");
            Assign(baseState, "keeper", "storehouse_dock");
            Assign(baseState, "healer", "infirmary_bed");
            // maksym/myroslava/протагонист — в полі (§3.0): на посты НЕ ставятся.

            var adapter = new RosterAdapter(roster, ProtagonistId, cfg);

            var tension = new TensionState(cfg.Tension);
            var pulse = new WorldPulse(cfg.Pulse);
            var crisisSource = testBuildTensionPace ? TestBuildTensionPace.BuildCrisisSource() : null;
            foreach (var source in DefaultPressureSources.All(crisisSource)) pulse.AddSource(source);
            // Именной накопитель «Тугар» — слух о боярине из сцены открытия.
            pulse.AddSource(new OpeningContent.TuharPressureSource());
            // Авторская последовательность открытия: узел / припасы / девочка.
            foreach (var scripted in OpeningContent.ScriptedSources()) pulse.AddSource(scripted);

            var incidents = DefaultIncidents.BuildTable();
            foreach (var incident in OpeningContent.All()) incidents.Add(incident);

            var readiness = new ReadinessTrack(cfg.Readiness);

            var production = new ProductionStep(baseState);
            var steps = new List<IDayStep>(SettlementCycleType.BuildSteps(production))
            {
                new CityWorksStep(works, baseState),
                new PopulationStep(works),
                // R8 (Готовність громади до фіналу): без цього кроку
                // ReadinessTickStep ніколи не викликається — будинок, добудований
                // сьогодні, і спокійна доба без страху проходили б повз трек.
                new ReadinessTickStep(readiness, cfg.Readiness)
            };

            var sites = new SiteLedger();
            var flags = new StoryFlags();
            var party = new ExpeditionParty();

            // R6: квести поза конвеєром дня — пул реєструється тут же, разом з
            // рештою контенту відкриття, щоб GameSession міг одразу почати
            // "hafiya" на добу 2 без додаткового виклику зовні.
            var quests = new QuestLog(DefaultQuests.All(cfg));

            // R5: три фракції зрізу зі стартовим нейтральним ставленням.
            var factions = DefaultFactions.NewRegistry(cfg.Faction);

            var processor = new DayProcessor(tension, cfg, steps)
            {
                Tier = tier,
                Roster = adapter,
                Casualties = adapter,
                Population = new PopulationState(),
                Pulse = pulse,
                Incidents = incidents,
                Repeats = new RepeatTracker(),
                CityState = works,
                Economy = new EconomyBlob(baseState),
                Sites = sites,
                Flags = flags,
                Party = party,
                RequirePlayerDecision = requirePlayerDecision,
                // Три поста, что реально держат люди на старте (§3.0) — доклад
                // с пустого поста молчит сам по себе (правило §2 табл. строка 6).
                PostDomains = new[]
                {
                    new PostDomain("council_seat", "рада", SkillKeys.Persuade, 5),
                    new PostDomain("storehouse_dock", "склад", SkillKeys.Survival, 5),
                    new PostDomain("infirmary_bed", "лазарет", SkillKeys.Medicine, 5)
                }
            };

            var cycle = new SettlementCycleType(baseState, processor, production);

            var points = new SpendablePoints();
            var inventory = new Inventory();

            return new FirstHourWorld(baseState, works, processor, cycle, roster, party, sites, flags,
                quests, factions, points, readiness, inventory);
        }

        private static void Assign(BaseState baseState, string companionId, string positionId)
        {
            var result = baseState.TryAssign(companionId, positionId);
            if (result != Game.Core.Base.AssignmentResult.Success)
                throw new System.InvalidOperationException(
                    "Стартовая расстановка сорвалась: " + companionId + " -> " + positionId +
                    " (" + result + "). Тихая неудача здесь означает пост без человека на весь прогон.");
        }

        /// <summary>
        /// Именной каст (аудит G8/G9): карточки берутся из <see cref="OpeningCast"/>,
        /// протагонист — из <see cref="OpeningScenes.Protagonist"/>. Числа скилов
        /// расставлены под домены их постов и под §3.0 FIRST_HOUR — сами по себе
        /// они ПЛЕЙСХОЛДЕР, как и весь остальной баланс среза.
        /// </summary>
        private static Roster BuildRoster(BalanceConfig cfg)
        {
            var roster = new Roster();

            // Стартові трейти (полірування, ціль 1 «Картка персонажа»):
            // Game.Core.Characters.Traits.DefaultTraits — інакше секція
            // "Трейти" картки персонажа порожня для всього іменного касту.
            roster.Add(Named("zakhar", OpeningCast.Zakhar(), cfg, arch => arch
                .SetAttribute(AttributeType.Will, 6).SetAttribute(AttributeType.Wits, 5)
                .SetAttribute(AttributeType.Strength, 3).SetAttribute(AttributeType.Agility, 3)
                .SetSkill(SkillType.Persuade, 8).SetSkill(SkillType.Tactics, 5)
                .SetSkill(SkillType.Intimidate, 3).SetSkill(SkillType.Trade, 3)
                .AddStartingTrait(DefaultTraits.Steadfast())));

            roster.Add(Named("keeper", OpeningCast.Keeper(), cfg, arch => arch
                .SetAttribute(AttributeType.Strength, 5).SetAttribute(AttributeType.Wits, 4)
                .SetAttribute(AttributeType.Will, 4).SetAttribute(AttributeType.Agility, 3)
                .SetSkill(SkillType.Survival, 8).SetSkill(SkillType.Trade, 7)
                .SetSkill(SkillType.Persuade, 4)
                .AddStartingTrait(DefaultTraits.Meticulous())));

            roster.Add(Named("healer", OpeningCast.Healer(), cfg, arch => arch
                .SetAttribute(AttributeType.Wits, 6).SetAttribute(AttributeType.Will, 5)
                .SetAttribute(AttributeType.Agility, 4).SetAttribute(AttributeType.Strength, 3)
                .SetSkill(SkillType.Medicine, 8).SetSkill(SkillType.Survival, 4)
                .SetSkill(SkillType.Persuade, 3)
                .AddStartingTrait(DefaultTraits.Blunt())));

            // Максим Беркут — Persuade 4 / Tactics 4, Melee 6 / Survival 5 (§3.0).
            roster.Add(Named("maksym", OpeningCast.Maksym(), cfg, arch => arch
                .SetAttribute(AttributeType.Strength, 6).SetAttribute(AttributeType.Agility, 5)
                .SetAttribute(AttributeType.Wits, 4).SetAttribute(AttributeType.Will, 5)
                .SetSkill(SkillType.Melee, 6).SetSkill(SkillType.Survival, 5)
                .SetSkill(SkillType.Tactics, 4).SetSkill(SkillType.Persuade, 4)
                .AddStartingTrait(DefaultTraits.Steadfast())
                .AddStartingTrait(DefaultTraits.HotBlooded())));

            // Мирослава — Persuade 5, Ranged 6 / Trade 4 (§3.0).
            roster.Add(Named("myroslava", OpeningCast.Myroslava(), cfg, arch => arch
                .SetAttribute(AttributeType.Agility, 6).SetAttribute(AttributeType.Wits, 5)
                .SetAttribute(AttributeType.Will, 4).SetAttribute(AttributeType.Strength, 3)
                .SetSkill(SkillType.Ranged, 6).SetSkill(SkillType.Persuade, 5)
                .SetSkill(SkillType.Trade, 4)
                .AddStartingTrait(DefaultTraits.Wary())
                .AddStartingTrait(DefaultTraits.SharpEyed())));

            // Протагонист: сборный старт-плейсхолдер. Полноценное создание
            // (R12, ProtagonistCreation/Backgrounds) — работа пакета B7, ещё не
            // смерджена; здесь — только чтобы актор существовал и был в ростере.
            roster.Add(Named(ProtagonistId, OpeningScenes.Protagonist(), cfg, arch => arch
                .SetAttribute(AttributeType.Strength, 4).SetAttribute(AttributeType.Agility, 4)
                .SetAttribute(AttributeType.Wits, 4).SetAttribute(AttributeType.Will, 4)
                .SetSkill(SkillType.Persuade, 4).SetSkill(SkillType.Tactics, 4)
                .SetSkill(SkillType.Melee, 4).SetSkill(SkillType.Survival, 4)));

            return roster;
        }

        private static Companion Named(string id, CharacterCard card, BalanceConfig cfg,
            System.Func<CompanionArchetype, CompanionArchetype> build)
        {
            var arch = new CompanionArchetype(id, card.DisplayName);
            build(arch);
            var companion = arch.CreateInstance(id, cfg);
            companion.Card = card;
            return companion;
        }
    }
}
