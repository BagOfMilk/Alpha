using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Збирає сцену «Відкриття» — портретну сцену перших діб, ту саму, що
    /// друкує текстова збірка.
    ///
    /// Сцена генерується, а не лежить у репозиторії, з тієї ж причини, що й
    /// «Хроніка»: сцена посилається на скрипт за GUID із .meta, а .meta
    /// створюються локально і не комітяться. Готова сцена з репозиторію
    /// посилалась би у порожнечу.
    /// </summary>
    public static class OpeningSceneBuilder
    {
        private const string SceneFolder = "Assets/Scenes";
        private const string ScenePath = SceneFolder + "/Opening.unity";
        private const string PortraitFolder = "Assets/Resources/Portraits";

        [MenuItem("Alpha/Пересоздать сцену «Открытие»")]
        public static void Rebuild()
        {
            EnsureFolder("Assets", "Scenes");
            EnsureFolder("Assets", "Resources");
            EnsureFolder("Assets/Resources", "Portraits");
            EnsurePortraitReadme();

            // Режим обирається за тим, чи є що втрачати. Адитивно — щоб не
            // закрити відкриту у власника сцену з незбереженими правками; але в
            // batchmode відкрита порожня безіменна сцена, і адитивно до неї Unity
            // створювати відмовляється («untitled scene unsaved»). Спіймано першим
            // же прогоном у batchmode.
            var mode = CurrentSceneIsIdle() ? NewSceneMode.Single : NewSceneMode.Additive;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, mode);

            var go = new GameObject("ScenePlayer");
            go.AddComponent<ScenePlayer>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Открытие] сцена собрана: " + ScenePath +
                      ". Портреты класть в " + PortraitFolder + "/<id>.png");
        }

        /// <summary>
        /// Художнику потрібно знати, куди класти файли і як їх називати, — інакше
        /// він спитає програміста, а вся затія з ключами якраз у тому, щоб
        /// не питав.
        /// </summary>
        private static void EnsurePortraitReadme()
        {
            const string path = PortraitFolder + "/ЧИТАЙ.txt";
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) return;

            var text =
                "Портреты персонажей.\n\n" +
                "Имя файла = id персонажа из OpeningCast:\n" +
                "  tuhar.png       — Тугар Волк\n" +
                "  zakhar.png      — Захар Беркут\n" +
                "  maksym.png      — Максим Беркут\n" +
                "  myroslava.png   — Мирослава\n" +
                "  keeper.png      — Дід Овсій (кладовщик)\n" +
                "  healer.png      — Знахарка Гафія\n" +
                "  protagonist.png — протагонист\n\n" +
                "Файла нет — сцена всё равно играется, вместо портрета будет\n" +
                "именная заглушка. Ничего в коде менять не нужно.\n\n" +
                "Лицензии: см. docs/ASSETS.md. Бесплатно не равно общественное\n" +
                "достояние — проверяй условия на КАЖДЫЙ файл.\n";

            System.IO.File.WriteAllText(path, text);
            AssetDatabase.ImportAsset(path);
        }

        private static void AddToBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int i = 0; i < scenes.Count; i++)
                if (scenes[i].path == ScenePath) return;

            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>Відкрита порожня безіменна сцена: втрачати нічого.</summary>
        private static bool CurrentSceneIsIdle()
        {
            var active = SceneManager.GetActiveScene();
            return string.IsNullOrEmpty(active.path) && !active.isDirty;
        }

        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name))
                AssetDatabase.CreateFolder(parent, name);
        }
    }
}
