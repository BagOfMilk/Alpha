using System.Collections.Generic;
using Game.Gameplay.Ring.Stage;
using UnityEditor;
using UnityEngine;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Прив'язує ассети 3D-рингу до об'єкта <see cref="RingStage3D"/> у сцені «Ворожнеча»: арена, тіла борців, текстури,
    /// кліпи, мікрофон. FBX/PNG лишаються в Assets/Art — у білд вони потрапляють лише через ці посилання. Не лінтується
    /// (AssetDatabase), як і решта збирачів сцени.
    /// </summary>
    public static class RingStageSceneSetup
    {
        private const string Art = "Assets/Art/Ring/";
        private const string Tex = Art + "Wrestlers/Textures/wrestler_";
        private const string MaterialPath = Art + "Materials/ring_wrestler.mat";

        public static void Attach(GameObject host)
        {
            var assets = host.AddComponent<RingStageAssets>();
            assets.venue = Load<GameObject>(Art + "venue_gym.fbx");
            assets.bodyPlayer = Load<GameObject>("Assets/Art/Characters/Kit/body_m_ukrainian.fbx");
            assets.bodyRival = Load<GameObject>("Assets/Art/Characters/Kit/body_m_nordic.fbx");
            assets.bodyRef = Load<GameObject>("Assets/Art/Characters/Kit/body_m_mediterranean.fbx");
            assets.mic = Load<GameObject>(Art + "Props/mic.fbx");
            assets.kitMale = Load<GameObject>("Assets/Art/Characters/Kit/kit_m.fbx");
            // Зала: стільці (props.fbx читається - сітки склеюються в рантаймі), натовп з анімаціями, тексти розстановки.
            assets.props = Load<GameObject>(Art + "Props/props.fbx");
            assets.crowd = Load<GameObject>("Assets/Art/Crowd/crowd_fans.fbx");
            assets.venueAnchors = Load<TextAsset>(Art + "venue_gym_anchors.txt");
            assets.venueProps = Load<TextAsset>(Art + "venue_gym_props.txt");
            assets.venueCrowd = Load<TextAsset>(Art + "venue_gym_crowd.txt");
            var crowdClips = new List<AnimationClip>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath("Assets/Art/Animations/CROWD_Anims.fbx"))
            {
                var c = o as AnimationClip;
                if (c != null && !c.name.StartsWith("__preview__")) crowdClips.Add(c);
                var av = o as Avatar;
                if (av != null && av.isHuman) assets.crowdAvatar = av;
            }
            assets.crowdClips = crowdClips.ToArray();

            string[] sets = { "body_m_ukrainian", "body_m_nordic", "ref_m_mediterranean" };
            for (int i = 0; i < 3; i++)
            {
                assets.albedo[i] = Load<Texture2D>(Tex + sets[i] + "_albedo.png");
                assets.normal[i] = Load<Texture2D>(Tex + sets[i] + "_normal.png");
                assets.mask[i] = Load<Texture2D>(Tex + sets[i] + "_mask.png");
            }

            var bodies = new List<GameObject>(); var ba = new List<Texture2D>(); var bn = new List<Texture2D>(); var bm = new List<Texture2D>();
            foreach (string sex in new[] { "m", "f" })
                foreach (string culture in new[] { "ukrainian", "nordic", "mediterranean", "latin", "west_african", "east_asian", "south_asian", "middle_eastern" })
                {
                    string id = "body_" + sex + "_" + culture;
                    var b = Load<GameObject>("Assets/Art/Characters/Kit/" + id + ".fbx");
                    if (b == null) continue;
                    bodies.Add(b);
                    ba.Add(Load<Texture2D>(Tex + id + "_albedo.png"));
                    bn.Add(Load<Texture2D>(Tex + id + "_normal.png"));
                    bm.Add(Load<Texture2D>(Tex + id + "_mask.png"));
                }
            assets.bodies = bodies.ToArray(); assets.bodyAlbedo = ba.ToArray(); assets.bodyNormal = bn.ToArray(); assets.bodyMask = bm.ToArray();
            assets.kitFemale = Load<GameObject>("Assets/Art/Characters/Kit/kit_f.fbx");

            assets.wrestlerTemplate = EnsureMaterial();
            assets.screenTemplate = EnsureScreenMaterial();
            var clips = new List<AnimationClip>();
            foreach (string fbx in new[] { "Assets/Art/Animations/RING_Moves.fbx", "Assets/Art/Animations/RING_Promo.fbx" })
            {
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbx))
                {
                    var c = o as AnimationClip;
                    if (c != null && !c.name.StartsWith("__preview__")) clips.Add(c);
                }
            }
            assets.clips = clips.ToArray();
            assets.manifest = Load<TextAsset>("Assets/Art/Animations/ring_anims_manifest.txt");
            assets.promoManifest = Load<TextAsset>("Assets/Art/Animations/ring_promo_manifest.txt");

            var stage = host.AddComponent<RingStage3D>();
            stage.assets = assets;
            Debug.Log("[Ринг] 3D: ассети прив'язано, кліпів " + clips.Count);
        }

        private static T Load<T>(string path) where T : Object
        {
            var o = AssetDatabase.LoadAssetAtPath<T>(path);
            if (o == null) Debug.LogError("[Ринг] 3D: немає ассета " + path);
            return o;
        }

        /// <summary>
        /// Матеріал екрана (титантрон): URP/Lit з увімкненою емісією — щоб варіант шейдера з емісією потрапив у білд
        /// (матеріали, створені в рантаймі, варіантів не тягнуть).
        /// </summary>
        private static Material EnsureScreenMaterial()
        {
            const string path = "Assets/Art/Ring/Materials/ring_screen.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Art/Ring/Materials")) AssetDatabase.CreateFolder("Assets/Art/Ring", "Materials");
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.white);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material EnsureMaterial()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (m != null) return m;
            if (!AssetDatabase.IsValidFolder("Assets/Art/Ring/Materials")) AssetDatabase.CreateFolder("Assets/Art/Ring", "Materials");
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, MaterialPath);
            return m;
        }
    }
}
