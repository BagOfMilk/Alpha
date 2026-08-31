using System;
using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Economy;

namespace Game.Core.Base
{
    /// <summary>
    /// Центральное состояние базы и главный игровой цикл «мирной» фазы.
    /// Связывает ростер, слоты назначений, кошелёк ресурсов и баланс-конфиг,
    /// и продвигает время методом <see cref="AdvanceCycle"/> (один цикл = один
    /// игровой день). Вся логика — чистый C#, без зависимостей от Unity.
    /// </summary>
    public sealed class BaseState
    {
        public Roster Roster { get; }
        public ResourceLedger Resources { get; }
        public BalanceConfig Balance { get; }

        public int CurrentCycle { get; private set; }

        private readonly Dictionary<string, AssignmentSlot> _slotsById = new Dictionary<string, AssignmentSlot>();
        private readonly List<AssignmentSlot> _slots = new List<AssignmentSlot>();

        public IReadOnlyList<AssignmentSlot> Slots => _slots;

        public BaseState(Roster roster, ResourceLedger resources, BalanceConfig balance)
        {
            Roster = roster ?? throw new ArgumentNullException(nameof(roster));
            Resources = resources ?? throw new ArgumentNullException(nameof(resources));
            Balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        public AssignmentSlot GetSlot(string slotId)
        {
            return slotId != null && _slotsById.TryGetValue(slotId, out var s) ? s : null;
        }

        public AssignmentSlot AddSlot(AssignmentSlotDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.Id)) return null;
            if (_slotsById.ContainsKey(definition.Id)) return _slotsById[definition.Id];
            var slot = new AssignmentSlot(definition);
            _slotsById.Add(slot.Id, slot);
            _slots.Add(slot);
            return slot;
        }

        /// <summary>
        /// Открывает закрытый слот за ресурсы. До появления полноценной стройки
        /// (US-7.1, US-7.3) это единственный способ ввести слот в игру — раньше
        /// закрытый слот оставался закрытым навсегда.
        ///
        /// Списание атомарное: если ресурсов не хватает, кошелёк не трогается
        /// вообще, а слот остаётся закрытым.
        /// </summary>
        public UnlockResult TryUnlockSlot(string slotId)
        {
            var slot = GetSlot(slotId);
            if (slot == null) return UnlockResult.SlotNotFound;
            if (slot.Unlocked) return UnlockResult.AlreadyUnlocked;

            var cost = slot.Definition?.UnlockCost;
            if (cost == null || cost.Count == 0) return UnlockResult.NoPriceDefined;
            if (!Resources.TrySpend(cost)) return UnlockResult.CannotAfford;

            slot.Unlocked = true;
            return UnlockResult.Success;
        }

        // ---- Назначения ----

        /// <summary>
        /// Назначает напарника на слот. Если напарник уже стоит на другом слоте —
        /// он автоматически снимается оттуда (перевод). Слот должен быть пустым.
        /// </summary>
        public AssignmentResult TryAssign(string companionId, string slotId)
        {
            var slot = GetSlot(slotId);
            if (slot == null) return AssignmentResult.SlotNotFound;
            if (!slot.Unlocked) return AssignmentResult.SlotLocked;

            var companion = Roster.Get(companionId);
            if (companion == null) return AssignmentResult.CompanionNotFound;

            if (companion.Status == CompanionStatus.OnMission)
                return AssignmentResult.CompanionUnavailable;

            if (slot.IsOccupied && slot.AssignedCompanionId != companionId)
                return AssignmentResult.SlotOccupied;

            // Снять с прежнего слота, если был назначен.
            if (companion.IsAssigned && companion.AssignedSlotId != slotId)
                Unassign(companion.AssignedSlotId);

            slot.AssignedCompanionId = companionId;
            companion.AssignedSlotId = slotId;
            if (companion.Status == CompanionStatus.Idle)
                companion.Status = CompanionStatus.Assigned;

            return AssignmentResult.Success;
        }

