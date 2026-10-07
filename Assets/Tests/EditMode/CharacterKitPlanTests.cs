using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core.Characters;
using Game.Core.Characters.Creation;
using Game.Core.Session;
using Game.Gameplay.UI;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// План збирання моделі (Поправка №19) і вибір зовнішності на екрані створення (№19.3): чисті
    /// правила, які збирач сцени лише застосовує. Звіряються з маніфестом набору, щоб план ніколи не
    /// просив частину, якої немає у FBX.
    /// </summary>
    public class CharacterKitPlanTests
    {
        private static HashSet<string> Manifest(string gender)
        {
            string path = Path.Combine(Application.dataPath, "Art", "Characters", "Kit", "kit_manifest.txt");
            return new HashSet<string>(File.ReadAllLines(path).Where(l => l.StartsWith(gender + ":")).Select(l => l.Substring(2)));
        }

        [Test]
        public void Plan_ForEveryNamedCharacter_AsksOnlyForPartsThatExist()
        {
            foreach (var card in CastingRules.AllNamed())
            {
                var look = AppearanceCatalog.Named(card.Id);
                var plan = CharacterKitPlan.From(look, null);
                var m = Manifest(look.Gender == Gender.Female ? "f" : "m");
                Assert.IsTrue(m.Contains("culture:" + plan.BodyId.Substring(7)), card.Id + ": немає тіла " + plan.BodyId);
                foreach (var p in plan.Parts)
                    Assert.IsTrue(m.Contains(p.Part), card.Id + ": немає частини " + p.Part);
                foreach (var a in plan.Accents)
                    Assert.IsTrue(m.Any(x => CharacterKitPlan.MatchesAccent(x, a)), card.Id + ": немає акценту " + a);
            }
        }

        [Test]
        public void ClothesHideTheBodyZonesUnderThem_HeadAndHandsStayVisible()
        {
            var plan = CharacterKitPlan.From(AppearanceCatalog.Named("zakhar"), null); // довга сорочка до п'ят
            Assert.IsFalse(plan.ShowsBodyZone("torso"));
            Assert.IsFalse(plan.ShowsBodyZone("calves"));
            Assert.IsTrue(plan.ShowsBodyZone("head"));
            Assert.IsTrue(plan.ShowsBodyZone("hands"));
        }

        [Test]
        public void Helmet_HidesHair_ButNotBeard_EquippedWeaponReplacesSignature()
        {
            var look = AppearanceCatalog.Named("maksym"); // сокирка, коротке волосся, вуса
            var plan = CharacterKitPlan.From(look, new[] { "helm_spangen", "wpn_sword", "mail" });
            string tint;
            Assert.IsFalse(plan.Wants(look.Hair, out tint), "шолом ховає зачіску");
            Assert.IsTrue(plan.Wants("moustache", out tint), "вуса лишаються");
            Assert.AreEqual(look.HairColor, tint, "борода й вуса фарбуються кольором волосся");
            Assert.IsFalse(plan.Wants("wpn_axe", out tint), "надітий меч замінює впізнавану сокирку");
            Assert.IsTrue(plan.Wants("wpn_sword", out tint));
            Assert.IsTrue(plan.Wants("mail", out tint));
            Assert.IsFalse(plan.ShowsBodyZone("lowerarms"), "кольчуга з рукавами ховає передпліччя");
        }

        [Test]
        public void Accents_TurnOnTheirHelperPartsByPrefix()
        {
            var plan = CharacterKitPlan.From(AppearanceCatalog.Named("maksym"), null);
            string tint;
            Assert.IsTrue(plan.Wants("sash", out tint));
            Assert.IsTrue(plan.Wants("sash_tails", out tint), "складений акцент — разом з допоміжними частинами");
            Assert.IsNull(tint, "колір акценту запечений, не фарбується");
            Assert.IsFalse(plan.Wants("sashimi", out tint), "префікс — лише з підкресленням");
        }

        [Test]
        public void Female_NeverGetsFacialHair_EvenIfTheLookAsksForIt()
        {
            var look = AppearanceCatalog.Named("myroslava").Clone();
            look.FacialHair = "beard_full";
            string tint;
            Assert.IsFalse(CharacterKitPlan.From(look, null).Wants("beard_full", out tint));
            Assert.AreEqual("kit_f", CharacterKitPlan.From(look, null).KitId);
        }

        [Test]
        public void Covers_IsTheSameTableAsTheBlenderWardrobe()
        {
            // Python-оригінал — tools/blender/alpha_wardrobe.py (COVERS); тут — його непорожня частина.
            string py = File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "tools", "blender", "alpha_wardrobe.py"));
            foreach (var kv in CharacterKitPlan.Covers)
                StringAssert.Contains("\"" + kv.Key + "\": [" + string.Join(", ", kv.Value.Select(z => "\"" + z + "\"")) + "]", py,
                    "таблиця COVERS розійшлася з набором Blender: " + kv.Key);
        }

        // ---------------- екран створення ----------------

        [Test]
        public void LookModel_EveryCombination_GivesAValidAppearance()
        {
            foreach (var g in new[] { Gender.Male, Gender.Female })
            {
                var m = new CreationLookModel(AppearanceCatalog.DefaultProtagonist(g));
                for (int c = 0; c < m.Count(CreationLookField.Culture); c++, m.Step(CreationLookField.Culture, 1))
                    for (int o = 0; o < m.Count(CreationLookField.Outfit); o++, m.Step(CreationLookField.Outfit, 1))
                    {
                        m.Step(CreationLookField.Hair, 3); m.Step(CreationLookField.HairColor, 1);
                        m.Step(CreationLookField.OutfitColor, 2); m.Step(CreationLookField.FacialHair, 1);
                        var a = m.Build();
                        Assert.IsTrue(GameSession.IsValidAppearance(a), g + ": " + a.Encode());
                        Assert.AreEqual(g, a.Gender);
                    }
            }
        }

        [Test]
        public void LookModel_StepWrapsAround_AndLoadRoundTrips()
        {
            var m = new CreationLookModel(AppearanceCatalog.DefaultProtagonist(Gender.Male));
            int n = m.Count(CreationLookField.Culture);
            m.Step(CreationLookField.Culture, -1);
            Assert.AreEqual(n - 1, m.Index(CreationLookField.Culture), "назад з першого — на останній");
            m.Step(CreationLookField.Culture, 1);
            Assert.AreEqual(0, m.Index(CreationLookField.Culture));

            m.Step(CreationLookField.Culture, 2); m.Step(CreationLookField.Hair, 4); m.Step(CreationLookField.OutfitColor, 3);
            var built = m.Build();
            var again = new CreationLookModel(built);
            Assert.AreEqual(built.Encode(), again.Build().Encode(), "образ із сесії відновлюється тим самим вибором");
        }

        [Test]
        public void LookModel_SwitchingToFemale_DropsTheBeard_AndHidesTheRow()
        {
            var m = new CreationLookModel(AppearanceCatalog.DefaultProtagonist(Gender.Male));
            m.Step(CreationLookField.FacialHair, 1);
            m.SetGender(Gender.Female);
            Assert.IsFalse(m.Visible(CreationLookField.FacialHair));
            Assert.AreEqual("", m.Build().FacialHair);
            Assert.IsTrue(GameSession.IsValidAppearance(m.Build()));
        }

        [Test]
        public void LookModel_TextKeysExist()
        {
            var m = new CreationLookModel(AppearanceCatalog.DefaultProtagonist(Gender.Male));
            for (int i = 0; i < m.Count(CreationLookField.Culture); i++, m.Step(CreationLookField.Culture, 1))
                Assert.IsTrue(Game.Gameplay.Text.UkrainianText.Has(m.ValueKey(CreationLookField.Culture), Gender.Male), m.ValueKey(CreationLookField.Culture));
            for (int i = 0; i < m.Count(CreationLookField.FacialHair); i++, m.Step(CreationLookField.FacialHair, 1))
                Assert.IsTrue(Game.Gameplay.Text.UkrainianText.Has(m.ValueKey(CreationLookField.FacialHair), Gender.Male), m.ValueKey(CreationLookField.FacialHair));
            foreach (var key in new[] { "ui.creation.look", "ui.creation.culture", "ui.creation.hair", "ui.creation.hair_color",
                                        "ui.creation.facial", "ui.creation.outfit", "ui.creation.outfit_color", "ui.creation.hair.none",
                                        "ui.creation.outfit_color.default", "ui.creation.option", "ui.creation.rotate_hint" })
                Assert.IsTrue(Game.Gameplay.Text.UkrainianText.Has(key, Gender.Male), key);
        }
    }
}
