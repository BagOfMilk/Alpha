using System;
using System.Collections.Generic;
using Game.Core.Base;
using Game.Core.Economy;
using Game.Core.Factions;
using Game.Core.Threats;

namespace Game.Core.Council
{
    /// <summary>
    /// Совет (GDD §8.4): повторяемые действия с КД (в днях) и ценой (золото/влияние),
    /// бьющие по разным системам через DI — Напряжение/Готовность (ThreatSystem),
    /// фракции/репутация/влияние (FactionRegistry), золото (ResourceLedger). КД и
    /// доход от Инвестиции тикаются TickDays (зовётся оркестратором при ходе времени).
    /// threats/baseState — null-терпимы (действия деградируют мягко).
    /// </summary>
    public sealed class Council
    {
        private readonly FactionRegistry _factions;
        private readonly ResourceLedger _ledger;
        private readonly ThreatSystem _threats;
        private readonly BaseState _base;

        private readonly Dictionary<string, CouncilActionDefinition> _actions = new Dictionary<string, CouncilActionDefinition>();
        private readonly List<CouncilActionDefinition> _ordered = new List<CouncilActionDefinition>();
        private readonly Dictionary<string, int> _cooldowns = new Dictionary<string, int>();

        private int _investmentDaysRemaining;
        private int _investmentGoldPerDay;

        /// <summary>Баф следующей вылазке от «Снаряжения»; потребляется ConsumeExpeditionBuff.</summary>
        public ExpeditionBuff PendingExpeditionBuff { get; private set; }

        public IReadOnlyList<CouncilActionDefinition> Actions => _ordered;

        public Council(FactionRegistry factions, ResourceLedger ledger,
                       ThreatSystem threats = null, BaseState baseState = null)
        {
            _factions = factions;
            _ledger = ledger;
            _threats = threats;
            _base = baseState;
        }

        public Council AddAction(CouncilActionDefinition def)
        {
            if (def != null && !string.IsNullOrEmpty(def.Id) && !_actions.ContainsKey(def.Id))
            {
                _actions.Add(def.Id, def);
                _ordered.Add(def);
            }
            return this;
        }

        public int CooldownRemaining(string actionId)
            => actionId != null && _cooldowns.TryGetValue(actionId, out var v) ? v : 0;

        /// <summary>
        /// Выполнить действие. targetFactionId — для Дипломатии (какую фракцию тянем).
        /// Валидация: открыто → КД → золото → влияние; затем оплата, КД и эффекты.
        /// </summary>
        public CouncilActionReport Execute(string actionId, string targetFactionId = null)
        {
            if (actionId == null || !_actions.TryGetValue(actionId, out var def))
                return new CouncilActionReport(actionId, CouncilActionResult.UnknownAction);
            if (!def.Unlocked)
                return new CouncilActionReport(actionId, CouncilActionResult.Locked);
            if (CooldownRemaining(actionId) > 0)
                return new CouncilActionReport(actionId, CouncilActionResult.OnCooldown, CooldownRemaining(actionId));
            if (def.Type == CouncilActionType.Diplomacy && def.UsesTargetFaction
                && (targetFactionId == null || _factions == null || _factions.Get(targetFactionId) == null))
                return new CouncilActionReport(actionId, CouncilActionResult.InvalidTarget);

            bool goldOk = _ledger == null || _ledger.CanAfford(ResourceType.Gold, def.GoldCost);
            if (!goldOk) return new CouncilActionReport(actionId, CouncilActionResult.CannotAffordGold);
            bool inflOk = def.InfluenceCost <= 0 || (_factions != null && _factions.CanAfford(def.InfluenceCost));
            if (!inflOk) return new CouncilActionReport(actionId, CouncilActionResult.CannotAffordInfluence);

            // Оплата (обе цены уже проверены).
            if (def.GoldCost > 0) _ledger?.TrySpend(ResourceType.Gold, def.GoldCost);
            if (def.InfluenceCost > 0) _factions?.SpendInfluence(def.InfluenceCost);

            _cooldowns[actionId] = def.CooldownDays;
            ApplyEffect(def, targetFactionId);
            return new CouncilActionReport(actionId, CouncilActionResult.Success, def.CooldownDays);
        }

        private void ApplyEffect(CouncilActionDefinition def, string targetFactionId)
        {
            switch (def.Type)
            {
                case CouncilActionType.Raid:
                    if (def.TensionRelief != 0) _threats?.Tension.Add(-def.TensionRelief);
                    break;

                case CouncilActionType.PrepareThreat:
                    if (def.ReadinessGain != 0) _threats?.Readiness.Add(def.ReadinessGain);
                    break;

                case CouncilActionType.Investment:
                    _investmentGoldPerDay = def.InvestmentGoldPerDay;
                    _investmentDaysRemaining = def.InvestmentDays;
                    break;

                case CouncilActionType.OutfitExpedition:
                    PendingExpeditionBuff = def.OutfitBuff != null
                        ? new ExpeditionBuff { AccuracyBonus = def.OutfitBuff.AccuracyBonus, BonusLootGold = def.OutfitBuff.BonusLootGold }
                        : null;
                    break;

                case CouncilActionType.Diplomacy:
                    if (def.UsesTargetFaction && targetFactionId != null)
                        _factions?.Adjust(targetFactionId, def.TargetFactionDelta);
                    break;

                case CouncilActionType.Decree:
                    break; // размен целиком в Social
            }

            def.Social?.Apply(_factions, _base, _threats);
        }

        public ExpeditionBuff ConsumeExpeditionBuff()
        {
            var buff = PendingExpeditionBuff;
            PendingExpeditionBuff = null;
            return buff;
        }

        /// <summary>Ход времени: тикает КД действий и капает доход от активной Инвестиции.</summary>
        public void TickDays(int days)
        {
            if (days <= 0) return;

            if (_cooldowns.Count > 0)
            {
                var keys = new List<string>(_cooldowns.Keys);
                for (int i = 0; i < keys.Count; i++)
                {
                    int v = _cooldowns[keys[i]] - days;
                    if (v <= 0) _cooldowns.Remove(keys[i]);
                    else _cooldowns[keys[i]] = v;
                }
            }

            if (_investmentDaysRemaining > 0 && _ledger != null)
            {
                int payDays = Math.Min(days, _investmentDaysRemaining);
                _ledger.Add(ResourceType.Gold, _investmentGoldPerDay * payDays);
                _investmentDaysRemaining -= payDays;
            }
        }
    }
}
