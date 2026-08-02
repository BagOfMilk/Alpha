using System.Collections.Generic;
using Game.Core;
using Game.Core.Combat;
using Game.Core.Items;
using Game.Gameplay.Content;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// SO-миграция контента (US-18.1): обёртки собирают те же определения, что
    /// код-константы; несериализуемые куски (резисты-словарь, IItemEffect)
    /// переживают DTO; засеянная библиотека валидна и эквивалентна Default*.
    /// </summary>
    public class ContentAssetTests
    {
        [Test]
        public void EnemyAsset_RebuildsResists_AndAbilityRefs()
        {
            var abilityAsset = ScriptableObject.CreateInstance<AbilityAsset>();
            abilityAsset.ability = DefaultContent.Lunge();

            var asset = ScriptableObject.CreateInstance<EnemyAsset>();
            asset.definition = DefaultContent.FeralGhoul();
            asset.definition.Abilities.Clear(); // как после сидера: способности — только ссылками
            asset.resists.Add(new ResistEntry { type = DamageType.Fire, multiplier = 1.5 });
            asset.abilities.Add(abilityAsset);

            var def = asset.ToDefinition();
            Assert.AreEqual(1.5, def.Resists.Multiplier(DamageType.Fire), 0.001, "резисты пережили DTO");
            Assert.AreEqual(1.0, def.Resists.Multiplier(DamageType.Toxin), 0.001);
            Assert.AreEqual(1, def.Abilities.Count);
            Assert.AreEqual("lunge", def.Abilities[0].Id, "способности — ссылки на общий пул (US-3.14)");

            Object.DestroyImmediate(abilityAsset);
            Object.DestroyImmediate(asset);
        }

        [Test]
        public void ItemAsset_RestoresSignatureEffect_FromDto()
        {
            var asset = ScriptableObject.CreateInstance<ItemAsset>();
            var def = DefaultItems.AegisPlate();
            asset.hasSignatureEffect = true;
            asset.signatureName = def.Effect.Name;
            asset.signatureModifiers = new List<Game.Core.Stats.StatModifier>(def.Effect.ExtraModifiers()).ToArray();
            def.Effect = null; // интерфейс не сериализуется — как в ассете
            asset.definition = def;

            var rebuilt = asset.ToDefinition();
            Assert.IsNotNull(rebuilt.Effect, "эффект именного восстановлен из DTO (US-6.1)");
            Assert.AreEqual("Несгибаемость", rebuilt.Effect.Name);

            Object.DestroyImmediate(asset);
        }

        [Test]
        public void SeededLibrary_Exists_Validates_AndMatchesDefaults()
        {
            var library = AssetDatabase.LoadAssetAtPath<ContentLibraryAsset>(
                "Assets/_Project/Content/ContentLibrary.asset");
            Assert.IsNotNull(library, "библиотека засеяна (Alpha → Seed Content Assets)");
            Assert.IsTrue(library.Validate(out var errors), string.Join("; ", errors));

            Assert.AreEqual(DefaultContent.AllTraits().Count, library.traits.Count);
            Assert.AreEqual(DefaultItems.AllDefinitions().Count, library.items.Count);
            Assert.AreEqual(DefaultContent.AbilityCatalog().Count, library.abilities.Count);
            Assert.AreEqual(DefaultContent.AllBackgrounds().Count, library.backgrounds.Count);
            Assert.AreEqual(5, library.enemies.Count);
            Assert.AreEqual(DefaultContent.IncidentPool().Count, library.incidents.Count,
                "в пуле ТОЛЬКО ежедневные инциденты — одноразовый всплеск не сеется (иначе повторяемый кризис)");

            // Каталог из ассетов эквивалентен кодовому по ключевым позициям.
            var catalog = library.BuildCatalog();
            Assert.IsNotNull(catalog.GetTrait("sharp_eye"));
            Assert.IsNotNull(catalog.GetItem("widowmaker"));
            Assert.IsNotNull(catalog.GetArc("arc_medic"));
            Assert.IsNotNull(catalog.GetItem("aegis_plate").Effect, "сигнатурный эффект пережил миграцию");

            // Врагов можно собрать из ассетной библиотеки — с резистами и пулом способностей.
            var ghoul = library.EnemyPool().Find(e => e.Id == "feral_ghoul");
            Assert.IsNotNull(ghoul);
            Assert.AreEqual(1.5, ghoul.Resists.Multiplier(DamageType.Fire), 0.001);
            Assert.IsTrue(ghoul.Abilities.Exists(a => a.Id == "lunge"), "способность врага — из общего пула");
        }
    }
}
