using Game.Gameplay.UI;
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
        public const string ArtPath = Folder + "/AlphaHudArt.asset";
        private const string Fonts = "Assets/ThirdParty/Fonts";
        private const string Icons = "Assets/ThirdParty/GameIcons";

        /// <summary>
        /// Ключ значка → файл з <c>Assets/ThirdParty/GameIcons</c> (Поправка №12.2,
        /// атрибуція — там же, <c>ATTRIBUTION.md</c>). Ключі ресурсів — ті самі, що
        /// <c>HudHeaderModel.ResourceRows</c>: два компоненти — «build_component»
        /// і «craft_component» (Поправка №12.5).
        /// </summary>
        private static readonly string[] IconKeys =
        {
            "gold", "food", "build_component", "craft_component",
            "phase.morning", "phase.day", "phase.evening", "phase.night",
            "away", "wounded", "post.empty", "post.closed", "fear", "council",
            "build", "signal", "decision", "patrol", "population", "tier",
        };
        private static readonly string[] IconFiles =
        {
            "two-coins", "bread", "wood-pile", "gears",
            "sunrise", "sun", "sunset", "moon",
            "hiking", "bleeding-wound", "wooden-chair", "padlock", "terror", "round-table",
            "hammer-nails", "ringing-bell", "choice", "watchtower", "meeple-group", "village",
        };

        [MenuItem("Alpha/HUD: створити PanelSettings шапки")]
        public static void Ensure()
        {
            EnsurePanel();
            EnsureArt();
        }

        private static void EnsurePanel()
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

        /// <summary>
        /// Асет шрифтів і значків — щоразу переписує посилання (ідемпотентно):
        /// так нові значки і перейменовані файли підхоплюються без ручної правки.
        /// </summary>
        public static void EnsureArt()
        {
            var art = AssetDatabase.LoadAssetAtPath<HudArt>(ArtPath);
            bool created = art == null;
            if (created) art = ScriptableObject.CreateInstance<HudArt>();

            art.UiRegular = AssetDatabase.LoadAssetAtPath<Font>(Fonts + "/Fixel/FixelText-Regular.ttf");
            art.UiSemiBold = AssetDatabase.LoadAssetAtPath<Font>(Fonts + "/Fixel/FixelText-SemiBold.ttf");
            art.Display = AssetDatabase.LoadAssetAtPath<Font>(Fonts + "/Fixel/FixelDisplay-SemiBold.ttf");
            art.Paper = AssetDatabase.LoadAssetAtPath<Font>(Fonts + "/NotoSerif/NotoSerif-Regular.ttf");
            art.HeadingSerif = AssetDatabase.LoadAssetAtPath<Font>(Fonts + "/NotoSerif/NotoSerif-Bold.ttf");

            var icons = new Texture2D[IconFiles.Length];
            int missing = 0;
            for (int i = 0; i < IconFiles.Length; i++)
            {
                icons[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(Icons + "/" + IconFiles[i] + ".png");
                if (icons[i] == null) missing++;
            }
            art.IconKeys = (string[])IconKeys.Clone();
            art.Icons = icons;

            if (art.UiRegular == null) Debug.LogWarning("[HUD] Fixel не знайдено в " + Fonts + " — інтерфейс лишиться на вбудованому шрифті.");
            if (missing > 0) Debug.LogWarning("[HUD] не знайдено значків: " + missing + " з " + IconFiles.Length + " (" + Icons + ").");

            if (created) AssetDatabase.CreateAsset(art, ArtPath);
            else EditorUtility.SetDirty(art);
            AssetDatabase.SaveAssets();
        }
    }
}
