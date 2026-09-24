using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Economy;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// G26: <c>CompanionStatus.Resting</c> был объявлен, но никогда не
    /// присваивался — «отдыхает в лазарете» не наступало. Свободный (не на
    /// посту) раненый, которого лечит лазарет, теперь переходит в Resting,
    /// пока рана не закрылась, и обратно в Idle, когда закрылась.
    /// </summary>
    public class RestingLifecycleTests
    {
        private static BaseState BuildWithHealer(BalanceConfig cfg, out Companion patient)
        {
            var roster = new Roster();
            var state = new BaseState(roster, new ResourceLedger(), cfg);
            foreach (var slot in Game.Core.DefaultContent.AllSlots()) state.AddSlot(slot);

            var healerArch = new CompanionArchetype("healer", "Знахарка");
            healerArch.SetSkill(SkillType.Medicine, 10);
            roster.Add(healerArch.CreateInstance("healer_1", cfg));
            state.TryAssign("healer_1", "infirmary_bed");

            var patientArch = new CompanionArchetype("patient", "Поранений");
            patient = patientArch.CreateInstance("patient_1", cfg);
            roster.Add(patient);

            return state;
        }

        [Test]
        public void UnassignedInjured_BecomesResting_WhileInfirmaryTreatsThem()
        {
            var cfg = new BalanceConfig { FoodUpkeepPerCompanion = 0, BaseHealingPerCycle = 0 };
            var state = BuildWithHealer(cfg, out var patient);
            patient.InjuryPoints = 1000; // заведомо не долечится за один цикл
            patient.Status = CompanionStatus.Injured; // как после настоящего Wound()

            state.AdvanceCycle();

            Assert.AreEqual(CompanionStatus.Resting, patient.Status,
                "лазарет лечит, рана ещё не закрыта, поста пациент не держит — он именно отдыхает");
            Assert.Greater(patient.InjuryPoints, 0);
        }

        [Test]
        public void RestingCompanion_GoesIdle_WhenFullyHealed()
        {
            var cfg = new BalanceConfig { FoodUpkeepPerCompanion = 0 };
            var state = BuildWithHealer(cfg, out var patient);
            patient.InjuryPoints = 1; // долечится за первый же цикл
            patient.Status = CompanionStatus.Resting; // как будто уже отдыхал прошлые сутки

            state.AdvanceCycle();

            Assert.AreEqual(CompanionStatus.Idle, patient.Status);
            Assert.AreEqual(0.0, patient.InjuryPoints);
        }

        [Test]
        public void AssignedInjured_StaysInjured_NotResting_WhileWorkingThroughIt()
        {
            var cfg = new BalanceConfig { FoodUpkeepPerCompanion = 0, BaseHealingPerCycle = 0 };
            var state = BuildWithHealer(cfg, out var patient);

            // Пациент занимает свой собственный пост и работает через рану —
            // это уже покрыто BaseStateTests.InjuredCompanion_ProducesLess,
            // здесь проверяется именно ярлык статуса: он НЕ должен стать Resting.
            var slot = new AssignmentSlotDefinition("bench", "Верстак", BaseSectionType.Workshop)
            {
                OutputKind = SlotOutputKind.Resource,
                OutputResource = Game.Core.Economy.ResourceType.Materials,
                PrimarySkill = SkillType.Mechanics
            };
            state.AddSlot(slot);
            state.TryAssign(patient.Id, "bench");
            patient.InjuryPoints = 1000;
            patient.Status = CompanionStatus.Injured; // явное ранение, как после Wound()

            state.AdvanceCycle();

            Assert.AreEqual(CompanionStatus.Injured, patient.Status,
                "держит пост — значит работает через рану, а не отдыхает");
        }

        [Test]
        public void RestingCompanion_IsStillPresent_ForChecks()
        {
            var roster = new Roster();
            var comp = new CompanionArchetype("r", "r").CreateInstance("r");
            comp.Status = CompanionStatus.Resting;
            roster.Add(comp);

            var adapter = new RosterAdapter(roster);
            Assert.IsTrue(adapter.PresentActors.Count == 1 && adapter.PresentActors[0].Id == "r",
                "отдыхающий физически в поселении — присутствие (IsPresentInSettlement) не должно ломаться на Resting");
        }

        [Test]
        public void RestingCompanion_CanStillBeAssigned_LikeInjuredAlreadyCan()
        {
            var roster = new Roster();
            var state = new BaseState(roster, new ResourceLedger(), new BalanceConfig { FoodUpkeepPerCompanion = 0 });
            // Свой слот, открытый по умолчанию (UnlockedByDefault=true) — не
            // зависим от того, какие посты в DefaultContent сегодня закрыты.
            state.AddSlot(new AssignmentSlotDefinition("bench", "Верстак", BaseSectionType.Workshop)
            {
                OutputKind = SlotOutputKind.Resource,
                OutputResource = Game.Core.Economy.ResourceType.Materials,
                PrimarySkill = SkillType.Mechanics
            });

            var comp = new CompanionArchetype("r", "r").CreateInstance("r");
            comp.Status = CompanionStatus.Resting;
            roster.Add(comp);

            var result = state.TryAssign("r", "bench");

            Assert.AreEqual(AssignmentResult.Success, result,
                "как и Injured, Resting не блокирует назначение — только Dead/OnMission блокируют его сегодня");
        }

        /// <summary>
        /// Ревью B7: назначение — не только "разрешено", но и обязано вернуть
        /// ярлык статуса к Injured. Иначе тот, кто держит пост через рану,
        /// читался бы как "отдыхает" (Resting) до полного излечения — пока
        /// собственный комментарий ApplyHealing обещает ровно обратное.
        /// </summary>
        [Test]
        public void RestingCompanion_Assigned_BecomesInjured_NotLeftResting()
        {
            var roster = new Roster();
            var state = new BaseState(roster, new ResourceLedger(), new BalanceConfig { FoodUpkeepPerCompanion = 0 });
            state.AddSlot(new AssignmentSlotDefinition("bench", "Верстак", BaseSectionType.Workshop)
            {
                OutputKind = SlotOutputKind.Resource,
                OutputResource = Game.Core.Economy.ResourceType.Materials,
                PrimarySkill = SkillType.Mechanics
            });

            var comp = new CompanionArchetype("r", "r").CreateInstance("r");
            comp.Status = CompanionStatus.Resting;
            comp.InjuryPoints = 5; // Resting всегда означает "ещё лечится"
            roster.Add(comp);

            state.TryAssign("r", "bench");

            Assert.AreEqual(CompanionStatus.Injured, comp.Status,
                "держит пост, ещё ранен — значит работает через рану, а не отдыхает");
        }
    }
}
