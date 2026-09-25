using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Checks;
using Game.Core.Economy;
using Game.Core.Expeditions;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Замкнутість економіки. У кожного ресурсу повинен бути і кран, і злив —
    /// інакше він або копиться без сенсу, або його нема звідки взяти, і обидві біди
    /// виглядають у грі однаково: «система є, а грати в неї не можна».
    /// </summary>
    public class EconomyClosureTests
    {
        private static BalanceConfig Cfg() => new BalanceConfig { FoodUpkeepPerCompanion = 1 };

        /// <summary>Матеріали приходять у гру рівно з вилазки — і більше нізвідки.</summary>
        [Test]
        public void Materials_ComeFromExpedition()
        {
            var cfg = Cfg();
            var state = new BaseState(new Roster(), new ResourceLedger(), cfg);

            var arch = new CompanionArchetype("scout", "Разведчик");
            arch.SetSkill(SkillType.Survival, 6);
            var scout = arch.CreateInstance("scout_1", cfg);
            state.Roster.Add(scout);

            var site = DefaultSites.Outskirts();
            var party = new List<ISettlementActor> { new CompanionActorAdapter(scout, false, cfg) };

            Assert.AreEqual(0, state.Resources.Get(ResourceType.Materials), "до вылазки материалов нет");

            var result = ExpeditionResolver.Resolve(site, ExpeditionApproach.Quiet, party, new SiteLedger(), cfg);
            ExpeditionRunner.Complete(state, result);

            Assert.Greater(state.Resources.Get(ResourceType.Materials), 0, "вылазка — кран материалов");
        }

        /// <summary>
        /// Злив матеріалів існує: розблокування дока коштує їх за US-7.2.
        /// Якщо злив зникне, ресурс стане складом заради складу.
        /// </summary>
        [Test]
        public void Materials_HaveASink()
        {
            var costs = DefaultContent.AllSlots()
                .Where(s => s.UnlockCost != null)
                .SelectMany(s => s.UnlockCost.Keys);
            CollectionAssert.Contains(costs.ToList(), ResourceType.Materials);
        }

        /// <summary>
        /// Кожен оголошений ресурс повинен десь з'являтися і десь діватися.
        ///
        /// Крани і зливи ЗБИРАЮТЬСЯ З КОНТЕНТУ, а не перелічуються в тілі
        /// тесту. Попередня версія звіряла enum зі словником, вписаним тут же, —
        /// такий тест виглядає парканом, але ловить рівно одне: що хтось додав
        /// член enum і забув дописати рядок у словник. Прибери завтра вихід
        /// ферм — і він би цього не помітив.
        ///
        /// Кран вилазки не можна вирахувати з контенту слотів, тому він
        /// перевіряється окремо (Materials_ComeFromExpedition) і тут
        /// додається тим самим викликом справжнього резолву.
        /// </summary>
        [Test]
        public void EveryResource_HasFaucetAndSink()
        {
            var cfg = Cfg();
            var slots = DefaultContent.AllSlots();

            var faucets = new HashSet<ResourceType>(slots
                .Where(s => s.OutputKind == SlotOutputKind.Resource)
                .Select(s => s.OutputResource));

            // Вилазка — кран матеріалів. Беремо не зі списку, а зі справжнього
            // резолву: якщо вона перестане їх приносити, тест це побачить.
            var arch = new CompanionArchetype("scout", "Разведчик");
            arch.SetSkill(SkillType.Survival, 8);
            var scout = arch.CreateInstance("scout_1", cfg);
            var loot = ExpeditionResolver.Resolve(DefaultSites.Outskirts(), ExpeditionApproach.Quiet,
                new List<ISettlementActor> { new CompanionActorAdapter(scout, false, cfg) }, new SiteLedger(), cfg);
            if (loot.Materials > 0) faucets.Add(ResourceType.Materials);
            if (loot.Gold > 0) faucets.Add(ResourceType.Gold);

            var sinks = new HashSet<ResourceType>(slots
                .Where(s => s.UnlockCost != null)
                .SelectMany(s => s.UnlockCost.Keys));

            // Прокорм — злив їжі. Теж перевіряється ділом: ганяємо цикл і дивимось,
            // чи поменшало в гаманці.
            var state = new BaseState(new Roster(), new ResourceLedger(), cfg);
            state.Roster.Add(arch.CreateInstance("eater_1", cfg));
            state.Resources.Add(ResourceType.Food, 10);
            state.AdvanceCycle();
            if (state.Resources.Get(ResourceType.Food) < 10) sinks.Add(ResourceType.Food);

            foreach (ResourceType r in Enum.GetValues(typeof(ResourceType)))
            {
                if (r == ResourceType.None) continue;
                Assert.IsTrue(faucets.Contains(r), $"{r}: нет крана — откуда он берётся?");
                Assert.IsTrue(sinks.Contains(r), $"{r}: нет слива — куда он девается?");
            }
        }

        /// <summary>
        /// Огородження Поправки №4: їжа не конвертується ні в що. Перевіряється
        /// за контентом, а не за обіцянкою — їжа не повинна стояти в жодній ціні.
        /// </summary>
        [Test]
        public void Food_IsNotSpentOnAnything()
        {
            foreach (var slot in DefaultContent.AllSlots())
            {
                if (slot.UnlockCost == null) continue;
                CollectionAssert.DoesNotContain(slot.UnlockCost.Keys, ResourceType.Food,
                    $"«{slot.DisplayName}» берёт плату едой — Поправка №4 это запрещает");
            }
        }

        // Детермінізм вилазки окремим тестом не перевіряється: інваріант №1
        // уже тримає ArchitectureGuardTests.Core_ContainsNoRandom, і він
        // сканує весь Core/**, включно з цим шаром. Другий такий самий тест
        // лише створював би ілюзію подвійної перевірки.
    }
}
