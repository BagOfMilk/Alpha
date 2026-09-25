using System.Collections.Generic;
using Game.Gameplay;
using UnityEditor;
using UnityEngine;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Пакет E2 — наповнення <c>World/BattleArena</c>: викликається РЕФЛЕКСІЄЮ
    /// з <c>GameSceneBuilder</c> (E1b), щоб той компілювався і без цього
    /// пакета (шов §5 TEST_BUILD.md):
    /// <c>Type.GetType("Game.Gameplay.EditorTools.BattleArenaBuilder, Game.Gameplay.Editor")
    /// ?.GetMethod("Build")?.Invoke(null, new object[]{ arenaRoot, arenaCamera })</c>.
    /// Сигнатура публічного статичного <see cref="Build"/> має лишатися
    /// точно такою — інакше рефлексія в E1b мовчки нічого не знайде.
    ///
    /// Присвоює <see cref="BattleArenaController"/>/<see cref="PortraitRig"/>
    /// готові пули префабів Kenney (Mini Characters — юніти/портрети; Nature
    /// Kit — укриття Half/Full), щоб рантайм-код (лінт-виключений, §
    /// BattleArenaController.cs) НІКОЛИ не кликав <c>AssetDatabase</c>/
    /// <c>PrefabUtility</c> (Editor-only, недоступні в білді гравця) — уся
    /// робота з асетами тут, одноразово, при збірці сцени.
    /// </summary>
    public static class BattleArenaBuilder
    {
        private const string Chars = "Assets/ThirdParty/Kenney/MiniCharacters/Models/";
        private const string Nature = "Assets/ThirdParty/Kenney/NatureKit/Models/";

        /// <summary>
        /// Фікс-ревью (major, знайдено QA): раніше кожен тайл грида —
        /// голий <c>GameObject.CreatePrimitive(PrimitiveType.Quad)</c> з
        /// одним спільним нефарбованим URP/Lit-матеріалом, і лише колір
        /// (MaterialPropertyBlock, §BattleArenaController.ApplyTileTint)
        /// відрізняв тайли — читалось як таблиця, не "земля". Nature Kit не
        /// має ЖОДНОЇ текстури на всі 329 моделей (0 PNG/JPG,
        /// §KenneyImportSettings.IsNatureKit) — "накласти текстуру" з
        /// набору фізично нема чим. Але <c>ground_grass.fbx</c> виміряно
        /// (тимчасовий Editor-пробник, разово, і рендером зверху — це
        /// звичайний осьовий квадрат, не ромб, на відміну від
        /// <c>cliff_block_rock.fbx</c> нижче) РІВНО 1×0×1 з центром у
        /// (0,0,0): він і є "готовий тайл ґрунту" набору, збіг зі
        /// <see cref="BattleArenaView.TileSize"/> точний, без підбору масштабу.
        /// </summary>
        private const string GroundTileModel = Nature + "ground_grass.fbx";

        private static readonly string[] MaleModels =
        {
            "character-male-a.fbx", "character-male-b.fbx", "character-male-c.fbx",
            "character-male-d.fbx", "character-male-e.fbx", "character-male-f.fbx"
        };

        private static readonly string[] FemaleModels =
        {
            "character-female-a.fbx", "character-female-b.fbx", "character-female-c.fbx",
            "character-female-d.fbx", "character-female-e.fbx", "character-female-f.fbx"
        };

        // Half — прохідне з боку укриття (тин/колода), Full — суцільне (камінь/скеля).
        private static readonly string[] CoverHalfModels =
        {
            "fence_simple.fbx", "fence_simpleHigh.fbx", "log.fbx", "log_stack.fbx"
        };

        /// <summary>
        /// Фікс-ревью (major, знайдено QA, "big flat green diamond primitive
        /// ... does not match any Kenney rock/cliff/fence asset silhouette"):
        /// винесено <c>cliff_block_rock.fbx</c> — розібрано тимчасовим
        /// Editor-пробником (разово, знято): зверху це РІВНО один плаский
        /// зелений квадрат без жодного рельєфу/фактури (на відміну від решти
        /// чотирьох — у них зверху видно зелений верх ІЗ земляним/скельним
        /// обвідом), а <see cref="PlaceCoverProp"/> ще й крутить кожне
        /// укриття на детермінований, але довільний кут (Hash01
        /// "cover_yaw_x_y") — плаский квадрат під кутом близьким до 45°
        /// читається саме як "зелений ромб", не як камінь. Решта чотирьох —
        /// перевірені тим самим пробником, кожна дає впізнаваний силует
        /// каменя/скелі під будь-яким поворотом.
        /// </summary>
        private static readonly string[] CoverFullModels =
        {
            "rock_largeA.fbx", "rock_largeC.fbx", "rock_largeE.fbx", "cliff_half_rock.fbx"
        };

        /// <summary>Викликається <c>GameSceneBuilder</c> (E1b) рефлексією — сигнатура фіксована швом, не змінювати без узгодження.</summary>
        public static void Build(GameObject arenaRoot, Camera arenaCamera)
        {
            if (arenaRoot == null)
            {
                Debug.LogWarning("[BattleArenaBuilder] arenaRoot == null — немає куди ставити презентер бою.");
                return;
            }

            var malePool = LoadAll(Chars, MaleModels);
            var femalePool = LoadAll(Chars, FemaleModels);
            var coverHalfPool = LoadAll(Nature, CoverHalfModels);
            var coverFullPool = LoadAll(Nature, CoverFullModels);
            var maleClips = LoadClipSets(Chars, MaleModels);
            var femaleClips = LoadClipSets(Chars, FemaleModels);

            var controller = arenaRoot.GetComponent<BattleArenaController>();
            if (controller == null) controller = arenaRoot.AddComponent<BattleArenaController>();
            controller.ArenaRoot = arenaRoot;
            controller.ArenaCamera = arenaCamera;
            controller.MaleCharacterPrefabs = malePool;
            controller.FemaleCharacterPrefabs = femalePool;
            controller.CoverHalfPrefabs = coverHalfPool;
            controller.CoverFullPrefabs = coverFullPool;
            controller.MaleClipSets = maleClips;
            controller.FemaleClipSets = femaleClips;
            controller.TileGroundPrefab = Load(GroundTileModel);

            var portraits = arenaRoot.GetComponent<PortraitRig>();
            if (portraits == null) portraits = arenaRoot.AddComponent<PortraitRig>();
            portraits.MaleCharacterPrefabs = malePool;
            portraits.FemaleCharacterPrefabs = femalePool;

            BuildStaticProps(arenaRoot);

            Debug.Log("[BattleArenaBuilder] арену наповнено: " + malePool.Length + " чол./" + femalePool.Length +
                       " жін. моделей, " + coverHalfPool.Length + " Half/" + coverFullPool.Length + " Full укриттів, " +
                       maleClips.Length + "+" + femaleClips.Length + " наборів бойових кліпів.");
        }

        /// <summary>
        /// Бій v2 (docs/COMBAT_V2.md §3): один <see cref="BattleCharacterClips"/>
        /// на кожну модель набору, той самий порядок і довжина, що
        /// відповідний пул префабів (<see cref="LoadAll"/>) — контролер бере
        /// пару "префаб/кліпи" за ОДНИМ і тим самим індексом (детермінований
        /// хеш id юніта). Кліпи беруться з ТОГО САМОГО FBX, що й модель
        /// (<c>AssetDatabase.LoadAllAssetsAtPath</c>, той самий прийом, що
        /// <c>GameSceneBuilder.Clip</c>) — набір Kenney Mini Characters несе
        /// повний бойовий каталог one-shot тейків у кожному файлі (аудит
        /// ASSETS, 25.09.2026): idle/walk/sprint/attack-melee-right/
        /// holding-right-shoot/die/interact-right/crouch.
        /// </summary>
        private static BattleCharacterClips[] LoadClipSets(string basePath, string[] fileNames)
        {
            var result = new List<BattleCharacterClips>(fileNames.Length);
            foreach (var name in fileNames)
            {
                string path = basePath + name;
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue; // модель не знайдено — LoadAll вище вже попередив
                result.Add(new BattleCharacterClips
                {
                    Idle = ClipFrom(path, "idle"),
                    Walk = ClipFrom(path, "walk"),
                    Sprint = ClipFrom(path, "sprint"),
                    AttackMelee = ClipFrom(path, "attack-melee-right"),
                    HoldingShoot = ClipFrom(path, "holding-right-shoot"),
                    Die = ClipFrom(path, "die"),
                    Interact = ClipFrom(path, "interact-right"),
                    Crouch = ClipFrom(path, "crouch")
                });
            }
            return result.ToArray();
        }

        /// <summary>Той самий прийом, що <c>GameSceneBuilder.Clip</c> — кліп із FBX за ім'ям дубля; відсутній (не критично — не кожна модель несе кожен такт) віддає <c>null</c> без попередження.</summary>
        private static AnimationClip ClipFrom(string fbxPath, string clipName)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                var clip = asset as AnimationClip;
                if (clip != null && clip.name == clipName && !clip.name.StartsWith("__preview__")) return clip;
            }
            return null;
        }

        /// <summary>
        /// Незмінне обрамлення арени — не тайли (ті будує рантайм-контролер
        /// під конкретний грид кожного бою): широка земля під ЛЮБИЙ розмір
        /// грида (макс. 10×10, §TEST_BUILD.md R9) з запасом і кілька каменів
        /// по кутах, щоб порожнеча навколо грида не була голою площиною.
        ///
        /// Фікс-ревью (minor, знайдено QA): попередній масштаб (1.8× — плоскінь
        /// 18×18) рахувався на розмір самого грида, не на те, що РЕАЛЬНО
        /// бачить ортографічна камера. <see cref="BattleArenaView.FrameGrid"/>
        /// на повному 10×10 дає orthographicSize=6.5, а
        /// <see cref="BattleArenaController.HudPanelShiftWorldX"/> зверху
        /// зсуває кадр ще на купу світових одиниць вліво (компенсація лівої
        /// панелі HUD) — на екрані 1600×900 (§TEST_BUILD.md UI-тур) видимий
        /// діапазон X виходить далеко за межі колишньої плоскіні (порахунок:
        /// ліворуч аж до ≈-15, а не -4). Дерева на дальніх кутах опинялись за
        /// краєм землі — "плавали" на тлі. 5× замість 1.8× (плоскінь 50×50,
        /// той самий центр) — запас, що покриває і найширший екран, і
        /// найбільший зсув камери під HUD, без перерахунку щоразу вручну.
        /// </summary>
        private static void BuildStaticProps(GameObject arenaRoot)
        {
            var existing = arenaRoot.transform.Find("StaticProps");
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var root = new GameObject("StaticProps");
            root.transform.SetParent(arenaRoot.transform, false);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "ArenaGround";
            ground.transform.SetParent(root.transform, false);
            ground.transform.localPosition = new Vector3(5f, -0.02f, 5f);
            ground.transform.localScale = new Vector3(5f, 1f, 5f); // Plane 10×10 юнітів на localScale=1

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit != null)
            {
                var mat = new Material(lit);
                // Фікс-ревью (minor, знайдено QA): попередній колір
                // (0.16,0.15,0.13) — темніший за сам фон "порожньої" сцени
                // поза ареною (заміряно по скріншоту: фон ≈(50,50,46)/255,
                // цей колір ≈(41,38,33)/255) — навіть після того, як
                // плоскінь зробили в 5× ширшою (§BuildStaticProps вище), вона
                // лишалась НЕВИДИМА: темніша за темряву, дерева на її площі
                // однаково читались як "у повітрі". Той самий зелений тон, що
                // й трав'яний тайл без укриття за замовчуванням
                // (§BattleArenaView.CoverTint "None") — земля читається
                // продовженням того самого поля, не окремою чорною плямою.
                mat.SetColor("_BaseColor", new Color(0.27f, 0.42f, 0.22f));
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.05f);
                var renderer = ground.GetComponent<Renderer>();
                if (renderer != null) renderer.sharedMaterial = mat;
            }

            var rockPrefab = Load(Nature + "rock_largeB.fbx");
            if (rockPrefab != null)
            {
                PlaceCorner(root, rockPrefab, new Vector3(-2f, 0f, -2f), 15f);
                PlaceCorner(root, rockPrefab, new Vector3(12f, 0f, -2f), 105f);
                PlaceCorner(root, rockPrefab, new Vector3(-2f, 0f, 12f), 255f);
                PlaceCorner(root, rockPrefab, new Vector3(12f, 0f, 12f), 195f);
            }

            // Полірування (ціль 3 «Бойові декорації», owner: "scenery around
            // the arena edges (trees, rocks) for context"): раніше лише
            // чотири камені по кутах — самé поле лишалось голою рівниною.
            // Дерева вздовж країв, поза межами будь-якого гріда (макс. 10×10),
            // тим самим прийомом фіксованого посіву, що вже коректно працює
            // для кутових каменів (жодного Random — інваріант 1).
            var treePrefab = Load(Nature + "tree_default.fbx");
            var treeDarkPrefab = Load(Nature + "tree_default_dark.fbx");
            if (treePrefab != null && treeDarkPrefab != null)
            {
                PlaceCorner(root, treePrefab, new Vector3(-2.5f, 0f, 3.5f), 40f);
                PlaceCorner(root, treeDarkPrefab, new Vector3(-2.2f, 0f, 7f), 160f);
                PlaceCorner(root, treeDarkPrefab, new Vector3(3.5f, 0f, -2.5f), 300f);
                PlaceCorner(root, treePrefab, new Vector3(7f, 0f, -2.2f), 210f);
                PlaceCorner(root, treePrefab, new Vector3(12.5f, 0f, 4f), 80f);
                PlaceCorner(root, treeDarkPrefab, new Vector3(4f, 0f, 12.5f), 260f);

                // Фікс-ревью (ціль А, owner: "scenery around the arena edges"
                // — знайдено тур-автоплеєм): бої зазвичай дрібніші за
                // максимальний 10×10 грід (спостережено 8×6 у ранньому вузлі
                // 1) — FrameGrid кадрує камеру ТІСНІШЕ під фактичний розмір,
                // і всі шість дерев/чотири камені вище (розраховані на повний
                // 10×10) випадають за межі кадру: поле лишається голою
                // рівниною без жодної рослинності в кадрі. Грід завжди
                // починається з (0,0) незалежно від розміру (TileToWorld) —
                // тому цей кут єдиний, що лишається "поруч із краєм" для
                // БУДЬ-ЯКОГО розміру бою; тісний посів тут гарантує хоч якусь
                // видиму рослинність навіть на найдрібнішій арені, не
                // конфліктуючи з тайлами (від'ємні координати ніколи не
                // потрапляють у грід).
                PlaceCorner(root, treePrefab, new Vector3(-1f, 0f, -1f), 55f);
                PlaceCorner(root, treeDarkPrefab, new Vector3(-1.1f, 0f, 1.6f), 190f);
                PlaceCorner(root, treeDarkPrefab, new Vector3(1.6f, 0f, -1.1f), 320f);

                // Фікс-ревью (minor, знайдено QA, "increase prop density
                // around the edges"): земля тепер набагато ширша (5× замість
                // 1.8×, див. коментар вище BuildStaticProps) — і дальні краї,
                // за колишньою межею плоскіні, тепер безпечно нести дерева, не
                // лишаючи їх "у повітрі". Ще один пруток уздовж усіх чотирьох
                // сторін, поза 10×10-гридом (від'ємні/>10 координати ніколи
                // не потрапляють у тайли — той самий доказ, що й вище).
                PlaceCorner(root, treeDarkPrefab, new Vector3(-3f, 0f, 5f), 75f);
                PlaceCorner(root, treePrefab, new Vector3(5f, 0f, -3f), 15f);
                PlaceCorner(root, treeDarkPrefab, new Vector3(13.5f, 0f, 7.5f), 135f);
                PlaceCorner(root, treePrefab, new Vector3(7.5f, 0f, 13.5f), 225f);
                PlaceCorner(root, treePrefab, new Vector3(-2f, 0f, 9.5f), 20f);
                PlaceCorner(root, treeDarkPrefab, new Vector3(9.5f, 0f, -2f), 340f);
            }
        }

        private static void PlaceCorner(GameObject root, GameObject prefab, Vector3 pos, float yaw)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (go == null) return;
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        private static GameObject[] LoadAll(string basePath, string[] fileNames)
        {
            var result = new List<GameObject>(fileNames.Length);
            foreach (var name in fileNames)
            {
                var go = Load(basePath + name);
                if (go != null) result.Add(go);
            }
            return result.ToArray();
        }

        private static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) Debug.LogWarning("[BattleArenaBuilder] модель не знайдено: " + path);
            return prefab;
        }
    }
}
