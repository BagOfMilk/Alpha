using Game.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// Генерирует Boot-сцену (точку входа игры): GameObject «Campaign» с
    /// CampaignHost + сцена в Build Settings. До неё в проекте не было НИ ОДНОЙ
    /// сцены — билд был невозможен, а демо вешались на объекты вручную.
    /// Запуск: меню Alpha → Create Boot Scene или batchmode -executeMethod
    /// Game.EditorTools.BootstrapSeeder.CreateBootScene. Идемпотентно.
    /// </summary>
    public static class BootstrapSeeder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Boot.unity";

        [MenuItem("Alpha/Create Boot Scene")]
        public static void CreateBootScene()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Scenes"))
                AssetDatabase.CreateFolder("Assets/_Project", "Scenes");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var host = new GameObject("Campaign");
            host.AddComponent<CampaignHost>();
            EditorSceneManager.SaveScene(scene, ScenePath);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[BootstrapSeeder] Boot-сцена создана и добавлена в Build Settings: " + ScenePath);
        }
    }
}
