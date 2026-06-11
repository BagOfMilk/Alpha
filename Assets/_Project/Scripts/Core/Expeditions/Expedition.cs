using System;
using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Economy;
using Game.Core.Health;

namespace Game.Core.Expeditions
{
    public enum ExpeditionPhase
    {
        Mustering = 0, // сбор отряда (база)
        Away = 1,      // отряд в пути / на миссии
        Concluded = 2  // вылазка завершена
    }

    public enum ExpeditionSendResult
    {
        Success = 0,
        EmptySquad = 1,
        SquadTooLarge = 2,
        DuplicateCompanion = 3,
        CompanionNotFound = 4,
        CompanionUnavailable = 5, // мёртв/антагонист/ранен/уже в отряде
        WrongPhase = 6
    }

    /// <summary>
    /// Связка база ↔ бой (Эпики 1/4/15): отправка отряда снимает напарников с
    /// позиций (US-8.3), путешествие двигает календарь (US-1.2), по завершении боя
    /// последствия возвращаются в ростер через SourceCompanionId — смерть насовсем,
    /// ранения тирами + вечные шрамы (US-4.2), XP за победу (US-5.1), лут банкуется
    /// только при победе. Смерть протагониста в айронмене даёт GameOver (US-4.4).
    /// Бой между Depart и Conclude ведёт вызывающий код (CombatState).
    /// </summary>
    public sealed class Expedition
    {
        private readonly BaseState _base;
        private readonly BalanceConfig _cfg;
        private readonly Func<Companion, Scar> _scarPicker;
        private readonly List<string> _squadIds = new List<string>();
        private int _scarCounter;

        public ExpeditionPlan Plan { get; }
        public ExpeditionPhase Phase { get; private set; } = ExpeditionPhase.Mustering;
        public IReadOnlyList<string> SquadIds => _squadIds;

        public Expedition(BaseState baseState, ExpeditionPlan plan, BalanceConfig cfg,
                          Func<Companion, Scar> scarPicker = null)
        {
            _base = baseState ?? throw new ArgumentNullException(nameof(baseState));
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            _scarPicker = scarPicker ?? DefaultScarPicker;
        }

        // ---- Сбор отряда ----
        /// <summary>
        /// Набирает отряд: валидация ВСЕХ кандидатов до каких-либо изменений, затем
        /// снятие с позиций/совета (позиция пустует, US-8.3) и статус «в отряде».
        /// </summary>
        public ExpeditionSendResult TrySend(IReadOnlyList<string> companionIds)
        {
            if (Phase != ExpeditionPhase.Mustering || _squadIds.Count > 0) return ExpeditionSendResult.WrongPhase;
            if (companionIds == null || companionIds.Count == 0) return ExpeditionSendResult.EmptySquad;
            if (companionIds.Count > _cfg.SquadSize) return ExpeditionSendResult.SquadTooLarge;

            var seen = new HashSet<string>();
            foreach (var id in companionIds)
            {
                if (!seen.Add(id)) return ExpeditionSendResult.DuplicateCompanion;
                var c = _base.Roster.Get(id);
                if (c == null) return ExpeditionSendResult.CompanionNotFound;
                if (!c.IsAvailableForDuty) return ExpeditionSendResult.CompanionUnavailable;
            }

            foreach (var id in companionIds)
            {
                var c = _base.Roster.Get(id);
                if (c.IsAssigned) _base.Unassign(c.AssignedSlotId);
                c.Status = CompanionStatus.InSquad;
                _squadIds.Add(id);
            }
            return ExpeditionSendResult.Success;
        }

        /// <summary>Выход в путь: календарь двигается на дни дороги туда (US-1.2).</summary>
        public CycleReport Depart()
        {
            if (Phase != ExpeditionPhase.Mustering || _squadIds.Count == 0)
                throw new InvalidOperationException("Отряд не собран или вылазка уже в пути");
            Phase = ExpeditionPhase.Away;
            return _base.AdvanceDays(Plan.TravelDaysOut);
        }

        /// <summary>Боевые юниты отряда для CombatState; оружие выдаёт «оружейная» вызывающего.</summary>
        public List<CombatUnit> BuildCombatUnits(Func<Companion, WeaponDefinition> armory)
        {
            if (Phase == ExpeditionPhase.Concluded) throw new InvalidOperationException("Вылазка завершена");
            var units = new List<CombatUnit>();
            foreach (var id in _squadIds)
            {
                var c = _base.Roster.Get(id);
                if (c == null) continue;
                units.Add(CombatUnit.FromCompanion(c, armory != null ? armory(c) : null, _cfg));
            }
            return units;
        }

