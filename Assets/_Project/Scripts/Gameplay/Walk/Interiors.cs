using System.Collections.Generic;

namespace Game.Gameplay.Walk
{
    /// <summary>
    /// Шаблон сірої кімнати (docs/UX_DESIGN.md §4.10, варіант Б; §7.3): одна
    /// кімната в тій самій сцені, осторонь села, яку оболонка налаштовує під
    /// будівлю — підпис, до п'яти станцій, двері виходу. Не одна кімната на
    /// будівлю: бюджет сірого каркасу. Координати світові (площина XZ), ті
    /// самі читає і збирач сцени, і герой, і тести.
    /// </summary>
    public static class Interiors
    {
        /// <summary>Центр кімнати у світі — далеко від села (село займає x −11..12), щоб кадр кімнати не чіпляв хат.</summary>
        public const float OriginX = 40f;
        public const float OriginZ = 0f;

        /// <summary>Половина розміру підлоги.</summary>
        public const float HalfWidth = 4f;
        public const float HalfDepth = 3f;

        /// <summary>П'ять місць під станції — вздовж задньої і бічних стін, прохід від дверей вільний.</summary>
        private static readonly WalkPoint[] SlotOffsets =
        {
            new WalkPoint(-2.4f, 1.9f),
            new WalkPoint(0f, 2.2f),
            new WalkPoint(2.4f, 1.9f),
            new WalkPoint(-3.0f, -0.4f),
            new WalkPoint(3.0f, -0.4f),
        };

        /// <summary>Розмір станції-п'єдесталу (сторона квадрата).</summary>
        public const float StationSize = 0.8f;

        public const string ExitPlaceId = "exit";

        public static int SlotCount => SlotOffsets.Length;

        /// <summary>Центр слоту станції <paramref name="index"/> у світі.</summary>
        public static WalkPoint Slot(int index)
        {
            var o = SlotOffsets[index];
            return new WalkPoint(OriginX + o.X, OriginZ + o.Z);
        }

        /// <summary>
        /// Де стати, щоб працювати зі станцією: перед п'єдесталом, з боку
        /// центру кімнати (сам п'єдестал — перешкода, до нього не підійти впритул).
        /// </summary>
        public static WalkPoint Approach(int index)
        {
            var slot = Slot(index);
            float dx = OriginX - slot.X, dz = OriginZ - slot.Z;
            float len = (float)System.Math.Sqrt(dx * dx + dz * dz);
            if (len < 0.001f) return slot;
            float step = StationSize * 0.5f + 0.55f;
            return new WalkPoint(slot.X + dx / len * step, slot.Z + dz / len * step);
        }

        /// <summary>Двері виходу — посередині передньої стіни.</summary>
        public static WalkPoint Door => new WalkPoint(OriginX, OriginZ - HalfDepth + 0.35f);

        /// <summary>Де герой з'являється, увійшовши: крок усередину від дверей.</summary>
        public static WalkPoint Spawn => new WalkPoint(OriginX, OriginZ - HalfDepth + 1.1f);

        public static float MinX => OriginX - HalfWidth;
        public static float MaxX => OriginX + HalfWidth;
        public static float MinZ => OriginZ - HalfDepth;
        public static float MaxZ => OriginZ + HalfDepth;

        /// <summary>
        /// Місця кімнати для будівлі: станції по слотах (у порядку каталогу) і
        /// двері виходу. Порожній список — у будівлю не заходять.
        /// </summary>
        public static List<WalkPlace> PlacesFor(string buildingId)
        {
            var places = new List<WalkPlace>();
            var building = BuildingCatalog.Get(buildingId);
            if (building == null || !building.Enterable) return places;
            int n = building.Stations.Count < SlotCount ? building.Stations.Count : SlotCount;
            for (int i = 0; i < n; i++)
            {
                var station = building.Stations[i];
                var slot = Slot(i);
                var at = Approach(i);
                var place = VillagePlaces.Station(station, at.X, at.Z, inside: true);
                place.LabelX = slot.X;
                place.LabelZ = slot.Z;
                places.Add(place);
            }
            var door = Door;
            places.Add(VillagePlaces.Exit(buildingId, door.X, door.Z));
            return places;
        }
    }
}
