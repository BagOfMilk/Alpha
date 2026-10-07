using System;
using System.Linq;
using Game.Core.Characters;
using Game.Core.Characters.Creation;
using Game.Core.Combat;
using Game.Core.Items;
using Game.Core.Session;
using Game.Gameplay.Text;
using Game.Gameplay.UI;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Правила «ляльки» спорядження (Поправка №19.3): кожен слот на екрані, надіте видно на моделі,
    /// сховок фільтрується за слотом, кузня каже причину відмови.
    /// </summary>
    public class InventoryModelTests
    {
        private static GameSession MorningSession()
        {
            var s = new GameSession();
            s.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold });
            var step = s.AdvanceScene();
            while (!step.IsFinished)
                step = step.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene();
            Assert.AreEqual(SessionState.Morning, s.State);
            return s;
        }

        [Test]
        public void DollOrder_HasEverySlotExactlyOnce_AndEachHasText()
        {
            var all = Enum.GetValues(typeof(EquipSlot)).Cast<EquipSlot>().ToList();
            CollectionAssert.AreEquivalent(all, InventoryModel.DollOrder);
            Assert.AreEqual(all.Count, InventoryModel.DollOrder.Distinct().Count());
            foreach (var s in InventoryModel.DollOrder)
                Assert.IsTrue(UkrainianText.Has(InventoryModel.SlotKey(s), Gender.Male), InventoryModel.SlotKey(s));
        }

        [Test]
        public void EquippedHelmetAndSword_ShowOnTheModel_HelmetHidesHair()
        {
            var session = MorningSession();
            var helm = session.DebugGrantItem(DefaultItems.ForgeCatalog().First(d => d.Slot == EquipSlot.Head).Id);
            Assert.IsTrue(session.Equip(GameSession.ProtagonistId, helm.InstanceId, EquipSlot.Head));

            var keys = InventoryModel.VisualKeys(session.GetCharacterSheet(GameSession.ProtagonistId).Equipment);
            CollectionAssert.Contains(keys, helm.Definition.VisualKey);
            var look = session.GetAppearance(GameSession.ProtagonistId);
            var plan = CharacterKitPlan.From(look, keys);
            string tint;
            Assert.IsTrue(plan.Wants(helm.Definition.VisualKey, out tint));
            if (!string.IsNullOrEmpty(look.Hair))
                Assert.IsFalse(plan.Wants(look.Hair, out tint), "шолом ховає зачіску");
        }

        [Test]
        public void StashFor_FiltersBySlot_NullShowsAll()
        {
            var session = MorningSession();
            var head = session.DebugGrantItem(DefaultItems.ForgeCatalog().First(d => d.Slot == EquipSlot.Head).Id);
            var feet = session.DebugGrantItem(DefaultItems.ForgeCatalog().First(d => d.Slot == EquipSlot.Feet).Id);
            var stash = session.GetStash();
            var onlyHead = InventoryModel.StashFor(stash, EquipSlot.Head);
            Assert.IsTrue(onlyHead.Contains(head));
            Assert.IsFalse(onlyHead.Contains(feet));
            Assert.AreEqual(stash.Count, InventoryModel.StashFor(stash, null).Count);
        }

        [Test]
        public void Wearers_StartWithTheProtagonist()
        {
            var session = MorningSession();
            var wearers = InventoryModel.Wearers(session.GetRosterView());
            Assert.IsNotEmpty(wearers);
            Assert.AreEqual(GameSession.ProtagonistId, wearers[0]);
        }

        [Test]
        public void ForgeFailure_HasAReasonText_ForEveryNonSuccess()
        {
            foreach (ForgeResult r in Enum.GetValues(typeof(ForgeResult)))
            {
                string key = InventoryModel.ForgeFailureKey(r);
                if (r == ForgeResult.Success) { Assert.IsNull(key); continue; }
                Assert.IsTrue(UkrainianText.Has(key, Gender.Male), r + ": " + key);
            }
        }

        [Test]
        public void ForgeFor_ListsOnlyTheSlot()
        {
            var session = MorningSession();
            var offers = session.GetForgeOffers();
            Assert.IsTrue(InventoryModel.ForgeFor(offers, EquipSlot.Offhand).All(o => o.Slot == EquipSlot.Offhand));
            Assert.AreEqual(offers.Count, InventoryModel.ForgeFor(offers, null).Count);
        }
    }
}
