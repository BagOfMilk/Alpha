using System;
using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Health;
using Game.Core.Stats;
using Game.Core.Traits;

namespace Game.Core.Characters
{
    /// <summary>
    /// Единый конечный автомат напарника (GDD §9 US-9.1). Смерть и переход в
    /// антагонисты необратимы.
    /// </summary>
    public enum CompanionStatus
    {
        InCamp = 0,     // в лагере, свободен
        OnDuty = 1,     // на позиции базы
        OnCouncil = 2,  // в совете
        InSquad = 3,    // в отряде / на вылазке
        Injured = 4,    // ранен, восстанавливается
        Antagonist = 5, // ушёл в антагонисты (необратимо)
        Dead = 6        // погиб (необратимо)
    }

    /// <summary>
    /// Рантайм-экземпляр именного напарника. Держит три независимые оси модели
    /// персонажа (GDD §2): атрибуты (почти статичны), скилы (растут за очки),
    /// трейты (слоты) — плюс отдельный вечный трек шрамов и боевой статус.
    ///
    /// Производные числа считаются из атрибутов и сворачиваются с модификаторами
    /// трейтов/шрамов (позже — гира/состояний) в ОДНОМ агрегаторе («один эффект —
    /// одна система», US-18.2).
    /// </summary>
    public sealed class Companion
    {
        public string Id { get; }
        public string DisplayName { get; set; }

        /// <summary>
        /// Протагонист-лидер (US-2.7). В обычном режиме защищён от смерти — только
        /// даунится (US-4.4); в айронмене его гибель = game over (Эпик 16).
        /// </summary>
        public bool IsProtagonist { get; set; }

        /// <summary>
        /// Лояльность к лидеру/делу (US-9.2): СКРЫТАЯ шкала 0..100 (старт 50). Игроку
        /// число не показывается — наружу полоса и реакции. Двигают квест-выборы и
        /// события (видимая социальная рябь, US-10.3).
        /// </summary>
        public int Loyalty { get; private set; } = 50;
        public LoyaltyBand LoyaltyBand => LoyaltyBands.Of(Loyalty);

        public AttributeBlock Attributes { get; }
        public SkillSet Skills { get; }
        public TraitSet Traits { get; }
        public ScarTrack Scars { get; }

        /// <summary>Надетый гир (Эпик 6): его модификаторы льются в единый агрегатор (Source = Gear).</summary>
        public Items.Equipment Equipment { get; }

        /// <summary>Открытые перки (US-3.10). Пересчитываются из каталога по порогам скилов.</summary>
        private readonly List<PerkDefinition> _perks = new List<PerkDefinition>();
        public IReadOnlyList<PerkDefinition> Perks => _perks;

        public int Level { get; private set; } = 1;
        public int Xp { get; private set; }

        /// <summary>Нераспределённые очки скилов — игрок тратит вручную (классов нет, респека нет).</summary>
        public int UnspentSkillPoints { get; private set; }

        public CompanionStatus Status { get; set; } = CompanionStatus.InCamp;

        /// <summary>Id позиции базы, на которую назначен (null если не назначен).</summary>
        public string AssignedSlotId { get; internal set; }

        /// <summary>Текущее ранение и остаток «дней восстановления».</summary>
        public InjuryTier CurrentInjury { get; private set; } = InjuryTier.None;
        public double RecoveryDaysRemaining { get; internal set; }

        public Companion(string id, AttributeBlock attributes, int traitSlots)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Attributes = attributes ?? new AttributeBlock();
            Skills = new SkillSet();
            Traits = new TraitSet(traitSlots);
            Scars = new ScarTrack();
            Equipment = new Items.Equipment();
        }

        public bool IsAssigned => !string.IsNullOrEmpty(AssignedSlotId);
        public bool IsInjured => CurrentInjury != InjuryTier.None || RecoveryDaysRemaining > 0;
        public bool IsAlive => Status != CompanionStatus.Dead;

        /// <summary>Может ли стоять на позиции/в совете (не в отряде, не ранен, не выбыл).</summary>
        public bool IsAvailableForDuty =>
            Status == CompanionStatus.InCamp || Status == CompanionStatus.OnDuty || Status == CompanionStatus.OnCouncil;

        // ---- Доступ к осям ----
        public int GetAttribute(AttributeType attribute) => Attributes.Get(attribute);
        public int GetSkill(SkillType skill) => Skills.Get(skill);

        /// <summary>Суммарный флэт-модификатор к проверке по скилу от трейтов и шрамов (US-2.6).</summary>
        public int CheckModifierFor(SkillType skill)
            => Traits.CheckModifierFor(skill) + Scars.CheckModifierFor(skill);

