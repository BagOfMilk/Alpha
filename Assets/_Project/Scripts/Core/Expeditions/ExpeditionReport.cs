using System.Collections.Generic;
using Game.Core.Combat;
using Game.Core.Health;

namespace Game.Core.Expeditions
{
    /// <summary>Последствие вылазки для одного напарника.</summary>
    public sealed class CompanionOutcome
    {
        public string CompanionId;
        public bool Died;
        public InjuryTier Injury = InjuryTier.None;
        public string ScarId; // null — без шрама

        public CompanionOutcome(string companionId)
        {
            CompanionId = companionId;
        }
    }

    /// <summary>
    /// Сводка завершённой вылазки: исход, банк лута, судьбы напарников, опыт,
    /// game over (айронмен). Возвращается из Expedition.Conclude — удобна для UI.
    /// </summary>
    public sealed class ExpeditionReport
    {
        public CombatOutcome Outcome;

        /// <summary>Смерть протагониста в айронмене (US-4.4/Эпик 16).</summary>
        public bool GameOver;

        public int GoldBanked;
        public int BuildingMaterialBanked;
        public int CraftingMaterialBanked;

        public readonly List<CompanionOutcome> Companions = new List<CompanionOutcome>();

        /// <summary>Кто поднял уровень с XP за вылазку.</summary>
        public readonly List<string> LeveledUp = new List<string>();

        /// <summary>Кто успел полностью вылечиться уже по дороге домой.</summary>
        public readonly List<string> RecoveredOnReturn = new List<string>();

        public int TravelDaysTotal;
    }
}
