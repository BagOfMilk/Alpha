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

            // Позначку ставимо лише коли сцена є: якщо зараз збудувати не
            // вийшло (відкрита безіменна сцена з правками), спробуємо наступного разу.
            if (AssetDatabase.LoadAssetAtPath<Object>(ScenePath) != null || Build())
                EditorPrefs.SetBool(AutoCreatedKey, true);
        }

        [MenuItem("Alpha/Пересоздать сцену «Хроника»")]
        public static void Rebuild()
        {
            Build();
        }

        /// <returns>false — сцену не створено: відкрита безіменна сцена з правками.</returns>
        private static bool Build()
        {
            // Вирішуємо ДО створення нової сцени: після неї набір відкритих
            // сцен зміниться, і перевірки перестануть означати те, що потрібно.
            bool untitled = HasUntitledScene();
            bool dirty = HasDirtyScene();

            // Поки відкрита сцена без файлу, Unity не дає створити сцену адитивно
            // («Cannot create a new scene additively with an untitled scene
            // unsaved» — так падало при першому відкритті свіжої копії проєкту,
            // 28.09.2026). З правками її не можна ні закрити, ні лишити.
            if (untitled && dirty)
            {
                Debug.Log("Сцену «Хроніка» не створено: відкрита сцена без імені має незбережені правки. " +
                          "Збережи або закрий її, тоді — меню Alpha/Пересоздать сцену «Хроника».");
                return false;
            }

            var config = EnsureBalanceAssets();
            EnsureFolder("Assets", "Scenes");

            // Безіменну сцену без правок (свіжий проєкт) просто замінюємо —
            // втрачати нічого. Інакше адитивно, а не Single: NewSceneMode.Single
            // закрив би сцену, відкриту у власника.
            var mode = untitled ? NewSceneMode.Single : NewSceneMode.Additive;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, mode);

            AddPreset(scene, config, "1. Тихий хутор", "Тихий хутор",
                tier: 1, days: 90, patrol: true, choiceEvery: 0, active: true);
            AddPreset(scene, config, "2. Село под давлением", "Село под давлением",
                tier: 2, days: 90, patrol: true, choiceEvery: 10, active: false);
            AddPreset(scene, config, "3. Слобода на изломе", "Слобода на изломе",
                tier: 3, days: 120, patrol: false, choiceEvery: 6, active: false);

            EditorSceneManager.SaveScene(scene, ScenePath);
            if (mode == NewSceneMode.Additive) EditorSceneManager.CloseScene(scene, true);

            RegisterInBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (mode == NewSceneMode.Single)
            {
                Debug.Log("Сцена «Хроника» создана и открыта: " + ScenePath +
                          ". Нажми Play — в консоль пойдёт хроника поселения.");
            }
            else
            {
                Debug.Log("Сцена «Хроника» создана: " + ScenePath +
                          ". Открой её и нажми Play. Текущую сцену не трогал.");
            }
            return true;
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

        private static bool HasUntitledScene()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path)) return true;
            return false;
        }

        private static bool HasDirtyScene()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) return true;
            return false;
        }
    }
}
