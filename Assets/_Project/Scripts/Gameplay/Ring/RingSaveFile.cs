using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Game.Gameplay.Ring
{
    /// <summary>
    /// Файл кар'єри «Рингу» без ризику втратити її на півдорозі запису (прогін скілів 08.11.2026, знахідка В5).
    /// Новий сейв спершу пишеться в <c>.tmp</c> поруч, потім атомарно стає на місце старого, а старий лишається як
    /// <c>.bak</c>. Якщо гра впаде під час запису, на диску лишиться або старий цілий файл, або новий цілий.
    /// Під час читання, якщо основний файл зіпсований чи порожній, береться <c>.bak</c> — попередній збережений тиждень.
    /// Лише System.IO: без рушія, тож перевіряється звичайним .NET.
    /// </summary>
    public static class RingSaveFile
    {
        public static string BackupPath(string path) { return path + ".bak"; }

        private static string TempPath(string path) { return path + ".tmp"; }

        /// <summary>Є що продовжувати: основний файл або запасний.</summary>
        public static bool Exists(string path) { return File.Exists(path) || File.Exists(BackupPath(path)); }

        /// <summary>Записати рядки сейва атомарно; попередній сейв стає <c>.bak</c>.</summary>
        public static void Write(string path, string[] lines)
        {
            string temp = TempPath(path);
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var w = new StreamWriter(fs, new UTF8Encoding(false)))
            {
                for (int i = 0; i < lines.Length; i++) w.WriteLine(lines[i]);
                w.Flush();
                fs.Flush(true);                     // на диск, а не лише в кеш ОС
            }
            if (!File.Exists(path)) { File.Move(temp, path); return; }
            string backup = BackupPath(path);
            try
            {
                File.Replace(temp, path, backup, true);
            }
            catch (Exception e) when (e is IOException || e is PlatformNotSupportedException || e is UnauthorizedAccessException)
            {
                // Файлова система без атомарної заміни (деякі Android-сховища): копія старого в .bak, далі перенос нового.
                File.Copy(path, backup, true);
                File.Delete(path);
                File.Move(temp, path);
            }
        }

        /// <summary>
        /// Спробувати прочитати сейв: спершу основний файл, потім <c>.bak</c>. <paramref name="accept"/> відновлює кар'єру з
        /// рядків і кидає виняток, якщо вони биті. Повертає рядки, які прийнято, і чи це запасна копія.
        /// Якщо не підійшов жоден — кидає останній виняток (або <see cref="FileNotFoundException"/>, коли файлів немає).
        /// </summary>
        public static string[] Read(string path, Action<string[]> accept, out bool fromBackup)
        {
            fromBackup = false;
            Exception last = null;
            var candidates = new List<string> { path, BackupPath(path) };
            for (int i = 0; i < candidates.Count; i++)
            {
                string p = candidates[i];
                if (!File.Exists(p)) continue;
                try
                {
                    var lines = File.ReadAllLines(p);
                    if (lines.Length == 0 || string.IsNullOrEmpty(lines[0])) throw new InvalidDataException("порожній сейв: " + Path.GetFileName(p));
                    accept(lines);
                    fromBackup = i > 0;
                    return lines;
                }
                catch (Exception e) { last = e; }
            }
            if (last != null) throw last;
            throw new FileNotFoundException("сейва немає", path);
        }

        /// <summary>Стерти сейв разом із запасною копією й недописаним тимчасовим файлом.</summary>
        public static void Delete(string path)
        {
            foreach (var p in new[] { path, BackupPath(path), TempPath(path) })
                if (File.Exists(p)) File.Delete(p);
        }
    }
}
