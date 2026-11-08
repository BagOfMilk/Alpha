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

        private static void AiPlay(MatchEngine e)
        {
            int guard = 0;
            while (!e.IsOver && guard++ < 4000) MatchAi.Step(e);
        }

        /// <summary>
        /// Перемоги гравця за тижнем сезону, на великому шоу ворожнечі й у титульному матчі (відповідь власника 06.11.2026 «Так,
        /// поміряй» на 75 % перемог у тижні 1). Кар'єру веде CareerBot (дії тижня, розвиток), кожен матч — однаковий ШІ за обидві
        /// сторони; титульний шанс береться завжди. Ключ — «s{сезон} w{тиждень}» і окремо «ворожнеча», «титул».
        /// </summary>
        public static SortedDictionary<string, int[]> WinRates(int careers, int seasons)
        {
            var d = new SortedDictionary<string, int[]>();
            Action<string, MatchWinner> add = (k, w) =>
            {
                int[] v;
                if (!d.TryGetValue(k, out v)) d[k] = v = new int[3];
                v[w == MatchWinner.Player ? 0 : w == MatchWinner.Rival ? 1 : 2]++;
            };
            for (int i = 0; i < careers; i++)
            {
                var s = CareerKit.NewHero((ulong)(9000 + i));
                var rep = new CareerRunReport();
                int guard = 0;
                string extra = null;
                while (s.State.Season <= seasons && guard++ < 3000)
                {
                    if (s.Stage == CareerStage.SeasonEnd)
                    {
                        if (s.CanStartTitleMatch) s.StartTitleMatch(); else s.StartNextSeason();
                        continue;
                    }
                    if (s.Stage != CareerStage.Show) { CareerBot.Step(s, i % 8, rep); continue; }
                    int season = s.State.Season, week = s.State.Week;
                    switch (s.State.Phase)
                    {
                        case ShowPhase.Card:
                        {
                            int[] st;
                            string sk = "~щаблі s" + season + (s.State.IsTitle ? " титул" : s.State.IsFeudShow ? " ворожнеча" : "");
                            if (!d.TryGetValue(sk, out st)) d[sk] = st = new int[4];
                            st[0] += s.RivalStepsNow(); st[1] += s.PlayerGrowth(); st[2]++; st[3] += s.State.IsInjured ? 100 : 0;
                            // Пік — щотижневий матч, де суперник слабший за поточний ріст гравця хоча б на щабель (виріс між підвищеннями).
                            extra = null;
                            if (!s.State.IsTitle && !s.State.IsFeudShow)
                            {
                                if (s.IsRematch) extra = "реванш";
                                else
                                {
                                    int lag = CareerRules.RivalSteps(s.State.Position, s.PlayerGrowth()) - s.RivalStepsNow();
                                    if (lag >= 2) extra = "пік 2+"; else if (lag == 1) extra = "пік 1";
                                }
                            }
                            s.StartShow();
                            break;
                        }
                        case ShowPhase.Match:
                        {
                            AiPlay(s.Engine);
                            var r = s.FinishMatch();
                            add(r.IsTitle ? "титул" : "s" + season + " w" + week, r.Winner);
                            if (extra != null) add(extra, r.Winner);
                            break;
                        }
                        case ShowPhase.Feud:
                            if (s.Feud.Stage == RingStage.MatchPrep)
                            {
                                AiPlay(s.StartFeudMatch());
                                var r = s.FinishMatch();
                                add("ворожнеча s" + season, r.Winner);
                            }
                            else CareerBot.Step(s, i % 8, rep);
                            break;
                        case ShowPhase.Result: s.ContinueFromShow(); break;
                    }
                }
            }
            return d;
        }

        [Test]
        public void WinRates_ByWeek_FeudAndTitle()
        {
            var d = WinRates(300, 3);
            var sb = new System.Text.StringBuilder();
            foreach (var kv in d)
            {
                if (kv.Key.StartsWith("~"))
                {
                    sb.AppendFormat("{0}: щаблів {1:F1}, ріст {2:F1}, травмований {3:F0} %; ", kv.Key, (double)kv.Value[0] / kv.Value[2], (double)kv.Value[1] / kv.Value[2], (double)kv.Value[3] / kv.Value[2]);
                    continue;
                }
                int n = kv.Value[0] + kv.Value[1] + kv.Value[2];
                sb.AppendFormat("{0}: перемог {1:P0} ({2}/{3}); ", kv.Key, (double)kv.Value[0] / n, kv.Value[0], n);
            }
            TestContext.WriteLine(sb.ToString());
            System.Console.WriteLine("[wins] " + sb);
            // Сила суперника росте разом із гравцем (власник 06.11.2026: «1: за місцем у картці»; CareerRules «сила суперника»).
            // Замір 06.11.2026 (мейн-івент +2, поріг росту 21): щотижневі s1 61–73 %, s2 52–54 %, s3 56–65 %; Молот 50 / 60 / 57 %; титул 41 %.
            // Без цього було: s3 91–95 %, Молот 60 → 89 → 98 %, титул 82 %. Межі — із запасом на шум 300 кар'єр.
            for (int season = 1; season <= 3; season++)
            {
                int w = 0, n = 0;
                foreach (var kv in d)
                    if (kv.Key.StartsWith("s" + season + " w")) { w += kv.Value[0]; n += kv.Value[0] + kv.Value[1] + kv.Value[2]; }
                double rate = (double)w / n;
                Assert.That(rate, Is.InRange(0.45, 0.75), "щотижневі матчі сезону " + season + ": " + rate.ToString("P0"));
                var f = d["ворожнеча s" + season];
                double fr = (double)f[0] / (f[0] + f[1] + f[2]);
                Assert.That(fr, Is.InRange(0.38, 0.68), "ворожнеча з Молотом, сезон " + season + ": " + fr.ToString("P0"));
            }
            // Піки сили (власник 06.11.2026: «Піки 70–80%»): реванш і щотижневий матч після прокачки (суперник відстає на 2+
            // щаблі) — 70–80 %. Замір 06.11.2026: реванш 73 %, пік 2+ 71 %. Межі — із запасом на шум.
            foreach (var key in new[] { "реванш", "пік 2+" })
            {
                var pk = d[key];
                double pr = (double)pk[0] / (pk[0] + pk[1] + pk[2]);
                Assert.That(pr, Is.InRange(0.64, 0.86), key + ": " + pr.ToString("P0"));
            }
            var t = d["титул"];
            double tr = (double)t[0] / (t[0] + t[1] + t[2]);
            Assert.That(tr, Is.InRange(0.30, 0.52), "титульний матч: " + tr.ToString("P0"));
        }

        [Test]
        public void MatchFeel_Measure1000()
        {
            var f = Measure(1000);
            TestContext.WriteLine(Report(f));
            System.Console.WriteLine("[feel] " + Report(f));
            // Замір 06.11.2026 (поріг фінішера 3): чистий 39 %, частково 44 %, провал 17 %; 5,7 раунду; «на два» у 35 % матчів;
            // середня найдовша серія провалів 0,9; перший поп — після 3,8 дії, без попу 11 % матчів. Межі — з запасом.
            // Замір 08.11.2026 (ланцюг ініціативи, «свіжий борець», докрутка гравця до 10+): чистий 27 %, частково 42 %, провал 30 %;
            // 15,0 дії; «на два» у 56 % матчів; найдовша серія провалів 1,3; без попу 2 %.
            int rolls = f.Miss + f.Partial + f.Strong;
            Assert.GreaterOrEqual((double)f.Strong / rolls, 0.25, "чистий успіх має бути частим, а не рідкістю");
            Assert.GreaterOrEqual((double)f.NearFallMatches / f.Matches, 0.25, "«на два» — хоча б у чверті матчів (найгучніший момент вечора)");
            Assert.LessOrEqual((double)f.NoPop / f.Matches, 0.20, "матч без жодного попу залу — не частіше за кожен п'ятий");
            Assert.LessOrEqual((double)f.SumMissStreakMax / f.Matches, 1.5, "серії провалів поспіль — короткі");
            // З 08.11.2026 матч рахує дії ланцюга ініціативи, а не раунди: критерій М1 — 8–15 дій (P2-1 проходу 08.11, Поправка №11).
            double actions = (double)f.Rounds / f.Matches;
            Assert.That(actions, Is.InRange(8.0, 15.0), "матч — 8–15 дій: не розгром і не тягучка");
        }
    }
}
