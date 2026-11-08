using System;
using System.Collections.Generic;
using System.IO;
using Game.Gameplay.Ring;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// «Ринг» у Play Mode відкритого редактора — без збірки Ring.exe. Власник, 08.11.2026: «Зроби так шоб гра більше не
    /// ходила в інтернет і ти її міг локально тестувати без білдку».
    ///
    /// <see cref="Run"/> збирає сцену кодом (як <see cref="RingSceneBuilder.BuildPlayer"/>), ставить Game view 1600×900,
    /// кладе аргументи у файл <see cref="RingLaunchArgs"/> і входить у Play Mode. Огляд (<c>-ring-shots</c>) сам виходить
    /// із Play Mode, коли закінчить: <c>HardExit</c> у редакторі — це вихід із Play Mode.
    ///
    /// Керування ззовні — файлами, без мережі й без Unity CLI (той на кожен виклик шле телеметрію): редактор раз на
    /// пів секунди читає <see cref="CommandFile"/> (перший рядок — команда, далі по аргументу в рядку) і пише стан у
    /// <see cref="StatusFile"/>. Команди: <c>refresh</c>, <c>run</c> [аргументи гри], <c>stop</c> (вийти з Play Mode),
    /// <c>quit</c> (закрити редактор). Обгортка — <c>tools/ring-editor.ps1</c>.
    /// </summary>
    [InitializeOnLoad]
    public static class RingInEditor
    {
        public const string CommandFile = "Temp/ring-editor.cmd";
        public const string StatusFile = "Temp/ring-editor.status";
        private const int ShotWidth = 1600;
        private const int ShotHeight = 900;
        private const string LastKey = "RingInEditor.last";
        private static double _nextTick;

        static RingInEditor()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state =>
            {
                // Огляд, перерваний руками (Stop), лишив би файл аргументів, і наступний звичайний Play знову став би оглядом.
                if (state == PlayModeStateChange.EnteredEditMode)
                {
                    RingLaunchArgs.ClearEditorArgs();
                    Last = "play-ended";
                    WriteStatus();
                }
            };
        }

        [MenuItem("Alpha/Ринг: грати в редакторі (без збірки)")]
        private static void MenuPlay() { Debug.Log("[Ринг] " + Run()); }

        [MenuItem("Alpha/Ринг: огляд сезону в редакторі (кадри в Logs/RingEditorShots)")]
        private static void MenuShots()
        {
            string dir = Path.Combine(ProjectRoot(), "Logs", "RingEditorShots");
            Debug.Log("[Ринг] " + Run("-ring-shots", dir, "-ring-seed", "7"));
        }

        /// <summary>
        /// Запускає гру в Play Mode з аргументами (порожньо — звичайна гра). Повертає «started…» або причину, чому ні
        /// (busy / compile-error / dirty-scene / no-gameview) — її бачить обгортка в <see cref="StatusFile"/>.
        /// </summary>
        public static string Run(params string[] args)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "busy: уже Play Mode";
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return "busy: компіляція чи імпорт";
            if (EditorUtility.scriptCompilationFailed) return "compile-error: спершу виправ помилки компіляції (Console)";
            // RingSceneBuilder.Build відкриває нову сцену в режимі Single — незбережене у відкритих сценах пропало б мовчки.
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var scene = EditorSceneManager.GetSceneAt(i);
                if (scene.isDirty) return "dirty-scene: відкрита сцена має незбережені зміни (" + scene.path + ")";
            }
            // Сцену щоразу збирає код, як перед Ring.exe: зміни збирача й налаштувань сцени видно без ручних кроків.
            RingSceneBuilder.Build();
            RingFontAssets.Ensure();
            var gameView = AutoplayInEditor.FindGameView();
            if (gameView == null)
            {
                var gameViewType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
                if (gameViewType != null) gameView = EditorWindow.GetWindow(gameViewType, false, "Game", false);
            }
            if (gameView == null) return "no-gameview: відкрий вкладку Game (Window → General → Game)";
            // Вкладка Game має бути зверху свого доку, інакше вона не малює і кадри огляду порожні.
            gameView.ShowTab();
            string sizeNote = AutoplayInEditor.TrySetGameViewSize(gameView, ShotWidth, ShotHeight);
            RingLaunchArgs.WriteEditorArgs(args ?? new string[0]);
            EditorApplication.isPlaying = true;
            return "started" + sizeNote + (args != null && args.Length > 0 ? ": " + string.Join(" ", args) : "");
        }

        // ================= керування файлами =================

        private static string Last
        {
            get { return SessionState.GetString(LastKey, "loaded"); }
            set { SessionState.SetString(LastKey, value ?? string.Empty); }
        }

        private static void Tick()
        {
            if (EditorApplication.timeSinceStartup < _nextTick) return;
            _nextTick = EditorApplication.timeSinceStartup + 0.5;
            try
            {
                string cmdPath = Path.Combine(ProjectRoot(), CommandFile);
                if (File.Exists(cmdPath))
                {
                    string[] lines = File.ReadAllLines(cmdPath);
                    File.Delete(cmdPath);
                    Execute(lines);
                }
                WriteStatus();
            }
            catch (Exception ex)
            {
                // Файл ще пишеться обгорткою або зайнятий — спробуємо на наступному такті.
                Last = "error: " + ex.GetType().Name + " " + ex.Message;
            }
        }

        private static void Execute(string[] lines)
        {
            var words = new List<string>();
            foreach (var l in lines)
                if (l.Trim().Length > 0) words.Add(l.Trim());
            if (words.Count == 0) return;
            string verb = words[0];
            words.RemoveAt(0);
            switch (verb)
            {
                case "refresh":
                    AssetDatabase.Refresh();
                    Last = "refreshed";
                    break;
                case "run":
                    Last = Run(words.ToArray());
                    break;
                case "stop":
                    if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
                    Last = "stop-requested";
                    break;
                case "quit":
                    Last = "quitting";
                    WriteStatus();
                    EditorApplication.Exit(0);
                    break;
                default:
                    Last = "unknown-command: " + verb;
                    break;
            }
        }

        private static void WriteStatus()
        {
            string state = EditorApplication.isCompiling ? "compiling"
                : EditorUtility.scriptCompilationFailed ? "compile-error"
                : EditorApplication.isPlayingOrWillChangePlaymode ? "playing"
                : EditorApplication.isUpdating ? "importing"
                : "idle";
            string path = Path.Combine(ProjectRoot(), StatusFile);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path,
                "state=" + state + "\n" +
                "last=" + Last.Replace("\n", " ") + "\n" +
                "pid=" + System.Diagnostics.Process.GetCurrentProcess().Id + "\n" +
                "utc=" + DateTime.UtcNow.ToString("o") + "\n");
        }

        private static string ProjectRoot()
        {
            var parent = Directory.GetParent(Application.dataPath);
            return parent != null ? parent.FullName : Application.dataPath;
        }
    }
}
