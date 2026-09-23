using System;

// ЗАГЛУШКА UnityEngine — ТОЛЬКО для проверки компиляции обёрток вне редактора.
//
// Зачем: Game.Gameplay зависит от движка и потому не попадает в headless-тесты.
// Но большинство ошибок в обёртках — это НЕ поведение движка, а обычные ошибки
// C#: опечатка в имени, забытый using, обращение к internal-члену ядра. Такие
// ошибки ловятся компилятором, и глупо ждать ради них запуска Unity.
//
// ЧЕГО ЭТА ЗАГЛУШКА НЕ ДЕЛАЕТ: она не проверяет поведение. Сцены, инспектор,
// сериализация, жизненный цикл MonoBehaviour — только настоящий редактор.
// Зелёный линт означает «код собирается», а не «код работает».
//
// Файл лежит вне Assets/, поэтому Unity его не видит и конфликта имён нет.
namespace UnityEngine
{
    public class Object
    {
        public string name;
    }

    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject { return null; }
    }

    public class Component : Object { }

    public class Behaviour : Component { }

    public class MonoBehaviour : Behaviour { }

    public class GameObject : Object
    {
        public GameObject() { }
        public GameObject(string name) { }
        public T AddComponent<T>() where T : Component { return null; }
        public void SetActive(bool value) { }
    }

    public static class Application
    {
        public static bool isBatchMode { get { return false; } }
        public static string dataPath { get { return "."; } }
    }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
        public static void LogError(object message) { }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CreateAssetMenuAttribute : Attribute
    {
        public string fileName;
        public string menuName;
        public int order;
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class HeaderAttribute : Attribute
    {
        public HeaderAttribute(string header) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class TooltipAttribute : Attribute
    {
        public TooltipAttribute(string tooltip) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class MinAttribute : Attribute
    {
        public MinAttribute(float min) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class RangeAttribute : Attribute
    {
        public RangeAttribute(float min, float max) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ContextMenuAttribute : Attribute
    {
        public ContextMenuAttribute(string name) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeFieldAttribute : Attribute { }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public string path { get { return null; } }
        public string name { get { return null; } }
        public bool isDirty { get { return false; } }
        public int rootCount { get { return 0; } }
        public bool IsValid() { return false; }
    }

    public static class SceneManager
    {
        public static Scene GetActiveScene() { return default(Scene); }
        public static void MoveGameObjectToScene(GameObject go, Scene scene) { }
    }
}

// ЗАГЛУШКА UnityEditor. Та же оговорка, что и выше, но острее: редакторный
// код целиком состоит из вызовов Unity API, а сигнатуры этих вызовов я
// объявляю здесь сам. Значит, линт подтверждает согласованность моего кода
// с МОЕЙ ЖЕ заглушкой, а не с настоящим редактором. Он ловит опечатки,
// забытые using и ошибки типов — но не поймает, если я неверно помню API.
// Поверхность держим узкой намеренно: чем меньше вызовов, тем меньше риск.
namespace UnityEditor
{
    public sealed class MenuItemAttribute : System.Attribute
    {
        public MenuItemAttribute(string itemName) { }
        public MenuItemAttribute(string itemName, bool isValidateFunction) { }
    }

    public sealed class InitializeOnLoadAttribute : System.Attribute { }

    public static class EditorApplication
    {
        public static void Exit(int code) { }
        public delegate void CallbackFunction();
        public static CallbackFunction delayCall;
    }

    public static class AssetDatabase
    {
        public static void ImportAsset(string path) { }
        public static string[] FindAssets(string filter) { return new string[0]; }
        public static void Refresh() { }
        public static void SaveAssets() { }
        public static void CreateAsset(UnityEngine.Object asset, string path) { }
        public static T LoadAssetAtPath<T>(string path) where T : UnityEngine.Object { return null; }
        public static bool IsValidFolder(string path) { return false; }
        public static string CreateFolder(string parentFolder, string newFolderName) { return null; }
    }

    public static class EditorUtility
    {
        public static void SetDirty(UnityEngine.Object target) { }
    }

    public static class EditorPrefs
    {
        public static bool GetBool(string key, bool defaultValue) { return false; }
        public static void SetBool(string key, bool value) { }
    }

    public sealed class EditorBuildSettingsScene
    {
        public EditorBuildSettingsScene(string path, bool enabled) { }
        public string path;
        public bool enabled;
    }

    public static class EditorBuildSettings
    {
        public static EditorBuildSettingsScene[] scenes;
    }
}

namespace UnityEditor.SceneManagement
{
    public enum NewSceneSetup { EmptyScene = 0, DefaultGameObjects = 1 }
    public enum NewSceneMode { Single = 0, Additive = 1 }
    public enum OpenSceneMode { Single = 0, Additive = 1, AdditiveWithoutLoading = 2 }

    public static class EditorSceneManager
    {
        public static void MarkSceneDirty(UnityEngine.SceneManagement.Scene scene) { }
        public static UnityEngine.SceneManagement.Scene NewScene(NewSceneSetup setup, NewSceneMode mode)
        {
            return default(UnityEngine.SceneManagement.Scene);
        }

        public static bool SaveScene(UnityEngine.SceneManagement.Scene scene, string dstScenePath) { return true; }
        public static bool CloseScene(UnityEngine.SceneManagement.Scene scene, bool removeScene) { return true; }

        public static UnityEngine.SceneManagement.Scene OpenScene(string scenePath, OpenSceneMode mode)
        {
            return default(UnityEngine.SceneManagement.Scene);
        }
    }
}

// ---------------------------------------------------------------------------
// Заглушки графического слоя: ровно столько, сколько нужно скрипту перевода
// проекта на URP. Линт ловит опечатки и забытые using без редактора, но НЕ
// проверяет, что настоящий API именно такой — это подтверждается прогоном
// Unity в batch-режиме, а не этими строками.
// ---------------------------------------------------------------------------
namespace UnityEngine
{
    public static class QualitySettings
    {
        public static string[] names { get { return new string[0]; } }
        public static int GetQualityLevel() { return 0; }
        public static void SetQualityLevel(int index, bool applyExpensiveChanges) { }
        public static UnityEngine.Rendering.RenderPipelineAsset renderPipeline { get; set; }
    }
}

namespace UnityEngine.Rendering
{
    public class RenderPipelineAsset : ScriptableObject { }

    public static class GraphicsSettings
    {
        public static RenderPipelineAsset defaultRenderPipeline { get; set; }
        public static RenderPipelineAsset currentRenderPipeline { get { return null; } }
    }
}

namespace UnityEngine.Rendering.Universal
{
    public class ScriptableRendererData : ScriptableObject { }

    public class UniversalRendererData : ScriptableRendererData { }

    public class UniversalRenderPipelineAsset : RenderPipelineAsset
    {
        public static UniversalRenderPipelineAsset Create(ScriptableRendererData data) { return null; }
    }
}

// ---------------------------------------------------------------------------
// IMGUI и рантайм-мелочи: нужны интерпретатору сцены (ScenePlayer).
//
// Заглушка ПРОВЕРЯЕТ СБОРКУ, а не рисует: ни одна из этих функций ничего не
// делает. Поведение видно только в редакторе — здесь ловятся опечатки,
// несуществующие поля и перепутанные типы.
// ---------------------------------------------------------------------------
namespace UnityEngine
{
    public struct Rect
    {
        public float x, y, width, height;

        public Rect(float x, float y, float width, float height)
        {
            this.x = x; this.y = y; this.width = width; this.height = height;
        }
    }

    public class Texture2D : Object { }

    public static class Screen
    {
        public static int width { get { return 1280; } }
        public static int height { get { return 720; } }
    }

    public static class Time
    {
        public static float deltaTime { get { return 0f; } }
    }

    public enum KeyCode { None = 0, Space = 32 }

    public static class Input
    {
        public static bool GetKeyDown(KeyCode key) { return false; }
        public static bool GetMouseButtonDown(int button) { return false; }
    }

    public static class Resources
    {
        public static T Load<T>(string path) where T : Object { return null; }
    }

    public enum TextAnchor { UpperLeft = 0, MiddleCenter = 4 }

    public enum ScaleMode { StretchToFill = 0, ScaleAndCrop = 1, ScaleToFit = 2 }

    public class GUIContent
    {
        public static readonly GUIContent none = new GUIContent();
    }

    public class GUIStyle
    {
        public GUIStyle() { }
        public GUIStyle(GUIStyle other) { }
        public TextAnchor alignment { get; set; }
    }

    public class GUISkin
    {
        public GUIStyle label = new GUIStyle();
        public GUIStyle box = new GUIStyle();
    }

    public static class GUI
    {
        public static GUISkin skin = new GUISkin();

        public static void Box(Rect position, GUIContent content) { }
        public static void Box(Rect position, string text) { }
        public static void Label(Rect position, string text) { }
        public static void Label(Rect position, string text, GUIStyle style) { }
        public static void DrawTexture(Rect position, Texture2D image, ScaleMode scaleMode) { }
    }
}

// ---------------------------------------------------------------------------
// Сборка билда: нужна Builder.cs. Заглушка ничего не собирает — она проверяет,
// что код сборки компилируется, а не запускает Unity.
// ---------------------------------------------------------------------------
namespace UnityEditor
{
    public enum BuildTarget { StandaloneWindows64 = 19 }

    public enum BuildTargetGroup { Standalone = 1 }

    [System.Flags]
    public enum BuildOptions { None = 0, Development = 1 }

    public struct BuildPlayerOptions
    {
        public string[] scenes;
        public string locationPathName;
        public BuildTarget target;
        public BuildTargetGroup targetGroup;
        public BuildOptions options;
    }

    public static class BuildPipeline
    {
        public static Build.Reporting.BuildReport BuildPlayer(BuildPlayerOptions options)
        {
            return new Build.Reporting.BuildReport();
        }
    }
}

namespace UnityEditor.Build.Reporting
{
    public enum BuildResult { Unknown = 0, Succeeded = 1, Failed = 2 }

    public class BuildSummary
    {
        public BuildResult result;
        public int totalErrors;
        public ulong totalSize;
        public System.TimeSpan totalTime;
        public string outputPath;
    }

    public class BuildReport
    {
        public BuildSummary summary = new BuildSummary();
    }
}

namespace UnityEngine
{
    public enum ColorSpace { Uninitialized = -1, Gamma = 0, Linear = 1 }
}

namespace UnityEditor
{
    public static class PlayerSettings
    {
        public static UnityEngine.ColorSpace colorSpace { get; set; }
    }
}
