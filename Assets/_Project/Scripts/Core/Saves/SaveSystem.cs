using System;
using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Economy;
using Game.Core.Factions;
using Game.Core.Health;
using Game.Core.Items;
using Game.Core.Stats;
using Game.Core.Threats;

namespace Game.Core.Saves
{
    /// <summary>
    /// Снимок и восстановление кампании (US-16.1). Чистый C# (без Unity) — сериализация
    /// SaveData ↔ файл живёт в Gameplay-обёртке. Capture снимает рантайм-состояние,
    /// Restore собирает свежую кампанию из снимка (контент — по id через ContentCatalog).
    /// </summary>
    public static class SaveSystem
    {
        // ---- Снимок ----
        public static SaveData Capture(Campaign campaign)
        {
            var b = campaign.Base;
            var data = new SaveData
            {
                day = b.CurrentDay,
                cityTier = b.CityTier,
                population = (float)b.Population,
                gold = b.Resources.Get(ResourceType.Gold),
                buildingMaterial = b.Resources.Get(ResourceType.BuildingMaterial),
                craftingMaterial = b.Resources.Get(ResourceType.CraftingMaterial),
                tension = (float)(b.ThreatsSystem != null ? b.ThreatsSystem.Tension.Value : 0),
                readiness = (float)(b.ThreatsSystem != null ? b.ThreatsSystem.Readiness.Value : 0),
                ironman = campaign.Ironman,
                reputation = (float)campaign.Factions.Reputation,
                influence = campaign.Factions.Influence
            };

            foreach (var c in b.Roster.All) data.companions.Add(CaptureCompanion(c));
            foreach (var item in b.Inventory.Items) data.inventory.Add(CaptureItem(item));
            foreach (var slot in b.Slots)
                if (slot.IsOccupied)
                    data.assignments.Add(new SlotDto { slotId = slot.Id, companionId = slot.AssignedCompanionId });
            foreach (var standing in campaign.Factions.Standings)
                data.factions.Add(new FactionDto { id = standing.Faction.Id, value = (float)standing.Value });
            foreach (var flag in campaign.Flags) data.flags.Add(flag);

            return data;
        }

        private static CompanionDto CaptureCompanion(Companion c)
        {
            var dto = new CompanionDto
            {
                id = c.Id,
                displayName = c.DisplayName,
                isProtagonist = c.IsProtagonist,
                strength = c.GetAttribute(AttributeType.Strength),
                agility = c.GetAttribute(AttributeType.Agility),
                wits = c.GetAttribute(AttributeType.Wits),
                will = c.GetAttribute(AttributeType.Will),
                level = c.Level,
                xp = c.Xp,
                unspentSkillPoints = c.UnspentSkillPoints,
                status = (int)c.Status,
                loyalty = c.Loyalty,
                injuryTier = (int)c.CurrentInjury,
                recoveryDays = (float)c.RecoveryDaysRemaining
            };

            foreach (SkillType skill in Enum.GetValues(typeof(SkillType)))
            {
                if (skill == SkillType.None) continue;
                int lvl = c.GetSkill(skill);
                if (lvl != 0) dto.skills.Add(new SkillDto { skill = (int)skill, level = lvl });
            }
            foreach (var t in c.Traits.Traits) dto.traitIds.Add(t.Id);
            foreach (var s in c.Scars.Scars) dto.scarIds.Add(s.Id);
            foreach (EquipSlot slot in Enum.GetValues(typeof(EquipSlot)))
            {
                var item = c.Equipment.Get(slot);
                if (item != null) dto.equipment.Add(CaptureItem(item));
            }
            return dto;
        }

        private static ItemDto CaptureItem(ItemInstance item)
        {
            var dto = new ItemDto { defId = item.Definition.Id, rarity = (int)item.Rarity };
            // Только БАЗОВЫЕ роллы (StatMods) — эффект именного восстановится из Definition.
            foreach (var m in item.StatMods)
                dto.mods.Add(new ModDto { stat = (int)m.Stat, value = (float)m.Value, mode = (int)m.Mode });
            return dto;
        }

