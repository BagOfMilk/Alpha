using System.Collections.Generic;
using Game.Core.Characters;
using Game.Core.Items;
using Game.Core.Session;
using Game.Core.Session.Views;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Правила екрана «лялька» (Поправка №19.3): порядок слотів, хто може вдягатися, що з надітого видно
    /// на моделі, які речі сховку пасують до слота. Чистий C# — екран UI Toolkit лише показує, а
    /// <see cref="CharacterKitPlan"/> збирає модель з тих самих ключів (охоронці — <c>InventoryModelTests</c>).
    /// </summary>
    public static class InventoryModel
    {
        /// <summary>Порядок слотів на «ляльці»: згори донизу, як у CRPG-референсів.</summary>
        public static readonly EquipSlot[] DollOrder =
        {
            EquipSlot.Head, EquipSlot.Weapon, EquipSlot.Offhand, EquipSlot.Armor,
            EquipSlot.Hands, EquipSlot.Legs, EquipSlot.Feet, EquipSlot.Accessory
        };

        public static string SlotKey(EquipSlot slot) => "ui.gear.slot." + slot.ToString().ToLowerInvariant();

        /// <summary>Хто може вдягатися: протагоніст і живі напарники, що вже в громаді.</summary>
        public static bool CanWear(CompanionSummary c) =>
            c != null && (c.Loyalty != null || c.Id == GameSession.ProtagonistId) &&
            c.Status != CompanionStatus.Dead && c.Status != CompanionStatus.Antagonist && c.Status != CompanionStatus.NotArrived;

        public static List<string> Wearers(RosterView roster)
        {
            var list = new List<string>();
            if (roster?.Companions == null) return list;
            // Протагоніст першим — «лялька» відкривається на ньому.
            foreach (var c in roster.Companions)
                if (c.Id == GameSession.ProtagonistId && CanWear(c)) list.Add(c.Id);
            foreach (var c in roster.Companions)
                if (c.Id != GameSession.ProtagonistId && CanWear(c)) list.Add(c.Id);
            return list;
        }

        /// <summary>Ключі набору надітого — для <see cref="CharacterKitPlan.From"/>.</summary>
        public static List<string> VisualKeys(EquipmentSheetView equipment)
        {
            var keys = new List<string>();
            if (equipment?.Slots == null) return keys;
            foreach (var s in equipment.Slots)
                if (!string.IsNullOrEmpty(s.ItemId) && !string.IsNullOrEmpty(s.VisualKey) && !keys.Contains(s.VisualKey))
                    keys.Add(s.VisualKey);
            return keys;
        }

        public static EquipSlotView SlotView(EquipmentSheetView equipment, EquipSlot slot)
        {
            if (equipment?.Slots != null)
                foreach (var s in equipment.Slots)
                    if (s.Slot == slot) return s;
            return new EquipSlotView { Slot = slot };
        }

        /// <summary>Речі сховку для слота (null — усі), у порядку сховку.</summary>
        public static List<ItemInstance> StashFor(IReadOnlyList<ItemInstance> stash, EquipSlot? slot)
        {
            var list = new List<ItemInstance>();
            if (stash == null) return list;
            foreach (var item in stash)
                if (item != null && (slot == null || item.Slot == slot.Value)) list.Add(item);
            return list;
        }

        /// <summary>Пропозиції кузні для слота (null — усі).</summary>
        public static List<ForgeOfferView> ForgeFor(IReadOnlyList<ForgeOfferView> offers, EquipSlot? slot)
        {
            var list = new List<ForgeOfferView>();
            if (offers == null) return list;
            foreach (var o in offers)
                if (o != null && (slot == null || o.Slot == slot.Value)) list.Add(o);
            return list;
        }

        /// <summary>Текст причини, чому кування не вдалося (null — вдалося).</summary>
        public static string ForgeFailureKey(ForgeResult result)
        {
            switch (result)
            {
                case ForgeResult.Success: return null;
                case ForgeResult.ArmoryClosed: return "ui.gear.forge.closed";
                case ForgeResult.CannotAfford: return "ui.gear.forge.cannot_afford";
                default: return "ui.gear.forge.unknown";
            }
        }
    }
}
