// Перевірка імпорту моделей «Рингу» як Humanoid у чистому проєкті. Запускається батчем, у гру не входить.
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class CheckAvatar
{
    // Кліпи на моделі: кістки після Humanoid-перенесення. Стопа має стояти на підлозі, голова — на своїй висоті.
    static void SampleClips(GameObject prefab, string path, StringBuilder sb)
    {
        var female = path.Contains("komet") || path.Contains("vdova") || path.Contains("novachka") || path.Contains("lina");
        var anim = AssetDatabase.LoadAllAssetsAtPath("Assets/Cast/Anim/RING_Cast_" + (female ? "F" : "M") + ".fbx").OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToDictionary(c => c.name.Substring(c.name.LastIndexOf('|') + 1), c => c);
        sb.AppendLine("    кліпів у наборі: " + anim.Count + ", приклад: " + anim.Keys.First());
        var go = Object.Instantiate(prefab);
        var an = go.GetComponent<Animator>();
        if (an == null) an = go.AddComponent<Animator>();
        an.avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().First();
        foreach (var name in new[] { "U_Idle_Loop", "U_Walk_Loop", "Ring_Stance_Loop", "Kneel_Idle_Loop", "Sit_Mat_Idle_Loop", "Lying_Back_Loop", "belt_hold", "chair_idle_Loop" })
        {
            if (!anim.TryGetValue(name, out var c)) { sb.AppendLine("    " + name + ": НЕМАЄ"); continue; }
            c.SampleAnimation(go, 0.4f);
            Transform B(HumanBodyBones b) { return an.GetBoneTransform(b); }
            sb.AppendLine("    " + name + ": голова " + B(HumanBodyBones.Head).position.y.ToString("0.00") + ", таз " + B(HumanBodyBones.Hips).position.y.ToString("0.00")
                + ", стопи " + B(HumanBodyBones.LeftFoot).position.y.ToString("0.00") + "/" + B(HumanBodyBones.RightFoot).position.y.ToString("0.00")
                + ", кисті " + B(HumanBodyBones.LeftHand).position.y.ToString("0.00") + "/" + B(HumanBodyBones.RightHand).position.y.ToString("0.00"));
            if (name == "U_Idle_Loop")
            {
                var smr = go.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(x => x.sharedMesh.vertexCount).First();
                var baked = new Mesh();
                smr.BakeMesh(baked, true);
                var vs = baked.vertices;
                var ys = vs.Select(v => smr.transform.TransformPoint(v).y).ToArray();
                sb.AppendLine("    вершини в цій позі, Y: від " + ys.Min().ToString("0.000") + " до " + ys.Max().ToString("0.000") + " м");
            }
        }
        Object.DestroyImmediate(go);
    }

    public static void Run()
    {
        var sb = new StringBuilder();
        Application.logMessageReceived += (m, st, t) => { if (t != LogType.Log) sb.AppendLine("  [" + t + "] " + m.Split('\n')[0]); };
        var paths = AssetDatabase.FindAssets("t:Model").Select(AssetDatabase.GUIDToAssetPath).Where(p => p.ToLowerInvariant().EndsWith(".fbx")).OrderBy(p => p).ToArray();
        foreach (var path in paths)
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.importBlendShapes = false;
            imp.SaveAndReimport();
            var objs = AssetDatabase.LoadAllAssetsAtPath(path);
            var avatar = objs.OfType<Avatar>().FirstOrDefault();
            var clips = objs.OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
            var meshes = objs.OfType<Mesh>().ToArray();
            int humanBones = imp.humanDescription.human != null ? imp.humanDescription.human.Length : 0;
            sb.AppendLine(path);
            sb.AppendLine("  avatar: " + (avatar == null ? "НЕМАЄ" : ("isValid=" + avatar.isValid + " isHuman=" + avatar.isHuman)) + " humanBones=" + humanBones
                          + " clips=" + clips.Length + " humanClips=" + clips.Count(c => c.isHumanMotion) + " meshes=" + meshes.Length + " tris=" + meshes.Sum(m => m.triangles.Length / 3));
            if (imp.humanDescription.human != null && humanBones > 0)
            {
                var need = new[] { "Hips", "Spine", "Chest", "UpperChest", "Neck", "Head", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand", "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot" };
                var have = imp.humanDescription.human.Select(h => h.humanName).ToArray();
                var miss = need.Where(n => !have.Contains(n)).ToArray();
                if (miss.Length > 0) sb.AppendLine("  бракує кісток Humanoid: " + string.Join(", ", miss));
                var map = imp.humanDescription.human.Where(h => h.humanName == "Hips" || h.humanName == "LeftHand" || h.humanName == "RightFoot").Select(h => h.humanName + "=" + h.boneName);
                sb.AppendLine("  " + string.Join(", ", map));
            }
            if (avatar != null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    var body = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(x => x.sharedMesh.vertexCount).FirstOrDefault();
                    if (body != null) sb.AppendLine("  сітка тіла: висота " + body.sharedMesh.bounds.size.y.ToString("0.000") + " м, низ " + body.sharedMesh.bounds.min.y.ToString("0.000") + ", вершин " + body.sharedMesh.vertexCount + ", кісток " + body.bones.Length);
                    sb.AppendLine("  вісь кореня: " + prefab.transform.up + ", масштаб кореня: " + prefab.transform.localScale);
                    if (clips.Length == 0) SampleClips(prefab, path, sb);
                    var sockets = prefab.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Socket_")).Select(t => t.name + " під " + t.parent.name);
                    foreach (var s in sockets) sb.AppendLine("  сокет: " + s);
                }
            }
        }
        File.WriteAllText(System.Environment.GetEnvironmentVariable("RING_REPORT") ?? "avatar_report.txt", sb.ToString());
        EditorApplication.Exit(0);
    }
}
