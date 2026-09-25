using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Editor
{
    /// <summary>
    /// Переведення проекту на URP однією командою, без кліків у редакторі.
    ///
    /// Навіщо скриптом. Перемикання пайплайна — це три ассети і дві настройки
    /// проекту, які зазвичай робляться мишею і тому ніде не записані. Тут
    /// вони робляться кодом: результат потрапляє в git, а повторити його можна на
    /// чистій машині і в CI.
    ///
    /// Запуск із консолі:
    ///   Unity.exe -batchmode -quit -projectPath &lt;шлях&gt; \
    ///             -executeMethod Game.Editor.UrpSetup.Apply -logFile -
    ///
    /// Операція ідемпотентна: повторний запуск нічого не псує і не плодить
    /// других копій ассетів.
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

            // Лінійний колірний простір — не смакова примха, а вимога URP:
            // у гамі його формули освітлення рахують неправильно, і сцена йде в
            // перенасичену бірюзу (перевірено знімком 23.09.2026). Перемикання
            // змушує редактор переімпортувати текстури і шейдери — це
            // довго один раз і безкоштовно потім.
            if (PlayerSettings.colorSpace != ColorSpace.Linear)
            {
                PlayerSettings.colorSpace = ColorSpace.Linear;
                Debug.Log("URP: цветовое пространство переключено на линейное");
            }

            // Призначення у двох місцях: Graphics Settings задає пайплайн за
            // замовчуванням, Quality Settings — перевизначення на рівень якості.
            // Якщо лишити тільки перше, рівень якості мовчки поверне проект
            // на Built-in, і причина буде неочевидна.
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

            // Ассет глобальних настройок URP (шейдери і ресурси за замовчуванням)
            // редактор заводить сам при призначенні пайплайна. Перевіряємо і кажемо
            // вголос: якщо його немає, матеріали пізніше стануть рожевими, і причину
            // будуть шукати в моделях, а не тут.
            int globals = AssetDatabase.FindAssets("t:UniversalRenderPipelineGlobalSettings").Length;
            Debug.Log(globals > 0
                ? "URP: глобальные настройки на месте (" + globals + ")"
                : "URP: ВНИМАНИЕ — ассет глобальных настроек не создан; открой Project Settings → Graphics");
        }
    }
}
