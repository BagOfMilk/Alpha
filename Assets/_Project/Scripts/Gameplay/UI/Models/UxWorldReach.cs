using System.Collections.Generic;
using Game.Core.Session;
using Game.Gameplay.Walk;

namespace Game.Gameplay.UI
{
    /// <summary>Місце, з якого відкривається панель: у селі (крок 1) чи станція в кімнаті (крок 2 — будівля, станція).</summary>
    public sealed class UxWorldEntry
    {
        /// <summary>Id місця в селі (<see cref="VillagePlaces"/>): «plot:…», «building:…», «station:…», «person:…».</summary>
        public string PlaceId;
        public UxPanelId Panel;
        public string Context;
        /// <summary>Кроки від села до відкритої панелі: 1 — місце в селі, 2 — будівля → станція.</summary>
        public int Depth;
        /// <summary>Будівля, в якій стоїть станція, або null.</summary>
        public string BuildingId;
    }

    /// <summary>
    /// Що зараз досяжне зі світу (Поправка №18.1): ті самі місця, що будує
    /// сцена (<see cref="VillagePlaces"/>, <see cref="BuildingCatalog"/>,
    /// <see cref="Interiors"/>), але без геометрії. Панелі огляду (C, J, N, F10)
    /// сюди не входять — це не світ. Читають охоронці й headless-бот; згодом —
    /// автотур `-autoplay-world`.
    /// </summary>
    public static class UxWorldReach
    {
        public static List<UxWorldEntry> Now(GameSession s)
        {
            var result = new List<UxWorldEntry>();
            if (s == null) return result;
            var city = s.GetCityView();

            foreach (var building in BuildingCatalog.All)
            {
                if (!BuildingCatalog.HasPlot(building.BuildingId)) continue;
                bool built;
                int stage = UxBricks.Stage(city, building.BuildingId, out built);
                var place = VillagePlaces.ForBuilding(new PlotAnchor { BuildingId = building.BuildingId }, built ? 5 : stage);
                if (place.Kind == PlaceKind.Building && place.Panel == UxPanelId.None)
                {
                    foreach (var st in building.Stations)
                        result.Add(new UxWorldEntry
                        {
                            PlaceId = VillagePlaces.StationPrefix + st.Id, Panel = st.Panel, Context = st.Id,
                            Depth = 2, BuildingId = building.BuildingId
                        });
                    continue;
                }
                result.Add(new UxWorldEntry { PlaceId = place.Id, Panel = place.Panel, Context = place.TargetId, Depth = 1, BuildingId = building.BuildingId });
            }

            foreach (var st in BuildingCatalog.OpenAirStations)
                result.Add(new UxWorldEntry { PlaceId = VillagePlaces.StationPrefix + st.Id, Panel = st.Panel, Context = st.Id, Depth = 1 });

            result.Add(new UxWorldEntry { PlaceId = VillagePlaces.NoticeBoardId, Panel = UxPanelId.NoticeBoard, Context = VillagePlaces.NoticeBoardId, Depth = 1 });
            result.Add(new UxWorldEntry { PlaceId = VillagePlaces.TrainingGroundId, Panel = UxPanelId.TrainingGround, Context = VillagePlaces.TrainingGroundId, Depth = 1 });

            var roster = s.GetRosterView();
            if (roster?.Companions != null)
                foreach (var c in roster.Companions)
                    if (VillagePeople.IsInVillage(c))
                        result.Add(new UxWorldEntry { PlaceId = VillagePlaces.PersonPrefix + c.Id, Panel = UxPanelId.Talk, Context = c.Id, Depth = 1 });
            return result;
        }

        /// <summary>Ідентифікатори місць, що існують у селі зараз (для посилань «Показати в селі»).</summary>
        public static HashSet<string> VillagePlaceIds(GameSession s)
        {
            var ids = new HashSet<string>();
            if (s == null) return ids;
            var city = s.GetCityView();
            foreach (var building in BuildingCatalog.All)
            {
                if (!BuildingCatalog.HasPlot(building.BuildingId)) continue;
                bool built;
                int stage = UxBricks.Stage(city, building.BuildingId, out built);
                ids.Add(VillagePlaces.ForBuilding(new PlotAnchor { BuildingId = building.BuildingId }, built ? 5 : stage).Id);
            }
            foreach (var st in BuildingCatalog.OpenAirStations) ids.Add(VillagePlaces.StationPrefix + st.Id);
            ids.Add(VillagePlaces.NoticeBoardId);
            ids.Add(VillagePlaces.TrainingGroundId);
            var roster = s.GetRosterView();
            if (roster?.Companions != null)
                foreach (var c in roster.Companions)
                    if (VillagePeople.IsInVillage(c)) ids.Add(VillagePlaces.PersonPrefix + c.Id);
            return ids;
        }
    }
}
