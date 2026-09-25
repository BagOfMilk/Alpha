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
            stage.Apply(new VillageStageData
            {
                Phase = view.Phase == Game.Core.Loop.DayPhase.Night ? StagePhase.Night : StagePhase.Day,
                Tier = view.Tier,
                Patrolling = view.IsPatrolling,
                Posts = BuildPosts(session.GetRosterView()),
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

        private static List<StagePost> BuildPosts(RosterView roster)
        {
            var occupants = new Dictionary<string, string>();
            if (roster?.Companions != null)
                foreach (var c in roster.Companions)
                    if (c != null && !string.IsNullOrEmpty(c.AssignedSlotId) && !occupants.ContainsKey(c.AssignedSlotId))
                        occupants[c.AssignedSlotId] = c.Id;

            var posts = new List<StagePost>(PostIds.Length);
            foreach (var id in PostIds)
            {
                string occupant;
                occupants.TryGetValue(id, out occupant);
                posts.Add(new StagePost { PostId = id, OccupantId = occupant });
            }
            return posts;
        }

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
