using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Собирает сцену «Хроника» сам, чтобы владельцу проекта не приходилось
    /// вручную создавать объект и вешать на него компонент.
    ///
    /// Почему сцена не лежит в репозитории готовой: сцена Unity ссылается на
    /// скрипт по GUID из его .meta-файла, а .meta генерируются локально и в
    /// репозиторий не закоммичены. Готовая сцена из репозитория ссылалась бы
    /// в пустоту («Missing (Mono Script)»). Скрипт этой проблемы не имеет:
    /// Unity связывает компонент по типу C#, GUID подставляются сами.
    /// </summary>
    [InitializeOnLoad]
    public static class ChronicleSceneBuilder
    {
        private const string SceneFolder = "Assets/Scenes";
        private const string ScenePath = SceneFolder + "/Chronicle.unity";
        private const string BalanceFolder = "Assets/_Project/Balance";

        /// <summary>Чтобы удалённую вручную сцену не воскрешать против воли владельца.</summary>
        private const string AutoCreatedKey = "Alpha.ChronicleScene.AutoCreated";

        static ChronicleSceneBuilder()
        {
            // delayCall: во время InitializeOnLoad AssetDatabase ещё не готова.
            EditorApplication.delayCall += AutoCreateOnce;
        }

        private static void AutoCreateOnce()
        {
            if (EditorPrefs.GetBool(AutoCreatedKey, false)) return;
            EditorPrefs.SetBool(AutoCreatedKey, true);

            if (AssetDatabase.LoadAssetAtPath<Object>(ScenePath) != null) return;
            Build();
        }

        [MenuItem("Alpha/Пересоздать сцену «Хроника»")]
        public static void Rebuild()
        {
            Build();
        }

        private static void Build()
        {
            // Решаем ДО создания аддитивной сцены: после неё активная сцена
            // может смениться, и проверка перестанет означать то, что нужно.
            bool nothingToLose = CurrentSceneIsIdle();

            var config = EnsureBalanceAssets();
            EnsureFolder("Assets", "Scenes");

            // Аддитивно, а не Single: NewSceneMode.Single закрыл бы сцену,
            // открытую у владельца, вместе с несохранёнными правками.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            AddPreset(scene, config, "1. Тихий хутор", "Тихий хутор",
                tier: 1, days: 90, patrol: true, choiceEvery: 0, active: true);
            AddPreset(scene, config, "2. Село под давлением", "Село под давлением",
                tier: 2, days: 90, patrol: true, choiceEvery: 10, active: false);
            AddPreset(scene, config, "3. Слобода на изломе", "Слобода на изломе",
                tier: 3, days: 120, patrol: false, choiceEvery: 6, active: false);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.CloseScene(scene, true);

            RegisterInBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (nothingToLose)
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Debug.Log("Сцена «Хроника» создана и открыта: " + ScenePath +
                          ". Нажми Play — в консоль пойдёт хроника поселения.");
            }
            else
            {
                Debug.Log("Сцена «Хроника» создана: " + ScenePath +
                          ". Открой её и нажми Play. Текущую сцену не трогал.");
            }
        }

        /// <summary>
        /// Три пресета с одними и теми же правилами, но разной судьбой —
        /// наглядно, что исход определяют решения игрока, а не бросок кубика.
        /// Активен только первый: иначе три хроники смешаются в консоли.
        /// </summary>
        private static void AddPreset(Scene scene, BalanceConfigAsset config,
            string objectName, string presetName,
            int tier, int days, bool patrol, int choiceEvery, bool active)
        {
            var go = new GameObject(objectName);
            SceneManager.MoveGameObjectToScene(go, scene);

            var demo = go.AddComponent<SettlementChronicleDemo>();
            demo.presetName = presetName;
            demo.balanceAsset = config;
            demo.tier = tier;
            demo.daysToSimulate = days;
            demo.patrolAtNight = patrol;
            demo.heavyChoiceDays = EveryNthDay(choiceEvery, days);
            demo.runOnStart = true;

            go.SetActive(active);
        }

        private static int[] EveryNthDay(int every, int days)
        {
            if (every <= 0) return new int[0];
            var list = new List<int>();
            for (int day = every; day <= days; day += every) list.Add(day);
            return list.ToArray();
        }

        /// <summary>
        /// Ассеты баланса создаём сразу: владелец правит числа в инспекторе,
        /// а не лезет за ними в код. Значения по умолчанию совпадают с кодом,
        /// поэтому поведение не меняется — меняется доступность.
        /// </summary>
        private static BalanceConfigAsset EnsureBalanceAssets()
        {
            EnsureFolder("Assets/_Project", "Balance");

            var config = EnsureAsset<BalanceConfigAsset>(BalanceFolder + "/BalanceConfig.asset");
            config.tension = EnsureAsset<TensionBalanceAsset>(BalanceFolder + "/TensionBalance.asset");
            config.signals = EnsureAsset<SignalBalanceAsset>(BalanceFolder + "/SignalBalance.asset");
            config.pulse = EnsureAsset<PulseBalanceAsset>(BalanceFolder + "/PulseBalance.asset");
            config.checks = EnsureAsset<CheckBalanceAsset>(BalanceFolder + "/CheckBalance.asset");

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            return config;
        }

        private static T EnsureAsset<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;

            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name))
                AssetDatabase.CreateFolder(parent, name);
        }

        private static void RegisterInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var s in scenes)
                if (s.path == ScenePath) return;

            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>
        /// Можно ли открыть новую сцену, ничего не потеряв: активная сцена
        /// без файла, без правок и без объектов.
        /// </summary>
        private static bool CurrentSceneIsIdle()
        {
            var active = SceneManager.GetActiveScene();
            return string.IsNullOrEmpty(active.path) && !active.isDirty && active.rootCount == 0;
        }
    }
}
