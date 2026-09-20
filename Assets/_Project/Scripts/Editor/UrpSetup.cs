using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Editor
{
    /// <summary>
    /// Перевод проекта на URP одной командой, без кликов в редакторе.
    ///
    /// Зачем скриптом. Переключение пайплайна — это три ассета и две настройки
    /// проекта, которые обычно делаются мышью и потому нигде не записаны. Здесь
    /// они делаются кодом: результат попадает в git, а повторить его можно на
    /// чистой машине и в CI.
    ///
    /// Запуск из консоли:
    ///   Unity.exe -batchmode -quit -projectPath &lt;путь&gt; \
    ///             -executeMethod Game.Editor.UrpSetup.Apply -logFile -
    ///
    /// Операция идемпотентна: повторный запуск ничего не портит и не плодит
    /// вторых копий ассетов.
    /// </summary>
    public static class UrpSetup
    {
        private const string SettingsDir = "Assets/Settings";
        private const string RendererPath = SettingsDir + "/URP-Renderer.asset";
        private const string PipelinePath = SettingsDir + "/URP-Pipeline.asset";

        [MenuItem("Alpha/Перевести проект на URP")]
        public static void Apply()
        {
            if (!Directory.Exists(SettingsDir))
            {
                Directory.CreateDirectory(SettingsDir);
                AssetDatabase.Refresh();
            }

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
                Debug.Log("URP: создан рендерер " + RendererPath);
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
                Debug.Log("URP: создан ассет пайплайна " + PipelinePath);
            }

            AssetDatabase.SaveAssets();

            // Назначение в двух местах: Graphics Settings задаёт пайплайн по
            // умолчанию, Quality Settings — переопределение на уровень качества.
            // Если оставить только первое, уровень качества молча вернёт проект
            // на Built-in, и причина будет неочевидна.
            GraphicsSettings.defaultRenderPipeline = pipeline;

            int current = QualitySettings.GetQualityLevel();
            string[] levels = QualitySettings.names;
            for (int i = 0; i < levels.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(current, false);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var active = GraphicsSettings.currentRenderPipeline;
            Debug.Log("URP: активный пайплайн — " + (active != null ? active.name : "НЕТ (остался Built-in)"));
            Debug.Log("URP: уровней качества переведено — " + levels.Length);

            // Ассет глобальных настроек URP (шейдеры и ресурсы по умолчанию)
            // редактор заводит сам при назначении пайплайна. Проверяем и говорим
            // вслух: если его нет, материалы позже станут розовыми, и причину
            // будут искать в моделях, а не здесь.
            int globals = AssetDatabase.FindAssets("t:UniversalRenderPipelineGlobalSettings").Length;
            Debug.Log(globals > 0
                ? "URP: глобальные настройки на месте (" + globals + ")"
                : "URP: ВНИМАНИЕ — ассет глобальных настроек не создан; открой Project Settings → Graphics");
        }
    }
}
