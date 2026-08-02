using System.Collections;
using System.IO;
using Game.Core.Economy;
using Game.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests.PlayMode
{
    /// <summary>
    /// PlayMode-смоук: страхует Unity-шов (сцена, MonoBehaviour-жизненный цикл,
    /// файловый сейв в persistentDataPath). Рабочая коняка тестов — EditMode-ядро;
    /// здесь только то, что без движка не проверить.
    /// </summary>
    public class PlayModeSmokeTests
    {
        // Тесты трогают БОЕВОЙ quicksave.json в persistentDataPath — бэкапим и
        // возвращаем, чтобы прогон тестов не съедал сейв разработчика.
        private string _backup;

        [SetUp]
        public void BackupQuickSave()
        {
            _backup = SaveSerializer.QuickSavePath + ".test-bak";
            if (File.Exists(SaveSerializer.QuickSavePath))
                File.Copy(SaveSerializer.QuickSavePath, _backup, overwrite: true);
            else
                _backup = null;
        }

        [TearDown]
        public void RestoreQuickSave()
        {
            if (_backup != null)
            {
                File.Copy(_backup, SaveSerializer.QuickSavePath, overwrite: true);
                File.Delete(_backup);
            }
            else if (File.Exists(SaveSerializer.QuickSavePath))
            {
                File.Delete(SaveSerializer.QuickSavePath);
            }
        }

        private static IEnumerator LoadBoot()
        {
            yield return SceneManager.LoadSceneAsync("Boot", LoadSceneMode.Single);
            yield return null; // кадр на Awake/Start
        }

        [UnityTest]
        public IEnumerator BootScene_Loads_AndCampaignStarts()
        {
            yield return LoadBoot();
            var host = Object.FindFirstObjectByType<CampaignHost>();
            Assert.IsNotNull(host, "в Boot-сцене есть CampaignHost");
            Assert.IsNotNull(host.Session, "кампания стартовала в Awake");
            Assert.AreEqual(6, host.Session.Roster.Count, "дефолтный ростер собран");
        }

        [UnityTest]
        public IEnumerator QuickSave_QuickLoad_FileRoundTrip()
        {
            yield return LoadBoot();
            var host = Object.FindFirstObjectByType<CampaignHost>();

            host.Session.Base.Resources.Add(ResourceType.Gold, 123);
            host.QuickSave();
            Assert.IsTrue(File.Exists(SaveSerializer.QuickSavePath), "файл сейва записан");

            host.QuickLoad();
            Assert.AreEqual(123, host.Session.Base.Resources.Get(ResourceType.Gold),
                "файловый round-trip через persistentDataPath");
        }

        [UnityTest]
        public IEnumerator QuickLoad_WithoutFile_IsGraceful()
        {
            yield return LoadBoot();
            var host = Object.FindFirstObjectByType<CampaignHost>();
            if (File.Exists(SaveSerializer.QuickSavePath)) File.Delete(SaveSerializer.QuickSavePath);

            host.QuickLoad(); // файла нет — сообщение в лог, сессия жива
            Assert.IsNotNull(host.Session, "отсутствие сейва не роняет игру");
        }
    }
}
