using System;
using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Economy;
using Game.Core.Stats;

namespace Game.Core.Base
{
    /// <summary>Итог попытки начать стройку (US-7.1: стройка стоит золото/строймат).</summary>
    public enum ConstructionStartResult
    {
        Success = 0,
        CannotAffordGold = 1,
        CannotAffordMaterials = 2,
        AlreadyBuilt = 3,
        AlreadyQueued = 4
    }

    /// <summary>
    /// Система, живущая в календаре базы: тикается из <see cref="BaseState.AdvanceDays"/>
    /// (напр. Совет — КД действий и доход Инвестиции). Убирает рассинхрон «календарь
    /// двинулся, а КД нет» — оркестрация без отдельного бога-класса.
    /// </summary>
    public interface ITimeSink
    {
        void TickDays(int days);
    }

    /// <summary>
    /// Состояние базы и продвижение «мирного» времени (GDD §1). Связывает ростер,
    /// позиции, кошелёк (золото + 2 материала — наполняется вылазками, не базой) и
    /// баланс. <see cref="AdvanceDays"/> продвигает календарь: лечит раненых в днях
    /// (медик в Лазарете ускоряет), достраивает стройки, растит население, применяет
    /// эффекты построенных спец-зданий. Материалы здесь НЕ производятся — рост города
    /// требует выходить наружу (стройка СЛИВАЕТ строймат, US-15.1). Чистый C#.
    /// </summary>
    public sealed class BaseState
    {
        public Roster Roster { get; }
        public ResourceLedger Resources { get; }
        public BalanceConfig Balance { get; }

        public int CurrentDay { get; private set; }
        public double Population { get; private set; }

        /// <summary>Тир города (US-7.6, 1..MaxCityTier) — главный двигатель фонового Напряжения.</summary>
        public int CityTier { get; private set; } = 1;

        /// <summary>Скрытые угрозы (Эпик 11). Подключаются опционально через AttachThreats.</summary>
        public Threats.ThreatSystem ThreatsSystem { get; private set; }

        /// <summary>Сташ отряда (Эпик 6): лут с вылазок копится здесь — гир для экипировки/крафта.</summary>
        public Items.Inventory Inventory { get; } = new Items.Inventory();

        private readonly Dictionary<string, AssignmentSlot> _slotsById = new Dictionary<string, AssignmentSlot>();
        private readonly List<AssignmentSlot> _slots = new List<AssignmentSlot>();
        private readonly List<Construction> _construction = new List<Construction>();
        private readonly HashSet<BaseSectionType> _builtSections = new HashSet<BaseSectionType>();
        private readonly List<ITimeSink> _timeSinks = new List<ITimeSink>();

        public IReadOnlyList<AssignmentSlot> Slots => _slots;
        public IReadOnlyList<Construction> ConstructionQueue => _construction;

        /// <summary>Построенные здания (US-7.1): дают эффекты в AdvanceDays и считаются в тир (US-7.6).</summary>
        public IEnumerable<BaseSectionType> BuiltSections => _builtSections;
        public bool IsBuilt(BaseSectionType section) => _builtSections.Contains(section);

        /// <summary>Пометить здание построенным без стройки (старт кампании, восстановление из сейва).</summary>
        public void MarkBuilt(BaseSectionType section) => _builtSections.Add(section);

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

        /// <summary>
        /// Начинает стройку, СПИСЫВАЯ цену (US-7.1/15.1): золото и строймат должны
        /// быть в кошельке. Нулевая цена — бесплатно (совместимость/скрипты).
        /// </summary>
        public ConstructionStartResult StartConstruction(Construction construction)
        {
            if (construction == null) throw new ArgumentNullException(nameof(construction));
            if (IsBuilt(construction.Section)) return ConstructionStartResult.AlreadyBuilt; // уровни — US-7.2 [ПОЗЖЕ]
            for (int i = 0; i < _construction.Count; i++)
                if (_construction[i].Section == construction.Section)
                    return ConstructionStartResult.AlreadyQueued; // двойной заказ = двойное списание
            if (!Resources.CanAfford(Economy.ResourceType.Gold, construction.GoldCost))
                return ConstructionStartResult.CannotAffordGold;
            if (!Resources.CanAfford(Economy.ResourceType.BuildingMaterial, construction.BuildingMaterialCost))
                return ConstructionStartResult.CannotAffordMaterials;

            if (construction.GoldCost > 0) Resources.TrySpend(Economy.ResourceType.Gold, construction.GoldCost);
            if (construction.BuildingMaterialCost > 0)
                Resources.TrySpend(Economy.ResourceType.BuildingMaterial, construction.BuildingMaterialCost);
            _construction.Add(construction);
            return ConstructionStartResult.Success;
        }

