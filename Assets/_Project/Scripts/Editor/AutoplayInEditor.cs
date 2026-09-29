using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Автотур у Play Mode відкритого редактора — без окремого Alpha.exe і без
    /// нових вікон (власник, 28.09.2026: «мені не подобається, що ти відчиняєш
    /// та зачиняєш вікно. Проєкт нехай буде запущенним при тестуванні»).
    ///
    /// <see cref="Run"/> пише прапорці у файл (<see cref="AutoplayArgs"/>),
    /// ставить Game view 1600×900 і входить у Play Mode; тур сам виходить із
    /// Play Mode, коли закінчить (<c>AutoplayBootstrap.Finish</c>). Знімки й
    /// підсумок — у <c>Logs/Autoplay/</c> проєкту. З терміналу:
    /// <c>unity command eval</c> → <c>AutoplayInEditor.Run("-autoplay-journal")</c>,
    /// обгортка — <c>tools/editor-tour.ps1</c>.
    /// </summary>
    [InitializeOnLoad]
    public static class AutoplayInEditor
    {
        private const string ScenePath = "Assets/Scenes/Game.unity";
        private const int ShotWidth = 1600;
        private const int ShotHeight = 900;

        static AutoplayInEditor()
        {
            // Тур, перерваний руками (Stop у редакторі), лишив би файл прапорців,
            // і наступний звичайний Play знову запустив би тур.
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode) AutoplayArgs.ClearEditorArgs();
            };
        }

        [MenuItem("Alpha/Автотур/Журнал механік (44)")]
        private static void MenuJournal() => Debug.Log(Run("-autoplay-journal"));

        [MenuItem("Alpha/Автотур/Сценарій — відсоток")]
        private static void MenuPercent() => Debug.Log(Run("-autoplay"));

        [MenuItem("Alpha/Автотур/Сценарій — поріг")]
        private static void MenuThreshold() => Debug.Log(Run("-autoplay -autoplay-threshold"));

        [MenuItem("Alpha/Автотур/Довгий — до бунту")]
        private static void MenuLong() => Debug.Log(Run("-autoplay-long"));

        [MenuItem("Alpha/Автотур/Лише бій")]
        private static void MenuBattle() => Debug.Log(Run("-autoplay-battle"));

        /// <summary>
        /// Запускає тур. Повертає «started» або причину, чому не запустив:
        /// CLI бачить відповідь як результат <c>eval</c>.
        /// </summary>
        public static string Run(string flags)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "busy: уже Play Mode";
            if (EditorApplication.isCompiling) return "busy: компіляція";
            if (!File.Exists(ScenePath)) return "no-scene: спершу Alpha/Собрать сцену «Игра» (GameSceneBuilder.Build)";

            var active = EditorSceneManager.GetActiveScene();
            if (active.path != ScenePath)
            {
                // Не відкривати поверх незбереженого — інакше редактор спитає
                // модальним вікном, і тур завис би на ньому.
                if (active.isDirty) return "dirty-scene: відкрита сцена має незбережені зміни (" + active.path + ")";
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            string root = AutoplayArgs.Root();
            string output = Path.Combine(root, "Logs", "Autoplay");
            if (Directory.Exists(output)) Directory.Delete(output, true);

            string argsPath = Path.Combine(root, AutoplayArgs.EditorArgsFile);
            Directory.CreateDirectory(Path.GetDirectoryName(argsPath));
            File.WriteAllText(argsPath, flags ?? string.Empty);

            string sizeNote = TrySetGameViewSize(ShotWidth, ShotHeight);
            EditorApplication.isPlaying = true;
            return "started" + sizeNote;
        }

        // ================= Game view 1600×900 =================

        /// <summary>
        /// Той самий розмір, що в автотурах зібраної гри (IMGUI розкладається за
        /// Screen.width/height, тож інший розмір — інший кадр). Публічного API
        /// немає — через рефлексію внутрішніх GameViewSizes/GameView; якщо в
        /// цій версії Unity щось перейменовано, тур іде в поточному розмірі
        /// Game view, а відповідь каже чому.
        /// </summary>
        private static string TrySetGameViewSize(int width, int height)
        {
            try
            {
                var editorAssembly = typeof(Editor).Assembly;
                var sizesType = editorAssembly.GetType("UnityEditor.GameViewSizes");
                var sizeType = editorAssembly.GetType("UnityEditor.GameViewSize");
                var sizeKindType = editorAssembly.GetType("UnityEditor.GameViewSizeType");
                var gameViewType = editorAssembly.GetType("UnityEditor.GameView");
                if (sizesType == null || sizeType == null || sizeKindType == null || gameViewType == null)
                    return " (Game view: внутрішні типи не знайдено — розмір як є)";

                var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
                var sizes = singleton.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null, null);
                var group = sizesType.GetMethod("GetGroup").Invoke(sizes, new object[] { (int)GameViewSizeGroupType.Standalone });
                var groupType = group.GetType();

                int index = FindSize(group, groupType, width, height);
                if (index < 0)
                {
                    var fixedKind = Enum.Parse(sizeKindType, "FixedResolution");
                    var size = Activator.CreateInstance(sizeType, fixedKind, width, height, "Alpha " + width + "x" + height);
                    groupType.GetMethod("AddCustomSize").Invoke(group, new[] { size });
                    index = FindSize(group, groupType, width, height);
                }
                if (index < 0) return " (Game view: не вдалося додати " + width + "x" + height + ")";

                var gameView = EditorWindow.GetWindow(gameViewType);
                var select = gameViewType.GetMethod("SizeSelectionCallback", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (select != null) select.Invoke(gameView, new object[] { index, null });
                else
                {
                    var prop = gameViewType.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (prop == null) return " (Game view: немає способу вибрати розмір — розмір як є)";
                    prop.SetValue(gameView, index, null);
                }
                return " (Game view " + width + "x" + height + ")";
            }
            catch (Exception ex)
            {
                return " (Game view: " + ex.GetType().Name + " — розмір як є)";
            }
        }

        private static int FindSize(object group, Type groupType, int width, int height)
        {
            int total = (int)groupType.GetMethod("GetTotalCount").Invoke(group, null);
            var getSize = groupType.GetMethod("GetGameViewSize");
            for (int i = 0; i < total; i++)
            {
                var size = getSize.Invoke(group, new object[] { i });
                var t = size.GetType();
                int w = (int)t.GetProperty("width").GetValue(size, null);
                int h = (int)t.GetProperty("height").GetValue(size, null);
                string kind = t.GetProperty("sizeType").GetValue(size, null).ToString();
                if (w == width && h == height && kind == "FixedResolution") return i;
            }
            return -1;
        }
    }
}
