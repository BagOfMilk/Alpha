using System.Collections.Generic;
using Game.Core.Base;
using Game.Core.Economy;
using Game.Core.Factions;
using Game.Core.Threats;

namespace Game.Core.Council
{
    /// <summary>
    /// Сид-набор действий совета (GDD §8.4, ПЛЕЙСХОЛДЕР-числа). В новом неймспейсе,
    /// чтобы не трогать общий DefaultContent.cs. Соц-последствия завязаны на
    /// DefaultFactions: Облава радует Гарнизон и злит Вольных (честный размен вместо
    /// прямой −лояльности, которая придёт с Эпиком 9).
    /// </summary>
    public static class DefaultCouncil
    {
        public const string Raid = "raid";
        public const string Decree = "decree";
        public const string Diplomacy = "diplomacy";
        public const string Investment = "investment";
        public const string Prepare = "prepare_threat";
        public const string Outfit = "outfit_expedition";

        public static List<CouncilActionDefinition> All() => new List<CouncilActionDefinition>
        {
            new CouncilActionDefinition(Raid, "Облава", CouncilActionType.Raid)
            {
                GoldCost = 20, InfluenceCost = 1, CooldownDays = 6, TensionRelief = 12,
                Social = new SocialConsequence()
                    .Faction(DefaultFactions.Garrison, +6)   // силовики одобряют
                    .Faction(DefaultFactions.FreeFolk, -8)   // вольные в ярости (риск вместо −лояльности)
            },
            new CouncilActionDefinition(Decree, "Указ", CouncilActionType.Decree)
            {
                InfluenceCost = 1, CooldownDays = 8,
                Social = new SocialConsequence()
                    .Faction(DefaultFactions.Traders, +8)    // про-рыночный указ
                    .Faction(DefaultFactions.Commune, -6)    // ценой общины
            },
            new CouncilActionDefinition(Diplomacy, "Дипломатия", CouncilActionType.Diplomacy)
            {
                InfluenceCost = 2, CooldownDays = 6,
                UsesTargetFaction = true, TargetFactionDelta = +10,
                Social = new SocialConsequence().Reputation(+2)
            },
            new CouncilActionDefinition(Investment, "Инвестиция", CouncilActionType.Investment)
            {
                GoldCost = 40, CooldownDays = 8,
                InvestmentGoldPerDay = 8, InvestmentDays = 10 // 40 → 80 за 10 дней (профит за время)
            },
            new CouncilActionDefinition(Prepare, "Подготовка к угрозе", CouncilActionType.PrepareThreat)
            {
                GoldCost = 15, InfluenceCost = 1, CooldownDays = 6, ReadinessGain = 10
            },
            new CouncilActionDefinition(Outfit, "Снаряжение экспедиции", CouncilActionType.OutfitExpedition)
            {
                GoldCost = 25, CooldownDays = 5,
                OutfitBuff = new ExpeditionBuff { AccuracyBonus = 10, BonusLootGold = 30 }
            }
        };

        /// <summary>Готовый совет со всеми сид-действиями.</summary>
        public static Council NewCouncil(FactionRegistry factions, ResourceLedger ledger,
                                         ThreatSystem threats = null, BaseState baseState = null)
        {
            var council = new Council(factions, ledger, threats, baseState);
            foreach (var a in All()) council.AddAction(a);
            return council;
        }
    }
}
