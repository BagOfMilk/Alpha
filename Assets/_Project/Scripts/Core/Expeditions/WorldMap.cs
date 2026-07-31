using System.Collections.Generic;

namespace Game.Core.Expeditions
{
    /// <summary>
    /// Узел карты мира (US-1.1): точка-ExpeditionPlan + гейт доступности (тир
    /// города / сюжетный флаг). Недоступные узлы отдаются отдельным списком —
    /// «визуально отличимы» (подача — слой UI). В Unity обернётся ScriptableObject.
    /// </summary>
    public sealed class WorldNode
    {
        public ExpeditionPlan Plan;
        public string Id => Plan?.Id;

        /// <summary>Гейты доступности: тир города и/или сюжетный флаг.</summary>
        public int RequiresCityTier = 1;
        public string RequiresFlag;

        /// <summary>Узел закрывается флагом (напр. одноразовая авторская локация пройдена).</summary>
        public string BlockedByFlag;

        public WorldNode(ExpeditionPlan plan)
        {
            Plan = plan;
        }

        public WorldNode Tier(int cityTier) { RequiresCityTier = cityTier; return this; }
        public WorldNode Gate(string flag) { RequiresFlag = flag; return this; }
        public WorldNode BlockFlag(string flag) { BlockedByFlag = flag; return this; }
    }

    /// <summary>
    /// Карта мира (US-1.1): реестр узлов с доступностью. Выбор доступного узла
    /// даёт ExpeditionPlan для вылазки (Campaign.LaunchExpedition). Тир города
    /// открывает новые точки (US-7.6), сюжет — авторские локации.
    /// </summary>
    public sealed class WorldMap
    {
        private readonly List<WorldNode> _nodes = new List<WorldNode>();

        public IReadOnlyList<WorldNode> All => _nodes;

        public WorldMap Add(WorldNode node)
        {
            if (node != null && node.Plan != null) _nodes.Add(node);
            return this;
        }

        public WorldNode Get(string id)
        {
            for (int i = 0; i < _nodes.Count; i++)
                if (_nodes[i].Id == id) return _nodes[i];
            return null;
        }

        public bool IsAvailable(WorldNode node, int cityTier, ICollection<string> flags)
        {
            if (node == null) return false;
            if (cityTier < node.RequiresCityTier) return false;
            if (!string.IsNullOrEmpty(node.RequiresFlag) && (flags == null || !flags.Contains(node.RequiresFlag)))
                return false;
            if (!string.IsNullOrEmpty(node.BlockedByFlag) && flags != null && flags.Contains(node.BlockedByFlag))
                return false;
            return true;
        }

        /// <summary>Куда можно отправить отряд сейчас.</summary>
        public List<WorldNode> Available(int cityTier, ICollection<string> flags)
        {
            var result = new List<WorldNode>();
            for (int i = 0; i < _nodes.Count; i++)
                if (IsAvailable(_nodes[i], cityTier, flags)) result.Add(_nodes[i]);
            return result;
        }

        /// <summary>Видимые, но недоступные узлы (US-1.1: «недоступные визуально отличимы»).</summary>
        public List<WorldNode> Locked(int cityTier, ICollection<string> flags)
        {
            var result = new List<WorldNode>();
            for (int i = 0; i < _nodes.Count; i++)
                if (!IsAvailable(_nodes[i], cityTier, flags)) result.Add(_nodes[i]);
            return result;
        }
    }

    /// <summary>Сид-карта прототипа (ПЛЕЙСХОЛДЕР-контент): ближняя точка, данж-руины, финальная окраина.</summary>
    public static class DefaultWorld
    {
        public static WorldMap NewMap()
        {
            return new WorldMap()
                .Add(new WorldNode(new ExpeditionPlan("east_road", "Восточный тракт")
                {
                    TravelDaysOut = 2, TravelDaysBack = 2,
                    RewardGold = 100, RewardBuildingMaterial = 8, RewardCraftingMaterial = 5
                }))
                .Add(new WorldNode(new ExpeditionPlan("rusted_works", "Ржавые цеха")
                {
                    TravelDaysOut = 3, TravelDaysBack = 3,
                    RewardGold = 60, RewardBuildingMaterial = 12, RewardCraftingMaterial = 8
                }).Tier(2)) // дальний источник материалов — открывается ростом города
                .Add(new WorldNode(new ExpeditionPlan("horde_outskirts", "Окраина: лагерь орды")
                {
                    TravelDaysOut = 1, TravelDaysBack = 1,
                    RewardGold = 40
                }).Gate("finale_ready")); // финальная точка — по сюжетной вехе (US-14.2)
        }
    }
}
