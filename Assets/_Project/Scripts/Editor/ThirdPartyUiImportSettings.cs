using UnityEditor;
using UnityEngine;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Правила імпорту шрифтів і значків інтерфейсу (Поправка №12.2), як
    /// <c>KenneyImportSettings</c> для моделей. Не лінтується: це глибина
    /// імпортера (TextureImporter, TrueTypeFontImporter), яку заглушка чесно не
    /// покриє; перевіряє сам Unity під час збирання.
    /// </summary>
    public sealed class ThirdPartyUiImportSettings : AssetPostprocessor
    {
        private const string IconsFolder = "Assets/ThirdParty/GameIcons/";
        private const string FontsFolder = "Assets/ThirdParty/Fonts/";

        /// <summary>
        /// Значки — білі PNG 512×512, у HUD мають ~20–28 px. Зменшуємо до 128 і
        /// лишаємо mip-рівні (без них зменшення в 5–6 разів «сиплеться»),
        /// прозорість без ореолу, без стиснення (білий на прозорому під стисненням
        /// дає брудні краї), колір дає тінт у рушії.
        /// </summary>
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(IconsFolder)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 128;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
        }

        /// <summary>
        /// Fixel не має геометричних знаків (▲ ◆ ▸ ▾ ✓), які трапляються в IMGUI:
        /// динамічний шрифт бере їх із системних шрифтів Windows за цим списком.
        /// UI Toolkit на цей список не спирається — шапка і стрічка вживають лише
        /// знаки, що є у Fixel (HudToolkitTheme.SeverityMark).
        /// </summary>
        private void OnPreprocessAsset()
        {
            if (!assetPath.StartsWith(FontsFolder) || !assetPath.EndsWith(".ttf")) return;
            var importer = assetImporter as TrueTypeFontImporter;
            if (importer == null) return;
            importer.fontTextureCase = FontTextureCase.Dynamic;
            importer.includeFontData = true;
            importer.fontNames = new[] { "Segoe UI Symbol", "Segoe UI", "Arial" };
        }
    }
}
