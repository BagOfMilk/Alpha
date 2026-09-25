using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Сама збирає сцену «Хроніка», щоб власнику проекту не доводилось
    /// вручну створювати об'єкт і вішати на нього компонент.
    ///
    /// Чому сцена не лежить у репозиторії готовою: сцена Unity посилається на
    /// скрипт за GUID із його .meta-файлу, а .meta генеруються локально і в
    /// репозиторій не закомічені. Готова сцена з репозиторію посилалась би
    /// у порожнечу («Missing (Mono Script)»). Скрипт цієї проблеми не має:
    /// Unity зв'язує компонент за типом C#, GUID підставляються самі.
    /// </summary>
    [InitializeOnLoad]
    public static class ChronicleSceneBuilder
    {
        private const string SceneFolder = "Assets/Scenes";
        private const string ScenePath = SceneFolder + "/Chronicle.unity";
        private const string BalanceFolder = "Assets/_Project/Balance";

        /// <summary>Щоб вручну видалену сцену не воскрешати проти волі власника.</summary>
        private const string AutoCreatedKey = "Alpha.ChronicleScene.AutoCreated";

        static ChronicleSceneBuilder()
        {
            // delayCall: під час InitializeOnLoad AssetDatabase ще не готова.
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
            // Вирішуємо ДО створення адитивної сцени: після неї активна сцена
            // може змінитися, і перевірка перестане означати те, що потрібно.
            bool nothingToLose = CurrentSceneIsIdle();

            var config = EnsureBalanceAssets();
            EnsureFolder("Assets", "Scenes");

            // Адитивно, а не Single: NewSceneMode.Single закрив би сцену,
            // відкриту у власника, разом із незбереженими правками.
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
        /// Три пресети з однаковими правилами, але різною долею —
        /// наочно, що результат визначають рішення гравця, а не кидок кубика.
        /// Активний лише перший: інакше три хроніки змішаються в консолі.
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
        /// Ассети балансу створюємо одразу: власник править числа в інспекторі,
        /// а не лізе за ними в код. Значення за замовчуванням збігаються з кодом,
        /// тому поведінка не змінюється — змінюється доступність.
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
        /// Чи можна відкрити нову сцену, нічого не втративши: активна сцена
        /// без файлу, без правок і без об'єктів.
        /// </summary>
        private static bool CurrentSceneIsIdle()
        {
            var active = SceneManager.GetActiveScene();
            return string.IsNullOrEmpty(active.path) && !active.isDirty && active.rootCount == 0;
        }
    }
}
