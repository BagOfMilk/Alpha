using System.Collections.Generic;
using Game.Core.Checks;
using Game.Core.Signals;

namespace Game.Core.Loop
{
    /// <summary>
    /// Последний содержательный шаг дня: превращает финальное состояние
    /// в то, что игрок увидит и услышит.
    /// </summary>
    public sealed class SignalStep : IDayStep
    {
        public int Order => DayStepOrder.Signals;

        public void Execute(DayContext ctx)
        {
            ctx.Signals = SignalComposer.Compose(
                ctx.Tension.Band,
                ctx.Tension.DayLedger,
                ctx.Tier,
                ctx.Balance.Signals,
                ctx.Forewarnings,
                ctx.IncidentOutcomes,
                BuildPostReports(ctx),
                ctx.IsNight);
        }

        /// <summary>
        /// Доклады с постов. Ночью город спит — докладывают только утром.
        /// Точность считается тем же резолвером, что и всё остальное.
        /// </summary>
        private static List<PostReport> BuildPostReports(DayContext ctx)
        {
            if (ctx.IsNight || ctx.PostDomains == null || ctx.PostDomains.Count == 0)
                return null;

            var reports = new List<PostReport>(ctx.PostDomains.Count);
            foreach (var domain in ctx.PostDomains)
            {
                var request = new CheckRequest(
                    domain.Skill,
                    domain.Threshold,
                    ApproachForm.Neutral,
                    "post:" + domain.PositionId,
                    domain.PositionId);

                // Повторы здесь не штрафуем: доклад — ежедневная рутина,
                // а не обращение к фракции.
                var preview = CheckResolver.Preview(request, ctx.Roster, null, ctx.Day, ctx.Balance);

                reports.Add(preview.HasCandidate
                    ? new PostReport(domain.PositionId, domain.DomainTag, preview.BestActorId,
                        preview.ExpectedBand, false)
                    : PostReport.Silent(domain.PositionId, domain.DomainTag));
            }
            return reports;
        }
    }
}
