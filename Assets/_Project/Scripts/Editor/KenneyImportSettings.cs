using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Настройки импорта сторонних наборов Kenney.
    ///
    /// Зачем скриптом, а не руками в инспекторе: моделей больше пятисот, и
    /// выставлять им импорт по одной — работа, которую никто не повторит на
    /// другой машине. Здесь правило записано один раз и применяется ко всему,
    /// что лежит под ThirdParty/Kenney.
    ///
    /// Главное здесь — материалы, и это место, где легко ошибиться.
    ///
    /// Первая версия просила импортёр сделать материалы «по-старому»
    /// (ImportStandard), а потом меняла им шейдер на URP/Lit уже готовым.
    /// Результат: текстуры рисовались верно, а ЦВЕТ материала превращался в
    /// один и тот же бирюзовый — и у крон деревьев, и у земли. Подменённый
    /// шейдер получает чужой набор свойств, и цвет читается не оттуда.
    ///
    /// Правильно — не подменять, а сразу просить материалы под активный
    /// пайплайн: ImportViaMaterialDescription делает URP/Lit по описанию из
    /// самого FBX.
    /// </summary>
    public sealed class KenneyImportSettings : AssetPostprocessor
    {
        private const string Root = "Assets/ThirdParty/Kenney/";

        private bool IsKenney => assetPath != null && assetPath.StartsWith(Root);

        private void OnPreprocessModel()
        {
            if (!IsKenney) return;

            var importer = (ModelImporter)assetImporter;

            // Материалы наружу: так их можно чинить и перекрашивать под сеттинг,
            // не трогая сам FBX. Общая текстура у набора одна, поэтому
            // материалов получится единицы, а не по одному на модель.
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.External;

            // Анимации есть только у персонажей; у домов и деревьев импортировать
            // нечего, и выключение экономит минуты на пятистах моделях.
            importer.importAnimation = assetPath.Contains("MiniCharacters");
            importer.animationType = importer.importAnimation
                ? ModelImporterAnimationType.Generic
                : ModelImporterAnimationType.None;

            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
        }

        private void OnPostprocessMaterial(Material material)
        {
            if (!IsKenney || material == null) return;

            // Шейдер НЕ трогаем — его выбрал импортёр под активный пайплайн.
            // Правим только вид поверхности: стилизованные модели не блестят,
            // иначе хаты и кроны выглядят мокрыми под любым светом.
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0f);
        }

        /// <summary>
        /// Переимпорт всех наборов: нужен после смены правил импорта, иначе
        /// модели останутся с материалами, сделанными по старому правилу.
        /// </summary>
        [MenuItem("Alpha/Переимпортировать наборы Kenney")]
        public static void Reimport()
        {
            // Материалы, созданные прежним правилом, удаляются: импортёр
            // соберёт их заново, а не подхватит сломанные.
            foreach (var dir in Directory.GetDirectories(Root, "Materials", SearchOption.AllDirectories))
            {
                AssetDatabase.DeleteAsset(dir.Replace('\\', '/'));
                Debug.Log("Удалены старые материалы: " + dir);
            }

            AssetDatabase.ImportAsset(Root.TrimEnd('/'),
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ImportRecursive);
            AssetDatabase.Refresh();
            Debug.Log("Наборы Kenney переимпортированы");
        }
    }
}
