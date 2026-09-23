using System.Collections.Generic;
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
    /// Собирает сцену «Село на перевале» из бесплатных наборов и снимает её в PNG.
    ///
    /// Зачем это есть. Первая игровая час («Перевал», docs/FIRST_HOUR.md) до сих
    /// пор существовала только текстом. Эта сцена — первый взгляд на неё глазами:
    /// хаты, частокол, лес, жители на постах. Она собирается КОДОМ и целиком
    /// детерминированно, поэтому её можно пересобрать на любой машине и увидеть
    /// то же самое — в отличие от сцены, которую кто-то однажды настроил мышью.
    ///
    /// Никакого Random: разброс деревьев и камней берётся из детерминированного
    /// хеша по индексу. Это не каприз — по инварианту 1 случайности в проекте
    /// нет нигде, и «сцену трясёт при каждой пересборке» здесь тоже не нужно.
    ///
    /// Размещение идёт ПО ИЗМЕРЕННЫМ ГАБАРИТАМ моделей, а не по предположениям о
    /// том, где у них точка привязки: скрипт спрашивает у меша его bounds и
    /// ставит соседний модуль вплотную. Иначе стены домов расходились бы или
    /// налезали друг на друга при первой же смене набора.
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

            // Село: четыре хаты вдоль дороги, две смотрят на неё фасадом.
            House(new Vector3(-6f, 0f, 2f), 3, 3, wood: true, facing: 0f);
            House(new Vector3(-1.5f, 0f, 2.5f), 3, 2, wood: true, facing: 0f);
            House(new Vector3(3.5f, 0f, 2f), 2, 3, wood: false, facing: 0f);
            House(new Vector3(-4f, 0f, -4f), 2, 2, wood: true, facing: 180f);
            House(new Vector3(2f, 0f, -4.5f), 3, 2, wood: false, facing: 180f);

            // Мельница у воды — ориентир, по которому село узнаётся с первого кадра.
            Place(Town + "watermill.fbx", new Vector3(9f, 0f, -1f), 210f, "Мельница");

            // Частокол: община на краю обитаемого мира огораживается.
            Palisade();

            // Лес вокруг и камни на склоне.
            Forest();

            // Посты: якоря, к которым привязаны жители и метки происшествий.
            var posts = Posts();

            // Жители на постах — масштаб человека рядом с домами.
            var villagers = Villagers(posts);

            // Сутки идут прямо в сцене: свет, жители и лента событий.
            Life(posts, villagers);

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

        /// <summary>Снять то, что открыто сейчас, — без перезагрузки сцены.</summary>
        private static void Capture(string shotPath)
        {
            var camera = Object.FindFirstObjectByType<Camera>();
            if (camera == null) { Debug.LogError("В сцене нет камеры"); return; }

            // Пакетовщик SRP в батч-режиме не обновляет буферы материалов: в
            // контрольном кадре красный, зелёный и синий кубы вышли ОДНИМ
            // цветом, и село получалось сплошь бирюзовым. На время снимка
            // выключаем — в редакторе и в игре он остаётся включённым.
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            bool batcher = urp != null && urp.useSRPBatcher;
            if (urp != null) urp.useSRPBatcher = false;

            const int width = 1920, height = 1080;
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB) { antiAliasing = 8 };
            camera.targetTexture = rt;

            // В URP кадр снимается ЗАПРОСОМ рендера, а не Camera.Render(): второй
            // путь остался от Built-in, конвейер его не обслуживает, и в
            // батч-режиме в текстуру попадает неинициализированная память —
            // радужные полосы вместо картинки. Проверено на первом снимке.
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
        }

        /// <summary>
        /// Контрольный кадр: три куба заведомо известных цветов на чёрном фоне.
        /// Нужен, чтобы отделить «сломан цвет материала» от «сломана сцена».
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
        /// Плёнка суток: конвейер крутится прямо в редакторе, каждая фаза
        /// снимается кадром. Режим игры для этого не нужен — сутки двигает тот
        /// же метод, что и таймер в игре, поэтому плёнка показывает настоящую
        /// жизнь села, а не отдельную «демонстрационную» ветку кода.
        /// </summary>
        [MenuItem("Alpha/Снять плёнку суток")]
        public static void FilmDays()
        {
            Build();

            var life = Object.FindFirstObjectByType<Game.Gameplay.VillageLife>();
            if (life == null) { Debug.LogError("В сцене нет компонента жизни"); return; }

            life.Initialize();

            // Сорок фаз — это двадцать суток. Короче нельзя: харнес показал, что
            // первое происшествие приходит примерно на девятые сутки, и плёнка
            // на пять дней показала бы «ничего не происходит» как приговор.
            const int phases = 40;
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
        /// Хата из модулей: стены по периметру, дверь по фасаду, окна по бокам,
        /// двускатная крыша сверху. Размеры считаются из габаритов самой стены.
        /// </summary>
        private static void House(Vector3 origin, int width, int depth, bool wood, float facing)
        {
            string prefix = wood ? "wall-wood" : "wall";
            var wall = Load(Town + prefix + ".fbx");
            if (wall == null) return;

            var size = MeasureSize(wall);
            float step = Mathf.Max(size.x, 0.1f);
            float height = Mathf.Max(size.y, 0.1f);

            var house = new GameObject(wood ? "Хата" : "Дом");
            house.transform.position = origin;
            house.transform.rotation = Quaternion.Euler(0f, facing, 0f);

            string door = Town + prefix + "-door.fbx";
            string window = Town + prefix + "-window-small.fbx";

            for (int x = 0; x < width; x++)
            {
                // Фасад: посередине дверь.
                bool isDoor = x == width / 2;
                Attach(house, isDoor ? door : Town + prefix + ".fbx",
                    new Vector3(x * step, 0f, 0f), 0f);

                // Задняя стена.
                Attach(house, Town + prefix + ".fbx",
                    new Vector3(x * step, 0f, depth * step), 180f);
            }

            for (int z = 1; z < depth; z++)
            {
                bool isWindow = z == depth / 2;
                Attach(house, isWindow ? window : Town + prefix + ".fbx",
                    new Vector3(0f, 0f, z * step), 270f);
                Attach(house, isWindow ? window : Town + prefix + ".fbx",
                    new Vector3((width - 1) * step, 0f, z * step), 90f);
            }

            // Крыша: скаты вдоль фасада, конёк поверх.
            string gable = Town + "roof-gable.fbx";
            string gableEnd = Town + "roof-gable-end.fbx";
            for (int x = 0; x < width; x++)
            {
                Attach(house, gable, new Vector3(x * step, height, 0f), 0f);
                Attach(house, gable, new Vector3(x * step, height, depth * step), 180f);
            }
            Attach(house, gableEnd, new Vector3(0f, height, depth * step * 0.5f), 270f);
            Attach(house, gableEnd, new Vector3((width - 1) * step, height, depth * step * 0.5f), 90f);
        }

        /// <summary>Частокол вокруг села: колья по периметру с воротами со стороны дороги.</summary>
        private static void Palisade()
        {
            var fence = Load(Town + "fence.fbx");
            if (fence == null) return;

            float step = Mathf.Max(MeasureSize(fence).x, 0.5f);
            var group = new GameObject("Частокол");

            const float left = -11f, right = 12f, near = -9f, far = 8f;

            for (float x = left; x <= right; x += step)
            {
                // Ворота посередине ближней стороны: село не глухая коробка.
                bool gate = Mathf.Abs(x - 0.5f) < step;
                Attach(group, gate ? Town + "fence-gate.fbx" : Town + "fence.fbx",
                    new Vector3(x, 0f, near), 0f);
                Attach(group, Town + "fence.fbx", new Vector3(x, 0f, far), 180f);
            }

            for (float z = near; z <= far; z += step)
            {
                Attach(group, Town + "fence.fbx", new Vector3(left, 0f, z), 90f);
                Attach(group, Town + "fence.fbx", new Vector3(right, 0f, z), 270f);
            }
        }

        /// <summary>Лес и камни. Разброс детерминированный — сцена пересобирается один в один.</summary>
        private static void Forest()
        {
            string[] trees =
            {
                Nature + "tree_default.fbx", Nature + "tree_oak.fbx",
                Nature + "tree_cone.fbx", Nature + "tree_thin.fbx",
                Nature + "tree_default_dark.fbx", Nature + "tree_fat.fbx"
            };
            string[] rocks =
            {
                Nature + "rock_largeA.fbx", Nature + "rock_smallA.fbx",
                Nature + "cliff_blockHalf_rock.fbx"
            };

            var group = new GameObject("Лес");

            for (int i = 0; i < 90; i++)
            {
                float a = Hash(i, 1) * Mathf.PI * 2f;
                float r = 14f + Hash(i, 2) * 9f;
                var pos = new Vector3(Mathf.Cos(a) * r + 0.5f, 0f, Mathf.Sin(a) * r * 0.8f - 0.5f);

                var go = Attach(group, trees[i % trees.Length], pos, Hash(i, 3) * 360f);
                if (go != null)
                {
                    float s = 0.8f + Hash(i, 4) * 0.7f;
                    go.transform.localScale = new Vector3(s, s + Hash(i, 5) * 0.3f, s);
                }
            }

            for (int i = 0; i < 24; i++)
            {
                float a = Hash(i, 6) * Mathf.PI * 2f;
                float r = 11f + Hash(i, 7) * 12f;
                Attach(group, rocks[i % rocks.Length],
                    new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r * 0.8f), Hash(i, 8) * 360f);
            }

            // Трава внутри ограды, чтобы двор не выглядел вытоптанной плитой.
            for (int i = 0; i < 40; i++)
            {
                var pos = new Vector3(-10f + Hash(i, 9) * 21f, 0f, -8f + Hash(i, 10) * 16f);
                Attach(group, Nature + "grass.fbx", pos, Hash(i, 11) * 360f);
            }
        }

        /// <summary>
        /// Посты поселения как якоря сцены. Имя «post:&lt;id&gt;» — это связь с
        /// ядром: тот же идентификатор носит слот назначения, по нему компонент
        /// жизни находит, где стоит человек и где показать происшествие.
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

        private static void Anchor(GameObject parent, string postId, Vector3 position)
        {
            var go = new GameObject("post:" + postId);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = position;
        }

        /// <summary>
        /// Жители стоят на своих постах. Имя «villager:&lt;postId&gt;» — та же
        /// связь: ночью компонент прячет именно тех, кто не патрулирует.
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

                Attach(holder, who[i], Vector3.zero, facing[i]);
            }

            return group;
        }

        /// <summary>
        /// Компонент, который крутит сутки, плюс экранный текст: строка
        /// состояния сверху и лента событий под ней.
        /// </summary>
        private static void Life(GameObject posts, GameObject villagers)
        {
            var cameraGo = Object.FindFirstObjectByType<Camera>();
            var sunLight = Object.FindFirstObjectByType<Light>();

            var life = new GameObject("Жизнь села").AddComponent<Game.Gameplay.VillageLife>();
            life.sun = sunLight;
            life.view = cameraGo;
            life.postsRoot = posts.transform;
            life.villagersRoot = villagers.transform;

            if (cameraGo != null)
            {
                life.headline = Caption(cameraGo.transform, "Строка состояния",
                    new Vector3(-18.6f, 10.2f, 12f), 0.16f);
                life.log = Caption(cameraGo.transform, "Лента событий",
                    new Vector3(-18.6f, 8.6f, 12f), 0.11f);
            }
        }

        /// <summary>
        /// Текст в мире перед камерой. Встроенный шрифт берётся намеренно:
        /// свой (Fixel, OFL) появится вместе с интерфейсом, а срез не должен
        /// ждать вёрстки.
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

        private static void BuildGround()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Земля";
            ground.transform.localScale = new Vector3(6f, 1f, 6f);

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit != null)
            {
                var mat = new Material(lit);
                mat.SetColor("_BaseColor", new Color(0.33f, 0.40f, 0.22f));
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0f);

                Directory.CreateDirectory("Assets/Scenes");
                AssetDatabase.CreateAsset(mat, "Assets/Scenes/Ground.mat");
                ground.GetComponent<Renderer>().sharedMaterial = mat;
            }
        }

        private static void BuildLight()
        {
            var go = new GameObject("Солнце");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.86f);
            light.intensity = 1.25f;
            light.shadows = LightShadows.Soft;
            // Низкое утреннее солнце: тени длинные, рельеф читается.
            go.transform.rotation = Quaternion.Euler(38f, 145f, 0f);
        }

        private static void BuildCamera()
        {
            var go = new GameObject("Камера");
            var cam = go.AddComponent<Camera>();

            // Ходибельная изометрия (US-7.7): ортографическая проекция,
            // разворот на 45 градусов, наклон 30 — канонический изометрический вид.
            cam.orthographic = true;
            cam.orthographicSize = 11f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.68f, 0.78f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;

            // Данные камеры URP добавляем явно: у камеры, созданной кодом, их
            // может не оказаться, и конвейер тогда рисует её не своим путём.
            if (go.GetComponent<UniversalAdditionalCameraData>() == null)
                go.AddComponent<UniversalAdditionalCameraData>();

            go.transform.rotation = Quaternion.Euler(30f, 45f, 0f);
            go.transform.position = Quaternion.Euler(30f, 45f, 0f) * new Vector3(0f, 0f, -45f)
                                    + new Vector3(0f, 0f, -1f);
            go.tag = "MainCamera";

            // Ровный дневной подсвет: без него теневая сторона домов и кроны
            // проваливаются в чёрное, и село читается силуэтами.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.72f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.48f, 0.52f, 0.48f);
            RenderSettings.ambientGroundColor = new Color(0.28f, 0.26f, 0.22f);
            RenderSettings.skybox = null;
        }

        // ================= мелочи =================

        private static GameObject Attach(GameObject parent, string path, Vector3 local, float yaw)
        {
            var prefab = Load(path);
            if (prefab == null) return null;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (go == null) return null;

            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return go;
        }

        private static GameObject Place(string path, Vector3 position, float yaw, string name)
        {
            var prefab = Load(path);
            if (prefab == null) return null;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (go == null) return null;

            go.name = name;
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            return go;
        }

        private static readonly Dictionary<string, GameObject> Cache = new Dictionary<string, GameObject>();

        private static GameObject Load(string path)
        {
            GameObject cached;
            if (Cache.TryGetValue(path, out cached)) return cached;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) Debug.LogWarning("Модель не найдена: " + path);
            Cache[path] = prefab;
            return prefab;
        }

        /// <summary>Габариты модели по мешам — основа раскладки вместо догадок о пивоте.</summary>
        private static Vector3 MeasureSize(GameObject prefab)
        {
            var filters = prefab.GetComponentsInChildren<MeshFilter>();
            if (filters == null || filters.Length == 0) return Vector3.one;

            var bounds = new Bounds(filters[0].sharedMesh.bounds.center, filters[0].sharedMesh.bounds.size);
            for (int i = 1; i < filters.Length; i++)
                if (filters[i].sharedMesh != null)
                    bounds.Encapsulate(filters[i].sharedMesh.bounds);

            return bounds.size;
        }

        /// <summary>Детерминированный «шум» 0..1: те же числа при каждой пересборке.</summary>
        private static float Hash(int i, int salt)
        {
            unchecked
            {
                int h = i * 374761393 + salt * 668265263;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return ((h & 0x7fffffff) % 10000) / 10000f;
            }
        }
    }
}
