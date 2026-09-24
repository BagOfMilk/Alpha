using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Economy;
using Game.Core.Stats;

namespace Game.Core.Items
{
    /// <summary>
    /// Один стат «до → після» крафт-апгрейду (полірування, ціль 2
    /// «Прозорість дій», owner: "Craft upgrade with cost and before→after
    /// preview"). Ціле округлення — те, що бачить гравець; сирий double
    /// лишається деталлю <see cref="ItemInstance.UpgradeTo"/>.
    /// </summary>
    public readonly struct StatPreviewLine
    {
        public readonly StatKey Key;
        public readonly int Before;
        public readonly int After;

        public StatPreviewLine(StatKey key, int before, int after)
        {
            Key = key;
            Before = before;
            After = after;
        }
    }

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

        /// <summary>
        /// Прев'ю «до → після» БЕЗ мутації предмета: та сама формула
        /// масштабування, що й <see cref="ItemInstance.UpgradeTo"/> (округлення
        /// AwayFromZero, +1 якщо округлення дало те саме число), винесена
        /// сюди read-only — інакше екран мусив би або дублювати правило
        /// вручну (розсинхрон гарантований), або справді апгрейдити предмет,
        /// щоб побачити наслідок. Порожній список — іменний/уже Epic предмет
        /// (нема що апгрейдити, той самий гейт, що в TryUpgrade).
        /// </summary>
        public static List<StatPreviewLine> PreviewUpgrade(ItemInstance item)
        {
            var result = new List<StatPreviewLine>();
            if (item == null || item.Definition.IsNamed || item.Rarity >= Rarity.Epic) return result;

            double from = RarityTuning.MagnitudeMultiplier(item.Rarity);
            double to = RarityTuning.MagnitudeMultiplier(RarityTuning.Next(item.Rarity));
            if (from <= 0) return result;

            foreach (var m in item.StatMods)
            {
                double scaled = System.Math.Round(m.Value * to / from, System.MidpointRounding.AwayFromZero);
                if (scaled == m.Value) scaled = m.Value + 1; // апгрейд зобов'язаний бути помітним (той самий рядок, що в UpgradeTo)
                result.Add(new StatPreviewLine(m.Key, (int)System.Math.Round(m.Value), (int)scaled));
            }
            return result;
        }
    }
}
