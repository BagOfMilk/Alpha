using System;
using System.Collections.Generic;
using Game.Core.Items;

namespace Game.Core.Expeditions
{
    /// <summary>
    /// План вылазки: точка на карте мира (US-1.1) как данные — дни пути и награда
    /// за успех. Награда банкуется ТОЛЬКО при победе (Эпик 15: squad-loop кормит
    /// settlement-loop; провал бьёт по ресурсам — US-16.2). В Unity обернётся
    /// ScriptableObject-ассетом локации.
    /// </summary>
    [Serializable]
    public sealed class ExpeditionPlan
    {
        public string Id;
        public string DisplayName;

        /// <summary>Дни пути туда/обратно (US-1.2: путешествие двигает календарь).</summary>
        public int TravelDaysOut = 2;
        public int TravelDaysBack = 2;

        /// <summary>Награда при победе: золото + два компонента (Эпик 6.2).</summary>
        public int RewardGold;
        public int RewardBuildingMaterial;
        public int RewardCraftingMaterial;

        /// <summary>Рандом-дроп лута при победе (Эпик 6.1): таблица + сколько кинуть.</summary>
        public LootTable DropTable;
        public int DropCount;

        /// <summary>Заработанные именные предметы (боссы/квесты/локации, US-6.1): фикс, всегда при победе.</summary>
        public readonly List<ItemDefinition> NamedRewards = new List<ItemDefinition>();

        public ExpeditionPlan() { }

        public ExpeditionPlan(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }
    }
}