        // ---- Возврат ----
        /// <summary>
        /// Применяет исход боя к ростеру и базе: смерти, ранения по тирам (+шрамы за
        /// Серьёзное+), XP и банк лута при победе, game over в айронмене; затем
        /// дорога домой (лечение тикает в пути).
        /// </summary>
        public ExpeditionReport Conclude(CombatState combat)
        {
            if (combat == null) throw new ArgumentNullException(nameof(combat));
            if (Phase != ExpeditionPhase.Away) throw new InvalidOperationException("Вылазка не в пути");
            if (combat.Outcome == CombatOutcome.Ongoing) throw new InvalidOperationException("Бой не завершён");

            var report = new ExpeditionReport
            {
                Outcome = combat.Outcome,
                TravelDaysTotal = Plan.TravelDaysOut + Plan.TravelDaysBack
            };
            bool victory = combat.Outcome == CombatOutcome.Victory;

            foreach (var unit in combat.Units)
            {
                if (unit.Side != Side.Player || unit.SourceCompanionId == null) continue;
                var comp = _base.Roster.Get(unit.SourceCompanionId);
                if (comp == null) continue;

                var oc = new CompanionOutcome(comp.Id);
                switch (unit.LifeState)
                {
                    case UnitLifeState.Dead:
                        comp.Kill(); // смерть насовсем (US-4.1)
                        oc.Died = true;
                        break;

                    case UnitLifeState.Stabilized:
                    case UnitLifeState.Downed:
                    {
                        // Спасённые и не вытащенные к концу боя ранены тяжело; при
                        // поражении отступление под огнём добивает до Критического.
                        var tier = victory ? InjuryTier.Serious : InjuryTier.Critical;
                        var scar = _scarPicker(comp);
                        bool scarred = comp.ApplyInjury(tier, _cfg, scar);
                        oc.Injury = comp.CurrentInjury;
                        oc.ScarId = scarred && scar != null ? scar.Id : null;
                        break;
                    }

                    case UnitLifeState.Active:
                        double fraction = unit.Profile.MaxHp > 0 ? (double)unit.Hp / unit.Profile.MaxHp : 1.0;
                        if (fraction <= _cfg.LightInjuryHpFraction)
                        {
                            comp.ApplyInjury(InjuryTier.Light, _cfg, null); // лёгкие раны шрамов не дают
                            oc.Injury = comp.CurrentInjury;
                        }
                        else
                        {
                            comp.Status = CompanionStatus.InCamp;
                        }
                        break;
                }
                report.Companions.Add(oc);
            }

            if (victory)
            {
                _base.Resources.Add(ResourceType.Gold, Plan.RewardGold);
                _base.Resources.Add(ResourceType.BuildingMaterial, Plan.RewardBuildingMaterial);
                _base.Resources.Add(ResourceType.CraftingMaterial, Plan.RewardCraftingMaterial);
                report.GoldBanked = Plan.RewardGold;
                report.BuildingMaterialBanked = Plan.RewardBuildingMaterial;
                report.CraftingMaterialBanked = Plan.RewardCraftingMaterial;

                foreach (var id in _squadIds)
                {
                    var comp = _base.Roster.Get(id);
                    if (comp == null || !comp.IsAlive) continue;
                    if (comp.GainXp(_cfg.XpPerExpeditionVictory, _cfg).LeveledUp)
                        report.LeveledUp.Add(id);
                }
            }

            if (_cfg.Ironman)
            {
                foreach (var id in _squadIds)
                {
                    var comp = _base.Roster.Get(id);
                    if (comp != null && comp.IsProtagonist && !comp.IsAlive)
                        report.GameOver = true; // айронмен: гибель протагониста = конец (US-4.4)
                }
            }

            var back = _base.AdvanceDays(Plan.TravelDaysBack); // лечение тикает в дороге
            report.RecoveredOnReturn.AddRange(back.Recovered);

            Phase = ExpeditionPhase.Concluded;
            return report;
        }

        /// <summary>Дефолтный пул шрамов — плейсхолдер на стартовом контенте (ротация).</summary>
        private Scar DefaultScarPicker(Companion _)
            => _scarCounter++ % 2 == 0 ? DefaultContent.OneEye() : DefaultContent.Limp();
    }
}
