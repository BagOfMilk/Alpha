using System.Collections.Generic;
using Game.Core.Session;
using Game.Gameplay.Walk;

namespace Game.Gameplay.UI
{
    /// <summary>Де живе команда ядра (Поправка №18.1: дія — у світі, панелі — огляд, меню — сервіс).</summary>
    public enum UxHomeKind
    {
        /// <summary>Станція в будівлі або просто неба (<see cref="BuildingCatalog"/>).</summary>
        Station = 0,
        /// <summary>Місце в селі без станції: ділянка, Дошка оголошень, майданчик, людина.</summary>
        Place,
        /// <summary>Головна кнопка фази HUD (HP-2) — завжди на тому самому місці.</summary>
        Hud,
        /// <summary>Подія, а не меню: створення героя, портретна сцена, бій, підсумок (№18.1).</summary>
        Event,
        /// <summary>Сервіс: титул і Esc-меню — зберегти, завантажити, тестові перемикачі.</summary>
        Service,
        /// <summary>Не дія гравця: оболонка чи суперник кличе сам (сцени вечора, хід ворога).</summary>
        Auto,
        /// <summary>Ще не у світі: живе на повноекранному вікні чи в панелі огляду; закриє крок плану (<see cref="UxHome.Where"/>).</summary>
        Gap
    }

    /// <summary>Як охоронець підтверджує, що дія справді там (<c>WorldFirstTests</c>).</summary>
    public enum UxHomeCheck
    {
        /// <summary>Панель місця вже ранку доби 1 має дію з цією командою (будуємо й дивимось).</summary>
        DayOne = 0,
        /// <summary>Дія з'являється лише в певному стані (річ у схованці, прогноз зборів, глава арки) — перевірка за кодом.</summary>
        Source
    }

    /// <summary>Один дім команди: вид, місце (id станції, місця, екрана або крок плану), панель-посередник, спосіб перевірки.</summary>
    public sealed class UxHome
    {
        public readonly string Command;
        public readonly UxHomeKind Kind;
        public readonly string Where;
        /// <summary>Панель, через яку дія відкривається з місця (Креслення з Віча), або <see cref="UxPanelId.None"/>.</summary>
        public readonly UxPanelId Via;
        public readonly UxHomeCheck Check;

        public UxHome(string command, UxHomeKind kind, string where, UxHomeCheck check = UxHomeCheck.DayOne, UxPanelId via = UxPanelId.None)
        {
            Command = command;
            Kind = kind;
            Where = where;
            Check = check;
            Via = via;
        }

        public bool InWorld => Kind == UxHomeKind.Station || Kind == UxHomeKind.Place;
    }

    /// <summary>
    /// Таблиця «команда ядра → дім» (Поправка №18.1, `docs/ROADMAP.md` трек U,
    /// U8–U13). Одне джерело правди для охоронців: кожна команда, яку кличе
    /// інтерфейс, має дім; панелі огляду (C, J, N, F10) не мають дій, яких немає
    /// у світі; список «ще не у світі» (<see cref="UxHomeKind.Gap"/>) лише
    /// скорочується — кожен крок плану забирає звідси свої команди.
    /// </summary>
    public static class UxWorldHomes
    {
        /// <summary>Будь-яка ділянка села (картка ділянки з «Замовити»).</summary>
        public const string AnyPlot = "plot:*";
        /// <summary>Будь-яка людина в селі (розмова).</summary>
        public const string AnyPerson = "person:*";
        public const string PhaseButton = "phase_button";

