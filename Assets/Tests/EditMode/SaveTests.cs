using Game.Core;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Economy;
using Game.Core.Factions;
using Game.Core.Items;
using Game.Core.Saves;
using Game.Core.Stats;
using Game.Core.Threats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Сейвы (US-16.1): полный round-trip кампании через JSON (как при F5→F9),
    /// фиделити роллов гира, гибрид/айронмен-гейт быстрого сейва.
    /// </summary>
    public class SaveTests
    {
        /// <summary>Собирает детерминированную кампанию с нетривиальным состоянием.</summary>
        private static Campaign BuildMutatedCampaign(BalanceConfig cfg)
        {
            var roster = new Roster();
            var medic = new Companion("medic", new AttributeBlock(3, 4, 5, 5), 4)
            { DisplayName = "Медик", IsProtagonist = true };
            medic.Skills.Set(SkillType.Medicine, 3);
            medic.AdjustLoyalty(20); // 50 → 70
            medic.Equipment.Equip(new ItemInstance(DefaultItems.ArmorVest(), Rarity.Rare, new ScriptedRng(2, 2)));
            roster.Add(medic);

            var brawler = new Companion("brawler", new AttributeBlock(6, 3, 2, 4), 4) { DisplayName = "Боец" };
            brawler.Skills.Set(SkillType.Melee, 4);
            roster.Add(brawler);

            var baseState = new BaseState(roster, new ResourceLedger(), cfg);
            foreach (var slot in DefaultContent.AllSlots()) baseState.AddSlot(slot);
            baseState.RestoreTime(7, 12.0, 2);
            baseState.Resources.Add(ResourceType.Gold, 250);
            baseState.Resources.Add(ResourceType.BuildingMaterial, 8);
            baseState.Resources.Add(ResourceType.CraftingMaterial, 5);

            var threats = new ThreatSystem(cfg, new ScriptedRng(),
                DefaultContent.IncidentPool(), DefaultContent.TensionSpikes(), startingTension: 30);
            threats.Readiness.Add(20);
            baseState.AttachThreats(threats);

            baseState.Inventory.Add(ItemInstance.NamedFrom(DefaultItems.Widowmaker()));
            baseState.TryAssign("medic", "infirmary_bed");

            var factions = DefaultFactions.NewRegistry();
            factions.Adjust(DefaultFactions.Garrison, 20);
            factions.AdjustReputation(15);
            factions.AddInfluence(3);

            var campaign = new Campaign(cfg, baseState, factions) { Ironman = true };
            campaign.Flags.Add("caravan_saved");
            campaign.Flags.Add("arc_medic_ch1");
            return campaign;
        }

        [Test]
        public void RoundTrip_ThroughJson_PreservesCampaign()
        {
            var cfg = new BalanceConfig();
            var original = BuildMutatedCampaign(cfg);

            // F5: снимок → JSON → файл-эквивалент; F9: JSON → снимок → восстановление.
            var data = SaveSystem.Capture(original);
            var json = UnityEngine.JsonUtility.ToJson(data);
            var data2 = UnityEngine.JsonUtility.FromJson<SaveData>(json);
            var loaded = SaveSystem.Restore(data2, cfg, ContentCatalog.Default());

            // База / экономика / шкалы.
            Assert.AreEqual(7, loaded.Base.CurrentDay);
            Assert.AreEqual(2, loaded.Base.CityTier);
            Assert.AreEqual(250, loaded.Base.Resources.Get(ResourceType.Gold));
            Assert.AreEqual(8, loaded.Base.Resources.Get(ResourceType.BuildingMaterial));
            Assert.AreEqual(30, loaded.Base.ThreatsSystem.Tension.Value, 0.001);
            Assert.AreEqual(20, loaded.Base.ThreatsSystem.Readiness.Value, 0.001);

            // Фракции / репутация / влияние.
            Assert.AreEqual(20, loaded.Factions.Get(DefaultFactions.Garrison).Value, 0.001);
            Assert.AreEqual(15, loaded.Factions.Reputation, 0.001);
            Assert.AreEqual(3, loaded.Factions.Influence);

            // Флаги / айронмен.
            Assert.IsTrue(loaded.Flags.Contains("caravan_saved"));
            Assert.IsTrue(loaded.Flags.Contains("arc_medic_ch1"));
            Assert.IsTrue(loaded.Ironman);

            // Ростер.
            var medic = loaded.Roster.Get("medic");
            Assert.IsNotNull(medic);
            Assert.AreEqual("Медик", medic.DisplayName);
            Assert.IsTrue(medic.IsProtagonist);
            Assert.AreEqual(5, medic.GetAttribute(AttributeType.Wits));
            Assert.AreEqual(3, medic.GetSkill(SkillType.Medicine));
            Assert.AreEqual(70, medic.Loyalty);
            Assert.AreEqual(4, loaded.Roster.Get("brawler").GetSkill(SkillType.Melee));

            // Назначение восстановлено.
            Assert.AreEqual("medic", loaded.Base.GetSlot("infirmary_bed").AssignedCompanionId);

            // Инвентарь: именной предмет на месте.
            Assert.AreEqual(1, loaded.Base.Inventory.Count);
            Assert.IsTrue(loaded.Base.Inventory.Items[0].Definition.IsNamed);
        }

        [Test]
        public void RoundTrip_PreservesGearRolls_Faithfully()
        {
            var cfg = new BalanceConfig();
            var original = BuildMutatedCampaign(cfg);
            int armorBefore = original.Roster.Get("medic").GetDerived(DerivedStat.Armor, cfg);

            var data = SaveSystem.Capture(original);
            var loaded = SaveSystem.Restore(
                UnityEngine.JsonUtility.FromJson<SaveData>(UnityEngine.JsonUtility.ToJson(data)),
                cfg, ContentCatalog.Default());

            int armorAfter = loaded.Roster.Get("medic").GetDerived(DerivedStat.Armor, cfg);
            Assert.AreEqual(4, armorBefore, "ArmorVest: ролл 2 × редкость Rare (×2) = 4 брони");
            Assert.AreEqual(armorBefore, armorAfter, "роллы гира сохраняются точно (без re-roll)");
        }

        [Test]
        public void NewGame_HasDefaultRoster_AndProtagonist()
        {
            var campaign = Campaign.NewGame(new BalanceConfig());
            Assert.AreEqual(6, campaign.Roster.Count);
            Assert.IsTrue(campaign.Roster.Get("leader").IsProtagonist);
        }

        [Test]
        public void QuickSave_Hybrid_BlockedInExpeditionUnderIronman()
        {
            var iron = Campaign.NewGame(new BalanceConfig { Ironman = true });
            iron.InExpedition = false;
            Assert.IsTrue(iron.CanQuickSave, "в базе — свободно");
            iron.InExpedition = true;
            Assert.IsFalse(iron.CanQuickSave, "в вылазке под айронменом — нельзя (US-16.1)");

            var casual = Campaign.NewGame(new BalanceConfig { Ironman = false });
            casual.InExpedition = true;
            Assert.IsTrue(casual.CanQuickSave, "без айронмена — всегда можно");
        }
    }
}
