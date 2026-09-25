using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Economy;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public class BaseStateTests
    {
        private static (BaseState state, Companion comp) MakeBaseWithOneSlot(SlotOutputKind kind)
        {
            var roster = new Roster();
            var ledger = new ResourceLedger();
            var cfg = new BalanceConfig { FoodUpkeepPerCompanion = 0 }; // изолируем от расхода еды
            var state = new BaseState(roster, ledger, cfg);

            var arch = new CompanionArchetype("eng", "Инженер");
            arch.SetSkill(SkillType.Mechanics, 10);
            var comp = arch.CreateInstance("eng_1");
            roster.Add(comp);

            var def = new AssignmentSlotDefinition("bench", "Верстак", BaseSectionType.Workshop)
            {
                OutputKind = kind,
                OutputResource = ResourceType.Materials,
                PrimarySkill = SkillType.Mechanics,
                BaseOutput = 5, OutputPerPrimaryPoint = 1.0, OutputPerSecondaryPoint = 0
            };
            state.AddSlot(def);
            return (state, comp);
        }

        [Test]
        public void Assign_Then_Produce_AddsResources()
        {
            var (state, comp) = MakeBaseWithOneSlot(SlotOutputKind.Resource);
            Assert.AreEqual(AssignmentResult.Success, state.TryAssign(comp.Id, "bench"));

            var report = state.AdvanceCycle();
            // 5 (база) + 10 (механика) * 1.0 = 15
            Assert.AreEqual(15, state.Resources.Get(ResourceType.Materials));
            Assert.AreEqual(15, report.Produced[ResourceType.Materials]);
        }

        [Test]
        public void Assign_ToLockedSlot_Fails()
        {
            var (state, comp) = MakeBaseWithOneSlot(SlotOutputKind.Resource);
            state.GetSlot("bench").Unlocked = false;
            Assert.AreEqual(AssignmentResult.SlotLocked, state.TryAssign(comp.Id, "bench"));
        }

        [Test]
        public void Assign_OccupiedSlot_Fails()
        {
            var (state, comp) = MakeBaseWithOneSlot(SlotOutputKind.Resource);
            var other = new CompanionArchetype("eng2", "Инженер2").CreateInstance("eng_2");
            state.Roster.Add(other);

            Assert.AreEqual(AssignmentResult.Success, state.TryAssign(comp.Id, "bench"));
            Assert.AreEqual(AssignmentResult.SlotOccupied, state.TryAssign(other.Id, "bench"));
        }

        [Test]
        public void Reassign_MovesCompanion_FreesOldSlot()
        {
            var (state, comp) = MakeBaseWithOneSlot(SlotOutputKind.Resource);
            var def2 = new AssignmentSlotDefinition("bench2", "Верстак2", BaseSectionType.Workshop)
            {
                OutputResource = ResourceType.Materials, PrimarySkill = SkillType.Mechanics
            };
            state.AddSlot(def2);

            state.TryAssign(comp.Id, "bench");
            state.TryAssign(comp.Id, "bench2");

            Assert.IsFalse(state.GetSlot("bench").IsOccupied);
            Assert.AreEqual(comp.Id, state.GetSlot("bench2").AssignedCompanionId);
        }

        [Test]
        public void InjuredCompanion_ProducesLess()
        {
            var (state, comp) = MakeBaseWithOneSlot(SlotOutputKind.Resource);
            state.TryAssign(comp.Id, "bench");
            comp.InjuryPoints = 100; // тяжело ранен, но всё ещё на посту

            state.AdvanceCycle();
            // 15 * 0.5 (InjuredProductionMultiplier по умолчанию) = 7.5 -> 8
            Assert.AreEqual(8, state.Resources.Get(ResourceType.Materials));
        }

        [Test]
        public void FoodUpkeep_CausesShortage_WhenEmpty()
        {
            var roster = new Roster();
            var ledger = new ResourceLedger();
            var cfg = new BalanceConfig { FoodUpkeepPerCompanion = 5 };
            var state = new BaseState(roster, ledger, cfg);
            roster.Add(new CompanionArchetype("x", "X").CreateInstance("x_1"));

            var report = state.AdvanceCycle();
            Assert.IsTrue(report.FoodShortage);
        }

        /// <summary>
        /// R11 (seamsForD1 B7): постова XP протагоніста мусить банкуватись, а
        /// не витрачатись автоматично — інакше призначення протагоніста на пост
        /// тихо обходить R11 (єдину гілку раніше мав лише GameSession.GrantXp
        /// для бойової/квестової/інцидентної XP).
        /// </summary>
        [Test]
        public void AdvanceCycle_ProtagonistOnPost_LevelsUp_ReportsProtagonistLevelsGained()
        {
            var (state, comp) = MakeBaseWithOneSlot(SlotOutputKind.Resource);
            state.ProtagonistId = comp.Id;
            Assert.AreEqual(AssignmentResult.Success, state.TryAssign(comp.Id, "bench"));

            int levelBefore = comp.Level;
            CycleReport report = null;
            int totalGained = 0;
            for (int i = 0; i < 20 && comp.Level == levelBefore; i++)
            {
                report = state.AdvanceCycle();
                totalGained += report.ProtagonistLevelsGained;
            }

            Assert.Greater(comp.Level, levelBefore, "постова XP мусить піднімати рівень і протагоністу так само, як будь-кому іншому");
            Assert.Greater(totalGained, 0, "CycleReport.ProtagonistLevelsGained мусить відобразити підвищення рівня протагоніста");
            Assert.AreEqual(comp.Level - levelBefore, totalGained, "кожен здобутий рівень протагоніста мусить бути врахований рівно раз");
        }

        /// <summary>Контроль: без встановленого ProtagonistId (звичайний напарник) поле лишається нульовим.</summary>
        [Test]
        public void AdvanceCycle_NonProtagonistOnPost_LevelsUp_DoesNotReportProtagonistLevelsGained()
        {
            var (state, comp) = MakeBaseWithOneSlot(SlotOutputKind.Resource);
            Assert.AreEqual(AssignmentResult.Success, state.TryAssign(comp.Id, "bench"));

            int levelBefore = comp.Level;
            CycleReport report = null;
            for (int i = 0; i < 20 && comp.Level == levelBefore; i++)
            {
                report = state.AdvanceCycle();
                Assert.AreEqual(0, report.ProtagonistLevelsGained);
            }

            Assert.Greater(comp.Level, levelBefore, "звичайний напарник так само піднімає рівень постовою XP");
        }

        /// <summary>
        /// Блокер-фікс (знайдено 25.09.2026, лід): "хто на посту" зберігається
        /// ДВІЧІ — на боці напарника (<c>Companion.AssignedSlotId</c>, несе
        /// його <c>RosterAdapter.RestoreState</c>) і на боці слота (це поле,
        /// його несе лише <see cref="BaseState.TryAssign"/> під час гри).
        /// <see cref="BaseState.CaptureState"/>/<see cref="BaseState.RestoreState"/>
        /// не знають про сторону слота взагалі — на СВІЖОМУ інстансі (той
        /// самий сценарій, що ContinueGame/RestoreFromBlob на новому
        /// GameSession) слот лишається порожнім, навіть якщо напарник
        /// "вважає" себе призначеним. <see cref="BaseState.RestoreSlotOccupancy"/>
        /// пересобирає сторону слота з боку напарника — виклик належить
        /// <c>GameSession.ApplySave</c>, тест лише перевіряє сам метод.
        /// </summary>
        [Test]
        public void RestoreSlotOccupancy_SyncsSlotFromRosterAssignment_OnFreshInstance()
        {
            var (state, comp) = MakeBaseWithOneSlot(SlotOutputKind.Resource);
            comp.AssignedSlotId = "bench"; // імітує RosterAdapter.RestoreState на свіжому інстансі

            Assert.IsFalse(state.GetSlot("bench").IsOccupied,
                "до синхронізації свіжий слот не знає про призначення напарника — саме тому AdvanceCycle тихо пропускав зайнятий пост");

            state.RestoreSlotOccupancy();

            Assert.AreEqual(comp.Id, state.GetSlot("bench").AssignedCompanionId);
            var report = state.AdvanceCycle();
            Assert.AreEqual(15, report.Produced[ResourceType.Materials],
                "після синхронізації пост знову виробляє — не лишається порожнім, хоч і на свіжому інстансі");
        }

        /// <summary>Дзеркальний випадок: пересборка мусить ОЧИЩАТИ слот, чиє старе призначення не підтверджене жодним напарником (інакше залишок від попереднього сейву переживав би RestoreSlotOccupancy).</summary>
        [Test]
        public void RestoreSlotOccupancy_ClearsSlotsWithoutAMatchingCompanion()
        {
            var (state, comp) = MakeBaseWithOneSlot(SlotOutputKind.Resource);
            Assert.AreEqual(AssignmentResult.Success, state.TryAssign(comp.Id, "bench"));
            comp.AssignedSlotId = null; // напарник більше не "вважає" себе призначеним

            state.RestoreSlotOccupancy();

            Assert.IsFalse(state.GetSlot("bench").IsOccupied);
        }

        /// <summary>
        /// Блокер-фікс (знайдено 25.09.2026, лід): прапор голоду минулого
        /// циклу (Поправка №4, штраф виробітку/XP НАСТУПНОГО циклу) не
        /// входив до слепка взагалі — на свіжому інстансі завантаження
        /// безкарно знімало голод, накладений прямо перед сейвом.
        /// </summary>
        [Test]
        public void CaptureState_RestoreState_PreservesWasHungryLastCycle()
        {
            var roster = new Roster();
            var cfg = new BalanceConfig { FoodUpkeepPerCompanion = 5 };
            var state = new BaseState(roster, new ResourceLedger(), cfg);
            roster.Add(new CompanionArchetype("x", "X").CreateInstance("x_1"));

            var report = state.AdvanceCycle(); // порожній кошель -> голодний день
            Assert.IsTrue(report.FoodShortage);
            Assert.IsTrue(state.WasHungryLastCycle);

            string blob = state.CaptureState();

            var freshRoster = new Roster();
            freshRoster.Add(new CompanionArchetype("x", "X").CreateInstance("x_1"));
            var freshState = new BaseState(freshRoster, new ResourceLedger(), cfg);
            Assert.IsFalse(freshState.WasHungryLastCycle, "контроль: свіжий інстанс за замовчуванням не голодний");

            freshState.RestoreState(blob);
            Assert.IsTrue(freshState.WasHungryLastCycle,
                "прапор голоду минулого циклу мусить дійти крізь CaptureState/RestoreState на свіжий інстанс — " +
                "інакше штраф виробітку/XP наступного циклу знімається безкарно перезавантаженням");
        }
    }
}
