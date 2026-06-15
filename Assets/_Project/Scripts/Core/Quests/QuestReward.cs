using System.Collections.Generic;
using Game.Core.Factions;
using Game.Core.Items;

namespace Game.Core.Quests
{
    /// <summary>
    /// Награда за квест/исход (US-5.1: квесты дают XP + ресурсы/предметы, ≥ половины
    /// прогресса столпа №1). Соц-часть (репутация/фракции/влияние/Напряжение/флаги)
    /// несёт SocialConsequence — тот же механизм, что у совета (US-10.3).
    /// </summary>
    public sealed class QuestReward
    {
        public int Xp;
        public int Gold;
        public int BuildingMaterial;
        public int CraftingMaterial;

        /// <summary>Заработанные именные предметы (US-6.1: квесты/локации — источник именных).</summary>
        public readonly List<ItemDefinition> NamedItems = new List<ItemDefinition>();

        /// <summary>Соц-последствия (репутация/фракции/влияние/скрытая Напряжение/флаги).</summary>
        public SocialConsequence Social;

        public QuestReward() { }
        public QuestReward(int xp, int gold = 0) { Xp = xp; Gold = gold; }

        public QuestReward Materials(int building, int crafting)
        {
            BuildingMaterial = building;
            CraftingMaterial = crafting;
            return this;
        }

        public QuestReward Item(ItemDefinition named)
        {
            if (named != null) NamedItems.Add(named);
            return this;
        }

        public QuestReward WithSocial(SocialConsequence social)
        {
            Social = social;
            return this;
        }
    }
}
