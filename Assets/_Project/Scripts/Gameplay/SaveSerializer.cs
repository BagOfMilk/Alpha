using System.IO;
using Game.Core.Saves;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Unity-обёртка сериализации сейва (US-16.1): SaveData ↔ JSON (JsonUtility) ↔
    /// файл в Application.persistentDataPath. Ядро (Capture/Restore) — чистый C#,
    /// здесь только мост к движку и диску. (В продакшене — Easy Save 3, US-18.1.)
    /// </summary>
    public static class SaveSerializer
    {
        public static string QuickSavePath => Path.Combine(Application.persistentDataPath, "quicksave.json");

        public static string ToJson(SaveData data) => JsonUtility.ToJson(data, prettyPrint: true);
        public static SaveData FromJson(string json) => JsonUtility.FromJson<SaveData>(json);

        public static void SaveToFile(SaveData data, string path)
        {
            File.WriteAllText(path, ToJson(data));
        }

        public static SaveData LoadFromFile(string path)
        {
            return File.Exists(path) ? FromJson(File.ReadAllText(path)) : null;
        }
    }
}
