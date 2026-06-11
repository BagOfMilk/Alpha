using System;
using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Economy;
using Game.Core.Stats;

namespace Game.Core.Base
{
    /// <summary>
    /// Состояние базы и продвижение «мирного» времени (GDD §1). Связывает ростер,
    /// позиции, кошелёк (золото + 2 материала — наполняется вылазками, не базой) и
    /// баланс. <see cref="AdvanceDays"/> продвигает календарь: лечит раненых в днях
    /// (медик в Лазарете ускоряет), достраивает стройки, растит население. Материалы
    /// здесь НЕ производятся — рост города требует выходить наружу. Чистый C#.
    /// </summary>
    public sealed class BaseState
    {
        public Roster Roster { get; }
        public ResourceLedger Resources { get; }
        public BalanceConfig Balance { get; }

        public int CurrentDay { get; private set; }
        public double Population { get; private set; }

        private readonly Dictionary<string, AssignmentSlot> _slotsById = new Dictionary<string, AssignmentSlot>();
        private readonly List<AssignmentSlot> _slots = new List<AssignmentSlot>();
        private readonly List<Construction> _construction = new List<Construction>();

        public IReadOnlyList<AssignmentSlot> Slots => _slots;
        public IReadOnlyList<Construction> ConstructionQueue => _construction;

        public BaseState(Roster roster, ResourceLedger resources, BalanceConfig balance)
        {
            Roster = roster ?? throw new ArgumentNullException(nameof(roster));
            Resources = resources ?? throw new ArgumentNullException(nameof(resources));
            Balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        public AssignmentSlot GetSlot(string slotId)
            => slotId != null && _slotsById.TryGetValue(slotId, out var s) ? s : null;

        public AssignmentSlot AddSlot(AssignmentSlotDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.Id)) return null;
            if (_slotsById.TryGetValue(definition.Id, out var existing)) return existing;
            var slot = new AssignmentSlot(definition);
            _slotsById.Add(slot.Id, slot);
            _slots.Add(slot);
            return slot;
        }

        public void StartConstruction(Construction construction)
        {
            if (construction != null) _construction.Add(construction);
        }

        // ---- Назначения ----
        /// <summary>
        /// Назначает напарника на позицию. Если он уже стоит на другой — переводит.
        /// Недоступен (в отряде/ранен/выбыл) — отказ. Совет даёт статус «в совете»,
        /// прочие — «на позиции».
        /// </summary>
        public AssignmentResult TryAssign(string companionId, string slotId)
        {
            var slot = GetSlot(slotId);
            if (slot == null) return AssignmentResult.SlotNotFound;
            if (!slot.Unlocked) return AssignmentResult.SlotLocked;

            var companion = Roster.Get(companionId);
            if (companion == null) return AssignmentResult.CompanionNotFound;
            if (!companion.IsAvailableForDuty) return AssignmentResult.CompanionUnavailable;

            if (slot.IsOccupied && slot.AssignedCompanionId != companionId)
                return AssignmentResult.SlotOccupied;

            if (companion.IsAssigned && companion.AssignedSlotId != slotId)
                Unassign(companion.AssignedSlotId);

            slot.AssignedCompanionId = companionId;
            companion.AssignedSlotId = slotId;
            companion.Status = slot.Definition.Section == BaseSectionType.Council
                ? CompanionStatus.OnCouncil
                : CompanionStatus.OnDuty;
            return AssignmentResult.Success;
        }

        public void Unassign(string slotId)
        {
            var slot = GetSlot(slotId);
            if (slot == null || !slot.IsOccupied) return;

            var companion = Roster.Get(slot.AssignedCompanionId);
            slot.AssignedCompanionId = null;
            if (companion != null)
            {
                companion.AssignedSlotId = null;
                if (companion.Status == CompanionStatus.OnDuty || companion.Status == CompanionStatus.OnCouncil)
                    companion.Status = CompanionStatus.InCamp;
            }
        }

        // ---- Продвижение времени ----
        /// <summary>
        /// Продвигает календарь на N дней: лечение раненых (естественное + Лазарет),
        /// достройка строек, пассивный рост населения. Возвращает сводку. Ресурсы
        /// НЕ производятся — они приходят с вылазок/квестов.
        /// </summary>
        public CycleReport AdvanceDays(int days)
        {
            if (days < 1) days = 1;
            var report = new CycleReport { FromDay = CurrentDay, DaysAdvanced = days };

            double dailyRecovery = Balance.NaturalRecoveryPerDay + InfirmaryRecoveryBonus();
            double totalRecovery = dailyRecovery * days;
            foreach (var c in Roster.All)
            {
                if (!c.IsInjured) continue;
                c.TickRecovery(totalRecovery);
                if (!c.IsInjured) report.Recovered.Add(c.Id);
            }

            for (int i = _construction.Count - 1; i >= 0; i--)
            {
                var con = _construction[i];
                con.RemainingDays -= days;
                if (!con.IsComplete) continue;

                if (!string.IsNullOrEmpty(con.UnlocksSlotId))
                {
                    var unlocked = GetSlot(con.UnlocksSlotId);
                    if (unlocked != null) unlocked.Unlocked = true;
                }
                report.ConstructionCompleted.Add(string.IsNullOrEmpty(con.DisplayName) ? con.Id : con.DisplayName);
                _construction.RemoveAt(i);
            }

            Population += Balance.PopulationGrowthPerDay * days;
            CurrentDay += days;
            report.ToDay = CurrentDay;
            report.Population = (int)Math.Floor(Population);
            return report;
        }

        /// <summary>Бонус к лечению в день, если Лазарет укомплектован медиком (лучший среди позиций Лазарета).</summary>
        private double InfirmaryRecoveryBonus()
        {
            double best = 0;
            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (slot.Definition.Section != BaseSectionType.Infirmary || !slot.IsOccupied) continue;
                var medic = Roster.Get(slot.AssignedCompanionId);
                if (medic == null) continue;
                double bonus = Balance.InfirmaryRecoveryPerDay
                               + medic.GetSkill(SkillType.Medicine) * Balance.MedicRecoveryPerSkillPoint;
                if (bonus > best) best = bonus;
            }
            return best;
        }
    }
}