        // ---- Восстановление ----
        public static Campaign Restore(SaveData data, BalanceConfig cfg, ContentCatalog catalog)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            cfg = cfg ?? new BalanceConfig();
            catalog = catalog ?? ContentCatalog.Default();

            var roster = new Roster();
            foreach (var cd in data.companions) roster.Add(RestoreCompanion(cd, cfg, catalog));

            var baseState = new BaseState(roster, new ResourceLedger(), cfg);
            foreach (var slot in DefaultContent.AllSlots()) baseState.AddSlot(slot);
            baseState.RestoreTime(data.day, data.population, data.cityTier);
            baseState.Resources.Add(ResourceType.Gold, data.gold);
            baseState.Resources.Add(ResourceType.BuildingMaterial, data.buildingMaterial);
            baseState.Resources.Add(ResourceType.CraftingMaterial, data.craftingMaterial);

            var threats = new ThreatSystem(cfg, new SeededRng(0),
                DefaultContent.IncidentPool(), DefaultContent.TensionSpikes(), startingTension: data.tension);
            threats.Readiness.Add(data.readiness);
            baseState.AttachThreats(threats);

            foreach (var it in data.inventory)
            {
                var inst = RestoreItem(it, catalog);
                if (inst != null) baseState.Inventory.Add(inst);
            }

            var factions = DefaultFactions.NewRegistry();
            foreach (var fd in data.factions) factions.Adjust(fd.id, fd.value); // из 0 → сохранённое
            if (data.reputation != 0) factions.AdjustReputation(data.reputation);
            if (data.influence != 0) factions.AddInfluence(data.influence);

            foreach (var sd in data.assignments) baseState.TryAssign(sd.companionId, sd.slotId);

            var campaign = new Campaign(cfg, baseState, factions) { Ironman = data.ironman };
            foreach (var f in data.flags) campaign.Flags.Add(f);
            return campaign;
        }

        private static Companion RestoreCompanion(CompanionDto cd, BalanceConfig cfg, ContentCatalog catalog)
        {
            var attrs = new AttributeBlock(cd.strength, cd.agility, cd.wits, cd.will);
            var c = new Companion(cd.id, attrs, cfg.TraitSlots)
            {
                DisplayName = cd.displayName,
                IsProtagonist = cd.isProtagonist
            };

            foreach (var sd in cd.skills) c.Skills.Set((SkillType)sd.skill, sd.level);
            foreach (var tid in cd.traitIds)
            {
                var t = catalog.GetTrait(tid);
                if (t != null) c.Traits.TryAdd(t);
            }
            foreach (var sid in cd.scarIds)
            {
                var s = catalog.GetScar(sid);
                if (s != null) c.Scars.Add(s);
            }
            foreach (var it in cd.equipment)
            {
                var inst = RestoreItem(it, catalog);
                if (inst != null) c.Equipment.Equip(inst);
            }

            c.RestoreProgress(cd.level, cd.xp, cd.unspentSkillPoints, (CompanionStatus)cd.status,
                              cd.loyalty, (InjuryTier)cd.injuryTier, cd.recoveryDays);
            return c;
        }

        private static ItemInstance RestoreItem(ItemDto it, ContentCatalog catalog)
        {
            var def = catalog.GetItem(it.defId);
            if (def == null) return null;
            var mods = new List<StatModifier>();
            for (int i = 0; i < it.mods.Count; i++)
            {
                var m = it.mods[i];
                mods.Add(new StatModifier((DerivedStat)m.stat, m.value, (ModMode)m.mode, ModifierSource.Gear));
            }
            return ItemInstance.FromSaved(def, (Rarity)it.rarity, mods);
        }
    }
}
