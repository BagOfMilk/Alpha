using System.Collections.Generic;

namespace Game.Core.Base
{
    /// <summary>
    /// Каталог будівель — US-7.1 GDD цілком (Поправка №6.1).
    ///
    /// Ціни і строки — ПЛЕЙСХОЛДЕР. Розбивка за ціною — із самого GDD: ядро-будівлі
    /// будуються за золото, спеціальні — за золото і будівельний компонент.
    ///
    /// <c>DisplayName</c> — Core-контентное поле, гравець його НЕ бачить (R7,
    /// CLAUDE.md): показ іде через <c>UkrainianText.Get("building.&lt;id&gt;", …)</c>
    /// (Gameplay/Text/UkrainianText.cs). Раніше тут лежав російський текст —
    /// фікс-ревью (раунд 2, blocker): 3D-мітка ділянки читала це поле напряму
    /// й показувала гравцю "Мастерская" замість "Майстерня". Поле лишили —
    /// прибрати цілком дорожче, ніж переписати — але тепер воно теж українською,
    /// щоб жоден інший шлях читання DisplayName більше не міг протягти РФ-текст.
    /// </summary>
    public static class DefaultBuildings
    {
        public const string Infirmary = "infirmary";
        public const string Workshop = "workshop";
        public const string Storehouse = "storehouse";
        public const string CouncilHall = "council_hall";
        public const string Market = "market";
        public const string Tavern = "tavern";
        public const string Temple = "temple";
        public const string Fortifications = "fortifications";
        public const string Armory = "armory";
        public const string Laboratory = "laboratory";

        /// <summary>
        /// З чим громада зустрічає гравця. ПРОПОЗИЦІЯ, не рішення власника:
        /// у хутора вже є старший і амбар, решта будується (Поправка №6.1).
        /// </summary>
        public static readonly string[] StartingSet = { CouncilHall, Storehouse };

        public static IEnumerable<BuildingDefinition> All()
        {
            // ---- Ядро: будує гравець, платить золотом ----
            yield return new BuildingDefinition
            {
                Id = Infirmary, DisplayName = "Лазарет",
                GoldCost = 30, Days = 4,
                Effect = BuildingEffect.OpensPost, OpensSlotId = "infirmary_bed"
            };
            yield return new BuildingDefinition
            {
                Id = Workshop, DisplayName = "Майстерня",
                GoldCost = 30, Days = 4,
                Effect = BuildingEffect.OpensPost, OpensSlotId = "workshop_bench"
            };
            yield return new BuildingDefinition
            {
                Id = Storehouse, DisplayName = "Склад",
                GoldCost = 25, Days = 3,
                Effect = BuildingEffect.OpensPost, OpensSlotId = "storehouse_dock"
            };
            yield return new BuildingDefinition
            {
                Id = CouncilHall, DisplayName = "Зала ради",
                GoldCost = 40, Days = 5,
                Effect = BuildingEffect.CouncilActions, OpensSlotId = "council_seat"
            };

            // ---- Спеціальні: золото + будівельний компонент ----
            yield return new BuildingDefinition
            {
                Id = Market, DisplayName = "Ринок",
                GoldCost = 40, MaterialsCost = 4, Days = 6,
                Effect = BuildingEffect.OpensPost, OpensSlotId = "settlement_market"
            };
            yield return new BuildingDefinition
            {
                Id = Tavern, DisplayName = "Таверна",
                GoldCost = 35, MaterialsCost = 3, Days = 5,
                Effect = BuildingEffect.TavernArrivals
            };
            yield return new BuildingDefinition
            {
                Id = Temple, DisplayName = "Храм",
                GoldCost = 50, MaterialsCost = 6, Days = 8,
                Effect = BuildingEffect.TempleAura
            };
            yield return new BuildingDefinition
            {
                Id = Fortifications, DisplayName = "Укріплення",
                GoldCost = 45, MaterialsCost = 8, Days = 8,
                Effect = BuildingEffect.Fortifications
            };
            yield return new BuildingDefinition
            {
                Id = Armory, DisplayName = "Збройня",
                GoldCost = 60, MaterialsCost = 6, Days = 8,
                Effect = BuildingEffect.Awaiting, AwaitingNote = "ждёт снаряжения (Э4)"
            };
            yield return new BuildingDefinition
            {
                Id = Laboratory, DisplayName = "Лабораторія",
                GoldCost = 80, MaterialsCost = 10, Days = 10, QuestOnly = true,
                Effect = BuildingEffect.Awaiting, AwaitingNote = "приходит по квесту, ждёт аугментов (Э6)"
            };
        }

        public static BuildingDefinition Get(string id)
        {
            foreach (var b in All())
                if (b.Id == id) return b;
            return null;
        }
    }
}
