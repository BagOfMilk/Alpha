using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Game.Core.Loop;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Збирає сцену «Село на перевалі» з безкоштовних наборів і знімає її в PNG.
    ///
    /// Навіщо це є. Перша ігрова година («Перевал», docs/FIRST_HOUR.md) досі
    /// існувала тільки текстом. Ця сцена — перший погляд на неї очима:
    /// хати, частокол, ліс, жителі на постах. Вона збирається КОДОМ і цілком
    /// детерміновано, тому її можна перезібрати на будь-якій машині й побачити
    /// те саме — на відміну від сцени, яку хтось одного разу налаштував мишею.
    ///
    /// Жодного Random: розкид дерев і каменів береться з детермінованого
    /// хеша за індексом. Це не примха — за інваріантом 1 випадковості в проєкті
    /// немає ніде, і «сцену трясе при кожній перезбірці» тут теж не потрібно.
    ///
    /// Розміщення йде ЗА ВИМІРЯНИМИ ГАБАРИТАМИ моделей, а не за припущеннями про
    /// те, де в них точка прив'язки: скрипт питає в меша його bounds і
    /// ставить сусідній модуль впритул. Інакше стіни будинків розходились би або
    /// налазили одна на одну за першої ж зміни набору.
    /// </summary>
    public static class VillageShowcase
    {
        private const string Town = "Assets/ThirdParty/Kenney/FantasyTownKit/Models/";
        private const string Nature = "Assets/ThirdParty/Kenney/NatureKit/Models/";
        private const string Chars = "Assets/ThirdParty/Kenney/MiniCharacters/Models/";

        private const string ScenePath = "Assets/Scenes/Village.unity";
        private const string ShotPath = "Screenshots/village.png";

        [MenuItem("Alpha/Собрать сцену «Село на перевале»")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildGround();
            BuildLight();
            BuildCamera();

            // Село: чотири хати вздовж дороги, дві дивляться на неї фасадом.
            House(new Vector3(-6f, 0f, 2f), 3, 3, wood: true, facing: 0f);
            House(new Vector3(-1.5f, 0f, 2.5f), 3, 2, wood: true, facing: 0f);
            House(new Vector3(3.5f, 0f, 2f), 2, 3, wood: false, facing: 0f);
            House(new Vector3(-4f, 0f, -4f), 2, 2, wood: true, facing: 180f);
            House(new Vector3(2f, 0f, -4.5f), 3, 2, wood: false, facing: 180f);

            // Млин біля води — орієнтир, за яким село впізнається з першого кадру.
            KitBuilder.Place(Town + "watermill.fbx", new Vector3(9f, 0f, -1f), 210f, "Мельница");

            // Частокол: громада на краю обжитого світу обгороджується.
            Palisade();

            // Ліс навколо і камені на схилі.
            Forest();

            // Пости: якорі, до яких прив'язані жителі й мітки подій.
            var posts = Posts();

            // Жителі на постах — масштаб людини поруч із будинками.
            var villagers = Villagers(posts);

            // Ділянки під будівлі, яких у хутора ще немає.
            var plots = Plots();

            // Доба йде прямо в сцені: світло, жителі й стрічка подій.
            Life(posts, villagers, plots);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("Сцена собрана: " + ScenePath);
        }

        [MenuItem("Alpha/Снять сцену в PNG")]
        public static void Shoot()
        {
            Shoot(ScenePath, ShotPath);
        }

        private static void Shoot(string scenePath, string shotPath)
        {
            if (!File.Exists(scenePath))
            {
                Debug.LogError("Сцены нет — сначала собери: " + scenePath);
                return;
            }

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Capture(shotPath);
        }

        /// <summary>Зняти те, що відкрито зараз, — без перезавантаження сцени.</summary>
        private static void Capture(string shotPath)
        {
            var camera = Object.FindFirstObjectByType<Camera>();
            if (camera == null) { Debug.LogError("В сцене нет камеры"); return; }

            // Пакетувальник SRP у батч-режимі не оновлює буфери матеріалів: у
            // контрольному кадрі червоний, зелений і синій куби вийшли ОДНИМ
            // кольором, і село виходило суцільно бірюзовим. На час знімка
            // вимикаємо — в редакторі і в грі він лишається увімкненим.
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            bool batcher = urp != null && urp.useSRPBatcher;
            if (urp != null) urp.useSRPBatcher = false;

            const int width = 1920, height = 1080;
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB) { antiAliasing = 8 };
            camera.targetTexture = rt;

            // В URP кадр знімається ЗАПИТОМ рендера, а не Camera.Render(): другий
            // шлях лишився від Built-in, конвеєр його не обслуговує, і в
            // батч-режимі в текстуру потрапляє неініціалізована пам'ять —
            // райдужні смуги замість картинки. Перевірено на першому знімку.
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(camera, request))
            {
                RenderPipeline.SubmitRenderRequest(camera, request);
            }
            else
            {
                Debug.LogWarning("Запрос рендера не поддержан — снимаю старым путём");
                camera.Render();
            }

            RenderTexture.active = rt;
            var shot = new Texture2D(width, height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            shot.Apply();

            camera.targetTexture = null;
            RenderTexture.active = null;
            if (urp != null) urp.useSRPBatcher = batcher;

            Directory.CreateDirectory(Path.GetDirectoryName(shotPath));
            File.WriteAllBytes(shotPath, shot.EncodeToPNG());
            Debug.Log("Снимок: " + Path.GetFullPath(shotPath));

            // Кожен кадр — текстура 1920×1080 з восьмикратним згладжуванням.
            // Без звільнення плівка на шістдесят фаз з'їдала всю пам'ять і
            // обвалювала редактор на тридцятому кадрі («System out of memory»).
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(shot);
        }

        /// <summary>
        /// Контрольний кадр: три куби заздалегідь відомих кольорів на чорному тлі.
        /// Потрібен, щоб відділити «зламаний колір матеріалу» від «зламана сцена».
        /// </summary>
        public static void Diagnose()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            Debug.Log("ДИАГНОЗ: шейдер URP/Lit " + (lit != null ? "найден" : "НЕ НАЙДЕН"));

            var colors = new[] { Color.red, Color.green, Color.blue };
            for (int i = 0; i < colors.Length; i++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.position = new Vector3((i - 1) * 2.5f, 0f, 0f);
                if (lit != null)
                {
                    var m = new Material(lit);
                    m.SetColor("_BaseColor", colors[i]);
                    cube.GetComponent<Renderer>().sharedMaterial = m;
                }
            }

            var lightGo = new GameObject("Свет");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, 30f, 0f);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.2f, 0.2f, 0.2f);

            var camGo = new GameObject("Камера");
            var cam = camGo.AddComponent<Camera>();
            if (camGo.GetComponent<UniversalAdditionalCameraData>() == null)
                camGo.AddComponent<UniversalAdditionalCameraData>();
            cam.orthographic = true;
            cam.orthographicSize = 3f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            camGo.transform.position = new Vector3(0f, 0f, -10f);
            camGo.tag = "MainCamera";

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Diagnose.unity");
            Shoot("Assets/Scenes/Diagnose.unity", "Screenshots/diagnose.png");
        }

        public static void BuildAndShoot()
        {
            Build();
            Shoot();
        }

        /// <summary>
        /// Плівка доби: конвеєр крутиться прямо в редакторі, кожна фаза
        /// знімається кадром. Режим гри для цього не потрібен — добу рухає той
        /// самий метод, що й таймер у грі, тому плівка показує справжнє
        /// життя села, а не окрему «демонстраційну» гілку коду.
        /// </summary>
        [MenuItem("Alpha/Снять плёнку суток")]
        public static void FilmDays()
        {
            Build();

            var life = Object.FindFirstObjectByType<Game.Gameplay.VillageLife>();
            if (life == null) { Debug.LogError("В сцене нет компонента жизни"); return; }

            life.Initialize();

            // Сорок фаз — це двадцять діб. Коротше не можна: харнес показав, що
            // перша подія приходить приблизно на дев'яту добу, і плівка
            // на п'ять днів показала б «нічого не відбувається» як вирок.
            const int phases = 60;
            for (int i = 0; i < phases; i++)
            {
                var report = life.AdvancePhase();
                string mark = report.Phase == DayPhase.Night ? "ночь" : "день";
                Capture(string.Format("Screenshots/life-{0:00}-{1}.png", i + 1, mark));
                var lines = Game.Gameplay.VillageView.Lines(report);
                string what = lines.Count > 0 ? string.Join(" | ", lines.ToArray()) : "тихо";
                Debug.Log("Кадр " + (i + 1) + ": сутки " + report.Day + ", " + mark + " — " + what);
            }

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            Debug.Log("Плёнка снята: " + phases + " фаз");
        }

        // ================= постройки =================

        /// <summary>
        /// Хата з модулів: стіни по периметру, двері по фасаду, вікна по боках,
        /// двосхилий дах зверху. Розміри рахуються з габаритів самої стіни.
        /// Сама розкладка тепер у <see cref="KitBuilder.House"/> (B8) — спільний
        /// примітив, яким зможе скористатися майбутній збирач ігрової
        /// сцени; тут лише шлях до набору будівель цієї вітрини.
        /// </summary>
        private static GameObject House(Vector3 origin, int width, int depth, bool wood, float facing)
        {
            return KitBuilder.House(Town, origin, width, depth, wood, facing);
        }

        /// <summary>
        /// Ділянки під будівлі US-7.1, яких у хутора ще немає. Будівля готова
        /// заздалегідь, але схована: компонент життя піднімає її п'ятьма стадіями
        /// по мірі будівництва (US-7.3) і підписує, коли вона добудована.
        /// Ім'я «plot:&lt;id&gt;» — зв'язок із каталогом будівель ядра.
        /// </summary>
        private static GameObject Plots()
        {
            var root = new GameObject("Стройка");

            Plot(root, Game.Core.Base.DefaultBuildings.Infirmary, new Vector3(6.0f, 0f, 4.2f), 2, 2, true);
            Plot(root, Game.Core.Base.DefaultBuildings.Workshop, new Vector3(7.0f, 0f, -6.0f), 2, 2, true);
            Plot(root, Game.Core.Base.DefaultBuildings.Market, new Vector3(-1.0f, 0f, -2.2f), 3, 2, true);
            Plot(root, Game.Core.Base.DefaultBuildings.Tavern, new Vector3(-8.6f, 0f, -3.4f), 3, 3, true);
            Plot(root, Game.Core.Base.DefaultBuildings.Temple, new Vector3(-3.4f, 0f, 5.2f), 3, 3, false);
            Plot(root, Game.Core.Base.DefaultBuildings.Fortifications, new Vector3(-9.6f, 0f, 5.6f), 2, 2, false);
            Plot(root, Game.Core.Base.DefaultBuildings.Armory, new Vector3(9.6f, 0f, 4.6f), 2, 2, false);

            return root;
        }

        private static void Plot(GameObject root, string buildingId, Vector3 at, int width, int depth, bool wood)
        {
            // Розкладка ділянки — в KitBuilder (B8), тут лише свій зв'язок
            // із каталогом будівель ядра (підпис за DisplayName).
            var def = Game.Core.Base.DefaultBuildings.Get(buildingId);
            KitBuilder.Plot(Town, root, buildingId, def != null ? def.DisplayName : buildingId,
                at, width, depth, wood);
        }

        /// <summary>
        /// Частокол навколо села: кілки по периметру з воротами з боку дороги.
        /// Розкладка — в <see cref="KitBuilder.Palisade"/> (B8); тут лише
        /// геометрія цієї конкретної вітрини.
        /// </summary>
        private static void Palisade()
        {
            const float left = -11f, right = 12f, near = -9f, far = 8f;
            // Ворота посередині ближньої сторони (x≈0.5): село не глуха коробка.
            KitBuilder.Palisade(Town, left, right, near, far, gateX: 0.5f);
        }

        /// <summary>
        /// Ліс і камені. Розкладка (розкид, детермінований хеш) — в
        /// <see cref="KitBuilder.Forest"/> (B8); тут лише шлях до набору.
        /// </summary>
        private static void Forest()
        {
            KitBuilder.Forest(Nature);
        }

        /// <summary>
        /// Пости поселення як якорі сцени. Ім'я «post:&lt;id&gt;» — це зв'язок з
        /// ядром: той самий ідентифікатор носить слот призначення, за ним компонент
        /// життя знаходить, де стоїть людина і де показати подію.
        /// </summary>
        private static GameObject Posts()
        {
            var group = new GameObject("Посты");

            Anchor(group, "council_seat", new Vector3(-1.0f, 0f, 1.2f));
            Anchor(group, "storehouse_dock", new Vector3(-5.4f, 0f, 0.6f));
            Anchor(group, "settlement_market", new Vector3(2.2f, 0f, 0.2f));
            Anchor(group, "infirmary_bed", new Vector3(4.2f, 0f, 0.8f));
            Anchor(group, "settlement_farms", new Vector3(-3.4f, 0f, -5.0f));
            Anchor(group, "scouting_post", new Vector3(0.5f, 0f, -8.0f));
            Anchor(group, "workshop_bench", new Vector3(8.2f, 0f, -1.6f));

            return group;
        }

        /// <summary>Тонка обгортка над <see cref="KitBuilder.Anchor"/> (B8) — зберігає стару назву параметра.</summary>
        private static void Anchor(GameObject parent, string postId, Vector3 position)
        {
            KitBuilder.Anchor(parent, postId, position);
        }

        /// <summary>
        /// Жителі стоять на своїх постах. Ім'я «villager:&lt;postId&gt;» — той самий
        /// зв'язок: вночі компонент ховає саме тих, хто не патрулює.
        /// </summary>
        private static GameObject Villagers(GameObject posts)
        {
            var group = new GameObject("Жители");

            string[] postIds =
            {
                "council_seat", "storehouse_dock", "settlement_market",
                "infirmary_bed", "settlement_farms", "scouting_post"
            };
            string[] who =
            {
                Chars + "character-male-a.fbx", Chars + "character-female-b.fbx",
                Chars + "character-male-c.fbx", Chars + "character-female-d.fbx",
                Chars + "character-male-e.fbx", Chars + "character-female-f.fbx"
            };
            float[] facing = { 250f, 90f, 200f, 300f, 20f, 160f };

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

        /// <summary>
        /// Компонент, який крутить добу, плюс екранний текст: рядок
        /// стану зверху і стрічка подій під ним.
        /// </summary>
        private static void Life(GameObject posts, GameObject villagers, GameObject plots)
        {
            var cameraGo = Object.FindFirstObjectByType<Camera>();
            var sunLight = Object.FindFirstObjectByType<Light>();

            var life = new GameObject("Жизнь села").AddComponent<Game.Gameplay.VillageLife>();
            life.sun = sunLight;
            life.view = cameraGo;
            life.postsRoot = posts.transform;
            life.villagersRoot = villagers.transform;
            life.plotsRoot = plots.transform;

            if (cameraGo != null)
            {
                life.headline = Caption(cameraGo.transform, "Строка состояния",
                    new Vector3(-18.6f, 10.2f, 12f), 0.16f);
                life.log = Caption(cameraGo.transform, "Лента событий",
                    new Vector3(-18.6f, 8.6f, 12f), 0.11f);
            }
        }

        /// <summary>
        /// Текст у світі перед камерою. Вбудований шрифт береться навмисно:
        /// свій (Fixel, OFL) з'явиться разом з інтерфейсом, а зріз не повинен
        /// чекати верстки.
        /// </summary>
        private static TextMesh Caption(Transform parent, string name, Vector3 local, float size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.identity;

            var text = go.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 64;
            text.characterSize = size;
            text.anchor = TextAnchor.UpperLeft;
            text.color = Color.white;
            text.text = string.Empty;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && text.font != null)
                renderer.sharedMaterial = text.font.material;

            return text;
        }

        // ================= сцена =================

        /// <summary>Розкладка — в <see cref="KitBuilder.Ground"/> (B8); тут лише колір і шлях вітрини.</summary>
        private static void BuildGround()
        {
            KitBuilder.Ground(new Color(0.33f, 0.40f, 0.22f), new Vector3(6f, 1f, 6f), "Assets/Scenes/Ground.mat");
        }

        private static void BuildLight()
        {
            var go = new GameObject("Солнце");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.86f);
            light.intensity = 1.25f;
            light.shadows = LightShadows.Soft;
            // Низьке ранкове сонце: тіні довгі, рельєф читається.
            go.transform.rotation = Quaternion.Euler(38f, 145f, 0f);
        }

        private static void BuildCamera()
        {
            var go = new GameObject("Камера");
            var cam = go.AddComponent<Camera>();

            // Ходибельна ізометрія (US-7.7): ортографічна проєкція,
            // розворот на 45 градусів, нахил 30 — канонічний ізометричний вигляд.
            cam.orthographic = true;
            cam.orthographicSize = 11f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.68f, 0.78f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;

            // Дані камери URP додаємо явно: у камери, створеної кодом, їх
            // може не виявитися, і конвеєр тоді малює її не своїм шляхом.
            if (go.GetComponent<UniversalAdditionalCameraData>() == null)
                go.AddComponent<UniversalAdditionalCameraData>();

            go.transform.rotation = Quaternion.Euler(30f, 45f, 0f);
            go.transform.position = Quaternion.Euler(30f, 45f, 0f) * new Vector3(0f, 0f, -45f)
                                    + new Vector3(0f, 0f, -1f);
            go.tag = "MainCamera";

            // Рівне денне підсвічування: без нього тіньова сторона будинків і крони
            // провалюються в чорне, і село читається силуетами.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.72f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.48f, 0.52f, 0.48f);
            RenderSettings.ambientGroundColor = new Color(0.28f, 0.26f, 0.22f);
            RenderSettings.skybox = null;
        }

        // Attach/Place/Load/MeasureSize/Hash/Cache переїхали в KitBuilder (B8) —
        // це спільні примітиви без даних цієї конкретної вітрини, ними
        // напряму користуються House/Palisade/Forest/Plot/Villagers вище.
    }
}
