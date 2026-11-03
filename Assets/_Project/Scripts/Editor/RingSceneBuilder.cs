using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Game.Gameplay.Ring;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Збирає сцену зрізу «Ворожнеча» кодом (як ChronicleSceneBuilder: сцена не лежить у репозиторії готовою, бо посилається
    /// на скрипт за GUID із .meta, а .meta локальні). У сцені лише камера й об'єкт з RingFeudBootstrap: увесь екран —
    /// UI Toolkit, 3D немає. Сцену НЕ додаємо в Build Settings: єдина сцена білду — Game.unity старої збірки.
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

            if (!AssetDatabase.IsValidFolder(SceneFolder)) AssetDatabase.CreateFolder("Assets", "Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[Ринг] Сцену ворожнечі збережено: " + ScenePath);
        }
    }
}
