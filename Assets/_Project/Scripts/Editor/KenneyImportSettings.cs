using System.Collections.Generic;
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
        private const string NatureKitRoot = Root + "NatureKit/";

        private bool IsKenney => assetPath != null && assetPath.StartsWith(Root);

        /// <summary>
        /// Nature Kit — единственный из трёх наборов без единой текстуры
        /// (см. B8): у Fantasy Town Kit и Mini Characters есть общий атлас
        /// (<c>Textures/variation-a.png</c>, <c>Textures/colormap.png</c>),
        /// а у Nature Kit — 0 PNG/JPG на все 329 моделей. Палитра ниже правит
        /// только его материалы: у остальных наборов совпадение имени
        /// материала было бы случайным и испортило бы текстуру перекраской.
        /// </summary>
        private bool IsNatureKit => assetPath != null && assetPath.StartsWith(NatureKitRoot);

        private void OnPreprocessModel()
        {
            if (!IsKenney) return;

            var importer = (ModelImporter)assetImporter;

            // Материалы наружу: так их можно чинить и перекрашивать под сеттинг,
            // не трогая сам FBX. Общая текстура у набора одна, поэтому
            // материалов получится единицы, а не по одному на модель.
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;

            // Не устаревшая строка (проверено по факту, B8): держим материалы
            // внешними файлами явно, а не молча полагаемся на значение по
            // умолчанию. Это не косметика — именно во внешние "Materials"-папки
            // импортёр кладёт результат, и именно их чистит Reimport() ниже
            // перед пересборкой; без External чистить было бы нечего.
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

            // Nature Kit без текстур: цвет материала — сырой diffuse из самого
            // FBX (проверено по тексту файла, B8), не баг импортёра и не гамма.
            // У части имён эта заготовка — бирюза/пастель, а не зелень/камень:
            // "leafsGreen" — (0.1608, 0.7882, 0.6706), "leafsDark" —
            // (0.1686, 0.6510, 0.6667), "grass" — (0.1725, 0.8471, 0.7216),
            // "stone" — (0.7216, 0.8863, 0.9098). Правим точечно по имени
            // материала на правдоподобные карпатские тона; остальные имена
            // набора (цветы, берёзовая кора, вода, кукуруза, тан гриба…) уже
            // выглядят правдоподобно как есть и не трогаются.
            if (IsNatureKit && NaturePalette.TryGetValue(material.name, out var believable))
            {
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", believable);
                if (material.HasProperty("_Color")) material.SetColor("_Color", believable);
            }
        }

        /// <summary>
        /// Именованные оверрайды заготовочных цветов Nature Kit (R18/§3
        /// design-unity.md). Ключ — имя материала ПОСЛЕ импорта (совпадает с
        /// именем в самом FBX, без префикса "Material::"). Список — все
        /// материалы набора (см. <c>Models/Materials/*.mat</c> и текст FBX),
        /// у которых заготовочный diffuse читается неправдоподобно для
        /// карпатского села; цветы/ягоды/кора/вода намеренно не входят —
        /// их заготовочные тона и так похожи на природные.
        /// </summary>
        private static readonly Dictionary<string, Color> NaturePalette = new Dictionary<string, Color>
        {
            // Крона и трава: заготовка — бирюза, лес должен быть зелёным.
            { "leafsGreen", new Color(0.243f, 0.482f, 0.192f) },
            { "leafsDark", new Color(0.145f, 0.318f, 0.133f) },
            { "grass", new Color(0.286f, 0.522f, 0.204f) },

            // Камень: заготовка — пастельная голубизна, горная порода — серая.
            { "stone", new Color(0.55f, 0.55f, 0.52f) },
            { "stoneDark", new Color(0.38f, 0.38f, 0.36f) },

            // Земля и дерево: заготовка — насыщенный апельсин, нужны землистые тона.
            { "dirt", new Color(0.42f, 0.30f, 0.20f) },
            { "dirtDark", new Color(0.30f, 0.21f, 0.14f) },
            { "wood", new Color(0.55f, 0.38f, 0.24f) },
            { "woodDark", new Color(0.36f, 0.24f, 0.16f) },
            { "woodBark", new Color(0.40f, 0.27f, 0.18f) },
            { "woodBarkDark", new Color(0.28f, 0.19f, 0.13f) },
        };

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
