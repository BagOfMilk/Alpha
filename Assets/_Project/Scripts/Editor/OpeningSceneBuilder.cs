using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Собирает сцену «Открытие» — портретную сцену первых суток, ту же, что
    /// печатает текстовая сборка.
    ///
    /// Сцена генерируется, а не лежит в репозитории, по той же причине, что и
    /// «Хроника»: сцена ссылается на скрипт по GUID из .meta, а .meta
    /// создаются локально и не коммитятся. Готовая сцена из репозитория
    /// ссылалась бы в пустоту.
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

            // Режим выбирается по тому, есть ли что терять. Аддитивно — чтобы не
            // закрыть открытую у владельца сцену с несохранёнными правками; но в
            // batchmode открыта пустая безымянная сцена, и аддитивно к ней Unity
            // создавать отказывается («untitled scene unsaved»). Поймано первым
            // же прогоном в batchmode.
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
        /// Художнику нужно знать, куда класть файлы и как их называть, — иначе
        /// он спросит программиста, а вся затея с ключами ровно в том, чтобы
        /// не спрашивал.
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

        /// <summary>Открыта пустая безымянная сцена: терять нечего.</summary>
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
