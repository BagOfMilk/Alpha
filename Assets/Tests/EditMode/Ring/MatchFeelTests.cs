using System;
using System.Collections.Generic;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Core.Ring.Match;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Відчуття матчу в числах (аудит фану 06.11.2026, docs/ring/fun_audit_2026-11-06.md): на 1000 матчах кар'єри —
    /// ступені кидків гравця, раунди, near-fall «на два», серії провалів, дії до першого «попу» залу. Гравця й суперника
    /// веде той самий ШІ (як у <see cref="FinisherReachTests"/>). Межі — охоронці кривої напруги, а не баланс.
    /// </summary>
    public sealed class MatchFeelTests
    {
        public sealed class Feel
        {
            public int Matches, Miss, Partial, Strong, Rounds, NearFallMatches, PlayerNearFalls, RivalNearFalls,
                LongestMissStreak, SumMissStreakMax, FirstPopSum, FirstPopCount, NoPop, Wins, Losses, Draws, FinisherLanded,
                ComebackWins;
        }

        /// <summary>Поп — подія, після якої зал вибухає: вирвався «на два», фінішер, або ажіотаж +2 і більше за раз.</summary>
        private static bool IsPop(MatchEvent ev)
        {
            if (ev.Kind == MatchEventKind.Kickout && ev.Tier == RollTier.Partial) return true;
            if (ev.Action == MatchActionId.Finisher && ev.Tier != RollTier.Miss && ev.Kind == MatchEventKind.Action) return true;
            return ev.EffectSum(MatchEffectKind.Hype, MatchSide.Player) >= 2;
        }

        public static Feel Measure(int matches)
        {
            var f = new Feel { Matches = matches };
            for (int i = 0; i < matches; i++)
            {
                var s = CareerKit.NewHero((ulong)(5000 + i));
                s.EndWeek();
                var e = s.StartShow();
                int guard = 0;
                while (!e.IsOver && guard++ < 4000) MatchAi.Step(e);

                int streak = 0, maxStreak = 0, actions = 0, firstPop = -1;
                bool nearFall = false, wasBehind = false;
                foreach (var ev in e.State.Log)
                {
                    if (ev.Kind == MatchEventKind.Action && ev.Actor == MatchSide.Player && ev.HasRoll)
                    {
                        actions++;
                        if (ev.Tier == RollTier.Miss) { f.Miss++; streak++; maxStreak = Math.Max(maxStreak, streak); }
                        else { streak = 0; if (ev.Tier == RollTier.Partial) f.Partial++; else f.Strong++; }
                        if (ev.Action == MatchActionId.Finisher && ev.Tier != RollTier.Miss) f.FinisherLanded++;
                    }
                    if (ev.Kind == MatchEventKind.Kickout && ev.Tier == RollTier.Partial)
                    {
                        nearFall = true;
                        if (ev.Actor == MatchSide.Player) { f.PlayerNearFalls++; wasBehind = true; } else f.RivalNearFalls++;
                    }
                    if (firstPop < 0 && IsPop(ev)) firstPop = actions;
                }
                if (nearFall) f.NearFallMatches++;
                f.SumMissStreakMax += maxStreak;
                f.LongestMissStreak = Math.Max(f.LongestMissStreak, maxStreak);
                if (firstPop >= 0) { f.FirstPopSum += firstPop; f.FirstPopCount++; } else f.NoPop++;
                f.Rounds += e.State.Round;
                var r = e.Result;
                if (r.Winner == MatchWinner.Player) { f.Wins++; if (wasBehind) f.ComebackWins++; }
                else if (r.Winner == MatchWinner.Rival) f.Losses++;
                else f.Draws++;
            }
            return f;
        }

        public static string Report(Feel f)
        {
            int rolls = Math.Max(1, f.Miss + f.Partial + f.Strong);
            return string.Format(
                "матчів {0}; кидків гравця {1}: чистий {2:P0}, частково {3:P0}, провал {4:P0}; раундів у середньому {5:F1}; " +
                "near-fall «на два» у {6:P0} матчів (гравець вирвався {7}, суперник {8}); найдовша серія провалів {9}, " +
                "у середньому найдовша за матч {10:F1}; до першого попу {11:F1} дій гравця, без попу {12:P0} матчів; " +
                "перемог {13}, поразок {14}, нічиїх {15}; фінішер влучний {16}; перемог після «на два» гравця {17}",
                f.Matches, rolls, (double)f.Strong / rolls, (double)f.Partial / rolls, (double)f.Miss / rolls,
                (double)f.Rounds / f.Matches, (double)f.NearFallMatches / f.Matches, f.PlayerNearFalls, f.RivalNearFalls,
                f.LongestMissStreak, (double)f.SumMissStreakMax / f.Matches,
                f.FirstPopCount > 0 ? (double)f.FirstPopSum / f.FirstPopCount : -1, (double)f.NoPop / f.Matches,
                f.Wins, f.Losses, f.Draws, f.FinisherLanded, f.ComebackWins);
        }

        [Test]
        public void MatchFeel_Measure1000()
        {
            var f = Measure(1000);
            TestContext.WriteLine(Report(f));
            System.Console.WriteLine("[feel] " + Report(f));
            // Замір 06.11.2026 (поріг фінішера 3): чистий 39 %, частково 44 %, провал 17 %; 5,7 раунду; «на два» у 35 % матчів;
            // середня найдовша серія провалів 0,9; перший поп — після 3,8 дії, без попу 11 % матчів. Межі — з запасом.
            int rolls = f.Miss + f.Partial + f.Strong;
            Assert.GreaterOrEqual((double)f.Strong / rolls, 0.25, "чистий успіх має бути частим, а не рідкістю");
            Assert.GreaterOrEqual((double)f.NearFallMatches / f.Matches, 0.25, "«на два» — хоча б у чверті матчів (найгучніший момент вечора)");
            Assert.LessOrEqual((double)f.NoPop / f.Matches, 0.20, "матч без жодного попу залу — не частіше за кожен п'ятий");
            Assert.LessOrEqual((double)f.SumMissStreakMax / f.Matches, 1.5, "серії провалів поспіль — короткі");
            Assert.LessOrEqual((double)f.Rounds / f.Matches, 6.5, "малий бій лишається коротким (Поправка №11)");
        }
    }
}
