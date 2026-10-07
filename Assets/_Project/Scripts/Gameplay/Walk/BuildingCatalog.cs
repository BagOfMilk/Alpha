using System.Collections.Generic;
using Game.Core.Base;
using Game.Gameplay.UI;

namespace Game.Gameplay.Walk
{
    /// <summary>
    /// Станція — гаряча точка місця: всередині будівлі або просто неба
    /// (docs/UX_DESIGN.md §4.1, §4.6). Клік чи E відкриває панель
    /// <see cref="Panel"/> з контекстом <see cref="Id"/>.
    /// </summary>
    public sealed class StationDef
    {
        public readonly string Id;
        public readonly string LabelKey;
        public readonly UxPanelId Panel;
        /// <summary>Пост, що стоїть на цій станції (жетон працівника), або null.</summary>
        public readonly string PostId;

        public StationDef(string id, UxPanelId panel, string postId = null)
        {
            Id = id;
            LabelKey = "ux.station." + id;
            Panel = panel;
            PostId = postId;
        }
    }

    /// <summary>Будівля як місце: чи в неї заходять, чий пост біля дверей, які станції всередині.</summary>
    public sealed class BuildingPlace
    {
        public readonly string BuildingId;
        /// <summary>У будівлю заходять (є інтер'єр); інакше клік відкриває лише картку (UX-10: жодних мертвих входів).</summary>
        public readonly bool Enterable;
        /// <summary>Пост працівника будівлі (фігура стоїть біля дверей), або null.</summary>
        public readonly string PostId;
        public readonly IReadOnlyList<StationDef> Stations;

        public BuildingPlace(string buildingId, bool enterable, string postId, params StationDef[] stations)
        {
            BuildingId = buildingId;
            Enterable = enterable;
            PostId = postId;
            Stations = stations ?? new StationDef[0];
        }
    }

    /// <summary>
    /// Будівля ↔ пост ↔ станції (docs/UX_DESIGN.md §4.6, §4.8: пост = станція
    /// працівника в своїй будівлі). Чистий C#: і сцена, і панелі, і тести
    /// читають один каталог.
    /// </summary>
    public static class BuildingCatalog
    {
        /// <summary>Не більше п'яти станцій на кімнату — бюджет сірого каркасу (§7.3).</summary>
        public const int MaxStations = 5;

        public const string VecheStation = "veche";
        public const string FarmsStation = "farms";
        public const string MusterStation = "muster";

        private static readonly BuildingPlace[] Entries =
        {
            new BuildingPlace(DefaultBuildings.Watch, true, null,
                new StationDef("watch_wall", UxPanelId.Station)),
            new BuildingPlace(DefaultBuildings.Storehouse, true, "storehouse_dock",
                new StationDef("storehouse_dock", UxPanelId.Station, "storehouse_dock"),
                new StationDef("stash", UxPanelId.Stash),
                new StationDef("pantry", UxPanelId.Station)),
            new BuildingPlace(DefaultBuildings.Infirmary, true, "infirmary_bed",
                new StationDef("infirmary_bed", UxPanelId.Station, "infirmary_bed"),
                new StationDef("infirmary_beds", UxPanelId.Station)),
            new BuildingPlace(DefaultBuildings.Workshop, true, "workshop_bench",
                new StationDef("workshop_bench", UxPanelId.Station, "workshop_bench"),
                new StationDef("workbench", UxPanelId.Workbench)),
            new BuildingPlace(DefaultBuildings.Market, true, "settlement_market",
                new StationDef("settlement_market", UxPanelId.Station, "settlement_market"),
                new StationDef("market_traders", UxPanelId.Station)),
            new BuildingPlace(DefaultBuildings.Tavern, true, null,
                new StationDef("tavern_tables", UxPanelId.Station)),
            new BuildingPlace(DefaultBuildings.Temple, true, null,
                new StationDef("temple_memorial", UxPanelId.Station)),
            new BuildingPlace(DefaultBuildings.CouncilHall, true, null,
                new StationDef("council_table", UxPanelId.CouncilTable)),
            new BuildingPlace(DefaultBuildings.Fortifications, false, null),
            new BuildingPlace(DefaultBuildings.Armory, false, null),
            new BuildingPlace(DefaultBuildings.Laboratory, false, null),
        };

        /// <summary>Станції просто неба (Віче, Поле, Застава) — без будівлі.</summary>
        private static readonly StationDef[] OpenAir =
        {
            new StationDef(VecheStation, UxPanelId.Veche, "council_seat"),
            new StationDef(FarmsStation, UxPanelId.Station, "settlement_farms"),
            new StationDef(MusterStation, UxPanelId.Muster, "scouting_post"),
        };

        public static IReadOnlyList<BuildingPlace> All => Entries;

        /// <summary>
        /// Чи має будівля ділянку в селі (<c>GameSceneBuilder.Plots</c>). Лабораторії
        /// ділянки немає: її механіки ще немає (UX-10), тож і місця в селі немає.
        /// Охоронець звіряє з кодом сцени (<c>WorldFirstTests</c>).
        /// </summary>
        public static bool HasPlot(string buildingId) =>
            Get(buildingId) != null && buildingId != DefaultBuildings.Laboratory;

        public static IReadOnlyList<StationDef> OpenAirStations => OpenAir;

        /// <summary>Будівля за id; null — такої в каталозі немає.</summary>
        public static BuildingPlace Get(string buildingId)
        {
            foreach (var e in Entries)
                if (e.BuildingId == buildingId) return e;
            return null;
        }

        /// <summary>Станція за id — у будь-якій будівлі або просто неба; null — немає.</summary>
        public static StationDef FindStation(string stationId)
        {
            foreach (var e in Entries)
                foreach (var s in e.Stations)
                    if (s.Id == stationId) return s;
            foreach (var s in OpenAir)
                if (s.Id == stationId) return s;
            return null;
        }

        /// <summary>Будівля, в якій стоїть станція; null — станція просто неба або невідома.</summary>
        public static BuildingPlace BuildingOfStation(string stationId)
        {
            foreach (var e in Entries)
                foreach (var s in e.Stations)
                    if (s.Id == stationId) return e;
            return null;
        }

        /// <summary>Станція, на якій стоїть пост (жетон працівника); null — пост без станції.</summary>
        public static StationDef StationOfPost(string postId)
        {
            foreach (var e in Entries)
                foreach (var s in e.Stations)
                    if (s.PostId == postId) return s;
            foreach (var s in OpenAir)
                if (s.PostId == postId) return s;
            return null;
        }
    }
}
