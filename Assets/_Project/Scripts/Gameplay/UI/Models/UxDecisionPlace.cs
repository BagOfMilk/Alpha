using System.Collections.Generic;
using Game.Core.Session.Views;
using Game.Gameplay.Walk;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Де в селі сталася подія, що чекає рішення (Поправка №18.3: камера летить до
    /// місця, над ним «!», картка з «Подумати»). Ключ — пост, якого стосується подія
    /// (<see cref="PendingOfferView.RelevantPositionId"/>): тег домену в ядрі
    /// неточний (нічний підпал — «ночь», а стосується майстерні). Пост → станція →
    /// будівля: зведена — двері, ні — ділянка; станція просто неба — вона сама.
    /// Без рушія: тестується headless.
    /// </summary>
    public static class UxDecisionPlace
    {
        /// <summary>
        /// Винятки з правила «пост → місце»: авангард з перевалу (вузол 1) формально
        /// стосується ради, але стоїть біля воріт — на Заставі.
        /// </summary>
        private static readonly Dictionary<string, string> Overrides = new Dictionary<string, string>
        {
            { "pass_vanguard", VillagePlaces.StationPrefix + BuildingCatalog.MusterStation },
        };

        /// <summary>Місце події; без поста чи поза каталогом — Віче (рада вирішує все, чому немає окремого місця).</summary>
        public static string Of(PendingOfferView offer, CityView city)
        {
            if (offer == null) return null;
            string id;
            if (offer.IncidentId != null && Overrides.TryGetValue(offer.IncidentId, out id)) return id;
            return OfPost(offer.RelevantPositionId, city) ?? VillagePlaces.StationPrefix + BuildingCatalog.VecheStation;
        }

        /// <summary>Місце поста в селі зараз: двері зведеної будівлі, ділянка незведеної чи станція просто неба; null — немає.</summary>
        public static string OfPost(string postId, CityView city)
        {
            if (string.IsNullOrEmpty(postId)) return null;
            var station = BuildingCatalog.StationOfPost(postId);
            if (station == null) return null;
            var building = BuildingCatalog.BuildingOfStation(station.Id);
            if (building == null) return VillagePlaces.StationPrefix + station.Id;
            if (!BuildingCatalog.HasPlot(building.BuildingId)) return null;
            bool built;
            int stage = UxBricks.Stage(city, building.BuildingId, out built);
            return VillagePlaces.ForBuilding(new PlotAnchor { BuildingId = building.BuildingId }, built ? 5 : stage).Id;
        }
    }
}
