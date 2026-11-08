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
    ///
    /// Розподіл Етапу 0 (docs/ring/ZONES.md, 08.11.2026): тут — сцена (зона «Изучение»); збірка Ring.exe (BuildPlayer*) —
    /// RingSceneBuilder.Player.cs (зона «Відео огляд»).
    /// </summary>
    public static partial class RingSceneBuilder
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
    }
}
