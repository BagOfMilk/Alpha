using System.Collections.Generic;
using Game.Core.Combat;
using Game.Core.Health;
using Game.Core.Items;

namespace Game.Core.Expeditions
{
    /// <summary>Последствие вылазки для одного напарника.</summary>
    public sealed class CompanionOutcome
    {
        public string CompanionId;
        public bool Died;

        /// <summary>Погиб не в бою, а по дороге домой (кризис «Напряжения» в городе).</summary>
        public bool DiedOnReturn;
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

        /// <summary>Что случилось в городе, пока отряд возвращался (инциденты/кризисы).</summary>
        public readonly List<Threats.IncidentReport> IncidentsWhileAway = new List<Threats.IncidentReport>();

        /// <summary>Стройки, достроившиеся, пока отряд возвращался.</summary>
        public readonly List<string> ConstructionCompletedWhileAway = new List<string>();

        /// <summary>Новый тир города, если он вырос за дни вылазки (0 — не рос, US-7.6).</summary>
        public int CityTierAdvancedTo;
        public int CityTierAdvancedFrom;

        /// <summary>Лут, добытый в этой вылазке и сложенный в сташ базы (только при победе).</summary>
        public readonly List<ItemInstance> LootDropped = new List<ItemInstance>();

        /// <summary>
        /// Рябь ростера от потерь этой вылазки (US-9.6) — УЖЕ ПРИМЕНЁННАЯ в
        /// Campaign.ConcludeExpedition, здесь только для показа. Экран отчёта её не
        /// применяет: он рисуется повторно при каждом пересоздании контроллера, а
        /// AdjustLoyalty не идемпотентен — просадка множилась бы на каждый показ.
        /// </summary>
        public readonly List<Companions.RippleEffect> DeathRipples = new List<Companions.RippleEffect>();

        public int TravelDaysTotal;
    }
}
