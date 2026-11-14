using System;
using System.Collections.Generic;
using System.Text;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Core.Ring.Match;
using Game.Gameplay.Ring.Stage;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Фінішер грається своїм прийомом (прохід 3 «Скилів», 3-P1-3: 35 із 35 фінішерів суперника — один і той самий кліп, бо ШІ
    /// кликав «Фінішер» без підказки й у події не було ключа прийому). Кар'єра кладе в <see cref="MatchSetup"/> фінішер ґіміка
    /// для показу; ШІ й загальна кнопка його беруть. Ключ — лише показ: кидки, ажіотаж і повтор ті самі, що без нього.
    /// </summary>
    public sealed class FinisherShowTests
    {
        [Test]
        public void EveryGimmick_FinisherShow_IsItsCatalogMove_WithItsOwnClip()
        {
            var c = CareerKit.Content();
            var clips = new HashSet<string>();
            foreach (var g in c.Gimmicks)
            {
                var h = c.FinisherShow(g.Id);
                Assert.AreEqual(g.FinisherMove, h.Key, "ґімік " + g.Key + ": ключ фінішера для показу");
                Assert.AreEqual(0, h.RollMod, "ґімік " + g.Key + ": показ не дає бонусу до кидка");
                Assert.AreEqual(0, h.HypeOnStrong, "ґімік " + g.Key + ": показ не дає ажіотажу");
                Assert.IsTrue(RingClipTable.KnowsMove(h.Key), "таблиця кліпів не знає фінішера " + h.Key);
                var ev = new MatchEvent
                {
                    Kind = MatchEventKind.Action, Actor = MatchSide.Rival, Action = MatchActionId.Finisher, Tier = RollTier.Strong,
                    MoveKey = h.Key, Staging = h.Staging ?? MoveStaging.Center
                };
                string clip = null;
                foreach (var st in RingClipTable.Plan(ev))
                    if (st.Kind == StageStepKind.Pair || st.Kind == StageStepKind.Solo) { clip = st.Clip; break; }
                Assert.IsNotNull(clip, "фінішер " + h.Key + " без кліпа пари");
                clips.Add(clip);
            }
            Assert.AreEqual(c.Gimmicks.Count, clips.Count, "у кожного ґіміка свій кліп фінішера: " + string.Join(", ", clips));
        }

        [Test]
        public void CareerMatches_FinisherEvents_CarryTheGimmickMoveKey()
        {
            int rivalFinishers = 0, playerFinishers = 0;
            var rivalKeys = new HashSet<string>();
            for (int i = 0; i < 120; i++)
            {
                var gim = (Gimmick)(i % CareerKit.GimmickCount);
                var s = CareerKit.NewHero((ulong)(4000 + i), gim);
                s.EndWeek();
                var e = s.StartShow();
                Assert.IsNotNull(e, "тиждень 1 — звичайне шоу");
                var npc = s.Content.FindNpc(s.State.Opponent);
                string mine = s.Content.GimmickOf(gim).FinisherMove, theirs = s.Content.GimmickOf(npc.Gimmick).FinisherMove;
                Assert.AreEqual(mine, e.Setup.PlayerFinisher.Key);
                Assert.AreEqual(theirs, e.Setup.RivalFinisher.Key);
                Play(e);
                foreach (var ev in e.State.Log)
                {
                    if (ev.Kind != MatchEventKind.Action || ev.Action != MatchActionId.Finisher) continue;
                    if (ev.Actor == MatchSide.Rival) { rivalFinishers++; rivalKeys.Add(ev.MoveKey); Assert.AreEqual(theirs, ev.MoveKey, "фінішер суперника " + npc.Id); }
                    else { playerFinishers++; Assert.AreEqual(mine, ev.MoveKey, "фінішер гравця"); }
                }
            }
            TestContext.WriteLine("фінішерів: суперник " + rivalFinishers + ", гравець " + playerFinishers + "; ключі суперників: " + string.Join(", ", rivalKeys));
            Assert.Greater(rivalFinishers, 0, "замір має бачити фінішери суперника");
            Assert.Greater(playerFinishers, 0, "замір має бачити фінішери гравця");
        }

        [Test]
        public void FeudMatch_HammerFinisher_IsHisGimmickMove()
        {
            var s = CareerKit.NewAtTitle(77);
            var rep = new CareerRunReport();
            int guard = 0;
            while (!(s.Stage == CareerStage.Show && s.State.Phase == ShowPhase.Feud && s.Feud.Stage == RingStage.MatchPrep))
            {
                if (++guard > 400) Assert.Fail("бот не дійшов до матчу ворожнечі, етап " + s.Stage + "/" + s.State.Phase);
                CareerBot.Step(s, 0, rep);
            }
            var e = s.StartFeudMatch();
            var hammer = s.Content.FindNpc(CareerContent.HammerId);
            string theirs = s.Content.GimmickOf(hammer.Gimmick).FinisherMove, mine = s.Content.GimmickOf(s.State.Gimmick).FinisherMove;
            Assert.AreEqual(theirs, e.Setup.RivalFinisher.Key, "фінішер Молота — прийом його ґіміка");
            Assert.AreEqual(mine, e.Setup.PlayerFinisher.Key);
            Play(e);
            foreach (var ev in e.State.Log)
                if (ev.Kind == MatchEventKind.Action && ev.Action == MatchActionId.Finisher)
                    Assert.AreEqual(ev.Actor == MatchSide.Rival ? theirs : mine, ev.MoveKey);
        }

        /// <summary>Баланс: той самий матч із підказкою фінішера й без неї — ті самі кидки, підсумок і довжина.</summary>
        [Test]
        public void FinisherShow_ChangesNoRollsAndNoResult()
        {
            int diffs = 0, finishers = 0;
            for (int i = 0; i < 80; i++)
            {
                var gim = (Gimmick)(i % CareerKit.GimmickCount);
                var a = CareerKit.NewHero((ulong)(9000 + i), gim); a.EndWeek();
                var b = CareerKit.NewHero((ulong)(9000 + i), gim); b.EndWeek();
                var ea = a.StartShow();
                var eb = b.StartShow();
                eb.Setup.PlayerFinisher = null; eb.Setup.RivalFinisher = null;
                Play(ea); Play(eb);
                string la = Trace(ea), lb = Trace(eb);
                foreach (var ev in ea.State.Log) if (ev.Kind == MatchEventKind.Action && ev.Action == MatchActionId.Finisher) finishers++;
                if (la != lb) { diffs++; TestContext.WriteLine("сід " + (9000 + i) + ":\n з підказкою: " + la + "\n без:         " + lb); }
                Assert.AreEqual(ea.Result.Winner, eb.Result.Winner);
                Assert.AreEqual(ea.Result.Kind, eb.Result.Kind);
                Assert.AreEqual(ea.Result.FinalHype, eb.Result.FinalHype);
                Assert.AreEqual(ea.Result.Rounds, eb.Result.Rounds);
            }
            TestContext.WriteLine("фінішерів у замірі: " + finishers);
            Assert.Greater(finishers, 0, "замір має бачити фінішери");
            Assert.AreEqual(0, diffs, "підказка показу змінила хід матчу");
        }

        private static void Play(MatchEngine e)
        {
            int guard = 0;
            while (!e.IsOver)
            {
                if (++guard > MatchAutoplay.MaxSteps) throw new InvalidOperationException("матч не закінчився");
                MatchAi.Step(e);
            }
        }

        /// <summary>Хід матчу без ключа показу: подія, бік, дія, кидок, ступінь, ажіотаж-ефекти.</summary>
        private static string Trace(MatchEngine e)
        {
            var sb = new StringBuilder();
            foreach (var ev in e.State.Log)
            {
                sb.Append((int)ev.Kind).Append('/').Append((int)ev.Actor).Append('/').Append((int)ev.Action)
                  .Append('/').Append(ev.Total).Append('/').Append((int)ev.Tier);
                foreach (var fx in ev.Effects) sb.Append(':').Append((int)fx.Kind).Append(fx.Amount);
                sb.Append(' ');
            }
            return sb.ToString();
        }
    }
}
