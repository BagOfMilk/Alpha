using System.Collections.Generic;
using Game.Core.Session;
using Game.Core.Session.Views;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Годує <see cref="VillageStage"/> знімком із живих View-шарів
    /// <c>GameSession</c> — те, що коментар класу <see cref="VillageStage"/>
    /// обіцяв "коли GameSession приїде з трунку" (пакет E1b). Викликається
    /// <see cref="GameShell"/> ЧЕРЕЗ РЕФЛЕКСІЮ (той самий прийом, що й
    /// Editor/GameSceneBuilder.cs → Game.Gameplay.Editor.BattleArenaBuilder,
    /// §5 TEST_BUILD.md): <see cref="VillageStage"/>/<see cref="VillageStageData"/>
    /// самі глибокого рушія не чіпають, але лежать у файлі, виключеному з
    /// <c>Game.Gameplay.Lint</c> (Light/Camera/Renderer/Shader.Find — та сама
    /// причина, що вже виключила сам VillageStage.cs), тож GameShell.cs, який
    /// ЛІНТУЄТЬСЯ, не може посилатися на цей тип напряму — інакше лінт-проєкт
    /// не зібрався б узагалі (тип не в його компіляції).
    /// </summary>
    public static class VillageStageBridge
    {
        /// <summary>Вісім слотів бази (Core/DefaultContent.cs AllSlots) — постів у сцені сьогодні сім (без lab_station), зайвий запис просто ігнорується VillageStage.Apply.</summary>
        private static readonly string[] PostIds =
        {
            "council_seat", "storehouse_dock", "infirmary_bed",
            "settlement_market", "settlement_farms", "workshop_bench",
            "scouting_post", "lab_station"
        };

        private static VillageStage _cachedStage;

        /// <summary>Один кадр села зі стану сесії — викликається після кожної команди, що рухає фазу/пости/стройку.</summary>
        public static void Feed(GameSession session)
        {
            if (session == null) return;
            var stage = FindStage();
            if (stage == null) return;

            var view = session.CurrentView;
            // Ранок (і вільна гра) — уже день для гравця, хоча конвеєр ще в
            // нічній фазі до «Почати день» (та сама причина, що в шапці
            // GameShell.DrawTopBar). Без цього на прогулянці вранці село
            // стояло темне, як уночі, і без людей на постах.
            bool morning = session.State == SessionState.Morning || session.State == SessionState.FreePlay;
            stage.Apply(new VillageStageData
            {
                Phase = morning || view.Phase != Game.Core.Loop.DayPhase.Night ? StagePhase.Day : StagePhase.Night,
                Tier = view.Tier,
                Patrolling = view.IsPatrolling,
                Posts = WithLooks(session, BuildPosts(session.GetRosterView())),
                Idle = WithLooks(session, BuildIdle(session.GetRosterView())),
                Plots = BuildPlots(session.GetCityView()),
                Incidents = BuildIncidents(session.LastDayReport)
            });
        }

        private static VillageStage FindStage()
        {
            if (_cachedStage != null) return _cachedStage;
            _cachedStage = Object.FindAnyObjectByType<VillageStage>();
            return _cachedStage;
        }

        /// <summary>Образ і надіте кожного, хто в селі (Поправка №19) — постать із модульного набору.</summary>
        private static List<StagePost> WithLooks(GameSession session, List<StagePost> posts)
        {
            foreach (var p in posts)
                if (!string.IsNullOrEmpty(p.OccupantId)) { p.Look = session.GetAppearance(p.OccupantId); p.Equip = EquipOf(session, p.OccupantId); }
            return posts;
        }

        private static List<StageIdlePerson> WithLooks(GameSession session, List<StageIdlePerson> idle)
        {
            foreach (var p in idle)
                if (!string.IsNullOrEmpty(p.CompanionId)) { p.Look = session.GetAppearance(p.CompanionId); p.Equip = EquipOf(session, p.CompanionId); }
            return idle;
        }

        // Лист персонажа рахує агрегатор статів — не щокадру: надіте змінюється лише подією журналу
        // (equip.changed), тож кеш живе, доки не виросте журнал або не зміниться сесія.
        private static GameSession _equipSession;
        private static int _equipLogCount = -1;
        private static readonly Dictionary<string, List<string>> _equipCache = new Dictionary<string, List<string>>();

        private static List<string> EquipOf(GameSession session, string companionId)
        {
            if (session != _equipSession || session.DayLog.Count != _equipLogCount)
            {
                _equipCache.Clear();
                _equipSession = session;
                _equipLogCount = session.DayLog.Count;
            }
            List<string> keys;
            if (_equipCache.TryGetValue(companionId, out keys)) return keys;
            var sheet = session.GetCharacterSheet(companionId);
            keys = Game.Gameplay.UI.InventoryModel.VisualKeys(sheet != null ? sheet.Equipment : null);
            _equipCache[companionId] = keys;
            return keys;
        }

        private static List<StagePost> BuildPosts(RosterView roster)
        {
            var occupants = new Dictionary<string, string>();
            if (roster?.Companions != null)
                foreach (var c in roster.Companions)
                    if (c != null && !string.IsNullOrEmpty(c.AssignedSlotId) && !occupants.ContainsKey(c.AssignedSlotId)
                        && Game.Gameplay.Walk.VillagePeople.IsInVillage(c))
                        occupants[c.AssignedSlotId] = c.Id;

            var posts = new List<StagePost>(PostIds.Length);
            foreach (var id in PostIds)
            {
                string occupant;
                occupants.TryGetValue(id, out occupant);
                posts.Add(new StagePost { PostId = id, OccupantId = occupant, OccupantFemale = IsFemale(occupant) });
            }
            return posts;
        }

        /// <summary>
        /// Хто стоїть біля вогнища Віча — та сама розстановка, що й місця
        /// розмови (<see cref="Game.Gameplay.Walk.VillagePeople.Arrange"/>): фігура і місце збігаються.
        /// </summary>
        private static List<StageIdlePerson> BuildIdle(RosterView roster)
        {
            var figures = new Dictionary<string, Game.Gameplay.Walk.WalkPoint>();
            foreach (var id in PostIds)
                if (id != "lab_station") figures[id] = default(Game.Gameplay.Walk.WalkPoint);
            var spots = new List<Game.Gameplay.Walk.WalkPoint>();
            for (int i = 0; i < Game.Gameplay.Walk.VillagePeople.IdleSpotCount; i++) spots.Add(default(Game.Gameplay.Walk.WalkPoint));
            var idle = new List<StageIdlePerson>();
            foreach (var spot in Game.Gameplay.Walk.VillagePeople.Arrange(roster, figures, spots))
                if (!spot.AtPost)
                    idle.Add(new StageIdlePerson { Index = spot.IdleIndex, CompanionId = spot.CompanionId, Female = IsFemale(spot.CompanionId) });
            return idle;
        }

        private static bool IsFemale(string companionId) =>
            !string.IsNullOrEmpty(companionId) &&
            Game.Gameplay.UI.ScreenText.SubjectGender(companionId, Game.Core.Characters.Creation.Gender.Male)
                == Game.Core.Characters.Creation.Gender.Female;

        private static List<StagePlot> BuildPlots(CityView city)
        {
            var plots = new List<StagePlot>();
            if (city?.Built != null)
                foreach (var b in city.Built)
                    if (b != null) plots.Add(new StagePlot { PlotId = b.Id, Stage = 5 });
            if (city?.InProgress != null)
                foreach (var b in city.InProgress)
                    if (b != null) plots.Add(new StagePlot { PlotId = b.Id, Stage = b.StageOf });
            return plots;
        }

        private static List<StageIncidentMark> BuildIncidents(DayReportView report)
        {
            var marks = new List<StageIncidentMark>();
            if (report?.Incidents == null) return marks;
            foreach (var outcome in report.Incidents)
                marks.Add(new StageIncidentMark
                {
                    DomainTag = outcome.DomainTag,
                    Band = outcome.Band,
                    WasCrisis = outcome.WasCrisis
                });
            return marks;
        }
    }
}
