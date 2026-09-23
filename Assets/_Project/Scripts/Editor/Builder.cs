using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Сборка играбельного билда: тестер не должен ставить Unity — ему нужен
    /// каталог с .exe, который запускается двойным кликом.
    ///
    /// Запуск: меню Alpha → Собрать билд (Windows), либо batchmode
    /// -executeMethod Game.Gameplay.EditorTools.Builder.BuildWindows
    /// (см. tools/build-unity.ps1).
    ///
    /// Сцены берутся из Build Settings — ровно те, что открываются в редакторе:
    /// иначе билд показал бы тестеру не ту игру, в которую играли здесь.
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
                // Development-билд намеренно: плейтест нужен с консолью и стек-трейсами,
                // иначе репорт «оно упало» невозможно связать с местом падения.
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
        /// Что вообще показывать. Сцены собираются кодом, поэтому в Build
        /// Settings их может не быть вовсе — тогда билд вышел бы пустым.
        ///
        /// Село идёт первым: это МЕСТО, с него начинается взгляд. Портретная
        /// сцена — люди — следом. Перехода между ними пока нет, и это честно:
        /// они два отдельных вида, а не игра. Игра сейчас — текстовый срез.
        /// Диагностическая сцена в билд не идёт: она для редактора.
        /// </summary>
        private static void RegisterScenes()
        {
            var wanted = new List<string>();
            foreach (var path in new[] { "Assets/Scenes/Village.unity", "Assets/Scenes/Opening.unity" })
                if (File.Exists(path)) wanted.Add(path);

            if (wanted.Count == 0) return;

            var scenes = new List<EditorBuildSettingsScene>();
            for (int i = 0; i < wanted.Count; i++) scenes.Add(new EditorBuildSettingsScene(wanted[i], true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>Только ВКЛЮЧЁННЫЕ сцены и только реально существующие на диске.</summary>
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

        /// <summary>В batchmode падаем с ненулевым кодом — иначе CI/скрипт «зелёный» на пустом месте.</summary>
        private static void Fail(string message)
        {
            Debug.LogError("[Builder] " + message);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            else throw new Exception(message);
        }
    }
}
