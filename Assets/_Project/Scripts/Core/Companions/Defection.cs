using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Items;

namespace Game.Core.Companions
{
    /// <summary>
    /// Снимок ушедшего в антагонисты (US-9.4): уровень + надетый гир на момент ухода.
    /// Гир возвращается убийством босса (ReturnGearOnKill).
    /// </summary>
    public sealed class AntagonistRecord
    {
        public string CompanionId;
        public int Level;
        public WeaponDefinition Weapon;
        public readonly List<ItemInstance> CapturedGear = new List<ItemInstance>();
    }

    /// <summary>
    /// Уход напарника в антагонисты и встреча его боссом (US-9.4): срабатывает при
    /// низкой лояльности (полоса Resentful) + сюжетной развилке (решает вызывающий).
    /// Уходит со СВОИМ гиром и уровнями; убийство возвращает гир. Переход необратим
    /// (US-9.1). Протагонист предать не может.
    /// </summary>
    public static class DefectionSystem
    {
        /// <summary>Готов ли напарник к уходу: жив, не протагонист, лояльность на дне.</summary>
        public static bool ShouldDefect(Companion c)
            => c != null && c.IsAlive && !c.IsProtagonist && c.LoyaltyBand == LoyaltyBand.Resentful;

        /// <summary>Переводит в антагонисты: снимок гира/уровня, снятие с позиции, статус.</summary>
        public static AntagonistRecord Defect(Companion c, BaseState baseState = null)
        {
            var record = new AntagonistRecord
            {
                CompanionId = c.Id,
                Level = c.Level,
                Weapon = c.Equipment.EquippedWeapon
            };
            foreach (EquipSlot slot in System.Enum.GetValues(typeof(EquipSlot)))
            {
                var item = c.Equipment.Get(slot);
                if (item != null) record.CapturedGear.Add(item);
            }

            if (c.IsAssigned) baseState?.Unassign(c.AssignedSlotId);
            c.Status = CompanionStatus.Antagonist; // необратимо (US-9.1)
            return record;
        }

        /// <summary>Босс-энкаунтер из перебежчика (его статы + гир + способности).</summary>
        public static CombatUnit BuildBossUnit(Companion c, BalanceConfig cfg,
                                               IEnumerable<AbilityDefinition> abilityCatalog = null)
            => CombatUnit.FromDefector(c, cfg, abilityCatalog);

        /// <summary>Убийство босса возвращает его надетый гир в сташ базы (US-9.4).</summary>
        public static void ReturnGearOnKill(AntagonistRecord record, Inventory inventory)
        {
            if (record == null || inventory == null) return;
            for (int i = 0; i < record.CapturedGear.Count; i++)
                inventory.Add(record.CapturedGear[i]);
        }
    }
}
