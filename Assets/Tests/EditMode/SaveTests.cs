using System.Collections.Generic;
using Game.Core;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Companions;
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
        public void RoundTrip_V2_PreservesCity_Arcs_Antagonists()
        {
            var cfg = new BalanceConfig();
            var campaign = Campaign.NewGame(cfg);
            campaign.InExpedition = true;
            campaign.Roster.Get("marksman").Status = CompanionStatus.InSquad; // «в вылазке»

            // Город: Рынок достроен (позиция открылась), Храм — в процессе.
            campaign.Base.Resources.Add(ResourceType.Gold, 500);
            campaign.Base.Resources.Add(ResourceType.BuildingMaterial, 20);
            Assert.AreEqual(ConstructionStartResult.Success,
                campaign.Base.StartConstruction(DefaultContent.Blueprint(BaseSectionType.Market, cfg)));
            campaign.AdvanceDays(cfg.ConstructionLargeDays);
            Assert.AreEqual(ConstructionStartResult.Success,
                campaign.Base.StartConstruction(DefaultContent.Blueprint(BaseSectionType.Temple, cfg)));

            // Арка медика: первая глава пройдена (флаг + индекс).
            var arc = new CompanionArcRun(DefaultArcs.MedicOldDebt(), campaign.Flags);
            arc.CompleteChapter();
            campaign.Arcs.Add(arc);

            // Перебежчик уходит с именным гиром.
            var traitor = campaign.Roster.Get("brawler");
            traitor.Equipment.Equip(ItemInstance.NamedFrom(DefaultItems.Widowmaker()));
            campaign.Antagonists.Add(DefectionSystem.Defect(traitor, campaign.Base));

            var json = UnityEngine.JsonUtility.ToJson(SaveSystem.Capture(campaign));
            var loaded = SaveSystem.Restore(
                UnityEngine.JsonUtility.FromJson<SaveData>(json), cfg, ContentCatalog.Default());

            // Вылазка не сериализуется: при загрузке она отменена, отряд дома —
            // иначе InSquad-статусы лочили бы ростер навсегда (софтлок).
            Assert.IsFalse(loaded.InExpedition, "прерванная вылазка отменена при загрузке");
            Assert.AreEqual(CompanionStatus.InCamp, loaded.Roster.Get("marksman").Status,
                "InSquad нормализован — напарник снова доступен");
            Assert.IsTrue(loaded.Roster.Get("marksman").IsAvailableForDuty);

            Assert.IsTrue(loaded.Base.IsBuilt(BaseSectionType.Market));
            Assert.IsTrue(loaded.Base.GetSlot("market_stall").Unlocked, "открытая позиция не запирается заново");
            Assert.AreEqual(1, loaded.Base.ConstructionQueue.Count, "идущая стройка в сейве");
            Assert.AreEqual(BaseSectionType.Temple, loaded.Base.ConstructionQueue[0].Section);

            Assert.AreEqual(1, loaded.Arcs.Count);
            Assert.AreEqual(1, loaded.Arcs[0].ChapterIndex, "прогресс арки в сейве (US-9.5)");
            Assert.IsTrue(loaded.Flags.Contains("arc_medic_ch1"));

            Assert.AreEqual(1, loaded.Antagonists.Count);
            var rec = loaded.Antagonists[0];
            Assert.AreEqual("brawler", rec.CompanionId);
            Assert.AreEqual(1, rec.CapturedGear.Count);
            Assert.IsTrue(rec.CapturedGear[0].Definition.IsNamed, "трофей вернётся с босса (US-9.4)");
            Assert.IsNotNull(rec.Weapon, "оружие босса восстановлено из гира");
            Assert.AreEqual(CompanionStatus.Antagonist, loaded.Roster.Get("brawler").Status);
        }

        [Test]
        public void RoundTrip_V2_RestoresCouncilState()
        {
            var cfg = new BalanceConfig();
            var campaign = Campaign.NewGame(cfg);
            campaign.Base.Resources.Add(ResourceType.Gold, 200);
            campaign.Factions.AddInfluence(5);
            var council = Game.Core.Council.DefaultCouncil.NewCouncil(
                campaign.Factions, campaign.Base.Resources, campaign.Base.ThreatsSystem, campaign.Base);
            campaign.AttachCouncil(council);

            // Инвестиция: −40 золота сейчас, +8/день × 10 дней потом (оплачено!).
            Assert.IsTrue(council.Execute(Game.Core.Council.DefaultCouncil.Investment).Success);
            int goldAfterPay = campaign.Base.Resources.Get(ResourceType.Gold);

            var json = UnityEngine.JsonUtility.ToJson(SaveSystem.Capture(campaign));
            var loaded = SaveSystem.Restore(
                UnityEngine.JsonUtility.FromJson<SaveData>(json), cfg, ContentCatalog.Default());

            Assert.IsNotNull(loaded.Council, "совет пере-подключён при загрузке");
            Assert.Greater(loaded.Council.CooldownRemaining(Game.Core.Council.DefaultCouncil.Investment), 0,
                "КД пережил сейв — F5/F9 не сбрасывает кулдауны");

            loaded.AdvanceDays(10);
            Assert.AreEqual(goldAfterPay + 80, loaded.Base.Resources.Get(ResourceType.Gold),
                "оплаченная Инвестиция капает и после загрузки");
        }

        // ---- Сид кампании и поток случайностей (v3): анти-save-scum ----
        [Test]
        public void NewGame_WithoutSeed_IsUnique()
        {
            var a = Campaign.NewGame(new BalanceConfig());
            var b = Campaign.NewGame(new BalanceConfig());
            Assert.AreNotEqual(a.Seed, b.Seed, "кампании больше не клоны фиксированного SeededRng(0)");
        }

        [Test]
        public void SameSeed_SameIncidentStream()
        {
            var cfg = new BalanceConfig { IncidentChanceBase = 100, IncidentChancePerTension = 0 };
            var a = Campaign.NewGame(cfg, null, seed: 123);
            var b = Campaign.NewGame(cfg, null, seed: 123);
            CollectionAssert.AreEqual(IncidentIds(a.AdvanceDays(10)), IncidentIds(b.AdvanceDays(10)),
                "тот же сид — тот же поток (реплей кампании)");
        }

        [Test]
        public void Load_ContinuesIncidentStream_NoSaveScum()
        {
            var cfg = new BalanceConfig { IncidentChanceBase = 100, IncidentChancePerTension = 0 };
            var campaign = Campaign.NewGame(cfg, null, seed: 777);
            campaign.AdvanceDays(5); // прокрутили начало потока

            var json = UnityEngine.JsonUtility.ToJson(SaveSystem.Capture(campaign));
            var future = IncidentIds(campaign.AdvanceDays(10)); // ветка А: играем дальше

            var loaded = SaveSystem.Restore(UnityEngine.JsonUtility.FromJson<SaveData>(json),
                cfg, ContentCatalog.Default());
            var reloaded = IncidentIds(loaded.AdvanceDays(10)); // ветка Б: load → играем дальше

            CollectionAssert.AreEqual(future, reloaded,
                "загрузка ПРОДОЛЖАЕТ поток случайностей — рероллить инциденты сейв-скамом нельзя (US-16.1)");
        }

        [Test]
        public void Load_DoesNotRefireSpentThresholdSpike()
        {
            var cfg = new BalanceConfig();
            var campaign = Campaign.NewGame(cfg, null, seed: 5);
            var fired = campaign.Base.ThreatsSystem.ApplyHiddenDelta(campaign.Base, 80); // порог 75 пересечён
            Assert.IsNotNull(fired, "всплеск сработал до сейва");

            var json = UnityEngine.JsonUtility.ToJson(SaveSystem.Capture(campaign));
            var loaded = SaveSystem.Restore(UnityEngine.JsonUtility.FromJson<SaveData>(json),
                cfg, ContentCatalog.Default());
            Assert.IsNull(loaded.Base.ThreatsSystem.ApplyHiddenDelta(loaded.Base, 1),
                "одноразовый всплеск не перевзводится загрузкой");
        }

        [Test]
        public void Migration_V2_MarksCrossedSpikesAsSpent()
        {
            var cfg = new BalanceConfig();
            var campaign = Campaign.NewGame(cfg, null, seed: 9);
            var data = SaveSystem.Capture(campaign);
            data.version = 2;                  // старый сейв: fired-набора ещё не было
            data.tension = 80;                 // порог 75 был пересечён ДО сейва
            data.firedSpikeThresholds.Clear();
            data.threatsRngState = null;

            var loaded = SaveSystem.Restore(data, cfg, ContentCatalog.Default());
            Assert.IsNull(loaded.Base.ThreatsSystem.ApplyHiddenDelta(loaded.Base, 1),
                "миграция v2: всплеск ≤ tension считается потраченным — одноразовый кризис не повторяется");
        }

        private static List<string> IncidentIds(CycleReport report)
        {
            var ids = new List<string>();
            foreach (var i in report.Incidents) ids.Add(i.IncidentId);
            return ids;
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
