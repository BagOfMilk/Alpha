using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Economy;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Разблокировка закрытого слота за ресурсы.
    ///
    /// До появления этого механизма закрытый слот («Погрузочный док») оставался
    /// закрытым навсегда: свойство Unlocked было публичным, но переключать его
    /// в игровом коде было некому. Заодно это первый настоящий слив ресурсов
    /// кроме прокорма — до него всё, кроме еды, только копилось.
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
                OutputResource = ResourceType.Supplies,
                PrimaryAptitude = Game.Core.Stats.StatType.Logistics,
                BaseOutput = 3,
                UnlockedByDefault = false,
                UnlockCost = new Dictionary<ResourceType, int>
                {
                    { ResourceType.Supplies, 40 },
                    { ResourceType.Materials, 25 }
                }
            });
            return (state, ledger);
        }

        [Test]
        public void Unlock_WithEnoughResources_OpensSlotAndSpends()
        {
            var (state, ledger) = MakeBaseWithLockedSlot();
            ledger.Add(ResourceType.Supplies, 100);
            ledger.Add(ResourceType.Materials, 30);

            Assert.AreEqual(UnlockResult.Success, state.TryUnlockSlot(SlotId));
            Assert.IsTrue(state.GetSlot(SlotId).Unlocked);
            Assert.AreEqual(60, ledger.Get(ResourceType.Supplies));
            Assert.AreEqual(5, ledger.Get(ResourceType.Materials));
        }

        /// <summary>Списание атомарное: не хватило одного ресурса — не тратится ничего.</summary>
        [Test]
        public void Unlock_WhenOneResourceIsShort_SpendsNothing()
        {
            var (state, ledger) = MakeBaseWithLockedSlot();
            ledger.Add(ResourceType.Supplies, 100);
            ledger.Add(ResourceType.Materials, 24); // на единицу меньше цены

            Assert.AreEqual(UnlockResult.CannotAfford, state.TryUnlockSlot(SlotId));
            Assert.IsFalse(state.GetSlot(SlotId).Unlocked);
            Assert.AreEqual(100, ledger.Get(ResourceType.Supplies));
            Assert.AreEqual(24, ledger.Get(ResourceType.Materials));
        }

        [Test]
        public void Unlock_AlreadyOpenSlot_Fails()
        {
            var (state, ledger) = MakeBaseWithLockedSlot();
            ledger.Add(ResourceType.Supplies, 100);
            ledger.Add(ResourceType.Materials, 30);
            state.TryUnlockSlot(SlotId);

            Assert.AreEqual(UnlockResult.AlreadyUnlocked, state.TryUnlockSlot(SlotId));
            Assert.AreEqual(60, ledger.Get(ResourceType.Supplies), "повторная попытка не должна списывать");
        }

        [Test]
        public void Unlock_UnknownSlot_Fails()
        {
            var (state, _) = MakeBaseWithLockedSlot();
            Assert.AreEqual(UnlockResult.SlotNotFound, state.TryUnlockSlot("нет такого"));
        }

        /// <summary>Закрытый слот без цены открыть нельзя — это заглушка под стройку.</summary>
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

        /// <summary>Открытый слот сразу принимает напарника — раньше это было недостижимо.</summary>
        [Test]
        public void Unlock_ThenAssign_Works()
        {
            var (state, ledger) = MakeBaseWithLockedSlot();
            ledger.Add(ResourceType.Supplies, 40);
            ledger.Add(ResourceType.Materials, 25);

            var arch = new CompanionArchetype("hauler", "Грузчик");
            arch.BaseStats.Set(Game.Core.Stats.StatType.Logistics, 6);
            var comp = arch.CreateInstance("hauler_1");
            state.Roster.Add(comp);

            Assert.AreEqual(AssignmentResult.SlotLocked, state.TryAssign(comp.Id, SlotId));
            Assert.AreEqual(UnlockResult.Success, state.TryUnlockSlot(SlotId));
            Assert.AreEqual(AssignmentResult.Success, state.TryAssign(comp.Id, SlotId));
        }
    }
}
