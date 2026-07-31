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
        public int version = 1;

        public int day;
        public int cityTier = 1;
        public float population;

        public int gold;
        public int buildingMaterial;
        public int craftingMaterial;

        public float tension;
        public float readiness;
        public bool ironman;

        public float reputation;
        public int influence;

        public List<CompanionDto> companions = new List<CompanionDto>();
        public List<ItemDto> inventory = new List<ItemDto>();
        public List<SlotDto> assignments = new List<SlotDto>();
        public List<FactionDto> factions = new List<FactionDto>();
        public List<string> flags = new List<string>();
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
