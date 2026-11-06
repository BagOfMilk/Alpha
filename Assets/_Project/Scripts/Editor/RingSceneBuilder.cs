using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
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
            // Пост-обробка (віньєтка, тональна корекція, м'яке свічення) і згладжування країв: кадр виглядає як шоу, а не макет.
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            var post = new GameObject("RingPost").AddComponent<Volume>();
            post.isGlobal = true;
            post.sharedProfile = EnsurePostProfile();

            var host = new GameObject("Ring");
            // Уся гра (кар'єра альфи — ітерація 1); зріз ворожнечі (RingFeudBootstrap) лишається компонентом без сцени.
            host.AddComponent<RingGameBootstrap>();
            // Ітерація 1: 3D-ринг під панелями (арена, борці, рефері, режисер кліпів).
            RingStageSceneSetup.Attach(host);

            if (!AssetDatabase.IsValidFolder(SceneFolder)) AssetDatabase.CreateFolder("Assets", "Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[Ринг] Сцену ворожнечі збережено: " + ScenePath);
        }

        private const string PostPath = "Assets/Art/Ring/Materials/ring_post.asset";

        /// <summary>Профіль пост-обробки рингу (створюється кодом, лежить у Assets/Art/Ring/Materials).</summary>
        private static VolumeProfile EnsurePostProfile()
        {
            var old = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostPath);
            if (old != null) AssetDatabase.DeleteAsset(PostPath);
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, PostPath);

            var tone = p.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);

            var vig = p.Add<Vignette>(true);
            vig.intensity.Override(0.34f);
            vig.smoothness.Override(0.55f);
            vig.rounded.Override(false);

            var ca = p.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.15f);
            ca.contrast.Override(16f);
            ca.saturation.Override(14f);

            var bloom = p.Add<Bloom>(true);
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.22f);
            bloom.scatter.Override(0.6f);

            EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssets();
            return p;
        }

        /// <summary>
        /// Окремий білд зрізу: лише сцена RingFeud у Build/Ring/Ring.exe (Build Settings не чіпаємо — сцена передається
        /// прямо). Для огляду вигляду: <c>Ring.exe -ring-shots &lt;тека&gt;</c> проходить ворожнечу ботом і знімає екрани.
        /// Із консолі: -executeMethod Game.Gameplay.EditorTools.RingSceneBuilder.BuildPlayer
        /// </summary>
        public static void BuildPlayer() { BuildPlayerTo("Ring", true); }

        /// <summary>Збірка для показу власнику: без Development (без напису «Development Build» у кадрі) — Build/RingShow/Ring.exe.</summary>
        public static void BuildPlayerShow() { BuildPlayerTo("RingShow", false); }

        /// <summary>
        /// Ідентичність застосунку (ТЗ B00 §3): назва «Ринг», значок, без заставки Unity — лише на час збірки Ring.exe й
        /// повертається у finally. Unity-проєкт спільний з архівною Alpha (Alpha.exe з того самого ProjectSettings), тож
        /// ProjectSettings.asset не змінюється. companyName лишається, доки власник не вирішить. Сейви зі старої теки
        /// …/DefaultCompany/Alpha переносить гра при першому запуску (RingGameBootstrap.MigrateSave).
        /// </summary>
        private static void BuildPlayerTo(string folder, bool development)
        {
            Build();
            RingFontAssets.Ensure();
            string outDir = Path.Combine(Directory.GetCurrentDirectory(), "Build", folder);
            Directory.CreateDirectory(outDir);
            string oldName = PlayerSettings.productName;
            bool oldSplash = PlayerSettings.SplashScreen.show;
            var oldIcons = PlayerSettings.GetIcons(NamedBuildTarget.Unknown, IconKind.Any);
            bool ok = false;
            try
            {
                PlayerSettings.productName = "Ринг";
                var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Ring/Brand/app_icon_1024.png");
                if (icon != null) PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
                else Debug.LogWarning("[Ринг] значок застосунку не знайдено: Assets/Art/Ring/Brand/app_icon_1024.png");
                // Заставку вимикаємо, лише якщо ліцензія дозволяє (Unity повертає show = true, якщо ні).
                PlayerSettings.SplashScreen.show = false;
                Debug.Log("[Ринг] заставка Unity: " + (PlayerSettings.SplashScreen.show ? "лишилась (ліцензія не дозволяє)" : "вимкнена"));
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = Path.Combine(outDir, "Ring.exe"),
                    target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = development ? BuildOptions.Development : BuildOptions.None
                });
                ok = report.summary.result == BuildResult.Succeeded;
                Debug.Log("[Ринг] Білд: " + report.summary.result + ", помилок " + report.summary.totalErrors);
            }
            finally
            {
                PlayerSettings.productName = oldName;
                PlayerSettings.SplashScreen.show = oldSplash;
                PlayerSettings.SetIcons(NamedBuildTarget.Unknown, oldIcons, IconKind.Any);
                AssetDatabase.SaveAssets();
            }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
