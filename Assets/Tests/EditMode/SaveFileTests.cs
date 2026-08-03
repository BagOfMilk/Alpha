using System.IO;
using Game.Core.Balance;
using Game.Core.Saves;
using Game.Gameplay;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Файловый слой сейвов (итерация 16): слоты, автосейв, ротация .bak —
    /// один битый файл не убивает кампанию.
    /// </summary>
    public class SaveFileTests
    {
        private string _path;

        [SetUp]
        public void MakeTempPath()
        {
            _path = Path.Combine(Path.GetTempPath(), "alpha-savefile-test.json");
            Delete();
        }

        [TearDown]
        public void Delete()
        {
            if (File.Exists(_path)) File.Delete(_path);
            if (File.Exists(_path + ".bak")) File.Delete(_path + ".bak");
        }

        private static SaveData Capture(int gold)
        {
            var campaign = Campaign.NewGame(new BalanceConfig(), null, seed: 1);
            campaign.Base.Resources.Add(Game.Core.Economy.ResourceType.Gold, gold);
            return SaveSystem.Capture(campaign);
        }

        [Test]
        public void SaveToFile_RotatesBackup()
        {
            SaveSerializer.SaveToFile(Capture(100), _path);
            Assert.IsFalse(File.Exists(_path + ".bak"), "первой записи бэкапить нечего");

            SaveSerializer.SaveToFile(Capture(200), _path);
            Assert.IsTrue(File.Exists(_path + ".bak"), "перезапись увела прошлую версию в .bak");

            var current = SaveSerializer.LoadFromFile(_path);
            Assert.AreEqual(200, current.gold);
            var backup = SaveSerializer.FromJson(File.ReadAllText(_path + ".bak"));
            Assert.AreEqual(100, backup.gold, "в бэкапе — предыдущее состояние");
        }

        [Test]
        public void LoadFromFile_FallsBackToBackup_WhenMainCorrupt()
        {
            SaveSerializer.SaveToFile(Capture(100), _path);
            SaveSerializer.SaveToFile(Capture(200), _path); // .bak = 100
            File.WriteAllText(_path, "{ это не json ");     // основной файл побит

            var data = SaveSerializer.LoadFromFile(_path);
            Assert.IsNotNull(data, "битый основной файл — грузимся из .bak");
            Assert.AreEqual(100, data.gold);
        }

        [Test]
        public void Exists_SeesBackupToo()
        {
            Assert.IsFalse(SaveSerializer.Exists(_path));
            SaveSerializer.SaveToFile(Capture(1), _path);
            SaveSerializer.SaveToFile(Capture(2), _path);
            File.Delete(_path); // остался только .bak
            Assert.IsTrue(SaveSerializer.Exists(_path), "кампания восстановима из бэкапа");
        }
    }
}
