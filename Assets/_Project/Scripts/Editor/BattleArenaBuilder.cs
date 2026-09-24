using System.Collections.Generic;
using Game.Gameplay;
using UnityEditor;
using UnityEngine;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Пакет E2 — наповнення <c>World/BattleArena</c>: викликається РЕФЛЕКСІЄЮ
    /// з <c>GameSceneBuilder</c> (E1b), щоб той компілювався і без цього
    /// пакета (шов §5 TEST_BUILD.md):
    /// <c>Type.GetType("Game.Gameplay.EditorTools.BattleArenaBuilder, Game.Gameplay.Editor")
    /// ?.GetMethod("Build")?.Invoke(null, new object[]{ arenaRoot, arenaCamera })</c>.
    /// Сигнатура публічного статичного <see cref="Build"/> має лишатися
    /// точно такою — інакше рефлексія в E1b мовчки нічого не знайде.
    ///
    /// Присвоює <see cref="BattleArenaController"/>/<see cref="PortraitRig"/>
    /// готові пули префабів Kenney (Mini Characters — юніти/портрети; Nature
    /// Kit — укриття Half/Full), щоб рантайм-код (лінт-виключений, §
    /// BattleArenaController.cs) НІКОЛИ не кликав <c>AssetDatabase</c>/
    /// <c>PrefabUtility</c> (Editor-only, недоступні в білді гравця) — уся
    /// робота з асетами тут, одноразово, при збірці сцени.
    /// </summary>
    public static class BattleArenaBuilder
    {
        private const string Chars = "Assets/ThirdParty/Kenney/MiniCharacters/Models/";
        private const string Nature = "Assets/ThirdParty/Kenney/NatureKit/Models/";

        private static readonly string[] MaleModels =
        {
            "character-male-a.fbx", "character-male-b.fbx", "character-male-c.fbx",
            "character-male-d.fbx", "character-male-e.fbx", "character-male-f.fbx"
        };

        private static readonly string[] FemaleModels =
        {
            "character-female-a.fbx", "character-female-b.fbx", "character-female-c.fbx",
            "character-female-d.fbx", "character-female-e.fbx", "character-female-f.fbx"
        };

        // Half — прохідне з боку укриття (тин/колода), Full — суцільне (камінь/скеля).
        private static readonly string[] CoverHalfModels =
        {
            "fence_simple.fbx", "fence_simpleHigh.fbx", "log.fbx", "log_stack.fbx"
        };

        private static readonly string[] CoverFullModels =
        {
            "rock_largeA.fbx", "rock_largeC.fbx", "rock_largeE.fbx",
            "cliff_block_rock.fbx", "cliff_half_rock.fbx"
        };

        /// <summary>Викликається <c>GameSceneBuilder</c> (E1b) рефлексією — сигнатура фіксована швом, не змінювати без узгодження.</summary>
        public static void Build(GameObject arenaRoot, Camera arenaCamera)
        {
            if (arenaRoot == null)
            {
                Debug.LogWarning("[BattleArenaBuilder] arenaRoot == null — немає куди ставити презентер бою.");
                return;
            }

            var malePool = LoadAll(Chars, MaleModels);
            var femalePool = LoadAll(Chars, FemaleModels);
            var coverHalfPool = LoadAll(Nature, CoverHalfModels);
            var coverFullPool = LoadAll(Nature, CoverFullModels);

            var controller = arenaRoot.GetComponent<BattleArenaController>();
            if (controller == null) controller = arenaRoot.AddComponent<BattleArenaController>();
            controller.ArenaRoot = arenaRoot;
            controller.ArenaCamera = arenaCamera;
            controller.MaleCharacterPrefabs = malePool;
            controller.FemaleCharacterPrefabs = femalePool;
            controller.CoverHalfPrefabs = coverHalfPool;
            controller.CoverFullPrefabs = coverFullPool;

            var portraits = arenaRoot.GetComponent<PortraitRig>();
            if (portraits == null) portraits = arenaRoot.AddComponent<PortraitRig>();
            portraits.MaleCharacterPrefabs = malePool;
            portraits.FemaleCharacterPrefabs = femalePool;

            BuildStaticProps(arenaRoot);

            Debug.Log("[BattleArenaBuilder] арену наповнено: " + malePool.Length + " чол./" + femalePool.Length +
                       " жін. моделей, " + coverHalfPool.Length + " Half/" + coverFullPool.Length + " Full укриттів.");
        }

        /// <summary>
        /// Незмінне обрамлення арени — не тайли (ті будує рантайм-контролер
        /// під конкретний грид кожного бою): широка земля під ЛЮБИЙ розмір
        /// грида (макс. 10×10, §TEST_BUILD.md R9) з запасом і кілька каменів
        /// по кутах, щоб порожнеча навколо грида не була голою площиною.
        /// </summary>
        private static void BuildStaticProps(GameObject arenaRoot)
        {
            var existing = arenaRoot.transform.Find("StaticProps");
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var root = new GameObject("StaticProps");
            root.transform.SetParent(arenaRoot.transform, false);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "ArenaGround";
            ground.transform.SetParent(root.transform, false);
            ground.transform.localPosition = new Vector3(5f, -0.02f, 5f);
            ground.transform.localScale = new Vector3(1.8f, 1f, 1.8f); // Plane 10×10 юнітів на localScale=1

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit != null)
            {
                var mat = new Material(lit);
                mat.SetColor("_BaseColor", new Color(0.16f, 0.15f, 0.13f));
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.05f);
                var renderer = ground.GetComponent<Renderer>();
                if (renderer != null) renderer.sharedMaterial = mat;
            }

            var rockPrefab = Load(Nature + "rock_largeB.fbx");
            if (rockPrefab != null)
            {
                PlaceCorner(root, rockPrefab, new Vector3(-2f, 0f, -2f), 15f);
                PlaceCorner(root, rockPrefab, new Vector3(12f, 0f, -2f), 105f);
                PlaceCorner(root, rockPrefab, new Vector3(-2f, 0f, 12f), 255f);
                PlaceCorner(root, rockPrefab, new Vector3(12f, 0f, 12f), 195f);
            }
        }

        private static void PlaceCorner(GameObject root, GameObject prefab, Vector3 pos, float yaw)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (go == null) return;
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        private static GameObject[] LoadAll(string basePath, string[] fileNames)
        {
            var result = new List<GameObject>(fileNames.Length);
            foreach (var name in fileNames)
            {
                var go = Load(basePath + name);
                if (go != null) result.Add(go);
            }
            return result.ToArray();
        }

        private static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) Debug.LogWarning("[BattleArenaBuilder] модель не знайдено: " + path);
            return prefab;
        }
    }
}
