using System.Collections;
using System.IO;
using Game.Core;
using Game.Core.Combat;
using Game.Core.Economy;
using Game.Core.Expeditions;
using Game.Core.Quests;
using Game.Gameplay;
using Game.Gameplay.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

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

            // Песочница — не тупик: выход в кампанию доступен (итерация 18).
            var exit = Object.FindFirstObjectByType<UIDocument>().rootVisualElement.Q<Button>("exit-button");
            Assert.IsNotNull(exit, "в бою есть кнопка выхода");
            Assert.AreEqual(DisplayStyle.Flex, exit.style.display.value,
                "в песочнице выход показан (в бою кампании — скрыт)");
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

            // Стройка не предлагает «пустую» секцию (итерация 18: покупка None
            // сжигала золото и материалы в никуда).
            var root = Object.FindFirstObjectByType<UIDocument>().rootVisualElement;
            foreach (var child in root.Q<VisualElement>("build-list").Children())
                Assert.IsFalse((child as Button).text.StartsWith("None"),
                    "секции None в списке стройки быть не должно");

            GameFlow.Reset(); // не влияем на другие тесты
        }

        [UnityTest]
        public IEnumerator PostponedQuest_DoesNotHijackExpeditionBattle()
        {
            GameFlow.Reset();
            yield return SceneManager.LoadSceneAsync("Campaign", LoadSceneMode.Single);
            yield return null;
            var controller = Object.FindFirstObjectByType<CampaignScreenController>();
            controller.OnStartCampaign(); // пролог взят: PendingQuest != null

            Assert.IsNotNull(GameFlow.PendingQuest, "квест висит (игрок его отложил)");

            // Имитируем handoff боя ВЫЛАЗКИ при отложенном квесте.
            var campaign = GameFlow.Campaign;
            var exp = campaign.LaunchExpedition(DefaultWorld.NewMap().Get("east_road").Plan);
            Assert.AreEqual(ExpeditionSendResult.Success, exp.TrySend(new[] { "leader" }));
            campaign.DepartExpedition();
            GameFlow.PendingExpedition = exp;
            GameFlow.BattleKind = PendingBattleKind.Expedition;

            Assert.AreEqual(PendingBattleKind.Expedition, GameFlow.BattleKind,
                "бой вылазки помечен как вылазочный — отложенный квест его не перехватит");
            Assert.IsNotNull(campaign.ActiveExpedition);

            // Исход боя вылазки обязан закрывать ИМЕННО вылазку.
            var cs = new CombatState(CombatDemo.BuildArena(), campaign.Cfg, new ScriptedRng(1, 100, 5));
            cs.AddUnit(CombatUnit.FromCompanion(campaign.Roster.Get("leader"),
                DefaultContent.Rifle(), campaign.Cfg), CombatDemo.SquadSpawns[0]);
            var dummy = new UnitProfile
            {
                DisplayName = "мишень", MaxHp = 1, MaxAp = 8, Accuracy = 1,
                Initiative = 0, CanBeDowned = false
            };
            cs.AddUnit(new CombatUnit("e", Side.Enemy, dummy, DefaultContent.Pistol()),
                CombatDemo.SquadSpawns[1]);
            cs.Begin();
            Assert.AreEqual(CombatActionResult.Success, cs.Attack("e"));
            Assert.AreEqual(CombatOutcome.Victory, cs.Outcome);

            campaign.ConcludeExpedition(cs);
            Assert.IsNull(campaign.ActiveExpedition, "вылазка завершена, а не залипла навсегда");
            Assert.IsNotNull(GameFlow.PendingQuest, "отложенный квест при этом цел");

            GameFlow.Reset();
        }

        [UnityTest]
        public IEnumerator CampaignScene_CharacterPanel_SpendsSkillPoints()
        {
            GameFlow.Reset();
            yield return SceneManager.LoadSceneAsync("Campaign", LoadSceneMode.Single);
            yield return null;
            var controller = Object.FindFirstObjectByType<CampaignScreenController>();
            controller.OnStartCampaign();

            // Пролог отыгрывается через Core, дальше — город.
            GameFlow.PendingQuest.ResolveCombat(won: true);
            GameFlow.PendingQuest.Choose(0, GameFlow.Campaign.Roster.All);
            controller.ConcludeActiveQuest();

            var leader = GameFlow.Campaign.Roster.Get("leader");
            leader.GainXp(2000, GameFlow.Campaign.Cfg); // кампания прокачала лидера
            Assert.Greater(leader.UnspentSkillPoints, 0, "уровни дали очки в пул");

            controller.ShowCharacter("leader");
            var root = Object.FindFirstObjectByType<UIDocument>().rootVisualElement;
            Assert.IsTrue(root.Q<VisualElement>("panel-character").ClassListContains("panel--visible"),
                "карточка бойца открывает экран персонажа");

            var rows = root.Q<ScrollView>("character-skills").contentContainer;
            Assert.AreEqual(10, rows.childCount, "все 10 навыков видны (US-2.2)");
            var plus = rows[0].Q<Button>();
            Assert.IsNotNull(plus);
            Assert.IsTrue(plus.enabledSelf, "при наличии очков «+» активна — очки можно потратить");

            GameFlow.Reset();
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
