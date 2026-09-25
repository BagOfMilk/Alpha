using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Economy;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// G26: <c>CompanionStatus.Resting</c> був оголошений, але ніколи не
    /// присвоювався — «відпочиває в лазареті» не наставало. Вільний (не на
    /// посту) поранений, якого лікує лазарет, тепер переходить у Resting,
    /// поки рана не закрилася, і назад в Idle, коли закрилася.
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
            patient.InjuryPoints = 1000; // свідомо не долікується за один цикл
            patient.Status = CompanionStatus.Injured; // як після справжнього Wound()

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
            patient.InjuryPoints = 1; // долікується за перший же цикл
            patient.Status = CompanionStatus.Resting; // наче вже відпочивав минулі доби

            state.AdvanceCycle();

            Assert.AreEqual(CompanionStatus.Idle, patient.Status);
            Assert.AreEqual(0.0, patient.InjuryPoints);
        }

        [Test]
        public void AssignedInjured_StaysInjured_NotResting_WhileWorkingThroughIt()
        {
            var cfg = new BalanceConfig { FoodUpkeepPerCompanion = 0, BaseHealingPerCycle = 0 };
            var state = BuildWithHealer(cfg, out var patient);

            // Пацієнт займає свій власний пост і працює через рану —
            // це вже покрито BaseStateTests.InjuredCompanion_ProducesLess,
            // тут перевіряється саме ярлик статусу: він НЕ повинен стати Resting.
            var slot = new AssignmentSlotDefinition("bench", "Верстак", BaseSectionType.Workshop)
            {
                OutputKind = SlotOutputKind.Resource,
                OutputResource = Game.Core.Economy.ResourceType.Materials,
                PrimarySkill = SkillType.Mechanics
            };
            state.AddSlot(slot);
            state.TryAssign(patient.Id, "bench");
            patient.InjuryPoints = 1000;
            patient.Status = CompanionStatus.Injured; // явне поранення, як після Wound()

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
            // Свій слот, відкритий за замовчуванням (UnlockedByDefault=true) — не
            // залежимо від того, які пости в DefaultContent сьогодні закриті.
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
        /// Рев'ю B7: призначення — не тільки "дозволено", а й зобов'язане повернути
        /// ярлик статусу до Injured. Інакше той, хто тримає пост через рану,
        /// читався б як "відпочиває" (Resting) до повного одужання — тоді як
        /// власний коментар ApplyHealing обіцяє рівно протилежне.
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
            comp.InjuryPoints = 5; // Resting завжди означає "ще лікується"
            roster.Add(comp);

            state.TryAssign("r", "bench");

            Assert.AreEqual(CompanionStatus.Injured, comp.Status,
                "держит пост, ещё ранен — значит работает через рану, а не отдыхает");
        }
    }
}
