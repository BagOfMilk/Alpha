using System;
using System.Linq;
using Game.Core.Combat;
using Game.Core.Session;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Правило без кубика (власник, 29.09.2026: «Усі відсотки мають працювати у тому
    /// режимі, в настройках його можна змінить»): прев'ю наперед каже, чи влучить саме
    /// цей удар, і каже правду; правило перемикається посеред партії й діє з
    /// наступного бою.
    /// </summary>
    public class HitRuleCarryTests
    {
        [Test]
        public void Preview_PredictsEachBlow_AndThePredictionIsTheFact()
        {
            var s = new GameSession();
            s.NewTrainingBattle(new TrainingBattleOptions { HitRule = HitRuleKind.Threshold });

            int checkedBlows = 0;
            for (int guard = 0; guard < 200 && s.State == SessionState.Battle && checkedBlows < 12; guard++)
            {
                var view = s.GetBattleView();
                if (view.IsAiTurn) { s.CombatAiStepOneAction(); continue; }
                var me = view.Units.Single(u => u.Id == view.CurrentUnitId);
                var foe = view.Units.Where(u => u.Side == "Enemy" && !u.IsOutOfBattle && !u.IsDowned)
                    .OrderBy(u => Math.Max(Math.Abs(u.Pos.X - me.Pos.X), Math.Abs(u.Pos.Y - me.Pos.Y))).FirstOrDefault();
                if (foe == null) break;

                var preview = s.PreviewAttack(me.Id, foe.Id);
                if (preview.Result != "Success") { s.CombatEndTurn(); continue; }
                Assert.AreEqual(1, preview.PredictedShots, "без кубика прев'ю знає результат удару");

                int before = s.DayLog.Count;
                Assert.AreEqual(CombatActionResult.Success, s.CombatAttack(foe.Id));
                var mine = s.DayLog.Skip(before).First(e => e.Key.StartsWith("combat.attack.", StringComparison.Ordinal));
                bool hit = mine.Key != "combat.attack.miss"; // ключ стрічки сесії, не журналу бою
                Assert.AreEqual(preview.PredictedHits == 1, hit, "прогноз прев'ю = факт (удар №" + (checkedBlows + 1) + ")");
                checkedBlows++;
            }
            Assert.Greater(checkedBlows, 3, "перевірено кілька ударів поспіль");
        }

        [Test]
        public void HitRule_SwitchesMidCampaign_FromTheNextBattle()
        {
            var s = new GameSession(new ScriptedDiceRoller());
            s.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold });
            Assert.AreEqual(HitRuleKind.Threshold, s.HitRule);

            s.SetHitRule(HitRuleKind.Percent);
            Assert.AreEqual(HitRuleKind.Percent, s.HitRule);
            s.NewTrainingBattle(new TrainingBattleOptions { HitRule = s.HitRule });
            Assert.IsTrue(s.GetBattleView().IsHitRulePercent, "наступний бій — уже на новому правилі");

            var noDice = new GameSession();
            noDice.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold });
            Assert.Throws<InvalidOperationException>(() => noDice.SetHitRule(HitRuleKind.Percent),
                "правило з кубиком без впровадженого кубика — відмова (R1)");
        }

        [Test]
        public void HitRule_SurvivesAFreshRestore()
        {
            var s = new GameSession(new ScriptedDiceRoller());
            s.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold });
            var step = s.AdvanceScene();
            while (!step.IsFinished) step = step.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene();
            s.SetHitRule(HitRuleKind.Percent);

            var fresh = new GameSession(new ScriptedDiceRoller());
            fresh.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold });
            fresh.RestoreFromBlob(s.SaveState(0));
            Assert.AreEqual(HitRuleKind.Percent, fresh.HitRule);
        }
    }
}
