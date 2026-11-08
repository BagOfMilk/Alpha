using System;
using System.Collections.Generic;
using System.IO;

namespace Game.Gameplay.Ring
{
    /// <summary>
    /// Аргументи запуску «Рингу» (<c>-ring-shots</c>, <c>-ring-seed</c>, <c>-ring-demo</c>, <c>-gfx</c>…).
    ///
    /// У зібраній грі — командний рядок. У редакторі командного рядка гри немає, тож аргументи лежать ще й у файлі
    /// <see cref="EditorArgsFile"/>: його пише <c>Editor/RingInEditor.Run</c> перед входом у Play Mode, і файл переживає
    /// перезавантаження домену, що стирає статичні поля. Власник, 08.11.2026: «Зроби так шоб гра більше не ходила в
    /// інтернет і ти її міг локально тестувати без білдку» — огляд іде в Play Mode редактора, без Ring.exe.
    ///
    /// Формат файла: перший рядок — позначка процесу редактора (<c>pid=…</c>), далі по аргументу в рядку (шлях із
    /// пробілами лишається одним аргументом). Файл чужого сеансу (після краху редактора) не читається.
    /// </summary>
    public static class RingLaunchArgs
    {
        /// <summary>Шлях від кореня проєкту; <c>Temp/</c> ігнорує git, Unity чистить її при закритті редактора.</summary>
        public const string EditorArgsFile = "Temp/ring-editor.args";

        /// <summary>Командний рядок, а в редакторі — ще й аргументи з <see cref="EditorArgsFile"/> (вони йдуть після).</summary>
        public static string[] All()
        {
            string[] cmd = Environment.GetCommandLineArgs();
#if UNITY_EDITOR
            string[] editor = EditorArgs();
            if (editor.Length > 0)
            {
                var all = new string[cmd.Length + editor.Length];
                Array.Copy(cmd, all, cmd.Length);
                Array.Copy(editor, 0, all, cmd.Length, editor.Length);
                return all;
            }
#endif
            return cmd;
        }

        /// <summary>Значення після прапорця <paramref name="name"/> або null.</summary>
        public static string Value(string name)
        {
            string[] args = All();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        /// <summary>Чи є прапорець (без урахування регістру).</summary>
        public static bool Has(string name)
        {
            foreach (var a in All())
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

#if UNITY_EDITOR
        /// <summary>Чи запущено гру з редактора з аргументами (огляд), а не звичайним Play.</summary>
        public static bool FromEditor { get { return EditorArgs().Length > 0; } }

        /// <summary>Аргументи для наступного входу в Play Mode (порожній список — звичайна гра).</summary>
        public static void WriteEditorArgs(IList<string> args)
        {
            string path = Path.Combine(ProjectRoot(), EditorArgsFile);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var lines = new List<string> { PidToken() };
            if (args != null)
                foreach (var a in args)
                    if (!string.IsNullOrEmpty(a)) lines.Add(a);
            File.WriteAllLines(path, lines.ToArray());
        }

        /// <summary>Стерти файл: інакше наступний звичайний Play знову став би оглядом. Кличе вихід із Play Mode.</summary>
        public static void ClearEditorArgs()
        {
            string path = Path.Combine(ProjectRoot(), EditorArgsFile);
            if (File.Exists(path)) File.Delete(path);
        }

        private static string[] EditorArgs()
        {
            string path = Path.Combine(ProjectRoot(), EditorArgsFile);
            if (!File.Exists(path)) return new string[0];
            string[] lines = File.ReadAllLines(path);
            if (lines.Length == 0 || lines[0].Trim() != PidToken()) return new string[0];
            var args = new List<string>();
            for (int i = 1; i < lines.Length; i++)
                if (lines[i].Trim().Length > 0) args.Add(lines[i].Trim());
            return args.ToArray();
        }

        private static string ProjectRoot()
        {
            var parent = Directory.GetParent(UnityEngine.Application.dataPath);
            return parent != null ? parent.FullName : UnityEngine.Application.dataPath;
        }

        private static string PidToken() { return "pid=" + System.Diagnostics.Process.GetCurrentProcess().Id; }
#endif
    }
}
