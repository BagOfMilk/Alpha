using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Налаштування імпорту сторонніх наборів Kenney.
    ///
    /// Навіщо скриптом, а не руками в інспекторі: моделей більше п'ятисот, і
    /// виставляти їм імпорт по одній — робота, яку ніхто не повторить на
    /// іншій машині. Тут правило записане один раз і застосовується до всього,
    /// що лежить під ThirdParty/Kenney.
    ///
    /// Головне тут — матеріали, і це місце, де легко помилитись.
    ///
    /// Перша версія просила імпортер зробити матеріали «по-старому»
    /// (ImportStandard), а потім міняла їм шейдер на URP/Lit вже готовим.
    /// Результат: текстури малювались правильно, а КОЛІР матеріалу перетворювався
    /// на один і той самий бірюзовий — і в крон дерев, і в землі. Підмінений
    /// шейдер отримує чужий набір властивостей, і колір читається не звідти.
    ///
    /// Правильно — не підміняти, а одразу просити матеріали під активний
    /// пайплайн: ImportViaMaterialDescription робить URP/Lit за описом із
    /// самого FBX.
    /// </summary>
    public sealed class KenneyImportSettings : AssetPostprocessor
    {
        private const string Root = "Assets/ThirdParty/Kenney/";
        private const string NatureKitRoot = Root + "NatureKit/";

        private bool IsKenney => assetPath != null && assetPath.StartsWith(Root);

        /// <summary>
        /// Nature Kit — єдиний з трьох наборів без жодної текстури
        /// (див. B8): у Fantasy Town Kit і Mini Characters є спільний атлас
        /// (<c>Textures/variation-a.png</c>, <c>Textures/colormap.png</c>),
        /// а у Nature Kit — 0 PNG/JPG на всі 329 моделей. Палітра нижче править
        /// лише його матеріали: у решти наборів збіг імені
        /// матеріалу був би випадковим і зіпсував би текстуру перефарбуванням.
        /// </summary>
        private bool IsNatureKit => assetPath != null && assetPath.StartsWith(NatureKitRoot);

        private void OnPreprocessModel()
        {
            if (!IsKenney) return;

            var importer = (ModelImporter)assetImporter;

            // Матеріали назовні: так їх можна лагодити і перефарбовувати під сеттинг,
            // не чіпаючи сам FBX. Спільна текстура в набору одна, тому
            // матеріалів вийдуть одиниці, а не по одному на модель.
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;

            // Не застаріла настройка (перевірено по факту, B8): тримаємо матеріали
            // зовнішніми файлами явно, а не мовчки покладаємось на значення за
            // замовчуванням. Це не косметика — саме в зовнішні папки "Materials"
            // імпортер кладе результат, і саме їх чистить Reimport() нижче
            // перед перезбіркою; без External чистити було б нічого.
            importer.materialLocation = ModelImporterMaterialLocation.External;

            // Анімації є лише у персонажів; у будинків і дерев імпортувати
            // нічого, і вимкнення економить хвилини на п'ятистах моделях.
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
        /// Іменовані оверрайди заготовкових кольорів Nature Kit (R18/§3
        /// design-unity.md). Ключ — ім'я матеріалу ПІСЛЯ імпорту (збігається з
        /// ім'ям у самому FBX, без префікса "Material::"). Список — усі
        /// матеріали набору (див. <c>Models/Materials/*.mat</c> і текст FBX),
        /// у яких заготовковий diffuse читається неправдоподібно для
        /// карпатського села; квіти/ягоди/кора/вода навмисно не входять —
        /// їхні заготовкові тони і так схожі на природні.
        /// </summary>
        private static readonly Dictionary<string, Color> NaturePalette = new Dictionary<string, Color>
        {
            // Крона і трава: заготовка — бірюза, ліс має бути зеленим.
            { "leafsGreen", new Color(0.243f, 0.482f, 0.192f) },
            { "leafsDark", new Color(0.145f, 0.318f, 0.133f) },
            { "grass", new Color(0.286f, 0.522f, 0.204f) },

            // Камінь: заготовка — пастельна голубизна, гірська порода — сіра.
            { "stone", new Color(0.55f, 0.55f, 0.52f) },
            { "stoneDark", new Color(0.38f, 0.38f, 0.36f) },

            // Земля і дерево: заготовка — насичений апельсин, потрібні землисті тони.
            { "dirt", new Color(0.42f, 0.30f, 0.20f) },
            { "dirtDark", new Color(0.30f, 0.21f, 0.14f) },
            { "wood", new Color(0.55f, 0.38f, 0.24f) },
            { "woodDark", new Color(0.36f, 0.24f, 0.16f) },
            { "woodBark", new Color(0.40f, 0.27f, 0.18f) },
            { "woodBarkDark", new Color(0.28f, 0.19f, 0.13f) },
        };

        /// <summary>
        /// Переімпорт усіх наборів: потрібен після зміни правил імпорту, інакше
        /// моделі лишаться з матеріалами, зробленими за старим правилом.
        /// </summary>
        [MenuItem("Alpha/Переимпортировать наборы Kenney")]
        public static void Reimport()
        {
            // Матеріали, створені попереднім правилом, видаляються: імпортер
            // збере їх заново, а не підхопить зламані.
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

                // Шейдер НЕ чіпаємо — його обрав імпортер під активний
                // пайплайн. Правимо лише вигляд поверхні: стилізовані
                // моделі не блищать, інакше хати і крони виглядають мокрими під
                // будь-яким світлом.
                bool changed = false;
                if (mat.HasProperty("_Smoothness") && mat.GetFloat("_Smoothness") != 0f)
                { mat.SetFloat("_Smoothness", 0f); changed = true; }
                if (mat.HasProperty("_Glossiness") && mat.GetFloat("_Glossiness") != 0f)
                { mat.SetFloat("_Glossiness", 0f); changed = true; }
                if (changed) matte++;

                // Nature Kit без текстур: колір матеріалу — сирий diffuse із
                // самого FBX (перевірено по тексту файлу, B8), не баг
                // імпортера і не гамма. У частини імен ця заготовка —
                // бірюза/пастель, а не зелень/камінь: "leafsGreen" —
                // (0.1608, 0.7882, 0.6706), "leafsDark" — (0.1686, 0.6510,
                // 0.6667), "grass" — (0.1725, 0.8471, 0.7216), "stone" —
                // (0.7216, 0.8863, 0.9098). Правимо точково за іменем
                // матеріалу (= ім'я файлу) на правдоподібні карпатські тони.
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