        // ---- Производные статы (через единый агрегатор) ----
        /// <summary>Модификаторы производных статов: гир + трейты + шрамы + перки (один агрегатор, US-18.2).</summary>
        public IEnumerable<StatModifier> CollectModifiers()
        {
            foreach (var m in Equipment.Modifiers()) yield return m;
            foreach (var m in Traits.CombatModifiers()) yield return m;
            foreach (var m in Scars.Modifiers()) yield return m;
            for (int i = 0; i < _perks.Count; i++)
                for (int j = 0; j < _perks[i].Modifiers.Count; j++)
                    yield return _perks[i].Modifiers[j];
        }

        /// <summary>
        /// Пересчитывает открытые перки по каталогу (порог скила, US-2.2). Зови после
        /// траты очков скилов. Идемпотентно: список строится заново — двойного счёта нет.
        /// </summary>
        public void RefreshPerks(IEnumerable<PerkDefinition> catalog)
        {
            _perks.Clear();
            if (catalog == null) return;
            foreach (var perk in catalog)
                if (perk != null && perk.UnlockedFor(this))
                    _perks.Add(perk);
        }

        public int GetDerived(DerivedStat stat, BalanceConfig cfg)
        {
            double baseValue = DerivedStatCalculator.BaseValue(stat, Attributes, cfg);
            return ModifierAggregator.Resolve(stat, baseValue, CollectModifiers());
        }

        public Dictionary<DerivedStat, int> EffectiveDerived(BalanceConfig cfg)
        {
            var bases = DerivedStatCalculator.BaseValues(Attributes, cfg);
            return ModifierAggregator.ResolveAll(bases, CollectModifiers());
        }

        // ---- Прокачка (XP → очки скилов) ----
        /// <summary>
        /// Начисляет опыт. Каждый полученный уровень кладёт очки скилов в общий
        /// нераспределённый пул — игрок тратит их вручную (классов нет, респека нет).
        /// </summary>
        public ProgressionMath.LevelUpResult GainXp(int amount, BalanceConfig cfg)
        {
            var result = ProgressionMath.GrantXp(Level, Xp, amount, cfg);
            if (result.LeveledUp)
                UnspentSkillPoints += result.LevelsGained * cfg.SkillPointsPerLevel;
            Level = result.Level;
            Xp = result.RemainderXp;
            return result;
        }

        /// <summary>Тратит одно нераспределённое очко на повышение скила. true — если получилось.</summary>
        public bool SpendSkillPoint(SkillType skill)
        {
            if (skill == SkillType.None || UnspentSkillPoints <= 0) return false;
            Skills.Raise(skill, 1);
            UnspentSkillPoints--;
            return true;
        }

        /// <summary>Сдвиг лояльности от выбора/события (US-9.2/10.3). Клампится 0..100.</summary>
        public void AdjustLoyalty(int delta)
            => Loyalty = Math.Max(0, Math.Min(100, Loyalty + delta));

        // ---- Ранения / восстановление / выбытие ----
        /// <summary>
        /// Применяет ранение тира. Берётся более тяжёлый из текущего и нового;
        /// Серьёзное+ присваивает переданный шрам (если включено в балансе).
        /// Возвращает true, если шрам был присвоен.
        /// </summary>
        public bool ApplyInjury(InjuryTier tier, BalanceConfig cfg, Scar scar = null)
        {
            if (tier == InjuryTier.None || Status == CompanionStatus.Dead) return false;

            if (tier > CurrentInjury) CurrentInjury = tier;
            RecoveryDaysRemaining = Math.Max(RecoveryDaysRemaining, DaysForTier(CurrentInjury, cfg));
            if (Status != CompanionStatus.Antagonist) Status = CompanionStatus.Injured;

            bool scarred = false;
            if (cfg.ScarOnSeriousOrAbove && tier >= InjuryTier.Serious && scar != null && !Scars.Has(scar.Id))
            {
                Scars.Add(scar);
                scarred = true;
            }
            return scarred;
        }

        /// <summary>Снимает «дни восстановления»; при нуле — здоров и снова в лагере.</summary>
        internal void TickRecovery(double recoveryDays)
        {
            if (RecoveryDaysRemaining <= 0) return;
            RecoveryDaysRemaining -= recoveryDays;
            if (RecoveryDaysRemaining <= 0)
            {
                RecoveryDaysRemaining = 0;
                CurrentInjury = InjuryTier.None;
                if (Status == CompanionStatus.Injured) Status = CompanionStatus.InCamp;
            }
        }

        public void Kill()
        {
            Status = CompanionStatus.Dead;
            AssignedSlotId = null;
        }

        public void TurnAntagonist()
        {
            if (Status == CompanionStatus.Dead) return;
            Status = CompanionStatus.Antagonist;
            AssignedSlotId = null;
        }

        public static double DaysForTier(InjuryTier tier, BalanceConfig cfg)
        {
            switch (tier)
            {
                case InjuryTier.Light:    return cfg.InjuryDaysLight;
                case InjuryTier.Serious:  return cfg.InjuryDaysSerious;
                case InjuryTier.Critical: return cfg.InjuryDaysCritical;
                default:                  return 0;
            }
        }
    }
}
