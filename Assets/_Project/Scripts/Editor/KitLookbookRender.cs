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
                    var plan = CharacterKitPlan.From(look.Value, null);
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
                    var b = Bounds(modelGo);
                    report.AppendLine("   межі: " + b.center.ToString("0.00") + " розмір " + b.size.ToString("0.00"));
                    Shoot(cam, b, facing, OutDir + "/" + look.Key + ".png");

                    string gkey = look.Key.Substring(0, 1);
                    var walk = anims != null ? anims.For(CharacterAnimState.Walk, style) : null;
                    if (walk != null && motionDone.Add(gkey))
                        ShootMotion(cam, modelGo, walk, facing, OutDir + "/motion-" + look.Key + ".png", report);
                    if (graph.IsValid()) graph.Destroy();
                    Object.DestroyImmediate(root);
                }
            }
            finally
            {
                if (urp != null) urp.useSRPBatcher = batcher;
            }
            if (urp != null) urp.useSRPBatcher = false;
            try { RenderBuildings(cam, report); }
            finally { if (urp != null) urp.useSRPBatcher = batcher; }
            File.WriteAllText(OutDir + "/report.txt", report.ToString(), new UTF8Encoding(false));
            Debug.Log("[Лукбук набору] " + looks.Count + " образів → " + OutDir);
            if (Application.isBatchMode) EditorApplication.Exit(0);
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

        /// <summary>Поставити модель у позу кліпу на мить <paramref name="t"/> (граф плейблів, як у грі; без Play Mode).</summary>
        private static PlayableGraph Pose(GameObject model, AnimationClip clip, float t)
        {
            var animator = model.GetComponentInChildren<Animator>();
            if (clip == null || animator == null) return default(PlayableGraph);
            animator.applyRootMotion = false;
            var graph = PlayableGraph.Create("lookbook");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "look", animator);
            var play = AnimationClipPlayable.Create(graph, clip);
            output.SetSourcePlayable(play);
            play.SetTime(t);
            graph.Evaluate(0f);
            return graph;
        }

        private static float Yaw(Vector3 v) => Vector3.SignedAngle(Vector3.forward, new Vector3(v.x, 0f, v.z), Vector3.up);

        /// <summary>
        /// Ворота G4: вісім кадрів циклу ходи збоку (верхній ряд) і спереду (нижній) в один аркуш + середній
        /// зсув кісток між сусідніми кадрами (0 — анімація стоїть, постать «замерзла»).
        /// </summary>
        private static void ShootMotion(Camera cam, GameObject model, AnimationClip walk, Vector3 facing, string path, StringBuilder report)
        {
            const int frames = 8;
            int cw = Cell / 2;
            var sheet = new Texture2D(cw * frames, Cell * 2, TextureFormat.RGB24, false);
            var rt = new RenderTexture(cw, Cell, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            var bones = model.GetComponentsInChildren<Transform>();
            Vector3[] prev = null;
            float moved = 0f;
            var side = Vector3.Cross(Vector3.up, facing).normalized;
            for (int i = 0; i < frames; i++)
            {
                var graph = Pose(model, walk, walk.length * i / frames);
                var now = new Vector3[bones.Length];
                for (int k = 0; k < bones.Length; k++) now[k] = bones[k].position;
                if (prev != null)
                {
                    float sum = 0f;
                    for (int k = 0; k < bones.Length; k++) sum += (now[k] - prev[k]).magnitude;
                    moved += sum / bones.Length;
                }
                prev = now;
                var b = Bounds(model);
                cam.targetTexture = rt;
                float dist = Mathf.Max(b.size.y, 1f) * 0.55f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                for (int row = 0; row < 2; row++)
                {
                    var dir = row == 0 ? side : facing;
                    cam.transform.position = b.center + dir * dist;
                    cam.transform.rotation = Quaternion.LookRotation(-dir);
                    Render(cam, rt);
                    var prevRt = RenderTexture.active;
                    RenderTexture.active = rt;
                    sheet.ReadPixels(new Rect(0, 0, cw, Cell), i * cw, (1 - row) * Cell);
                    RenderTexture.active = prevRt;
                }
                if (graph.IsValid()) graph.Destroy();
            }
            sheet.Apply();
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            report.AppendLine("   хода «" + walk.name + "» (" + walk.length.ToString("0.00") + " с): середній зсув кісток між кадрами " +
                              (moved / (frames - 1)).ToString("0.000") + " (0 — анімація не грає) → " + Path.GetFileName(path));
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(sheet);
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
