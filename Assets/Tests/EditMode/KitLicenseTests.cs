using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Охоронець ліцензій модульного набору персонажів (Поправка №18: «Лише CC0», репозиторій публічний).
    /// 07.10.2026 у наборі знайшлись 11 зачісок і туніка з бібліотеки MakeHuman під AGPL3 і CC BY — збирач
    /// брав усе підряд. Тепер <c>tools/blender/build_kit_all.py</c> пише <c>Textures/Kit/SOURCES.txt</c>
    /// (файл → асет → ліцензія) і падає на не-CC0; тест тримає, щоб жодна бібліотечна текстура не лежала
    /// без рядка і жоден рядок не був не CC0.
    /// </summary>
    public class KitLicenseTests
    {
        private static string KitTextures => Path.Combine(Application.dataPath, "Art", "Textures", "Kit");

        // Власна генерація проєкту (Blender-скрипти tools/blender): тканини, акценти, шкіри культур, нейтральне волосся.
        private static bool IsOwnGenerated(string file) =>
            file.StartsWith("kit_") || file.StartsWith("acc_") || file.StartsWith("skin_") || file.StartsWith("hairN_");

        [Test]
        public void EveryLibraryTexture_HasACc0SourceLine()
        {
            string sources = Path.Combine(KitTextures, "SOURCES.txt");
            Assert.That(File.Exists(sources), Is.True, "немає " + sources);
            var lines = File.ReadAllLines(sources).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList();
            foreach (var l in lines)
            {
                var cols = l.Split('\t');
                Assert.AreEqual(3, cols.Length, "рядок «" + l + "»: файл, асет, ліцензія через табуляцію");
                Assert.AreEqual("CC0", cols[2], cols[0] + " (" + cols[1] + "): лише CC0");
            }
            var listed = lines.Select(l => l.Split('\t')[0]).ToList();
            foreach (var path in Directory.GetFiles(KitTextures, "*.png"))
            {
                string file = Path.GetFileName(path);
                if (IsOwnGenerated(file)) continue;
                Assert.That(listed, Does.Contain(file), file + ": текстура з бібліотеки без рядка походження в SOURCES.txt");
            }
        }
    }
}
