using Game.Core.Balance;
using Game.Core.Economy;

namespace Game.Core.Items
{
    public enum CraftResult
    {
        Success = 0,
        InvalidItem = 1,
        NamedNotUpgradable = 2, // іменні фіксовані (US-6.1)
        AlreadyMaxRarity = 3,
        WorkshopClosed = 4,     // майстерня ще не збудована/відкрита (Поправка №6, слот workshop_bench)
        CannotAfford = 5
    }

    /// <summary>
    /// Крафт (Епік 6.2, US-6.3): піднімає тір предмета за Матеріали + Золото
    /// (Поправка №4.1: золото — валюта відряду; матеріали — лише з вилазок) на
    /// відкритій Майстерні. НІКОЛИ не знижує стат (ItemInstance.UpgradeTo —
    /// масштабування, не пере-роздача). Умисно НЕ звертається до CityWorks:
    /// «відкрито майстерню чи ні» приходить готовим прапорцем від викликача,
    /// щоб Items не залежав від пакета B5 (Council/CityWorks).
    /// </summary>
    public static class CraftSystem
    {
        public static CraftResult TryUpgrade(ItemInstance item, ResourceLedger ledger,
            bool workshopOpen, int materialsCost, int goldCost)
        {
            if (item == null) return CraftResult.InvalidItem;
            if (item.Definition.IsNamed) return CraftResult.NamedNotUpgradable;
            if (item.Rarity >= Rarity.Epic) return CraftResult.AlreadyMaxRarity;
            if (!workshopOpen) return CraftResult.WorkshopClosed;

            if (ledger != null)
            {
                if (!ledger.CanAfford(ResourceType.Materials, materialsCost) ||
                    !ledger.CanAfford(ResourceType.Gold, goldCost))
                    return CraftResult.CannotAfford;

                ledger.TrySpend(ResourceType.Materials, materialsCost);
                ledger.TrySpend(ResourceType.Gold, goldCost);
            }

            item.UpgradeTo(RarityTuning.Next(item.Rarity));
            return CraftResult.Success;
        }

        /// <summary>Той самий апгрейд, але з числами із балансу (Core/Balance/ItemBalance.cs, R14).</summary>
        public static CraftResult TryUpgrade(ItemInstance item, ResourceLedger ledger, bool workshopOpen, ItemBalance cfg)
        {
            cfg = cfg ?? new ItemBalance();
            return TryUpgrade(item, ledger, workshopOpen, cfg.CraftMaterialsCost, cfg.CraftGoldCost);
        }
    }
}
