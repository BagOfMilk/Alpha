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
    /// Світ першої години «Перевал» (Поправка №7, аудит G8/G9/G15) — побудову
    /// перенесено з tools/Shared/SettlementWorld СЮДИ, в ядро, щоб Unity,
    /// консольна збірка і харнес темпу збирали ОДИН І ТОЙ САМИЙ світ, а не три
    /// схожі копії.
    ///
    /// Раніше ростер зрізу був generic-архетипами («guard», «trader», ...), а
    /// іменний каст відкриття існував лише як текст сцен — картки не
    /// були на ростері, протагоніст не був актором (аудит G8/G9). Тут ростер
    /// — сам іменний каст: Захар на раді, Дід Овсій на складі, Знахарка Гафія
    /// в лазареті, Максим і Мирослава в полі (доступні вилазці доби 4),
    /// протагоніст — Companion з id "protagonist", зареєстрований у
    /// RosterAdapter як актор.
    ///
    /// Міський цикл підключений ПОВНІСТЮ: виробництво, будівництво (старт без
    /// будівель — <see cref="DefaultBuildings.StartingSet"/> порожній, першу
    /// обирає гравець після прологу, Поправка №12.7), населення. Раніше зріз і харнес темпу збирались через
    /// DayProcessor.DefaultSteps() — без жодного зерна виробітку і без голоду
    /// (див. CLAUDE.md «Міст виробництва»). Поправка №7.1 підключає їх тут.
    ///
    /// Повністю детермінований: два виклики Build з тими самими аргументами дають
    /// тотожні за структурою світи (жодного System.Random — інваріант 1).
    /// </summary>
    public sealed class FirstHourWorld
    {
        public const string ProtagonistId = "protagonist";

        /// <summary>Сім постів громади — той самий порядок, що й у tools/Shared/SettlementWorld раніше.</summary>
        public static readonly string[] Positions =
        {
            "storehouse_dock", "settlement_market", "settlement_farms",
            "infirmary_bed", "council_seat", "scouting_post", "workshop_bench"
        };

        /// <summary>
        /// Хто за замовчуванням доступний для вилазки доби 4 (FIRST_HOUR §2.2): сам
        /// протагоніст і двоє напарників у полі — Максим і Мирослава. Всі троє
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
        /// Зібрати світ першої години. Деталь: тир і режим точки рішення —
        /// параметри (Alpha.Play питає гравця, Alpha.Sim міряє темп в
        /// автономному режимі), все інше — фіксований контент відкриття.
        ///
        /// <paramref name="testBuildOneDayConstruction"/> — Поправка №7.7.
        /// Default тут — false: прямі виклики Build() (CampaignPacingTests,
        /// SettlementSaveTests, FirstHourWorldTests) — це замір темпу/балансу
        /// кампанії, а не тестова збірка, і не повинні мовчки змінити поведінку
        /// від самого лише додавання параметра. GameSession.NewGame —
        /// єдиний викликач, який явно передає значення з
        /// NewGameOptions (там default true) — так тестова збірка (Unity,
        /// Alpha.Play, боти) отримує один день, а кампанія і харнес темпу —
        /// ні.
        ///
        /// <paramref name="testBuildTensionPace"/> — Поправка №7 (рішення власника
        /// 24.09.2026, <see cref="TestBuildTensionPace"/>): той самий прийом, що й
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

            // Поправка №8.4, уточнена №12.7 (рішення власника 29.09.2026): гра
            // стартує БЕЗ будівель — усі пости, які відкриває будівля (рада,
            // склад, лазарет, майстерня, ринок), закриті. Першу будівлю гравець
            // обирає одразу після прологу (OpeningScenes, вибір
            // first_building_choice) — вона стає без ціни, і на її пост стає свій
            // іменний. Раніше тут стояли Зала ради і Склад, а лазарет
            // відкривався руками без будівлі — пропозиція асистента, яку
            // власник скасував.
            var works = new CityWorksType(DefaultBuildings.StartingSet, testBuildOneDayConstruction);
            works.ApplyToSlots(baseState);

            // Поправка №12.9 (рішення власника 29.09.2026: «рада — віче просто
            // неба від старту, Зала ради — пізніше як розширення»): пост ради
            // відкритий і зайнятий Захаром з першого ранку, незалежно від
            // того, яку першу будівлю обере гравець після прологу — на
            // відміну від Складу/Лазарету, які чекають вибору. Зала ради
            // лишається звичайною будівлею (вмикає Указ/Дипломатію/
            // Інвестицію/Спорядження), тому OpensSlotId у неї більше немає
            // (DefaultBuildings) — інакше ApplyToSlots вище закрив би щойно
            // відкритий пост.
            var councilSeat = baseState.GetSlot("council_seat");
            if (councilSeat != null) councilSeat.Unlocked = true;
            baseState.TryAssign("zakhar", "council_seat");

            // Стартовий гаманець — ПЛЕЙСХОЛДЕР (числа балансу виправить власник):
            // вистачає на перші доби без паніки, не вистачає назавжди — ферми
            // ніхто не тримає всі п'ять діб відкриття (§3.0-3.5), і голод —
            // чесна, а не зрежисована ціна цієї прогалини.
            // Поправка №12.7: було 40 золота ПЛЮС збудовані Зала ради (40) і
            // Склад (25). Тепер одна будівля — з вибору, без ціни, тож гаманець
            // піднято на 30, щоб друга будова була по кишені вже першого
            // ранку (боти туру інакше не встигали до таверни/майстерні за 15
            // діб — AllMechanicsCoverageTests Row16/Row22).
            resources.Add(ResourceType.Gold, 70);
            resources.Add(ResourceType.BuildComponent, 10);
            // Поправка №12.5: крафтовий компонент — на ОДИН апгрейд у майстерні
            // (ItemBalance.CraftComponentCost); далі — лише ззовні (майстерня-
            // руїна, данжі). ПЛЕЙСХОЛДЕР, як і решта гаманця.
            resources.Add(ResourceType.CraftComponent, 3);
            resources.Add(ResourceType.Food, 20);

            // Поправка №12.7: Захар, Дід Овсій і Гафія на старті вільні — їхні
            // пости закриті, доки не стане будівля. На пост першої будівлі
            // іменного ставить сам вибір (GameSession.GrantBuildingFromConsequence);
            // решту гравець розставляє сам (напр. на ферми — вони відкриті).
            // maksym/myroslava/протагоніст — в полі (§3.0): на пости НЕ ставляться.

            var adapter = new RosterAdapter(roster, ProtagonistId, cfg);

            var tension = new TensionState(cfg.Tension);
            var pulse = new WorldPulse(cfg.Pulse);
            var crisisSource = testBuildTensionPace ? TestBuildTensionPace.BuildCrisisSource() : null;
            foreach (var source in DefaultPressureSources.All(crisisSource)) pulse.AddSource(source);
            // Іменний накопичувач «Тугар» — чутка про боярина зі сцени відкриття.
            pulse.AddSource(new OpeningContent.TuharPressureSource());
            // Авторська послідовність відкриття: вузол / припаси / дівчинка.
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
                // Поправка №12.10: Майстерня й Ринок тепер теж можуть стати
                // ПЕРШОЮ будівлею (ремесло Гобана-Сайра/Синдбада), але СВІДОМО
                // без власного PostDomain тут — три профільних пости
                // (рада/склад/лазарет) лишаються еталоном темпу
                // (TestBuildTensionPaceTests калібрується проти них), а
                // додавання двох нових доменів вимірювано зсуває темп (Ропіт
                // 15→13, бунт 25→23 на сухому HomebodyPolicy-прогоні) — заміна
                // одного присутнього фахівця на іншого з іншим набором скілів,
                // а не сама наявність PostDomain. Верстак/Ринок доповідають
                // мовчанням, як і решта непрофільних постів (§7.5, AUDIT-GAPS
                // G13/G14) — відкрите питання власнику, чи розширювати трійку.
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

        /// <summary>
        /// Іменний каст (аудит G8/G9): картки беруться з <see cref="OpeningCast"/>,
        /// протагоніст — з <see cref="OpeningScenes.Protagonist"/>. Числа скілів
        /// розставлені під домени їхніх постів і під §3.0 FIRST_HOUR — самі по собі
        /// вони ПЛЕЙСХОЛДЕР, як і весь інший баланс зрізу.
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

            // Поправка №12.10 (пул прибульців): усі четверо фахівців — Дід
            // Овсій, Гафія, Гобан-Сайр, Синдбад — існують у ростері з самого
            // Build() (хто саме ПРИБИВСЯ — вирішується пізніше, GameSession.
            // ApplyArrivalsPool, коли відомі і передісторія, і відповідь
            // Тугарові); той, хто не прибився, отримує CompanionStatus.
            // NotArrived і ніде більше не з'являється.
            roster.Add(Named(ArrivalsPool.GobanId, OpeningCast.Goban(), cfg, arch => arch
                .SetAttribute(AttributeType.Strength, 5).SetAttribute(AttributeType.Wits, 4)
                .SetAttribute(AttributeType.Will, 4).SetAttribute(AttributeType.Agility, 4)
                .SetSkill(SkillType.Mechanics, 8).SetSkill(SkillType.Trade, 5)
                .SetSkill(SkillType.Survival, 4)
                .AddStartingTrait(DefaultTraits.Meticulous())));

            roster.Add(Named(ArrivalsPool.SindbadId, OpeningCast.Sindbad(), cfg, arch => arch
                .SetAttribute(AttributeType.Wits, 5).SetAttribute(AttributeType.Will, 4)
                .SetAttribute(AttributeType.Agility, 4).SetAttribute(AttributeType.Strength, 4)
                .SetSkill(SkillType.Trade, 8).SetSkill(SkillType.Persuade, 5)
                .SetSkill(SkillType.Survival, 4)
                .AddStartingTrait(DefaultTraits.Wary())));

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

            // Протагоніст: збірний старт-плейсхолдер. Повноцінне створення
            // (R12, ProtagonistCreation/Backgrounds) — робота пакета B7, ще не
            // змерджена; тут — лише щоб актор існував і був у ростері.
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
