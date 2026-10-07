using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Світло й постобробка треку V2 (Поправка №18, <c>docs/ART_BIBLE.md</c>): глобальний Volume
    /// (ACES-тонмапінг, м'яка кольорокорекція, легкий блум, віньєтка), SSAO на рендерері, чотири каскади
    /// тіней і м'які тіні в асеті пайплайна, постобробка на камерах. Скриптом і ідемпотентно — як
    /// <see cref="UrpSetup"/>; кличе <c>GameSceneBuilder.Build</c>. Колір і яскравість доби лишаються за
    /// <c>VillageView</c> (Volume не перекриває денний цикл: експозиція й баланс білого — нейтральні).
    /// </summary>
    public static class LightingSetup
    {
        private const string ProfilePath = "Assets/Settings/AlphaVolume.asset";
        private const string RendererPath = "Assets/Settings/URP-Renderer.asset";
        private const string PipelinePath = "Assets/Settings/URP-Pipeline.asset";

        public static void Apply(params Camera[] cameras)
        {
            var profile = EnsureProfile();
            var volumeGo = new GameObject("Світло: Volume");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.sharedProfile = profile;

            foreach (var cam in cameras)
            {
                if (cam == null) continue;
                var data = cam.GetComponent<UniversalAdditionalCameraData>();
                if (data == null) data = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            }

            ConfigurePipeline();
            EnsureSsao();
        }

        private static VolumeProfile EnsureProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            var tone = Get<Tonemapping>(profile);
            tone.mode.Override(TonemappingMode.ACES);

            var color = Get<ColorAdjustments>(profile);
            color.postExposure.Override(0.15f);
            color.contrast.Override(12f);
            color.saturation.Override(-6f); // приглушена палітра «реалістичного» вигляду (ART_BIBLE)

            var bloom = Get<Bloom>(profile);
            bloom.threshold.Override(1.1f);
            bloom.intensity.Override(0.35f);
            bloom.scatter.Override(0.6f);

            var vignette = Get<Vignette>(profile);
            vignette.intensity.Override(0.22f);
            vignette.smoothness.Override(0.45f);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        private static T Get<T>(VolumeProfile profile) where T : VolumeComponent
        {
            T c;
            if (!profile.TryGet(out c))
            {
                c = profile.Add<T>(true);
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, profile);
            }
            c.active = true;
            return c;
        }

        private static void ConfigurePipeline()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null) return;
            pipeline.shadowCascadeCount = 4;
            pipeline.shadowDistance = 45f; // село ~25 од. завширшки + запас під ізометрію
            pipeline.supportsHDR = true;
            pipeline.msaaSampleCount = 4;
            var so = new SerializedObject(pipeline);
            var soft = so.FindProperty("m_SoftShadowsSupported");
            if (soft != null) soft.boolValue = true;
            var res = so.FindProperty("m_MainLightShadowmapResolution");
            if (res != null) res.intValue = 2048;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);
        }

        /// <summary>SSAO — фіча рендерера; її тип у URP 17 не публічний, тож створюємо за іменем.</summary>
        private static void EnsureSsao()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererPath);
            if (renderer == null) return;
            foreach (var f in renderer.rendererFeatures)
                if (f != null && f.GetType().Name == "ScreenSpaceAmbientOcclusion") return;

            var type = Type.GetType("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion, Unity.RenderPipelines.Universal.Runtime");
            if (type == null) { Debug.LogWarning("[Світло] немає типу SSAO у цій версії URP — пропускаю."); return; }
            var feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(type);
            feature.name = "SSAO";
            AssetDatabase.AddObjectToAsset(feature, renderer);

            var so = new SerializedObject(renderer);
            var list = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
            long localId;
            string guid;
            if (map != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out guid, out localId))
            {
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(renderer);
            AssetDatabase.SaveAssets();
        }
    }
}
