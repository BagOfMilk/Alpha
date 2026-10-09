using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Game.Core.Session;
using Game.Gameplay.UI;
using Game.Gameplay.UI.Toolkit;
using Game.Gameplay.Visual;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Game.Gameplay.Playtest
{
    /// <summary>
    /// Плейтест власника (<c>docs/PLAYTEST.md</c>). Вмикається аргументом <c>-playtest</c>:
    /// <list type="bullet">
    /// <item>F8 — нотатка: спершу кадр (до того як з'явиться вікно), потім коротке вікно на UI Toolkit — тип
    /// (Баг / Вигляд / Баланс / UX / Ідея) і текст; Enter — зберегти, Esc — скасувати. Стан, доба, місце,
    /// погода, графіка, FPS, останні події й зліпок гри додаються самі;</item>
    /// <item>помилки й винятки рушія записуються без нотатки — однакові одним рядком з лічильником;</item>
    /// <item><c>-lowcpu</c>: 30 кадрів/с, без vsync, рендер через кадр на стоячих екранах, 5 кадрів/с поза фокусом
    /// — щоб гра не гріла ПК, поки тестер пише.</item>
    /// </list>
    /// Усе — у <c>Playtest/&lt;дата_час&gt;/</c> поруч з exe: <c>notes.md</c> (таблиця), <c>NNN.jpg</c> (960 px),
    /// <c>NNN.txt</c> (контекст), <c>NNN.save.txt</c> (зліпок), <c>errors.md</c>. Форматування — <see cref="PlaytestLog"/>.
    /// </summary>
    public sealed class PlaytestReporter : MonoBehaviour
    {
        public const string PlaytestFlag = "-playtest";
        public const string LowCpuFlag = "-lowcpu";
        private const int ShotWidth = 960;

        private GameShell _shell;
        private string _dir;
        private int _index;
        private bool _lowCpu;
        private float _fps = 60f;

        private readonly Dictionary<string, int> _errors = new Dictionary<string, int>();
        private readonly Dictionary<string, string> _errorStacks = new Dictionary<string, string>();
        private bool _errorsDirty;
        private StreamWriter _log;
        private float _errorsFlushAt;

        // Вікно нотатки.
        private GameObject _host;
        private PanelSettings _panel;
        private VisualElement _window;
        private TextField _text;
        private Label _status;
        private readonly List<Button> _categoryButtons = new List<Button>();
        private PlaytestCategory _category = PlaytestCategory.Bug;
        private byte[] _pendingShot;
        private string _pendingSave;
        private PlaytestContext _pendingCtx;
        private float _statusUntil;

        private static bool IsOpen { get => PlaytestLog.NoteOpen; set => PlaytestLog.NoteOpen = value; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!HasArg(PlaytestFlag)) return;
            var go = new GameObject("Плейтест");
            DontDestroyOnLoad(go);
            go.AddComponent<PlaytestReporter>();
        }

        private static bool HasArg(string flag)
        {
            foreach (var a in Environment.GetCommandLineArgs())
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private void Awake()
        {
            var started = DateTime.Now;
            string root = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Playtest");
            _dir = Path.Combine(root, PlaytestLog.SessionFolderName(started));
            Directory.CreateDirectory(_dir);
            string system = SystemInfo.operatingSystem + "; " + SystemInfo.processorType + "; " + SystemInfo.graphicsDeviceName
                            + "; RAM " + SystemInfo.systemMemorySize + " МБ";
            File.WriteAllText(Path.Combine(_dir, "notes.md"),
                PlaytestLog.SessionHeader(ReadCommit(), started, system, Screen.width + "×" + Screen.height));
            // Повний журнал без буфера: Player.log губить усе після ~4 КБ, бо гра виходить через
            // TerminateProcess (HardExit) і буфер Unity не встигає на диск.
            try { _log = new StreamWriter(Path.Combine(_dir, "log.txt"), false, new System.Text.UTF8Encoding(false)) { AutoFlush = true }; }
            catch (Exception) { _log = null; }
            Application.logMessageReceived += OnLog;

            _lowCpu = HasArg(LowCpuFlag);
            if (_lowCpu)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 30;
                // Лише на цей запуск: збережений вибір гравця не чіпаємо (раніше «-lowcpu» лишав Низьку назавжди).
                if (GraphicsTier.Current != GraphicsLevel.Low) GraphicsTier.UseForSession(GraphicsLevel.Low);
            }
            BuildWindow();
            Debug.Log("[Плейтест] нотатки — F8; тека: " + _dir);
        }

        private static string ReadCommit()
        {
            try
            {
                string f = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "build_commit.txt");
                return File.Exists(f) ? File.ReadAllText(f).Trim() : null;
            }
            catch (Exception) { return null; }
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            FlushErrors();
            if (_log != null) { _log.Dispose(); _log = null; }
            if (_host != null) Destroy(_host);
            if (_panel != null) Destroy(_panel);
        }

        private void OnApplicationQuit() => FlushErrors();

        private void OnApplicationFocus(bool focused)
        {
            if (!_lowCpu) return;
            Application.targetFrameRate = focused ? 30 : 5; // поза фокусом гра майже не гріє ПК
        }

        // ============================ кожен кадр ============================

        private void Update()
        {
            _fps = Mathf.Lerp(_fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 0.001f), 0.05f);
            if (_shell == null) _shell = FindAnyObjectByType<GameShell>();

            if (!IsOpen && Input.GetKeyDown(KeyCode.F8)) StartCoroutine(OpenNote());
            if (IsOpen)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) CloseNote();
                else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) SaveNote();
            }

            if (_lowCpu)
            {
                // Стоячі екрани (меню, сцени, вікна) — рендер через кадр; прогулянка й бій — щокадру.
                var s = _shell != null && _shell.Session != null ? _shell.Session.State : SessionState.Title;
                bool moving = s == SessionState.Battle || (_shell != null && _shell.CanExplore);
                OnDemandRendering.renderFrameInterval = moving || IsOpen ? 1 : 2;
            }

            if (_status != null && _statusUntil > 0f && Time.unscaledTime > _statusUntil) { _status.text = string.Empty; _statusUntil = 0f; }
            if (_errorsDirty && Time.unscaledTime > _errorsFlushAt) FlushErrors();
        }

        // ============================ нотатка ============================

        private IEnumerator OpenNote()
        {
            IsOpen = true; // з цього кадру F8 і клавіші гри не перехоплюються вдруге
            yield return new WaitForEndOfFrame();
            _pendingShot = CaptureJpg();
            _pendingCtx = Context();
            _pendingSave = _shell != null && _shell.Session != null ? _shell.Session.CaptureForBugReport() : null;
            _category = PlaytestCategory.Bug;
            MarkCategory();
            _text.value = string.Empty;
            _window.style.display = DisplayStyle.Flex;
            _text.Focus();
        }

        private void SaveNote()
        {
            string text = _text.value;
            if (string.IsNullOrWhiteSpace(text)) { CloseNote(); return; }
            _index++;
            string id = PlaytestLog.NoteId(_index);
            try
            {
                if (_pendingShot != null) File.WriteAllBytes(Path.Combine(_dir, id + ".jpg"), _pendingShot);
                if (!string.IsNullOrEmpty(_pendingSave)) File.WriteAllText(Path.Combine(_dir, id + ".save.txt"), _pendingSave);
                File.WriteAllText(Path.Combine(_dir, id + ".txt"), PlaytestLog.NoteDetails(_index, _category, text, _pendingCtx));
                File.AppendAllText(Path.Combine(_dir, "notes.md"),
                    PlaytestLog.NoteRow(_index, _category, text, _pendingCtx, _pendingShot != null, !string.IsNullOrEmpty(_pendingSave)));
                Flash("Нотатку " + id + " записано");
            }
            catch (Exception ex)
            {
                Flash("Не вдалося записати: " + ex.Message);
            }
            CloseNote();
        }

        private void CloseNote()
        {
            _window.style.display = DisplayStyle.None;
            _pendingShot = null;
            _pendingSave = null;
            _pendingCtx = null;
            IsOpen = false;
        }

        private void Flash(string message)
        {
            if (_status == null) return;
            _status.text = message;
            _statusUntil = Time.unscaledTime + 2.5f;
        }

        /// <summary>Кадр, зменшений до 960 px завширшки, JPG 75 — дешево і зберігати, і переглядати.</summary>
        private static byte[] CaptureJpg()
        {
            Texture2D full = null, small = null;
            RenderTexture rt = null;
            try
            {
                full = ScreenCapture.CaptureScreenshotAsTexture();
                int w = Mathf.Min(ShotWidth, full.width);
                int h = Mathf.Max(1, full.height * w / full.width);
                rt = RenderTexture.GetTemporary(w, h, 0);
                Graphics.Blit(full, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                small = new Texture2D(w, h, TextureFormat.RGB24, false);
                small.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                small.Apply();
                RenderTexture.active = prev;
                return small.EncodeToJPG(75);
            }
            catch (Exception) { return null; }
            finally
            {
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (full != null) Destroy(full);
                if (small != null) Destroy(small);
            }
        }

        private PlaytestContext Context()
        {
            var ctx = new PlaytestContext { GraphicsTier = GraphicsTier.Current.ToString(), Fps = _fps };
            var session = _shell != null ? _shell.Session : null;
            if (session == null) return ctx;
            try
            {
                ctx.State = session.State.ToString();
                var view = session.CurrentView;
                if (view != null) { ctx.Day = view.Day; ctx.Phase = view.Phase.ToString(); }
                string place = _shell.InteriorBuildingId != null ? "будівля " + _shell.InteriorBuildingId : "село";
                if (_shell.InventoryOpen) place += ", «лялька»";
                else if (_shell.OpenPanelId != UxPanelId.None) place += ", панель " + _shell.OpenPanelId;
                ctx.Place = ctx.State + " · " + place;
                var weather = session.GetWeatherView();
                if (weather != null) ctx.Weather = weather.Today;
                var log = session.DayLog;
                var recent = new List<string>();
                for (int i = Math.Max(0, log.Count - PlaytestLog.RecentEventCount); i < log.Count; i++) recent.Add(log[i].Key);
                ctx.RecentEvents = recent;
            }
            catch (Exception) { /* контекст — найкраще, що вдалося; нотатка важливіша */ }
            return ctx;
        }

        // ============================ помилки рушія ============================

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (_log != null)
            {
                try
                {
                    _log.WriteLine("[" + type + " " + Time.frameCount + "] " + condition);
                    if (type == LogType.Exception || type == LogType.Error) _log.WriteLine("    " + PlaytestLog.ErrorKey(stackTrace));
                }
                catch (Exception) { }
            }
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            string key = PlaytestLog.ErrorKey(condition);
            int n;
            _errors[key] = _errors.TryGetValue(key, out n) ? n + 1 : 1;
            if (!_errorStacks.ContainsKey(key)) _errorStacks[key] = stackTrace;
            // Виняток — одразу на диск: гра виходить через TerminateProcess (HardExit), і відкладений
            // запис губився (тур 07.10.2026). Решта помилок — пачкою раз на 5 с.
            if (type == LogType.Exception) { FlushErrors(); return; }
            if (!_errorsDirty) { _errorsDirty = true; _errorsFlushAt = Time.unscaledTime + 5f; }
        }

        private void FlushErrors()
        {
            if (_dir == null) return;
            _errorsDirty = false;
            var list = new List<KeyValuePair<string, int>>(_errors);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            try { File.WriteAllText(Path.Combine(_dir, "errors.md"), PlaytestLog.ErrorsReport(list, _errorStacks)); }
            catch (Exception) { }
        }

        // ============================ вікно ============================

        private void BuildWindow()
        {
            var asset = Resources.Load<PanelSettings>(HudToolkitTheme.PanelResourcePath);
            _panel = asset != null ? Instantiate(asset) : ScriptableObject.CreateInstance<PanelSettings>();
            _panel.name = "AlphaPlaytestPanel";
            _panel.scaleMode = PanelScaleMode.ConstantPixelSize;
            _panel.scale = UI.UiScale.PanelScale();
            _panel.sortingOrder = 50f; // поверх усіх екранів
            if (_panel.themeStyleSheet == null)
            {
                var theme = Resources.Load<ThemeStyleSheet>(HudToolkitTheme.ThemeResourcePath);
                if (theme != null) _panel.themeStyleSheet = theme;
            }
            _host = new GameObject("Плейтест (UI Toolkit)");
            DontDestroyOnLoad(_host);
            _host.SetActive(false);
            var doc = _host.AddComponent<UIDocument>();
            doc.panelSettings = _panel;
            _host.SetActive(true);
            var root = doc.rootVisualElement;
            var font = HudToolkitTheme.LoadFont();
            if (font != null) root.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(font));
            root.pickingMode = PickingMode.Ignore;

            _status = HudToolkitTheme.Text(string.Empty, HudToolkitTheme.BodySize, HudToolkitTheme.AccentColor);
            _status.style.position = Position.Absolute;
            _status.style.left = 16f; _status.style.bottom = 12f;
            _status.pickingMode = PickingMode.Ignore;
            root.Add(_status);

            _window = new VisualElement();
            _window.style.position = Position.Absolute;
            _window.style.left = new Length(20, LengthUnit.Percent);
            _window.style.right = new Length(20, LengthUnit.Percent);
            _window.style.top = new Length(30, LengthUnit.Percent);
            _window.style.backgroundColor = HudToolkitTheme.PanelColor;
            HudToolkitTheme.Border(_window, 2f, HudToolkitTheme.AccentColor);
            HudToolkitTheme.Padding(_window, 14f, 18f);
            _window.style.display = DisplayStyle.None;
            root.Add(_window);

            _window.Add(HudToolkitTheme.Text("Нотатка тесту — тип, суть, Enter (Esc — скасувати). Кадр і стан уже збережено.",
                HudToolkitTheme.BodySize, HudToolkitTheme.TextColor, bold: true));
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 8f; row.style.marginBottom = 8f;
            foreach (PlaytestCategory c in Enum.GetValues(typeof(PlaytestCategory)))
            {
                var captured = c;
                var b = new Button(() => { _category = captured; MarkCategory(); _text.Focus(); }) { text = PlaytestLog.CategoryLabel(c) };
                b.focusable = false;
                b.style.marginRight = 6f;
                b.style.backgroundColor = HudToolkitTheme.RaisedColor;
                b.style.color = HudToolkitTheme.TextColor;
                _categoryButtons.Add(b);
                row.Add(b);
            }
            _window.Add(row);
            _text = new TextField { multiline = false, maxLength = PlaytestLog.MaxNoteLength };
            _text.style.fontSize = HudToolkitTheme.BodySize;
            _window.Add(_text);
        }

        private void MarkCategory()
        {
            var values = (PlaytestCategory[])Enum.GetValues(typeof(PlaytestCategory));
            for (int i = 0; i < _categoryButtons.Count; i++)
                HudToolkitTheme.Border(_categoryButtons[i], 2f, values[i] == _category ? HudToolkitTheme.AccentColor : HudToolkitTheme.BorderColor);
        }
    }
}
