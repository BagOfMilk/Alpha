using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Economy;
using Game.Core.Loop;
using Game.Core.Pressure;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Голод як стимул виходити назовні (Поправка №4).
    ///
    /// Тисне з двох боків: піднімає Напругу (гаситься Храмом і Укріпленнями,
    /// а ті будуються за компонент із вилазок) і роняє виробіток бази. Сенс у
    /// тому, що пересидіти голод удома не можна.
    /// </summary>
    public class HungerTests
    {
        private static BalanceConfig Cfg() => new BalanceConfig
        {
            FoodUpkeepPerCompanion = 5,
            HungryProductionMultiplier = 0.5,
            HungryRoleXpMultiplier = 0.5
        };

        private static (BaseState state, Companion comp) MakeBase(BalanceConfig cfg)
        {
            var roster = new Roster();
            var state = new BaseState(roster, new ResourceLedger(), cfg);

            var arch = new CompanionArchetype("eng", "Инженер");
            arch.SetSkill(SkillType.Mechanics, 10);
            var comp = arch.CreateInstance("eng_1");
            roster.Add(comp);

            state.AddSlot(new AssignmentSlotDefinition("bench", "Верстак", BaseSectionType.Workshop)
            {
                OutputKind = SlotOutputKind.Resource,
                OutputResource = ResourceType.Materials,
                PrimarySkill = SkillType.Mechanics,
                BaseOutput = 5,
                OutputPerPrimaryPoint = 1.0,
                OutputPerSecondaryPoint = 0
            });
            state.TryAssign(comp.Id, "bench");
            return (state, comp);
        }

        [Test]
        public void FullBelly_LeavesProductionAlone()
        {
            var cfg = Cfg();
            var (state, _) = MakeBase(cfg);
            state.Resources.Add(ResourceType.Food, 100);

            state.AdvanceCycle();
            Assert.IsFalse(state.WasHungryLastCycle);

            var second = state.AdvanceCycle();
            Assert.AreEqual(15, second.Produced[ResourceType.Materials], "сытый день — полная выработка");
        }

        /// <summary>Просідання приходить наступним циклом: прокорм рахується останнім кроком дня.</summary>
        [Test]
        public void HungryDay_HalvesNextCycleOutput()
        {
            var cfg = Cfg();
            var (state, _) = MakeBase(cfg);

            var first = state.AdvanceCycle();
            Assert.IsTrue(first.FoodShortage, "еды не было — день голодный");
            Assert.AreEqual(15, first.Produced[ResourceType.Materials], "в сам голодный день выработка ещё полная");
            Assert.IsTrue(state.WasHungryLastCycle);

            var second = state.AdvanceCycle();
            Assert.AreEqual(8, second.Produced[ResourceType.Materials], "15 * 0.5 = 7.5 -> 8");
        }

        [Test]
        public void FeedingAfterHunger_RestoresProduction()
        {
            var cfg = Cfg();
            var (state, _) = MakeBase(cfg);

            state.AdvanceCycle();                                  // голодний
            state.Resources.Add(ResourceType.Food, 100);
            state.AdvanceCycle();                                  // просівший, але нагодований
            Assert.IsFalse(state.WasHungryLastCycle);

            var third = state.AdvanceCycle();
            Assert.AreEqual(15, third.Produced[ResourceType.Materials], "просадка держится ровно один цикл");
        }

        /// <summary>
        /// Темп: перша межа полоси — 200, голодний день дає 8, отже полоса
        /// змінюється на 25-й день голоду. Це і є заявлена ціна: голод тисне
        /// помітно, але не миттєво, і якір шкали (пасивний дрейф ≈ одна полоса
        /// за кампанію) не ламає — голод пасивним дрейфом не є.
        /// </summary>
        [Test]
        public void HungryDays_ShiftBandOnTwentyFifthDay()
        {
            var cfg = Cfg();
            var tension = new TensionState(cfg.Tension);
            var processor = new DayProcessor(tension, cfg, new IDayStep[] { new HungerStep() })
            {
                IsHungry = true
            };

            for (int i = 0; i < 24; i++) processor.Advance();
            Assert.AreEqual(TensionBand.Calm, tension.Band, "24 голодных дня — ещё в первой полосе");

            processor.Advance();
            Assert.Greater((int)tension.Band, (int)TensionBand.Calm, "25-й голодный день переводит полосу");
        }

        [Test]
        public void WellFedDays_DoNotRaiseTension()
        {
            var cfg = Cfg();
            var tension = new TensionState(cfg.Tension);
            var processor = new DayProcessor(tension, cfg, new IDayStep[] { new HungerStep() })
            {
                IsHungry = false
            };
            for (int i = 0; i < 20; i++) processor.Advance();

            Assert.AreEqual(TensionBand.Calm, tension.Band, "сытая база сама по себе Напряжение не растит");
        }

        /// <summary>Голод стоїть перед тіком Напруги — інакше інциденти зважаться за вчорашньою полосою.</summary>
        [Test]
        public void HungerStep_RunsBeforeTensionTick()
        {
            Assert.Less(DayStepOrder.Hunger, DayStepOrder.Tension);
        }

        /// <summary>Поправка №4 розширила закритий список рівно на одне значення.</summary>
        [Test]
        public void Hunger_IsAllowedToRaiseTension()
        {
            var cfg = new TensionBalance();
            CollectionAssert.Contains(cfg.AllowedRaising, TensionDriver.Hunger);
            CollectionAssert.DoesNotContain(cfg.AllowedLowering, TensionDriver.Hunger);
        }
    }
}
