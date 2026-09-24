using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>Заголовок слота — те, що показує SaveLoadScreen БЕЗ читання самого блоба.</summary>
    public sealed class SaveSlotHeader
    {
        public int Slot;
        public bool Occupied;
        public string Headline;
        public int Day;
    }

    /// <summary>
    /// Слоти 0..2 + автосейв (слот -1) під <see cref="Application.persistentDataPath"/>.
    ///
    /// ЩО ЦЕ НЕ РОБИТЬ. Ані байта з опак-блоба (композитний блоб R13 —
    /// фрагменти DayProcessor/GameSession/кубика) тут не парситься: ядро
    /// віддає рядок, ядро ж його й читає назад. Файлове сховище відповідає
    /// лише за «записати цілим, прочитати цілим, не загубити на середині».
    ///
    /// АТОМАРНІСТЬ. Запис іде у тимчасовий файл поруч, і лише після того, як
    /// він повністю написаний, стає самим слотом (<see cref="File.Replace"/>
    /// або, коли слота ще нема, звичайний <see cref="File.Move"/>). Якщо
    /// процес впаде посередині запису — впаде тимчасовий файл, а слот, що
    /// вже був, лишається цілим. Половинного збереження не буває.
    /// </summary>
    public static class SaveFileStore
    {
        public const int AutosaveSlot = -1;
        public const int MinSlot = 0;
        public const int MaxSlot = 2;

        private const string Folder = "Saves";
        private const string BlobExtension = ".sav";
        private const string HeaderExtension = ".head.txt";

        /// <summary>Каталог сховища; створюється лінь-но при першому зверненні.</summary>
        public static string RootDirectory()
        {
            string dir = Path.Combine(Application.persistentDataPath, Folder);
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>Записує опак-рядок у слот атомарно й оновлює його заголовок поруч.</summary>
        public static void Write(int slot, string blob, string headline, int day)
        {
            if (blob == null) throw new ArgumentNullException("blob");

            WriteAtomic(BlobPath(slot), blob);
            WriteAtomic(HeaderPath(slot), headline ?? string.Empty, day);
        }

        /// <summary>Читає опак-рядок слота як є, без жодного розбору. null — слота нема.</summary>
        public static string Read(int slot)
        {
            string path = BlobPath(slot);
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
        }

        public static bool Occupied(int slot)
        {
            return File.Exists(BlobPath(slot));
        }

        public static void Delete(int slot)
        {
            SafeDelete(BlobPath(slot));
            SafeDelete(HeaderPath(slot));
        }

        /// <summary>Заголовки в порядку показу SaveLoadScreen: слоти 0..2, потім автосейв.</summary>
        public static List<SaveSlotHeader> ListHeaders()
        {
            var list = new List<SaveSlotHeader>();
            for (int slot = MinSlot; slot <= MaxSlot; slot++) list.Add(ReadHeader(slot));
            list.Add(ReadHeader(AutosaveSlot));
            return list;
        }

        private static SaveSlotHeader ReadHeader(int slot)
        {
            var header = new SaveSlotHeader { Slot = slot, Occupied = Occupied(slot) };
            if (!header.Occupied) return header;

            string path = HeaderPath(slot);
            if (!File.Exists(path)) return header; // блоб є, заголовок пошкоджений/старий — слот усе одно робочий

            var lines = File.ReadAllLines(path, Encoding.UTF8);
            if (lines.Length > 0) header.Headline = lines[0];
            if (lines.Length > 1) int.TryParse(lines[1], out header.Day);
            return header;
        }

        private static void WriteAtomic(string path, string headline, int day)
        {
            WriteAtomic(path, headline + "\n" + day);
        }

        /// <summary>temp-файл поруч, потім заміна: цільового файлу може ще не бути — тоді звичайний перенос.</summary>
        private static void WriteAtomic(string path, string content)
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, content, Encoding.UTF8);

            try
            {
                if (File.Exists(path))
                {
                    string backup = path + ".bak";
                    File.Replace(temp, path, backup);
                    SafeDelete(backup);
                }
                else
                {
                    File.Move(temp, path);
                }
            }
            catch (IOException)
            {
                // Заміна впала (крос-диск/крос-платформа) — останній шанс:
                // видалити старе й перенести нове. Гірше, ніж File.Replace,
                // лише вузьким вікном між Delete і Move, але це той самий
                // ризик, що й у File.Move завжди був.
                SafeDelete(path);
                File.Move(temp, path);
            }
        }

        private static void SafeDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
                // Заголовок/бекап — не критичний файл: не читається зараз,
                // прочитається наступного разу. Провал видалення тут не має
                // рвати збереження самого блоба.
            }
        }

        private static string BlobPath(int slot)
        {
            return Path.Combine(RootDirectory(), SlotName(slot) + BlobExtension);
        }

        private static string HeaderPath(int slot)
        {
            return Path.Combine(RootDirectory(), SlotName(slot) + HeaderExtension);
        }

        private static string SlotName(int slot)
        {
            return slot == AutosaveSlot ? "autosave" : "slot" + slot;
        }
    }
}
