using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.Core.Characters;
using Game.Core.Characters.Creation;
using Game.Gameplay.Characters;
using Game.Gameplay.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Лукбук набору персонажів у редакторі (ворота G3, docs/PLAYTEST.md): збирає постаті так само, як гра
    /// (<see cref="CharacterKitPlan"/> → <see cref="CharacterAssembler"/>), рендерить кожну з чотирьох боків в
    /// один PNG і пише звіт: які частини ввімкнено, матеріал, шейдер, текстура, альфа-зріз, відсікання граней.
    /// Постать стоїть в анімованій позі (кліп «стоїть» з бібліотеки, як у грі), знімки — відносно напрямку
    /// тіла за стегнами: перша клітинка — спереду (ворота G2: там має бути обличчя). Для перших образів
    /// кожної статі — ще й серія кадрів ходи збоку (G4) і число «наскільки кадри різні».
    /// Хвилина замість циклу «білд + тур». Меню <c>Alpha/Лукбук набору персонажів</c> або
    /// <c>-executeMethod Game.Gameplay.EditorTools.KitLookbookRender.Run</c>; результат — <c>Logs/kitlook/</c>.
    /// </summary>
    public static class KitLookbookRender
    {
        private const string OutDir = "Logs/kitlook";
        private const int Cell = 384;

        [MenuItem("Alpha/Лукбук набору персонажів")]
        public static void Run()
        {
            ArtImportSettings.EnsureAnimationImport();
            ArtImportSettings.EnsureModelImport();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Directory.CreateDirectory(OutDir);
            var report = new StringBuilder();

            var libGo = CharacterKitBuilder.Build();
            var lib = libGo.GetComponent<CharacterKitLibrary>();
            var anims = libGo.GetComponent<CharacterAnimLibrary>();
            if (lib == null || !lib.IsComplete) { File.WriteAllText(OutDir + "/report.txt", "Набір неповний"); return; }

            var light = new GameObject("key").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.3f;
            light.transform.rotation = Quaternion.Euler(35f, 150f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.48f);

            var camGo = new GameObject("cam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.32f, 0.33f, 0.36f);
            cam.fieldOfView = 24f;

            var looks = new List<KeyValuePair<string, Appearance>>();
            foreach (var g in new[] { Gender.Male, Gender.Female })
            {
                var model = new CreationLookModel(AppearanceCatalog.DefaultProtagonist(g));
                for (int c = 0; c < model.Count(CreationLookField.Culture); c++)
                {
                    for (int o = 0; o < model.Count(CreationLookField.Outfit); o++)
                    {
                        var a = model.Build();
                        looks.Add(new KeyValuePair<string, Appearance>((g == Gender.Male ? "m" : "f") + "-" + a.Culture + "-o" + o, a));
                        model.Step(CreationLookField.Outfit, 1);
                    }
                    model.Step(CreationLookField.Culture, 1);
                }
            }
            foreach (var id in new[] { "zakhar", "maksym", "myroslava", "hafiia", "tuhar" })
            {
                var a = AppearanceCatalog.Named(id);
                if (a != null) looks.Add(new KeyValuePair<string, Appearance>("named-" + id, a));
            }

            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            bool batcher = urp != null && urp.useSRPBatcher;
            if (urp != null) urp.useSRPBatcher = false; // батч-режим: інакше всі матеріали одного кольору (граблі VillageShowcase)
            var motionDone = new HashSet<string>();
            try
            {
                foreach (var look in looks)
                {
                    var plan = CharacterKitPlan.From(look.Value, null).Stowed(); // як у селі: зброя за спиною
                    var root = new GameObject("look");
                    var modelGo = CharacterAssembler.Build(lib, plan, root.transform, 0);
                    report.AppendLine("== " + look.Key + " · " + plan.Signature());
                    if (modelGo == null) { report.AppendLine("   НЕ ЗІБРАНО"); Object.DestroyImmediate(root); continue; }
                    var bindFacing = CharacterAssembler.Facing(modelGo);
                    var style = AnimStateTable.StyleOf(KitFigure.WeaponOf(plan));
                    var idle = anims != null ? anims.For(CharacterAnimState.Idle, style) : null;
                    var graph = Pose(modelGo, idle, 0.4f);
                    var thighs = CharacterAssembler.Facing(modelGo);
                    var facing = CharacterAssembler.BodyFacing(modelGo);
                    report.AppendLine("   напрям тіла від +Z кореня: бінд (стегна) " + Yaw(bindFacing).ToString("0") + "°; поза «" +
                                      (idle != null ? idle.name : "немає кліпу") + "»: стегна " + Yaw(thighs).ToString("0") +
                                      "°, центр мас " + Yaw(facing).ToString("0") + "° (гра доповертає на цей кут; знімок 1 — з цього боку)");
                    foreach (var r in modelGo.GetComponentsInChildren<Renderer>(false))
                    {
                        if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                        foreach (var m in r.sharedMaterials)
                        {
                            if (m == null) { report.AppendLine("   " + r.name + " | null"); continue; }
                            var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                            report.AppendLine("   " + r.name + " | " + m.name + " | tex=" + (tex ? tex.name : "НЕМАЄ") +
                                              " | clip=" + m.IsKeywordEnabled("_ALPHATEST_ON") +
                                              " | cull=" + (m.HasProperty("_Cull") ? m.GetFloat("_Cull").ToString("0") : "?") +
                                              " | smooth=" + (m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness").ToString("0.00") : "?"));
                        }
                    }
                    var b = BoneBounds(modelGo);
                    report.AppendLine("   межі (кістки): " + b.center.ToString("0.00") + " розмір " + b.size.ToString("0.00"));
                    var snap = Snapshot(modelGo);
                    Shoot(cam, b, facing, OutDir + "/" + look.Key + ".png");
                    Unsnap(modelGo, snap);

                    string gkey = look.Key.Substring(0, 1);
                    var walk = anims != null ? anims.For(CharacterAnimState.Walk, style) : null;
                    if (walk != null && motionDone.Add(gkey))
                    {
                        ShootMotion(cam, modelGo, walk, facing, OutDir + "/motion-" + look.Key + ".png", report);
                        foreach (var st in new[] { CharacterAnimState.Walk, CharacterAnimState.Run, CharacterAnimState.Sprint })
                        {
                            var clip = anims.For(st, style);
                            if (clip != null)
                            {
                                float sep;
                                float foot = MeasureStride(modelGo, clip, facing, out sep);
                                report.AppendLine("   природна швидкість «" + clip.name + "»: " + sep.ToString("0.00") +
                                                  " м/с за довжиною кроку (нога на землі: " + foot.ToString("0.00") + ")");
                            }
                        }
                    }
                    if (graph.IsValid()) graph.Destroy();
                    Object.DestroyImmediate(root);
                }
            }
            finally
            {
                if (urp != null) urp.useSRPBatcher = batcher;
            }
            if (anims != null && anims.IsComplete) AuditClips(lib, anims, looks[0].Value, report);
            if (urp != null) urp.useSRPBatcher = false;
            try { RenderBuildings(cam, report); }
            finally { if (urp != null) urp.useSRPBatcher = batcher; }
            File.WriteAllText(OutDir + "/report.txt", report.ToString(), new UTF8Encoding(false));
            Debug.Log("[Лукбук набору] " + looks.Count + " образів → " + OutDir);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>Видима зброя кожного стилю для бойових плівок (null — без зброї).</summary>
        private static readonly string[] BattleWeapons = { null, "wpn_sword", "wpn_axe", "wpn_spear", "wpn_bow", "wpn_musket" };

        /// <summary>Стани, кліп яких залежить від зброї, — плівка для кожної зброї.</summary>
        private static readonly CharacterAnimState[] StyleStates =
        {
            CharacterAnimState.CombatIdle, CharacterAnimState.Overwatch, CharacterAnimState.Attack, CharacterAnimState.Ability,
            CharacterAnimState.Reload
        };

        /// <summary>Стани з одним кліпом на всіх — одна плівка.</summary>
        private static readonly CharacterAnimState[] SharedStates =
        {
            CharacterAnimState.Run, CharacterAnimState.Hit, CharacterAnimState.HitHeavy, CharacterAnimState.Block,
            CharacterAnimState.Stunned, CharacterAnimState.Down, CharacterAnimState.GetUp, CharacterAnimState.Surrender,
            CharacterAnimState.CoverIdle, CharacterAnimState.Victory, CharacterAnimState.Social
        };

        /// <summary>
        /// Бойові кліпи в русі (власник 08.10.2026 на «не перевіряв, як анімації виглядають у русі»: «Це погано і так
        /// не повинно буть»): кожен стан бою × зброя, постать із цією зброєю в руці, вісім кадрів збоку й спереду —
        /// <c>Logs/kitlook/battle/&lt;стан&gt;-&lt;зброя&gt;.png</c> і числа в <c>battle.txt</c>. Меню
        /// <c>Alpha/Лукбук бойових кліпів</c> або <c>-executeMethod Game.Gameplay.EditorTools.KitLookbookRender.RunBattle</c>.
        /// </summary>
        [MenuItem("Alpha/Лукбук бойових кліпів")]
        public static void RunBattle()
        {
            ArtImportSettings.EnsureAnimationImport();
            ArtImportSettings.EnsureModelImport();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string dir = OutDir + "/battle";
            Directory.CreateDirectory(dir);
            var report = new StringBuilder();

            var libGo = CharacterKitBuilder.Build();
            var lib = libGo.GetComponent<CharacterKitLibrary>();
            var anims = libGo.GetComponent<CharacterAnimLibrary>();
            if (lib == null || !lib.IsComplete || anims == null)
            {
                File.WriteAllText(dir + "/battle.txt", "Набір неповний");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }
            var cam = SetupStage();

            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            bool batcher = urp != null && urp.useSRPBatcher;
            if (urp != null) urp.useSRPBatcher = false; // батч-режим: інакше всі матеріали одного кольору
            try
            {
                var look = AppearanceCatalog.DefaultProtagonist(Gender.Male).Clone();
                look.SignatureWeapon = "";
                bool first = true;
                foreach (var weapon in BattleWeapons)
                {
                    var plan = CharacterKitPlan.From(look, weapon != null ? new[] { weapon } : null);
                    var root = new GameObject("battle-look");
                    var model = CharacterAssembler.Build(lib, plan, root.transform, 0);
                    string wname = weapon ?? "unarmed";
                    if (model == null) { report.AppendLine("== " + wname + ": НЕ ЗІБРАНО"); Object.DestroyImmediate(root); continue; }
                    var style = AnimStateTable.StyleOf(KitFigure.WeaponOf(plan));
                    var g0 = Pose(model, anims.For(CharacterAnimState.CombatIdle, style), 0.2f);
                    var facing = CharacterAssembler.BodyFacing(model);
                    if (g0.IsValid()) g0.Destroy();
                    report.AppendLine("== " + wname + " (стиль " + style + ")");
                    var states = new List<CharacterAnimState>(StyleStates);
                    if (first) states.AddRange(SharedStates);
                    first = false;
                    foreach (var st in states)
                    {
                        var choice = AnimStateTable.For(st, style);
                        var clip = anims.For(st, style);
                        if (clip == null) { report.AppendLine(" " + st + " → «" + choice.Clip + "»: КЛІПУ НЕМАЄ"); continue; }
                        report.AppendLine(" " + st + " → " + clip.name + (choice.Loop ? " (петля)" : " (раз)"));
                        if (weapon != null)
                        {
                            GripReport(model, clip, facing, weapon, report);
                            // Той самий кліп на голому скелеті набору (джерело кліпів) — чи винне перенесення між тілами.
                            var kitGo = Object.Instantiate(lib.Kit(plan.KitId));
                            var kg = Pose(kitGo, anims.For(CharacterAnimState.CombatIdle, style), 0.2f);
                            var kitFacing = CharacterAssembler.BodyFacing(kitGo);
                            if (kg.IsValid()) kg.Destroy();
                            report.Append("   [скелет набору]");
                            GripReport(kitGo, clip, kitFacing, weapon, report);
                            Object.DestroyImmediate(kitGo);
                        }
                        ShootMotion(cam, model, clip, facing, dir + "/" + st + "-" + wname + ".png", report, 256, 320, !choice.Loop);
                    }
                    Object.DestroyImmediate(root);
                }
            }
            finally
            {
                if (urp != null) urp.useSRPBatcher = batcher;
            }
            File.WriteAllText(dir + "/battle.txt", report.ToString(), new UTF8Encoding(false));
            Debug.Log("[Лукбук бою] → " + dir);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>
        /// Хват зброї в середині кліпу: напрям передпліччя, великого пальця й самої зброї (найдовша вісь запеченої сітки)
        /// у рамці тіла — (вперед, вгору, ліворуч). Лук у прицілі має стояти вертикально (вісь ≈ вгору), рушниця й спис —
        /// дивитись уперед.
        /// </summary>
        private static void GripReport(GameObject model, AnimationClip clip, Vector3 facing, string weapon, StringBuilder report)
        {
            var g = Pose(model, clip, clip.length * 0.5f);
            var left = Vector3.Cross(Vector3.up, facing).normalized * -1f;
            string side = weapon == "wpn_bow" ? "l" : "r";
            Transform low = null, hand = null, thumb = null;
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "lowerarm_" + side) low = t;
                else if (t.name == "hand_" + side) hand = t;
                else if (t.name == "thumb_01_" + side) thumb = t;
            }
            string F(Vector3 v) { v.Normalize(); return "(" + Vector3.Dot(v, facing).ToString("0.00") + ", " + v.y.ToString("0.00") + ", " + Vector3.Dot(v, left).ToString("0.00") + ")"; }
            string axis = "?";
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (!smr.name.EndsWith(weapon)) continue;
                var mesh = new Mesh();
                smr.BakeMesh(mesh, true);
                var v = mesh.vertices;
                var m = smr.transform.localToWorldMatrix;
                Vector3 a = m.MultiplyPoint3x4(v[0]), b = a;
                float best = 0f;
                for (int i = 0; i < v.Length; i += 3)
                    for (int j = i + 1; j < v.Length; j += 3)
                    {
                        float d = (v[i] - v[j]).sqrMagnitude;
                        if (d > best) { best = d; a = m.MultiplyPoint3x4(v[i]); b = m.MultiplyPoint3x4(v[j]); }
                    }
                axis = F(b - a);
                Object.DestroyImmediate(mesh);
            }
            if (low != null && hand != null && thumb != null)
                report.AppendLine("   хват (вперед, вгору, ліворуч) у середині: передпліччя " + F(hand.position - low.position) +
                                  ", великий палець " + F(thumb.position - hand.position) + ", зброя " + axis);
            var pts = model.GetComponent<KitWeaponPoints>();
            if (pts != null && pts.HandL != null && (weapon == "wpn_bow" || weapon == "wpn_musket"))
            {
                // Найдальші точки сітки від кисті — кінці лука / дуло; формула з KitWeaponPoints має влучати в них.
                foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(false))
                {
                    if (!smr.name.EndsWith(weapon)) continue;
                    var mesh = new Mesh();
                    smr.BakeMesh(mesh, true);
                    var m = smr.transform.localToWorldMatrix;
                    var h = weapon == "wpn_bow" ? pts.HandL : pts.HandR;
                    var formula = weapon == "wpn_bow" ? new[] { h.TransformPoint(pts.BowTipA), h.TransformPoint(pts.BowTipB) }
                                                      : new[] { h.TransformPoint(pts.Muzzle) };
                    float worst = 0f;
                    foreach (var f in formula)
                    {
                        float best = float.MaxValue;
                        foreach (var v in mesh.vertices) best = Mathf.Min(best, (m.MultiplyPoint3x4(v) - f).magnitude);
                        worst = Mathf.Max(worst, best);
                    }
                    float scale = h.lossyScale.x;
                    report.AppendLine("   точки зброї (формула → найближча вершина сітки): " + (worst / scale * 100f).ToString("0") + " см");
                    Object.DestroyImmediate(mesh);
                }
            }
            if (g.IsValid()) g.Destroy();
        }

        /// <summary>Світло й камера лукбука (той самий вигляд, що в <see cref="Run"/>).</summary>
        private static Camera SetupStage()
        {
            var light = new GameObject("key").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.3f;
            light.transform.rotation = Quaternion.Euler(35f, 150f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.48f);
            var cam = new GameObject("cam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.32f, 0.33f, 0.36f);
            cam.fieldOfView = 24f;
            return cam;
        }

        /// <summary>
        /// Будівлі й реквізит треку V (<c>Assets/Art/Models</c>): кожна модель — п'ять стадій будівництва в ряд
        /// (через <see cref="VillageStage.ShowStageNodes"/>, ту саму логіку, що в грі) під кутом камери села.
        /// Без вузлів стадій — один кадр і рядок «стадій немає» (гра тоді масштабує модель по висоті).
        /// </summary>
        private static void RenderBuildings(Camera cam, StringBuilder report)
        {
            report.AppendLine();
            report.AppendLine("==== БУДІВЛІ Й РЕКВІЗИТ (Assets/Art/Models)");
            foreach (var path in Directory.GetFiles("Assets/Art/Models", "*.fbx"))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path.Replace('\\', '/'));
                if (prefab == null) continue;
                var go = Object.Instantiate(prefab);
                string id = Path.GetFileNameWithoutExtension(path);
                var stageNames = new List<string>();
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    if (t.name.Contains(".stage")) stageNames.Add(t.name);
                bool staged = VillageStage.ShowStageNodes(go.transform, 5);
                var b = Bounds(go);
                report.AppendLine("== " + id + " · розмір " + b.size.ToString("0.0") + " · стадії: " +
                                  (staged ? string.Join(", ", stageNames) : "немає"));
                int n = staged ? 5 : 1;
                var sheet = new Texture2D(Cell * n, Cell, TextureFormat.RGB24, false);
                var rt = new RenderTexture(Cell, Cell, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
                for (int k = 0; k < n; k++)
                {
                    if (staged) VillageStage.ShowStageNodes(go.transform, k + 1);
                    // Камера як у селі: ізометрія з півдня-сходу, фасад (−Z у моделі після повороту гри — тут +Z) у кадрі.
                    float size = Mathf.Max(b.size.x, b.size.y, b.size.z);
                    var dir = Quaternion.Euler(35f, 225f, 0f) * Vector3.back;
                    cam.transform.position = b.center + dir * size * 2.4f;
                    cam.transform.LookAt(b.center);
                    cam.targetTexture = rt;
                    Render(cam, rt);
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    sheet.ReadPixels(new Rect(0, 0, Cell, Cell), k * Cell, 0);
                    RenderTexture.active = prev;
                }
                sheet.Apply();
                File.WriteAllBytes(OutDir + "/bld-" + id + ".png", sheet.EncodeToPNG());
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(sheet);
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// Кожен кліп, який гра бере з таблиці станів (стан × стиль зброї): де опиняється таз відносно кореня, на
        /// якій висоті й куди дивиться тіло. Тур 07.10.2026: у бою Максима й героїню не було видно — бойові кліпи
        /// не перевіряв ніхто. «!!» — таз далі 0,5 м від кореня, нижче 0,3 м (крім падіння) чи тіло повернуте понад 60°.
        /// </summary>
        private static void AuditClips(CharacterKitLibrary lib, CharacterAnimLibrary anims, Appearance look, StringBuilder report)
        {
            report.AppendLine();
            report.AppendLine("==== КЛІПИ (стан × стиль → таз відносно кореня в середині кліпу)");
            var plan = CharacterKitPlan.From(look, null);
            var root = new GameObject("clip-audit");
            var model = CharacterAssembler.Build(lib, plan, root.transform, 0);
            if (model == null) { Object.DestroyImmediate(root); return; }
            Transform pelvis = null;
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) if (t.name == "pelvis") pelvis = t;
            var seen = new HashSet<string>();
            foreach (CharacterAnimState st in System.Enum.GetValues(typeof(CharacterAnimState)))
                foreach (WeaponStyle style in System.Enum.GetValues(typeof(WeaponStyle)))
                {
                    var clip = anims.For(st, style);
                    if (clip == null || !seen.Add(clip.name)) continue;
                    var issues = new List<string>();
                    foreach (float frac in new[] { 0.1f, 0.5f, 0.9f })
                    {
                        var g = Pose(model, clip, clip.length * frac);
                        var p = pelvis != null ? pelvis.position - model.transform.position : Vector3.zero;
                        float yaw = Yaw(CharacterAssembler.Facing(model));
                        bool down = st == CharacterAnimState.Down || st == CharacterAnimState.Sit || st == CharacterAnimState.GetUp ||
                                    st == CharacterAnimState.CoverIdle || st == CharacterAnimState.CoverMove || st == CharacterAnimState.Surrender;
                        string bad = (new Vector2(p.x, p.z).magnitude > 0.5f || (!down && p.y < 0.3f) || Mathf.Abs(yaw) > 60f) ? " !!" : "";
                        issues.Add(frac.ToString("0.0") + ": таз " + p.ToString("0.00") + ", тіло " + yaw.ToString("0") + "°" + bad);
                        if (g.IsValid()) g.Destroy();
                    }
                    report.AppendLine(st + "/" + style + " → " + clip.name + " | " + string.Join(" · ", issues));
                }
            Object.DestroyImmediate(root);
        }

        /// <summary>Поставити модель у позу кліпу на мить <paramref name="t"/> (граф плейблів, як у грі; без Play Mode).</summary>
        private static PlayableGraph Pose(GameObject model, AnimationClip clip, float t)
        {
            var animator = model.GetComponentInChildren<Animator>();
            if (clip == null || animator == null) return default(PlayableGraph);
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // знімок ховає скінені рендерери — без цього кістки «засинали»
            animator.Rebind(); // поза Play Mode аніматор не ініціалізований — граф інакше лишав бінд-позу
            var graph = PlayableGraph.Create("lookbook");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "look", animator);
            var play = AnimationClipPlayable.Create(graph, clip);
            output.SetSourcePlayable(play);
            play.SetTime(t);
            graph.Evaluate(0f);
            return graph;
        }

        /// <summary>
        /// Запекти поточну позу в статичні сітки й сховати скінені: поза Play Mode рендер показував бінд-позу,
        /// хоч кістки вже стояли в позі кліпу (лукбук 07.10.2026). Повертає корінь знімка — знищити після кадру.
        /// </summary>
        private static GameObject Snapshot(GameObject model)
        {
            var snap = new GameObject("snapshot");
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (!smr.enabled || !smr.gameObject.activeInHierarchy || smr.sharedMesh == null) continue;
                var mesh = new Mesh();
                smr.BakeMesh(mesh, true);
                var go = new GameObject(smr.name);
                go.transform.SetParent(snap.transform, false);
                go.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = smr.sharedMaterials;
                if (smr.HasPropertyBlock())
                {
                    var block = new MaterialPropertyBlock();
                    smr.GetPropertyBlock(block);
                    mr.SetPropertyBlock(block); // відтінок тканини й волосся — гра дає його блоком властивостей
                }
                smr.enabled = false;
            }
            return snap;
        }

        private static void Unsnap(GameObject model, GameObject snap)
        {
            foreach (var mf in snap.GetComponentsInChildren<MeshFilter>()) Object.DestroyImmediate(mf.sharedMesh);
            Object.DestroyImmediate(snap);
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(false)) smr.enabled = true;
        }

        /// <summary>Рамка за кістками (голова, ступні): межі скінених рендерерів поза Play Mode застарілі.</summary>
        private static Bounds BoneBounds(GameObject model)
        {
            Transform head = null, fl = null, fr = null;
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "head") head = t;
                else if (t.name == "foot_l") fl = t;
                else if (t.name == "foot_r") fr = t;
            }
            if (head == null || fl == null || fr == null) return Bounds(model);
            float feet = Mathf.Min(fl.position.y, fr.position.y) - 0.1f;
            float top = head.position.y + 0.3f;
            var c = (fl.position + fr.position) * 0.5f;
            return new Bounds(new Vector3(c.x, (top + feet) * 0.5f, c.z), new Vector3(1f, top - feet, 1f));
        }

        private static float Yaw(Vector3 v) => Vector3.SignedAngle(Vector3.forward, new Vector3(v.x, 0f, v.z), Vector3.up);

        /// <summary>
        /// Ворота G4: вісім кадрів циклу ходи збоку (верхній ряд) і спереду (нижній) в один аркуш + середній
        /// зсув кісток між сусідніми кадрами (0 — анімація стоїть, постать «замерзла»).
        /// </summary>
        private static void ShootMotion(Camera cam, GameObject model, AnimationClip walk, Vector3 facing, string path, StringBuilder report)
            => ShootMotion(cam, model, walk, facing, path, report, Cell / 2, Cell, false);

        /// <param name="toEnd">Одноразовий кліп: останній кадр — кінець кліпу (удар, падіння), а не «перед новим циклом».</param>
        private static void ShootMotion(Camera cam, GameObject model, AnimationClip walk, Vector3 facing, string path, StringBuilder report,
            int cw, int ch, bool toEnd)
        {
            const int frames = 8;
            var sheet = new Texture2D(cw * frames, ch * 2, TextureFormat.RGB24, false);
            var rt = new RenderTexture(cw, ch, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            var bones = model.GetComponentsInChildren<Transform>();
            Vector3[] prev = null;
            float moved = 0f;
            var side = Vector3.Cross(Vector3.up, facing).normalized;
            Transform pelvis = null;
            foreach (var t in bones) if (t.name == "pelvis") pelvis = t;
            var yaws = new StringBuilder();
            Vector3 hip0 = Vector3.zero;
            float drift = 0f;
            Bounds frame = default(Bounds);
            for (int i = 0; i < frames; i++)
            {
                var graph = Pose(model, walk, walk.length * i / (toEnd ? frames - 1 : frames));
                yaws.Append(Yaw(CharacterAssembler.Facing(model)).ToString("0")).Append("° ");
                if (pelvis != null)
                {
                    if (i == 0) hip0 = pelvis.position;
                    var d = pelvis.position - hip0; d.y = 0f;
                    drift = Mathf.Max(drift, d.magnitude);
                }
                var now = new Vector3[bones.Length];
                for (int k = 0; k < bones.Length; k++) now[k] = bones[k].position;
                if (prev != null)
                {
                    float sum = 0f;
                    for (int k = 0; k < bones.Length; k++) sum += (now[k] - prev[k]).magnitude;
                    moved += sum / bones.Length;
                }
                prev = now;
                if (i == 0) frame = BoneBounds(model); // одна рамка на всю серію: дрейф тіла видно на кадрах
                var b = frame;
                var snap = Snapshot(model);
                cam.targetTexture = rt;
                // Клітинка ширша за половину висоти (бойові плівки) — відступаємо, щоб улазила й ширина зброї.
                float dist = Mathf.Max(b.size.y, 1f) * 0.55f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) *
                             Mathf.Max(1f, cw * 2f / ch);
                for (int row = 0; row < 2; row++)
                {
                    var dir = row == 0 ? side : facing;
                    cam.transform.position = b.center + dir * dist;
                    cam.transform.rotation = Quaternion.LookRotation(-dir);
                    Render(cam, rt);
                    var prevRt = RenderTexture.active;
                    RenderTexture.active = rt;
                    sheet.ReadPixels(new Rect(0, 0, cw, ch), i * cw, (1 - row) * ch);
                    RenderTexture.active = prevRt;
                }
                Unsnap(model, snap);
                if (graph.IsValid()) graph.Destroy();
            }
            sheet.Apply();
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            report.AppendLine("   кліп «" + walk.name + "» (" + walk.length.ToString("0.00") + " с): середній зсув кісток між кадрами " +
                              (moved / (frames - 1)).ToString("0.000") + " (0 — анімація не грає) → " + Path.GetFileName(path));
            report.AppendLine("   напрям тіла (стегна) по кадрах: " + yaws + "· найбільший дрейф таза від кадру 0: " +
                              drift.ToString("0.00") + " м (кліп на місці — кілька см)");
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(sheet);
        }

        /// <summary>
        /// Швидкість, з якою кліп «іде» (м/с при масштабі 1): кліпи на місці, тож нога, що стоїть на землі,
        /// їде назад рівно з цією швидкістю. Медіана за кадрами — грі, щоб крутити кліп під справжній рух.
        /// </summary>
        private static float MeasureStride(GameObject model, AnimationClip clip, Vector3 facing, out float bySeparation)
        {
            bySeparation = 0f;
            Transform fl = null, fr = null;
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "foot_l") fl = t;
                else if (t.name == "foot_r") fr = t;
            }
            if (fl == null || fr == null) return 0f;
            const int n = 90;
            float dt = clip.length / n;
            var ls = new Vector3[n + 1];
            var rs = new Vector3[n + 1];
            float sep = 0f, ymin = float.MaxValue;
            for (int i = 0; i <= n; i++)
            {
                var g = Pose(model, clip, i * dt);
                ls[i] = fl.position; rs[i] = fr.position;
                sep = Mathf.Max(sep, Mathf.Abs(Vector3.Dot(ls[i] - rs[i], facing)));
                ymin = Mathf.Min(ymin, Mathf.Min(ls[i].y, rs[i].y));
                if (g.IsValid()) g.Destroy();
            }
            // Лише кадри опори: нога на висоті ≤ 3 см над найнижчою точкою (у бігу є фаза польоту, коли
            // «нижча» нога летить уперед — без цього біг виходив повільнішим за ходу).
            var speeds = new List<float>();
            for (int i = 1; i <= n; i++)
            {
                foreach (var pair in new[] { new[] { ls[i - 1], ls[i] }, new[] { rs[i - 1], rs[i] } })
                {
                    if (Mathf.Max(pair[0].y, pair[1].y) > ymin + 0.03f) continue;
                    speeds.Add(-Vector3.Dot(pair[1] - pair[0], facing) / dt);
                }
            }
            speeds.Sort();
            // Два кроки за цикл, крок ≈ найбільше розведення ступень уздовж руху — працює і для бігу з фазою польоту.
            bySeparation = 2f * sep / clip.length;
            return speeds.Count > 0 ? speeds[speeds.Count / 2] : 0f;
        }

        private static void Render(Camera cam, RenderTexture rt)
        {
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
            else cam.Render();
        }

        private static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(false);
            var b = new Bounds(go.transform.position, Vector3.one);
            bool any = false;
            foreach (var r in rs)
            {
                if (!r.gameObject.activeInHierarchy) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        /// <summary>Чотири боки відносно напрямку тіла: спереду, справа від постаті, ззаду, зліва — в один рядок.</summary>
        private static void Shoot(Camera cam, Bounds b, Vector3 facing, string path)
        {
            var sheet = new Texture2D(Cell * 4, Cell * 2, TextureFormat.RGB24, false);
            var rt = new RenderTexture(Cell, Cell * 2, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            cam.targetTexture = rt;
            float h = Mathf.Max(b.size.y, 1f);
            float dist = h * 0.55f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var right = Vector3.Cross(Vector3.up, facing).normalized;
            var dirs = new[] { facing, right, -facing, -right };
            for (int i = 0; i < 4; i++)
            {
                cam.transform.position = b.center + dirs[i] * dist;
                cam.transform.rotation = Quaternion.LookRotation(-dirs[i]);
                Render(cam, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                sheet.ReadPixels(new Rect(0, 0, Cell, Cell * 2), i * Cell, 0);
                RenderTexture.active = prev;
            }
            sheet.Apply();
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(sheet);
        }
    }
}
