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
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Лукбук набору персонажів у редакторі (ворота G3, docs/PLAYTEST.md): збирає постаті так само, як гра
    /// (<see cref="CharacterKitPlan"/> → <see cref="CharacterAssembler"/>), рендерить кожну з чотирьох боків в
    /// один PNG і пише звіт: які частини ввімкнено, матеріал, шейдер, текстура, альфа-зріз, відсікання граней.
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
            try
            {
                foreach (var look in looks)
                {
                    var plan = CharacterKitPlan.From(look.Value, null);
                    var root = new GameObject("look");
                    var modelGo = CharacterAssembler.Build(lib, plan, root.transform, 0);
                    report.AppendLine("== " + look.Key + " · " + plan.Signature());
                    if (modelGo == null) { report.AppendLine("   НЕ ЗІБРАНО"); Object.DestroyImmediate(root); continue; }
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
                    Shoot(cam, b, OutDir + "/" + look.Key + ".png");
                    Object.DestroyImmediate(root);
                }
            }
            finally
            {
                if (urp != null) urp.useSRPBatcher = batcher;
            }
            File.WriteAllText(OutDir + "/report.txt", report.ToString(), new UTF8Encoding(false));
            Debug.Log("[Лукбук набору] " + looks.Count + " образів → " + OutDir);
            if (Application.isBatchMode) EditorApplication.Exit(0);
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

        /// <summary>Чотири боки (з +Z, +X, −Z, −X) в один рядок — щоб не залежати від того, куди «дивиться» бінд-поза.</summary>
        private static void Shoot(Camera cam, Bounds b, string path)
        {
            var sheet = new Texture2D(Cell * 4, Cell * 2, TextureFormat.RGB24, false);
            var rt = new RenderTexture(Cell, Cell * 2, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            cam.targetTexture = rt;
            float h = Mathf.Max(b.size.y, 1f);
            float dist = h * 0.55f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var dirs = new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
            for (int i = 0; i < 4; i++)
            {
                cam.transform.position = b.center + dirs[i] * dist;
                cam.transform.rotation = Quaternion.LookRotation(-dirs[i]);
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
                else cam.Render();
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
