using System;
using System.Collections.Generic;
using Game.Core.Combat;
using Game.Core.Session;
using Game.Gameplay.UI;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Рутина плейтесту (docs/PLAYTEST.md): нотатка не ламає таблицю, контекст і помилки згортаються,
    /// зліпок для нотатки не змінює гру (жодного запису в слот і події журналу).
    /// </summary>
    public class PlaytestLogTests
    {
        [Test]
        public void NoteRow_SurvivesPipesAndNewlines_AndLinksItsFiles()
        {
            var ctx = new PlaytestContext { State = "Morning", Day = 3, Phase = "Day", Place = "Morning · село" };
            string row = PlaytestLog.NoteRow(7, PlaytestCategory.Visual, "волосся | чорне\nквадратами", ctx, true, true);
            Assert.AreEqual(1, row.Split('\n').Length - 1, "один рядок таблиці");
            StringAssert.Contains("| 007 | ВИГЛЯД |", row);
            StringAssert.Contains("волосся / чорне квадратами", row);
            StringAssert.Contains("[007.jpg](007.jpg)", row);
            StringAssert.Contains("[007.save.txt](007.save.txt)", row);
            StringAssert.Contains("д3 · Day", row);
        }

        [Test]
        public void LongNote_IsTrimmed_EmptyContextIsSafe()
        {
            string row = PlaytestLog.NoteRow(1, PlaytestCategory.Bug, new string('а', 900), null, false, false);
            StringAssert.Contains("…", row);
            StringAssert.Contains("| — | — |", row);
            Assert.DoesNotThrow(() => PlaytestLog.NoteDetails(1, PlaytestCategory.Idea, null, null));
        }

        [Test]
        public void Errors_GroupByFirstLine_MostFrequentFirst()
        {
            Assert.AreEqual("NullReferenceException: x", PlaytestLog.ErrorKey("NullReferenceException: x\n  at A.B()"));
            var report = PlaytestLog.ErrorsReport(new List<KeyValuePair<string, int>>
            {
                new KeyValuePair<string, int>("NullReferenceException: x", 12),
                new KeyValuePair<string, int>("Missing clip", 1)
            }, new Dictionary<string, string> { { "NullReferenceException: x", "at A.B()\nat C.D()" } });
            Assert.Less(report.IndexOf("×12", StringComparison.Ordinal), report.IndexOf("×1**", StringComparison.Ordinal));
            StringAssert.Contains("`at A.B()`", report);
            StringAssert.Contains("Немає", PlaytestLog.ErrorsReport(null, null));
        }

        [Test]
        public void Categories_AreFive_DigitsMapOneToOne()
        {
            for (int d = 1; d <= 5; d++) Assert.IsNotNull(PlaytestLog.CategoryForDigit(d));
            Assert.IsNull(PlaytestLog.CategoryForDigit(0));
            Assert.IsNull(PlaytestLog.CategoryForDigit(6));
            Assert.AreEqual("2026-10-07_14-05", PlaytestLog.SessionFolderName(new DateTime(2026, 10, 7, 14, 5, 59)));
        }

        [Test]
        public void SnapshotForNote_DoesNotChangeTheGame_AndRestoresInAFreshSession()
        {
            var s = new GameSession();
            s.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold });
            var step = s.AdvanceScene();
            while (!step.IsFinished) step = step.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene();
            Assert.AreEqual(SessionState.Morning, s.State);
            int events = s.DayLog.Count;
            string blob = s.CaptureForBugReport();
            Assert.IsNotNull(blob);
            Assert.AreEqual(events, s.DayLog.Count, "нотатка не пише в журнал гри");
            Assert.IsFalse(s.DayLog.Count > 0 && s.DayLog[s.DayLog.Count - 1].Key == "game.saved");

            var fresh = new GameSession();
            fresh.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold });
            Assert.DoesNotThrow(() => fresh.RestoreFromBlob(blob), "зліпок з нотатки відтворює стан у свіжій сесії");
            Assert.AreEqual(s.CurrentView.Day, fresh.CurrentView.Day);
        }
    }
}
