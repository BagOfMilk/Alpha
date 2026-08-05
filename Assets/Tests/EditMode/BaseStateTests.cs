using System.Collections.Generic;
using Game.Core;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Economy;
using Game.Core.Factions;
using Game.Core.Health;
using Game.Core.Stats;
using Game.Core.Threats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// База: назначения на позиции + продвижение времени (лечение в днях, стройка,
    /// население) + экономика города (US-7.1/15.1: стройка СЛИВАЕТ золото/строймат,
    /// спец-здания дают эффекты, тир растёт комбинацией условий).
    /// </summary>
    public class BaseStateTests
    {
        private static (BaseState state, Roster roster) MakeBase(BalanceConfig cfg = null)
        {
            cfg = cfg ?? new BalanceConfig();
            var roster = new Roster();
            var state = new BaseState(roster, new ResourceLedger(), cfg);
            return (state, roster);
        }

        private static Companion AddCompanion(Roster roster, string id, int medicine = 0)
        {
            var c = new Companion(id, new AttributeBlock(3, 3, 3, 3), 4);
            if (medicine > 0) c.Skills.Set(SkillType.Medicine, medicine);
            roster.Add(c);
            return c;
        }

        // ---- Назначения ----
        [Test]
        public void Assign_Succeeds_SetsOnDuty()
        {
            var (state, roster) = MakeBase();
            var c = AddCompanion(roster, "c");
            state.AddSlot(new AssignmentSlotDefinition("bench", "Верстак", BaseSectionType.Workshop));
            Assert.AreEqual(AssignmentResult.Success, state.TryAssign("c", "bench"));
            Assert.AreEqual(CompanionStatus.OnDuty, c.Status);
        }

        [Test]
        public void Assign_ToCouncil_SetsOnCouncil()
        {
            var (state, roster) = MakeBase();
            AddCompanion(roster, "c");
            state.AddSlot(new AssignmentSlotDefinition("seat", "Совет", BaseSectionType.Council));
            state.TryAssign("c", "seat");
            Assert.AreEqual(CompanionStatus.OnCouncil, roster.Get("c").Status);
        }

        [Test]
        public void Assign_LockedSlot_Fails()
        {
            var (state, roster) = MakeBase();
            AddCompanion(roster, "c");
            state.AddSlot(new AssignmentSlotDefinition("m", "Рынок", BaseSectionType.Market) { UnlockedByDefault = false });
            Assert.AreEqual(AssignmentResult.SlotLocked, state.TryAssign("c", "m"));
        }

        [Test]
        public void Assign_OccupiedSlot_Fails()
        {
            var (state, roster) = MakeBase();
            AddCompanion(roster, "a");
            AddCompanion(roster, "b");
            state.AddSlot(new AssignmentSlotDefinition("bench", "Верстак", BaseSectionType.Workshop));
            state.TryAssign("a", "bench");
            Assert.AreEqual(AssignmentResult.SlotOccupied, state.TryAssign("b", "bench"));
        }

        [Test]
        public void Reassign_MovesCompanion_FreesOldSlot()
        {
            var (state, roster) = MakeBase();
            AddCompanion(roster, "a");
            state.AddSlot(new AssignmentSlotDefinition("b1", "B1", BaseSectionType.Workshop));
            state.AddSlot(new AssignmentSlotDefinition("b2", "B2", BaseSectionType.Workshop));
            state.TryAssign("a", "b1");
            state.TryAssign("a", "b2");
            Assert.IsFalse(state.GetSlot("b1").IsOccupied);
            Assert.AreEqual("a", state.GetSlot("b2").AssignedCompanionId);
        }

        [Test]
        public void Assign_InjuredCompanion_Unavailable()
        {
            var (state, roster) = MakeBase();
            var c = AddCompanion(roster, "c");
            c.ApplyInjury(InjuryTier.Light, state.Balance, null);
            state.AddSlot(new AssignmentSlotDefinition("bench", "Верстак", BaseSectionType.Workshop));
            Assert.AreEqual(AssignmentResult.CompanionUnavailable, state.TryAssign("c", "bench"));
        }

        // ---- Продвижение времени ----
        [Test]
        public void Injured_HealsOverDays_NaturalOnly()
        {
            var cfg = new BalanceConfig { NaturalRecoveryPerDay = 1, InfirmaryRecoveryPerDay = 1, MedicRecoveryPerSkillPoint = 0.2 };
            var (state, roster) = MakeBase(cfg);
            var c = AddCompanion(roster, "c");
            c.ApplyInjury(InjuryTier.Serious, cfg, null); // 5 дней

            state.AdvanceDays(3);
            Assert.IsTrue(c.IsInjured); // вылечено 3, осталось 2

            state.AdvanceDays(2);
            Assert.IsFalse(c.IsInjured);
            Assert.AreEqual(CompanionStatus.InCamp, c.Status);
        }

        [Test]
        public void Infirmary_WithMedic_SpeedsHealing()
        {
            var cfg = new BalanceConfig { NaturalRecoveryPerDay = 1, InfirmaryRecoveryPerDay = 1, MedicRecoveryPerSkillPoint = 0.2 };
            var (state, roster) = MakeBase(cfg);
            var patient = AddCompanion(roster, "p");
            AddCompanion(roster, "m", medicine: 5);
            state.AddSlot(new AssignmentSlotDefinition("bed", "Койка", BaseSectionType.Infirmary) { RelevantSkill = SkillType.Medicine });
            state.TryAssign("m", "bed");
            patient.ApplyInjury(InjuryTier.Serious, cfg, null); // 5 дней

            // в день: natural 1 + (infirmary 1 + medicine 5*0.2=1) = 3 → 2 дня дают 6 ≥ 5
            var r = state.AdvanceDays(2);
            Assert.IsFalse(patient.IsInjured);
            Assert.Contains("p", r.Recovered);
        }

        [Test]
        public void Infirmary_InjuredMedic_GivesNoBonus()
        {
            var cfg = new BalanceConfig { NaturalRecoveryPerDay = 1, InfirmaryRecoveryPerDay = 1, MedicRecoveryPerSkillPoint = 0.2 };
            var (state, roster) = MakeBase(cfg);
            var patient = AddCompanion(roster, "p");
            var medic = AddCompanion(roster, "m", medicine: 5);
            state.AddSlot(new AssignmentSlotDefinition("bed", "Койка", BaseSectionType.Infirmary) { RelevantSkill = SkillType.Medicine });
            state.TryAssign("m", "bed");
            patient.ApplyInjury(InjuryTier.Serious, cfg, null); // 5 дней

            // Медик выбывает, УЖЕ стоя на посту: ранение слот не освобождает. Пока
            // бонус считался по «слот занят», пациент на койке лечил сам себя
            // ускоренно — и укомплектованность Лазарета ничего не стоила.
            medic.ApplyInjury(InjuryTier.Serious, cfg, null);

            state.AdvanceDays(2); // только natural: 2 < 5
            Assert.IsTrue(patient.IsInjured, "раненый медик бонуса не даёт");
            state.AdvanceDays(3);
            Assert.IsFalse(patient.IsInjured, "natural-лечение своё отработало");
        }

        [Test]
        public void Construction_Completes_UnlocksSlot_AndPopulationGrows()
        {
            var cfg = new BalanceConfig { PopulationGrowthPerDay = 1 };
            var (state, _) = MakeBase(cfg);
            state.AddSlot(new AssignmentSlotDefinition("stall", "Прилавок", BaseSectionType.Market) { UnlockedByDefault = false });
            state.StartConstruction(new Construction("c", "Рынок", BaseSectionType.Market, 4, "stall"));

            state.AdvanceDays(3);
            Assert.IsFalse(state.GetSlot("stall").Unlocked); // 3 < 4

            var r2 = state.AdvanceDays(2); // всего 5 ≥ 4
            Assert.IsTrue(state.GetSlot("stall").Unlocked);
            Assert.Contains("Рынок", r2.ConstructionCompleted);
            Assert.AreEqual(5, r2.Population); // 5 дней × 1/день
        }

        // ---- Экономика города (US-7.1/15.1): стройка стоит золото + строймат ----
        [Test]
        public void Construction_SpecialBuilding_SpendsGoldAndMaterials()
        {
            var cfg = new BalanceConfig();
            var (state, _) = MakeBase(cfg);
            state.Resources.Add(ResourceType.Gold, 100);
            state.Resources.Add(ResourceType.BuildingMaterial, 10);

            var result = state.StartConstruction(DefaultContent.Blueprint(BaseSectionType.Market, cfg));
            Assert.AreEqual(ConstructionStartResult.Success, result);
            Assert.AreEqual(100 - cfg.SpecialConstructionGold, state.Resources.Get(ResourceType.Gold));
            Assert.AreEqual(10 - cfg.SpecialConstructionMaterials, state.Resources.Get(ResourceType.BuildingMaterial),
                "строймат добывается вылазками и СЛИВАЕТСЯ стройкой (US-15.1)");
            Assert.AreEqual(1, state.ConstructionQueue.Count);
        }

        [Test]
        public void Construction_WithoutMaterials_Refused_NothingSpent()
        {
            var cfg = new BalanceConfig();
            var (state, _) = MakeBase(cfg);
            state.Resources.Add(ResourceType.Gold, 1000); // золота море, строймата нет

            var result = state.StartConstruction(DefaultContent.Blueprint(BaseSectionType.Temple, cfg));
            Assert.AreEqual(ConstructionStartResult.CannotAffordMaterials, result);
            Assert.AreEqual(0, state.ConstructionQueue.Count);
            Assert.AreEqual(1000, state.Resources.Get(ResourceType.Gold), "при отказе ничего не списывается");
        }

        [Test]
        public void Construction_SameSectionTwice_RefusedWhileQueued()
        {
            var cfg = new BalanceConfig();
            var (state, _) = MakeBase(cfg);
            state.Resources.Add(ResourceType.Gold, 200);
            state.Resources.Add(ResourceType.BuildingMaterial, 20);

            Assert.AreEqual(ConstructionStartResult.Success,
                state.StartConstruction(DefaultContent.Blueprint(BaseSectionType.Market, cfg)));
            Assert.AreEqual(ConstructionStartResult.AlreadyQueued,
                state.StartConstruction(DefaultContent.Blueprint(BaseSectionType.Market, cfg)),
                "повторный заказ того же здания — отказ, не двойное списание");
            Assert.AreEqual(200 - cfg.SpecialConstructionGold, state.Resources.Get(ResourceType.Gold));
            Assert.AreEqual(1, state.ConstructionQueue.Count);
        }

        [Test]
        public void BuildingEffects_StartAfterCompletion_NoRetroPay()
        {
            var cfg = new BalanceConfig { MarketGoldPerDay = 2, PopulationGrowthPerDay = 0 };
            var (state, _) = MakeBase(cfg);
            state.Resources.Add(ResourceType.Gold, 100);
            state.Resources.Add(ResourceType.BuildingMaterial, 10);
            state.StartConstruction(DefaultContent.Blueprint(BaseSectionType.Market, cfg)); // 10 дней, −40 зол

            state.AdvanceDays(10); // достроился В ЭТОМ чанке — ретроактивной выручки нет
            Assert.AreEqual(60, state.Resources.Get(ResourceType.Gold),
                "за дни, пока Рынок строился, он не платит");

            state.AdvanceDays(5);
            Assert.AreEqual(70, state.Resources.Get(ResourceType.Gold), "после достройки капает по дням");
        }

        [Test]
        public void Tavern_AcceleratesPopulationGrowth()
        {
            var cfg = new BalanceConfig { PopulationGrowthPerDay = 1, TavernPopulationGrowthMultiplier = 2 };
            var (plain, _) = MakeBase(cfg);
            var (tavern, _) = MakeBase(cfg);
            tavern.MarkBuilt(BaseSectionType.Tavern);

            plain.AdvanceDays(4);
            tavern.AdvanceDays(4);
            Assert.AreEqual(4.0, plain.Population, 0.001);
            Assert.AreEqual(8.0, tavern.Population, 0.001, "Таверна ускоряет рост (US-7.5)");
        }

        [Test]
        public void Market_DripsGold_ByDays()
        {
            var cfg = new BalanceConfig { MarketGoldPerDay = 2 };
            var (state, _) = MakeBase(cfg);
            state.MarkBuilt(BaseSectionType.Market);
            state.AdvanceDays(5);
            Assert.AreEqual(10, state.Resources.Get(ResourceType.Gold), "Рынок капает золото (US-7.1)");
        }

        [Test]
        public void Temple_CoolsTension_Fortifications_RaiseReadiness()
        {
            var cfg = new BalanceConfig
            {
                TensionPerDayPerTier = 0, IncidentChanceBase = 0, IncidentChancePerTension = 0,
                TempleTensionReliefPerDay = 0.5
            };
            var (state, _) = MakeBase(cfg);
            var threats = new ThreatSystem(cfg, new SeededRng(1), new List<IncidentDefinition>(),
                startingTension: 10);
            state.AttachThreats(threats);
            state.MarkBuilt(BaseSectionType.Temple);

            state.Resources.Add(ResourceType.Gold, 100);
            state.Resources.Add(ResourceType.BuildingMaterial, 10);
            Assert.AreEqual(ConstructionStartResult.Success,
                state.StartConstruction(DefaultContent.Blueprint(BaseSectionType.Fortifications, cfg)));

            state.AdvanceDays(cfg.ConstructionLargeDays); // Храм остужает; Укрепления достроились
            Assert.AreEqual(10 - 0.5 * cfg.ConstructionLargeDays, threats.Tension.Value, 0.001,
                "Храм снижает Напряжение (US-7.1)");
            Assert.AreEqual(cfg.ReadinessPerFortification, threats.Readiness.Value, 0.001,
                "достройка Укреплений даёт Готовность (US-11.4)");
            Assert.IsTrue(state.IsBuilt(BaseSectionType.Fortifications));
        }

        // ---- Тир города (US-7.6): комбинация население + спец-здания + репутация ----
        [Test]
        public void CityTier_TryAdvance_RequiresCombination()
        {
            var cfg = new BalanceConfig { PopulationGrowthPerDay = 1, TierPopulationPerStep = 10, TierReputationPerStep = 15 };
            var (state, _) = MakeBase(cfg);
            var factions = DefaultFactions.NewRegistry();

            Assert.IsFalse(state.TryAdvanceCityTier(factions), "пустой город не растёт");

            state.AdvanceDays(20);                     // население 20 ≥ 10 × след.тир(2)
            state.MarkBuilt(BaseSectionType.Workshop); // ядро спец-застройкой НЕ считается
            Assert.IsFalse(state.TryAdvanceCityTier(factions), "нужны спец-здание и репутация");

            state.MarkBuilt(BaseSectionType.Market);   // 1 спец ≥ (2−1)
            Assert.IsFalse(state.TryAdvanceCityTier(factions), "репутации ещё нет");

            factions.AdjustReputation(15);             // ≥ 15 × (2−1)
            Assert.IsTrue(state.TryAdvanceCityTier(factions));
            Assert.AreEqual(2, state.CityTier);
        }
    }
}