        private static readonly UxHome[] Fixed =
        {
            // Віче просто неба (№12.9)
            St(nameof(GameSession.OrderRaid), BuildingCatalog.VecheStation),
            St(nameof(GameSession.OrderSettlers), BuildingCatalog.VecheStation),
            St(nameof(GameSession.OrderPrepareThreat), BuildingCatalog.VecheStation),
            new UxHome(nameof(GameSession.OrderBuilding), UxHomeKind.Station, BuildingCatalog.VecheStation, UxHomeCheck.DayOne, UxPanelId.Blueprints),
            // Полонені й «наші в полоні» — блок під картками Віча (GameShell.DrawExtras, трек бою №14).
            St(nameof(GameSession.RecruitPrisoner), BuildingCatalog.VecheStation, UxHomeCheck.Source),
            St(nameof(GameSession.RansomPrisoner), BuildingCatalog.VecheStation, UxHomeCheck.Source),
            St(nameof(GameSession.ReleasePrisoner), BuildingCatalog.VecheStation, UxHomeCheck.Source),
            St(nameof(GameSession.RansomCaptive), BuildingCatalog.VecheStation, UxHomeCheck.Source),
            St(nameof(GameSession.NegotiateCaptive), BuildingCatalog.VecheStation, UxHomeCheck.Source),
            St(nameof(GameSession.RaidCaptors), BuildingCatalog.VecheStation, UxHomeCheck.Source),

            // Стіл ради в Залі
            St(nameof(GameSession.OrderDecree), "council_table"),
            St(nameof(GameSession.OrderDiplomacy), "council_table"),
            St(nameof(GameSession.OrderInvestment), "council_table"),
            St(nameof(GameSession.OrderOutfitExpedition), "council_table"),

            // Склад, Майстерня, Ринок
            St(nameof(GameSession.Equip), "stash", UxHomeCheck.Source),
            St(nameof(GameSession.Unequip), "stash", UxHomeCheck.Source),
            St(nameof(GameSession.CraftUpgrade), "workbench", UxHomeCheck.Source),
            St(nameof(GameSession.OfferQuestStage), "market_traders", UxHomeCheck.Source),
            St(nameof(GameSession.ResolveQuestChoice), "market_traders", UxHomeCheck.Source),

            // Застава: збори на вилазку
            St(nameof(GameSession.PreviewExpedition), BuildingCatalog.MusterStation),
            St(nameof(GameSession.DepartExpedition), BuildingCatalog.MusterStation, UxHomeCheck.Source),

            // Місця без станції
            Pl(nameof(GameSession.OrderBuilding), AnyPlot),
            Pl(nameof(GameSession.OfferQuestStage), VillagePlaces.NoticeBoardId, UxHomeCheck.Source),
            Pl(nameof(GameSession.ResolveQuestChoice), VillagePlaces.NoticeBoardId, UxHomeCheck.Source),
            Pl(nameof(GameSession.NewTrainingBattle), VillagePlaces.TrainingGroundId),
            Pl(nameof(GameSession.BeginArcChapterScene), AnyPerson, UxHomeCheck.Source),
            Pl(nameof(GameSession.BeginArcChapterQuest), AnyPerson, UxHomeCheck.Source),
            Pl(nameof(GameSession.OfferQuestStage), AnyPerson, UxHomeCheck.Source),
            Pl(nameof(GameSession.ResolveQuestChoice), AnyPerson, UxHomeCheck.Source),
            // Намет героя (U8): розвиток героя. План рахується, щойно відкрита картка; затвердити — коли є очки.
            Pl(nameof(GameSession.PreviewBuildPlan), VillagePlaces.HeroTentId, UxHomeCheck.Source),
            Pl(nameof(GameSession.CommitBuildPlan), VillagePlaces.HeroTentId, UxHomeCheck.Source),

            // Головна кнопка фази (HP-2)
            Of(nameof(GameSession.ConfirmMorning), UxHomeKind.Hud, PhaseButton),
            Of(nameof(GameSession.AdvanceDay), UxHomeKind.Hud, PhaseButton),

            // Сервіс: титул і Esc
            Of(nameof(GameSession.NewGame), UxHomeKind.Service, "title"),
            Of(nameof(GameSession.PreloadSlot), UxHomeKind.Service, "title"),
            Of(nameof(GameSession.ContinueGame), UxHomeKind.Service, "title"),
            Of(nameof(GameSession.SaveState), UxHomeKind.Service, "escape"),
            Of(nameof(GameSession.SetHitRule), UxHomeKind.Service, "escape"),
            Of(nameof(GameSession.NewTrainingBattle), UxHomeKind.Service, "title"),

            // Події (№18.1): стартують зі світу, Esc у них працює
            Of(nameof(GameSession.SetProtagonistName), UxHomeKind.Event, "creation"),
            Of(nameof(GameSession.SetProtagonistGender), UxHomeKind.Event, "creation"),
            Of(nameof(GameSession.SetProtagonistBackground), UxHomeKind.Event, "creation"),
            Of(nameof(GameSession.ConfirmCreation), UxHomeKind.Event, "creation"),
            Of(nameof(GameSession.AdvanceScene), UxHomeKind.Event, "scene"),
            Of(nameof(GameSession.ChooseSceneOption), UxHomeKind.Event, "scene"),
            Of(nameof(GameSession.AcknowledgeSummary), UxHomeKind.Event, "summary"),
            Of(nameof(GameSession.CombatMove), UxHomeKind.Event, "battle"),
            Of(nameof(GameSession.CombatAttack), UxHomeKind.Event, "battle"),
            Of(nameof(GameSession.CombatAttackObject), UxHomeKind.Event, "battle"),
            Of(nameof(GameSession.CombatAutoResolve), UxHomeKind.Event, "battle"),
            Of(nameof(GameSession.CombatEndTurn), UxHomeKind.Event, "battle"),
            Of(nameof(GameSession.CombatEnterOverwatch), UxHomeKind.Event, "battle"),
            Of(nameof(GameSession.CombatRetreat), UxHomeKind.Event, "battle"),
            Of(nameof(GameSession.CombatStabilize), UxHomeKind.Event, "battle"),
            Of(nameof(GameSession.CombatUseAbility), UxHomeKind.Event, "battle"),
            Of(nameof(GameSession.DecideSurrender), UxHomeKind.Event, "battle"),
            Of(nameof(GameSession.PreviewAttack), UxHomeKind.Event, "battle"),
            Of(nameof(GameSession.PreviewDamage), UxHomeKind.Event, "battle"),
            Of(nameof(GameSession.PreviewHitChance), UxHomeKind.Event, "battle"),
            Of(nameof(GameSession.PreviewMovePath), UxHomeKind.Event, "battle"),
            Of(nameof(GameSession.PreviewOverwatchCone), UxHomeKind.Event, "battle"),

            // Не дії гравця
            Of(nameof(GameSession.OfferMyroslavaEveningScene), UxHomeKind.Auto, "evening"),
            Of(nameof(GameSession.OfferZakharCouncilScene), UxHomeKind.Auto, "evening"),
            Of(nameof(GameSession.CombatAiStepOneAction), UxHomeKind.Auto, "battle"),

            // Ще не у світі — кроки плану (`docs/ROADMAP.md`, трек U)
            Of(nameof(GameSession.ResolveIncident), UxHomeKind.Gap, "U9"),
            Of(nameof(GameSession.ConfirmEvening), UxHomeKind.Gap, "U10"),
            Of(nameof(GameSession.SetPatrol), UxHomeKind.Gap, "U10"),
            Of(nameof(GameSession.AdvanceNight), UxHomeKind.Gap, "U10"),
            Of(nameof(GameSession.ReactToCrisis), UxHomeKind.Gap, "U11"),
            Of(nameof(GameSession.ResolveFinale), UxHomeKind.Gap, "U11"),
            Of(nameof(GameSession.ResolveDungeonRoom), UxHomeKind.Gap, "U12"),
            Of(nameof(GameSession.ResolveDungeonEvent), UxHomeKind.Gap, "U12"),
            Of(nameof(GameSession.ResolveDungeonParley), UxHomeKind.Gap, "U12"),
            Of(nameof(GameSession.PushDeeper), UxHomeKind.Gap, "U12"),
            Of(nameof(GameSession.ExtractDungeon), UxHomeKind.Gap, "U12"),
            Of(nameof(GameSession.AbandonDungeon), UxHomeKind.Gap, "U12"),
        };

