using System;
using System.IO;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Звідки автотур бере свої прапорці (<c>-autoplay</c>, <c>-autoplay-journal</c>…).
    ///
    /// У зібраній грі — з командного рядка, як і раніше. У редакторі командного
    /// рядка гри немає, тому прапорці лежать у файлі <see cref="EditorArgsFile"/>:
    /// його пише <c>Editor/AutoplayInEditor.Run</c> (через <c>WriteEditorArgs</c>)
    /// перед входом у Play Mode, а файл переживає перезавантаження домену,
    /// яке стирає всі статичні поля.
    /// Власник, 28.09.2026: «мені не подобається, що ти відчиняєш та зачиняєш
    /// вікно. Проєкт нехай буде запущенним при тестуванні» — тури йдуть у
    /// відкритому редакторі, без окремого Alpha.exe.
    /// </summary>
    public static class AutoplayArgs
    {
        /// <summary>Шлях від кореня проєкту. <c>Temp/</c> ігнорується git і чиститься при закритті редактора.</summary>
        public const string EditorArgsFile = "Temp/alpha-autoplay.args";

        public static bool Has(string flag)
        {
            foreach (var arg in Environment.GetCommandLineArgs())
                if (arg == flag) return true;
#if UNITY_EDITOR
            foreach (var arg in EditorArgs())
                if (arg == flag) return true;
#endif
            return false;
        }

        /// <summary>Корінь проєкту в редакторі, тека поруч з <c>.exe</c> у зібраній грі.</summary>
        public static string Root()
        {
            var parent = Directory.GetParent(Application.dataPath);
            return parent != null ? parent.FullName : Application.dataPath;
        }

#if UNITY_EDITOR
        /// <summary>Тур запущено з редактора (є файл прапорців), а не з командного рядка.</summary>
        public static bool FromEditor => EditorArgs().Length > 0;

        /// <summary>
        /// Записати прапорці туру для наступного входу в Play Mode. Файл
        /// позначений процесом редактора: після краху чи вбитого редактора
        /// <c>Temp/</c> може лишитися, і без позначки перший звичайний Play
        /// нового сеансу мовчки став би туром.
        /// </summary>
        public static void WriteEditorArgs(string flags)
        {
            string path = Path.Combine(Root(), EditorArgsFile);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, PidToken() + " " + (flags ?? string.Empty));
        }

        /// <summary>
        /// Стерти файл прапорців: інакше наступний звичайний Play у редакторі
        /// знову запустив би тур. Кличуть кінець туру і вихід із Play Mode.
        /// </summary>
        public static void ClearEditorArgs()
        {
            string path = Path.Combine(Root(), EditorArgsFile);
            if (File.Exists(path)) File.Delete(path);
        }

        private static string[] EditorArgs()
        {
            string path = Path.Combine(Root(), EditorArgsFile);
            if (!File.Exists(path)) return new string[0];
            var tokens = File.ReadAllText(path).Split(new[] { ' ', ',', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            // Чужий (попередній) сеанс редактора — не наш тур.
            return tokens.Length > 0 && tokens[0] == PidToken() ? tokens : new string[0];
        }

        private static string PidToken() => "pid=" + System.Diagnostics.Process.GetCurrentProcess().Id;
#endif
    }
}
