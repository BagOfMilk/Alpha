using System.IO;
using Game.Core.Saves;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Unity-обёртка сериализации сейва (US-16.1): SaveData ↔ JSON (JsonUtility) ↔
    /// файлы в Application.persistentDataPath. Слоты 1..3 + быстрый сейв +
    /// автосейв с ротацией бэкапа (перезапись сначала уводит старый файл в .bak —
    /// один битый сейв не убивает кампанию). Ядро (Capture/Restore) — чистый C#.
    /// </summary>
    public static class SaveSerializer
    {
        public const int SlotCount = 3;

        public static string QuickSavePath => Path.Combine(Application.persistentDataPath, "quicksave.json");
        public static string AutosavePath => Path.Combine(Application.persistentDataPath, "autosave.json");

        public static string SlotPath(int slot) =>
            Path.Combine(Application.persistentDataPath, $"save_slot{slot}.json");

        public static string ToJson(SaveData data) => JsonUtility.ToJson(data, prettyPrint: true);
        public static SaveData FromJson(string json) => JsonUtility.FromJson<SaveData>(json);

        /// <summary>Запись с ротацией: предыдущая версия файла уходит в .bak.</summary>
        public static void SaveToFile(SaveData data, string path)
        {
            if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
            File.WriteAllText(path, ToJson(data));
        }

        /// <summary>Чтение: битый/нечитаемый основной файл — пробуем .bak.</summary>
        public static SaveData LoadFromFile(string path)
        {
            var data = TryRead(path);
            return data ?? TryRead(path + ".bak");
        }

        public static bool Exists(string path) => File.Exists(path) || File.Exists(path + ".bak");

        private static SaveData TryRead(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                // version у дефолтного SaveData уже 3 — сам по себе он ничего не
                // доказывает; валиден сейв с хотя бы одним напарником (ростер
                // кампании не бывает пуст: протагонист обязателен).
                var data = FromJson(File.ReadAllText(path));
                return data != null && data.companions != null && data.companions.Count > 0
                    ? data : null;
            }
            catch (System.Exception)
            {
                return null; // битый файл — вызывающий упадёт на .bak
            }
        }
    }
}
