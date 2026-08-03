using System.Collections.Generic;
using Game.Gameplay;
using Game.Gameplay.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.EditorTools
{
    /// <summary>
    /// Генерирует сцены проекта: Boot (кампания + F5/F9) и Battle (экран боя на
    /// UI Toolkit, итерация 15). Запуск: меню Alpha → … или batchmode
    /// -executeMethod Game.EditorTools.BootstrapSeeder.CreateAllScenes. Идемпотентно.
    /// </summary>
    public static class BootstrapSeeder
    {
        private const string BootPath = "Assets/_Project/Scenes/Boot.unity";
        private const string BattlePath = "Assets/_Project/Scenes/Battle.unity";
        private const string CampaignPath = "Assets/_Project/Scenes/Campaign.unity";
        private const string PanelSettingsPath = "Assets/_Project/UI/BattlePanelSettings.asset";

        [MenuItem("Alpha/Create All Scenes")]
        public static void CreateAllScenes()
        {
            CreateBootScene();
            CreateBattleScene();
            CreateCampaignScene();
        }

        [MenuItem("Alpha/Create Campaign Scene")]
        public static void CreateCampaignScene()
        {
            EnsureScenesFolder();
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/_Project/UI/BattleTheme.tss");
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/CampaignScreen.uxml");
            if (theme == null || uxml == null)
            {
                Debug.LogError("[BootstrapSeeder] Не найдены ассеты UI кампании (tss/uxml)");
                return;
            }

            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, PanelSettingsPath);
            }
            panel.themeStyleSheet = theme;
            EditorUtility.SetDirty(panel);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var go = new GameObject("CampaignScreen");
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = panel;
            doc.visualTreeAsset = uxml;
            go.AddComponent<CampaignScreenController>();

            EditorSceneManager.SaveScene(scene, CampaignPath);
            AddSceneToBuild(CampaignPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[BootstrapSeeder] Campaign-сцена создана: " + CampaignPath);
        }

        [MenuItem("Alpha/Create Boot Scene")]
        public static void CreateBootScene()
        {
            EnsureScenesFolder();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var host = new GameObject("Campaign");
            host.AddComponent<CampaignHost>();
            EditorSceneManager.SaveScene(scene, BootPath);
            AddSceneToBuild(BootPath);
            Debug.Log("[BootstrapSeeder] Boot-сцена создана: " + BootPath);
        }

        [MenuItem("Alpha/Create Battle Scene")]
        public static void CreateBattleScene()
        {
            EnsureScenesFolder();

            // PanelSettings + рантайм-тема (без темы контролы UI Toolkit не стилизуются).
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/_Project/UI/BattleTheme.tss");
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/BattleScreen.uxml");
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/_Project/UI/BattleScreen.uss");
            if (theme == null || uxml == null || uss == null)
            {
                Debug.LogError("[BootstrapSeeder] Не найдены ассеты UI (tss/uxml/uss) в Assets/_Project/UI");
                return;
            }

            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, PanelSettingsPath);
            }
            panel.themeStyleSheet = theme;
            EditorUtility.SetDirty(panel);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var go = new GameObject("BattleScreen");
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = panel;
            doc.visualTreeAsset = uxml;
            go.AddComponent<BattleScreenController>();
            // USS разметка тянет сама: <Style src="BattleScreen.uss"/> внутри UXML.

            EditorSceneManager.SaveScene(scene, BattlePath);
            AddSceneToBuild(BattlePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[BootstrapSeeder] Battle-сцена создана: " + BattlePath);
        }

        private static void EnsureScenesFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Scenes"))
                AssetDatabase.CreateFolder("Assets/_Project", "Scenes");
        }

        /// <summary>Добавляет сцену в Build Settings, не выбрасывая остальные.</summary>
        private static void AddSceneToBuild(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var s in scenes)
                if (s.path == path) return;
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
