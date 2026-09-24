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
        public static string persistentDataPath { get { return "."; } }
        public static bool runInBackground { get; set; }
        public static void Quit(int exitCode) { }

        /// <summary>Реальна сигнатура — <c>public static event Func&lt;bool&gt; wantsToQuit</c> (GameShell.cs, фікс краху при виході).</summary>
        public static event Func<bool> wantsToQuit;
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

    // Минимум, нужный IMGUI-раскладке (скролл-позиция, разброс не нужен —
    // никакого Random, инвариант 1 держится и на уровне заглушки).
    public struct Vector2
    {
        public float x, y;
        public static readonly Vector2 zero = new Vector2(0f, 0f);

        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }

    // r/g/b/a 0..1, как в настоящем Unity; сравнений и арифметики нет —
    // палитра AlphaSkin складывается из готовых констант, а не вычислением.
    public struct Color
    {
        public float r, g, b, a;

        public Color(float r, float g, float b, float a = 1f)
        {
            this.r = r; this.g = g; this.b = b; this.a = a;
        }
    }

    // Байтовая палитра (0..255) — так тёплые тёмные тона читаются в коде
    // числами, привычными для RGB, а не дробями. Неявное преобразование —
    // как в настоящем Unity, поэтому Color32 можно передать всюду, где
    // ожидается Color, без явного каста на каждой константе.
    public struct Color32
    {
        public byte r, g, b, a;

        public Color32(byte r, byte g, byte b, byte a)
        {
            this.r = r; this.g = g; this.b = b; this.a = a;
        }

        public static implicit operator Color(Color32 c)
        {
            return new Color(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);
        }

        public static implicit operator Color32(Color c)
        {
            return new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)(c.a * 255f));
        }
    }

    public enum FilterMode { Point = 0, Bilinear = 1, Trilinear = 2 }
    public enum TextureWrapMode { Repeat = 0, Clamp = 1, Mirror = 2, MirrorOnce = 3 }

    /// <summary>
    /// Только то, что нужно AlphaSkin: сплошная текстура-заливка 2×2 под фон
    /// стиля. Настоящий Texture2D умеет заметно больше (форматы, мип-уровни,
    /// сжатие) — сюда это не попало намеренно, заливка одним цветом этого не
    /// требует.
    /// </summary>
    public class Texture2D : Object
    {
        public FilterMode filterMode { get; set; }
        public TextureWrapMode wrapMode { get; set; }

        public Texture2D(int width, int height) { }

        public void SetPixels(Color[] colors) { }
        public void SetPixel(int x, int y, Color color) { }
        public void Apply() { }
    }

    /// <summary>Заглушка шрифта: реальный экземпляр отдаёт Resources.GetBuiltinResource, здесь он просто ссылка.</summary>
    public class Font : Object { }

    public static class Screen
    {
        public static int width { get { return 1280; } }
        public static int height { get { return 720; } }
    }

    public static class Time
    {
        public static float deltaTime { get { return 0f; } }
    }

    public enum KeyCode { None = 0, Escape = 27, Space = 32 }

    public static class Input
    {
        public static bool GetKeyDown(KeyCode key) { return false; }
        public static bool GetMouseButtonDown(int button) { return false; }
    }

    public static class Resources
    {
        public static T Load<T>(string path) where T : Object { return null; }

        /// <summary>Встроенный шрифт Unity ("LegacyRuntime.ttf") рендерит кириллицу без единого загруженного файла.</summary>
        public static T GetBuiltinResource<T>(string path) where T : Object { return null; }
    }

    /// <summary>Скриншоты автопрогона: заглушка ничего не пишет на диск, только проверяет сигнатуру вызова.</summary>
    public static class ScreenCapture
    {
        public static void CaptureScreenshot(string filename) { }
        public static void CaptureScreenshot(string filename, int superSize) { }
    }

    public enum TextAnchor { UpperLeft = 0, MiddleCenter = 4 }

    public enum ScaleMode { StretchToFill = 0, ScaleAndCrop = 1, ScaleToFit = 2 }

    public enum FontStyle { Normal = 0, Bold = 1, Italic = 2, BoldAndItalic = 3 }

    public class GUIContent
    {
        public string text;
        public static readonly GUIContent none = new GUIContent();

        public GUIContent() { }
        public GUIContent(string text) { this.text = text; }
    }

    /// <summary>Три состояния оформления, которые реально различает AlphaSkin: покой/наведение/нажатие.</summary>
    public class GUIStyleState
    {
        public Texture2D background;
        public Color textColor;
    }

    /// <summary>Отступы стиля. Настоящий RectOffset хранит их свойствами — здесь поля, семантика вызова та же.</summary>
    public class RectOffset
    {
        public int left, right, top, bottom;

        public RectOffset() { }

        public RectOffset(int left, int right, int top, int bottom)
        {
            this.left = left; this.right = right; this.top = top; this.bottom = bottom;
        }
    }

    public class GUIStyle
    {
        public GUIStyle() { }
        public GUIStyle(GUIStyle other) { }
        public TextAnchor alignment { get; set; }
        public FontStyle fontStyle { get; set; }
        public int fontSize { get; set; }
        public bool wordWrap { get; set; }
        public bool richText { get; set; }
        public GUIStyleState normal { get; set; } = new GUIStyleState();
        public GUIStyleState hover { get; set; } = new GUIStyleState();
        public GUIStyleState active { get; set; } = new GUIStyleState();
        public RectOffset padding { get; set; } = new RectOffset();
        public RectOffset margin { get; set; } = new RectOffset();
        public float fixedWidth { get; set; }
        public float fixedHeight { get; set; }

        /// <summary>
        /// Реальний Unity міряє текст шрифтом стилю; заглушка не рендерить
        /// нічого (§клас, шапка файлу) — довжина рядка чесно наближає лише
        /// ПОРЯДОК величини, досить для компіляції коду, що загортає ряди
        /// (BattleHudScreen.DrawInitiativeStrip), поведінку перевіряє Unity.
        /// </summary>
        public Vector2 CalcSize(GUIContent content)
        {
            int len = content?.text?.Length ?? 0;
            return new Vector2(len * fontSize * 0.6f, fontSize + 4f);
        }
    }

    /// <summary>
    /// Настоящий GUISkin — ScriptableObject (US-18-подобная связь для
    /// CreateInstance&lt;T&gt;); без этого наследования AlphaSkin.Build() не
    /// собрался бы даже здесь, в заглушке.
    /// </summary>
    public class GUISkin : ScriptableObject
    {
        public Font font;
        public GUIStyle label = new GUIStyle();
        public GUIStyle box = new GUIStyle();
        public GUIStyle button = new GUIStyle();
        public GUIStyle window = new GUIStyle();
        public GUIStyle textField = new GUIStyle();
        public GUIStyle horizontalScrollbar = new GUIStyle();
        public GUIStyle horizontalScrollbarThumb = new GUIStyle();
        public GUIStyle verticalScrollbar = new GUIStyle();
        public GUIStyle verticalScrollbarThumb = new GUIStyle();
    }

    public static class GUI
    {
        public static GUISkin skin = new GUISkin();
        public static Color color = new Color(1f, 1f, 1f, 1f);
        public static Color backgroundColor = new Color(1f, 1f, 1f, 1f);
        public static bool enabled = true;

        public static void Box(Rect position, GUIContent content) { }
        public static void Box(Rect position, string text) { }
        public static void Label(Rect position, string text) { }
        public static void Label(Rect position, string text, GUIStyle style) { }
        public static void DrawTexture(Rect position, Texture2D image, ScaleMode scaleMode) { }
    }

    /// <summary>Opaque-маркер параметра раскладки — как в настоящем Unity, создаётся только через GUILayout.*.</summary>
    public sealed class GUILayoutOption
    {
        internal GUILayoutOption() { }
    }

    /// <summary>
    /// Часть IMGUI-раскладки, которой пользуются Widgets/AlphaSkin/GameShell.
    /// Ни один метод не рисует — заглушка только проверяет, что вызовы
    /// собираются с теми же именами и типами аргументов, что настоящий API.
    /// </summary>
    public static class GUILayout
    {
        public static void Label(string text, params GUILayoutOption[] options) { }
        public static void Label(string text, GUIStyle style, params GUILayoutOption[] options) { }
        public static bool Button(string text, params GUILayoutOption[] options) { return false; }
        public static bool Button(string text, GUIStyle style, params GUILayoutOption[] options) { return false; }
        public static void Box(string text, params GUILayoutOption[] options) { }
        public static void Box(string text, GUIStyle style, params GUILayoutOption[] options) { }
        public static void BeginHorizontal(params GUILayoutOption[] options) { }
        public static void BeginHorizontal(GUIStyle style, params GUILayoutOption[] options) { }
        public static void EndHorizontal() { }
        public static void BeginVertical(params GUILayoutOption[] options) { }
        public static void BeginVertical(GUIStyle style, params GUILayoutOption[] options) { }
        public static void EndVertical() { }
        public static Vector2 BeginScrollView(Vector2 scrollPosition, params GUILayoutOption[] options) { return scrollPosition; }
        public static void EndScrollView() { }
        public static void BeginArea(Rect screenRect) { }
        public static void EndArea() { }
        public static void Space(float pixels) { }
        public static void FlexibleSpace() { }
        public static string TextField(string text, params GUILayoutOption[] options) { return text; }
        public static bool Toggle(bool value, string text, params GUILayoutOption[] options) { return value; }
        public static GUILayoutOption Width(float width) { return new GUILayoutOption(); }
        public static GUILayoutOption Height(float height) { return new GUILayoutOption(); }
        public static GUILayoutOption ExpandWidth(bool expand) { return new GUILayoutOption(); }
        public static GUILayoutOption ExpandHeight(bool expand) { return new GUILayoutOption(); }
    }

    /// <summary>
    /// SceneScreen.cs (фікс-ревью, блокер) кличе GetLastRect одразу після
    /// Widgets.Panel, щоб дізнатись, де насправді розклалась діалогова
    /// панель, і прикріпити до неї портрет замість фіксованої позиції
    /// посеред екрана. Заглушка — нульовий Rect завжди: справжня поведінка
    /// (Layout=нуль, Repaint=реальний рект) перевіряється тільки в Unity.
    /// </summary>
    public static class GUILayoutUtility
    {
        public static Rect GetLastRect() { return new Rect(0f, 0f, 0f, 0f); }
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

// ==== E1b: Event/EventType (фікс-ревью — одноразове визначення фізичного
// натискання під час конкретного OnGUI-проходу, замість Input.GetKeyDown/
// GetMouseButtonDown, які лишаються true в УСІХ проходах кадру: SceneScreen,
// Widgets.Modal, GameShell). Поля, не властивості — так само, як у
// справжньому UnityEngine.Event. ====
namespace UnityEngine
{
    public enum EventType
    {
        Ignore = 0, Used = 1,
        MouseDown = 2, MouseUp = 3, MouseMove = 4, MouseDrag = 5,
        KeyDown = 6, KeyUp = 7, ScrollWheel = 8,
        Repaint = 9, Layout = 10,
        DragUpdated = 11, DragPerform = 12, DragExited = 13,
        ValidateCommand = 14, ExecuteCommand = 15, ContextClick = 16,
        MouseEnterWindow = 17, MouseLeaveWindow = 18
    }

    public class Event
    {
        public static Event current;

        public EventType type;
        public KeyCode keyCode;
        public int button;

        public void Use() { type = EventType.Used; }
    }
}

namespace UnityEditor
{
    public static class PlayerSettings
    {
        public static UnityEngine.ColorSpace colorSpace { get; set; }
    }
}
