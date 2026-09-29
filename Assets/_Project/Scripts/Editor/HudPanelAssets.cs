using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Спайк H4 (docs/HUD_DESIGN.md §8): асет PanelSettings для шапки і стрічки
    /// на UI Toolkit у <c>Assets/_Project/UI/Resources/</c>. Навіщо асет, якщо
    /// <c>HudToolkitView</c> уміє створити панель і в рантаймі: панель,
    /// створена в редакторі, серіалізує посилання на рантайм-шейдери UI Toolkit
    /// і на тему — вони гарантовано потрапляють у білд через Resources. Панель
    /// з <c>CreateInstance</c> у зібраній грі шукає шейдери за іменем, і якщо
    /// жодна сцена не тримає UIDocument, білд міг їх вирізати. Рантайм бере
    /// асет першим і лише без нього будує панель сам.
    ///
    /// Кличе <see cref="GameSceneBuilder.Build"/> (кожна збірка сцени,
    /// <c>tools/build-unity.ps1</c>); ідемпотентно — наявний асет не чіпає.
    /// </summary>
    public static class HudPanelAssets
    {
        public const string Folder = "Assets/_Project/UI/Resources";
        public const string PanelPath = Folder + "/AlphaHudPanel.asset";
        public const string ThemePath = Folder + "/AlphaHudTheme.tss";

        [MenuItem("Alpha/HUD: створити PanelSettings шапки")]
        public static void Ensure()
        {
            if (AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath) != null) return;

            var panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.scaleMode = PanelScaleMode.ConstantPixelSize;
            panel.scale = 1f;
            panel.sortingOrder = 0f;
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (theme != null) panel.themeStyleSheet = theme;
            else Debug.LogWarning("[HUD] Тема " + ThemePath + " не знайдена — панель без теми (текст малюється, смуги прокрутки ні).");

            AssetDatabase.CreateAsset(panel, PanelPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[HUD] створено " + PanelPath);
        }
    }
}
