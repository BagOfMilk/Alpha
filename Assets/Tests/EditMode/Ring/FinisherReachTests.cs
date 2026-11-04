using System;
using Game.Core.Randomness;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Core.Ring.Match;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Фінішер має бути досяжним у звичайному матчі кар'єри (власник 04.11.2026 на «бот жодного разу не зробив успішний
    /// фінішер»: «це погано»). Гравця веде той самий ШІ, що й суперника (він бере фінішер, щойно той доступний).
    /// </summary>
    public sealed class FinisherReachTests
    {
        private static void Measure(int matches, out int reached, out int landed, out int avgRounds)
        {
            reached = 0; landed = 0; int rounds = 0;
            for (int i = 0; i < matches; i++)
            {
                var s = CareerKit.NewHero((ulong)(1000 + i));
                s.EndWeek();
                var e = s.StartShow();
                bool seen = false, hit = false;
                int guard = 0;
                while (!e.IsOver && guard++ < 4000)
                {
                    if (e.Phase == MatchPhase.AwaitAction && e.CurrentActor == MatchSide.Player && e.Describe(MatchActionId.Finisher).Available) seen = true;
                    MatchAi.Step(e);
                }
                foreach (var ev in e.State.Log)
                    if (ev.Actor == MatchSide.Player && ev.Action == MatchActionId.Finisher && ev.Tier != RollTier.Miss
                        && (ev.Kind == MatchEventKind.Action || ev.Kind == MatchEventKind.Sell)) hit = true;
                if (seen) reached++;
                if (hit) landed++;
                rounds += e.State.Round;
            }
            avgRounds = rounds / Math.Max(1, matches);
        }

        [Test]
        public void Finisher_IsReachedInMostCareerMatches()
        {
            int reached, landed, rounds;
            Measure(300, out reached, out landed, out rounds);
            TestContext.WriteLine("фінішер доступний: " + reached + "/300, влучний: " + landed + "/300, раундів у середньому: " + rounds);
            // Замір 04.11.2026: поріг імпульсу 5 — 59/300 доступний; 3 — 198/300 доступний, 155/300 проведений; ≈5 раундів.
            Assert.GreaterOrEqual(reached, 180, "фінішер має ставати доступним хоча б у 60 % матчів");
            Assert.GreaterOrEqual(landed, 120, "хоча б у 40 % матчів гравець проводить фінішер");
            Assert.LessOrEqual(rounds, 6, "малий бій лишається коротким (Поправка №11: ≤ 5 раундів)");
        }
    }
}
