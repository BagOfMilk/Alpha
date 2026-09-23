using System.Collections.Generic;

namespace Game.Core.Base
{
    /// <summary>
    /// Каталог зданий — US-7.1 GDD целиком (Поправка №6.1).
    ///
    /// Цены и сроки — ПЛЕЙСХОЛДЕР. Разбивка по цене — из самого GDD: ядро-здания
    /// строятся за золото, специальные — за золото и строительный компонент.
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
        /// С чем община встречает игрока. ПРЕДЛОЖЕНИЕ, не решение владельца:
        /// у хутора уже есть старший и амбар, остальное строится (Поправка №6.1).
        /// </summary>
        public static readonly string[] StartingSet = { CouncilHall, Storehouse };

        public static IEnumerable<BuildingDefinition> All()
        {
            // ---- Ядро: строит игрок, платит золотом ----
            yield return new BuildingDefinition
            {
                Id = Infirmary, DisplayName = "Лазарет",
                GoldCost = 30, Days = 4,
                Effect = BuildingEffect.OpensPost, OpensSlotId = "infirmary_bed"
            };
            yield return new BuildingDefinition
            {
                Id = Workshop, DisplayName = "Мастерская",
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
                Id = CouncilHall, DisplayName = "Зал совета",
                GoldCost = 40, Days = 5,
                Effect = BuildingEffect.CouncilActions, OpensSlotId = "council_seat"
            };

            // ---- Специальные: золото + строительный компонент ----
            yield return new BuildingDefinition
            {
                Id = Market, DisplayName = "Рынок",
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
                Id = Fortifications, DisplayName = "Укрепления",
                GoldCost = 45, MaterialsCost = 8, Days = 8,
                Effect = BuildingEffect.Fortifications
            };
            yield return new BuildingDefinition
            {
                Id = Armory, DisplayName = "Оружейная",
                GoldCost = 60, MaterialsCost = 6, Days = 8,
                Effect = BuildingEffect.Awaiting, AwaitingNote = "ждёт снаряжения (Э4)"
            };
            yield return new BuildingDefinition
            {
                Id = Laboratory, DisplayName = "Лаборатория",
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
