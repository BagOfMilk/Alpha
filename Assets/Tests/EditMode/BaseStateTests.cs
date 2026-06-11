using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Economy;
using Game.Core.Health;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// База: назначения на позиции + продвижение времени (лечение в днях, стройка,
    /// население). Материалы тут НЕ производятся — экономика проверяется отдельно.
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
    }
}
