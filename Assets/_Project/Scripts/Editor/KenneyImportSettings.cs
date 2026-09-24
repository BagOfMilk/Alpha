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

        /// <summary>
        /// Фаза F знахідка: у Unity 6000.4 цей класичний колбек
        /// <c>AssetPostprocessor.OnPostprocessMaterial</c> НЕ викликається для
        /// матеріалів, які FBX-імпортер видобуває зовнішніми файлами через
        /// <c>ModelImporterMaterialImportMode.ImportViaMaterialDescription</c>
        /// (сам `materialLocation.External`, який це вмикав, тепер ще й
        /// позначений obsolete редактором — "no longer supported"). Файли
        /// .mat усе одно з'являються (новий, недокументований шлях
        /// видобування), просто без цього хука. Перевірено діагностичним
        /// логом під час переимпорту: жодного виклику за ~500 моделей.
        /// Тому колір/матовість тепер правляться ПІСЛЯ переімпорту, напряму
        /// по вже створених .mat-файлах — <see cref="ApplyMaterialFixups"/>,
        /// викликається з <see cref="Reimport"/>. Метод лишається тут
        /// незадіяним, а не видаленим — задокументувати граблі для того, хто
        /// наступного разу здивується, чому колбек "є, а не працює".
        /// </summary>
        private void OnPostprocessMaterial(Material material)
        {
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

            ApplyMaterialFixups();
        }

        /// <summary>
        /// Заміна непрацюючого <see cref="OnPostprocessMaterial"/> (див.
        /// коментар там): матовість — усім матеріалам усіх наборів; палітра
        /// Nature Kit — лише тим .mat-файлам під <see cref="NatureKitRoot"/>,
        /// чиє ім'я (== ім'я файлу без розширення, той самий рядок, що й ключ
        /// <see cref="NaturePalette"/>) там знайдене.
        /// </summary>
        private static void ApplyMaterialFixups()
        {
            var guids = AssetDatabase.FindAssets("t:Material", new[] { Root.TrimEnd('/') });
            int matte = 0, painted = 0;
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;

                // Шейдер НЕ трогаем — его выбрал импортёр под активный
                // пайплайн. Правим только вид поверхности: стилизованные
                // модели не блестят, иначе хаты и кроны выглядят мокрыми под
                // любым светом.
                bool changed = false;
                if (mat.HasProperty("_Smoothness") && mat.GetFloat("_Smoothness") != 0f)
                { mat.SetFloat("_Smoothness", 0f); changed = true; }
                if (mat.HasProperty("_Glossiness") && mat.GetFloat("_Glossiness") != 0f)
                { mat.SetFloat("_Glossiness", 0f); changed = true; }
                if (changed) matte++;

                // Nature Kit без текстур: цвет материала — сырой diffuse из
                // самого FBX (проверено по тексту файла, B8), не баг
                // импортёра и не гамма. У части имён эта заготовка —
                // бирюза/пастель, а не зелень/камень: "leafsGreen" —
                // (0.1608, 0.7882, 0.6706), "leafsDark" — (0.1686, 0.6510,
                // 0.6667), "grass" — (0.1725, 0.8471, 0.7216), "stone" —
                // (0.7216, 0.8863, 0.9098). Правим точечно по имени
                // материала (= имя файла) на правдоподобные карпатские тона.
                if (path.StartsWith(NatureKitRoot, System.StringComparison.Ordinal) &&
                    NaturePalette.TryGetValue(mat.name, out var believable))
                {
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", believable);
                    if (mat.HasProperty("_Color")) mat.SetColor("_Color", believable);
                    painted++;
                    changed = true;
                }

                if (changed) EditorUtility.SetDirty(mat);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[Kenney] матовість виправлено у " + matte + " матеріалах, палітру Nature Kit застосовано до " + painted + ".");
        }

        /// <summary>
        /// Фаза F (FOLIAGE): версія палітри — правиш <see cref="NaturePalette"/>
        /// (додаєш/міняєш колір) → піднімаєш цей рядок. Порівнюється з
        /// позначкою в <see cref="MarkerPath"/> (локальний файл під
        /// <c>Library/</c> — не в репозиторії, живе на конкретній машині разом
        /// із самим імпортованим кешем).
        /// </summary>
        private const string PaletteVersion = "carpathian-3";

        private static string MarkerPath =>
            Path.Combine("Library", "KenneyPaletteVersion.txt");

        /// <summary>
        /// Грабли CLAUDE.md: <see cref="NaturePalette"/> в <see cref="ApplyMaterialFixups"/>
        /// перефарбовує матеріал лише ПІСЛЯ явного переімпорту — якщо Library вже тепла
        /// (модель імпортована ДО того, як з'явилась/змінилась палітра), крони
        /// й трава лишаються бірюзовими доти, доки хтось руками не натисне
        /// «Alpha/Переимпортировать…». <c>GameSceneBuilder.Build()</c> кличе
        /// цей метод ПЕРШИМ кроком щоразу — дешева перевірка позначки, реальний
        /// (повільний) <see cref="Reimport"/> лише коли версія розійшлась.
        /// </summary>
        public static void ReimportIfPaletteChanged()
        {
            string existing = File.Exists(MarkerPath) ? File.ReadAllText(MarkerPath).Trim() : null;
            if (existing == PaletteVersion)
            {
                Debug.Log("[Kenney] палітра не змінилась (" + PaletteVersion + ") — переімпорт пропущено.");
                return;
            }

            Debug.Log("[Kenney] позначка палітри '" + (existing ?? "(немає)") + "' != '" + PaletteVersion +
                       "' — переімпортовую набори перед збіркою сцени.");
            Reimport();

            Directory.CreateDirectory("Library");
            File.WriteAllText(MarkerPath, PaletteVersion);
        }
    }
}
