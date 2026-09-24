using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core.Combat;
using Game.Core.Loop;
using Game.Core.Session;
using Game.Core.Session.Bots;
using Game.Core.Session.Views;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Пакет D2, §6.2/§6.3 TEST_BUILD.md: покриття ключів тексту (генератор
    /// <c>docs/TEST_BUILD_KEYS.txt</c> — E3 звірить його зі своєю таблицею) і
    /// замір темпу (§6.3, акцептанс R9).
    /// </summary>
    public class TimingAndKeysHarnessTests
    {
        /// <summary>
        /// §6.2: усі ключі, що фактично вилітають із GameSession/View-шару за
        /// 15-денний прогін кожної з 5 політик + окремі фікстури (обидва шляхи
        /// фіналу, тренувальний бій, створення протагоніста) — той самий набір
        /// сценаріїв, що AllMechanicsCoverageTests. Пишеться у ВІДСОРТОВАНИЙ,
        /// детермінований файл: той самий прогін завжди дає той самий список
        /// (HitRuleKind.Threshold, інваріант 1 — жодного дайса).
        /// </summary>
        [Test]
        public void Coverage_CollectsEveryEmittedKey_AndWritesTestBuildKeysFile()
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);

            // 5 політик × 15 діб (5 сценарних + 10 вільних, §3.6).
            foreach (var policy in FivePolicies())
            {
                var log = new List<GameEvent>();
                var viewKeys = new List<string>();
                var options = new NewGameOptions { SkipCreation = false, HitRule = HitRuleKind.Threshold, Seed = 1 };
                BotRunner.PlayDays(policy, 15, options, log, null, null, viewKeys);
                AddEventKeys(keys, log);
                AddViewKeys(keys, viewKeys);
            }

            // Фінал, обидва шляхи (§6.1 №31 — 15-денний прогін не гарантує жоден).
            AddFixtureKeys(keys, FinaleFixtureKeys(new PacifistPolicy(), 11));
            AddFixtureKeys(keys, FinaleFixtureKeys(new BloodyPolicy(), 13));

            // Тренувальний бій (§6.1 №35) — окрема пісочниця, ключі не йдуть у кампанійний DayLog.
            var trainingLog = new List<GameEvent>();
            var trainingSession = new GameSession();
            trainingSession.NewTrainingBattle(new TrainingBattleOptions { HitRule = HitRuleKind.Threshold });
            trainingSession.CombatAutoResolve();
            foreach (var e in trainingSession.DayLog) trainingLog.Add(e);
            AddEventKeys(keys, trainingLog);

            // Створення протагоніста (§6.1 №36).
            var creationSession = new GameSession();
            creationSession.NewGame(new NewGameOptions { SkipCreation = false, HitRule = HitRuleKind.Threshold });
            creationSession.SetProtagonistName("Тестова");
            creationSession.ConfirmCreation();
            AddEventKeys(keys, creationSession.DayLog);

            var sorted = keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            Assert.Greater(sorted.Count, 10, "§6.2: за весь бот-прогін мали вилетіти хоч якісь ключі — забагато порожньо для 15×5 діб плюс фікстури");

            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "docs", "TEST_BUILD_KEYS.txt");
            File.WriteAllText(path, string.Join("\n", sorted) + "\n", new System.Text.UTF8Encoding(false));

            TestContext.WriteLine("§6.2: записано " + sorted.Count + " ключів у " + path);
        }

        private static void AddEventKeys(HashSet<string> into, IEnumerable<GameEvent> log)
        {
            foreach (var e in log)
                if (!string.IsNullOrEmpty(e.Key)) into.Add(e.Key);
        }

        private static void AddViewKeys(HashSet<string> into, IEnumerable<string> viewKeys)
        {
            foreach (var k in viewKeys)
                if (!string.IsNullOrEmpty(k)) into.Add(k);
        }

        private static void AddFixtureKeys(HashSet<string> into, IReadOnlyList<GameEvent> log) => AddEventKeys(into, log);

        private static List<GameEvent> FinaleFixtureKeys(IBotPolicy policy, ulong seed)
        {
            var options = new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold, Seed = seed };
            var s = new GameSession();
            s.NewGame(options);
            BotRunner.Drive(s, policy, 4); // доби 1..4
            var log = new List<GameEvent>();
            BotRunner.Drive(s, policy, 1, log); // доба 5 — фінал
            return log;
        }

        private static IEnumerable<IBotPolicy> FivePolicies()
        {
            yield return new StewardPolicy();
            yield return new PacifistPolicy();
            yield return new BloodyPolicy();
            yield return new PatrolAlwaysPolicy();
            yield return new DelveGreedyPolicy();
        }

        // =======================================================================
        // §6.3 — замір темпу (акцептанс R9): рахує лише, що воно ОБЧИСЛЮЄТЬСЯ;
        // числа фіксуються в звіті пакета, не підганяються під 60–90 хв тут.
        // =======================================================================

        [Test]
        public void Timing_Days1To5_ComputesMinutesPerPolicy_BloodyPolicy()
        {
            var tally = new BotRunner.TimingTally();
            var options = new NewGameOptions { SkipCreation = false, HitRule = HitRuleKind.Threshold, Seed = 1 };
            BotRunner.PlayDays(new BloodyPolicy(), 5, options, null, null, null, null, null, tally);

            double minutes = tally.TotalMinutes();
            Assert.GreaterOrEqual(minutes, 0.0, "§6.3: замір темпу мав обчислитись без винятку");
            TestContext.WriteLine(string.Format(
                "§6.3 BloodyPolicy днів 1-5: screenReads={0} simple={1} decisions={2} combatTurns={3} autoBattles={4} dungeonRooms={5} saveLoads={6} => {7:0.0} сек = {8:0.00} хв",
                tally.ScreenReads, tally.SimpleCommands, tally.Decisions, tally.CombatTurns,
                tally.AutoBattles, tally.DungeonRooms, tally.SaveLoads, tally.TotalSeconds(), minutes));
        }

        [Test]
        public void Timing_Days1To5_ComputesMinutesPerPolicy_AllFive()
        {
            foreach (var policy in FivePolicies())
            {
                var tally = new BotRunner.TimingTally();
                var options = new NewGameOptions { SkipCreation = false, HitRule = HitRuleKind.Threshold, Seed = 1 };
                BotRunner.PlayDays(policy, 5, options, null, null, null, null, null, tally);

                double minutes = tally.TotalMinutes();
                Assert.GreaterOrEqual(minutes, 0.0, "§6.3 (" + policy.Name + "): замір темпу мав обчислитись без винятку");
                TestContext.WriteLine(string.Format("§6.3 {0} днів 1-5: {1:0.0} сек = {2:0.00} хв", policy.Name, tally.TotalSeconds(), minutes));
            }
        }
    }
}
