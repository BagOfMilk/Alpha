using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Economy;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Розблокування закритого слота за ресурси.
    ///
    /// До появи цього механізму закритий слот («Погрузочный док») лишався
    /// закритим назавжди: властивість Unlocked була публічною, але перемикати її
    /// в ігровому коді не було кому. Заодно це перший справжній злив ресурсів
    /// крім прокорму — до нього все, крім їжі, тільки накопичувалося.
    /// </summary>
    public class SlotUnlockTests
    {
        private const string SlotId = "dock";

        private static (BaseState state, ResourceLedger ledger) MakeBaseWithLockedSlot()
        {
            var ledger = new ResourceLedger();
            var state = new BaseState(new Roster(), ledger, new BalanceConfig { FoodUpkeepPerCompanion = 0 });

            state.AddSlot(new AssignmentSlotDefinition(SlotId, "Погрузочный док", BaseSectionType.Storehouse)
            {
                OutputKind = SlotOutputKind.Resource,
                OutputResource = ResourceType.Gold,
                PrimarySkill = Game.Core.Stats.SkillType.Trade,
                BaseOutput = 3,
                UnlockedByDefault = false,
                UnlockCost = new Dictionary<ResourceType, int>
                {
                    { ResourceType.Gold, 40 },
                    { ResourceType.Materials, 25 }
                }
            });
            return (state, ledger);
        }

        [Test]
        public void Unlock_WithEnoughResources_OpensSlotAndSpends()
        {
            var (state, ledger) = MakeBaseWithLockedSlot();
            ledger.Add(ResourceType.Gold, 100);
            ledger.Add(ResourceType.Materials, 30);

            Assert.AreEqual(UnlockResult.Success, state.TryUnlockSlot(SlotId));
            Assert.IsTrue(state.GetSlot(SlotId).Unlocked);
            Assert.AreEqual(60, ledger.Get(ResourceType.Gold));
            Assert.AreEqual(5, ledger.Get(ResourceType.Materials));
        }

        /// <summary>Списання атомарне: не вистачило одного ресурсу — не витрачається нічого.</summary>
        [Test]
        public void Unlock_WhenOneResourceIsShort_SpendsNothing()
        {
            var (state, ledger) = MakeBaseWithLockedSlot();
            ledger.Add(ResourceType.Gold, 100);
            ledger.Add(ResourceType.Materials, 24); // на одиницю менше ціни

            Assert.AreEqual(UnlockResult.CannotAfford, state.TryUnlockSlot(SlotId));
            Assert.IsFalse(state.GetSlot(SlotId).Unlocked);
            Assert.AreEqual(100, ledger.Get(ResourceType.Gold));
            Assert.AreEqual(24, ledger.Get(ResourceType.Materials));
        }

        [Test]
        public void Unlock_AlreadyOpenSlot_Fails()
        {
            var (state, ledger) = MakeBaseWithLockedSlot();
            ledger.Add(ResourceType.Gold, 100);
            ledger.Add(ResourceType.Materials, 30);
            state.TryUnlockSlot(SlotId);

            Assert.AreEqual(UnlockResult.AlreadyUnlocked, state.TryUnlockSlot(SlotId));
            Assert.AreEqual(60, ledger.Get(ResourceType.Gold), "повторная попытка не должна списывать");
        }

        [Test]
        public void Unlock_UnknownSlot_Fails()
        {
            var (state, _) = MakeBaseWithLockedSlot();
            Assert.AreEqual(UnlockResult.SlotNotFound, state.TryUnlockSlot("нет такого"));
        }

        /// <summary>Закритий слот без ціни відкрити не можна — це заглушка під будівництво.</summary>
        [Test]
        public void Unlock_SlotWithoutPrice_Fails()
        {
            var ledger = new ResourceLedger();
            var state = new BaseState(new Roster(), ledger, new BalanceConfig());
            state.AddSlot(new AssignmentSlotDefinition("noprice", "Без цены", BaseSectionType.Storehouse)
            {
                UnlockedByDefault = false
            });

            Assert.AreEqual(UnlockResult.NoPriceDefined, state.TryUnlockSlot("noprice"));
        }

        /// <summary>Відкритий слот одразу приймає напарника — раніше це було недосяжним.</summary>
        [Test]
        public void Unlock_ThenAssign_Works()
        {
            var (state, ledger) = MakeBaseWithLockedSlot();
            ledger.Add(ResourceType.Gold, 40);
            ledger.Add(ResourceType.Materials, 25);

            var arch = new CompanionArchetype("hauler", "Грузчик");
            arch.SetSkill(Game.Core.Stats.SkillType.Trade, 6);
            var comp = arch.CreateInstance("hauler_1");
            state.Roster.Add(comp);

            Assert.AreEqual(AssignmentResult.SlotLocked, state.TryAssign(comp.Id, SlotId));
            Assert.AreEqual(UnlockResult.Success, state.TryUnlockSlot(SlotId));
            Assert.AreEqual(AssignmentResult.Success, state.TryAssign(comp.Id, SlotId));
        }
    }
}
