using System.Collections.Generic;
using System.Linq;
using Game.Core;
using Game.Core.Combat;
using Game.Core.Items;
using Game.Gameplay.Content;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// Сид контент-ассетов из код-констант Default* (US-18.1): единоразовая миграция
    /// «код → ассеты». Повторный запуск идемпотентен (существующие ассеты
    /// перезаписываются данными из кода). Запуск: меню Alpha → Seed Content Assets
    /// или batchmode -executeMethod Game.EditorTools.ContentSeeder.Seed.
    /// </summary>
    public static class ContentSeeder
    {
        private const string Root = "Assets/_Project/Content";

        [MenuItem("Alpha/Seed Content Assets")]
        public static void Seed()
        {
            EnsureFolder(Root);
            foreach (var sub in new[] { "Traits", "Scars", "Perks", "Abilities", "Items", "Enemies", "Incidents", "Backgrounds" })
                EnsureFolder(Root + "/" + sub);

            var library = CreateOrLoad<ContentLibraryAsset>(Root + "/ContentLibrary.asset");
            library.traits.Clear(); library.scars.Clear(); library.perks.Clear(); library.abilities.Clear();
            library.items.Clear(); library.enemies.Clear(); library.incidents.Clear(); library.backgrounds.Clear();

            foreach (var t in DefaultContent.AllTraits())
            {
                var a = CreateOrLoad<TraitAsset>($"{Root}/Traits/{t.Id}.asset");
                a.trait = t;
                EditorUtility.SetDirty(a);
                library.traits.Add(a);
            }

            foreach (var s in DefaultContent.AllScars())
            {
                var a = CreateOrLoad<ScarAsset>($"{Root}/Scars/{s.Id}.asset");
                a.scar = s;
                EditorUtility.SetDirty(a);
                library.scars.Add(a);
            }

            foreach (var p in DefaultContent.PerkCatalog())
            {
                var a = CreateOrLoad<PerkAsset>($"{Root}/Perks/{p.Id}.asset");
                a.perk = p;
                EditorUtility.SetDirty(a);
                library.perks.Add(a);
            }

            var abilityAssets = new Dictionary<string, AbilityAsset>();
            foreach (var ab in DefaultContent.AbilityCatalog())
            {
                var a = CreateOrLoad<AbilityAsset>($"{Root}/Abilities/{ab.Id}.asset");
                a.ability = ab;
                EditorUtility.SetDirty(a);
                abilityAssets[ab.Id] = a;
                library.abilities.Add(a);
            }

            foreach (var def in DefaultItems.AllDefinitions())
            {
                var a = CreateOrLoad<ItemAsset>($"{Root}/Items/{def.Id}.asset");
                // Интерфейс IItemEffect не сериализуется — раскладываем в DTO-поля.
                if (def.Effect != null)
                {
                    a.hasSignatureEffect = true;
                    a.signatureName = def.Effect.Name;
                    a.signatureModifiers = def.Effect.ExtraModifiers().ToArray();
                    def.Effect = null; // в сериализуемой части — только данные
                }
                else
                {
                    a.hasSignatureEffect = false;
                    a.signatureName = null;
                    a.signatureModifiers = new Game.Core.Stats.StatModifier[0];
                }
                a.definition = def;
                EditorUtility.SetDirty(a);
                library.items.Add(a);
            }

            var enemies = new List<EnemyDefinition>
            {
                DefaultContent.RaiderBruiser(), DefaultContent.ScavGunner(),
                DefaultContent.RustDrone(), DefaultContent.PlagueBearer(), DefaultContent.FeralGhoul()
            };
            foreach (var def in enemies)
            {
                var a = CreateOrLoad<EnemyAsset>($"{Root}/Enemies/{def.Id}.asset");
                // Словарь резистов не сериализуется — в DTO-список ассета.
                a.resists.Clear();
                foreach (DamageType type in System.Enum.GetValues(typeof(DamageType)))
                {
                    if (type == DamageType.True) continue;
                    double m = def.Resists != null ? def.Resists.Multiplier(type) : 1.0;
                    if (System.Math.Abs(m - 1.0) > 0.0001)
                        a.resists.Add(new ResistEntry { type = type, multiplier = m });
                }
                // Способности — ссылками на ОБЩИЙ пул (US-3.14), не встроенными копиями.
                a.abilities.Clear();
                foreach (var ab in def.Abilities)
                    if (ab != null && abilityAssets.TryGetValue(ab.Id, out var abilityAsset))
                        a.abilities.Add(abilityAsset);
                def.Abilities.Clear();
                a.definition = def;
                EditorUtility.SetDirty(a);
                library.enemies.Add(a);
            }

            // ТОЛЬКО ежедневный пул: insider_strike — ОДНОРАЗОВЫЙ пороговый всплеск
            // (TensionSpikes, код-контент); в зважений пул ему нельзя — иначе
            // повторяемый KillCompanion в полосе Critical.
            foreach (var inc in DefaultContent.IncidentPool())
            {
                var a = CreateOrLoad<IncidentAsset>($"{Root}/Incidents/{inc.Id}.asset");
                a.incident = inc;
                EditorUtility.SetDirty(a);
                library.incidents.Add(a);
            }

            foreach (var bg in DefaultContent.AllBackgrounds())
            {
                var a = CreateOrLoad<BackgroundAsset>($"{Root}/Backgrounds/{bg.Id}.asset");
                a.background = bg;
                EditorUtility.SetDirty(a);
                library.backgrounds.Add(a);
            }

            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            bool ok = library.Validate(out var errors);
            Debug.Log($"[ContentSeeder] Засеяно: {library.traits.Count} трейтов, {library.scars.Count} шрамов, " +
                      $"{library.perks.Count} перков, {library.abilities.Count} способностей, {library.items.Count} предметов, " +
                      $"{library.enemies.Count} врагов, {library.incidents.Count} инцидентов, {library.backgrounds.Count} бэкграундов. " +
                      (ok ? "Валидация: ок." : "ОШИБКИ: " + string.Join("; ", errors)));
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }

        private static T CreateOrLoad<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            return created;
        }
    }
}
