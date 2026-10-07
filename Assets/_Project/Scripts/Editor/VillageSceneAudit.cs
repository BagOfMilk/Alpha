using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Огляд зібраної сцени гри (ворота G3/G5, docs/PLAYTEST.md): відкриває <c>Assets/Scenes/Game.unity</c> і
    /// пише кожен видимий об'єкт села — назву, модель, розмір і чи не лежить він на боці (висота менша за
    /// третину найбільшого виміру при великій довжині), плюс знімок села згори й під кутом гри. Лукбук набору
    /// бере FBX напряму й не бачить того, що робить збирач сцени (обгортки, повороти) — «брили» в турі 07.10.2026.
    /// Меню <c>Alpha/Огляд сцени села</c> або <c>-executeMethod Game.Gameplay.EditorTools.VillageSceneAudit.Run</c>;
    /// результат — <c>Logs/village/</c>.
    /// </summary>
    public static class VillageSceneAudit
    {
        private const string OutDir = "Logs/village";

        [MenuItem("Alpha/Огляд сцени села")]
        public static void Run()
        {
            Directory.CreateDirectory(OutDir);
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Game.unity", OpenSceneMode.Single);
            var report = new StringBuilder();
            var roots = new Dictionary<GameObject, Bounds>();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!r.enabled) continue;
                var owner = Owner(r.transform);
                Bounds b;
                if (roots.TryGetValue(owner, out b)) { b.Encapsulate(r.bounds); roots[owner] = b; }
                else roots[owner] = r.bounds;
            }
            foreach (var pair in roots.OrderBy(p => Path(p.Key.transform)))
            {
                var s = pair.Value.size;
                float big = Mathf.Max(s.x, s.z);
                string flag = big > 1.2f && s.y < big * 0.2f && !Path(pair.Key.transform).ToLowerInvariant().Contains("ground") ? "  ← ПЛАСКЕ?" : "";
                var mf = pair.Key.GetComponentInChildren<MeshFilter>();
                report.AppendLine(Path(pair.Key.transform) + " | " + (mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "-") +
                                  " | центр " + pair.Value.center.ToString("0.0") + " | розмір " + s.ToString("0.00") + flag);
            }
            File.WriteAllText(OutDir + "/scene.txt", report.ToString(), new UTF8Encoding(false));

            var cams = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var hub = cams.FirstOrDefault(c => c.orthographic) ?? cams.FirstOrDefault();
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            bool batcher = urp != null && urp.useSRPBatcher;
            if (urp != null) urp.useSRPBatcher = false;
            try
            {
                var camGo = new GameObject("audit-cam");
                var cam = camGo.AddComponent<Camera>();
                cam.orthographic = true;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.3f, 0.32f, 0.35f);
                cam.nearClipPlane = 0.1f; cam.farClipPlane = 200f;
                var centre = new Vector3(0f, 0f, 0f);
                cam.orthographicSize = 12f;
                Shoot(cam, centre + new Vector3(0f, 60f, 0f), Quaternion.Euler(90f, 0f, 0f), OutDir + "/top.png");
                if (hub != null)
                    Shoot(cam, hub.transform.position, hub.transform.rotation, OutDir + "/game-angle.png", 9f);
                Object.DestroyImmediate(camGo);
            }
            finally
            {
                if (urp != null) urp.useSRPBatcher = batcher;
            }
            Debug.Log("[Огляд села] " + roots.Count + " об'єктів → " + OutDir);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        // Власник рендерера в звіті — найближчий предок, що є коренем моделі (FBX-примірник) або дитиною групи сцени.
        private static GameObject Owner(Transform t)
        {
            var root = PrefabUtility.GetOutermostPrefabInstanceRoot(t.gameObject);
            if (root != null) return root;
            while (t.parent != null && t.parent.parent != null && t.parent.parent.parent != null) t = t.parent;
            return t.gameObject;
        }

        private static string Path(Transform t)
        {
            var parts = new List<string>();
            for (; t != null; t = t.parent) parts.Insert(0, t.name);
            return string.Join("/", parts);
        }

        private static void Shoot(Camera cam, Vector3 pos, Quaternion rot, string path, float size = 12f)
        {
            cam.orthographicSize = size;
            cam.transform.SetPositionAndRotation(pos, rot);
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            cam.targetTexture = rt;
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
            else cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }
    }
}
