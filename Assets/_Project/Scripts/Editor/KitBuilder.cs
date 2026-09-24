using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Общие строительные примитивы для сборщиков сцен из наборов Kenney.
    ///
    /// Пакет B8: вынесены из <see cref="VillageShowcase"/>, где раньше жили как
    /// приватные методы одного файла. Причина выноса — будущий
    /// <c>GameSceneBuilder</c> (пакет E1) собирает ту же самую единственную
    /// сцену билда (<c>Game.unity</c>, R18) из тех же наборов и нуждается в
    /// тех же кирпичиках (хата из стен, частокол, лес, участок под здание,
    /// якорь поста, детерминированный «шум»), но со своей раскладкой и своими
    /// путями к моделям — поэтому здесь всё параметризовано путём набора
    /// (<c>kitPath</c>), а не завязано на константы одной витрины.
    ///
    /// <see cref="VillageShowcase"/> не изменил поведения: он передаёт сюда те
    /// же константы и числа, что раньше были зашиты внутри его собственных
    /// методов, и получает те же объекты сцены.
    /// </summary>
    public static class KitBuilder
    {
        private static readonly Dictionary<string, GameObject> Cache = new Dictionary<string, GameObject>();

        // ================= кэш и примитивы загрузки =================

        /// <summary>Загрузка префаба с кэшем — набор кладёт по одному файлу на модуль, дублировать чтение не нужно.</summary>
        public static GameObject Load(string path)
        {
            GameObject cached;
            if (Cache.TryGetValue(path, out cached)) return cached;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) Debug.LogWarning("Модель не найдена: " + path);
            Cache[path] = prefab;
            return prefab;
        }

        /// <summary>Поставить модуль набора как ребёнка родителя в локальных координатах.</summary>
        public static GameObject Attach(GameObject parent, string path, Vector3 local, float yaw)
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

        /// <summary>Поставить модуль набора в мировых координатах (без родителя) — ориентиры сцены.</summary>
        public static GameObject Place(string path, Vector3 position, float yaw, string name)
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

        /// <summary>Габариты модели по мешам — основа раскладки вместо догадок о пивоте.</summary>
        public static Vector3 MeasureSize(GameObject prefab)
        {
            var filters = prefab.GetComponentsInChildren<MeshFilter>();
            if (filters == null || filters.Length == 0) return Vector3.one;

            var bounds = new Bounds(filters[0].sharedMesh.bounds.center, filters[0].sharedMesh.bounds.size);
            for (int i = 1; i < filters.Length; i++)
                if (filters[i].sharedMesh != null)
                    bounds.Encapsulate(filters[i].sharedMesh.bounds);

            return bounds.size;
        }

        /// <summary>Детерминированный «шум» 0..1: те же числа при каждой пересборке (инвариант 1 — никакого Random).</summary>
        public static float Hash(int i, int salt)
        {
            unchecked
            {
                int h = i * 374761393 + salt * 668265263;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return ((h & 0x7fffffff) % 10000) / 10000f;
            }
        }

        // ================= земля =================

        /// <summary>Плоскость земли под URP-цвет без блеска — общая подложка для любой сцены набора.</summary>
        public static GameObject Ground(Color baseColor, Vector3 scale, string materialSavePath)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Земля";
            ground.transform.localScale = scale;

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit != null)
            {
                var mat = new Material(lit);
                mat.SetColor("_BaseColor", baseColor);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0f);

                Directory.CreateDirectory(Path.GetDirectoryName(materialSavePath));
                AssetDatabase.CreateAsset(mat, materialSavePath);
                ground.GetComponent<Renderer>().sharedMaterial = mat;
            }

            return ground;
        }

        // ================= постройки =================

        /// <summary>
        /// Хата из модулей: стены по периметру, дверь по фасаду, окна по бокам,
        /// двускатная крыша сверху. Размеры считаются из габаритов самой стены.
        /// <paramref name="kitPath"/> — путь к папке Models набора построек
        /// (например, Fantasy Town Kit), с завершающим слэшем.
        /// </summary>
        public static GameObject House(string kitPath, Vector3 origin, int width, int depth, bool wood, float facing)
        {
            string prefix = wood ? "wall-wood" : "wall";
            var wall = Load(kitPath + prefix + ".fbx");
            if (wall == null) return null;

            var size = MeasureSize(wall);
            float step = Mathf.Max(size.x, 0.1f);
            float height = Mathf.Max(size.y, 0.1f);

            var house = new GameObject(wood ? "Хата" : "Дом");
            house.transform.position = origin;
            house.transform.rotation = Quaternion.Euler(0f, facing, 0f);

            string door = kitPath + prefix + "-door.fbx";
            string window = kitPath + prefix + "-window-small.fbx";

            for (int x = 0; x < width; x++)
            {
                // Фасад: посередине дверь.
                bool isDoor = x == width / 2;
                Attach(house, isDoor ? door : kitPath + prefix + ".fbx",
                    new Vector3(x * step, 0f, 0f), 0f);

                // Задняя стена.
                Attach(house, kitPath + prefix + ".fbx",
                    new Vector3(x * step, 0f, depth * step), 180f);
            }

            for (int z = 1; z < depth; z++)
            {
                bool isWindow = z == depth / 2;
                Attach(house, isWindow ? window : kitPath + prefix + ".fbx",
                    new Vector3(0f, 0f, z * step), 270f);
                Attach(house, isWindow ? window : kitPath + prefix + ".fbx",
                    new Vector3((width - 1) * step, 0f, z * step), 90f);
            }

            // Крыша: скаты вдоль фасада, конёк поверх.
            string gable = kitPath + "roof-gable.fbx";
            string gableEnd = kitPath + "roof-gable-end.fbx";
            for (int x = 0; x < width; x++)
            {
                Attach(house, gable, new Vector3(x * step, height, 0f), 0f);
                Attach(house, gable, new Vector3(x * step, height, depth * step), 180f);
            }
            Attach(house, gableEnd, new Vector3(0f, height, depth * step * 0.5f), 270f);
            Attach(house, gableEnd, new Vector3((width - 1) * step, height, depth * step * 0.5f), 90f);
            return house;
        }

        /// <summary>
        /// Участок под здание: спрятанная модель + подпись, обе выключены —
        /// компонент жизни поднимает участок по стадиям стройки и подписывает,
        /// когда здание готово. Имя объекта — «plot:&lt;plotId&gt;», это связь
        /// с ядром (тот же id носит здание в каталоге).
        /// </summary>
        public static GameObject Plot(string kitPath, GameObject root, string plotId, string labelText,
            Vector3 at, int width, int depth, bool wood)
        {
            var plot = new GameObject("plot:" + plotId);
            plot.transform.SetParent(root.transform, false);
            plot.transform.localPosition = at;

            var model = House(kitPath, Vector3.zero, width, depth, wood, 0f);
            if (model != null)
            {
                model.name = "model";
                model.transform.SetParent(plot.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.SetActive(false);
            }

            // Подпись над зданием смотрит в камеру: изометрия не вращается.
            var label = new GameObject("label");
            label.transform.SetParent(plot.transform, false);
            label.transform.localPosition = new Vector3(width * 0.5f, 3.2f, depth * 0.5f);
            label.transform.rotation = Quaternion.Euler(30f, 45f, 0f);

            var text = label.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 48;
            text.characterSize = 0.08f;
            text.anchor = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = labelText;
            var renderer = label.GetComponent<MeshRenderer>();
            if (renderer != null && text.font != null) renderer.sharedMaterial = text.font.material;
            label.SetActive(false);

            return plot;
        }

        /// <summary>Частокол по прямоугольному периметру с воротами на одной из ближних сторон.</summary>
        public static GameObject Palisade(string kitPath, float left, float right, float near, float far, float gateX)
        {
            var fence = Load(kitPath + "fence.fbx");
            if (fence == null) return null;

            float step = Mathf.Max(MeasureSize(fence).x, 0.5f);
            var group = new GameObject("Частокол");

            for (float x = left; x <= right; x += step)
            {
                bool gate = Mathf.Abs(x - gateX) < step;
                Attach(group, gate ? kitPath + "fence-gate.fbx" : kitPath + "fence.fbx",
                    new Vector3(x, 0f, near), 0f);
                Attach(group, kitPath + "fence.fbx", new Vector3(x, 0f, far), 180f);
            }

            for (float z = near; z <= far; z += step)
            {
                Attach(group, kitPath + "fence.fbx", new Vector3(left, 0f, z), 90f);
                Attach(group, kitPath + "fence.fbx", new Vector3(right, 0f, z), 270f);
            }

            return group;
        }

        /// <summary>
        /// Лес и камни вокруг поляны, трава внутри неё. Разброс детерминированный
        /// (инвариант 1) — те же деревья на тех же местах при каждой пересборке.
        /// <paramref name="naturePath"/> — путь к папке Models Nature Kit.
        /// </summary>
        public static GameObject Forest(string naturePath)
        {
            string[] trees =
            {
                naturePath + "tree_default.fbx", naturePath + "tree_oak.fbx",
                naturePath + "tree_cone.fbx", naturePath + "tree_thin.fbx",
                naturePath + "tree_default_dark.fbx", naturePath + "tree_fat.fbx"
            };
            string[] rocks =
            {
                naturePath + "rock_largeA.fbx", naturePath + "rock_smallA.fbx",
                naturePath + "cliff_blockHalf_rock.fbx"
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
                Attach(group, naturePath + "grass.fbx", pos, Hash(i, 11) * 360f);
            }

            return group;
        }

        // ================= посты =================

        /// <summary>
        /// Якорь поста поселения. Имя «post:&lt;id&gt;» — связь с ядром: тот
        /// же идентификатор носит слот назначения, по нему компонент жизни
        /// находит, где стоит человек и где показать происшествие.
        /// </summary>
        public static GameObject Anchor(GameObject parent, string id, Vector3 position)
        {
            var go = new GameObject("post:" + id);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = position;
            return go;
        }
    }
}
