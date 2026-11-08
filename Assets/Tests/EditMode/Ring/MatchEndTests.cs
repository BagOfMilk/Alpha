using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game.Core.Ring;
using Game.Core.Ring.Match;
using Game.Gameplay.Combat;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>Кінець матчу (TRPG_DESIGN §4.11): утримання, здача, дискваліфікація, ліміт дій; гарантія кінця; детермінізм.</summary>
    public class MatchEndTests
    {
        // ---------- утримання ----------

        [Test]
        public void Pin_ThreeCount_PlayerWins()
        {
            var e = MatchKit.Engine(true, new[] { 11, 3 });
            e.State.Rival.Damage = 1; MatchKit.NotFresh(e.State.Rival);
            MatchKit.Play(e, MatchActionId.Throw);
            MatchKit.Play(e, MatchActionId.Pin);
            Assert.IsTrue(e.IsOver);
            Assert.AreEqual(MatchWinner.Player, e.Result.Winner);
            Assert.AreEqual(MatchEndKind.Pin, e.Result.Kind);
            Assert.Throws<InvalidOperationException>(() => e.EndTurn());
            Assert.AreEqual(0, e.GetAvailableActions().Count(o => o.Available));
        }

        [Test]
        public void Pin_RivalPinsPlayer_RivalWins_AndSellIsCounted()
        {
            var e = MatchKit.Engine(false, new[] { 11, 4 });
            e.State.Player.Damage = 1; MatchKit.NotFresh(e.State.Player);
            MatchKit.Play(e, MatchActionId.Throw);
            MatchKit.Play(e, MatchActionId.Pin);
            Assert.AreEqual(MatchWinner.Rival, e.Result.Winner);
            Assert.AreEqual(1, e.Result.PlayerSells);
        }

        [Test]
        public void KickoutAtTwo_IsCounted_ForResult()
        {
            var e = MatchKit.Engine(false, new[] { 11, 6 });
            e.State.Player.Damage = 1; MatchKit.NotFresh(e.State.Player);
            MatchKit.Play(e, MatchActionId.Throw);
            MatchKit.Play(e, MatchActionId.Pin);
            Assert.IsFalse(e.IsOver);
            Assert.AreEqual(1, e.State.PlayerKickoutsAtTwo);
            e.State.Player.Posture = Posture.Standing;
            // доводимо до кінця (ліміт дій) і читаємо результат
            while (!e.IsOver) e.EndTurn();
            Assert.AreEqual(MatchEndKind.TimeLimit, e.Result.Kind);
            Assert.AreEqual(1, e.Result.PlayerKickoutsAtTwo);
        }

        [Test]
        public void AllyRescue_BreaksTheThreeCount_OncePerMatch()
        {
            // утримання 4+2 — «три», напарник розриває; далі больовий 7–9 тримається; друге утримання — рятунку немає
            var e = MatchKit.Engine(false, new[] { 4, 7, 7, 4 }, s => s.PlayerHasAlly = true);
            e.State.Player.Posture = Posture.Down; e.State.Player.Damage = Math.Max(e.State.Player.Damage, MatchRules.PinMinDamage); MatchKit.NotFresh(e.State.Player);
            e.Perform(MatchActionId.Pin);
            Assert.IsFalse(e.IsOver, "напарник розірвав утримання");
            Assert.IsTrue(e.State.PlayerUsedAlly);
            Assert.IsTrue(e.State.Log.Any(x => x.Kind == MatchEventKind.Rescue));
            Assert.AreEqual(UnavailableReason.JustPinned, e.Describe(MatchActionId.Pin).Reason, "накрити знову — після іншого прийому");
            MatchKit.Play(e, MatchActionId.Submission);
            Assert.IsFalse(e.IsOver);
            e.Perform(MatchActionId.Pin);
            Assert.AreEqual(MatchWinner.Rival, e.Result.Winner, "удруге рятунку немає");
            Assert.IsTrue(e.Result.PlayerUsedAlly);
        }

        [Test]
        public void NoAlly_DisablesActionAndRescue()
        {
            var e = MatchKit.Engine(false, new[] { 4 }, s => { s.PlayerHasAlly = true; s.Stipulations = MatchStipulations.NoAlly; });
            e.State.Player.Posture = Posture.Down; e.State.Player.Damage = Math.Max(e.State.Player.Damage, MatchRules.PinMinDamage); MatchKit.NotFresh(e.State.Player);
            e.Perform(MatchActionId.Pin);
            Assert.AreEqual(MatchWinner.Rival, e.Result.Winner);
            var f = MatchKit.Engine(true, null, s => { s.PlayerHasAlly = true; s.Stipulations = MatchStipulations.NoAlly; });
            Assert.AreEqual(UnavailableReason.AllyForbidden, f.Describe(MatchActionId.Ally).Reason);
        }

        [Test]
        public void Ally_Action_OnlyOncePerMatch_OnlyForPlayerWithAlly()
        {
            var none = MatchKit.Engine(true);
            Assert.AreEqual(UnavailableReason.NoAlly, none.Describe(MatchActionId.Ally).Reason);
            var e = MatchKit.Engine(true, null, s => s.PlayerHasAlly = true);
            MatchKit.Play(e, MatchActionId.Ally);
            Assert.IsTrue(e.State.PlayerUsedAlly);
            Assert.AreEqual(UnavailableReason.AlreadyUsed, e.Describe(MatchActionId.Ally).Reason);
        }

        // ---------- больовий ----------

        [Test]
        public void Submission_Strong_FailedEndure_PlayerWinsBySubmission()
        {
            var e = MatchKit.Engine(true, new[] { 11, 5 });
            e.State.Rival.Posture = Posture.Down; e.State.Rival.Damage = Math.Max(e.State.Rival.Damage, MatchRules.PinMinDamage); MatchKit.NotFresh(e.State.Rival);
            MatchKit.Play(e, MatchActionId.Submission);
            Assert.AreEqual(MatchEndKind.Submission, e.Result.Kind);
            Assert.AreEqual(MatchWinner.Player, e.Result.Winner);
            var ev = e.State.Log.Last(x => x.Kind == MatchEventKind.Endure);
            Assert.AreEqual(MatchRules.DefenseBase - 2, ev.Modifier, "10+ больового: «Терпіти» з -2");
        }

        [Test]
        public void Submission_Miss_OpponentBreaksFree_NoEndureRoll()
        {
            var e = MatchKit.Engine(true, new[] { 3 });
            e.State.Rival.Posture = Posture.Down; e.State.Rival.Damage = Math.Max(e.State.Rival.Damage, MatchRules.PinMinDamage); MatchKit.NotFresh(e.State.Rival);
            MatchKit.Play(e, MatchActionId.Submission);
            Assert.IsFalse(e.IsOver);
            Assert.IsFalse(e.State.Log.Any(x => x.Kind == MatchEventKind.Endure));
            Assert.AreEqual(MatchSide.Rival, e.CurrentActor);
        }

        [Test]
        public void Submission_PartialEndure_LateStagePenalises()
        {
            var late = MatchKit.Engine(true, new[] { 8, 9 });
            late.State.Round = MatchRules.LateStageRound;
            late.State.Rival.Posture = Posture.Down; late.State.Rival.Damage = Math.Max(late.State.Rival.Damage, MatchRules.PinMinDamage); MatchKit.NotFresh(late.State.Rival);
            MatchKit.Play(late, MatchActionId.Submission);
            var ev = late.State.Log.Last(x => x.Kind == MatchEventKind.Endure);
            Assert.AreEqual(MatchRules.DefenseBase - 2, ev.Modifier, "-1 за 7–9 больового, -1 пізня стадія");
        }

        // Відлік за рингом і подвійний відлік — MatchOutsideTests (бій за рингом склейкою, рішення власника 08.11.2026).

        // ---------- дискваліфікація ----------

        [Test]
        public void ThirdWarning_Disqualifies()
        {
            var e = MatchKit.Engine(true, new[] { 3, 3, 3 });
            for (int i = 0; i < 3; i++)
            {
                Assert.IsFalse(e.IsOver);
                MatchKit.Play(e, MatchActionId.Dirty);
                if (!e.IsOver) e.EndTurn();
            }
            Assert.IsTrue(e.IsOver);
            Assert.AreEqual(MatchEndKind.Disqualification, e.Result.Kind);
            Assert.AreEqual(MatchWinner.Rival, e.Result.Winner);
            Assert.AreEqual(3, e.Result.PlayerDirtyMoves);
        }

        [Test]
        public void NoDq_CancelsWarnings()
        {
            var e = MatchKit.Engine(true, new[] { 3, 3, 3, 3 }, s => s.Stipulations = MatchStipulations.NoDq);
            for (int i = 0; i < 4; i++)
            {
                MatchKit.Play(e, MatchActionId.Dirty);
                e.EndTurn();
            }
            Assert.IsFalse(e.IsOver);
            Assert.AreEqual(0, e.State.Player.Warnings);
        }

        // ---------- ліміт дій ----------

        [Test]
        public void ActionLimit_EndsInADraw()
        {
            var e = MatchKit.Engine(true, null, s => s.RoundLimit = 3);
            int n = 0;
            while (!e.IsOver) { e.EndTurn(); n++; }
            Assert.AreEqual(3, n, "кожна дія, і «перевести подих» теж, рахується");
            Assert.AreEqual(MatchEndKind.TimeLimit, e.Result.Kind);
            Assert.AreEqual(MatchWinner.Draw, e.Result.Winner);
            Assert.AreEqual(3, e.Result.Rounds);
        }

        [Test]
        public void Result_FinalHypeAndRounds_AreFilled()
        {
            var e = MatchKit.Engine(true, new[] { 11, 3 });
            e.State.Rival.Damage = 1; MatchKit.NotFresh(e.State.Rival);
            MatchKit.Play(e, MatchActionId.Throw);
            MatchKit.Play(e, MatchActionId.Pin);
            Assert.AreEqual(e.State.Hype, e.Result.FinalHype);
            Assert.AreEqual(2, e.Result.Rounds, "кидок і утримання — дві дії");
        }

        // ---------- гарантія кінця, детермінізм ----------

        private static MatchSetup SeededSetup(int seed)
        {
            Func<int, int> attr = k => (int)(((uint)(seed * 2654435761u + (uint)k * 40503u) >> 7) % 5) - 1;
            var s = MatchKit.Setup(x =>
            {
                foreach (RingAttr a in Enum.GetValues(typeof(RingAttr)))
                {
                    x.Player.Set(a, attr((int)a));
                    x.Rival.Set(a, attr(10 + (int)a));
                }
                x.Player.FinisherAttr = (RingAttr)(seed % 5);
                x.PlayerRole = (RingPath)(seed % 3);
                x.PlayerHasAlly = seed % 4 == 0;
                x.PlayerStartMomentum = seed % 3;
                x.StartHype = 2 + seed % 4;
            });
            return s;
        }

        private static string Fingerprint(MatchEngine e)
        {
            var sb = new StringBuilder();
            foreach (var ev in e.State.Log)
            {
                sb.Append(ev.Kind).Append(':').Append(ev.Actor).Append(':').Append(ev.Action).Append(':').Append(ev.Total).Append(':').Append(ev.Tier);
                foreach (var f in ev.Effects) sb.Append('[').Append(f.Kind).Append(f.Who).Append(f.Amount).Append(']');
                sb.Append(';');
            }
            return sb.ToString();
        }

        [Test]
        public void ThousandAiMatches_AllFinish_WithinLimit_BothWinnersHappen()
        {
            int pw = 0, rw = 0, draws = 0, rounds = 0, kickTwo = 0, kickAll = 0, changes = 0, noChange = 0, longest = 0;
            var kinds = new Dictionary<MatchEndKind, int>();
            for (int seed = 1; seed <= 1000; seed++)
            {
                var setup = SeededSetup(seed);
                var e = new MatchEngine(setup, new SeededDiceRoller((ulong)seed));
                var r = MatchAutoplay.Run(e, true);
                Assert.IsTrue(e.IsOver, "seed " + seed);
                Assert.IsNotNull(r);
                Assert.LessOrEqual(r.Rounds, setup.RoundLimit, "seed " + seed);
                Assert.GreaterOrEqual(r.Rounds, 1);
                if (r.Winner == MatchWinner.Player) pw++; else if (r.Winner == MatchWinner.Rival) rw++; else draws++;
                rounds += r.Rounds;
                changes += e.State.InitiativeChanges;
                if (e.State.InitiativeChanges == 0)
                {
                    noChange++;
                    Assert.AreNotEqual(MatchEndKind.TimeLimit, r.Kind, "до ліміту без жодної зміни ініціативи — seed " + seed);
                }
                foreach (var ev in e.State.Log)
                {
                    if (ev.Kind == MatchEventKind.Kickout) { kickAll++; if (ev.Tier == RollTier.Partial) kickTwo++; }
                    if (ev.ChainIndex > longest) longest = ev.ChainIndex;
                }
                int n; kinds.TryGetValue(r.Kind, out n); kinds[r.Kind] = n + 1;
            }
            TestContext.WriteLine("1000 матчів ШІ-ШІ: гравець " + pw + ", суперник " + rw + ", нічиї " + draws
                                  + ", вирвалось на «два» за матч " + (kickTwo / 1000.0).ToString("F2") + " (усього вирвань " + (kickAll / 1000.0).ToString("F2") + ")"
                                  + ", середнє дій " + (rounds / 1000.0).ToString("F2")
                                  + ", змін ініціативи " + (changes / 1000.0).ToString("F2") + " (без жодної — " + noChange + ")"
                                  + ", найдовший ланцюг " + longest
                                  + "; кінці: " + string.Join(", ", kinds.Select(k => k.Key + "=" + k.Value).ToArray()));
            Assert.Greater(pw, 0);
            Assert.Greater(rw, 0);
            Assert.LessOrEqual(noChange, 50, "ініціатива переходить хоч раз майже в кожному матчі");
        }

        [Test]
        public void PassivePlayer_RivalAiStillEndsTheMatch()
        {
            for (int seed = 1; seed <= 50; seed++)
            {
                var setup = SeededSetup(seed);
                var e = new MatchEngine(setup, new SeededDiceRoller((ulong)seed));
                var r = MatchAutoplay.Run(e, false);
                Assert.IsNotNull(r);
                Assert.LessOrEqual(r.Rounds, setup.RoundLimit);
            }
        }

        [Test]
        public void SameSeed_SameLog_DifferentSeed_DifferentLog()
        {
            var a = new MatchEngine(SeededSetup(7), new SeededDiceRoller(7)); MatchAutoplay.Run(a, true);
            var b = new MatchEngine(SeededSetup(7), new SeededDiceRoller(7)); MatchAutoplay.Run(b, true);
            var c = new MatchEngine(SeededSetup(8), new SeededDiceRoller(8)); MatchAutoplay.Run(c, true);
            Assert.AreEqual(Fingerprint(a), Fingerprint(b));
            Assert.AreNotEqual(Fingerprint(a), Fingerprint(c));
        }

        [Test]
        public void Ai_UsesOnlyThePublicApi_ActionsItChoosesAreAlwaysAvailable()
        {
            for (int seed = 1; seed <= 200; seed++)
            {
                var e = new MatchEngine(SeededSetup(seed), new SeededDiceRoller((ulong)seed));
                int guard = 0;
                while (!e.IsOver && guard++ < 5000)
                {
                    if (e.Phase == MatchPhase.AwaitAction)
                    {
                        var id = MatchAi.ChooseAction(e);
                        if (id != MatchActionId.CatchBreath) Assert.IsTrue(e.Describe(id).Available, id + " seed " + seed);
                    }
                    MatchAi.Step(e);
                }
                Assert.IsTrue(e.IsOver);
            }
        }

        [Test]
        public void Ai_ReactionRule_ReverseOnFinisherOrHurt_ElseSell()
        {
            var e = MatchKit.Engine(true);
            e.Perform(MatchActionId.Throw);
            Assert.AreEqual(ReactionChoice.Sell, MatchAi.ChooseReaction(e));
            var h = MatchKit.Engine(true);
            h.State.Rival.Damage = 6;
            h.Perform(MatchActionId.Throw);
            Assert.AreEqual(ReactionChoice.Reverse, MatchAi.ChooseReaction(h));
            var f = MatchKit.Engine(true);
            f.State.Player.Momentum = 5; f.State.Hype = 4; f.State.Rival.Damage = 6;
            f.Perform(MatchActionId.Finisher);
            Assert.AreEqual(ReactionChoice.Reverse, MatchAi.ChooseReaction(f));
        }

        [Test]
        public void Ai_PriorityOrder_FinisherThenPinThenSubmission()
        {
            var e = MatchKit.Engine(false);
            e.State.Player.Posture = Posture.Down; e.State.Player.Damage = Math.Max(e.State.Player.Damage, MatchRules.PinMinDamage); MatchKit.NotFresh(e.State.Player); e.State.Player.Damage = 8;
            Assert.AreEqual(MatchActionId.Pin, MatchAi.ChooseAction(e));
            e.State.Player.Damage = 6;
            Assert.AreEqual(MatchActionId.Submission, MatchAi.ChooseAction(e));
            e.State.Rival.Momentum = 5; e.State.Hype = 4;
            Assert.AreEqual(MatchActionId.Finisher, MatchAi.ChooseAction(e));
            var g = MatchKit.Engine(false);
            g.State.Rival.Posture = Posture.Down; g.State.Rival.Damage = Math.Max(g.State.Rival.Damage, MatchRules.PinMinDamage);
            Assert.AreEqual(MatchActionId.GetUp, MatchAi.ChooseAction(g));
            var h = MatchKit.Engine(false);
            Assert.AreEqual(MatchActionId.Throw, MatchAi.ChooseAction(h));
            h.State.Rival.Stamina = MatchRules.Cost(MatchActionId.Throw);
            Assert.AreEqual(MatchActionId.Strike, MatchAi.ChooseAction(h), "без запасу «Сил» кидок не ризикує");
            h.State.Rival.Stamina = 0;
            Assert.AreEqual(MatchActionId.CatchBreath, MatchAi.ChooseAction(h), "видихся — переводить подих");
        }
    }
}
