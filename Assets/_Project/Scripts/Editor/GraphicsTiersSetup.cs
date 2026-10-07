using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Три рівні графіки зі СВОЇМИ асетами URP (Поправка №21.1, Статут PERF-01). Раніше всі шість
    /// рівнів якості вели на один асет пайплайна, і «найнижча» якість для URP нічого не полегшувала:
    /// HDR, MSAA, тіні, SSAO і постобробка задаються саме в асетах пайплайна й рендерера.
    ///
    /// Низька — вбудована відеокарта рівня UHD 620: без HDR, без MSAA, рендер 0,8, тіні 1024 одним
    /// каскадом, рендерер без SSAO і без постобробки. Середня — повна роздільність, жорсткі тіні
    /// двома каскадами, постобробка без SSAO. Висока — вигляд треку V як є (<see cref="LightingSetup"/>
    /// налаштовує <c>URP-Pipeline.asset</c> і <c>URP-Renderer.asset</c>).
    ///
    /// Ідемпотентно, як <see cref="UrpSetup"/>; кличе <c>GameSceneBuilder.Build</c> після світла.
    /// Індекси рівнів — ті самі, що в <c>Game.Gameplay.GraphicsTier</c> (0 — Низька, 2 — Висока).
    /// </summary>
    public static class GraphicsTiersSetup
    {
        private const string LowPipeline = "Assets/Settings/URP-Pipeline-Low.asset";
        private const string MediumPipeline = "Assets/Settings/URP-Pipeline-Medium.asset";
        private const string HighPipeline = "Assets/Settings/URP-Pipeline.asset";
        private const string LowRenderer = "Assets/Settings/URP-Renderer-Low.asset";
        private const string MediumRenderer = "Assets/Settings/URP-Renderer-Medium.asset";
        private const string HighRenderer = "Assets/Settings/URP-Renderer.asset";
        private const string DefaultPostProcessData =
            "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset";

        [MenuItem("Alpha/Рівні графіки (Поправка №21)")]
        public static void Apply()
        {
            var postData = AssetDatabase.LoadAssetAtPath<ScriptableObject>(DefaultPostProcessData);
            if (postData == null)
                Debug.LogWarning("[Графіка] не знайшов PostProcessData за " + DefaultPostProcessData +
                                 " — постобробку середнього й високого рівня перевірити руками.");

            var lowRenderer = EnsureRenderer(LowRenderer, null);
            var mediumRenderer = EnsureRenderer(MediumRenderer, postData);
            var highRenderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(HighRenderer);
            if (highRenderer != null) SetPostProcess(highRenderer, postData);

            var low = EnsurePipeline(LowPipeline, lowRenderer);
            Configure(low, hdr: false, msaa: 1, scale: 0.8f, shadowDistance: 25f, cascades: 1, soft: false, shadowRes: 1024);
            var medium = EnsurePipeline(MediumPipeline, mediumRenderer);
            Configure(medium, hdr: true, msaa: 1, scale: 1f, shadowDistance: 35f, cascades: 2, soft: false, shadowRes: 2048);
            var high = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(HighPipeline);

            int current = QualitySettings.GetQualityLevel();
            var levels = QualitySettings.names;
            for (int i = 0; i < levels.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = i == 0 ? low : (i == 1 ? medium : high);
                QualitySettings.vSyncCount = 1; // жодного рівня без стелі кадрів — ноутбук не гріється даремно
            }
            QualitySettings.SetQualityLevel(current, false);

            AssetDatabase.SaveAssets();
            Debug.Log("[Графіка] рівні: " + string.Join(", ", levels) + " → Low/Medium/High-асети URP призначено.");
        }

        private static UniversalRendererData EnsureRenderer(string path, ScriptableObject postData)
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, path);
            }
            SetPostProcess(renderer, postData);
            return renderer;
        }

        /// <summary>Постобробка рендерера вмикається наявністю PostProcessData; null — вимкнено.</summary>
        private static void SetPostProcess(UniversalRendererData renderer, ScriptableObject postData)
        {
            var so = new SerializedObject(renderer);
            var prop = so.FindProperty("postProcessData");
            if (prop == null) return;
            prop.objectReferenceValue = postData;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(renderer);
        }

        private static UniversalRenderPipelineAsset EnsurePipeline(string path, UniversalRendererData renderer)
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, path);
            }
            return pipeline;
        }

        private static void Configure(UniversalRenderPipelineAsset pipeline, bool hdr, int msaa, float scale,
            float shadowDistance, int cascades, bool soft, int shadowRes)
        {
            pipeline.supportsHDR = hdr;
            pipeline.msaaSampleCount = msaa;
            pipeline.renderScale = scale;
            pipeline.shadowDistance = shadowDistance;
            pipeline.shadowCascadeCount = cascades;

            var so = new SerializedObject(pipeline);
            var softProp = so.FindProperty("m_SoftShadowsSupported");
            if (softProp != null) softProp.boolValue = soft;
            var res = so.FindProperty("m_MainLightShadowmapResolution");
            if (res != null) res.intValue = shadowRes;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);
        }
    }
}
