using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Збирає <c>Assets/Scenes/Game.unity</c> — ЄДИНУ сцену тестової збірки
    /// (R18): титул → створення → хаб → фінал, усе в одній сцені, без
    /// переходів між сценами. Меню «Alpha/Собрать сцену «Игра»» і той самий
    /// публічний метод — ціль для <c>-executeMethod</c> у
    /// <c>tools/build-unity.ps1</c> (§5 E1, TEST_BUILD.md).
    ///
    /// НЕ ФОРКАЄ <see cref="VillageShowcase"/>: хаб («Село на перевалі»)
    /// складається тими самими викликами <see cref="KitBuilder"/>, що й
    /// вітрина — House/Palisade/Forest/Anchor/Plot тут не переписані, лише
    /// заново скликані під коренем <c>World/Hub</c> і з
    /// <see cref="VillageStage"/> замість <see cref="VillageLife"/> (тут
    /// немає симуляції — GameSession годуватиме дані ззовні, коли приїде).
    ///
    /// Ієрархія (§5 E1):
    /// <c>Boot</c> (GameShell + AutoplayBootstrap) · <c>Sun</c> · <c>HubCamera</c> (ізо, увімкн.) ·
    /// <c>ArenaCamera</c> (згори, вимкн.) · <c>World/Hub</c> (KitBuilder +
    /// VillageStage) · <c>World/BattleArena</c> (порожній корінь, вимкн. —
    /// заповнить пакет E2) · <c>UI/Hub</c>, <c>UI/Battle</c> (порожні
    /// корені для майбутніх екранів; сьогоднішній IMGUI-шар не потребує
    /// об'єктів сцени, але організація готова наперед).
    /// </summary>
    public static class GameSceneBuilder
    {
        private const string Town = "Assets/ThirdParty/Kenney/FantasyTownKit/Models/";
        private const string Nature = "Assets/ThirdParty/Kenney/NatureKit/Models/";
        private const string Chars = "Assets/ThirdParty/Kenney/MiniCharacters/Models/";

        private const string ScenePath = "Assets/Scenes/Game.unity";

        [MenuItem("Alpha/Собрать сцену «Игра»")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var sun = BuildSun();
            var hubCamera = BuildHubCamera();
            var arenaCamera = BuildArenaCamera();

            var world = new GameObject("World");
            var hub = new GameObject("Hub");
            hub.transform.SetParent(world.transform, false);
            var battleArena = new GameObject("BattleArena");
            battleArena.transform.SetParent(world.transform, false);
            battleArena.SetActive(false);

            BuildHub(hub, sun, hubCamera);
            BuildBattleArenaHook(battleArena, arenaCamera);

            var ui = new GameObject("UI");
            var uiHub = new GameObject("Hub");
            uiHub.transform.SetParent(ui.transform, false);
            var uiBattle = new GameObject("Battle");
            uiBattle.transform.SetParent(ui.transform, false);

            var boot = new GameObject("Boot");
            boot.AddComponent<Game.Gameplay.GameShell>();
            boot.AddComponent<Game.Gameplay.AutoplayBootstrap>();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Гра] сцена зібрана: " + ScenePath);

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static void AddToBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int i = 0; i < scenes.Count; i++)
                if (scenes[i].path == ScenePath) return;

            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ================= світло/камери =================

        private static Light BuildSun()
        {
            var go = new GameObject("Sun");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.86f);
            light.intensity = 1.25f;
            light.shadows = LightShadows.Soft;
            go.transform.rotation = Quaternion.Euler(38f, 145f, 0f);
            return light;
        }

        /// <summary>Хабова камера — та сама ходибельна ізометрія US-7.7, що й у «Селі на перевалі».</summary>
        private static Camera BuildHubCamera()
        {
            var go = new GameObject("HubCamera");
            var cam = go.AddComponent<Camera>();

            cam.orthographic = true;
            cam.orthographicSize = 11f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.68f, 0.78f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;

            if (go.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>() == null)
                go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();

            go.transform.rotation = Quaternion.Euler(30f, 45f, 0f);
            go.transform.position = Quaternion.Euler(30f, 45f, 0f) * new Vector3(0f, 0f, -45f)
                                    + new Vector3(0f, 0f, -1f);
            go.tag = "MainCamera";
            return cam;
        }

        /// <summary>
        /// Арена бою — згори (тактичний бій дивиться на грид зверху, не
        /// ізометрично): вимкнена, поки бою нема кого показувати. Пакет E2
        /// (Battle presentation, §5 E1) підлаштує проєкцію під реальний грид.
        /// </summary>
        private static Camera BuildArenaCamera()
        {
            var go = new GameObject("ArenaCamera");
            var cam = go.AddComponent<Camera>();

            cam.orthographic = true;
            cam.orthographicSize = 12f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.10f, 0.12f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;

            if (go.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>() == null)
                go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();

            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.position = new Vector3(0f, 30f, 0f);

            go.SetActive(false);
            return cam;
        }

        /// <summary>
        /// Шов E1b/E2 (§ "Cross-package seams" TEST_BUILD.md): наповнює
        /// <c>World/BattleArena</c> через рефлексію, щоб цей файл компілювався
        /// БЕЗ жорсткого посилання на <c>Game.Gameplay.Editor</c> (складання
        /// E1b могло відбутися раніше, ніж E2 додав свій файл до трунку).
        /// Немає типу/методу в збірці — тиха відсутність (мовчазний null-check),
        /// не помилка: арена лишається порожнім вимкненим коренем, як і
        /// сьогодні, поки E2 не приїде.
        /// </summary>
        private static void BuildBattleArenaHook(GameObject arenaRoot, Camera arenaCamera)
        {
            var method = System.Type.GetType("Game.Gameplay.EditorTools.BattleArenaBuilder, Game.Gameplay.Editor")
                ?.GetMethod("Build");
            method?.Invoke(null, new object[] { arenaRoot, arenaCamera });
        }

        // ================= World/Hub =================

        /// <summary>
        /// Та сама «Село на перевалі» (VillageShowcase), скликана під
        /// <paramref name="hub"/>: земля, хати, мельниця, частокол, ліс,
        /// пости, жителі, ділянки під будівництво — усе через
        /// <see cref="KitBuilder"/>, жодної нової раскладкової математики.
        /// </summary>
        private static void BuildHub(GameObject hub, Light sun, Camera hubCamera)
        {
            var ground = KitBuilder.Ground(new Color(0.33f, 0.40f, 0.22f), new Vector3(6f, 1f, 6f),
                "Assets/Scenes/GameGround.mat");
            ground.transform.SetParent(hub.transform, true);

            House(hub, new Vector3(-6f, 0f, 2f), 3, 3, wood: true, facing: 0f);
            House(hub, new Vector3(-1.5f, 0f, 2.5f), 3, 2, wood: true, facing: 0f);
            House(hub, new Vector3(3.5f, 0f, 2f), 2, 3, wood: false, facing: 0f);
            House(hub, new Vector3(-4f, 0f, -4f), 2, 2, wood: true, facing: 180f);
            House(hub, new Vector3(2f, 0f, -4.5f), 3, 2, wood: false, facing: 180f);

            var mill = KitBuilder.Place(Town + "watermill.fbx", new Vector3(9f, 0f, -1f), 210f, "Мельница");
            if (mill != null) mill.transform.SetParent(hub.transform, true);

            var palisade = KitBuilder.Palisade(Town, -11f, 12f, -9f, 8f, 0.5f);
            if (palisade != null) palisade.transform.SetParent(hub.transform, true);

            var forest = KitBuilder.Forest(Nature);
            if (forest != null) forest.transform.SetParent(hub.transform, true);

            var posts = Posts(hub);
            var villagers = Villagers(hub, posts);
            var plots = Plots(hub);

            var stage = hub.AddComponent<Game.Gameplay.VillageStage>();
            stage.sun = sun;
            stage.view = hubCamera;
            stage.postsRoot = posts.transform;
            stage.villagersRoot = villagers.transform;
            stage.plotsRoot = plots.transform;
        }

        private static GameObject House(GameObject hub, Vector3 origin, int width, int depth, bool wood, float facing)
        {
            var house = KitBuilder.House(Town, origin, width, depth, wood, facing);
            if (house != null) house.transform.SetParent(hub.transform, true);
            return house;
        }

        /// <summary>Пости — ті самі сім якорів, що в касті TEST_BUILD.md §3.0 (усі сім, на відміну від вітрини).</summary>
        private static GameObject Posts(GameObject hub)
        {
            var group = new GameObject("Посты");
            group.transform.SetParent(hub.transform, false);

            KitBuilder.Anchor(group, "council_seat", new Vector3(-1.0f, 0f, 1.2f));
            KitBuilder.Anchor(group, "storehouse_dock", new Vector3(-5.4f, 0f, 0.6f));
            KitBuilder.Anchor(group, "settlement_market", new Vector3(2.2f, 0f, 0.2f));
            KitBuilder.Anchor(group, "infirmary_bed", new Vector3(4.2f, 0f, 0.8f));
            KitBuilder.Anchor(group, "settlement_farms", new Vector3(-3.4f, 0f, -5.0f));
            KitBuilder.Anchor(group, "scouting_post", new Vector3(0.5f, 0f, -8.0f));
            KitBuilder.Anchor(group, "workshop_bench", new Vector3(8.2f, 0f, -1.6f));

            return group;
        }

        /// <summary>
        /// Постать на кожному з семи постів (вітрина мала фігуру лише на
        /// шести — тут потрібні всі сім, бо workshop_bench — робочий пост
        /// касту, а не декоративний). Той самий набір моделей Kenney Mini
        /// Characters (a..f), сьомий пост бере ще одну вже наявну модель.
        /// </summary>
        private static GameObject Villagers(GameObject hub, GameObject posts)
        {
            var group = new GameObject("Жители");
            group.transform.SetParent(hub.transform, false);

            string[] postIds =
            {
                "council_seat", "storehouse_dock", "settlement_market",
                "infirmary_bed", "settlement_farms", "scouting_post", "workshop_bench"
            };
            string[] who =
            {
                Chars + "character-male-a.fbx", Chars + "character-female-b.fbx",
                Chars + "character-male-c.fbx", Chars + "character-female-d.fbx",
                Chars + "character-male-e.fbx", Chars + "character-female-f.fbx",
                Chars + "character-male-b.fbx"
            };
            float[] facing = { 250f, 90f, 200f, 300f, 20f, 160f, 140f };

            for (int i = 0; i < postIds.Length; i++)
            {
                var post = posts.transform.Find("post:" + postIds[i]);
                if (post == null) continue;

                var holder = new GameObject("villager:" + postIds[i]);
                holder.transform.SetParent(group.transform, false);
                holder.transform.position = post.position + new Vector3(0.6f, 0f, 0.4f);

                KitBuilder.Attach(holder, who[i], Vector3.zero, facing[i]);
            }

            return group;
        }

        /// <summary>Ділянки під будівництво, ще не готові — той самий каталог зданий ядра, що у вітрині.</summary>
        private static GameObject Plots(GameObject hub)
        {
            var group = new GameObject("Стройка");
            group.transform.SetParent(hub.transform, false);

            Plot(group, Game.Core.Base.DefaultBuildings.Infirmary, new Vector3(6.0f, 0f, 4.2f), 2, 2, true);
            Plot(group, Game.Core.Base.DefaultBuildings.Workshop, new Vector3(7.0f, 0f, -6.0f), 2, 2, true);
            Plot(group, Game.Core.Base.DefaultBuildings.Market, new Vector3(-1.0f, 0f, -2.2f), 3, 2, true);
            Plot(group, Game.Core.Base.DefaultBuildings.Tavern, new Vector3(-8.6f, 0f, -3.4f), 3, 3, true);
            Plot(group, Game.Core.Base.DefaultBuildings.Temple, new Vector3(-3.4f, 0f, 5.2f), 3, 3, false);
            Plot(group, Game.Core.Base.DefaultBuildings.Fortifications, new Vector3(-9.6f, 0f, 5.6f), 2, 2, false);
            Plot(group, Game.Core.Base.DefaultBuildings.Armory, new Vector3(9.6f, 0f, 4.6f), 2, 2, false);

            return group;
        }

        private static void Plot(GameObject root, string buildingId, Vector3 at, int width, int depth, bool wood)
        {
            var def = Game.Core.Base.DefaultBuildings.Get(buildingId);
            KitBuilder.Plot(Town, root, buildingId, def != null ? def.DisplayName : buildingId, at, width, depth, wood);
        }
    }
}
