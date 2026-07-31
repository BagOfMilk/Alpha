using System;
using System.Collections.Generic;

namespace Game.Core.Saves
{
    /// <summary>
    /// Снимок кампании для сериализации (US-16.1). Плоские поля + списки (без
    /// словарей/полиморфизма) — чтобы дружить с Unity JsonUtility. Контент (трейты/
    /// шрамы/предметы) хранится по id и восстанавливается через ContentCatalog.
    /// </summary>
    [Serializable]
    public sealed class SaveData
    {
        public int version = 2;

        public int day;
        public int cityTier = 1;
        public float population;

        public int gold;
        public int buildingMaterial;
        public int craftingMaterial;

        public float tension;
        public float readiness;
        public bool ironman;

        // v2: оркестрация кампании (US-16.1 хвосты).
        public bool inExpedition;
        public int campaignOutcome; // CampaignOutcome

        public float reputation;
        public int influence;

        public List<CompanionDto> companions = new List<CompanionDto>();
        public List<ItemDto> inventory = new List<ItemDto>();
        public List<SlotDto> assignments = new List<SlotDto>();
        public List<FactionDto> factions = new List<FactionDto>();
        public List<string> flags = new List<string>();

        // v2: город (US-7.1/7.6) — построенные здания, открытые позиции, идущие стройки.
        public List<int> builtSections = new List<int>();
        public List<string> unlockedSlots = new List<string>();
        public List<ConstructionDto> constructions = new List<ConstructionDto>();

        // v2: арки напарников (US-9.5) и трофеи перебежчиков (US-9.4).
        public List<ArcDto> arcs = new List<ArcDto>();
        public List<AntagonistDto> antagonists = new List<AntagonistDto>();

        // v2: совет (если был подключён) — КД, активная Инвестиция, баф вылазки.
        public bool councilAttached;
        public List<CouncilCooldownDto> councilCooldowns = new List<CouncilCooldownDto>();
        public int investmentGoldPerDay;
        public int investmentDaysRemaining;
        public bool hasExpeditionBuff;
        public int buffAccuracyBonus;
        public int buffBonusLootGold;
    }

    [Serializable]
    public sealed class CouncilCooldownDto
    {
        public string actionId;
        public int days;
    }

    [Serializable]
    public sealed class ConstructionDto
    {
        public string id;
        public string displayName;
        public int section;
        public float totalDays;
        public float remainingDays;
        public string unlocksSlotId;
    }

    [Serializable]
    public sealed class ArcDto
    {
        public string arcId;
        public int state;        // ArcState
        public int chapterIndex;
    }

    [Serializable]
    public sealed class AntagonistDto
    {
        public string companionId;
        public int level;
        public List<ItemDto> gear = new List<ItemDto>();
    }

    [Serializable]
    public sealed class CompanionDto
    {
        public string id;
        public string displayName;
        public bool isProtagonist;

        public int strength, agility, wits, will;
        public List<SkillDto> skills = new List<SkillDto>();
        public List<string> traitIds = new List<string>();
        public List<string> scarIds = new List<string>();

        public int level = 1;
        public int xp;
        public int unspentSkillPoints;
        public int status;
        public int loyalty = 50;
        public int injuryTier;
        public float recoveryDays;

        public List<ItemDto> equipment = new List<ItemDto>();
    }

    [Serializable]
    public sealed class ItemDto
    {
        public string defId;
        public int rarity;
        public List<ModDto> mods = new List<ModDto>();
    }

    [Serializable]
    public sealed class ModDto
    {
        public int stat;
        public float value;
        public int mode;
    }

    [Serializable]
    public sealed class SkillDto
    {
        public int skill;
        public int level;
    }

    [Serializable]
    public sealed class SlotDto
    {
        public string slotId;
        public string companionId;
    }

    [Serializable]
    public sealed class FactionDto
    {
        public string id;
        public float value;
    }
}
