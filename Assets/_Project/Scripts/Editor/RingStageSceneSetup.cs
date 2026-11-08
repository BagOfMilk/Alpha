using System.Collections.Generic;
using System.Linq;
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

            const string Snd = "Assets/ThirdParty/CC0/Audio/Ring/";
            assets.musicTitle = Load<AudioClip>(Snd + "music_rock_theme_loop.wav");
            assets.musicHero = Load<AudioClip>(Snd + "music_rock_city_ransom.ogg");
            assets.musicHammer = Load<AudioClip>(Snd + "music_hammer_of_the_berserker.ogg");
            assets.musicRoster = Load<AudioClip>(Snd + "music_downtown_destruction.ogg");
            assets.sndCrowd = Load<AudioClip>(Snd + "crowd_shouting.ogg");
            assets.sndApplause = Load<AudioClip>(Snd + "applause_hall.wav");
            assets.sndBell = Load<AudioClip>(Snd + "bell_01.ogg");
            assets.sndBellEnd = Load<AudioClip>(Snd + "bell_02.ogg");
            assets.sndGong = Load<AudioClip>(Snd + "gong_01.ogg");
            assets.sndMatSlap = Load<AudioClip>(Snd + "mat_slap.mp3");
            assets.sndLand = Load<AudioClip>(Snd + "land.wav");
            assets.sndHit = new[] { "hit_01", "hit_02", "hit_03", "hit_04" }.Select(n => Load<AudioClip>(Snd + n + ".ogg")).ToArray();
            assets.sndSlam = new[] { "slam_01", "slam_02", "slam_03", "slam_04", "slam_05" }.Select(n => Load<AudioClip>(Snd + n + ".ogg")).ToArray();

            assets.wrestlerTemplate = EnsureMaterial();
            assets.screenTemplate = EnsureScreenMaterial();
            assets.particleAdd = EnsureParticleMaterial("ring_particle_add", true);
            assets.particleAlpha = EnsureParticleMaterial("ring_particle_alpha", false);
            var clips = new List<AnimationClip>();
            foreach (string fbx in new[] { "Assets/Art/Animations/RING_Moves.fbx", "Assets/Art/Animations/RING_Promo.fbx", "Assets/Art/Animations/RING_Entrance.fbx" })
            {
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbx))
                {
                    var c = o as AnimationClip;
                    if (c != null && !c.name.StartsWith("__preview__")) clips.Add(c);
                }
            }
            assets.clips = clips.ToArray();
            // Жіночий набір виходу — окремим масивом: ті самі імена, що в RING_Entrance.fbx (ENTRANCE_PROFILE.md).
            var clipsF = new List<AnimationClip>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath("Assets/Art/Animations/RING_Entrance_F.fbx"))
            {
                var c = o as AnimationClip;
                if (c != null && !c.name.StartsWith("__preview__")) clipsF.Add(c);
            }
            assets.clipsFemale = clipsF.ToArray();
            // Екран виходу (ТЗ B07): пули й маски мотивів лишаються в теці директора (генератор пише туди) — у білд їх
            // тягнуть ці посилання; печатка — з бренду.
            const string Tron = Art + "Titantron/";
            assets.tronPools = AssetDatabase.FindAssets("t:TextAsset", new[] { Tron + "pools" })
                .Select(g => AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(t => t != null).OrderBy(t => t.name).ToArray();
            assets.tronSprites = AssetDatabase.FindAssets("t:Texture2D", new[] { Tron + "sprites" })
                .Select(g => AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(t => t != null).OrderBy(t => t.name).ToArray();
            assets.tronSeal = Load<Texture2D>(Art + "Brand/emblem_seal_1024.png");
            assets.beltProp = Load<GameObject>(Art + "Cast/Props/belt.fbx");
            assets.beltAlbedo = Load<Texture2D>(Art + "Cast/Props/Textures/belt_albedo.png");
            assets.beltNormal = Load<Texture2D>(Art + "Cast/Props/Textures/belt_normal.png");
            Debug.Log("[Ринг] 3D: виходи — жіночих кліпів " + clipsF.Count + ", пулів екрана " + assets.tronPools.Length + ", мотивів " + assets.tronSprites.Length);
            assets.manifest =Load<TextAsset>("Assets/Art/Animations/ring_anims_manifest.txt");
            assets.promoManifest = Load<TextAsset>("Assets/Art/Animations/ring_promo_manifest.txt");
            assets.entranceManifest = Load<TextAsset>("Assets/Art/Animations/ring_entrance_manifest.txt");
            assets.events = Load<TextAsset>("Assets/Art/Animations/ring_anims_events.txt");
            assets.eventsF = Load<TextAsset>("Assets/Art/Animations/ring_anims_f_events.txt");
            assets.motion = Load<TextAsset>("Assets/Art/Animations/ring_anims_motion.txt");

            // Меню (ТЗ B08): три варіації лейтмотиву й десять звуків одного тембру (власна генерація tools/ring_audio),
            // печатка й канатна рамка для лого-локапу. Програвач (RingMenuAudio) сам бере ці ассети в Awake.
            var menu = host.AddComponent<RingMenuAssets>();
            const string MenuSnd = Art + "Audio/Menu/";
            menu.musicLogin = Load<AudioClip>(MenuSnd + "music_login.wav");
            menu.musicLoading = Load<AudioClip>(MenuSnd + "music_loading.wav");
            menu.musicSettings = Load<AudioClip>(MenuSnd + "music_settings.wav");
            menu.sfx = new[] { "ui_move", "ui_confirm", "ui_back", "ui_error", "ui_toggle", "ui_slider", "ui_tab", "ui_transition", "ui_load_done", "ui_login_ok" }
                .Select(n => Load<AudioClip>(MenuSnd + n + ".wav")).ToArray();
            menu.seal = Load<Texture2D>(Art + "Brand/emblem_seal_1024.png");
            menu.ropeFrame = Load<Texture2D>(Art + "Brand/rope_frame_1040.png");
            host.AddComponent<RingMenuAudio>();

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

        /// <summary>
        /// Матеріал частинок шоу (URP Particles/Unlit, прозорий): адитивний для іскор і піро, звичайний для CO2 й конфеті.
        /// Ассет — щоб шейдер частинок потрапив у білд (матеріали з рантайму варіантів не тягнуть).
        /// </summary>
        private static Material EnsureParticleMaterial(string name, bool additive)
        {
            string path = "Assets/Art/Ring/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Art/Ring/Materials")) AssetDatabase.CreateFolder("Assets/Art/Ring", "Materials");
                m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetColor("_BaseColor", Color.white);
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
