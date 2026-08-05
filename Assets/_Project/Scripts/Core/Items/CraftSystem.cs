using Game.Core.Combat;
using Game.Core.Economy;

namespace Game.Core.Items
{
    public enum CraftResult
    {
        Success = 0,
        InvalidItem = 1,
        NamedNotUpgradable = 2, // именные фиксированы (US-6.1)
        AlreadyMaxRarity = 3,
        CannotAfford = 4
    }

    /// <summary>
    /// Крафт (Эпик 6.2, US-6.3): тратит КРАФТОВЫЙ компонент на апгрейд/моды гира
    /// (базовый гир не создаёт). Здесь — апгрейд редкости предмета: поднимает тир и
    /// МАСШТАБИРУЕТ уже выпавшие роллы новым множителем (без пере-ролла — см.
    /// ItemInstance.UpgradeTo). Именные не апгрейдятся.
    /// </summary>
    public static class CraftSystem
    {
        public static CraftResult TryUpgrade(ItemInstance item, ResourceLedger ledger, int craftingCost, IRng rng)
        {
            if (item == null) return CraftResult.InvalidItem;
            if (item.Definition.IsNamed) return CraftResult.NamedNotUpgradable;
            if (item.Rarity >= Rarity.Epic) return CraftResult.AlreadyMaxRarity;
            if (ledger != null && !ledger.CanAfford(ResourceType.CraftingMaterial, craftingCost))
                return CraftResult.CannotAfford;

            ledger?.TrySpend(ResourceType.CraftingMaterial, craftingCost);
            // Масштабирование, а не пере-ролл: апгрейд не может ухудшить предмет
            // (rng больше не нужен — оставлен в сигнатуре под будущие моды US-6.4).
            item.UpgradeTo(RarityTuning.Next(item.Rarity));
            return CraftResult.Success;
        }
    }
}
