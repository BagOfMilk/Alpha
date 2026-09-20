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
    /// Замкнутость экономики. У каждого ресурса должен быть и кран, и слив —
    /// иначе он либо копится без смысла, либо его нечем взять, и обе беды
    /// выглядят в игре одинаково: «система есть, а играть в неё нельзя».
    /// </summary>
    public class EconomyClosureTests
    {
        private static BalanceConfig Cfg() => new BalanceConfig { FoodUpkeepPerCompanion = 1 };

        /// <summary>Материалы приходят в игру ровно из вылазки — и больше ниоткуда.</summary>
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
        /// Слив материалов существует: разблокировка дока стоит их по US-7.2.
        /// Если слив исчезнет, ресурс станет складом ради склада.
        /// </summary>
        [Test]
        public void Materials_HaveASink()
        {
            var costs = DefaultContent.AllSlots()
                .Where(s => s.UnlockCost != null)
                .SelectMany(s => s.UnlockCost.Keys);
            CollectionAssert.Contains(costs.ToList(), ResourceType.Materials);
        }

        /// <summary>Каждый объявленный ресурс должен где-то появляться и где-то деваться.</summary>
        [Test]
        public void EveryResource_HasFaucetAndSink()
        {
            var faucets = new Dictionary<ResourceType, string>
            {
                { ResourceType.Gold, "Рынок и Погрузочный док (US-7.1, Поправка №4.1)" },
                { ResourceType.Food, "Фермы поселения (Поправка №4)" },
                { ResourceType.Materials, "Вылазка (Э6.2, Приложение А)" },
            };
            var sinks = new Dictionary<ResourceType, string>
            {
                { ResourceType.Gold, "Разблокировка дока" },
                { ResourceType.Food, "Ежедневный прокорм" },
                { ResourceType.Materials, "Разблокировка дока" },
            };

            foreach (ResourceType r in Enum.GetValues(typeof(ResourceType)))
            {
                if (r == ResourceType.None) continue;
                Assert.IsTrue(faucets.ContainsKey(r), $"{r}: нет крана — откуда он берётся?");
                Assert.IsTrue(sinks.ContainsKey(r), $"{r}: нет слива — куда он девается?");
            }
        }

        /// <summary>
        /// Ограждение Поправки №4: еда не конвертируется ни во что. Проверяется
        /// по контенту, а не по обещанию — еда не должна стоять ни в одной цене.
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

        // Детерминизм вылазки отдельным тестом не проверяется: инвариант №1
        // уже держит ArchitectureGuardTests.Core_ContainsNoRandom, и он
        // сканирует весь Core/**, включая этот слой. Второй такой же тест
        // только создавал бы иллюзию двойной проверки.
    }
}
