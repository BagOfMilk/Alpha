using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core.Characters;
using Game.Core.Characters.Creation;
using Game.Core.Combat;
using Game.Core.Economy;
using Game.Core.Items;
using Game.Core.Session;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Охоронці Поправки №19: броня і зброя видимі на моделі, надіте переживає сейв, надіта зброя —
    /// зброя в бою, кузня Збройні, зовнішність героя, образи іменних з першоджерел і різноманіття
    /// безіменних. Звірка ключів ядра з маніфестом модульного набору (що справді є у FBX).
    /// </summary>
    public class AppearanceAndGearTests
    {
        private static NewGameOptions Skip() => new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold };

        private static GameSession MorningSession()
        {
            var s = new GameSession();
            s.NewGame(Skip());
            var step = s.AdvanceScene();
            while (!step.IsFinished)
                step = step.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene();
            Assert.AreEqual(SessionState.Morning, s.State);
            return s;
        }

        private static HashSet<string> Manifest(string gender)
        {
            string path = Path.Combine(Application.dataPath, "Art", "Characters", "Kit", "kit_manifest.txt");
            Assert.That(File.Exists(path), Is.True, "немає маніфесту набору " + path);
            return new HashSet<string>(File.ReadAllLines(path)
                .Where(l => l.StartsWith(gender + ":")).Select(l => l.Substring(2)));
        }

        // ---------------- слоти й предмети ----------------

        [Test]
        public void EquipSlot_LegacyValues_AreStable_ForOldSaves()
        {
            Assert.AreEqual(0, (int)EquipSlot.Weapon);
            Assert.AreEqual(1, (int)EquipSlot.Armor);
            Assert.AreEqual(2, (int)EquipSlot.Accessory);
        }

        [Test]
        public void EveryVisibleItem_PointsAtAPartThatExistsInTheKit()
        {
            var m = Manifest("m");
            foreach (var d in DefaultItems.AllDefinitions())
            {
                if (string.IsNullOrEmpty(d.VisualKey)) continue;
                Assert.IsTrue(KitParts.IsBuilt(d.VisualKey), d.Id + ": ключ «" + d.VisualKey + "» не в KitParts");
                Assert.IsTrue(m.Contains(d.VisualKey), d.Id + ": частини «" + d.VisualKey + "» немає у FBX набору");
            }
        }

        [Test]
        public void EveryWeaponItem_HasACombatWeapon()
        {
            var catalog = DefaultCombatContent.PlayerWeaponCatalog();
            foreach (var d in DefaultItems.AllDefinitions())
            {
                if (string.IsNullOrEmpty(d.CombatWeaponId)) continue;
                Assert.AreEqual(EquipSlot.Weapon, d.Slot, d.Id);
                Assert.IsTrue(catalog.ContainsKey(d.CombatWeaponId), d.Id + " → " + d.CombatWeaponId);
            }
        }

        [Test]
        public void ForgeCatalog_CoversEverySlotOfTheDoll_ExceptAccessory()
        {
            var slots = new HashSet<EquipSlot>(DefaultItems.ForgeCatalog().Select(d => d.Slot));
            foreach (EquipSlot slot in System.Enum.GetValues(typeof(EquipSlot)))
                if (slot != EquipSlot.Accessory)
                    Assert.IsTrue(slots.Contains(slot), "кузня не кує нічого в слот " + slot);
        }

        [Test]
        public void TwoHandedWeapon_TakesTheShieldOff_AndShieldTakesTwoHandedOff_NothingIsLost()
        {
            var s = MorningSession();
            var shield = s.DebugGrantItem("round_shield");
            var spear = s.DebugGrantItem("spear");
            int before = s.GetStash().Count;

            Assert.IsTrue(s.Equip("maksym", shield.InstanceId, EquipSlot.Offhand));
            Assert.IsTrue(s.Equip("maksym", spear.InstanceId, EquipSlot.Weapon));
            var sheet = s.GetCharacterSheet("maksym").Equipment;
            Assert.AreEqual("spear", sheet.WeaponId);
            Assert.IsNull(sheet.OffhandId, "дворучний спис мав зняти щит");
            Assert.IsTrue(sheet.Slots.Single(x => x.Slot == EquipSlot.Offhand).BlockedByTwoHanded);

            Assert.IsTrue(s.Equip("maksym", shield.InstanceId, EquipSlot.Offhand));
            sheet = s.GetCharacterSheet("maksym").Equipment;
            Assert.AreEqual("round_shield", sheet.OffhandId);
            Assert.IsNull(sheet.WeaponId, "щит мав зняти дворучний спис");
            Assert.AreEqual(before - 1, s.GetStash().Count, "витіснене повертається в сташ, нічого не губиться");
        }

        [Test]
        public void EquippedWeapon_IsTheWeaponUsedInBattle()
        {
            var s = MorningSession();
            var musket = s.DebugGrantItem("musket");
            Assert.IsTrue(s.Equip("maksym", musket.InstanceId, EquipSlot.Weapon));
            var method = typeof(GameSession).GetMethod("ResolvePlayerUnit",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var unit = (PlayerUnitSource)method.Invoke(s, new object[] { "maksym" });
            Assert.AreEqual("weapon.musket", unit.Weapon.Id);
        }

        [Test]
        public void EquippedGear_SurvivesSaveLoad_IntoAFreshSession()
        {
            var s = MorningSession();
            var helm = s.DebugGrantItem("spangen_helm");
            var mail = s.DebugGrantItem("mail_hauberk");
            Assert.IsTrue(s.Equip("maksym", helm.InstanceId, EquipSlot.Head));
            Assert.IsTrue(s.Equip("maksym", mail.InstanceId, EquipSlot.Armor));
            string blob = s.SaveState(0);

            var fresh = new GameSession();
            fresh.NewGame(new NewGameOptions());
            fresh.RestoreFromBlob(blob);
            var sheet = fresh.GetCharacterSheet("maksym").Equipment;
            Assert.AreEqual("spangen_helm", sheet.HeadId);
            Assert.AreEqual("mail_hauberk", sheet.ArmorId);
            Assert.AreEqual(helm.InstanceId, sheet.Slots.Single(x => x.Slot == EquipSlot.Head).InstanceId,
                "той самий екземпляр (id), щоб команди після завантаження адресували ту саму річ");
        }

        [Test]
        public void Forge_NeedsArmoryAndResources_ThenPutsTheItemIntoTheStash()
        {
            var s = MorningSession();
            Assert.AreEqual(ForgeResult.ArmoryClosed, s.ForgeItem("arming_sword"));
            s.DebugMarkBuilt(Game.Core.Base.DefaultBuildings.Armory);
            s.DebugAddResource(ResourceType.Gold, 100);
            s.DebugAddResource(ResourceType.CraftComponent, 20);
            int before = s.GetStash().Count;
            Assert.AreEqual(ForgeResult.UnknownItem, s.ForgeItem("no_such_thing"));
            Assert.AreEqual(ForgeResult.Success, s.ForgeItem("arming_sword"));
            Assert.AreEqual(before + 1, s.GetStash().Count);
            Assert.IsTrue(s.GetForgeOffers().All(o => o.ArmoryOpen));
        }

        // ---------------- зовнішність ----------------

        [Test]
        public void KitParts_ThatAreBuilt_AllExistInTheKitManifest()
        {
            var m = Manifest("m"); var f = Manifest("f");
            foreach (var list in new[] { KitParts.Clothing, KitParts.Armor, KitParts.Weapons, KitParts.Hair, KitParts.Accents })
                foreach (var p in list)
                {
                    Assert.IsTrue(m.Contains(p), "m: немає частини " + p);
                    Assert.IsTrue(f.Contains(p), "f: немає частини " + p);
                }
            foreach (var p in KitParts.FacialHair) Assert.IsTrue(m.Contains(p), "m: немає " + p);
            foreach (var c in KitParts.Cultures)
            {
                Assert.IsTrue(m.Contains("culture:" + c), "m: немає тіла культури " + c);
                Assert.IsTrue(f.Contains("culture:" + c), "f: немає тіла культури " + c);
            }
        }

        [Test]
        public void EveryNamedCharacter_HasAValidLookFromTheSource()
        {
            foreach (var card in OpeningCast.All())
            {
                var a = AppearanceCatalog.Named(card.Id);
                Assert.IsNotNull(a, "немає образу для " + card.Id);
                Assert.IsTrue(GameSession.IsValidAppearance(a), "некоректний образ " + card.Id + ": " + a.Encode());
                if (card.Culture == SourceCulture.Ukrainian)
                    Assert.AreEqual("ukrainian", a.Culture, card.Id + ": культура образу ≠ культура першоджерела");
                Assert.Greater(a.Accents.Count, 0, card.Id + ": персонаж з історією має мати хоч один символ чи квірк");
            }
        }

        [Test]
        public void NamedCharacters_DoNotLookAlike()
        {
            var seen = new Dictionary<string, string>();
            foreach (var card in OpeningCast.All())
            {
                var a = AppearanceCatalog.Named(card.Id);
                string key = a.Hair + "|" + string.Join(",", a.Outfit.Select(o => o.Part + o.Color));
                Assert.IsFalse(seen.ContainsKey(key), card.Id + " виглядає як " + (seen.ContainsKey(key) ? seen[key] : ""));
                seen[key] = card.Id;
            }
        }

        [Test]
        public void Unnamed_AreStable_Valid_AndCoverManyCultures()
        {
            var cultures = new HashSet<string>();
            var looks = new HashSet<string>();
            for (int i = 0; i < 60; i++)
            {
                string id = "villager_" + i;
                var g = i % 2 == 0 ? Gender.Male : Gender.Female;
                var a = AppearanceCatalog.ForUnnamed(id, g);
                Assert.AreEqual(a.Encode(), AppearanceCatalog.ForUnnamed(id, g).Encode(), "той самий id — той самий вигляд");
                Assert.IsTrue(GameSession.IsValidAppearance(a), a.Encode());
                cultures.Add(a.Culture); looks.Add(a.Encode());
            }
            Assert.GreaterOrEqual(cultures.Count, 6, "безіменні мають бути з різних культур (№19.1)");
            Assert.GreaterOrEqual(looks.Count, 50, "безіменні не мають бути однаковими");
        }

        [Test]
        public void StarterOutfits_ThreePerCulture_ForBothGenders_AllValid()
        {
            foreach (var c in KitParts.Cultures)
                foreach (var g in new[] { Gender.Male, Gender.Female })
                {
                    var list = AppearanceCatalog.StarterOutfits(c, g);
                    Assert.AreEqual(3, list.Count, c);
                    foreach (var a in list)
                    {
                        a.Hair = AppearanceCatalog.HairOptions(g)[1];
                        Assert.IsTrue(GameSession.IsValidAppearance(a), c + "/" + g + ": " + a.Encode());
                    }
                }
        }

        [Test]
        public void Appearance_EncodeDecode_RoundTrips()
        {
            var a = AppearanceCatalog.Named("sindbad");
            Assert.AreEqual(a.Encode(), Appearance.Decode(a.Encode()).Encode());
            Assert.AreEqual(new Appearance().Encode(), Appearance.Decode("").Encode());
        }

        [Test]
        public void ProtagonistAppearance_IsChosenAtCreation_ValidatedAndSurvivesSave()
        {
            var s = new GameSession();
            s.NewGame(new NewGameOptions { HitRule = HitRuleKind.Threshold });
            Assert.AreEqual(SessionState.Creation, s.State);

            var bad = AppearanceCatalog.DefaultProtagonist(Gender.Female);
            bad.FacialHair = "beard_full";
            Assert.IsFalse(s.SetProtagonistAppearance(bad), "борода в жіночому наборі — некоректно");

            var look = AppearanceCatalog.StarterOutfits("west_african", Gender.Female)[1].Clone();
            look.Hair = "hair_afro01"; look.HairColor = AppearanceCatalog.HairBlack;
            Assert.IsTrue(s.SetProtagonistAppearance(look));
            Assert.AreEqual(Gender.Female, s.GetProtagonistCreationView().Gender, "стать образу стає статтю героя");
            s.ConfirmCreation();
            var step = s.AdvanceScene();
            while (!step.IsFinished)
                step = step.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene();
            string blob = s.SaveState(0);

            var fresh = new GameSession();
            fresh.NewGame(new NewGameOptions());
            fresh.RestoreFromBlob(blob);
            Assert.AreEqual(look.Encode(), fresh.GetAppearance(GameSession.ProtagonistId).Encode());
        }
    }
}
