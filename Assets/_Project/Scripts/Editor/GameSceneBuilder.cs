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

            // Спайк H4: асет PanelSettings шапки на UI Toolkit (шейдери і тема в
            // білді через Resources). Саму панель GameShell піднімає в рантаймі.
            HudPanelAssets.Ensure();

            // Поправка №19: модульний набір персонажів і живе прев'ю екрана створення героя.
            CharacterKitBuilder.Build();

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
            // Житло мешканців — не інтерактивне (UX_DESIGN §4.1). Дві колишні
            // безіменні хати на площі стали ділянками Складу й Зали ради: старт —
            // без будівель (№12.7), тож на їхньому місці спершу порожньо.
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

            var plots = Plots(hub);
            obstacles.Add(plots);
            var posts = Posts(hub, plots);
            var villagers = Villagers(hub, posts);
            var landmarks = Landmarks(hub);
            obstacles.Add(landmarks);
            var interior = Interior(hub);
            Hero(hub, hubCamera, posts, plots, landmarks, villagers, interior, obstacles, forest);

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

        /// <summary>
        /// Пости — ті самі сім якорів, що в касті TEST_BUILD.md §3.0. Пост
        /// будівлі стоїть біля її дверей (UX_DESIGN §4.8: пост = станція
        /// працівника у своїй будівлі) — раніше лазарет стояв на одному кінці
        /// села, а його пост — на іншому. Просто неба — Віче (крісло радника
        /// біля вогнища), Поле, Застава біля воріт.
        /// </summary>
        private static GameObject Posts(GameObject hub, GameObject plots)
        {
            var group = new GameObject("Посты");
            group.transform.SetParent(hub.transform, false);

            KitBuilder.Anchor(group, "council_seat", new Vector3(0.2f, 0f, 0.5f));
            KitBuilder.Anchor(group, "settlement_farms", new Vector3(-3.4f, 0f, -5.0f));
            KitBuilder.Anchor(group, "scouting_post", new Vector3(0.5f, 0f, -8.0f));
            BesideDoor(group, plots, "storehouse_dock", Game.Core.Base.DefaultBuildings.Storehouse);
            BesideDoor(group, plots, "settlement_market", Game.Core.Base.DefaultBuildings.Market);
            BesideDoor(group, plots, "infirmary_bed", Game.Core.Base.DefaultBuildings.Infirmary);
            BesideDoor(group, plots, "workshop_bench", Game.Core.Base.DefaultBuildings.Workshop);

            return group;
        }

        /// <summary>Якір поста — поруч із дверима будівлі, трохи вбік: працівник стоїть біля входу, не в дверях.</summary>
        private static void BesideDoor(GameObject group, GameObject plots, string postId, string buildingId)
        {
            var door = plots.transform.Find("plot:" + buildingId + "/door");
            var at = door != null ? door.position + new Vector3(0.9f, 0f, -0.2f) : Vector3.zero;
            KitBuilder.Anchor(group, postId, at);
        }

        /// <summary>
        /// Постать на кожному з семи постів і чотири місця біля вогнища Віча —
        /// для тих, хто без поста (з ними теж говорять). У кожної — дві моделі,
        /// «m» і «f»: VillageStage показує ту, що відповідає людині, а не посту
        /// (раніше Дід Овсій стояв жіночою фігуркою).
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
            string[] males = { "a", "c", "e", "b", "e", "a", "b" };
            string[] females = { "b", "d", "f", "d", "b", "f", "a" };
            float[] facing = { 250f, 180f, 200f, 200f, 20f, 160f, 200f };

            for (int i = 0; i < postIds.Length; i++)
            {
                var post = posts.transform.Find("post:" + postIds[i]);
                if (post == null) continue;
                var holder = new GameObject("villager:" + postIds[i]);
                holder.transform.SetParent(group.transform, false);
                holder.transform.position = post.position + new Vector3(0.6f, 0f, 0.4f);
                Pair(holder, males[i], females[i], facing[i], i * 0.37f);
            }

            // Біля вогнища Віча (саме вогнище — в Landmarks): місця idle:0..3.
            Vector3[] idle =
            {
                new Vector3(2.8f, 0f, 0.2f), new Vector3(2.8f, 0f, 1.4f),
                new Vector3(1.9f, 0f, 1.9f), new Vector3(1.1f, 0f, -0.1f)
            };
            float[] idleFacing = { 270f, 230f, 180f, 45f };
            for (int i = 0; i < idle.Length; i++)
            {
                var holder = new GameObject("idle:" + i);
                holder.transform.SetParent(group.transform, false);
                holder.transform.position = idle[i];
                Pair(holder, i % 2 == 0 ? "c" : "e", i % 2 == 0 ? "d" : "f", idleFacing[i], 0.2f + i * 0.31f);
                holder.SetActive(false);
            }

            return group;
        }

        /// <summary>Дві моделі на одному місці — «m» і «f»; видно одну.</summary>
        private static void Pair(GameObject holder, string male, string female, float facing, float phase)
        {
            string malePath = Chars + "character-male-" + male + ".fbx";
            string femalePath = Chars + "character-female-" + female + ".fbx";
            var m = KitBuilder.Attach(holder, malePath, Vector3.zero, facing);
            var f = KitBuilder.Attach(holder, femalePath, Vector3.zero, facing);
            if (m != null) { m.name = "m"; AddFigureAnimation(m, malePath, phase, walks: false); }
            if (f != null) { f.name = "f"; AddFigureAnimation(f, femalePath, phase + 0.13f, walks: false); f.SetActive(false); }
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

            // Віче просто неба (Поправка №12.9): вогнище і колоди-лави біля крісла радника.
            var veche = new GameObject("veche");
            veche.transform.SetParent(group.transform, false);
            veche.transform.localPosition = new Vector3(1.9f, 0f, 0.7f);
            KitBuilder.Attach(veche, Nature + "campfire_stones.fbx", Vector3.zero, 0f);
            KitBuilder.Attach(veche, Nature + "log.fbx", new Vector3(0f, 0f, -1.35f), 90f);
            KitBuilder.Attach(veche, Nature + "log.fbx", new Vector3(-1.35f, 0f, 0.1f), 0f);

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
        /// Сіра кімната (docs/UX_DESIGN.md §4.10, варіант Б; §7.3): одна на всі
        /// будівлі, осторонь села (<see cref="Game.Gameplay.Walk.Interiors"/>).
        /// Підлога, три стіни й передня з прорізом дверей, п'ять п'єдесталів
        /// станцій «slot:0..4» — HeroWalker вмикає стільки, скільки станцій у
        /// будівлі. Під <c>World/Hub</c>: бій ховає її разом із селом.
        /// </summary>
        private static GameObject Interior(GameObject hub)
        {
            float ox = Game.Gameplay.Walk.Interiors.OriginX, oz = Game.Gameplay.Walk.Interiors.OriginZ;
            float hw = Game.Gameplay.Walk.Interiors.HalfWidth, hd = Game.Gameplay.Walk.Interiors.HalfDepth;

            var root = new GameObject("Інтер'єр");
            root.transform.SetParent(hub.transform, false);
            root.transform.position = new Vector3(ox, 0f, oz);

            var floor = SolidMaterial(new Color(0.42f, 0.40f, 0.37f), "Assets/Scenes/InteriorFloor.mat");
            var wall = SolidMaterial(new Color(0.30f, 0.28f, 0.26f), "Assets/Scenes/InteriorWall.mat");
            var station = SolidMaterial(new Color(0.62f, 0.55f, 0.44f), "Assets/Scenes/InteriorStation.mat");

            Box(root, "floor", new Vector3(0f, -0.05f, 0f), new Vector3(hw * 2f, 0.1f, hd * 2f), floor);
            const float h = 1.2f, t = 0.2f;
            Box(root, "wall-back", new Vector3(0f, h * 0.5f, hd), new Vector3(hw * 2f, h, t), wall);
            Box(root, "wall-left", new Vector3(-hw, h * 0.5f, 0f), new Vector3(t, h, hd * 2f), wall);
            Box(root, "wall-right", new Vector3(hw, h * 0.5f, 0f), new Vector3(t, h, hd * 2f), wall);
            // Передня стіна низька (камера дивиться крізь неї) і з прорізом дверей посередині.
            float gap = 1.2f, seg = hw - gap * 0.5f;
            Box(root, "wall-front-l", new Vector3(-(gap * 0.5f + seg * 0.5f), 0.15f, -hd), new Vector3(seg, 0.3f, t), wall);
            Box(root, "wall-front-r", new Vector3(gap * 0.5f + seg * 0.5f, 0.15f, -hd), new Vector3(seg, 0.3f, t), wall);

            float size = Game.Gameplay.Walk.Interiors.StationSize;
            for (int i = 0; i < Game.Gameplay.Walk.Interiors.SlotCount; i++)
            {
                var at = Game.Gameplay.Walk.Interiors.Slot(i);
                var slot = Box(root, "slot:" + i, new Vector3(at.X - ox, 0.45f, at.Z - oz), new Vector3(size, 0.9f, size), station);
                slot.SetActive(false);
            }
            root.SetActive(false);
            return root;
        }

        private static GameObject Box(GameObject parent, string name, Vector3 local, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = local;
            go.transform.localScale = scale;
            var collider = go.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);
            if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        /// <summary>Матеріал URP одним кольором — збережений асетом, бо матеріал лише в пам'яті сцена не зберігає.</summary>
        private static Material SolidMaterial(Color color, string path)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) return null;
            var mat = new Material(lit);
            mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0f);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        /// <summary>
        /// Герой (власник, 25.09.2026: «бігати як у CRPG»): дві моделі — за
        /// статтю героя, яку обирає гравець, — і HeroWalker. Моделі не
        /// збігаються з жодним жителем на посту.
        /// </summary>
        private static void Hero(GameObject hub, Camera hubCamera, GameObject posts, GameObject plots,
            GameObject landmarks, GameObject villagers, GameObject interior, List<GameObject> obstacles, GameObject forest)
        {
            var hero = new GameObject("Герой");
            hero.transform.SetParent(hub.transform, false);
            hero.transform.position = new Vector3(-0.8f, 0f, 0.0f);

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
            walker.villagersRoot = villagers.transform;
            walker.interiorRoot = interior.transform;

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

            // Ремесла прибульців (№12.9) — Сторожа біля воріт, Склад і Зала ради на площі.
            Plot(group, Game.Core.Base.DefaultBuildings.Watch, new Vector3(4.2f, 0f, -7.2f), 2, 2, false);
            Plot(group, Game.Core.Base.DefaultBuildings.Storehouse, new Vector3(-6.0f, 0f, 2.0f), 3, 3, true);
            Plot(group, Game.Core.Base.DefaultBuildings.CouncilHall, new Vector3(-1.5f, 0f, 2.5f), 3, 2, true);
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