        /// <summary>Снимает назначение со слота (если занят).</summary>
        public void Unassign(string slotId)
        {
            var slot = GetSlot(slotId);
            if (slot == null || !slot.IsOccupied) return;

            var companion = Roster.Get(slot.AssignedCompanionId);
            slot.AssignedCompanionId = null;
            if (companion != null)
            {
                companion.AssignedSlotId = null;
                if (companion.Status == CompanionStatus.Assigned)
                    companion.Status = CompanionStatus.Idle;
            }
        }

        // ---- Продвижение времени ----

        /// <summary>
        /// Один цикл (день): производство со всех занятых слотов, начисление
        /// ролевого опыта, естественное лечение и расход еды поселением.
        /// </summary>
        public CycleReport AdvanceCycle()
        {
            CurrentCycle++;
            var report = new CycleReport { Cycle = CurrentCycle };

            foreach (var slot in _slots)
            {
                if (!slot.Unlocked || !slot.IsOccupied) continue;
                var companion = Roster.Get(slot.AssignedCompanionId);
                if (companion == null || companion.Status == CompanionStatus.OnMission) continue;

                var def = slot.Definition;
                int output = ProductionCalculator.OutputPerCycle(companion, def, Balance);

                switch (def.OutputKind)
                {
                    case SlotOutputKind.Resource:
                        Resources.Add(def.OutputResource, output);
                        report.AddProduced(def.OutputResource, output);
                        break;
                    case SlotOutputKind.Passive:
                        report.AddPassive(def.PassiveBonusId, output);
                        break;
                    case SlotOutputKind.Healing:
                        ApplyHealing(output, report);
                        break;
                }

                int xp = ProductionCalculator.RoleXpPerCycle(companion, def, Balance);
                var lvl = companion.GainXp(xp, Balance);
                if (lvl.LeveledUp)
                    report.LeveledUp.Add(companion.Id);
            }

            ApplyNaturalHealing(report);
            ApplyFoodUpkeep(report);
            return report;
        }

        /// <summary>Распределяет лечебные очки по самым тяжело раненным.</summary>
        private void ApplyHealing(int healing, CycleReport report)
        {
            if (healing <= 0) return;
            double pool = healing;
            // Сначала тем, у кого больше InjuryPoints.
            var injured = new List<Companion>();
            foreach (var c in Roster.All)
                if (c.IsInjured) injured.Add(c);
            injured.Sort((a, b) => b.InjuryPoints.CompareTo(a.InjuryPoints));

            foreach (var c in injured)
            {
                if (pool <= 0) break;
                double heal = Math.Min(pool, c.InjuryPoints);
                c.InjuryPoints -= heal;
                pool -= heal;
                if (c.InjuryPoints <= 0)
                {
                    c.InjuryPoints = 0;
                    if (c.Status == CompanionStatus.Injured || c.Status == CompanionStatus.Resting)
                        c.Status = CompanionStatus.Idle;
                    report.Recovered.Add(c.Id);
                }
            }
        }

        /// <summary>Базовое естественное восстановление всех раненых за цикл.</summary>
        private void ApplyNaturalHealing(CycleReport report)
        {
            double regen = Balance.BaseHealingPerCycle;
            if (regen <= 0) return;
            foreach (var c in Roster.All)
            {
                if (!c.IsInjured) continue;
                c.InjuryPoints -= regen;
                if (c.InjuryPoints <= 0)
                {
                    c.InjuryPoints = 0;
                    if (c.Status == CompanionStatus.Injured || c.Status == CompanionStatus.Resting)
                        c.Status = CompanionStatus.Idle;
                    if (!report.Recovered.Contains(c.Id))
                        report.Recovered.Add(c.Id);
                }
            }
        }

        private void ApplyFoodUpkeep(CycleReport report)
        {
            int upkeep = Balance.FoodUpkeepPerCompanion * Roster.Count;
            if (upkeep <= 0) return;
            if (!Resources.TrySpend(ResourceType.Food, upkeep))
            {
                // Не хватило еды — флаг для последующего штрафа к морали и т.п.
                Resources.Add(ResourceType.Food, -Resources.Get(ResourceType.Food));
                report.FoodShortage = true;
            }
        }
    }
}
