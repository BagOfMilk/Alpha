using System;
using Game.Core.Factions;

namespace Game.Core.Council
{
    /// <summary>6 действий совета (GDD §8.4 US-8.4): весомые, повторяемые, с КД и ценой.</summary>
    public enum CouncilActionType
    {
        Raid = 0,             // Облава: −Напряжение (цена: влияние/золото; злит вольных)
        Decree = 1,           // Указ: фракционный размен (одну фракцию вверх ценой другой)
        Diplomacy = 2,        // Дипломатия: привлечь/улучшить фракцию (цена: влияние)
        Investment = 3,       // Инвестиция: буст дохода золота на время
        PrepareThreat = 4,    // Подготовка к угрозе: +Готовность к финалу
        OutfitExpedition = 5  // Снаряжение экспедиции: баф следующему отряду
    }

    /// <summary>Баф следующей вылазке (US-8.4 «Снаряжение»). Потребляется один раз.</summary>
    public sealed class ExpeditionBuff
    {
        public int AccuracyBonus;
        public int BonusLootGold;
    }

    /// <summary>
    /// Действие совета: чистые данные (цена/КД/магнитуды/соц-последствия), как и
    /// способности (AbilityDefinition). В Unity обернётся ScriptableObject. Соц-эффект
    /// (фракции/репутация/влияние/скрытая Напруга) несёт Social.
    /// </summary>
    [Serializable]
    public sealed class CouncilActionDefinition
    {
        public string Id;
        public string DisplayName;
        public CouncilActionType Type;

        public int GoldCost;
        public int InfluenceCost;
        public int CooldownDays = 5;
        public bool Unlocked = true; // состав кресел может открывать (полное гейтирование — позже)

        // Магнитуды по типу:
        public double TensionRelief;        // Raid
        public double ReadinessGain;        // PrepareThreat
        public int InvestmentGoldPerDay;    // Investment
        public int InvestmentDays;          // Investment
        public ExpeditionBuff OutfitBuff;   // OutfitExpedition

        // Дипломатия: + к выбранной игроком фракции (targetFactionId в Execute).
        public bool UsesTargetFaction;
        public double TargetFactionDelta;

        // Фиксированные соц-последствия (Облава злит вольных, Указ-размен и т.п.).
        public SocialConsequence Social;

        public CouncilActionDefinition() { }

        public CouncilActionDefinition(string id, string displayName, CouncilActionType type)
        {
            Id = id;
            DisplayName = displayName;
            Type = type;
        }
    }

    public enum CouncilActionResult
    {
        Success = 0,
        UnknownAction = 1,
        Locked = 2,
        OnCooldown = 3,
        CannotAffordGold = 4,
        CannotAffordInfluence = 5,
        InvalidTarget = 6
    }

    /// <summary>Итог попытки выполнить действие совета — для UI/лога.</summary>
    public sealed class CouncilActionReport
    {
        public string ActionId;
        public CouncilActionResult Result;
        public int CooldownDays;

        public CouncilActionReport(string actionId, CouncilActionResult result, int cooldownDays = 0)
        {
            ActionId = actionId;
            Result = result;
            CooldownDays = cooldownDays;
        }

        public bool Success => Result == CouncilActionResult.Success;
    }
}