        /// <summary>Восстановление идущей стройки из сейва (цена уже уплачена). Только для SaveSystem.</summary>
        internal void RestoreConstruction(Construction construction)
        {
            if (construction != null) _construction.Add(construction);
        }

        /// <summary>Подключает систему скрытых угроз — тикается из AdvanceDays.</summary>
        public void AttachThreats(Threats.ThreatSystem threats) => ThreatsSystem = threats;

        /// <summary>Подключает систему, живущую в календаре (Совет и т.п.) — тикается из AdvanceDays.</summary>
        public void AttachTimeSink(ITimeSink sink)
        {
            if (sink != null && !_timeSinks.Contains(sink)) _timeSinks.Add(sink);
        }

        /// <summary>Сырое продвижение тира (без условий — для скриптов/тестов). Условия — TryAdvanceCityTier.</summary>
        public void AdvanceCityTier()
        {
            if (CityTier < Balance.MaxCityTier) CityTier++;
        }

        /// <summary>
        /// Комбинация условий следующего тира (US-7.6): население + спец-здания +
        /// репутация города. Ни одно условие не заперто за одной фракцией (Эпик 10).
        /// </summary>
        public bool CityTierRequirementsMet(Factions.FactionRegistry factions)
        {
            if (CityTier >= Balance.MaxCityTier) return false;
            int next = CityTier + 1;

            if (Population < Balance.TierPopulationPerStep * next) return false;

            int specialBuilt = 0;
            foreach (var s in _builtSections)
                if (!IsCoreSection(s)) specialBuilt++;
            if (specialBuilt < next - 1) return false;

            double repNeeded = Balance.TierReputationPerStep * (next - 1);
            if (factions == null || factions.Reputation < repNeeded) return false;
            return true;
        }

        /// <summary>Продвижение тира по условиям (US-7.6). true — тир вырос.</summary>
        public bool TryAdvanceCityTier(Factions.FactionRegistry factions)
        {
            if (!CityTierRequirementsMet(factions)) return false;
            CityTier++;
            return true;
        }

        private static bool IsCoreSection(BaseSectionType s)
            => s == BaseSectionType.Council || s == BaseSectionType.Infirmary
               || s == BaseSectionType.Workshop || s == BaseSectionType.Storehouse;

        /// <summary>Кризис «отток населения» и подобные сливы (не ниже нуля).</summary>
        internal void RemovePopulation(double amount)
        {
            Population = Math.Max(0, Population - amount);
        }

        /// <summary>Восстановление времени/тира/населения из сейва (US-16.1). Только для SaveSystem.</summary>
        internal void RestoreTime(int day, double population, int cityTier)
        {
            CurrentDay = day < 0 ? 0 : day;
            Population = population < 0 ? 0 : population;
            CityTier = cityTier < 1 ? 1 : (cityTier > Balance.MaxCityTier ? Balance.MaxCityTier : cityTier);
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

            // Эффекты зданий считаются по составу НА НАЧАЛО периода: достроенное в
            // этом же вызове здание работает со следующего продвижения — иначе
            // чанковый AdvanceDays ретроактивно платил бы за дни до достройки.
            bool tavernActive = IsBuilt(BaseSectionType.Tavern);
            bool marketActive = IsBuilt(BaseSectionType.Market);
            bool templeActive = IsBuilt(BaseSectionType.Temple);

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
                _builtSections.Add(con.Section);
                if (con.Section == BaseSectionType.Fortifications)
                    ThreatsSystem?.Readiness.AddFortification(); // Укрепления → Готовность (US-11.4)
                report.ConstructionCompleted.Add(string.IsNullOrEmpty(con.DisplayName) ? con.Id : con.DisplayName);
                _construction.RemoveAt(i);
            }

            // Население: пассив + ускорение Таверной (US-7.5).
            double growth = Balance.PopulationGrowthPerDay * days;
            if (tavernActive) growth *= Balance.TavernPopulationGrowthMultiplier;
            Population += growth;

            // Эффекты спец-зданий (US-7.1): Рынок капает золото, Храм остужает город.
            if (marketActive && Balance.MarketGoldPerDay > 0)
                Resources.Add(Economy.ResourceType.Gold, Balance.MarketGoldPerDay * days);
            if (templeActive)
                ThreatsSystem?.Tension.Add(-Balance.TempleTensionReliefPerDay * days);

            // Скрытые угрозы: фоновый тик Напряжения + роллы инцидентов (Эпик 11).
            ThreatsSystem?.TickDays(this, days, report);

            // Системы, живущие в календаре (Совет: КД + доход Инвестиции) — без рассинхрона.
            for (int i = 0; i < _timeSinks.Count; i++) _timeSinks[i].TickDays(days);

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