        /// <summary>
        /// Станції без дієслова, що показують справжній стан словами (UX-10
        /// дозволяє «справжній стан»): що в коморі, хто на ліжках, кого пам'ятає
        /// меморіал. Кожна — з причиною, чому стан справжній.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> StateOnlyStations = new Dictionary<string, string>
        {
            { "pantry", "відкриті числа запасів (R17)" },
            { "infirmary_beds", "хто поранений чи відпочиває (статуси ростера)" },
            { "temple_memorial", "загиблі й ті, хто пішов (статуси ростера)" },
            { "watch_wall", "готовність громади словом (публічний вид готовності)" },
            { "tavern_tables", "хто хоче поговорити (відкриті глави арок)" },
        };

        private static List<UxHome> _all;

        /// <summary>Усі доми: таблиця вище плюс «Поставити/Зняти» на кожній станції з постом.</summary>
        public static IReadOnlyList<UxHome> All
        {
            get
            {
                if (_all != null) return _all;
                var all = new List<UxHome>(Fixed);
                var posts = new List<StationDef>();
                foreach (var b in BuildingCatalog.All) posts.AddRange(b.Stations);
                posts.AddRange(BuildingCatalog.OpenAirStations);
                foreach (var st in posts)
                {
                    if (string.IsNullOrEmpty(st.PostId)) continue;
                    all.Add(St(nameof(GameSession.Assign), st.Id, UxHomeCheck.Source));
                    all.Add(St(nameof(GameSession.Unassign), st.Id, UxHomeCheck.Source));
                }
                _all = all;
                return _all;
            }
        }

        public static List<UxHome> Of(string command)
        {
            var result = new List<UxHome>();
            foreach (var h in All)
                if (h.Command == command) result.Add(h);
            return result;
        }

        /// <summary>Чи є в команди дім у світі (станція чи місце).</summary>
        public static bool InWorld(string command)
        {
            foreach (var h in All)
                if (h.Command == command && h.InWorld) return true;
            return false;
        }

        /// <summary>Команди, у яких немає жодного дому, крім кроку плану (<see cref="UxHomeKind.Gap"/>).</summary>
        public static SortedSet<string> GapCommands()
        {
            var gaps = new SortedSet<string>();
            foreach (var h in All)
                if (h.Kind == UxHomeKind.Gap) gaps.Add(h.Command);
            foreach (var h in All)
                if (h.Kind != UxHomeKind.Gap) gaps.Remove(h.Command);
            return gaps;
        }

        /// <summary>Чи має станція дієслово (дім якоїсь команди).</summary>
        public static bool StationHasVerb(string stationId)
        {
            foreach (var h in All)
                if (h.Kind == UxHomeKind.Station && h.Where == stationId) return true;
            return false;
        }

        private static UxHome St(string command, string stationId, UxHomeCheck check = UxHomeCheck.DayOne) =>
            new UxHome(command, UxHomeKind.Station, stationId, check);

        private static UxHome Pl(string command, string placeId, UxHomeCheck check = UxHomeCheck.DayOne) =>
            new UxHome(command, UxHomeKind.Place, placeId, check);

        private static UxHome Of(string command, UxHomeKind kind, string where) =>
            new UxHome(command, kind, where, UxHomeCheck.Source);
    }
}
