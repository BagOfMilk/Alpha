using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core.Combat;
using Game.Core.Loop;
using Game.Core.Session;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// M1.10 — Айронмен (US-16.1, UX Q5: «вирішено асистентом за PROC-01, 07.10.2026»):
    /// одне місце збереження (автозбереження), ручне збереження перезаписує його,
    /// у живій грі завантажувати старіше не можна, режим живе в сейві. Звичайна
    /// гра — як було: слоти 0–2 і автозбереження, завантаження вільне.
    /// </summary>
    public class IronmanSaveTests
    {
        private static GameSession Morning(bool ironman)
        {
            var s = new GameSession();
            s.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold, Ironman = ironman });
            var step = s.AdvanceScene();
            while (!step.IsFinished) step = step.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene();
            Assert.AreEqual(SessionState.Morning, s.State);
            return s;
        }

        /// <summary>Одна тиха доба до наступного ранку.</summary>
        private static void PlayDay(GameSession s)
        {
            s.ConfirmMorning();
            var report = s.AdvanceDay();
            while (report != null && report.AwaitsDecision) report = s.ResolveIncident(IncidentPath.Quiet);
            if (s.State == SessionState.Scene)
            {
                var step = s.AdvanceScene();
                while (!step.IsFinished) step = step.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene();
            }
            if (s.State == SessionState.Evening) s.ConfirmEvening();
            if (s.State == SessionState.Night)
            {
                var night = s.AdvanceNight();
                while (night != null && night.AwaitsDecision) night = s.ResolveIncident(IncidentPath.Quiet);
            }
        }

        [Test]
        public void Ironman_ManualSave_AlwaysOverwritesTheSingleSlot()
        {
            var s = Morning(ironman: true);
            Assert.IsTrue(s.IsIronman);
            Assert.AreEqual(GameSession.AutosaveSlot, s.ResolveSaveSlot(1), "будь-який слот — це єдине місце");
            Assert.AreEqual(GameSession.AutosaveSlot, s.ResolveSaveSlot(2));

            string first = s.SaveState(1);
            Assert.AreEqual(first, s.AutosaveBlob, "ручне збереження лягло в єдине місце");
            Assert.AreEqual(GameSession.AutosaveSlot.ToString(), s.DayLog.Last(e => e.Key == "game.saved").Args["slot"]);

            PlayDay(s);
            string second = s.SaveState(2);
            Assert.AreNotEqual(first, second, "тест сам зламався: за добу стан мав змінитись");
            Assert.AreEqual(second, s.AutosaveBlob, "інше «місце» ту саму ячейку перезаписало, а не завело другу");
        }

        [Test]
        public void NormalGame_KeepsItsSlots_AndAutosaveSeparate()
        {
            var s = Morning(ironman: false);
            Assert.IsFalse(s.IsIronman);
            Assert.AreEqual(2, s.ResolveSaveSlot(2));
            string beforeAuto = s.AutosaveBlob;

            string manual = s.SaveState(2);

            Assert.AreEqual(beforeAuto, s.AutosaveBlob, "звичайне збереження автозбереження не чіпає");
            Assert.AreEqual("2", s.DayLog.Last(e => e.Key == "game.saved").Args["slot"]);
            Assert.IsTrue(s.LoadState(2), "у звичайній грі завантажувати вільно");
            Assert.IsNotNull(manual);
        }

        [Test]
        public void Ironman_ModeIsStoredInTheSave_AndComesBackThroughContinue()
        {
            var s = Morning(ironman: true);
            s.SaveState(0);
            string blob = s.AutosaveBlob;

            var fresh = new GameSession();
            fresh.PreloadSlot(GameSession.AutosaveSlot, blob);
            Assert.IsTrue(fresh.ContinueGame(GameSession.AutosaveSlot), "«Продовжити» з титулу веде на єдине місце");
            Assert.IsTrue(fresh.IsIronman, "режим пережив збереження й відновлення у СВІЖУ сесію");

            var normal = Morning(ironman: false);
            var freshNormal = new GameSession();
            freshNormal.PreloadSlot(0, normal.SaveState(0));
            Assert.IsTrue(freshNormal.ContinueGame(0));
            Assert.IsFalse(freshNormal.IsIronman);
        }

        [Test]
        public void Ironman_RefusesLoadingAnOlderState_InTheRunningGame()
        {
            var s = Morning(ironman: true);
            string older = s.SaveState(0);
            PlayDay(s);
            int day = s.CurrentView.Day;

            Assert.IsFalse(s.LoadState(GameSession.AutosaveSlot), "у живій грі завантаження вимкнене — сейв-скам");
            Assert.Throws<InvalidOperationException>(() => s.RestoreFromBlob(older));
            Assert.AreEqual(day, s.CurrentView.Day, "стан не відкотився");
        }

        [Test]
        public void Ironman_AutosavesEveryMorning_IntoTheSameSlot()
        {
            var s = Morning(ironman: true);
            int version = s.AutosaveVersion;
            string before = s.AutosaveBlob;

            PlayDay(s);

            Assert.Greater(s.AutosaveVersion, version, "ранок — новий автосейв");
            Assert.AreNotEqual(before, s.AutosaveBlob);
        }
    }
}
