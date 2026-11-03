using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Game.Gameplay.Ring;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Збирає сцену зрізу «Ворожнеча» кодом (як ChronicleSceneBuilder: сцена не лежить у репозиторії готовою, бо посилається
    /// на скрипт за GUID із .meta, а .meta локальні). У сцені камера й об'єкт з RingFeudBootstrap (панелі UI Toolkit) і
    /// RingStage3D (3D-ринг під ними; ассети прив'язує RingStageSceneSetup). Сцену НЕ додаємо в Build Settings: єдина сцена білду — Game.unity старої збірки.
    /// Із консолі: Unity.exe -batchmode -quit -projectPath ... -executeMethod Game.Gameplay.EditorTools.RingSceneBuilder.Build
    /// </summary>
    public static class RingSceneBuilder
    {
        public const string SceneFolder = "Assets/Scenes";
        public const string ScenePath = SceneFolder + "/RingFeud.unity";

        [MenuItem("Alpha/Ринг: зібрати сцену ворожнечі")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.055f, 0.05f, 0.065f, 1f);
            camGo.AddComponent<AudioListener>();

            var host = new GameObject("RingFeud");
            host.AddComponent<RingFeudBootstrap>();
            // Ітерація 1: 3D-ринг під панелями (арена, борці, рефері, режисер кліпів).
            RingStageSceneSetup.Attach(host);

            if (!AssetDatabase.IsValidFolder(SceneFolder)) AssetDatabase.CreateFolder("Assets", "Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[Ринг] Сцену ворожнечі збережено: " + ScenePath);
        }

        /// <summary>
        /// Окремий білд зрізу: лише сцена RingFeud у Build/Ring/Ring.exe (Build Settings не чіпаємо — сцена передається
        /// прямо). Для огляду вигляду: <c>Ring.exe -ring-shots &lt;тека&gt;</c> проходить ворожнечу ботом і знімає екрани.
        /// Із консолі: -executeMethod Game.Gameplay.EditorTools.RingSceneBuilder.BuildPlayer
        /// </summary>
        public static void BuildPlayer()
        {
            Build();
            string outDir = Path.Combine(Directory.GetCurrentDirectory(), "Build", "Ring");
            Directory.CreateDirectory(outDir);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(outDir, "Ring.exe"),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.Development
            });
            bool ok = report.summary.result == BuildResult.Succeeded;
            Debug.Log("[Ринг] Білд: " + report.summary.result + ", помилок " + report.summary.totalErrors);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
