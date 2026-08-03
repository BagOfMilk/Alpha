using System.Collections;
using System.IO;
using Game.Core.Combat;
using Game.Core.Economy;
using Game.Core.Quests;
using Game.Gameplay;
using Game.Gameplay.UI;
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
        // Тесты трогают БОЕВЫЕ quicksave.json/autosave.json (и их .bak — ротация
        // SaveToFile перезаписывает и бэкап) в persistentDataPath — сохраняем и
        // возвращаем всё, чтобы прогон тестов не съедал сейвы разработчика.
        private static string[] ProtectedPaths => new[]
        {
            SaveSerializer.QuickSavePath, SaveSerializer.QuickSavePath + ".bak",
            SaveSerializer.AutosavePath, SaveSerializer.AutosavePath + ".bak"
        };

        [SetUp]
        public void BackupDevSaves()
        {
            foreach (var path in ProtectedPaths)
                if (File.Exists(path))
                    File.Copy(path, path + ".test-bak", overwrite: true);
        }

        [TearDown]
        public void RestoreDevSaves()
        {
            foreach (var path in ProtectedPaths)
            {
                if (File.Exists(path + ".test-bak"))
                {
                    File.Copy(path + ".test-bak", path, overwrite: true);
                    File.Delete(path + ".test-bak");
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
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
        public IEnumerator BattleScene_Loads_AndAiPlaysEnemyTurns()
        {
            yield return SceneManager.LoadSceneAsync("Battle", LoadSceneMode.Single);
            yield return null;
            var controller = Object.FindFirstObjectByType<BattleScreenController>();
            Assert.IsNotNull(controller, "в Battle-сцене есть контроллер экрана боя");
            Assert.IsNotNull(controller.Combat, "бой собран");
            Assert.AreEqual(CombatOutcome.Ongoing, controller.Combat.Outcome);
            Assert.AreEqual(Side.Player, controller.Combat.Current.Side,
                "после загрузки ход у игрока (вражеские ходы ИИ отыграл)");

            // Инициатива напарников выше вражеской — пропускаем ходы игрока,
            // пока очередь не дойдёт до врагов и ИИ не отыграет их (лог вырастет).
            int logBefore = controller.Combat.Log.Count;
            for (int i = 0; i < 8 && controller.Combat.Log.Count == logBefore; i++)
                controller.EndPlayerTurn();
            Assert.Greater(controller.Combat.Log.Count, logBefore, "враги сходили — лог боя вырос");
        }

        [UnityTest]
        public IEnumerator CampaignScene_NewGame_Prologue_CityLoop_Autosaves()
        {
            GameFlow.Reset();
            if (File.Exists(SaveSerializer.AutosavePath)) File.Delete(SaveSerializer.AutosavePath);

            yield return SceneManager.LoadSceneAsync("Campaign", LoadSceneMode.Single);
            yield return null;
            var controller = Object.FindFirstObjectByType<CampaignScreenController>();
            Assert.IsNotNull(controller, "в Campaign-сцене есть контроллер");

            controller.OnStartCampaign(); // новая игра с дефолтным билдером
            Assert.IsNotNull(GameFlow.Campaign, "кампания стартовала");
            Assert.IsTrue(File.Exists(SaveSerializer.AutosavePath), "автосейв записан на старте");

            // Пролог (US-17.1) начинается сразу: квест взят, первый этап — бой.
            Assert.IsNotNull(GameFlow.PendingQuest, "пролог взят в работу");
            Assert.AreEqual("prologue", GameFlow.PendingQuest.Def.Id);
            Assert.AreEqual(QuestStageKind.Combat, GameFlow.PendingQuest.Current.Kind);

            // Прогоняем этапы через Core (бой в Battle-сцене покрыт своим тестом):
            // победа → развилка «кого прикрываешь» → исход.
            GameFlow.LastQuestStep = GameFlow.PendingQuest.ResolveCombat(won: true);
            GameFlow.LastQuestStep = GameFlow.PendingQuest.Choose(0, GameFlow.Campaign.Roster.All);
            Assert.IsFalse(GameFlow.PendingQuest.IsActive, "пролог дошёл до исхода");
            controller.ConcludeActiveQuest();
            Assert.IsNull(GameFlow.PendingQuest, "прогон закрыт");
            Assert.IsTrue(GameFlow.Campaign.Flags.Contains(DefaultQuests.PrologueDoneFlag),
                "флаг пролога стоит — повторно не предлагается");

            int day = GameFlow.Campaign.Base.CurrentDay;
            controller.OnWaitDay();
            Assert.AreEqual(day + 1, GameFlow.Campaign.Base.CurrentDay, "«ждать день» двигает календарь");

            GameFlow.Reset(); // не влияем на другие тесты
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
