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

        /// <summary>
        /// Каждый объявленный ресурс должен где-то появляться и где-то деваться.
        ///
        /// Краны и сливы СОБИРАЮТСЯ ИЗ КОНТЕНТА, а не перечисляются в теле
        /// теста. Прошлая версия сверяла enum со словарём, вписанным сюда же, —
        /// такой тест выглядит забором, но ловит ровно одно: что кто-то добавил
        /// член enum и забыл дописать строчку в словарь. Убери завтра выход
        /// ферм — и он бы этого не заметил.
        ///
        /// Кран вылазки нельзя вычитать из контента слотов, поэтому он
        /// проверяется отдельно (Materials_ComeFromExpedition) и здесь
        /// добавляется тем же вызовом настоящего резолва.
        /// </summary>
        [Test]
        public void EveryResource_HasFaucetAndSink()
        {
            var cfg = Cfg();
            var slots = DefaultContent.AllSlots();

            var faucets = new HashSet<ResourceType>(slots
                .Where(s => s.OutputKind == SlotOutputKind.Resource)
                .Select(s => s.OutputResource));

            // Вылазка — кран материалов. Берём не из списка, а из настоящего
            // резолва: если она перестанет их приносить, тест это увидит.
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

            // Прокорм — слив еды. Тоже проверяется делом: гоняем цикл и смотрим,
            // убавилось ли в кошельке.
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
