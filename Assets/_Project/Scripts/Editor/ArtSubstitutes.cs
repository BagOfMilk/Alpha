using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Підміна моделей Kenney власними моделями треку V3 (Поправка №18): дерева, каміння, вогнище, лави,
    /// дрова, прапори, укриття бою. Збирачі сцени просять модель за старим шляхом Kenney — тут повертається
    /// префаб-обгортка з власною моделлю в потрібному масштабі (село — <see cref="Visual.ArtScale.World"/>,
    /// бій — <see cref="BattleScale"/>, щоб укриття пасувало до клітинки 1 од. і постаті 0,95 од.). Обгортки
    /// пишуться в <c>Assets/Art/Prefabs/</c> під час збирання сцени (не комітяться). Без власної моделі —
    /// null, і лишається Kenney.
    /// </summary>
    public static class ArtSubstitutes
    {
        private const string Models = "Assets/Art/Models/";
        private const string PrefabDir = "Assets/Art/Prefabs/";

        /// <summary>Масштаб у бою: напівукриття 1 м = 0,6 од. (по груди постаті), повне 1,8 м = 1,08 од.</summary>
        public const float BattleScale = 0.6f;

        // Kenney-файл → (власна модель, множник поверх масштабу сцени). Дерева менші за справжні 13-метрові
        // ялиці: навколо села вони тло, і ізометрична камера не має впиратися в крони.
        private static readonly Dictionary<string, KeyValuePair<string, float>> Village = new Dictionary<string, KeyValuePair<string, float>>
        {
            { "tree_default", P("tree_fir_large", 0.5f) },
            { "tree_oak", P("tree_fir_large", 0.45f) },
            { "tree_fat", P("tree_fir_medium", 0.6f) },
            { "tree_cone", P("tree_pine_tall", 0.5f) },
            { "tree_thin", P("tree_pine_tall", 0.45f) },
            { "tree_default_dark", P("tree_fir_medium", 0.55f) },
            { "rock_largeA", P("rock_boulder_big", 1f) },
            { "rock_largeB", P("rock_boulder_big", 1f) },
            { "rock_largeC", P("rock_boulder_big", 0.9f) },
            { "rock_largeE", P("rock_boulder_big", 0.8f) },
            { "rock_smallA", P("rock_boulder", 1f) },
            { "rock-small", P("rock_boulder", 1f) },
            { "cliff_blockHalf_rock", P("rock_boulder_big", 1.2f) },
            { "cliff_half_rock", P("rock_boulder_big", 1.2f) },
            { "campfire_stones", P("prop_campfire", 1f) },
            { "log", P("prop_bench", 1f) },
            { "log_stack", P("prop_woodpile", 1f) },
            { "banner-green", P("prop_banner", 1f) },
            { "banner-red", P("prop_banner", 1f) },
            // Частокіл села — тин (плетений пліт, як у селах Перевалу) замість білого паркану Kenney.
            // Множник 2,5 дає масштаб 1,0: ширина в сцені = довжині меша, тож крок частоколу (рахується з
            // меша) збігається з поставленим тином без щілин.
            { "fence", P("cover_half_wattle", 2.5f) },
        };

        private static readonly Dictionary<string, KeyValuePair<string, float>> Battle = new Dictionary<string, KeyValuePair<string, float>>
        {
            { "fence_simple", P("cover_half_wattle", 1f) },
            { "fence_simpleHigh", P("cover_half_wattle", 1f) },
            { "log", P("cover_half_wattle", 1f) },
            { "log_stack", P("prop_woodpile", 1f) },
            { "rock_largeA", P("cover_full_rock", 1f) },
            { "rock_largeB", P("rock_boulder_big", 0.7f) },
            { "rock_largeC", P("cover_full_rock", 1f) },
            { "rock_largeE", P("cover_full_rock", 1f) },
            { "cliff_half_rock", P("cover_full_rock", 1f) },
            { "tree_default", P("tree_fir_large", 0.35f) },
            { "tree_default_dark", P("tree_fir_medium", 0.45f) },
        };

        private static KeyValuePair<string, float> P(string art, float factor) => new KeyValuePair<string, float>(art, factor);

        /// <summary>Префаб-обгортка власної моделі замість Kenney за шляхом; null — підміни немає.</summary>
        public static GameObject Resolve(string kenneyPath, bool battle)
        {
            string name = Path.GetFileNameWithoutExtension(kenneyPath ?? string.Empty);
            KeyValuePair<string, float> sub;
            if (!(battle ? Battle : Village).TryGetValue(name, out sub)) return null;
            return ResolveArt(sub.Key, battle, sub.Value);
        }

        /// <summary>Префаб-обгортка власної моделі за її іменем (<c>Assets/Art/Models/&lt;id&gt;.fbx</c>); null — моделі немає.</summary>
        public static GameObject ResolveArt(string artId, bool battle, float factor = 1f)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Models + artId + ".fbx");
            if (model == null) return null;
            float scale = (battle ? BattleScale : Visual.ArtScale.World) * factor;
            var sub = new KeyValuePair<string, float>(artId, factor);
            string path = PrefabDir + sub.Key + (battle ? "_battle_" : "_village_") + Mathf.RoundToInt(scale * 1000f) + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            Directory.CreateDirectory(PrefabDir);
            var root = new GameObject(sub.Key);
            var child = (GameObject)PrefabUtility.InstantiatePrefab(model);
            child.transform.SetParent(root.transform, false);
            child.transform.localScale = Vector3.one * scale;
            // Моделі зі стадіями будівництва поза ділянкою — лише готова стадія.
            foreach (var t in child.GetComponentsInChildren<Transform>(true))
                if (t.name.Contains(".stage") && !t.name.EndsWith(".stage5")) t.gameObject.SetActive(false);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }
    }
}
