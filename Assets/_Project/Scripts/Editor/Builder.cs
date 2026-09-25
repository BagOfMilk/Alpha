using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Збірка грайбельного білда: тестер не повинен ставити Unity — йому потрібен
    /// каталог з .exe, який запускається подвійним кліком.
    ///
    /// Запуск: меню Alpha → Зібрати білд (Windows), або batchmode
    /// -executeMethod Game.Gameplay.EditorTools.Builder.BuildWindows
    /// (див. tools/build-unity.ps1).
    ///
    /// Сцени беруться з Build Settings — рівно ті, що відкриваються в редакторі:
    /// інакше білд показав би тестеру не ту гру, в яку грали тут.
    /// </summary>
    public static class Builder
    {
        private const string OutputDir = "Build/Windows";
        private const string ExeName = "Alpha.exe";

        [MenuItem("Alpha/Собрать билд (Windows)")]
        public static void BuildWindows()
        {
            RegisterScenes();
            var scenes = EnabledScenes();
            if (scenes.Length == 0)
            {
                Fail("В Build Settings нет включённых сцен — сначала Alpha → Пересоздать сцену «Открытие»");
                return;
            }

            string root = Directory.GetCurrentDirectory();
            string outDir = Path.Combine(root, OutputDir);
            Directory.CreateDirectory(outDir);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(outDir, ExeName),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                // Development-білд навмисно: плейтест потрібен з консоллю і стек-трейсами,
                // інакше репорт «воно впало» неможливо пов'язати з місцем падіння.
                options = BuildOptions.Development
            };

            Debug.Log($"[Builder] Сцены билда ({scenes.Length}), первая — точка входа:\n  " +
                      string.Join("\n  ", scenes));

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                Fail($"Билд не собрался: {summary.result}, ошибок {summary.totalErrors}");
                return;
            }

            Debug.Log($"[Builder] Готово: {summary.outputPath} " +
                      $"({summary.totalSize / (1024 * 1024)} МБ, {summary.totalTime.TotalSeconds:F0} с)");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>
        /// Що взагалі показувати. R18: білд тестової збірки — ОДНА сцена,
        /// <c>Game.unity</c> (титул → створення → хаб → фінал, усе в ній же).
        /// Вона збирається кодом (<c>GameSceneBuilder</c>, пакет E1), тому в
        /// Build Settings до перезбірки її може не бути зовсім — тоді білд
        /// вийшов би пустим, і це явна помилка, а не тихий пропуск.
        ///
        /// Village.unity і Opening.unity (вітрина села і портретна сцена
        /// перших діб) лишаються editor-only: їх можна відкрити і подивитися
        /// в редакторі, але в Build Settings вони більше не потрапляють — список
        /// замінюється цілком, а не доповнюється.
        /// </summary>
        private static void RegisterScenes()
        {
            const string gamePath = "Assets/Scenes/Game.unity";
            if (!File.Exists(gamePath)) return;

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(gamePath, true) };
        }

        /// <summary>Тільки УВІМКНЕНІ сцени і тільки реально наявні на диску.</summary>
        private static string[] EnabledScenes()
        {
            var paths = new List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (!scene.enabled || string.IsNullOrEmpty(scene.path)) continue;
                if (!File.Exists(scene.path))
                {
                    Debug.LogWarning($"[Builder] Сцена из Build Settings отсутствует на диске: {scene.path}");
                    continue;
                }
                paths.Add(scene.path);
            }
            return paths.ToArray();
        }

        /// <summary>У batchmode падаємо з ненульовим кодом — інакше CI/скрипт «зелений» на порожньому місці.</summary>
        private static void Fail(string message)
        {
            Debug.LogError("[Builder] " + message);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            else throw new Exception(message);
        }
    }
}
