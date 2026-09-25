using System.Collections.Generic;
using System.IO;
using Game.Core.Characters.Creation;
using Game.Gameplay.Text;
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
            // Фаза F (FOLIAGE): крок ПЕРЕД усім іншим — дешева перевірка
            // позначки палітри Kenney (Library/KenneyPaletteVersion.txt);
            // реальний переімпорт лише якщо вона розійшлась із поточною.
            // Без цього тепла Library (імпортована до появи/зміни палітри)
            // тримала б бірюзові крони/траву в кожній наступній збірці мовчки.
            KenneyImportSettings.ReimportIfPaletteChanged();
            EnsureAnimatedCharacters();

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
            var shell = boot.AddComponent<Game.Gameplay.GameShell>();
            var autoplay = boot.AddComponent<Game.Gameplay.AutoplayBootstrap>();
            // Фаза F: пряме посилання, виставлене тут (Editor-only виклик
            // GetComponent — тому саме тут, а не в самому AutoplayBootstrap.cs,
            // який лінтується заглушкою без GetComponent<T>) і збережене в
            // сцені — рантайму не потрібен FindAnyObjectByType/рефлексія, щоб
            // дим-тест знайшов GameShell на своєму ж об'єкті.
            autoplay.Shell = shell;

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
        /// Арена бою — тактична камера з нахилом (Бій v2, docs/COMBAT_V2.md
        /// §4: не строго згори — підпис/оверлеї над юнітом інакше лягають
        /// прямо на модель, аудит 25.09.2026): вимкнена, поки бою нема кого
        /// показувати. <see cref="Game.Gameplay.BattleArenaController.InitializeCamera"/>
        /// одразу перекладає позицію/поворот під реальний грид на вхід у бій —
        /// значення тут лише «розумний дефолт» (нахил/поворот ті самі
        /// константи, що керують камерою в бою, щоб не розходитись двома
        /// джерелами істини).
        /// </summary>
        private static Camera BuildArenaCamera()
        {
            var go = new GameObject("ArenaCamera");
            var cam = go.AddComponent<Camera>();

            cam.orthographic = true;
            cam.orthographicSize = 8f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.10f, 0.12f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;

            if (go.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>() == null)
                go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();

            go.transform.rotation = Quaternion.Euler(Game.Gameplay.BattleArenaController.CameraTiltDegrees, Game.Gameplay.BattleArenaController.InitialYawDegrees, 0f);
            go.transform.position = new Vector3(0f, 20f, -15f);

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

            // Перешкоди для прогулянки героя (HeroWalker): стіни хат, млин, ліс,
            // ділянки (ті лише коли добудовані — неактивна модель не заважає).
            var obstacles = new List<GameObject>();
            obstacles.Add(House(hub, new Vector3(-6f, 0f, 2f), 3, 3, wood: true, facing: 0f));
            obstacles.Add(House(hub, new Vector3(-1.5f, 0f, 2.5f), 3, 2, wood: true, facing: 0f));
            obstacles.Add(House(hub, new Vector3(3.5f, 0f, 2f), 2, 3, wood: false, facing: 0f));
            obstacles.Add(House(hub, new Vector3(-4f, 0f, -4f), 2, 2, wood: true, facing: 180f));
            obstacles.Add(House(hub, new Vector3(2f, 0f, -4.5f), 3, 2, wood: false, facing: 180f));

            var mill = KitBuilder.Place(Town + "watermill.fbx", new Vector3(9f, 0f, -1f), 210f, "Мельница");
            if (mill != null) mill.transform.SetParent(hub.transform, true);
            obstacles.Add(mill);

            var palisade = KitBuilder.Palisade(Town, -11f, 12f, -9f, 8f, 0.5f);
            if (palisade != null) palisade.transform.SetParent(hub.transform, true);

            var forest = KitBuilder.Forest(Nature);
            if (forest != null) forest.transform.SetParent(hub.transform, true);

            var posts = Posts(hub);
            var villagers = Villagers(hub, posts);
            var plots = Plots(hub);
            obstacles.Add(plots);
            var landmarks = Landmarks(hub);
            obstacles.Add(landmarks);
            Hero(hub, hubCamera, posts, plots, landmarks, obstacles, forest);

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

                var figure = KitBuilder.Attach(holder, who[i], Vector3.zero, facing[i]);
                // Жителі дихають (кліп idle), кожен зі своїм зсувом фази —
                // детермінованим, як і все в сцені.
                AddFigureAnimation(figure, who[i], i * 0.37f, walks: false);
            }

            return group;
        }

        // ================= прогулянка героя =================

        /// <summary>
        /// Орієнтири, до яких можна підійти на прогулянці: дошка оголошень
        /// (вкладка «Квести») біля площі і тренувальний майданчик (вкладка
        /// «Готовність», де тренувальний бій) у південно-західному куті, подалі
        /// від ділянок.
        /// </summary>
        private static GameObject Landmarks(GameObject hub)
        {
            var group = new GameObject("Орієнтири");
            group.transform.SetParent(hub.transform, false);

            var board = new GameObject("place:" + Game.Gameplay.Walk.VillagePlaces.NoticeBoardId);
            board.transform.SetParent(group.transform, false);
            board.transform.localPosition = new Vector3(-3.0f, 0f, 0.2f);
            KitBuilder.Attach(board, Town + "banner-green.fbx", Vector3.zero, 45f);

            var training = new GameObject("place:" + Game.Gameplay.Walk.VillagePlaces.TrainingGroundId);
            training.transform.SetParent(group.transform, false);
            training.transform.localPosition = new Vector3(-7.2f, 0f, -6.8f);
            KitBuilder.Attach(training, Town + "poles.fbx", new Vector3(-0.8f, 0f, 0f), 0f);
            KitBuilder.Attach(training, Town + "banner-red.fbx", new Vector3(0.8f, 0f, 0.4f), 45f);
            KitBuilder.Attach(training, Town + "rock-small.fbx", new Vector3(0f, 0f, -0.9f), 0f);

            return group;
        }

        /// <summary>
        /// Герой прогулянки (власник, 25.09.2026: «бігати як у CRPG»): дві
        /// моделі — за статтю героя, яку обирає гравець, — і HeroWalker.
        /// Моделі не збігаються з жодним жителем на посту.
        /// </summary>
        private static void Hero(GameObject hub, Camera hubCamera, GameObject posts, GameObject plots,
            GameObject landmarks, List<GameObject> obstacles, GameObject forest)
        {
            var hero = new GameObject("Герой");
            hero.transform.SetParent(hub.transform, false);
            hero.transform.position = new Vector3(0.6f, 0f, 0.4f);

            string malePath = Chars + "character-male-d.fbx";
            string femalePath = Chars + "character-female-c.fbx";
            var male = KitBuilder.Attach(hero, malePath, Vector3.zero, 0f);
            var female = KitBuilder.Attach(hero, femalePath, Vector3.zero, 0f);
            AddFigureAnimation(male, malePath, 0f, walks: true);
            AddFigureAnimation(female, femalePath, 0f, walks: true);
            if (female != null) female.SetActive(false);

            var walker = hero.AddComponent<Game.Gameplay.HeroWalker>();
            walker.hubCamera = hubCamera;
            walker.maleModel = male;
            walker.femaleModel = female;
            walker.postsRoot = posts.transform;
            walker.plotsRoot = plots.transform;
            walker.landmarksRoot = landmarks.transform;

            var roots = new List<Transform>();
            foreach (var o in obstacles)
                if (o != null) roots.Add(o.transform);
            walker.obstacleRoots = roots.ToArray();
            if (forest != null) walker.trunkRoots = new[] { forest.transform };
        }

        /// <summary>
        /// Фігурки Kenney імпортувалися ще до того, як у маніфесті з'явився
        /// модуль анімації, — на префабах немає Animator, і жителі стояли в
        /// T-позі (перший прогін прогулянки, 25.09.2026). Налаштування імпорту
        /// ті самі, тож Unity сам їх не переімпортує: робимо це тут, лише для
        /// моделей без аніматора (дешево і один раз).
        /// </summary>
        private static void EnsureAnimatedCharacters()
        {
            const string folder = "Assets/ThirdParty/Kenney/MiniCharacters/Models";
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!Path.GetFileName(path).StartsWith("character-")) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || prefab.GetComponentInChildren<Animator>() != null) continue;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                Debug.Log("[Гра] переімпорт фігурки заради аніматора: " + path);
            }
        }

        private static void AddFigureAnimation(GameObject figure, string fbxPath, float phase, bool walks)
        {
            if (figure == null) return;
            if (figure.GetComponentInChildren<Animator>() == null)
            {
                // Запасний шлях, якщо переімпорт аніматора не додав.
                var animator = figure.AddComponent<Animator>();
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
                {
                    var avatar = asset as Avatar;
                    if (avatar != null) { animator.avatar = avatar; break; }
                }
                Debug.LogWarning("[Гра] Animator доданий вручну: " + fbxPath);
            }
            var anim = figure.AddComponent<Game.Gameplay.FigureAnimation>();
            anim.idle = Clip(fbxPath, "idle");
            if (walks)
            {
                anim.walk = Clip(fbxPath, "walk");
                anim.sprint = Clip(fbxPath, "sprint");
            }
            anim.phase = phase;
        }

        /// <summary>Кліп із FBX набору за ім'ям дубля (idle/walk/sprint); службові «__preview__» пропускаються.</summary>
        private static AnimationClip Clip(string fbxPath, string clipName)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                var clip = asset as AnimationClip;
                if (clip != null && clip.name == clipName && !clip.name.StartsWith("__preview__")) return clip;
            }
            Debug.LogWarning("[Гра] кліп не знайдено: " + fbxPath + " / " + clipName);
            return null;
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
            // R7/CLAUDE.md: гравець ніколи не бачить Core-контентний DisplayName —
            // 3D-мітка йде через ту саму таблицю "building.<id>", що й UI-панелі
            // (HubScreen/SummaryScreen), а не через def.DisplayName (Core, RU).
            string label = UkrainianText.Get("building." + buildingId, Gender.Male);
            KitBuilder.Plot(Town, root, buildingId, label, at, width, depth, wood);
        }
    }
}
