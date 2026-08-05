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
        public int version = 5;

        // v3: сид кампании + состояние потока случайностей угроз (ulong строкой —
        // JsonUtility надёжно не сериализует ulong). Загрузка ПРОДОЛЖАЕТ поток.
        public int campaignSeed;
        public string threatsRngState;
        // v3: сработавшие одноразовые пороговые всплески (иначе перевзводятся
        // загрузкой). double — сужение до float ломало бы нецелые пороги.
        public List<double> firedSpikeThresholds = new List<double>();

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

        // v2: ачивки (US-16.1, берутся только в айронмене).
        public List<string> achievements = new List<string>();

        // v2: совет (если был подключён) — КД, активная Инвестиция, баф вылазки.
        public bool councilAttached;
        public List<CouncilCooldownDto> councilCooldowns = new List<CouncilCooldownDto>();
        public int investmentGoldPerDay;
        public int investmentDaysRemaining;
        public bool hasExpeditionBuff;
        public int buffAccuracyBonus;
        public int buffBonusLootGold;

        // v4: журнал квестов (US-14.3) — только id, контент восстанавливается из
        // пула. Прогресс АКТИВНОГО прогона (этап) не сейвится: загрузка возвращает
        // такие квесты в «доступные» — перепрохождение с начала честнее полу-состояния.
        public List<string> questsAvailable = new List<string>();
        public List<string> questsActive = new List<string>();
        public List<string> questsCompleted = new List<string>();

        // v5: день появления вехи финала — от него растёт волна штурма (US-11.4).
        // Старые сейвы: 0 → орда не набирает подкреплений (честно для прошлых прогонов).
        public int finaleReadyDay;

        /// <summary>
        /// v5, айронмен: сейв сделан НА ВЫХОДЕ вылазки, бой не доигран. Загрузка
        /// такого сейва не отматывает решение — вылазка резолвится как отступление
        /// (без наград и без смертей). Иначе выход из игры в проигрышном бою был
        /// бесплатным откатом мимо всей телеметрии save-scum (US-16.1).
        /// </summary>
        public bool expeditionUnresolved;
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
